using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using ASPAssistant.Core.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    public const string PlazaOrigin = "https://sa.lubeiluchen.com";
    private Window? _plazaWindow;
    private Task<string?>? _plazaLogin;

    public static string? ParsePlazaLogin(string source, string json)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || uri.GetLeftPart(UriPartial.Authority) != PlazaOrigin) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                && type.GetString() == "plaza-login"
                && root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public Task<string?> PlazaLoginAsync()
    {
        if (_plazaWindow is not null)
        {
            if (_plazaWindow.WindowState == WindowState.Minimized) _plazaWindow.WindowState = WindowState.Normal;
            _plazaWindow.Activate();
            return _plazaLogin!;
        }
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new WebView2();
        var window = new Window
        {
            Owner = DialogOwner, Title = "登录阵容广场", Width = 440, Height = 680,
            Content = browser, WindowStartupLocation = WindowStartupLocation.Manual
        };
        var owner = DialogOwner!;
        var dpi = User32.GetWindowDpiScale(new WindowInteropHelper(owner).Handle);
        var work = User32.GetMonitorGeometry((int)((owner.Left + owner.ActualWidth / 2) * dpi),
            (int)((owner.Top + owner.ActualHeight / 2) * dpi)).WorkArea;
        var area = new Rect(work.Left / dpi, work.Top / dpi, work.Width / dpi, work.Height / dpi);
        var right = owner.Left + owner.ActualWidth;
        window.Left = right + window.Width <= area.Right ? right : Math.Max(area.Left, owner.Left - window.Width);
        window.Top = Math.Max(area.Top, Math.Min(owner.Top + (owner.ActualHeight - window.Height) / 2, area.Bottom - window.Height));
        _plazaWindow = window;
        _plazaLogin = completion.Task;
        var closed = false;
        window.Closed += (_, _) =>
        {
            closed = true;
            _plazaWindow = null;
            _plazaLogin = null;
            browser.Dispose();
            completion.TrySetResult(null);
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                // 自动填充设置随配置共享，登录窗口独立存储以保留默认自动填充。
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(DataDirectory, "PlazaWebView2"));
                if (closed) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (closed) return;
                browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browser.CoreWebView2.ContextMenuRequested += (_, args) =>
                {
                    for (var i = args.MenuItems.Count - 1; i >= 0; i--)
                    {
                        if (args.MenuItems[i].Name is not ("cut" or "copy" or "paste" or "selectAll"))
                            args.MenuItems.RemoveAt(i);
                    }
                    if (args.MenuItems.Count == 0) args.Handled = true;
                };
                browser.CoreWebView2.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    OpenUrl(args.Uri);
                };
                browser.CoreWebView2.WebMessageReceived += (_, args) =>
                {
                    var token = ParsePlazaLogin(args.Source, args.WebMessageAsJson);
                    if (token is null) return;
                    try
                    {
                        new PlazaTokenStore(DataDirectory).Save(token);
                        completion.TrySetResult(token);
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    window.Close();
                };
                browser.CoreWebView2.Navigate(PlazaOrigin + "/login?app=1");
            }
            catch (Exception ex)
            {
                if (closed) return;
                completion.TrySetException(ex);
                window.Close();
            }
        };
        window.Show();
        return completion.Task;
    }
}
