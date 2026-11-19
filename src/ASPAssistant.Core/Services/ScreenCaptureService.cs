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
    // 2026-11-15：窗口缺失和前台不一致只在状态变化时记录。
    private bool _windowMissing;
    private IntPtr? _mismatchedForeground;
    public GameConnectionService Connection { get; }
    public ScreenCaptureService(GameConnectionService? connection = null)
        => Connection = connection ?? new();
    /// <summary>
    /// Maximum capture width in pixels. Screenshots wider than this are scaled down
    /// proportionally before PNG encoding, reducing both encode time and all
    /// downstream MAA decode/processing costs. Has no effect on windows narrower than this.
    /// </summary>
    public int MaxCaptureWidth { get; init; } = 1920;
    public Action<Bitmap>? NativeFrame { get; set; }
    public Func<bool>? NativeFrameRequested { get; set; }
    private Action<Bitmap>? RequestedNativeFrame => NativeFrameRequested?.Invoke() != false ? NativeFrame : null;

    /// <summary>
    /// 优先捕获游戏客户区；失败时沿用前台 BitBlt，缩放和编码保持一致。
    /// </summary>
    public byte[]? CaptureScreen(IntPtr allowedForeground = default, Action<Bitmap>? inspect = null, bool scanPixels = false)
    {
        if (Connection.IsAdb) return Connection.CaptureAdbScreen(MaxCaptureWidth, RequestedNativeFrame);
        using var window = CaptureWindowFrame();
        return window is not null ? EncodeCapture(window, inspect, scanPixels) : CaptureBitBltScreen(allowedForeground, inspect, scanPixels);
    }

    // 信息识别先尝试本路径，失败后由宿主隐藏侧栏，再调用 BitBlt。
    public byte[]? CaptureWindowScreen()
    {
        using var window = CaptureWindowFrame();
        return window is null ? null : EncodeCapture(window);
    }

    private Bitmap? CaptureWindowFrame()
    {
        if (Connection.IsAdb) return null;
        var hwnd = Connection.FindGameWindow();
        return Connection.IsAdb ? null : Connection.WindowCapture.Capture(hwnd);
    }

    public byte[]? CaptureBitBltScreen(IntPtr allowedForeground = default, Action<Bitmap>? inspect = null, bool scanPixels = false)
    {
        if (Connection.IsAdb) return Connection.CaptureAdbScreen(MaxCaptureWidth, RequestedNativeFrame);
        var hwnd = Connection.FindGameWindow();
        if (Connection.IsAdb) return Connection.CaptureAdbScreen(MaxCaptureWidth, RequestedNativeFrame);
        return CaptureBitBltWindow(hwnd, allowedForeground, inspect, scanPixels);
    }

    internal byte[]? CaptureBitBltWindow(IntPtr hwnd, IntPtr allowedForeground = default, Action<Bitmap>? inspect = null, bool scanPixels = false)
    {
        if (hwnd == IntPtr.Zero)
        {
            if (!_windowMissing) AppLogger.Warn("ScreenCapture", "Arknights window not found");
            _windowMissing = true;
            _mismatchedForeground = null;
            return null;
        }
        _windowMissing = false;

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
            if (_mismatchedForeground != foreground) AppLogger.Warn("ScreenCapture", $"Foreground mismatch: game=0x{hwnd.ToInt64():X}, foreground=0x{foreground.ToInt64():X}");
            _mismatchedForeground = foreground;
            return null;
        }
        _mismatchedForeground = null;

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

            return EncodeCapture(raw, inspect, scanPixels);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Window state changed between the geometry query and the capture
            // (e.g. minimized mid-capture). Treat as no screenshot available.
            AppLogger.Warn("ScreenCapture", $"Win32Exception during capture (code={ex.NativeErrorCode}): {ex.Message}");
            return null;
        }
    }
    /// <summary>
    /// 截游戏客户区里的一块（PC），给敌人面板探针用，不编码。
    /// region 按客户区宽高给出要截的范围；guard 是不能被盖住的范围（默认就是 region），blocked 收到它在屏幕上的位置，
    /// WGC 不受遮挡影响；仅 BitBlt 后备在 blocked 返回 true 时跳过，并检查前台和窗口位置。
    /// 返回截图和相对 1920×1080 的缩放；模拟器返回 null。
    /// </summary>
    public (Bitmap Image, double Scale)? CaptureRegion(Func<int, int, Rectangle> region,
        Func<Rectangle, bool>? blocked = null, Func<int, int, Rectangle>? guard = null)
    {
        if (Connection.IsAdb) return null;
        using var window = CaptureWindowFrame();
        if (window is not null)
        {
            var crop = Rectangle.Intersect(region(window.Width, window.Height), new(0, 0, window.Width, window.Height));
            if (crop.Width <= 0 || crop.Height <= 0) return null;
            return (window.Clone(crop, PixelFormat.Format32bppArgb), Math.Min(window.Width / 1920d, window.Height / 1080d));
        }
        var hwnd = Connection.FindGameWindow();
        if (hwnd == IntPtr.Zero || User32.IsIconic(hwnd) || User32.GetForegroundWindow() != hwnd) return null;
        if (User32.GetClientRectScreen(hwnd) is not { } rect || rect.Width <= 0 || rect.Height <= 0) return null;
        var area = Rectangle.Intersect(region(rect.Width, rect.Height), new(0, 0, rect.Width, rect.Height));
        if (area.Width <= 0 || area.Height <= 0) return null;
        var screen = new Rectangle(rect.Left + area.X, rect.Top + area.Y, area.Width, area.Height);
        var covered = guard?.Invoke(rect.Width, rect.Height) ?? area;
        if (blocked?.Invoke(new(rect.Left + covered.X, rect.Top + covered.Y, covered.Width, covered.Height)) == true) return null;
        var image = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        try
        {
            using (var g = Graphics.FromImage(image)) g.CopyFromScreen(screen.Left, screen.Top, 0, 0, area.Size);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            image.Dispose();
            return null;
        }
        // 截的时候切走了或窗口动了，这张不要
        if (User32.GetForegroundWindow() != hwnd || User32.GetClientRectScreen(hwnd) is not { } after
            || after.Left != rect.Left || after.Top != rect.Top || after.Width != rect.Width || after.Height != rect.Height)
        {
            image.Dispose();
            return null;
        }
        return (image, Math.Min(rect.Width / 1920d, rect.Height / 1080d));
    }

    // PC 后台帧以未压缩位图传递；仅在保留现场时压成 PNG。
    public static byte[] AsPng(byte[] frame)
    {
        if (frame[0] != 'B' || frame[1] != 'M') return frame;
        using var input = new MemoryStream(frame);
        using var bitmap = new Bitmap(input);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    // 合成图验收与真实截屏共享缩小、编码路径。
    public byte[] EncodeCapture(Bitmap raw, Action<Bitmap>? inspect = null, bool scanPixels = false)
    {
        RequestedNativeFrame?.Invoke(raw);
        using var resized = raw.Width > MaxCaptureWidth
            ? new Bitmap(MaxCaptureWidth, (int)(raw.Height * ((float)MaxCaptureWidth / raw.Width)), PixelFormat.Format32bppArgb)
            : null;
        if (resized is not null)
        {
            using var graphics = Graphics.FromImage(resized);
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(raw, new Rectangle(0, 0, resized.Width, resized.Height), new Rectangle(0, 0, raw.Width, raw.Height), GraphicsUnit.Pixel);
        }
        // 识别循环直接用缩放后的截图生成指纹，避免编码后再解码同一张 PNG。
        inspect?.Invoke(resized ?? raw);
        using var stream = new MemoryStream();
        (resized ?? raw).Save(stream, scanPixels ? ImageFormat.Bmp : ImageFormat.Png);
        return stream.ToArray();
    }

}
