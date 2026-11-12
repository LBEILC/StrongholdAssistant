using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using ASPAssistant.Core.Models;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public class OverlayWindowTests
{
    [Fact]
    public void ScreenshotPointScalesToOffsetScreenViewport() => OnSta(() =>
    {
        var layer = new ShopMarkerLayer();
        var item = Item();
        item.CardRegion = (960d / 1920, 270d / 1080, .1, .2);
        // 屏幕上的画面区域为 (100, 200, 1280, 720)，DPI 为 150%。
        const double dpi = 1.5;
        layer.Update([item], 1280 / dpi, 720 / dpi, animate: false);
        var marker = Assert.Single(layer.Children.Cast<FrameworkElement>());
        Assert.Equal(740, 100 + System.Windows.Controls.Canvas.GetLeft(marker) * dpi);
        Assert.Equal(380, 200 + System.Windows.Controls.Canvas.GetTop(marker) * dpi);
    });

    [Fact]
    public void DisappearingMarkerFadesFor150MsBeforeRemoval() => OnSta(() =>
    {
        var layer = new ShopMarkerLayer();
        layer.Update([], 1920, 1080);
        layer.Update([Item()], 1920, 1080);
        Pump(220);
        var marker = Assert.IsAssignableFrom<FrameworkElement>(layer.Children[0]);
        layer.Update([], 1920, 1080);
        Assert.True(marker.HasAnimatedProperties);
        Pump(80);
        Assert.Single(layer.Children.Cast<UIElement>());
        Assert.InRange(marker.Opacity, .01, .999);
        Pump(130);
        Assert.Empty(layer.Children.Cast<UIElement>());
    });

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 2)]
    public void ChangingStyleCrossfadesAndResizeRebuildsWithoutEntrance(int from, int to) => OnSta(() =>
    {
        var layer = new ShopMarkerLayer();
        var item = Item();
        item.IsTracked = from != 0; item.IsSecondary = from == 0; item.IsKey = from == 2;
        layer.Update([item], 1920, 1080);
        var old = (FrameworkElement)layer.Children[0];
        item.IsTracked = to != 0; item.IsSecondary = to == 0; item.IsKey = to == 2;
        layer.Update([item], 1920, 1080);
        Assert.Equal(2, layer.Children.Count);
        var next = (FrameworkElement)layer.Children[1];
        Assert.True(old.HasAnimatedProperties);
        Assert.True(next.HasAnimatedProperties);
        Pump(80);
        Assert.InRange(old.Opacity, .01, .999);
        Assert.InRange(next.Opacity, .01, .999);
        Pump(140);
        Assert.Same(next, Assert.Single(layer.Children.Cast<UIElement>()));
        layer.Update([item], 1280, 720);
        var resized = (FrameworkElement)layer.Children[0];
        Assert.False(resized.HasAnimatedProperties);
        Assert.Equal(1, resized.Opacity);
        Assert.Equal(item.CardRegion.Width * 1280, resized.Width);
    });

    private static ShopItem Item() => new()
        { Id = "3:山", IsTracked = true, CardRegion = (.5, .75, .11, .23) };

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 不显示窗口；隐藏的呈现源仅驱动 WPF 动画时钟。
                using var source = new HwndSource(new HwndSourceParameters("标记动画测试")
                    { WindowStyle = unchecked((int)0x80000000), Width = 1, Height = 1 });
                action();
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
