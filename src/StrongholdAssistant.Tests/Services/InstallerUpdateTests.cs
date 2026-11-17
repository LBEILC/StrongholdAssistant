using ASPAssistant.Core.Services;
using FluentAssertions;
using System.Diagnostics;
using System.Text;

namespace ASPAssistant.Tests.Services;

public class InstallerUpdateTests
{
    [Fact]
    public async Task InstallerStartsOnlyAfterOldProcessExits()
    {
        var directory = Directory.CreateTempSubdirectory("StrongholdAssistant-InstallerWait-").FullName;
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        using var old = Process.Start(new ProcessStartInfo(shell,
            "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30")
            { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var script = UpdateService.BuildInstallerUpdateScript(old.Id, Path.Combine(directory, "setup.exe"), directory);
            // 用替身安装器记录执行时序，不安装软件、不弹出失败对话框。
            var prefix = $$"""
                function Start-Process {
                    param($FilePath, $ArgumentList, $WindowStyle, [switch]$PassThru, [switch]$Wait, $WorkingDirectory)
                    if ($Wait) {
                        if (Get-Process -Id {{old.Id}} -ErrorAction SilentlyContinue) { throw '旧进程还在运行' }
                        Write-Output 'installer-after-exit' | Out-File -LiteralPath (Join-Path $PSScriptRoot 'result.txt')
                        $result = [pscustomobject]@{ ExitCode = 0 }
                        $result | Add-Member ScriptMethod Dispose {}
                        return $result
                    }
                    Add-Content -LiteralPath (Join-Path $PSScriptRoot 'result.txt') 'restarted'
                }
                Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ready.txt') 'ready'

                """;
            script = script.Replace("Add-Type -AssemblyName System.Windows.Forms", "Write-Output $_; exit 1");
            var path = Path.Combine(directory, "update.ps1");
            await File.WriteAllTextAsync(path, prefix + script, new UTF8Encoding(true));
            using var updater = Process.Start(new ProcessStartInfo(shell,
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{path}\"")
                { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!File.Exists(Path.Combine(directory, "ready.txt")))
                    await Task.Delay(50, timeout.Token);
                await Task.Delay(300, timeout.Token);
                Assert.False(File.Exists(Path.Combine(directory, "result.txt")));
                old.Kill();
                await old.WaitForExitAsync(timeout.Token);
                await updater.WaitForExitAsync(timeout.Token);
                Assert.Equal(0, updater.ExitCode);
                Assert.Equal(new[] { "installer-after-exit", "restarted" },
                    await File.ReadAllLinesAsync(Path.Combine(directory, "result.txt")));
            }
            finally { if (!updater.HasExited) updater.Kill(entireProcessTree: true); }
        }
        finally
        {
            if (!old.HasExited) { old.Kill(); old.WaitForExit(); }
            Directory.Delete(directory, true);
        }
    }

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
                script.Should().Contain("/DIR=\"").And.Contain("if ($code -ne 0) { throw");
                script.IndexOf("WaitForExit(30000)", StringComparison.Ordinal).Should().BeLessThan(script.IndexOf("$installer =", StringComparison.Ordinal));
                script.Should().Contain("WaitForExit(30000)").And.Contain("MessageBox]::Show")
                    .And.Contain("高效雅雅·卫戍协议助手更新没有完成。重新运行这个安装包就能修好：$setupPath")
                    .And.Contain(@"$setupPath = 'C:\temp\setup.exe'").And.NotContain("Remove-Item");
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
