using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class CrashGuardTests
{
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "crash-guard-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TwoAbnormalExitsWhileCapturingDisableWindowCapture()
    {
        var directory = NewDirectory();
        CrashGuard.Initialize(directory);
        CrashGuard.WgcStarting();
        // 没调用 NormalExit 就重新启动，相当于闪退。
        CrashGuard.Initialize(directory);
        Assert.False(CrashGuard.WgcDisabled);
        CrashGuard.WgcStarting();
        CrashGuard.Initialize(directory);
        Assert.True(CrashGuard.WgcDisabled);
        CrashGuard.Initialize(NewDirectory());
    }

    [Fact]
    public void CrashWhileLinkingAdbSwitchesToSafeScreencap()
    {
        var directory = NewDirectory();
        CrashGuard.Initialize(directory);
        CrashGuard.AdbStarting();
        CrashGuard.AdbLinked();
        CrashGuard.Initialize(directory);
        Assert.False(CrashGuard.AdbSafe);
        CrashGuard.AdbStarting();
        // 第一次截图前就没了，相当于连模拟器时闪退。
        CrashGuard.Initialize(directory);
        Assert.True(CrashGuard.AdbSafe);
        CrashGuard.Initialize(NewDirectory());
    }

    [Fact]
    public void NormalExitResetsTheCount()
    {
        var directory = NewDirectory();
        CrashGuard.Initialize(directory);
        CrashGuard.WgcStarting();
        CrashGuard.Initialize(directory);
        CrashGuard.WgcStarting();
        CrashGuard.NormalExit();
        CrashGuard.Initialize(directory);
        CrashGuard.WgcStarting();
        CrashGuard.Initialize(directory);
        Assert.False(CrashGuard.WgcDisabled);
        CrashGuard.Initialize(NewDirectory());
    }

    [Fact]
    public void GpuCrashDisablesHardwareAccelerationNextStart()
    {
        var directory = NewDirectory();
        CrashGuard.Initialize(directory);
        Assert.False(CrashGuard.GpuDisabled);
        CrashGuard.GpuCrashed();
        CrashGuard.Initialize(directory);
        Assert.True(CrashGuard.GpuDisabled);
        CrashGuard.Initialize(NewDirectory());
    }

    [Fact]
    public void OcrGpuFailureIsRememberedForNextStart()
    {
        var directory = NewDirectory();
        CrashGuard.Initialize(directory);
        Assert.False(CrashGuard.OcrCpu);
        CrashGuard.OcrGpuFailed();
        CrashGuard.Initialize(directory);
        Assert.True(CrashGuard.OcrCpu);
        CrashGuard.Initialize(NewDirectory());
    }
}
