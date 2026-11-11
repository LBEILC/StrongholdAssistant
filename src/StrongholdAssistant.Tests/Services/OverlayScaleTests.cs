using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class OverlayScaleTests
{
    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(3840, 2160, true)]
    public void OverlayUsesLeftClientArea(int width, int height, bool fullscreen)
    {
        var client = new RECT { Left = 100, Top = 50, Right = 100 + width, Bottom = 50 + height };
        var scale = WindowGeometry.UiScale(null, height, 1);
        Assert.True(WindowGeometry.NeedsOverlay(client, client, 1, 380 * scale, fullscreen));
        var bounds = WindowGeometry.OverlayBounds(client, 1, 380 * scale, null);
        Assert.Equal(client.Left, bounds.Left);
        Assert.Equal(client.Top + (int)Math.Round(height * .15), bounds.Top);
        Assert.Equal(client.Top + (int)Math.Round(height * .97), bounds.Bottom);
        Assert.Equal((int)(380 * scale), bounds.Width);
        var work = client; work.Right += (int)(380 * scale);
        Assert.False(WindowGeometry.NeedsOverlay(client, work, 1, 380 * scale, false));
    }

    [Fact]
    public void CollapsedAndExpandedWidthUseSameScale()
    {
        Assert.Equal(72, WindowGeometry.PanelWidth(400, 1.5, true));
        Assert.Equal(600, WindowGeometry.PanelWidth(400, 1.5, false));
    }

    [Fact]
    public void AutoScaleConvertsDpiAndClamps()
    {
        Assert.Equal(1, WindowGeometry.UiScale(null, null, 1));
        Assert.Equal(1, WindowGeometry.UiScale(null, 720, 1));
        Assert.Equal(1.5, WindowGeometry.UiScale(null, 2160, 4.0 / 3));
        Assert.Equal(2, WindowGeometry.UiScale(null, 4320, 1));
    }
}
