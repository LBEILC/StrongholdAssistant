using System.IO;

namespace ASPAssistant.Core.Services;

public partial class UpdateService
{
    // 具名实例共享读锁；实际替换文件期间，更新脚本持有独占锁。
    public static string InstallationId(string directory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(hash)[..16];
    }

    private static string ProfileLockPath(string directory) =>
        Path.Combine(Path.GetTempPath(), $"StrongholdAssistant-{InstallationId(directory)}.lock");

    public static FileStream OpenProfileLock(FileShare share) =>
        File.Open(ProfileLockPath(ASPAssistant.Core.PackageLayout.Root), FileMode.OpenOrCreate, FileAccess.Read, share);

    public string SelectPackageAsset(Version version, IEnumerable<string> assets, string releaseUrl)
    {
        var full = $"StrongholdAssistant-v{version}{PackageSuffix}";
        var delta = DeltaName(version);
        if (!FullOnly && assets.Contains(delta)) return delta;
        return assets.FirstOrDefault(name => string.Equals(name, full, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"发布 v{version} 中没有 {PackageSuffix} 包：{releaseUrl}");
    }

    private string DeltaName(Version version) => $"StrongholdAssistant-v{version}-from-v{CurrentVersion.ToString(3)}-delta.zip";
    // 按安装目录和旧版本区分，成功启动的新版本不会接手旧版本的回退任务。
    internal static string FailurePath(string directory, Version version) =>
        Path.ChangeExtension(ProfileLockPath(directory), $"v{version.ToString(3)}.failure");

    private string? _lastInstallFailure;
    private string FailureRecord => _lastInstallFailure ??= ReadFailureRecord();

    private string ReadFailureRecord()
    {
        var path = FailurePath(_appDirectory, CurrentVersion);
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    public string? PreviousInstallFailure => FailureRecord.Split('\n', 2) is [_, var reason] ? reason : null;
    public bool RetryFullOnStartup => Environment.GetCommandLineArgs().Contains("--retry-full-update")
        || FailureRecord.StartsWith("delta\n", StringComparison.Ordinal);
    public bool FullOnly => RetryFullOnStartup || PreviousInstallFailure is not null;
}
