using System;
using System.Diagnostics;
using System.IO;

internal static class Launcher
{
    private static int Main(string[] args)
    {
        // Windows 首个参数是映像路径，保留其后原始命令行的引号、空参数和空白。
        string line = Environment.CommandLine;
        bool quoted = false;
        int end = 0;
        for (; end < line.Length; end++)
        {
            if (line[end] == '"') quoted = !quoted;
            if (!quoted && (line[end] == ' ' || line[end] == '\t')) break;
        }
        var host = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app", "StrongholdAssistant.exe");
        var start = new ProcessStartInfo(host, line.Substring(end)) { UseShellExecute = false };
        using (var process = Process.Start(start))
        {
            // 旧更新器检查启动器 PID；识别自检的调用方还需要宿主退出码。
            if (Environment.GetEnvironmentVariable("STRONGHOLD_UPDATE_STARTUP_RESULT") == null
                && (args.Length == 0 || args[0] != "--check-recognition")) return 0;
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
