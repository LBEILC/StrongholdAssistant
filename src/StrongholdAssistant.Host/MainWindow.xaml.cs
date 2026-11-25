using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using ASPAssistant.Core.Interop;
using Microsoft.Web.WebView2.Core;

namespace StrongholdAssistant.Host;

public partial class MainWindow : Window
{
    private HostApi _api = null!;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private bool _catalogReported;
    private bool _uiReady;
    private readonly Queue<string> _pendingDeepLinks = new();
    private readonly bool _connectGame;
    private readonly string? _userDirectory;
    private Task _initialization = Task.CompletedTask;
    private bool _hiddenByMinimize;
    private readonly Queue<long> _rendererReloads = new();

    public MainWindow() : this(true) { }

    // 供无游戏启动验收工具使用，产品入口始终启用游戏服务。
    public MainWindow(bool connectGame, string? userDirectory = null)
    {
        _connectGame = connectGame;
        _userDirectory = userDirectory;
        _tracker = new(findWindow: _capture.Connection.FindGameWindow, getViewport: _capture.Connection.GetViewport);
        InitializeComponent();
        var directory = _userDirectory ?? AssistantProfile.Current.DirectoryPath;
        var settings = new HostSettingsStore(Path.Combine(directory, "settings.json")).LoadSettings();
        ShowInTaskbar = !settings.HideFromTaskbar;
        if (settings.HideFromTaskbar) SourceInitialized += (_, _) => SetToolWindow(true);
        ApplySplashTheme(settings);
    }

    private void SetToolWindow(bool hide)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
            hide ? style | User32.WS_EX_TOOLWINDOW : style & ~User32.WS_EX_TOOLWINDOW);
    }

    private void ApplySplashTheme(SettingsDto settings)
    {
        var light = settings.UiScheme == "light";
        if (settings.UiScheme == "system")
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        if (light && settings.UiSkin is "glass" or "round")
        {
            var colors = settings.UiSkin == "glass"
                ? new[] { "#ECEFF4", "#E7EAEF", "#1B1F26", "#6B7280", "#0067C0", "#DDE1E7" }
                : new[] { "#F3F1F8", "#ECE9F4", "#1E1B29", "#716C88", "#6854E8", "#E4E1EE" };
            string[] keys = ["SplashBackground", "SplashBorder", "SplashText", "SplashMuted", "SplashAccent", "SplashHover"];
            for (var i = 0; i < keys.Length; i++)
                Resources[keys[i]] = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]));
            SplashBody.Background = (System.Windows.Media.Brush)Resources["SplashBackground"];
        }
        var background = ((System.Windows.Media.SolidColorBrush)Resources["SplashBackground"]).Color;
        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(background.R, background.G, background.B);
    }

    private void MinimizeAssistant()
    {
        if (ShowInTaskbar) WindowState = WindowState.Minimized;
        else
        {
            _hiddenByMinimize = true;
            Hide();
        }
    }

    internal void ShowAfterMinimize()
    {
        _hiddenByMinimize = false;
        if (!IsVisible) Show();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await (_initialization = InitializeAsync());

    private async Task InitializeAsync()
    {
        var userDirectory = _userDirectory ?? AssistantProfile.Current.DirectoryPath;
        Trace.Listeners.Add(new HostTraceListener());
        var dataDirectory = Path.Combine(ASPAssistant.Core.PackageLayout.Root, "data");
        await HostApi.CheckStartupDataAsync(dataDirectory, App.StartupResult.IsPending
            || Environment.GetCommandLineArgs().Contains("--keep-identity"));
        _api = await HostApi.CreateAsync(dataDirectory, userDirectory);
        _ = _api.DownloadIncomeTableAsync();
        _api.DialogOwner = this;
        _api.UpdateReady += () => Dispatcher.BeginInvoke(LaunchPreparedUpdate);
        InitializeWindow();
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(userDirectory, "WebView2"),
            options: ASPAssistant.Core.Services.CrashGuard.GpuDisabled ? new CoreWebView2EnvironmentOptions("--disable-gpu") : null);
        environment.BrowserProcessExited += (_, args) => ASPAssistant.Core.AppLogger.Info("WebView2",
            $"BrowserProcessExited: BrowserProcessExitKind={args.BrowserProcessExitKind}; BrowserProcessId={args.BrowserProcessId}");
        // 默认底色已在构造窗口时按启动设置应用。
        await Browser.EnsureCoreWebView2Async(environment);
        var web = Browser.CoreWebView2;
        web.ProcessFailed += (_, args) =>
        {
            var action = "";
            var rendererFailed = args.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive;
            var reload = rendererFailed && TryReserveRendererReload(_rendererReloads, Environment.TickCount64);
            if (rendererFailed) action = reload ? "；自动重载界面" : "；一分钟内已重载三次，跳过本次重载";
            ASPAssistant.Core.AppLogger.Error("WebView2",
                $"ProcessFailed: ProcessFailedKind={args.ProcessFailedKind}; Reason={args.Reason}; " +
                $"ExitCode={args.ExitCode}; ProcessDescription={args.ProcessDescription}{action}");
            if (reload) web.Reload();
            if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.GpuProcessExited) ASPAssistant.Core.Services.CrashGuard.GpuCrashed();
        };
        ApplyScale(null);
        web.Settings.IsNonClientRegionSupportEnabled = true;
        // 缩放只走设置里的"界面大小"：关掉 Ctrl+滚轮 / Ctrl+加减号，否则页面放大而窗口不变，侧栏会挤坏
        web.Settings.IsZoomControlEnabled = false;
        Trace.WriteLine($"WebView2 runtime={environment.BrowserVersionString}; IsNonClientRegionSupportEnabled={web.Settings.IsNonClientRegionSupportEnabled}");
        var dev = Environment.GetEnvironmentVariable("STRONGHOLD_UI_DEV") == "1";
        var origin = dev ? "http://localhost:5178" : "https://app.local";
        if (!dev)
        {
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
        }
        // 自动填充会保存在配置中，切回开发模式时恢复原来的默认值。
        web.Settings.IsGeneralAutofillEnabled = dev;
        web.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.ClipboardRead
                && Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
                && uri.GetLeftPart(UriPartial.Authority) == origin)
                args.State = CoreWebView2PermissionState.Allow;
        };
        web.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != origin)
                args.Cancel = true;
        };
        web.NewWindowRequested += (_, args) => args.Handled = true;
        web.WebMessageReceived += OnWebMessage;
        web.NavigationCompleted += (_, args) => Trace.WriteLine($"界面导航完成: success={args.IsSuccess}; source={web.Source}");
        web.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess) App.StartupResult.ReportFailure("界面加载失败");
        };
        if (!dev)
            web.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(ASPAssistant.Core.PackageLayout.Root, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
        Directory.CreateDirectory(_api.ControlDirectory);
        web.SetVirtualHostNameToFolderMapping("control.local", _api.ControlDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
        web.Navigate(dev ? origin + "/" : origin + "/index.html");
        InitializeGameTracking();
        if (_connectGame && !_closing) _tracker.Start();
        await InitializeGameAsync();
        if (!_closing) _api.ConfigureLocalApi();
        if (_connectGame && !_closing)
        {
            // 初始化完成后再按已连接窗口定位，避免停留在启动尺寸与位置。
            RefreshScale();
            _scanner?.Start();
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        int? id = null;
        string? windowCommand = null;
        string? resizeEdge = null;
        var locked = false;
        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, HostApi.JsonOptions)!;
            id = request.Id;
            // 重扫由识别锁判忙；登录复用窗口结果，不占用存储请求锁。
            if (request.Method is not ("rescan" or "startRosterScan" or "stopRosterScan" or "plazaLogin" or "sklandGuide" or "sklandHistory" or "sklandLogin"))
            {
                await _requests.WaitAsync();
                locked = true;
            }
            object? result;
            bool followedBefore = _api.Settings.FollowGame;
            if (request.Method == "windowCommand")
            {
                windowCommand = request.Params[0].GetString();
                if (windowCommand is not ("minimize" or "close" or "collapse" or "expand" or "ready" or "workbench" or "sidebar" or "redock" or "maximize" or "restore")) throw new ArgumentException("窗口命令无效。");
                if (windowCommand == "redock") Redock();
                result = null;
            }
            else if (request.Method == "resizeWindow")
            {
                resizeEdge = request.Params[0].GetString();
                _ = ResizeHit(resizeEdge);
                result = null;
            }
            else if (request.Method == "setRunAsAdmin")
            {
                var on = request.Params[0].GetBoolean();
                if (on && !ProcessElevation.Current)
                {
                    ProcessElevation.RestartElevated();
                    _api.SaveRunAsAdmin(true);
                    Close();
                }
                else _api.SaveRunAsAdmin(on); // 关闭后下次普通启动生效。
                result = null;
            }
            else if (request.Method == "setHotkey")
            {
                SetHotkey(request.Params[0].GetString()!);
                result = null;
            }
            else if (request.Method == "collapseUp")
            {
                var height = request.Params[0].GetDouble();
                if (!double.IsFinite(height) || height <= 0) throw new ArgumentException("收起高度无效。");
                SetCollapse(true, height);
                result = null;
            }
            else
            {
                // 界面先于 OCR 加载；预热完成前点"重新识别"会等初始化结束
                if (request.Method is "rescan" or "startRosterScan") await _initialization;
                result = await _api.InvokeAsync(request.Method, request.Params);
            }
            if (request.Method is "saveSettings" or "importData") await RefreshSettingsAsync(followedBefore);
            if (request.Method == "connection")
                Trace.WriteLine($"connection()={JsonSerializer.Serialize(result, HostApi.JsonOptions)}");
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, result }, HostApi.JsonOptions));
            if (request.Method == "catalog" && !_catalogReported)
            {
                _catalogReported = true;
                var catalog = _api.Catalog;
                Trace.WriteLine($"消息桥请求 id={id}, method=catalog; 已发送响应: covenants={catalog.GetProperty("covenants").GetArrayLength()}, " +
                    $"ids={catalog.GetProperty("covenants").EnumerateArray().Count(c => !string.IsNullOrEmpty(c.GetProperty("id").GetString()))}, " +
                    $"operators={catalog.GetProperty("operators").GetArrayLength()}, equipment={catalog.GetProperty("equipment").GetArrayLength()}");
            }
        }
        catch (Exception ex)
        {
            // TODO(design)：确认消息桥错误提示文案；暂用简短中文说明。
            // 读取我的干员、模拟器连接等用 IOException 抛给玩家看的中文提示（如「先关掉右侧的盟约筛选」）也原样显示；
            // 英文的是内部错误，仍换成通用提示（v1.0.2 玩家诊断包）
            var error = ex is RecognitionException or SklandException || ex is ArgumentException && ex.Message.EndsWith('。')
                || ex is IOException && ex.Message.Any(c => c is >= '一' and <= '鿿')
                ? ex.Message : "请求处理失败，请检查数据后重试。";
            // 记下原因：之前只回给界面一句通用提示，诊断包里查不到是什么错
            Trace.WriteLine($"消息桥请求失败：{ex}");
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, error }, HostApi.JsonOptions));
            windowCommand = null;
            resizeEdge = null;
        }
        finally { if (locked) _requests.Release(); }
        if (windowCommand == "minimize") MinimizeAssistant();
        if (windowCommand == "close") Close();
        if (windowCommand == "collapse") SetCollapsed(true);
        if (windowCommand == "expand") SetCollapsed(false);
        if (windowCommand == "ready" && !_uiReady)
        {
            ShowUi();
            ReportStartupReady(windowCommand, App.StartupResult);
            await _requests.WaitAsync();
            try { await _api.RecoverUpdateOnStartupAsync(); }
            finally { _requests.Release(); }
        }
        if (windowCommand == "workbench") SetWorkbench(true);
        if (windowCommand == "sidebar") SetWorkbench(false);
        if (_workbench && windowCommand == "maximize") WindowState = WindowState.Maximized;
        if (_workbench && windowCommand == "restore") WindowState = WindowState.Normal;
        if (resizeEdge is not null) ResizeWindow(resizeEdge);
    }

    private bool _installing;
    private async void LaunchPreparedUpdate()
    {
        if (_api.UpdatePrepared && !_installing)
        {
            _installing = true;
            try
            {
                try { using var check = ASPAssistant.Core.Services.UpdateService.OpenProfileLock(FileShare.None); }
                catch (IOException ex) when ((ex.HResult & 0xffff) == 32)
                {
                    if (MessageBox.Show(this, "安装更新需要关闭这个安装目录的所有助手窗口。现在关闭并安装吗？",
                            Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    try
                    {
                        await InstanceRecovery.CloseProfilesForUpdateAsync();
                        using var check = ASPAssistant.Core.Services.UpdateService.OpenProfileLock(FileShare.None);
                    }
                    catch (Exception closeError) when (closeError is IOException or OperationCanceledException or System.ComponentModel.Win32Exception)
                    {
                        MessageBox.Show(this, "还有助手未能关闭。请手动关闭其他配置后再点安装更新。");
                        return;
                    }
                }
                if (!_api.Updater.LaunchPendingUpdateScript()) throw new IOException("更新脚本不存在");
                Close();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                _api.ClearPreparedUpdate();
                Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                {
                    @event = "update", data = new { state = "failed", message = "无法启动更新程序，请重试。" }
                }, HostApi.JsonOptions));
            }
            finally { _installing = false; }
        }
    }

    internal static void ReportStartupReady(string? windowCommand, ASPAssistant.Core.Services.UpdateStartupNotification result)
    {
        if (windowCommand == "ready" && result.IsPending) result.ReportReady();
    }

    // 网页挂载完成：把骨架换成网页（两者都只有同样的标题栏），网页变为可见后自己播入场动画
    private void ShowUi()
    {
        if (Application.Current is App app) app.UiReady = true;
        Browser.Visibility = Visibility.Visible;
        Splash.Visibility = Visibility.Collapsed;
        _uiReady = true;
        PublishRecognitionError();
        PublishMaximized();
        while (_pendingDeepLinks.TryDequeue(out var url)) ReceiveDeepLink(url);
    }

    public void ReceiveDeepLink(string url)
    {
        if (!DeepLinkChannel.IsValid(url)) return;
        if (!_uiReady)
        {
            _pendingDeepLinks.Enqueue(url);
            return;
        }
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = "deepLink", data = url }, HostApi.JsonOptions));
    }

    private void PostWindowEvent(string e) =>
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = "window", data = e }, HostApi.JsonOptions));

    private void OnSplashDrag(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
    private void OnSplashMinimize(object sender, RoutedEventArgs e) => MinimizeAssistant();
    private void OnSplashClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _tracker.Dispose();
        _scanner?.Dispose();
        _api?.CloseShopAdvice();
        _capture.Connection.Dispose();
        _ocr?.Dispose();
        _iconMatcher?.Dispose();
        Browser.Dispose();
        base.OnClosed(e);
    }

    private sealed record BridgeRequest(int Id, string Method, JsonElement[] Params);

    internal static bool TryReserveRendererReload(Queue<long> reloads, long now)
    {
        while (reloads.TryPeek(out var oldest) && now - oldest >= 60_000) reloads.Dequeue();
        if (reloads.Count >= 3) return false;
        reloads.Enqueue(now);
        return true;
    }

    private sealed class HostTraceListener : TraceListener
    {
        public override void Write(string? message) => WriteLine(message);
        public override void WriteLine(string? message) => ASPAssistant.Core.AppLogger.Info("Host", message ?? "");
    }
}
