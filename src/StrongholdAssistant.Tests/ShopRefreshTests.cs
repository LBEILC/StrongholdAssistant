using ASPAssistant.Core.Services;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Tests;

public class ShopRefreshTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void DiscardsOldCaptureEvenWhenRecognitionFinishesAfterDelay()
    {
        var clock = new Clock();
        var gate = new ShopRefreshWindow(clock);
        var old = gate.CaptureStarted();
        ShopItem[] previous = [new() { Id = "0:山", Name = "山" }];
        Assert.True(gate.TryUpdate(previous, old, true, out var stable));
        Assert.Single(stable);
        clock.Now += TimeSpan.FromMilliseconds(1);
        gate.Refresh();
        Assert.Equal(1, gate.Generation);
        clock.Now += TimeSpan.FromMilliseconds(499);
        var early = gate.CaptureStarted();
        Assert.False(gate.Accept(early));
        Assert.False(gate.TryUpdate(previous, early, true, out stable));
        Assert.Empty(stable);
        clock.Now += TimeSpan.FromMilliseconds(1);
        Assert.True(gate.Accept(gate.CaptureStarted()));
        Assert.False(gate.Accept(old));
        Assert.False(gate.Accept(early));
        ShopItem[] next = [new() { Id = "1:角峰", Name = "角峰" }];
        Assert.True(gate.TryUpdate(next, gate.CaptureStarted(), true, out stable));
        Assert.Equal("角峰", Assert.Single(stable).Name);
        gate.Refresh();
        Assert.Equal(2, gate.Generation);
        Assert.False(gate.Accept(gate.CaptureStarted()));
    }
}
