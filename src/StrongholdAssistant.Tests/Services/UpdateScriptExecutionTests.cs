// 2026-11-11：适配本项目的更新包与环境变量。
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

[Collection("Update environment")]
public class UpdateScriptExecutionTests
{
    private static readonly string PowerShell = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    [Theory]
    [InlineData("中文 空格 & [目录] O'Neil $测试")]
    public async Task Update_ReleasesInheritedWorkingDirectoryAndStartsNewVersion(string folder)
    {
        var root = Directory.CreateTempSubdirectory("StrongholdAssistant-UpdateTests-").FullName;
        try
        {
            var (app, source) = await PrepareFilesAsync(root, folder);
            var result = await RunUpdateAsync(root, app, source);
            result.ExitCode.Should().Be(0, result.Output);
            (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("new");
            File.Exists(Path.Combine(app, "new-only.txt")).Should().BeTrue();
            File.Exists(Path.Combine(app, "old-only.txt")).Should().BeFalse();
            await AssertStartedAsync(app, "new");
            (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should().Contain("activated new version");
            Directory.Exists(Path.Combine(root, "backup")).Should().BeFalse();
        }
        finally { DeleteTestDirectory(root); }
    }

    [Fact]
    public async Task NewVersionLaunchFails_RestoresCompleteOldDirectory()
    {
        var root = Directory.CreateTempSubdirectory("StrongholdAssistant-UpdateTests-").FullName;
        try
        {
            var (app, source) = await PrepareFilesAsync(root, "rollback");
            const string failNewLaunch = """
                function Start-Version([string]$path) {
                    if ((Get-Content -LiteralPath (Join-Path $appDir 'version.txt') -Raw) -eq 'new') {
                        # 映射视图继续占用文件，关闭文件句柄后允许父目录改名。
                        $heldFile = [System.IO.File]::Open((Join-Path $appDir 'new-only.txt'), 'Open', 'ReadWrite', 'ReadWrite, Delete')
                        $script:heldMapping = [System.IO.MemoryMappedFiles.MemoryMappedFile]::CreateFromFile($heldFile, [Guid]::NewGuid().ToString('N'), 0, 'ReadWrite', 'None', $true)
                        $script:heldView = $script:heldMapping.CreateViewAccessor()
                        $heldFile.Dispose()
                        throw 'simulated new-version launch failure'
                    }
                    $start = New-Object System.Diagnostics.ProcessStartInfo
                    $start.FileName = $path
                    $start.WorkingDirectory = $appDir
                    $start.UseShellExecute = $false
                    $process = [System.Diagnostics.Process]::Start($start)
                    return $process
                }

                """;
            var result = await RunUpdateAsync(root, app, source, prefix: failNewLaunch);
            result.ExitCode.Should().Be(1, result.Output);
            (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("old",
                await File.ReadAllTextAsync(Path.Combine(root, "update.log")));
            File.Exists(Path.Combine(app, "old-only.txt")).Should().BeTrue();
            File.Exists(Path.Combine(app, "StrongholdAssistant.exe")).Should().BeTrue();
            File.Exists(Path.Combine(app, "new-only.txt")).Should().BeFalse();
            await AssertStartedAsync(app, "old");
            (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should().Contain("rolled back to previous version");
        }
        finally { DeleteTestDirectory(root); }
    }

    private static async Task<(string App, string Source)> PrepareFilesAsync(string root, string folder)
    {
        var compiler = Path.Combine(root, "compile.ps1");
        await File.WriteAllTextAsync(compiler, """
            $ErrorActionPreference = 'Stop'
            Add-Type -OutputType WindowsApplication -OutputAssembly (Join-Path $PSScriptRoot 'test-host.exe') -TypeDefinition @'
            using System;
            using System.IO;
            public static class TestUpdateHost {
                public static void Main(string[] args) {
                    var root = AppDomain.CurrentDomain.BaseDirectory;
                    if (args.Length > 0 && args[0] != "--keep-identity") {
                        File.WriteAllText(Path.Combine(root, "held.ready"), "ready");
                        var wait = System.Diagnostics.Stopwatch.StartNew();
                        while (!File.Exists(Path.Combine(root, "release.hold")) && wait.ElapsedMilliseconds < 30000)
                            System.Threading.Thread.Sleep(25);
                        return;
                    }
                    File.WriteAllText(Path.Combine(root, "started.log"),
                        File.ReadAllText(Path.Combine(root, "version.txt")) + "|" + Environment.CurrentDirectory);
                    if (File.ReadAllText(Path.Combine(root, "version.txt")) == "new") {
                        var modePath = Path.Combine(root, "startup-mode.txt");
                        var mode = File.Exists(modePath) ? File.ReadAllText(modePath) : "ready";
                        if (mode == "exit") Environment.Exit(17);
                        var resultPath = Environment.GetEnvironmentVariable("STRONGHOLD_UPDATE_STARTUP_RESULT");
                        if (mode == "failed") {
                            File.WriteAllText(resultPath + ".tmp", "failed\nmock startup failure", System.Text.Encoding.UTF8);
                            File.Move(resultPath + ".tmp", resultPath);
                            Environment.Exit(18);
                        }
                        File.WriteAllText(Path.Combine(root, "new.running"), "running");
                        if (mode == "ready") {
                            File.WriteAllText(resultPath + ".tmp", "ready", System.Text.Encoding.UTF8);
                            File.Move(resultPath + ".tmp", resultPath);
                        }
                        var wait = System.Diagnostics.Stopwatch.StartNew();
                        while (!File.Exists(Path.Combine(root, "release.new")) && wait.ElapsedMilliseconds < 30000)
                            System.Threading.Thread.Sleep(25);
                        File.WriteAllText(Path.Combine(root, "new.exited"), "exited");
                    }
                }
            }
            '@
            """);
        var result = await RunPowerShellAsync(compiler, root);
        result.ExitCode.Should().Be(0, result.Output);
        var app = Directory.CreateDirectory(Path.Combine(root, folder)).FullName;
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        foreach (var directory in new[] { app, source })
            File.Copy(Path.Combine(root, "test-host.exe"), Path.Combine(directory, "StrongholdAssistant.exe"));
        await File.WriteAllTextAsync(Path.Combine(app, "version.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(app, "old-only.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(source, "version.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(source, "new-only.txt"), "new");
        return (app, source);
    }

    private static async Task<(int ExitCode, string Output)> RunUpdateAsync(
        string root, string app, string source, int pid = int.MaxValue, bool shortWait = false, string prefix = "")
    {
        var script = UpdateService.BuildUpdateScript(pid, source, app, Path.Combine(app, "StrongholdAssistant.exe"),
            Path.Combine(root, "staging"), Path.Combine(root, "backup"), Path.Combine(root, "update.log"));
        if (shortWait) script = script.Replace("$timeout = 300", "$timeout = 1")
            .Replace("$startupTimeout = 60", "$startupTimeout = 1");
        var path = Path.Combine(root, "update.ps1");
        script = script.Replace("Write-Log \"update started (pid=$appPid)\"", prefix + "\nWrite-Log \"update started (pid=$appPid)\"");
        await File.WriteAllTextAsync(path, script, new UTF8Encoding(true));
        var result = await RunPowerShellAsync(path, app);
        if (File.Exists(Path.Combine(app, "new.running")))
        {
            await WaitForFileAsync(Path.Combine(app, "new.exited"));
        }
        return result;
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string script, string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo(PowerShell)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(args))
            start.ArgumentList.Add(argument);
        start.Environment["STRONGHOLD_UPDATE_SILENT"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); // Only this test's own helper process.
            await process.WaitForExitAsync();
            throw new TimeoutException("The isolated update test did not finish.");
        }
        // 更新脚本已退出，但测试宿主继承着输出管道。先释放宿主再读取到 EOF，
        // 避免等待其 30 秒自停计时；真实更新、就绪握手与全部断言保持不变。
        if (File.Exists(Path.Combine(workingDirectory, "new.running")))
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "release.new"), "release");
        return (process.ExitCode, await output + await error);
    }

    private static async Task AssertStartedAsync(string app, string version)
    {
        var path = Path.Combine(app, "started.log");
        await WaitForFileAsync(path);
        (await File.ReadAllTextAsync(path)).Should().Be(version + "|" + app);
    }

    private static async Task WaitForFileAsync(string path)
    {
        var timeout = Stopwatch.StartNew();
        while (!File.Exists(path) && timeout.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(25);
        File.Exists(path).Should().BeTrue("the test-only helper must write its result");
    }

    private static void DeleteTestDirectory(string root)
    {
        var path = Path.GetFullPath(root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(path).StartsWith("StrongholdAssistant-UpdateTests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Update test cleanup escaped its temporary directory.");
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Delete(path, true); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20) { Thread.Sleep(100); }
        }
    }
}
