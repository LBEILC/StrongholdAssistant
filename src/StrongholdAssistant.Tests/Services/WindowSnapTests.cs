using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class WindowSnapTests
{
    [Theory]
    [InlineData(-833, 47, -833d, 47d)]
    [InlineData(-30, 47, 0d, 47d)]
    [InlineData(-30, 20, null, null)]
    [InlineData(40, -40, null, null)]
    public void FollowSnapsEachAxis(double x, double y, double? expectedX, double? expectedY)
    {
        var result = WindowGeometry.SnapOffset(new SidePanelOffset(x, y));
        Assert.Equal(expectedX, result?.X);
        Assert.Equal(expectedY, result?.Y);
    }

    [Theory]
    [InlineData("left", 1.0)]
    [InlineData("right", 1.5)]
    public void OverlaySnapsRelativeToDefaultBounds(string side, double scale)
    {
        var client = new RECT { Left = 100, Top = 50, Right = 1700, Bottom = 1051 };
        var bounds = WindowGeometry.OverlayBounds(client, scale, 380, null, side);
        var defaultY = (bounds.Top - client.Top) / scale;
        Assert.Equal(new SidePanelOffset(-833, defaultY + 47),
            WindowGeometry.SnapOverlayOffset(new(-833, defaultY + 47), client, scale, 380, side));
        Assert.Equal(new SidePanelOffset(0, defaultY + 47),
            WindowGeometry.SnapOverlayOffset(new(-30, defaultY + 47), client, scale, 380, side));
        Assert.Equal(new SidePanelOffset(-100, defaultY),
            WindowGeometry.SnapOverlayOffset(new(-100, defaultY + 20), client, scale, 380, side));
        Assert.Null(WindowGeometry.SnapOverlayOffset(new(-30, defaultY + 20), client, scale, 380, side));
        Assert.Null(WindowGeometry.SnapOverlayOffset(new(40, defaultY - 40), client, scale, 380, side));
    }
}
