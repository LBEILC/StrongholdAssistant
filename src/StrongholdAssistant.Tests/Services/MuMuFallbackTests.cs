using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

public class MuMuFallbackTests
{
    [Fact]
    public void FindsAdbBesideProcessOrInParentShellAndReturnsNullWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var main = Path.Combine(root, "nx_main");
        var shell = Path.Combine(root, "shell");
        Directory.CreateDirectory(main);
        Directory.CreateDirectory(shell);
        try
        {
            var process = Path.Combine(main, "MuMuNxMain.exe");
            Assert.Null(MuMuFallback.FindAdb(process));
            var shellAdb = Path.Combine(shell, "adb.exe");
            File.WriteAllText(shellAdb, "");
            Assert.Equal(shellAdb, MuMuFallback.FindAdb(process));
            var localAdb = Path.Combine(main, "adb.exe");
            File.WriteAllText(localAdb, "");
            Assert.Equal(localAdb, MuMuFallback.FindAdb(process));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void KeepsOnlyOnlineLoopbackDevices()
    {
        Assert.Equal(new[] { "127.0.0.1:16384", "127.0.0.1:16480" }, MuMuFallback.ParseDevices("""
            List of devices attached
            127.0.0.1:16384 device
            127.0.0.1:16416 offline
            127.0.0.1:16448 unauthorized
            127.0.0.1:16480 device product:test
            emulator-5554 device
            192.168.1.2:5555 device
            """));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UsesFallbackOnlyWhenToolkitHasNoMuMu(bool found)
    {
        EmulatorDevice[] toolkit = found ? [new("MuMu", "adb", "127.0.0.1:16384", "{}", "mumu")] : [];
        EmulatorDevice[] fallback = [new("MuMu 16416", "adb", "127.0.0.1:16416", "{}", "mumu")];
        var calls = 0;
        var result = MuMuFallback.UseIfMissing(toolkit, () => { calls++; return fallback; });
        Assert.Equal(found ? 0 : 1, calls);
        Assert.Same(found ? toolkit : fallback, result);
    }
}
