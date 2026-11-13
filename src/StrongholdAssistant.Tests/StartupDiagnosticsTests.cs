using ASPAssistant.Core;
using FluentAssertions;
using Microsoft.Web.WebView2.Core;
using StrongholdAssistant.Host;

namespace ASPAssistant.Tests;

public class StartupDiagnosticsTests
{
    [Fact]
    public void LogCrash_WritesErrorWithStackAndInnerException()
    {
        var directory = Directory.CreateTempSubdirectory("StrongholdAssistant-CrashTest-");
        try
        {
            AppLogger.Initialize(directory.FullName);
            try
            {
                throw new InvalidOperationException("启动失败测试", new IOException("内层异常测试"));
            }
            catch (Exception exception)
            {
                StartupDiagnostics.LogCrash(exception);
            }
            var log = File.ReadAllText(Path.Combine(directory.FullName, "host.log"));
            log.Should().Contain("[ERROR] [Crash]").And.Contain("System.InvalidOperationException")
                .And.Contain("启动失败测试").And.Contain("System.IO.IOException").And.Contain("内层异常测试")
                .And.Contain(nameof(LogCrash_WritesErrorWithStackAndInnerException));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void WebView2_MissingRuntimeIsUnavailable()
    {
        StartupDiagnostics.IsWebView2Available(() => throw new WebView2RuntimeNotFoundException()).Status.Should().Be(WebView2Availability.Missing);
        StartupDiagnostics.IsWebView2Available(() => "").Status.Should().Be(WebView2Availability.Missing);
    }

    [Theory]
    [InlineData("123.0.2420.46", false)]
    [InlineData(StartupDiagnostics.MinimumWebView2Version, true)]
    [InlineData("142.0.3595.94", true)]
    public void WebView2_ChecksMinimumVersion(string version, bool available)
    {
        var result = StartupDiagnostics.IsWebView2Available(() => version);
        result.Status.Should().Be(available ? WebView2Availability.Available : WebView2Availability.TooOld);
        result.Version.Should().Be(version);
    }

    [Fact]
    public void WebView2_InterfaceFailureRequiresCastExceptionAndNamespace()
    {
        const string message = "Unable to cast to Microsoft.Web.WebView2.Core.Raw.ICoreWebView2Settings9.";
        StartupDiagnostics.IsWebView2InterfaceUnavailable(new InvalidCastException(message)).Should().BeTrue();
        StartupDiagnostics.IsWebView2InterfaceUnavailable(new InvalidOperationException(message)).Should().BeFalse();
        StartupDiagnostics.IsWebView2InterfaceUnavailable(new InvalidCastException("其他接口")).Should().BeFalse();
    }
}
