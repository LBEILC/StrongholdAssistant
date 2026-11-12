using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _wait;
    private bool _ownsInstance;
    private readonly DeepLinkChannel _deepLinks = new("StrongholdAssistant.DeepLink");
    private Task? _deepLinkListener;
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
        var deepLink = e.Args.FirstOrDefault();
        _instance = new Mutex(true, @"Local\StrongholdAssistant", out _ownsInstance);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StrongholdAssistant.Activate");
        if (!_ownsInstance)
        {
            AllowSetForegroundWindow(-1);
            if (DeepLinkChannel.IsValid(deepLink)) await _deepLinks.SendAsync(deepLink!);
            else _activation.Set();
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => StartupResult.ReportFailure(args.Exception.Message);
        // 无游戏模式供打包验收使用，不启动窗口跟踪或截图。
        var noGame = e.Args.Contains("--no-game");
        MainWindow = new MainWindow(!noGame, noGame
            ? Path.Combine(Path.GetTempPath(), "StrongholdAssistant-StartupCheck", Guid.NewGuid().ToString("N")) : null);
        if (DeepLinkChannel.IsValid(deepLink)) ((MainWindow)MainWindow).ReceiveDeepLink(deepLink!);
        _deepLinkListener = _deepLinks.ListenAsync(url => Dispatcher.BeginInvoke(() =>
        {
            ActivateMainWindow();
            ((MainWindow)MainWindow).ReceiveDeepLink(url);
        }));
        MainWindow.Show();
        _wait = ThreadPool.RegisterWaitForSingleObject(_activation,
            (_, _) => Dispatcher.BeginInvoke(ActivateMainWindow), null, Timeout.Infinite, false);
    }

    private void ActivateMainWindow()
    {
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        SetForegroundWindow(new WindowInteropHelper(MainWindow).Handle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _deepLinks.Dispose();
        _wait?.Unregister(null);
        _activation?.Dispose();
        if (_ownsInstance) _instance!.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
