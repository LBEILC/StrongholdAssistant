using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class GameSourceSelectionTests
{
    [Theory]
    [InlineData("mumu")]
    [InlineData("ldplayer")]
    [InlineData("auto")]
    public void ManualAddressUsesOnlyRequestedDeviceWithoutReusingInstanceConfig(string source)
    {
        var inspected = new List<string>();
        var result = GameSourceSelection.FindManual(source, "android.local:5555", [],
            [new("MuMu", "mumu-adb", "old-mumu", "old-config"),
             new("雷电", "ld-adb", "emulator-5554", "old-config", "ldplayer")
                 { Window = (IntPtr)3, RenderWindow = (IntPtr)4 }],
            _ => throw new Exception("已有 adb，不应使用进程回退"), device =>
            {
                inspected.Add(device.Serial);
                Assert.Equal("{}", device.Config);
                return device with { DisplayId = 3 };
            });
        Assert.Equal(new[] { "android.local:5555" }, inspected);
        Assert.Equal("android.local:5555", result.Device!.Serial);
        Assert.Equal(source == "mumu" ? "mumu-adb" : "ld-adb", result.Device.AdbPath);
        Assert.Equal(source == "mumu" ? IntPtr.Zero : (IntPtr)3, result.Window);
        Assert.Null(result.Problem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualAddressUsesProcessAdbAndReportsConnectionFailure(bool fail)
    {
        const string address = "127.0.0.1:16384";
        var calls = new List<string>();
        var result = GameSourceSelection.FindManual("auto", address, [], [],
            _ => new("MuMu", "adb", "", "{}"), device => EmulatorDisplay.Inspect(device, (_, args) =>
            {
                calls.Add(string.Join(" ", args));
                return fail ? (1, [], "device offline") : (0, System.Text.Encoding.UTF8.GetBytes("""
                    Window #0 Window{111 u0 com.hypergryph.arknights/.Main}:
                      mDisplayId=0 mHasSurface=true
                    """), "");
            }, requireConnection: true));
        Assert.Equal(new[] { $"connect {address}", $"-s {address} shell dumpsys window windows" }, calls);
        Assert.Equal("mumu", result.Source);
        Assert.Equal(IntPtr.Zero, result.Window);
        if (fail)
        {
            Assert.Null(result.Device);
            Assert.Equal("连不上你填的 ADB 地址 127.0.0.1:16384，请确认模拟器开着、地址和端口对，用 adb devices 能看到它", result.Problem);
        }
        else
        {
            Assert.Equal(address, result.Device!.Serial);
            Assert.Null(result.Problem);
        }
    }

    [Fact]
    public void ManualAddressWithoutBundledAdbReportsMissingAdb()
    {
        var result = GameSourceSelection.FindManual("ldplayer", "emulator-5554", [],
            [new("MuMu", "adb", "old", "{}")], _ => null,
            _ => throw new Exception("没有 adb 时不应探测"));
        Assert.Equal("没找到模拟器自带的 adb，请先打开模拟器", result.Problem);
        Assert.Null(result.Device);
    }

    [Theory]
    [InlineData("auto", false, "ldplayer", null)]
    [InlineData("ldplayer", true, "ldplayer", null)]
    [InlineData("auto", true, null, "找到 2 个模拟器实例，请在设置里选一个")]
    public void SelectsAcrossEmulatorKinds(string source, bool mumuRunning, string? selectedKind, string? problem)
    {
        var result = GameSourceSelection.Find(source, "", () => IntPtr.Zero,
            () => [new((IntPtr)2, "MuMu")], () => [
                new("MuMu", "adb.exe", "mumu", "{}") { DisplayId = mumuRunning ? 0 : null },
                new("雷电模拟器", "adb.exe", "emulator-5554", "{}", "ldplayer")
                    { DisplayId = 0, Window = (IntPtr)3, RenderWindow = (IntPtr)4 }]);
        Assert.Equal(selectedKind, result.Device?.Kind);
        Assert.Equal(problem, result.Problem);
        if (selectedKind == "ldplayer") Assert.Equal((IntPtr)3, result.Window);
    }

    [Fact]
    public void LdPlayerSourceDoesNotSelectMumu()
    {
        var result = GameSourceSelection.Find("ldplayer", "", () => throw new Exception(),
            () => throw new Exception(), () => [new("MuMu", "adb", "mumu", "{}") { DisplayId = 0 }]);
        Assert.Null(result.Device);
        Assert.Null(result.Source);
    }

    [Theory]
    [InlineData("auto", true, 2, "", "pc", null, null)]
    [InlineData("pc", false, 1, "", null, null, null)]
    [InlineData("auto", false, 1, "", "mumu", "device-0", null)]
    [InlineData("mumu", true, 2, "device-1", "mumu", "device-1", null)]
    [InlineData("mumu", false, 2, "", "mumu", null, "找到 2 个模拟器实例，请在设置里选一个")]
    [InlineData("auto", false, 0, "", "mumu", null, GameSourceSelection.AdbProblem)]
    [InlineData("mumu", false, 1, "missing", "mumu", null, GameSourceSelection.AdbProblem)]
    public void SelectsSourceAndReportsProblem(string source, bool hasPc, int count, string serial,
        string? expectedSource, string? expectedSerial, string? problem)
    {
        var result = GameSourceSelection.Find(source, serial, () => hasPc ? (IntPtr)1 : IntPtr.Zero,
            () => [new((IntPtr)2, "MuMu")],
            () => Enumerable.Range(0, count).Select(i => new EmulatorDevice("MuMu", "adb.exe", $"device-{i}", "{}") { DisplayId = 0 }).ToArray());
        Assert.Equal(expectedSource, result.Source);
        Assert.Equal(expectedSerial, result.Device?.Serial);
        Assert.Equal(problem, result.Problem);
        Assert.Equal(expectedSource == "pc" ? (IntPtr)1 : expectedSource == "mumu" ? (IntPtr)2 : IntPtr.Zero, result.Window);
    }

    [Theory]
    [InlineData("MuMu 2", 3)]
    [InlineData("MuMu 3", 2)]
    public void MatchesTitleOrUsesFirstWindow(string deviceName, int expected)
    {
        var result = GameSourceSelection.Find("mumu", "", () => throw new Exception("不应查找 PC"),
            () => [new((IntPtr)2, "MuMu 1"), new((IntPtr)3, "MuMu 2")],
            () => [new(deviceName, "adb.exe", "device", "{}") { DisplayId = 0 }]);
        Assert.Equal((IntPtr)expected, result.Window);
    }

    [Theory]
    [InlineData("", 3, "game", null)]
    [InlineData("", null, null, GameSourceSelection.GameMissingProblem)]
    [InlineData("desktop", 3, null, GameSourceSelection.GameMissingProblem)]
    public void SelectsOnlyRunningGameAndHonorsExplicitSerial(string serial, int? display,
        string? expectedSerial, string? problem)
    {
        var result = GameSourceSelection.Find("mumu", serial, () => IntPtr.Zero,
            () => [new((IntPtr)2, "MuMu")],
            () => [new("MuMu", "adb.exe", "desktop", "{}"),
                new("MuMu", "adb.exe", "game", "{}") { DisplayId = display }]);
        Assert.Equal(expectedSerial, result.Device?.Serial);
        Assert.Equal(problem, result.Problem);
    }
}
