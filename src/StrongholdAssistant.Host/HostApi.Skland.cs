using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ASPAssistant.Core.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private static SklandException SklandReadError(string message, Exception ex)
    {
        Trace.WriteLine($"{message} {ex}");
        // WebView/IO 异常可能带路径或请求资料，只返回类型和错误码。
        return new SklandException($"{message}\n{ex.GetType().Name}: HRESULT 0x{ex.HResult:X8}");
    }

    private Action? _cancelSklandGuide;
    private Window? _sklandHistoryWindow;
    private Task<SklandRecord[]?>? _sklandHistory;
    private Task<SklandRecord[]?>? _silentSklandHistory;

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
        url = SklandParser.NormalizeGuideUrl(url);
        _cancelSklandGuide?.Invoke();
        var completion = new TaskCompletionSource<SklandGuide>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action cancel = () => completion.TrySetException(new SklandException("已取消上一次攻略读取。"));
        _cancelSklandGuide = cancel;
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
                ConfigureSkland(core, false);
                core.NavigationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess) completion.TrySetException(new SklandException(
                        $"打不开森空岛，检查一下网络再试。\nWebErrorStatus: {args.WebErrorStatus}"));
                };
                core.WebResourceResponseReceived += async (_, args) =>
                {
                    if (closed || !IsSklandResponse(args.Request.Uri, "info")) return;
                    if (args.Request.Method != "GET" || args.Response.StatusCode != 200) return;
                    try
                    {
                        using var stream = await args.Response.GetContentAsync();
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
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
            if (_cancelSklandGuide == cancel) _cancelSklandGuide = null;
        }
    }

    public Task<SklandRecord[]?> SklandHistoryAsync(bool silent = false)
    {
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
        try
        {
            var records = await ReadSklandHistorySessionAsync(false);
            return records ?? await ReadSklandHistorySessionAsync(true);
        }
        finally { _sklandHistory = null; }
    }

    private async Task<SklandRecord[]?> ReadSklandHistorySessionAsync(bool visible, bool silent = false)
    {
        const string editUrl = "https://game.skland.com/arknights/stronghold-guide/edit?header=0";
        var completion = new TaskCompletionSource<SklandRecord[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                core.NavigationStarting += (_, _) =>
                {
                    navigation?.Cancel();
                    historyResponseReceived = false;
                };
                core.NavigationCompleted += async (_, args) =>
                {
                    if (closed) return;
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
                        var deadline = DateTime.UtcNow.AddSeconds(15);
                        while (DateTime.UtcNow < deadline)
                        {
                            token.ThrowIfCancellationRequested();
                            var result = await core.ExecuteScriptAsync("""
                                (() => {
                                  const button = [...document.querySelectorAll('button')]
                                    .find(b => b.textContent.trim() === '导入历史战绩' && !b.disabled && b.getClientRects().length);
                                  return !!button;
                                })()
                                """);
                            token.ThrowIfCancellationRequested();
                            if (result == "true")
                            {
                                // 按钮先于绑定角色加载完成时，留出页面查询角色的时间。
                                await Task.Delay(TimeSpan.FromSeconds(1.5), token);
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
                                var responseDeadline = DateTime.UtcNow.AddSeconds(8);
                                while (DateTime.UtcNow < responseDeadline)
                                {
                                    await Task.Delay(500, token);
                                    var missingRole = await core.ExecuteScriptAsync("""
                                        document.body.innerText.includes('未查询到游戏角色')
                                        """);
                                    token.ThrowIfCancellationRequested();
                                    if (missingRole != "true" || historyResponseReceived) continue;
                                    if (retries < (visible ? 2 : 1))
                                    {
                                        retries++;
                                        // 静默重试重新计时，避免原来的 8 秒时限截断刷新后的读取。
                                        if (silent) silentTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                                        core.Reload();
                                    }
                                    else if (!visible) completion.TrySetResult(null);
                                    return;
                                }
                                if (!visible) completion.TrySetResult(null);
                                return;
                            }
                            await Task.Delay(500, token);
                        }
                        completion.TrySetException(new SklandException(
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
                    if (closed || !IsSklandResponse(args.Request.Uri, "history-log")) return;
                    if (args.Request.Method != "GET") return;
                    historyResponseReceived = true;
                    if (args.Response.StatusCode != 200) return;
                    try
                    {
                        using var stream = await args.Response.GetContentAsync();
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
                        if (closed) return;
                        var records = SklandParser.ParseHistory(json);
                        if (records is not null || !visible) completion.TrySetResult(records);
                    }
                    catch (ArgumentException ex) { completion.TrySetException(ex); }
                    catch (Exception ex)
                    {
                        completion.TrySetException(SklandReadError("历史战绩的数据结构已变化，暂时无法读取。", ex));
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
            if (silent) silentTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            if (visible)
            {
                window.Loaded += async (_, _) => await InitializeAsync();
                window.Show();
            }
            else _ = InitializeAsync();
            return await completion.Task;
        }
        finally
        {
            closed = true;
            lifetime.Cancel();
            navigation?.Dispose();
            navigation = null;
            controller?.Close();
            window.Close();
        }
    }
}
