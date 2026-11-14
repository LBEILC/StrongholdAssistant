using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class EmulatorDisplayTests
{
    [Fact]
    public void FindsLdPlayer14GameOnMainDisplay()
    {
        // 调研报告第一节记录的雷电 14 WindowState 片段。
        Assert.Equal(0, EmulatorDisplay.FindGameDisplay("""
            Window #7 Window{111 u0 com.hypergryph.arknights/com.u8.sdk.U8UnityContext}:
              mDisplayId=0
              mHasSurface=true
            """));
    }

    [Fact]
    public void FailedDumpsysFallsBackToMainDisplayAndRemainsSelectable()
    {
        var device = EmulatorDisplay.Inspect(new("MuMu", "adb", "127.0.0.1:16384", ""),
            (_, _) => (1, [], "device not found"));

        Assert.Equal(0, device.DisplayId);
        Assert.False(device.ProbeFailed);
        var selected = GameSourceSelection.Find("mumu", "", () => IntPtr.Zero,
            () => [new((IntPtr)1, "MuMu")], () => [device]);
        Assert.Equal(device, selected.Device);
        Assert.Null(selected.Problem);
    }

    [Theory]
    [InlineData("127.0.0.1:16384", true)]
    [InlineData("emulator-5554", false)]
    public void ConnectsNetworkSerialBeforeDumpsys(string serial, bool network)
    {
        var calls = new List<string>();
        var device = EmulatorDisplay.Inspect(new("MuMu", "adb", serial, ""), (_, arguments) =>
        {
            calls.Add(string.Join(" ", arguments));
            // connect 的结果不阻止 dumpsys。
            if (arguments[0] == "connect") return (1, [], "connection failed");
            return (0, System.Text.Encoding.UTF8.GetBytes("""
                Window #0 Window{111 u0 com.hypergryph.arknights/.Main}:
                  mDisplayId=3 mHasSurface=true
                """), "");
        });

        var expected = new List<string>();
        if (network) expected.Add($"connect {serial}");
        expected.Add($"-s {serial} shell dumpsys window windows");
        Assert.Equal(expected, calls);
        Assert.Equal(3, device.DisplayId);
        Assert.False(device.ProbeFailed);
    }

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
        Assert.Equal(display, EmulatorDisplay.FindGameDisplay(dump));
    }

    [Fact]
    public void DoesNotUseDesktopWhenGameHasNoWindow()
    {
        Assert.Null(EmulatorDisplay.FindGameDisplay("""
              Window #0 Window{1111111 u0 com.mumu.launcher/.Launcher}:
                mDisplayId=0 rootTaskId=1 mSession=Session{111}
                mHasSurface=true isReadyForDisplay()=true
              mLastFocus=Window{2222222 u0 com.hypergryph.arknights/com.u8.sdk.U8UnityContext}
            """));
    }
}
