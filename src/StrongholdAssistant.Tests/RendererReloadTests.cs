using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class RendererReloadTests
{
    [Fact]
    public void ReloadsAtMostThreeTimesInAnyRollingMinute()
    {
        var reloads = new Queue<long>();
        Assert.True(MainWindow.TryReserveRendererReload(reloads, 0));
        Assert.True(MainWindow.TryReserveRendererReload(reloads, 10_000));
        Assert.True(MainWindow.TryReserveRendererReload(reloads, 20_000));
        Assert.False(MainWindow.TryReserveRendererReload(reloads, 59_999));
        Assert.True(MainWindow.TryReserveRendererReload(reloads, 60_000));
        Assert.False(MainWindow.TryReserveRendererReload(reloads, 60_001));
    }
}
