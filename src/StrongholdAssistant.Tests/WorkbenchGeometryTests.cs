using System.Windows;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public class WorkbenchGeometryTests
{
    [Fact]
    public void CentersFirstWindowWithUiScale()
    {
        var actual = WorkbenchGeometry.Calculate(new Rect(100, 50, 2400, 1400), 1.5);
        Assert.Equal(new FreeWindowBounds(340, 150, 1920, 1200), actual);
    }

    [Theory]
    [InlineData(1100, 700)]
    [InlineData(800, 600)]
    public void FitsSmallWorkArea(double width, double height)
    {
        var actual = WorkbenchGeometry.Calculate(new Rect(0, 0, width, height), 2);
        Assert.Equal(new FreeWindowBounds(0, 0, width, height), actual);
    }

    [Fact]
    public void AppliesMinimumSizeToSavedWindow()
    {
        var actual = WorkbenchGeometry.Calculate(new Rect(0, 0, 1920, 1080), 1,
            new FreeWindowBounds(100, 100, 300, 200));
        Assert.Equal(new FreeWindowBounds(100, 100, 960, 640), actual);
    }
}
