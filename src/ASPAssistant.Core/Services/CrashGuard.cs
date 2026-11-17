namespace ASPAssistant.Core.Services;

/// <summary>
/// 原生代码崩溃时托管层拦不住、日志也留不下（v1.2.5）。用标记文件判断上次是不是在用窗口捕获时异常退出：
/// 连续两次就在这台电脑上改用 BitBlt；WebView2 的 GPU 进程崩过一次，以后界面就关掉硬件加速。
/// </summary>
public static class CrashGuard
{
    private const int WgcCrashLimit = 2;
    private static string? _directory;
    private static bool _wgcMarked;
    public static bool WgcDisabled { get; private set; }
    public static bool GpuDisabled { get; private set; }
    /// <summary>文字识别固定用 CPU：显卡推理读不出字过（v1.2.6）。</summary>
    public static bool OcrCpu { get; private set; }
    /// <summary>连模拟器时崩过（v1.2.8）：ADB 截图只用最基本的两种方式，不让 MaaFramework 去试别的。</summary>
    public static bool AdbSafe { get; private set; }
    private static bool _adbMarked;

    private static string PathOf(string name) => Path.Combine(_directory!, name);

    public static void Initialize(string directory)
    {
        _directory = Path.Combine(directory, "crash-guard");
        Directory.CreateDirectory(_directory);
        _wgcMarked = false;
        if (File.Exists(PathOf("wgc-active")))
        {
            File.Delete(PathOf("wgc-active"));
            int count = (int.TryParse(ReadOrEmpty("wgc-crashes"), out var n) ? n : 0) + 1;
            File.WriteAllText(PathOf("wgc-crashes"), count.ToString());
            AppLogger.Warn("CrashGuard", $"上次运行在用窗口捕获时异常退出（连续第 {count} 次）");
            if (count >= WgcCrashLimit) File.WriteAllText(PathOf("wgc-disabled"), DateTimeOffset.Now.ToString("O"));
        }
        if (File.Exists(PathOf("adb-active")))
        {
            File.Delete(PathOf("adb-active"));
            File.WriteAllText(PathOf("adb-safe"), DateTimeOffset.Now.ToString("O"));
            AppLogger.Warn("CrashGuard", "上次运行在连模拟器截图时异常退出");
        }
        _adbMarked = false;
        AdbSafe = File.Exists(PathOf("adb-safe"));
        if (AdbSafe) AppLogger.Warn("CrashGuard", "这台电脑上连模拟器截图崩过，ADB 截图只用基本方式");
        WgcDisabled = File.Exists(PathOf("wgc-disabled"));
        GpuDisabled = File.Exists(PathOf("gpu-disabled"));
        OcrCpu = File.Exists(PathOf("ocr-cpu"));
        if (OcrCpu) AppLogger.Warn("CrashGuard", "显卡推理读不出字过，文字识别固定用 CPU");
        if (WgcDisabled) AppLogger.Warn("CrashGuard", "这台电脑上窗口捕获连续异常退出，截图改用 BitBlt");
        if (GpuDisabled) AppLogger.Warn("CrashGuard", "界面的 GPU 进程崩溃过，已关闭界面硬件加速");
    }

    /// <summary>开始用窗口捕获前调用；正常退出时清掉标记。</summary>
    public static void WgcStarting()
    {
        if (_directory is null || _wgcMarked) return;
        File.WriteAllText(PathOf("wgc-active"), DateTimeOffset.Now.ToString("O"));
        _wgcMarked = true;
    }

    /// <summary>建 ADB 截图连接前调用；第一次截图成功后调 <see cref="AdbLinked"/> 清掉标记。</summary>
    public static void AdbStarting()
    {
        if (_directory is null || _adbMarked) return;
        File.WriteAllText(PathOf("adb-active"), DateTimeOffset.Now.ToString("O"));
        _adbMarked = true;
    }

    public static void AdbLinked()
    {
        if (_directory is null || !_adbMarked) return;
        File.Delete(PathOf("adb-active"));
        _adbMarked = false;
    }

    public static void GpuCrashed()
    {
        if (_directory is null || GpuDisabled) return;
        File.WriteAllText(PathOf("gpu-disabled"), DateTimeOffset.Now.ToString("O"));
    }

    public static void OcrGpuFailed()
    {
        if (_directory is null) return;
        File.WriteAllText(PathOf("ocr-cpu"), DateTimeOffset.Now.ToString("O"));
        OcrCpu = true;
    }

    public static void NormalExit()
    {
        AdbLinked();
        if (_directory is null || !_wgcMarked) return;
        File.Delete(PathOf("wgc-active"));
        File.Delete(PathOf("wgc-crashes"));
        _wgcMarked = false;
    }

    private static string ReadOrEmpty(string name) => File.Exists(PathOf(name)) ? File.ReadAllText(PathOf(name)) : "";
}
