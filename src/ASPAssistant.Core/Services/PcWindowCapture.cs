using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ASPAssistant.Core.Interop;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

/// <summary>连接内复用的客户区捕获；输入通道关闭，物资页与扫描共用。</summary>
public sealed class PcWindowCapture : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<nint, IMaaController> _createController;
    private IMaaController? _controller;
    private volatile bool _hideCaptureBorder;
    internal bool HideCaptureBorder => _hideCaptureBorder;

    public PcWindowCapture() : this(window =>
    {
        _ = Task.Run(CaptureBorderDiagnostic.Log);
        CrashGuard.WgcStarting();
        return new MaaWin32Controller(new DesktopWindowInfo(window, "", "", Win32ScreencapMethod.FramePool,
            Win32InputMethod.None, Win32InputMethod.None), LinkOption.None, CheckStatusOption.None);
    }) { }

    internal PcWindowCapture(Func<nint, IMaaController> createController) => _createController = createController;

    private nint _window;
    private bool _initializationFailed;
    private byte[]? _fingerprint;
    private long _changedAt;
    private readonly HashSet<string> _loggedReasons = [];
    public string Method { get; private set; } = "bitblt";
    public string? LastFallbackReason { get; private set; }

    public void SetHideCaptureBorder(bool enabled)
    {
        lock (_gate)
        {
            if (_hideCaptureBorder == enabled) return;
            _hideCaptureBorder = enabled;
            if (enabled)
            {
                Dispose();
                Fallback("设置里打开了去掉黄框");
            }
            else LastFallbackReason = null;
        }
    }

    public Bitmap? Capture(nint window, bool requireFresh = true)
    {
        lock (_gate)
        {
            if (_hideCaptureBorder) return null;
            if (_window != window)
            {
                _controller?.Dispose();
                _controller = null;
                _window = window;
                _initializationFailed = false;
                _fingerprint = null;
            }
            if (window == 0 || !User32.IsWindow(window) || User32.IsIconic(window))
                return Fallback("窗口不存在或已最小化");
            if (CrashGuard.WgcDisabled) return Fallback("这台电脑上窗口捕获连续异常退出，改用 BitBlt");
            if (_initializationFailed) return null;
            try
            {
                if (_controller is null)
                {
                    _initializationFailed = true;
                    _controller = _createController(window);
                    if (_controller.LinkStart().Wait() != MaaJobStatus.Succeeded)
                    {
                        _initializationFailed = true;
                        return Fallback("窗口捕获初始化失败");
                    }
                    _controller.SetOption_ScreenshotUseRawSize(true);
                    _initializationFailed = false;
                }
                if (_controller.Screencap().Wait() != MaaJobStatus.Succeeded)
                    return Fallback("窗口捕获没有可用画面");
                using var image = new MaaImageBuffer();
                if (!_controller.GetCachedImage(image) || !image.TryGetRawData(out var data, out var width, out var height, out var type))
                    return Fallback("窗口捕获读取画面失败");
                var client = User32.GetClientRectScreen(window);
                if (client is null || width != client.Value.Width || height != client.Value.Height || width <= 0 || height <= 0)
                    return Fallback("窗口捕获尺寸与客户区不一致");
                // Maa 的图像为连续 OpenCV BGR/BGRA 数据，避免中间 PNG 编解码。
                int channels = type == 16 ? 3 : type == 24 ? 4 : 0;
                if (channels == 0) return Fallback("窗口捕获像素格式不支持");
                var bytes = new byte[checked(width * height * channels)];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                // 绑定层不提供 WGC 帧时间戳，保守地把连续两秒相同的画面视为旧帧。
                var fingerprint = SHA256.HashData(bytes);
                if (_fingerprint is null || !fingerprint.AsSpan().SequenceEqual(_fingerprint))
                {
                    _fingerprint = fingerprint;
                    _changedAt = Stopwatch.GetTimestamp();
                }
                else if (requireFresh && Stopwatch.GetElapsedTime(_changedAt) >= TimeSpan.FromSeconds(2))
                    return Fallback("窗口捕获画面连续两秒未更新");
                var bitmap = new Bitmap(width, height, channels == 3 ? PixelFormat.Format24bppRgb : PixelFormat.Format32bppArgb);
                var pixels = bitmap.LockBits(new(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                try
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(bytes, y * width * channels, pixels.Scan0 + y * pixels.Stride, width * channels);
                }
                finally { bitmap.UnlockBits(pixels); }
                Method = "wgc";
                return bitmap;
            }
            catch (Exception ex)
            {
                // 原生捕获、驱动和图像解码属于系统边界，失败后保留原有截图路径。
                if (_controller is null) _initializationFailed = true;
                return Fallback($"窗口捕获失败：{ex.GetType().Name}");
            }
        }
    }

    private Bitmap? Fallback(string reason)
    {
        Method = "bitblt";
        LastFallbackReason = reason;
        if (_loggedReasons.Add(reason)) AppLogger.Warn("ScreenCapture", $"退回 BitBlt：{reason}");
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _controller?.Dispose();
            _controller = null;
            _window = 0;
            Method = "bitblt";
        }
    }
}
