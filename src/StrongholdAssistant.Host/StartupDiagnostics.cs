using ASPAssistant.Core;
using Microsoft.Web.WebView2.Core;

namespace StrongholdAssistant.Host;

internal enum WebView2Availability { Available, Missing, TooOld }

internal static class StartupDiagnostics
{
    // IsNonClientRegionSupportEnabled（ICoreWebView2Settings9）在此运行时进入稳定版。
    // https://learn.microsoft.com/microsoft-edge/webview2/release-notes/sdk/1-0-2420-47
    internal const string MinimumWebView2Version = "123.0.2420.47";

    internal static void LogCrash(Exception exception) => AppLogger.Error("Crash", exception.ToString());

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
