using ASPAssistant.Core;
using Microsoft.Web.WebView2.Core;

namespace StrongholdAssistant.Host;

internal static class StartupDiagnostics
{
    internal static void LogCrash(Exception exception) => AppLogger.Error("Crash", exception.ToString());

    internal static bool IsWebView2Available(Func<string>? getVersion = null)
    {
        try
        {
            return !string.IsNullOrWhiteSpace((getVersion ?? (() => CoreWebView2Environment.GetAvailableBrowserVersionString()))());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }
}
