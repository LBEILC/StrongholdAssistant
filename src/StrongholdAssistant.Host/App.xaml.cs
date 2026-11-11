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
    internal static readonly UpdateStartupNotification StartupResult = new(
        Environment.GetEnvironmentVariable(UpdateStartupNotification.ResultVariable));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\StrongholdAssistant", out _ownsInstance);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StrongholdAssistant.Activate");
        if (!_ownsInstance)
        {
            AllowSetForegroundWindow(-1);
            _activation.Set();
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => StartupResult.ReportFailure(args.Exception.Message);
        // 无游戏模式供打包验收使用，不启动窗口跟踪或截图。
        var noGame = e.Args.Contains("--no-game");
        MainWindow = new MainWindow(!noGame, noGame
            ? Path.Combine(Path.GetTempPath(), "StrongholdAssistant-StartupCheck", Guid.NewGuid().ToString("N")) : null);
        MainWindow.Show();
        _wait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
            SetForegroundWindow(new WindowInteropHelper(MainWindow).Handle);
        }), null, Timeout.Infinite, false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
