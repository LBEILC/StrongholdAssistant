using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public sealed class CaptureVisibilityTests
{
    [Theory]
    [InlineData(1800, false, true)]
    [InlineData(1920, false, false)]
    [InlineData(1800, true, false)]
    [InlineData(1920, true, false)]
    public void HideOnlyExpandedOverlappingSidebar(int left, bool collapsed, bool expected)
    {
        var client = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var sidebar = new RECT { Left = left, Top = 100, Right = left + 380, Bottom = 900 };
        Assert.Equal(expected, WindowGeometry.ShouldHideForCapture(sidebar, client, collapsed));
    }
}
