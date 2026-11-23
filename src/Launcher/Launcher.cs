using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

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
        bool forwardExit = Environment.GetEnvironmentVariable("STRONGHOLD_UPDATE_STARTUP_RESULT") != null
            || (args.Length > 0 && args[0] == "--check-recognition");
        Process process;
        try { process = Process.Start(start); }
        catch (Win32Exception ex)
        {
            if (!forwardExit) ShowFailure(host, IsHostMissing(host), ex.NativeErrorCode + " " + ex.Message);
            return 1;
        }
        using (process)
        {
            // 旧更新器检查启动器 PID；识别自检的调用方还需要宿主退出码。
            if (forwardExit)
            {
                process.WaitForExit();
                return process.ExitCode;
            }
            // 宿主主动退出用 0、1 或 2；Windows 异常和 apphost 错误码的高位为 1。
            if (process.WaitForExit(10000) && process.ExitCode < 0)
            {
                ShowFailure(host, false, "0x" + process.ExitCode.ToString("X8"));
                return 1;
            }
            return 0;
        }
    }

    private static bool IsHostMissing(string host)
    {
        // File.Exists 在访问被拒时也返回 false；只有系统确认路径不存在才提示恢复文件。
        try { File.GetAttributes(host); }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException || ex is IOException) { }
        return false;
    }

    private static int? ReadPolicyState()
    {
        try
        {
            return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\CI\Policy",
                "VerifiedAndReputablePolicyState", null) as int?;
        }
        catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
        {
            return null;
        }
    }

    private static void ShowFailure(string host, bool missing, string detail)
    {
        int? state = ReadPolicyState();
        bool smartControl = state == 1 || state == 2;
        string scenario = missing ? "主程序不见了" : smartControl ? "智能应用控制" : "被拦截";
        string version = "未知";
        try
        {
            version = FileVersionInfo.GetVersionInfo(Path.ChangeExtension(host, ".dll")).ProductVersion ?? version;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception) { }

        try
        {
            string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StrongholdAssistant", "logs");
            Directory.CreateDirectory(logs);
            File.AppendAllText(Path.Combine(logs, "launcher.log"), string.Format(
                "{0:yyyy-MM-dd HH:mm:ss} 版本={1} 情况={2} 原因={3} 智能应用控制状态={4}{5}",
                DateTime.Now, version, scenario, detail.Replace('\r', ' ').Replace('\n', ' '),
                state.HasValue ? state.Value.ToString() : "未知", Environment.NewLine));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }

        string message = missing
            ? @"助手没能启动：找不到主程序 app\StrongholdAssistant.exe，多半是被杀毒软件隔离了。

到 360、火绒、Windows 安全中心的「隔离区」把它恢复，把助手的安装目录加进信任，再重新安装一遍助手。"
            : smartControl
            ? @"助手没能启动：Windows 的「智能应用控制」拦下了助手的程序文件。助手暂时没有代码签名，会被它当成来路不明的程序。

解决办法：打开 设置 → 隐私和安全性 → Windows 安全中心 → 应用和浏览器控制 → 智能应用控制设置，改成「关」，再重新打开助手。

注意：在一些系统版本上，智能应用控制关掉后要重置系统才能再打开；杀毒软件照常工作，不受影响。"
            : @"助手没能启动：主程序没能正常运行，常见原因是被杀毒软件或系统策略拦截。可以按顺序试：
1. 到 360、火绒、Windows 安全中心的「隔离区」或「防护历史」看看有没有拦过 StrongholdAssistant，有就恢复，并把助手的安装目录加进信任；
2. 公司或学校的电脑可能有管理员策略，需要找管理员；
3. 重新安装一遍助手。

还是不行请把日志文件夹（%APPDATA%\StrongholdAssistant\logs）里的 launcher.log 发到 QQ 交流群 1131280375。";
        MessageBox.Show(message, "高效雅雅·卫戍协议助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
