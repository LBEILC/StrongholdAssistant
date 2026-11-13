// 2026-11-12：参考上游 PR #5，共享连接来源与内部画面区域。
// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
// 2026-11-11：信息页截屏允许指定侧栏处于前台，避免不遮挡时切换窗口。
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public class ScreenCaptureService
{
    public GameConnectionService Connection { get; }
    public ScreenCaptureService(GameConnectionService? connection = null)
        => Connection = connection ?? new();
    /// <summary>
    /// Maximum capture width in pixels. Screenshots wider than this are scaled down
    /// proportionally before PNG encoding, reducing both encode time and all
    /// downstream MAA decode/processing costs. Has no effect on windows narrower than this.
    /// </summary>
    public int MaxCaptureWidth { get; init; } = 1920;

    /// <summary>
    /// Captures the Arknights game window by reading pixels directly from the screen
    /// via BitBlt (Graphics.CopyFromScreen). This works with hardware-accelerated
    /// renderers (DirectX/Vulkan) that do not honour PrintWindow/GDI capture.
    /// Requires the window to be visible and not fully occluded.
    /// Returns PNG bytes, or null if the window is not found.
    /// </summary>
    public byte[]? CaptureScreen(IntPtr allowedForeground = default, Action<Bitmap>? inspect = null)
    {
        if (Connection.IsAdb) return Connection.CaptureAdbScreen(MaxCaptureWidth);
        var hwnd = Connection.FindGameWindow();
        if (Connection.IsAdb) return Connection.CaptureAdbScreen(MaxCaptureWidth);
        if (hwnd == IntPtr.Zero)
        {
            AppLogger.Warn("ScreenCapture", "Arknights window not found");
            return null;
        }

        // Skip minimized windows: ClientToScreen returns (-32000,-32000) for them,
        // which causes CopyFromScreen to throw Win32Exception(6) ERROR_INVALID_HANDLE.
        if (User32.IsIconic(hwnd))
        {
            AppLogger.Warn("ScreenCapture", "Arknights window is minimized — skipping capture");
            return null;
        }

        // CopyFromScreen cannot see through another application's foreground window.
        var foreground = User32.GetForegroundWindow();
        if (foreground != hwnd && (allowedForeground == IntPtr.Zero || foreground != allowedForeground))
        {
            AppLogger.Warn("ScreenCapture", $"Foreground mismatch: game=0x{hwnd.ToInt64():X}, foreground=0x{foreground.ToInt64():X}");
            return null;
        }

        var screenRect = User32.GetClientRectScreen(hwnd);
        if (screenRect == null)
        {
            AppLogger.Warn("ScreenCapture", "GetClientRectScreen returned null");
            return null;
        }

        var rect = screenRect.Value;
        int captureWidth  = rect.Width;
        int captureHeight = rect.Height;
        if (captureWidth <= 0 || captureHeight <= 0)
        {
            AppLogger.Warn("ScreenCapture", $"Invalid client rect size: {captureWidth}x{captureHeight}");
            return null;
        }

        try
        {
            // Capture at native resolution.
            using var raw = new Bitmap(captureWidth, captureHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(raw))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0,
                    new System.Drawing.Size(captureWidth, captureHeight));
            }

            // Reject a frame captured while the user switched apps or resized the game.
            var after = User32.GetClientRectScreen(hwnd);
            var afterForeground = User32.GetForegroundWindow();
            if (afterForeground != hwnd && (allowedForeground == IntPtr.Zero || afterForeground != allowedForeground) || after is null
                || after.Value.Left != rect.Left || after.Value.Top != rect.Top
                || after.Value.Width != rect.Width || after.Value.Height != rect.Height)
            {
                AppLogger.Warn("ScreenCapture", "Capture invalidated because foreground or client rect changed during capture");
                return null;
            }

            return EncodeCapture(raw, inspect);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Window state changed between the geometry query and the capture
            // (e.g. minimized mid-capture). Treat as no screenshot available.
            AppLogger.Warn("ScreenCapture", $"Win32Exception during capture (code={ex.NativeErrorCode}): {ex.Message}");
            return null;
        }
    }
    // 合成图验收与真实截屏共享缩小、编码路径。
    public byte[] EncodeCapture(Bitmap raw, Action<Bitmap>? inspect = null)
    {
        using var resized = raw.Width > MaxCaptureWidth
            ? new Bitmap(MaxCaptureWidth, (int)(raw.Height * ((float)MaxCaptureWidth / raw.Width)), PixelFormat.Format32bppArgb)
            : null;
        if (resized is not null)
        {
            using var graphics = Graphics.FromImage(resized);
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(raw, 0, 0, resized.Width, resized.Height);
        }
        // 识别循环直接用缩放后的截图生成指纹，避免编码后再解码同一张 PNG。
        inspect?.Invoke(resized ?? raw);
        using var stream = new MemoryStream();
        (resized ?? raw).Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

}
