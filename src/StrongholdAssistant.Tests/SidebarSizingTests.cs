using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public class SidebarSizingTests
{
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void OverlayDraggedToRightCollapsesAtClientEdgeAndExpandsLeft(string side) => OnSta(() =>
    {
        var directory = Directory.CreateTempSubdirectory("SidebarPosition-");
        var api = HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory.FullName)
            .GetAwaiter().GetResult();
        var window = new MainWindow(false, directory.FullName);
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var dpi = User32.GetWindowDpiScale(hwnd);
        var game = User32.GetMonitorGeometry(0, 0).Bounds;
        var tracker = (WindowTrackerService)Get(window, "_tracker")!;
        try
        {
            Set(window, "_api", api);
            typeof(WindowTrackerService).GetField("_getViewport", Private)!
                .SetValue(tracker, (Func<IntPtr, RECT?>)(_ => game));
            typeof(HostApi).GetProperty(nameof(HostApi.Settings))!.SetValue(api,
                api.Settings with { FollowGame = true, OverlaySide = side, UiScale = JsonSerializer.SerializeToElement(1) });
            Set(window, "_dock", new DockSettings(380, OverlayOffsetDip: new(game.Width / dpi - 400, 73),
                OverlayRightOffsetDip: new(-20, 73)));
            Call(window, "UpdateGame", game);
            var expanded = User32.ReadWindowRect(hwnd);
            Call(window, "SetCollapse", true, null);
            var strip = User32.ReadWindowRect(hwnd);
            Assert.Equal((game.Right, expanded.Top, (int)Math.Round(48 * dpi)), (strip.Right, strip.Top, strip.Width));
            Call(window, "SetCollapse", false, null);
            var restored = User32.ReadWindowRect(hwnd);
            Assert.Equal((game.Right, expanded.Top, expanded.Width), (restored.Right, restored.Top, restored.Width));
        }
        finally
        {
            tracker.Dispose();
            ((Window)Get(window, "_overlay")!).Close();
            directory.Delete(true);
        }
    });

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void FollowingCollapseAndHeightReportsNeverRestoreStaleWidth(double scale) => OnSta(() =>
    {
        var directory = Directory.CreateTempSubdirectory("SidebarSizing-");
        var api = HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory.FullName)
            .GetAwaiter().GetResult();
        var window = new MainWindow(false, directory.FullName);
        Set(window, "_api", api);
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var dpi = User32.GetWindowDpiScale(hwnd);
        var work = User32.GetMonitorGeometry(0, 0).WorkArea;
        var game = work;
        var tracker = (WindowTrackerService)Get(window, "_tracker")!;
        typeof(WindowTrackerService).GetField("_getViewport", Private)!
            .SetValue(tracker, (Func<IntPtr, RECT?>)(_ => game));
        var widths = new List<int>();
        var source = HwndSource.FromHwnd(hwnd);
        source.AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            // 捕获每次原生尺寸变化，不能只检查调用结束时已被改回的最终宽度。
            if (message == 0x0047) widths.Add(User32.ReadWindowRect(h).Width);
            return IntPtr.Zero;
        });
        try
        {
            Settings(1);
            Call(window, "UpdateGame", game);
            Call(window, "SetCollapse", true, 100d);
            Settings(scale);
            Call(window, "UpdateGame", game);
            var expected = Math.Min((int)Math.Round(380 * scale * dpi), work.Width);
            Assert.Equal(expected, User32.ReadWindowRect(hwnd).Width);
            widths.Clear();
            for (var i = 0; i < 3; i++)
            {
                Call(window, "UpdateGame", game);
                Call(window, "SetCollapse", true, 100d + i * 10);
            }
            Assert.All(widths, width => Assert.Equal(expected, width));
            Call(window, "SetCollapse", false, null);
            Assert.Equal(expected, User32.ReadWindowRect(hwnd).Width);
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            widths.Clear();
            // 拖边完成后保存的新宽度仍用于后续跟随；移动替身视口不改变宽度。
            Set(window, "_dock", new DockSettings(420));
            game.Top += 10;
            game.Bottom += 10;
            Call(window, "UpdateGame", game);
            expected = Math.Min((int)Math.Round(420 * scale * dpi), work.Width);
            Assert.All(widths, width => Assert.Equal(expected, width));
            Assert.Equal(expected, User32.ReadWindowRect(hwnd).Width);
            Assert.Equal(work.Top + 10 + (int)Math.Round(work.Height * .15), User32.ReadWindowRect(hwnd).Top);
        }
        finally
        {
            // 隐藏 HWND 随 STA 线程退出释放，不调用依赖 OCR 初始化的产品关闭流程。
            tracker.Dispose();
            ((Window)Get(window, "_overlay")!).Close();
            directory.Delete(true);
        }

        void Settings(double value) => typeof(HostApi).GetProperty(nameof(HostApi.Settings))!
            .SetValue(api, api.Settings with { UiScale = JsonSerializer.SerializeToElement(value), FollowGame = true });
    });

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object? Get(MainWindow window, string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(window);
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
    private static void Call(MainWindow window, string name, params object?[] args)
        => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
