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
        StartupDiagnostics.IsWebView2Available(() => throw new WebView2RuntimeNotFoundException()).Should().BeFalse();
        StartupDiagnostics.IsWebView2Available(() => "").Should().BeFalse();
    }

    [Fact]
    public void WebView2_VersionIsAvailable() =>
        StartupDiagnostics.IsWebView2Available(() => "142.0.3595.94").Should().BeTrue();
}
