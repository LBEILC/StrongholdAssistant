using System.Text.Json;
using ASPAssistant.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ASPAssistant.Core.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    // 页面 entry_index-DqRhmnpR.js 的 fetchGameBinding：data.list 按 appCode 映射，再读取 bindingList。
    private const string SklandBindingPath = "/api/v1/game/player/binding";

    // null 表示接口失败或结构不符，不能当成账号未绑定。
    internal static bool? SklandHasRole(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("code", out var code) || !code.TryGetInt32(out var value) || value != 0
            || !root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        foreach (var game in list.EnumerateArray())
        {
            if (!game.TryGetProperty("appCode", out var app) || app.GetString() != "arknights") continue;
            if (!game.TryGetProperty("bindingList", out var bindings) || bindings.ValueKind != JsonValueKind.Array) return null;
            return bindings.GetArrayLength() > 0;
        }
        return false;
    }

    internal static string SklandResponseSummary(string method, string url, int status, string? json)
    {
        var summary = $"{method} {new Uri(url).AbsolutePath} HTTP {status}";
        if (json is null) return summary;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            // 自由文本 message 可能回显凭据；只记录数字业务码，不记录任何字符串值。
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.Number && code.TryGetInt64(out var number))
                summary += $" code={number}";
            void Lists(JsonElement element, string path)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    summary += $" {path}={element.GetArrayLength()}";
                    foreach (var item in element.EnumerateArray()) Lists(item, path + "[]");
                }
                else if (element.ValueKind == JsonValueKind.Object)
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Equals("cred", StringComparison.OrdinalIgnoreCase)
                            || property.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                            || property.Name.Contains("cookie", StringComparison.OrdinalIgnoreCase)) continue;
                        // 字段名也来自网络，只允许普通标识符。
                        if (property.Name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                            Lists(property.Value, path + "." + property.Name);
                    }
            }
            Lists(root, "$");
        }
        catch (JsonException) { summary += " JSON=invalid"; }
        return summary;
    }

    private static SklandException SklandReadError(string message, Exception ex)
    {
        AppLogger.Info("Skland", $"读取失败 {ex.GetType().Name} HRESULT 0x{ex.HResult:X8}");
        // WebView/IO 异常可能带路径或请求资料，只返回类型和错误码。
        return new SklandException($"{message}\n{ex.GetType().Name}: HRESULT 0x{ex.HResult:X8}");
    }

    private Action? _cancelSklandGuide;
    private Window? _sklandHistoryWindow;
    private Task<SklandRecord[]?>? _sklandHistory;
    private Task<SklandRecord[]?>? _silentSklandHistory;
    private int _sklandLogoutGeneration;
    private readonly List<SklandSession> _sklandSessions = [];

    private sealed class SklandSession(Action cancel)
    {
        public TaskCompletionSource<CoreWebView2?> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action Cancel { get; } = cancel;
    }

    private string SklandAccountPath => Path.Combine(DataDirectory, "skland-account.json");

    private SklandAccount? ReadSklandAccount() => File.Exists(SklandAccountPath)
        ? JsonSerializer.Deserialize<SklandAccount>(File.ReadAllText(SklandAccountPath), JsonOptions) : null;

    private void RememberSklandAccount(string json)
    {
        var account = SklandParser.ParseAccount(json);
        if (account is null) File.Delete(SklandAccountPath);
        else
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(SklandAccountPath, JsonSerializer.Serialize(account, JsonOptions));
        }
    }

    private void FinishSklandSession(SklandSession session)
    {
        session.Ready.TrySetResult(null);
        _sklandSessions.Remove(session);
        session.Closed.TrySetResult();
    }

    private async Task SklandLogoutAsync()
    {
        _sklandLogoutGeneration++;
        if (_sklandGuidesQueue is not null) await _sklandGuidesQueue.CloseAsync();
        var sessions = _sklandSessions.ToArray();
        try
        {
            var cleared = false;
            foreach (var session in sessions)
            {
                var core = await session.Ready.Task;
                if (cleared || session.Closed.Task.IsCompleted || core is null) continue;
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
                cleared = true;
            }
            if (!cleared)
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(Path.Combine(DataDirectory, "skland-cleanup-pending"), "");
                CleanupSklandProfile();
                if (File.Exists(Path.Combine(DataDirectory, "skland-cleanup-pending")))
                {
                    // 旧浏览器进程尚占用目录时先清站点数据，保证立即换账号不会复用凭据。
                    using var source = new HwndSource(new HwndSourceParameters("SklandLogout")
                    { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
                    var environment = await SklandEnvironmentAsync();
                    var controller = await environment.CreateCoreWebView2ControllerAsync(source.Handle);
                    try { await controller.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile); }
                    finally { controller.Close(); }
                }
            }
            File.Delete(Path.Combine(DataDirectory, "skland-cleanup-pending"));
        }
        finally
        {
            foreach (var session in sessions) session.Cancel();
            await Task.WhenAll(sessions.Select(s => s.Closed.Task));
            File.Delete(SklandAccountPath);
        }
        // 等外层读取任务完成，下一次调用不会拿到旧任务，也不会从后台读取跳转到弹窗。
        if (_sklandHistory is { } history) await history;
        if (_silentSklandHistory is { } silent) await silent;
    }

    private void CleanupSklandProfile()
    {
        var marker = Path.Combine(DataDirectory, "skland-cleanup-pending");
        if (!File.Exists(marker)) return;
        try
        {
            var directory = Path.Combine(DataDirectory, "SklandWebView2");
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            File.Delete(marker);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Task<CoreWebView2Environment> SklandEnvironmentAsync() =>
        CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(DataDirectory, "SklandWebView2"));

    private static bool SklandNavigationAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        return uri.Host == "game.skland.com";
    }

    private static bool IsSklandResponse(string url, string endpoint) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.Host == "zonai.skland.com" && uri.IsDefaultPort
        && uri.AbsolutePath == "/web/v1/game/garrison/guide/" + endpoint;

    private static void ConfigureSkland(CoreWebView2 core, bool history)
    {
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.NavigationStarting += (_, args) => args.Cancel = !SklandNavigationAllowed(args.Uri);
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (history && SklandNavigationAllowed(args.Uri)) core.Navigate(args.Uri);
        };
    }

    public async Task<SklandGuide> SklandGuideAsync(string url)
    {
        if (_sklandLoginActive) throw new SklandException("森空岛窗口已经开着");
        url = SklandParser.NormalizeGuideUrl(url);
        _cancelSklandGuide?.Invoke();
        var completion = new TaskCompletionSource<SklandGuide>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action cancel = () => completion.TrySetException(new SklandException("已取消上一次攻略读取。"));
        _cancelSklandGuide = cancel;
        var session = new SklandSession(cancel);
        _sklandSessions.Add(session);
        var logoutGeneration = _sklandLogoutGeneration;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var registration = timeout.Token.Register(() => completion.TrySetException(new SklandException(
            "20 秒内没读到攻略内容，可能是森空岛页面改版了，或者这篇攻略已被删除。\nTimeoutException: 20 秒内未收到攻略内容。")));
        // 没有 WS_VISIBLE 的原生父窗口，为后台 WebView 提供视口，不显示或抢占焦点。
        using var source = new HwndSource(new HwndSourceParameters("SklandGuide")
        { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
        CoreWebView2Controller? controller = null;
        var closed = false;
        async Task InitializeAsync()
        {
            try
            {
                var environment = await SklandEnvironmentAsync();
                if (closed) return;
                var created = await environment.CreateCoreWebView2ControllerAsync(source.Handle);
                if (closed) { created.Close(); return; }
                controller = created;
                controller.Bounds = new System.Drawing.Rectangle(0, 0, 800, 600);
                controller.IsVisible = true;
                var core = controller.CoreWebView2;
                session.Ready.TrySetResult(core);
                ConfigureSkland(core, false);
                core.NavigationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess) completion.TrySetException(new SklandException(
                        $"打不开森空岛，检查一下网络再试。\nWebErrorStatus: {args.WebErrorStatus}"));
                };
                core.WebResourceResponseReceived += async (_, args) =>
                {
                    var isBinding = new Uri(args.Request.Uri) is { Host: "zonai.skland.com", AbsolutePath: SklandBindingPath };
                    if (closed || logoutGeneration != _sklandLogoutGeneration || (!isBinding && !IsSklandResponse(args.Request.Uri, "info"))) return;
                    if (isBinding && args.Response.StatusCode == 401) { File.Delete(SklandAccountPath); return; }
                    if (args.Request.Method != "GET" || args.Response.StatusCode != 200) return;
                    try
                    {
                        using var stream = await args.Response.GetContentAsync();
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
                        if (closed || logoutGeneration != _sklandLogoutGeneration) return;
                        if (isBinding) { RememberSklandAccount(json); return; }
                        if (!closed) completion.TrySetResult(SklandParser.ParseGuide(json, url));
                    }
                    catch (ArgumentException ex) { completion.TrySetException(ex); }
                    catch (Exception ex)
                    {
                        completion.TrySetException(SklandReadError("森空岛攻略的数据结构已变化，暂时无法读取。", ex));
                    }
                };
                core.Navigate(url);
            }
            catch (Exception ex)
            {
                if (!closed) completion.TrySetException(SklandReadError("打不开森空岛，检查一下网络再试。", ex));
            }
        }
        _ = InitializeAsync();
        try { return await completion.Task; }
        finally
        {
            closed = true;
            controller?.Close();
            FinishSklandSession(session);
            if (_cancelSklandGuide == cancel) _cancelSklandGuide = null;
        }
    }

    public Task<SklandRecord[]?> SklandHistoryAsync(bool silent = false)
    {
        if (_sklandLoginActive) throw new SklandException("森空岛窗口已经开着");
        if (silent) return _silentSklandHistory ??= ReadSilentSklandHistoryAsync();
        if (_sklandHistoryWindow is not null)
        {
            if (_sklandHistoryWindow.WindowState == WindowState.Minimized) _sklandHistoryWindow.WindowState = WindowState.Normal;
            _sklandHistoryWindow.Activate();
        }
        return _sklandHistory ??= ReadSklandHistoryAsync();
    }

    private async Task<SklandRecord[]?> ReadSilentSklandHistoryAsync()
    {
        try { return await ReadSklandHistorySessionAsync(false, true); }
        finally { _silentSklandHistory = null; }
    }

    private async Task<SklandRecord[]?> ReadSklandHistoryAsync()
    {
        var logoutGeneration = _sklandLogoutGeneration;
        try
        {
            var records = await ReadSklandHistorySessionAsync(false);
            return records ?? (logoutGeneration == _sklandLogoutGeneration ? await ReadSklandHistorySessionAsync(true) : null);
        }
        finally { _sklandHistory = null; }
    }

    private async Task<SklandRecord[]?> ReadSklandHistorySessionAsync(bool visible, bool silent = false)
    {
        const string editUrl = "https://game.skland.com/arknights/stronghold-guide/edit?header=0";
        var completion = new TaskCompletionSource<SklandRecord[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new SklandSession(() => completion.TrySetResult(null));
        _sklandSessions.Add(session);
        var logoutGeneration = _sklandLogoutGeneration;
        var browser = new WebView2();
        var panel = new DockPanel();
        var hint = new TextBlock
        {
            Text = "没登录的话先在下面登录；登录后点页面上的『导入历史战绩』。",
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(hint);
        panel.Children.Add(browser);
        var window = new Window
        {
            Owner = DialogOwner, Title = "森空岛 · 导入历史战绩", Width = 480, Height = 720,
            Content = panel, WindowStartupLocation = WindowStartupLocation.Manual
        };
        var owner = DialogOwner!;
        var dpi = User32.GetWindowDpiScale(new WindowInteropHelper(owner).Handle);
        var work = User32.GetMonitorGeometry((int)((owner.Left + owner.ActualWidth / 2) * dpi),
            (int)((owner.Top + owner.ActualHeight / 2) * dpi)).WorkArea;
        var area = new Rect(work.Left / dpi, work.Top / dpi, work.Width / dpi, work.Height / dpi);
        var right = owner.Left + owner.ActualWidth;
        window.Left = right + window.Width <= area.Right ? right : Math.Max(area.Left, owner.Left - window.Width);
        window.Top = Math.Max(area.Top, Math.Min(owner.Top + (owner.ActualHeight - window.Height) / 2, area.Bottom - window.Height));
        if (visible) _sklandHistoryWindow = window;
        var closed = false;
        using var lifetime = new CancellationTokenSource();
        using var silentTimeout = new CancellationTokenSource();
        using var silentTimeoutRegistration = silentTimeout.Token.Register(() => completion.TrySetResult(null));
        var retries = 0;
        var historyResponseReceived = false;
        bool? hasRole = null;
        var roleDetails = $"GET {SklandBindingPath} HTTP 未收到 code=未知";
        var generation = 0;
        var apiRequests = new HashSet<string>();
        AppLogger.Info("Skland", $"开始读取 visible={visible} silent={silent}");
        using var source = visible ? null : new HwndSource(new HwndSourceParameters("SklandHistory")
        { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
        CoreWebView2Controller? controller = null;
        CancellationTokenSource? navigation = null;
        window.Closed += (_, _) =>
        {
            closed = true;
            if (_sklandHistoryWindow == window) _sklandHistoryWindow = null;
            lifetime.Cancel();
            browser.Dispose();
            completion.TrySetResult(null);
        };
        async Task InitializeAsync()
        {
            try
            {
                var environment = await SklandEnvironmentAsync();
                if (closed) return;
                CoreWebView2 core;
                if (visible)
                {
                    await browser.EnsureCoreWebView2Async(environment);
                    if (closed) return;
                    core = browser.CoreWebView2;
                }
                else
                {
                    controller = await environment.CreateCoreWebView2ControllerAsync(source!.Handle);
                    if (closed) { controller.Close(); return; }
                    controller.Bounds = new System.Drawing.Rectangle(0, 0, 800, 600);
                    controller.IsVisible = true;
                    core = controller.CoreWebView2;
                }
                ConfigureSkland(core, true);
                session.Ready.TrySetResult(core);
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
                core.WebResourceRequested += (_, args) =>
                {
                    var uri = new Uri(args.Request.Uri);
                    if (uri.Scheme == "https" && uri.Host.EndsWith(".skland.com", StringComparison.OrdinalIgnoreCase))
                        apiRequests.Add(args.Request.Uri);
                };
                core.NavigationStarting += (_, _) =>
                {
                    navigation?.Cancel();
                    historyResponseReceived = false;
                    hasRole = null;
                    roleDetails = $"GET {SklandBindingPath} HTTP 未收到 code=未知";
                    generation++;
                    apiRequests.Clear();
                };
                core.NavigationCompleted += async (_, args) =>
                {
                    if (closed) return;
                    AppLogger.Info("Skland", $"导航完成 {new Uri(core.Source).AbsolutePath} success={args.IsSuccess}");
                    if (!args.IsSuccess)
                    {
                        completion.TrySetException(new SklandException(
                            $"打不开森空岛，检查一下网络再试。\nWebErrorStatus: {args.WebErrorStatus}"));
                        return;
                    }
                    if (new Uri(core.Source).AbsolutePath != "/arknights/stronghold-guide/edit")
                    {
                        if (visible) core.Navigate(editUrl);
                        return;
                    }
                    navigation?.Dispose();
                    navigation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    var token = navigation.Token;
                    try
                    {
                        var waitingSince = DateTime.UtcNow;
                        var buttonLogged = false;
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            var result = await core.ExecuteScriptAsync("""
                                (() => {
                                  const button = [...document.querySelectorAll('button')]
                                    .find(b => b.textContent.trim() === '导入历史战绩' && !b.disabled && b.getClientRects().length);
                                  const login = [...document.querySelectorAll('input')].some(i =>
                                    i.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true }) &&
                                    (i.type === 'password' || i.type === 'tel' || /手机号/.test(i.placeholder + i.parentElement.textContent)));
                                  return { button: !!button, login };
                                })()
                                """);
                            token.ThrowIfCancellationRequested();
                            using var state = JsonDocument.Parse(result);
                            var loginForm = state.RootElement.GetProperty("login").GetBoolean();
                            if (loginForm && logoutGeneration == _sklandLogoutGeneration) File.Delete(SklandAccountPath);
                            if (!visible && loginForm) { completion.TrySetResult(null); return; }
                            if (SklandParser.HistoryWaitExpired(visible, loginForm, DateTime.UtcNow, ref waitingSince)) break;
                            if (state.RootElement.GetProperty("button").GetBoolean() && !loginForm)
                            {
                                if (!buttonLogged)
                                {
                                    AppLogger.Info("Skland", "找到导入历史战绩按钮");
                                    buttonLogged = true;
                                }
                                if (hasRole != true)
                                {
                                    await Task.Delay(500, token);
                                    continue;
                                }
                                result = await core.ExecuteScriptAsync("""
                                    (() => {
                                      const button = [...document.querySelectorAll('button')]
                                        .find(b => b.textContent.trim() === '导入历史战绩' && !b.disabled && b.getClientRects().length);
                                      if (!button) return false;
                                      button.click();
                                      return true;
                                    })()
                                    """);
                                token.ThrowIfCancellationRequested();
                                if (result != "true") continue;
                                AppLogger.Info("Skland", "点击导入历史战绩按钮");
                                var responseDeadline = DateTime.UtcNow.AddSeconds(8);
                                while (DateTime.UtcNow < responseDeadline)
                                {
                                    await Task.Delay(500, token);
                                    var missingRole = await core.ExecuteScriptAsync("""
                                        document.body.innerText.includes('未查询到游戏角色')
                                        """);
                                    token.ThrowIfCancellationRequested();
                                    if (missingRole != "true" || historyResponseReceived) continue;
                                    AppLogger.Info("Skland", "看到未查询到游戏角色提示");
                                    if (retries < (visible ? 2 : 1))
                                    {
                                        retries++;
                                        AppLogger.Info("Skland", $"重试 {retries}/{(visible ? 2 : 1)}; {roleDetails}");
                                        // 覆盖角色等待（15 秒）和战绩等待（8 秒），重试时重新计时。
                                        if (silent) silentTimeout.CancelAfter(TimeSpan.FromSeconds(25));
                                        core.Reload();
                                    }
                                    else if (!visible) completion.TrySetResult(null);
                                    else completion.TrySetException(new SklandException(
                                        "森空岛没查到你的游戏角色。\n" + roleDetails));
                                    return;
                                }
                                if (!visible) completion.TrySetResult(null);
                                return;
                            }
                            await Task.Delay(500, token);
                        }
                        if (buttonLogged)
                        {
                            if (visible) completion.TrySetException(new SklandException(
                                "森空岛没查到你的游戏角色。\n" + roleDetails));
                            else completion.TrySetResult(null);
                        }
                        else completion.TrySetException(new SklandException(
                            "打不开森空岛，检查一下网络再试。\nTimeoutException: 15 秒内未找到历史战绩导入按钮。"));
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                            completion.TrySetException(SklandReadError("打不开森空岛，检查一下网络再试。", ex));
                    }
                };
                core.WebResourceResponseReceived += async (_, args) =>
                {
                    if (closed || args.Request.Method == "OPTIONS") return;
                    var uri = new Uri(args.Request.Uri);
                    var isHistory = IsSklandResponse(args.Request.Uri, "history-log") && args.Request.Method == "GET";
                    var isBinding = uri.Host == "zonai.skland.com" && uri.AbsolutePath == SklandBindingPath
                        && args.Request.Method == "GET";
                    if (!apiRequests.Contains(args.Request.Uri) && !isHistory && !isBinding) return;
                    var responseGeneration = generation;
                    if (isHistory)
                    {
                        historyResponseReceived = true;
                        AppLogger.Info("Skland", $"收到 history-log 响应 HTTP {args.Response.StatusCode}");
                    }
                    try
                    {
                        using var stream = await args.Response.GetContentAsync();
                        using var reader = stream is null ? null : new StreamReader(stream);
                        var json = reader is null ? null : await reader.ReadToEndAsync();
                        var detail = SklandResponseSummary(args.Request.Method, args.Request.Uri, args.Response.StatusCode, json);
                        AppLogger.Info("Skland", "API " + detail);
                        if (closed || responseGeneration != generation || logoutGeneration != _sklandLogoutGeneration) return;
                        if (isBinding)
                        {
                            roleDetails = detail;
                            if (args.Response.StatusCode == 401) File.Delete(SklandAccountPath);
                            else if (json is not null) RememberSklandAccount(json);
                            hasRole = args.Response.StatusCode == 200 && json is not null ? SklandHasRole(json) : null;
                            if (hasRole == false) completion.TrySetException(new SklandException(
                                "森空岛账号还没有绑定明日方舟角色，先在森空岛 App 里绑定"));
                        }
                        if (!isHistory || args.Response.StatusCode != 200 || json is null) return;
                        var records = SklandParser.ParseHistory(json);
                        if (records is not null || !visible) completion.TrySetResult(records);
                    }
                    catch (ArgumentException ex) { completion.TrySetException(ex); }
                    catch (Exception ex)
                    {
                        AppLogger.Info("Skland", $"API {SklandResponseSummary(args.Request.Method, args.Request.Uri, args.Response.StatusCode, null)} 读取失败 {ex.GetType().Name}");
                        if (isHistory) completion.TrySetException(SklandReadError("历史战绩的数据结构已变化，暂时无法读取。", ex));
                    }

                };
                core.Navigate(editUrl);
            }
            catch (Exception ex)
            {
                if (closed) return;
                completion.TrySetException(SklandReadError("打不开森空岛，检查一下网络再试。", ex));
            }
        }
        try
        {
            if (silent) silentTimeout.CancelAfter(TimeSpan.FromSeconds(25));
            if (visible)
            {
                window.Loaded += async (_, _) => await InitializeAsync();
                window.Show();
            }
            else _ = InitializeAsync();
            var records = await completion.Task;
            AppLogger.Info("Skland", records is null ? "最终结果：未读取到战绩或已取消" : $"最终结果：成功，战绩数量={records.Length}");
            return records;
        }
        catch (Exception ex)
        {
            AppLogger.Info("Skland", $"最终结果：失败 {ex.GetType().Name}; {roleDetails}");
            throw;
        }
        finally
        {
            closed = true;
            lifetime.Cancel();
            navigation?.Dispose();
            navigation = null;
            controller?.Close();
            window.Close();
            FinishSklandSession(session);
        }
    }
}
