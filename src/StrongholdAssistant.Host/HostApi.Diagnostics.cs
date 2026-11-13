using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ASPAssistant.Core.Interop;
using Microsoft.Win32;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private DiagnosticsStore _diagnostics = null!;

    public string ReportRecognitionFailure(byte[]? screenshot, string reason, double? scale, string[] steps)
    {
        try { return _diagnostics.SaveFailure(screenshot, reason, Connection.Source, scale, steps); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            ASPAssistant.Core.AppLogger.Error("Diagnostics", "保存失败现场失败", ex);
            return DiagnosticsStore.Redact(reason + "\n诊断保存失败: " + ex.Message + "\n" + string.Join('\n', steps));
        }
    }

    public async Task<string?> ExportDiagnosticsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ZIP (*.zip)|*.zip", DefaultExt = ".zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            FileName = $"卫戍协议助手-诊断-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };
        if (dialog.ShowDialog(DialogOwner) != true) return null;
        var system = ReadDiagnosticSystem();
        var boardFrame = GetDiagnosticBoardFrame();
        await Task.Run(() => _diagnostics.Export(dialog.FileName, Settings, Connection, system, boardFrame));
        return dialog.FileName;
    }

    internal object ReadDiagnosticSystem()
    {
        var hwnd = DialogOwner is null ? nint.Zero : new WindowInteropHelper(DialogOwner).Handle;
        return new
        {
            version = typeof(HostApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            windows = RuntimeInformation.OSDescription,
            dpi = hwnd == 0 ? 96 : User32.GetWindowDpiScale(hwnd) * 96,
            monitors = DiagnosticMonitors.Read(),
            dpiAwareness = User32.DescribeDpiAwareness(User32.GetDpiAwarenessContextForProcess(
                System.Diagnostics.Process.GetCurrentProcess().Handle)),
            windowSnapshot = (DialogOwner as MainWindow)?.ReadDiagnosticWindows()
        };
    }
}

internal static class DiagnosticMonitors
{
    private delegate bool MonitorCallback(nint monitor, nint dc, ref RECT rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    public static object[] Read()
    {
        var monitors = new List<object>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref RECT rect, nint data) =>
        {
            var info = new User32.MONITORINFO { cbSize = Marshal.SizeOf<User32.MONITORINFO>() };
            if (!User32.GetMonitorInfo(monitor, ref info))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Marshal.ThrowExceptionForHR(GetDpiForMonitor(monitor, 0, out var x, out var y));
            monitors.Add(new { left = rect.Left, top = rect.Top, width = rect.Width, height = rect.Height,
                dpiX = x, dpiY = y, primary = (info.dwFlags & 1) != 0 });
            return true;
        }, 0);
        return monitors.ToArray();
    }
}
