using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class GameSourceSelectionTests
{
    [Theory]
    [InlineData("auto", true, 2, "", "pc", null, null)]
    [InlineData("pc", false, 1, "", null, null, null)]
    [InlineData("auto", false, 1, "", "mumu", "device-0", null)]
    [InlineData("mumu", true, 2, "device-1", "mumu", "device-1", null)]
    [InlineData("mumu", false, 2, "", "mumu", null, "找到 2 个 MuMu 实例，请在设置里选一个")]
    [InlineData("auto", false, 0, "", "mumu", null, GameSourceSelection.AdbProblem)]
    [InlineData("mumu", false, 1, "missing", "mumu", null, GameSourceSelection.AdbProblem)]
    public void SelectsSourceAndReportsProblem(string source, bool hasPc, int count, string serial,
        string? expectedSource, string? expectedSerial, string? problem)
    {
        var result = GameSourceSelection.Find(source, serial, () => hasPc ? (IntPtr)1 : IntPtr.Zero,
            () => [new((IntPtr)2, "MuMu")],
            () => Enumerable.Range(0, count).Select(i => new MumuDevice("MuMu", "adb.exe", $"device-{i}", "{}")).ToArray());
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
            () => [new(deviceName, "adb.exe", "device", "{}")]);
        Assert.Equal((IntPtr)expected, result.Window);
    }
}
