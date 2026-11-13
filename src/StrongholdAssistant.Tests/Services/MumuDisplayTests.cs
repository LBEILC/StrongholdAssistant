using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class MumuDisplayTests
{
    // 按 AOSP Android 12 WindowState.dump 格式构造；不是 MuMu 实机采集。
    // 来源与待采集步骤见 docs/research/100-mumu-display.md。
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void FindsGameDisplayWithoutUsingLauncherOrStaleWindow(int display)
    {
        var dump = $$"""
            WINDOW MANAGER WINDOWS (dumpsys window windows)
              Window #0 Window{1111111 u0 com.mumu.launcher/.Launcher}:
                mDisplayId=0 rootTaskId=1 mSession=Session{111}
                mHasSurface=true isReadyForDisplay()=true
              Window #1 Window{2222222 u0 com.hypergryph.arknights/com.u8.sdk.U8UnityContext}:
                mDisplayId=8 rootTaskId=2 mSession=Session{222}
                mHasSurface=false isReadyForDisplay()=false
              Window #2 Window{3333333 u0 com.hypergryph.arknights/com.u8.sdk.U8UnityContext}:
                mDisplayId={{display}} rootTaskId=3 mSession=Session{333}
                mHasSurface=true isReadyForDisplay()=true
            """;
        Assert.Equal(display, MumuDisplay.FindGameDisplay(dump));
    }

    [Fact]
    public void DoesNotUseDesktopWhenGameHasNoWindow()
    {
        Assert.Null(MumuDisplay.FindGameDisplay("""
              Window #0 Window{1111111 u0 com.mumu.launcher/.Launcher}:
                mDisplayId=0 rootTaskId=1 mSession=Session{111}
                mHasSurface=true isReadyForDisplay()=true
              mLastFocus=Window{2222222 u0 com.hypergryph.arknights/com.u8.sdk.U8UnityContext}
            """));
    }
}
