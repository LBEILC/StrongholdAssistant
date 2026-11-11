using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class InstallerUpdateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkerSelectsMatchingPackageAndUpdateRoute(bool installed)
    {
        var directory = Directory.CreateTempSubdirectory("StrongholdAssistant-RouteTest-").FullName;
        try
        {
            if (installed) File.WriteAllText(Path.Combine(directory, "installed.json"), "{ \"installer\": \"inno\" }");
            var service = new UpdateService(new Version(0, 2, 2), directory);
            service.IsInstalled.Should().Be(installed);
            var assets = new[] { "StrongholdAssistant-v0.3.0-setup.exe", "StrongholdAssistant-v0.3.0-win-x64.zip" };
            service.SelectPackageAsset(new Version(0, 3, 0), assets, "https://example.org/release")
                .Should().Be(assets[installed ? 0 : 1]);
            var script = installed
                ? UpdateService.BuildInstallerUpdateScript(123, @"C:\temp\setup.exe", directory)
                : UpdateService.BuildUpdateScript(123, "source", directory, "app.exe", "staging", "backup", "log");
            if (installed)
            {
                script.Should().Contain("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER");
                script.Should().Contain("/DIR=\"").And.Contain("if ($code -ne 0) { exit $code }");
                script.IndexOf("WaitForExit()", StringComparison.Ordinal).Should().BeLessThan(script.IndexOf("$installer =", StringComparison.Ordinal));
                script.Should().NotContain("Rename-Item");
            }
            else script.Should().Contain("Rename-Item -LiteralPath $appDir");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void InstalledReleaseWithoutSetupReportsDownloadPageInsteadOfZip()
    {
        var directory = Directory.CreateTempSubdirectory("StrongholdAssistant-RouteTest-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "installed.json"), "{ \"installer\": \"inno\" }");
            var service = new UpdateService(new Version(0, 2, 2), directory);
            var select = () => service.SelectPackageAsset(new Version(0, 3, 0),
                ["StrongholdAssistant-v0.3.0-win-x64.zip"], "https://example.org/release");
            select.Should().Throw<InvalidDataException>().WithMessage("*setup*https://example.org/release*");
        }
        finally { Directory.Delete(directory, true); }
    }
}
