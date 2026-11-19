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
    public bool FullOnly { get; } = Environment.GetCommandLineArgs().Contains("--retry-full-update");
}
