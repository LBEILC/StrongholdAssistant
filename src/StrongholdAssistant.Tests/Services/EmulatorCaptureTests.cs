using System.Text;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class EmulatorCaptureTests
{
    // 按 Android 12 LogicalDisplay.dumpLocked / DisplayInfo.toString 构造。
    private const string Displays = """
        Logical Displays: size=2
          Display 0:
            mDisplayId=0
            mBaseDisplayInfo=DisplayInfo{"Built-in Screen", displayId 0, uniqueId "local:100", app 1920 x 1080}
          Display 3:
            mDisplayId=3
            mBaseDisplayInfo=DisplayInfo{"External Screen", displayId 3, uniqueId "local:18446744073709551614", app 1920 x 1080}
        """;
    private const string Physical = "18446744073709551614";
    private static EmulatorDevice Device => new("MuMu", "adb", "test-serial", "") { DisplayId = 3 };

    [Fact]
    public void MapsOnlyExplicitLocalDeviceOfRequestedLogicalDisplay()
    {
        Assert.Equal(Physical, EmulatorDisplay.FindPhysicalDisplayId(Displays, 3));
        Assert.Null(EmulatorDisplay.FindPhysicalDisplayId(Displays, 9));
        Assert.Null(EmulatorDisplay.FindPhysicalDisplayId(Displays.Replace($"local:{Physical}", "virtual:package,1000,tab,0"), 3));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TriesMappedThenLogicalAndCachesFirstSuccess(bool mappedWorks)
    {
        var capture = new EmulatorCapture(_ => { });
        var calls = new List<string>();
        (int, byte[], string) Run(EmulatorDevice _, string[] args)
        {
            calls.Add(args[^1]);
            if (args[^1] == "display") return (0, Encoding.UTF8.GetBytes(Displays), "");
            return args[^1] == Physical && !mappedWorks ? (1, [], "invalid display") : (0, [1], "");
        }
        Assert.NotNull(capture.Capture(Device, Run, bytes => bytes.Length > 0, out _));
        Assert.NotNull(capture.Capture(Device, Run, bytes => bytes.Length > 0, out _));
        Assert.Equal(mappedWorks ? ["display", Physical, Physical] : new[] { "display", Physical, "3", "3" }, calls);
        calls.Clear();
        // 缓存方法失效后重新映射，所有截图失败也不使用默认截图或主屏。
        Assert.Null(capture.Capture(Device, (_, args) =>
        {
            calls.Add(args[^1]);
            return args[^1] == "display" ? (0, Encoding.UTF8.GetBytes(Displays), "") : (1, [], "failed");
        }, _ => true, out _));
        Assert.Equal(new[] { mappedWorks ? Physical : "3", "display", Physical, "3" }, calls);
    }

    [Fact]
    public void LogsEachFailureOnceWithContextAndTruncatedReason()
    {
        var logs = new List<string>();
        var capture = new EmulatorCapture(logs.Add);
        for (var i = 0; i < 2; i++)
            Assert.Null(capture.Capture(Device, (_, args) => args[^1] == "display"
                ? (0, Encoding.UTF8.GetBytes(Displays), "") : (7, [], new string('x', 220)), _ => true, out _));
        Assert.Equal(2, logs.Count);
        Assert.All(logs, line =>
        {
            Assert.Contains("serial=test-serial display=3 capture=ADB", line);
            Assert.Contains("exit=7", line);
            Assert.EndsWith(new string('x', 200), line);
            Assert.DoesNotContain(new string('x', 201), line);
        });
    }
}
