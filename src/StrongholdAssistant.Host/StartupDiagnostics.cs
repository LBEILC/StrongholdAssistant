using ASPAssistant.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace StrongholdAssistant.Host;

internal enum WebView2Availability { Available, Missing, TooOld }

internal static class StartupDiagnostics
{
    // IsNonClientRegionSupportEnabled（ICoreWebView2Settings9）在此运行时进入稳定版。
    // https://learn.microsoft.com/microsoft-edge/webview2/release-notes/sdk/1-0-2420-47
    internal const string MinimumWebView2Version = "123.0.2420.47";

    internal static void LogCrash(Exception exception) => AppLogger.Error("Crash", exception.ToString());

    internal static Version GetWindowsVersion()
    {
        // 注册表中的实际系统版本不受 exe 兼容模式影响，UBR 是累积更新修订号。
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")!;
        return new Version((int)key.GetValue("CurrentMajorVersionNumber")!,
            (int)key.GetValue("CurrentMinorVersionNumber")!,
            int.Parse((string)key.GetValue("CurrentBuildNumber")!), (int)key.GetValue("UBR")!);
    }

    internal static void LogStartupEnvironment(Version windowsVersion, string? runtimeVersion, string userDataFolder)
    {
        using var identity = WindowsIdentity.GetCurrent();
        AppLogger.Info("Startup", $"Windows={windowsVersion}; WebView2={runtimeVersion ?? "missing"}; " +
            $"UserDataFolder={userDataFolder}; Elevated={ProcessElevation.Current}; " +
            $"BuiltInAdministrator={identity.User!.Value.EndsWith("-500", StringComparison.Ordinal)}");
    }

    internal static bool IsWebView2StartupFailure(Exception exception) =>
        exception is COMException && exception.HResult == unchecked((int)0x80010108);

    internal static bool IsWindowsUpdateNeeded(Version version) =>
        version.Major == 10 && version.Minor == 0 && version.Build == 17763 && version.Revision is >= 0 and < 1000;

    internal static string WebView2StartupFailureMessage(Version windowsVersion)
    {
        var steps = new List<string>
        {
            "右键助手的 exe → 属性 → 兼容性，取消「以兼容模式运行这个程序」；",
            "重启电脑后再打开助手；",
            "看看 360、火绒、电脑管家等有没有拦截 msedgewebview2.exe，加进信任；",
            "重装 WebView2 运行时（点「是」打开微软官方下载）；"
        };
        const string update = "系统很久没更新、或用的是精简版系统时，WebView2 可能起不来：先装 Windows 更新再试。";
        steps.Insert(IsWindowsUpdateNeeded(windowsVersion) ? 0 : steps.Count, update);
        return "助手的界面组件（Microsoft Edge WebView2）没能启动。可以按顺序试：\n" +
            string.Join("\n", steps.Select((step, index) => $"{index + 1}. {step}")) +
            "\n还是不行请把日志文件夹里的 host.log 发到 QQ 群 1132134217。";
    }

    internal static (WebView2Availability Status, string? Version) IsWebView2Available(Func<string>? getVersion = null)
    {
        try
        {
            var version = (getVersion ?? (() => CoreWebView2Environment.GetAvailableBrowserVersionString()))();
            if (string.IsNullOrWhiteSpace(version)) return (WebView2Availability.Missing, null);
            return (CoreWebView2Environment.CompareBrowserVersions(version, MinimumWebView2Version) < 0
                ? WebView2Availability.TooOld : WebView2Availability.Available, version);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return (WebView2Availability.Missing, null);
        }
    }

    internal static bool IsWebView2InterfaceUnavailable(Exception exception) =>
        exception is InvalidCastException && exception.Message.Contains("Microsoft.Web.WebView2", StringComparison.Ordinal);
}
