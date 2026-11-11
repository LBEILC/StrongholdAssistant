using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace StrongholdAssistant.Host;

public partial class MainWindow : Window
{
    private HostApi _api = null!;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private bool _catalogReported;
    private readonly bool _connectGame;
    private readonly string? _userDirectory;
    private Task _initialization = Task.CompletedTask;

    public MainWindow() : this(true) { }

    // 供无游戏启动验收工具使用，产品入口始终启用游戏服务。
    public MainWindow(bool connectGame, string? userDirectory = null)
    {
        _connectGame = connectGame;
        _userDirectory = userDirectory;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await (_initialization = InitializeAsync());

    private async Task InitializeAsync()
    {
#if DEBUG
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "logs"));
        Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(AppContext.BaseDirectory, "logs", "host.log")));
        Trace.AutoFlush = true;
#endif
        var userDirectory = _userDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StrongholdAssistant");
        _api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), userDirectory);
        InitializeWindow();
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(userDirectory, "WebView2"));
        // 页面加载前的底色和界面一致，避免白屏
        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0b, 0x0c, 0x0d);
        await Browser.EnsureCoreWebView2Async(environment);
        ApplyScale(null);
        var web = Browser.CoreWebView2;
        web.Settings.IsNonClientRegionSupportEnabled = true;
        // 缩放只走设置里的"界面大小"：关掉 Ctrl+滚轮 / Ctrl+加减号，否则页面放大而窗口不变，侧栏会挤坏
        web.Settings.IsZoomControlEnabled = false;
        Trace.WriteLine($"WebView2 runtime={environment.BrowserVersionString}; IsNonClientRegionSupportEnabled={web.Settings.IsNonClientRegionSupportEnabled}");
        var dev = Environment.GetEnvironmentVariable("STRONGHOLD_UI_DEV") == "1";
        var origin = dev ? "http://localhost:5178" : "https://app.local";
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
            web.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
        web.Navigate(dev ? origin + "/" : origin + "/index.html");
        await InitializeGameAsync();
        if (_connectGame && !_closing)
        {
            _tracker.Start();
            _scanner.Start();
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        int? id = null;
        string? windowCommand = null;
        var locked = false;
        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, HostApi.JsonOptions)!;
            id = request.Id;
            // 重扫由识别锁立即判忙；其余请求保持原有串行存储语义。
            if (request.Method is not ("rescan" or "scanContinuous"))
            {
                await _requests.WaitAsync();
                locked = true;
            }
            object? result;
            if (request.Method == "windowCommand")
            {
                windowCommand = request.Params[0].GetString();
                if (windowCommand is not ("minimize" or "close" or "collapse" or "expand")) throw new ArgumentException("窗口命令无效。");
                result = null;
            }
            else
            {
                // 界面先于 OCR 加载；预热完成前点"重新识别"会等初始化结束
                if (request.Method is "rescan" or "scanContinuous") await _initialization;
                result = await _api.InvokeAsync(request.Method, request.Params);
            }
            if (request.Method == "saveSettings") RefreshScale();
            if (request.Method == "connection")
                Trace.WriteLine($"connection()={JsonSerializer.Serialize(result, HostApi.JsonOptions)}");
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, result }, HostApi.JsonOptions));
            if (request.Method == "catalog" && !_catalogReported)
            {
                _catalogReported = true;
                App.StartupResult.ReportReady();
                var catalog = _api.Catalog;
                Trace.WriteLine($"消息桥请求 id={id}, method=catalog; 已发送响应: covenants={catalog.GetProperty("covenants").GetArrayLength()}, " +
                    $"ids={catalog.GetProperty("covenants").EnumerateArray().Count(c => !string.IsNullOrEmpty(c.GetProperty("id").GetString()))}, " +
                    $"operators={catalog.GetProperty("operators").GetArrayLength()}, equipment={catalog.GetProperty("equipment").GetArrayLength()}");
            }
        }
        catch (Exception ex)
        {
            // TODO(design)：确认消息桥错误提示文案；暂用简短中文说明。
            var error = ex is RecognitionException || ex is ArgumentException && ex.Message.EndsWith('。')
                ? ex.Message : "请求处理失败，请检查数据后重试。";
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, error }, HostApi.JsonOptions));
            windowCommand = null;
        }
        finally { if (locked) _requests.Release(); }
        if (windowCommand == "minimize") WindowState = WindowState.Minimized;
        if (windowCommand == "close") Close();
        if (windowCommand == "collapse") SetCollapsed(true);
        if (windowCommand == "expand") SetCollapsed(false);
        if (_api.UpdatePrepared)
        {
            try
            {
                if (_api.Updater.LaunchPendingUpdateScript()) Close();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                {
                    @event = "update", data = new { state = "failed", message = "无法启动更新程序，请重试。" }
                }, HostApi.JsonOptions));
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _tracker.Dispose();
        _scanner.Dispose();
        _ocr.Dispose();
        _iconMatcher.Dispose();
        Browser.Dispose();
        base.OnClosed(e);
    }

    private sealed record BridgeRequest(int Id, string Method, JsonElement[] Params);
}
