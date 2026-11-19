using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Tests;

public sealed class CaptureBorderDiagnosticTests
{
    [Theory]
    [InlineData(false, "DeniedByUser")]
    [InlineData(true, "Exception(UnauthorizedAccessException, 0x80070005)")]
    public async Task KeepsSystemCapabilityWhenAccessIsDeniedOrThrows(bool throws, string expected)
    {
        // 读取本机真实元数据，替换授权请求以便在无人值守环境复现拒绝和异常。
        var line = await Task.Run(() => CaptureBorderDiagnostic.Read(() =>
            throws ? throw new UnauthorizedAccessException() : "DeniedByUser"));

        var build = Environment.OSVersion.Version.Build;
        Assert.Equal($"WindowsBuild={build} IsBorderRequiredSupported={build >= 20348} BorderlessAccess={expected}", line);
    }
}
