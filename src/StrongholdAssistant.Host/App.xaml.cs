using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Diagnostics;
using System.ComponentModel;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _activation;
    private EventWaitHandle? _activated;
    private RegisteredWaitHandle? _wait;
    private bool _ownsInstance;
    private readonly DeepLinkChannel _deepLinks = new("StrongholdAssistant.DeepLink");
    private Task? _deepLinkListener;
    private string _logDirectory = null!;
    private int _failureDialogShown;
    private string? _webView2Version;
    private Version _windowsVersion = Environment.OSVersion.Version;
    internal volatile bool UiReady;
    internal static readonly UpdateStartupNotification StartupResult = new(
        Environment.GetEnvironmentVariable(UpdateStartupNotification.ResultVariable));

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 打包检查只读样图，不创建窗口、不激活已有实例，也不访问游戏。
        if (e.Args.FirstOrDefault() == "--check-recognition")
        {
            Shutdown(await PackageRecognitionCheck.RunAsync(e.Args));
            return;
        }
        await ProcessElevation.WaitForPreviousAsync(e.Args);
        var noGame = e.Args.Contains("--no-game");
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StrongholdAssistant", "settings.json");
        var deepLink = e.Args.FirstOrDefault(DeepLinkChannel.IsValid);
        _instance = new Mutex(true, @"Local\StrongholdAssistant", out _ownsInstance);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StrongholdAssistant.Activate");
        _activated = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StrongholdAssistant.Activated");
        if (_ownsInstance)
        {
            ProcessElevation.AllowSameUser(_instance.SafeWaitHandle);
            ProcessElevation.AllowSameUser(_activation.SafeWaitHandle);
            ProcessElevation.AllowSameUser(_activated.SafeWaitHandle);
        }
        if (!_ownsInstance)
        {
            AllowSetForegroundWindow(-1);
            _activated.Reset();
            var sent = true;
            try
            {
                if (DeepLinkChannel.IsValid(deepLink)) await _deepLinks.SendAsync(deepLink!);
                else _activation.Set();
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                // 管道连接失败也按旧实例无回应处理。
                sent = false;
            }
            if (!sent || !await InstanceRecovery.WaitForResponseAsync(
                    timeout => Task.Run(() => _activated.WaitOne(timeout))))
                RecoverInstance(e.Args, noGame, settingsPath);
            Shutdown();
            return;
        }
        if (!noGame && !ProcessElevation.Current && !e.Args.Contains("--keep-identity")
            && new HostSettingsStore(settingsPath).LoadSettings().RunAsAdmin)
        {
            try { ProcessElevation.RestartElevated(); Shutdown(); return; }
            catch (ArgumentException) { /* 取消授权后继续普通运行，不重试。 */ }
        }
        // 无游戏模式供打包验收使用，不启动窗口跟踪或截图。
        new UpdateService().CleanupStaleStagingDirs();
        var userDirectory = noGame
            ? Path.Combine(Path.GetTempPath(), "StrongholdAssistant-StartupCheck", Guid.NewGuid().ToString("N"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StrongholdAssistant");
        _logDirectory = Path.Combine(userDirectory, "logs");
        AppLogger.Initialize(_logDirectory);
        DispatcherUnhandledException += (_, args) => ReportUnhandledException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportUnhandledException((Exception)args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => ReportUnhandledException(args.Exception);
        var runtime = StartupDiagnostics.IsWebView2Available();
        _webView2Version = runtime.Version;
        _windowsVersion = StartupDiagnostics.GetWindowsVersion();
        StartupDiagnostics.LogStartupEnvironment(_windowsVersion, _webView2Version, Path.Combine(userDirectory, "WebView2"));
        if (runtime.Status == WebView2Availability.Missing)
        {
            const string message = "这台电脑缺少「Microsoft Edge WebView2 运行时」，助手的界面要靠它显示。";
            AppLogger.Error("Crash", message);
            StartupResult.ReportFailure(message);
            ShowFailureAndExit(message + "\n\n点「是」打开微软官方下载（免费，几 MB），装好后重新打开助手。",
                "https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            return;
        }
        if (runtime.Status == WebView2Availability.TooOld)
        {
            ReportOutdatedWebView2();
            return;
        }
        MainWindow = new MainWindow(!noGame, userDirectory);
        if (DeepLinkChannel.IsValid(deepLink)) ((MainWindow)MainWindow).ReceiveDeepLink(deepLink!);
        _deepLinkListener = _deepLinks.ListenAsync(url => Dispatcher.BeginInvoke(() =>
        {
            ActivateMainWindow();
            (MainWindow as MainWindow)?.ReceiveDeepLink(url);
            if (InstanceRecovery.ShouldAcknowledgeActivation(UiReady, MainWindow?.IsVisible == true))
                _activated.Set();
        }));
        MainWindow.Show();
        _wait = ThreadPool.RegisterWaitForSingleObject(_activation,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                ActivateMainWindow();
                if (InstanceRecovery.ShouldAcknowledgeActivation(UiReady, MainWindow?.IsVisible == true))
                    _activated.Set();
            }), null, Timeout.Infinite, false);
    }

    private void RecoverInstance(string[] args, bool noGame, string settingsPath)
    {
        if (MessageBox.Show("卫戍协议助手已经在运行，但没有响应，可能卡住了。\n要结束它并重新打开吗？",
                "卫戍协议助手", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        AppLogger.Initialize(noGame
            ? Path.Combine(Path.GetTempPath(), "StrongholdAssistant-StartupCheck", Guid.NewGuid().ToString("N"), "logs")
            : Path.Combine(Path.GetDirectoryName(settingsPath)!, "logs"));
        try
        {
            InstanceRecovery.StopOtherInstances();
            // 新进程创建前关闭本进程持有的命名对象，避免它再次被判断为第二个实例。
            _instance!.Dispose();
            _activation!.Dispose();
            _activated!.Dispose();
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var restarted = Process.Start(start);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
        {
            MessageBox.Show("结束不了，可能是以管理员身份运行的。请在任务管理器里结束 StrongholdAssistant 后再打开",
                "卫戍协议助手", MessageBoxButton.OK);
        }
    }

    private void ReportUnhandledException(Exception exception)
    {
        StartupDiagnostics.LogCrash(exception);
        if (!UiReady && StartupDiagnostics.IsWebView2InterfaceUnavailable(exception))
        {
            ReportOutdatedWebView2();
            return;
        }
        StartupResult.ReportFailure(exception.Message);
        if (!UiReady && StartupDiagnostics.IsWebView2StartupFailure(exception))
        {
            ShowFailureAndExit(StartupDiagnostics.WebView2StartupFailureMessage(_windowsVersion),
                "https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            return;
        }
        if (!UiReady)
            ShowFailureAndExit($"助手启动失败：{exception.Message.Split(['\r', '\n'])[0]}\n\n" +
                "点「是」打开日志文件夹，把 host.log 发到 QQ 群 1132134217 或 GitHub Issues，方便我们排查。", _logDirectory);
    }

    private void ReportOutdatedWebView2()
    {
        var version = _webView2Version is null ? "" : $"（{_webView2Version}）";
        var message = $"这台电脑的「Microsoft Edge WebView2 运行时」版本太旧{version}，助手需要 {StartupDiagnostics.MinimumWebView2Version} 或更新的版本。";
        AppLogger.Error("Crash", message);
        StartupResult.ReportFailure(message);
        ShowFailureAndExit(message + "\n\n点「是」打开微软官方下载，装好后重新打开助手。",
            "https://go.microsoft.com/fwlink/p/?LinkId=2124703");
    }

    private void ShowFailureAndExit(string message, string target)
    {
        if (Interlocked.Exchange(ref _failureDialogShown, 1) != 0) return;
        try
        {
            if (MessageBox.Show(message, "卫戍协议助手", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        finally
        {
            // 启动失败时窗口和服务可能只初始化了一部分，直接结束进程，避免执行正常关闭流程。
            Environment.Exit(1);
        }
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is null) return;
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        SetForegroundWindow(new WindowInteropHelper(MainWindow).Handle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _deepLinks.Dispose();
        _wait?.Unregister(null);
        _activation?.Dispose();
        _activated?.Dispose();
        if (_ownsInstance) _instance!.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
