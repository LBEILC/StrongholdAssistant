using System.Reflection;

namespace ASPAssistant.Core;

/// <summary>
/// Lightweight file logger. Initialize once at startup with <see cref="Initialize"/>;
/// all subsequent calls to Info/Warn/Error are thread-safe.
/// 日志写入数据目录，单文件约 1 MiB，包含当前文件共保留五份。
/// </summary>
public static class AppLogger
{
    private static string? _logFilePath;
    private static readonly object _lock = new();

    /// <summary>
    /// Call once at application startup. Creates the log directory, prunes old logs,
    /// and writes the first entry with version and runtime information.
    /// </summary>
    public static void Initialize(string logDir)
    {
        try
        {
            Directory.CreateDirectory(logDir);

            _logFilePath = Path.Combine(logDir, "host.log");

            var version = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";
            var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

            WriteLine("INFO", "AppLogger", $"===== ASPAssistant {version} | {runtime} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLogger] Failed to initialise: {ex.Message}");
        }
    }

    public static void Info(string component, string message) =>
        WriteLine("INFO ", component, message);

    public static void Warn(string component, string message) =>
        WriteLine("WARN ", component, message);

    public static void Error(string component, string message) =>
        WriteLine("ERROR", component, message);

    public static void Error(string component, string message, Exception ex) =>
        WriteLine("ERROR", component, $"{message}: {ex}");

    public static string? SaveArtifact(string prefix, byte[] bytes, string extension = ".bin")
    {
        try
        {
            if (_logFilePath is null) return null;
            var dir = Path.GetDirectoryName(_logFilePath)!;
            var path = Path.Combine(dir, $"{prefix}-{DateTime.Now:HHmmssfff}{extension}");
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            WriteLine("WARN ", "AppLogger", $"Failed to save artifact: {ex.Message}");
            return null;
        }
    }

    private static void WriteLine(string level, string component, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{component}] {message}";
        if (_logFilePath == null) return;
        lock (_lock)
        {
            try
            {
                if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length
                    + System.Text.Encoding.UTF8.GetByteCount(line) > 1024 * 1024)
                {
                    var folder = Path.GetDirectoryName(_logFilePath)!;
                    File.Delete(Path.Combine(folder, "host.4.log"));
                    for (int i = 3; i >= 1; i--)
                    {
                        var old = Path.Combine(folder, $"host.{i}.log");
                        if (File.Exists(old)) File.Move(old, Path.Combine(folder, $"host.{i + 1}.log"));
                    }
                    File.Move(_logFilePath, Path.Combine(folder, "host.1.log"));
                }
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch { /* Never let logging crash the app */ }
        }
    }
}
