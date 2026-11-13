using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private bool _sklandLoginActive;
    // 测试替换浏览器边界，登录流程、消息桥与昵称存储仍执行真实代码。
    internal Func<bool, Task<string?>>? SklandLoginSessionReader { get; set; }

    public async Task<SklandAccount?> SklandLoginAsync()
    {
        if (_sklandLoginActive || _sklandSessions.Count > 0 || _sklandHistory is not null || _silentSklandHistory is not null)
            throw new SklandException("森空岛窗口已经开着");
        _sklandLoginActive = true;
        var logoutGeneration = _sklandLogoutGeneration;
        try
        {
            if (_sklandGuidesQueue is not null) await _sklandGuidesQueue.CloseAsync();
            var read = SklandLoginSessionReader ?? ReadSklandLoginSessionAsync;
            var json = await read(false);
            if (logoutGeneration != _sklandLogoutGeneration) return null;
            if (json is null) json = await read(true);
            if (json is null || logoutGeneration != _sklandLogoutGeneration) return null;
            var account = SklandParser.ParseAccount(json);
            if (account is not null) RememberSklandAccount(json);
            return account;
        }
        finally { _sklandLoginActive = false; }
    }

    private async Task<string?> ReadSklandLoginSessionAsync(bool visible)
    {
        // 复用已用于登录的编辑页，只监听角色绑定，不点击导入、保存或发布。
        const string loginUrl = "https://game.skland.com/arknights/stronghold-guide/edit?header=0";
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new SklandSession(() => completion.TrySetResult(null));
        _sklandSessions.Add(session);
        var browser = visible ? new WebView2() : null;
        // TODO(design): 确认独立登录窗口标题；尺寸沿用历史战绩窗口。
        var window = visible ? new Window
        {
            Owner = DialogOwner, Title = "森空岛 · 登录", Width = 480, Height = 720,
            Content = browser, WindowStartupLocation = WindowStartupLocation.CenterOwner
        } : null;
        using var source = visible ? null : new HwndSource(new HwndSourceParameters("SklandLogin")
        { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
        CoreWebView2Controller? controller = null;
        using var lifetime = new CancellationTokenSource();
        CancellationTokenSource? navigation = null;
        var closed = false;
        if (window is not null) window.Closed += (_, _) =>
        {
            closed = true;
            lifetime.Cancel();
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
                    await browser!.EnsureCoreWebView2Async(environment);
                    if (closed) return;
                    core = browser.CoreWebView2;
                }
                else
                {
                    var created = await environment.CreateCoreWebView2ControllerAsync(source!.Handle);
                    if (closed) { created.Close(); return; }
                    controller = created;
                    controller.Bounds = new System.Drawing.Rectangle(0, 0, 800, 600);
                    controller.IsVisible = true;
                    core = controller.CoreWebView2;
                }
                session.Ready.TrySetResult(core);
                ConfigureSkland(core, true);
                core.NavigationStarting += (_, _) => navigation?.Cancel();
                core.NavigationCompleted += async (_, args) =>
                {
                    if (closed) return;
                    if (!args.IsSuccess)
                    {
                        completion.TrySetException(new SklandException(
                            $"打不开森空岛，检查一下网络再试。\nWebErrorStatus: {args.WebErrorStatus}"));
                        return;
                    }
                    navigation?.Cancel();
                    navigation?.Dispose();
                    navigation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    var token = navigation.Token;
                    try
                    {
                        var waitingSince = DateTime.UtcNow;
                        while (!completion.Task.IsCompleted)
                        {
                            var result = await core.ExecuteScriptAsync("""
                                [...document.querySelectorAll('input')].some(i =>
                                  i.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true }) &&
                                  (i.type === 'password' || i.type === 'tel' || /手机号/.test(i.placeholder + i.parentElement.textContent)))
                                """);
                            token.ThrowIfCancellationRequested();
                            var loginForm = result == "true";
                            if (!visible && loginForm) { completion.TrySetResult(null); return; }
                            if (SklandParser.HistoryWaitExpired(visible, loginForm, DateTime.UtcNow, ref waitingSince))
                                throw new SklandException("打不开森空岛，检查一下网络再试。\nTimeoutException: 15 秒内未收到角色绑定。");
                            await Task.Delay(500, token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested) completion.TrySetException(
                            ex is SklandException ? ex : SklandReadError("打不开森空岛，检查一下网络再试。", ex));
                    }
                };
                core.WebResourceResponseReceived += async (_, args) =>
                {
                    var uri = new Uri(args.Request.Uri);
                    if (closed || completion.Task.IsCompleted || uri.Scheme != "https" || uri.Host != "zonai.skland.com"
                        || uri.AbsolutePath != SklandBindingPath || args.Request.Method != "GET") return;
                    if (args.Response.StatusCode == 401)
                    {
                        if (!visible) completion.TrySetResult(null);
                        return;
                    }
                    if (args.Response.StatusCode != 200) return;
                    try
                    {
                        using var stream = await args.Response.GetContentAsync();
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
                        if (closed || completion.Task.IsCompleted) return;
                        if (SklandParser.ParseAccount(json) is not null) completion.TrySetResult(json);
                        else if (SklandHasRole(json) == false) completion.TrySetException(new SklandException(
                            "森空岛账号还没有绑定明日方舟角色，先在森空岛 App 里绑定"));
                        else if (!visible) completion.TrySetResult(null);
                    }
                    catch (Exception ex) { completion.TrySetException(SklandReadError("森空岛角色的数据结构已变化，暂时无法读取。", ex)); }
                };
                core.Navigate(loginUrl);
            }
            catch (Exception ex)
            {
                if (!closed) completion.TrySetException(SklandReadError("打不开森空岛，检查一下网络再试。", ex));
            }
        }

        try
        {
            if (window is not null)
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
            controller?.Close();
            window?.Close();
            browser?.Dispose();
            FinishSklandSession(session);
        }
    }
}
