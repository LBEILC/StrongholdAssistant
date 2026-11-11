using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public class OverlayWindowTests
{
    [Fact]
    public void Markers_IgnoreSmallDriftButFollowThresholdAndViewportChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new OverlayWindow { Opacity = 0, Topmost = false, Left = -10000, Top = -10000 };
                try
                {
                    void Resize(double width, double height)
                    {
                        window.Width = width; window.Height = height;
                        if (!window.IsVisible) window.Show();
                        window.Measure(new Size(width, height));
                        window.Arrange(new Rect(0, 0, width, height));
                    }
                    Resize(1920, 1080);
                    var item = new ShopItem { Id = "山@0", IsTracked = true, OcrRegion = (.5, .9, .05, .03) };
                    window.UpdateShopItems([item]);
                    var marker = Assert.IsAssignableFrom<FrameworkElement>(Assert.Single(((Canvas)window.Content).Children.Cast<UIElement>()));
                    (double, double) Position() => (Canvas.GetLeft(marker), Canvas.GetTop(marker));
                    void AssertFollows() => Assert.Equal(
                        WindowGeometry.ShopMarkerPosition(item.OcrRegion, window.ActualWidth, window.ActualHeight, marker.Width), Position());
                    AssertFollows();
                    var original = Position();
                    item.OcrRegion = (.5 + 5.0 / 1920, .9 - 5.0 / 1080, .05, .03);
                    window.UpdateShopItems([item]);
                    Assert.Equal(original, Position());
                    // 横向恰好 6 DIP、纵向超过阈值时都应跟随。
                    item.OcrRegion = (.5 + 6.0 / 1920, .9, .05, .03);
                    window.UpdateShopItems([item]);
                    AssertFollows();
                    Assert.NotEqual(original, Position());
                    item.OcrRegion = (.5, .9 + 7.0 / 1080, .05, .03);
                    window.UpdateShopItems([item]);
                    AssertFollows();
                    original = Position();
                    Resize(1922, 1080);
                    window.UpdateShopItems([item]);
                    AssertFollows();
                    Assert.NotEqual(original, Position());
                    original = Position();
                    Resize(1922, 1082);
                    window.UpdateShopItems([item]);
                    AssertFollows();
                    Assert.NotEqual(original, Position());
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
