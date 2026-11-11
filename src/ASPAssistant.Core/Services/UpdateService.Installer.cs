using System.IO;

namespace ASPAssistant.Core.Services;

public partial class UpdateService
{
    public string SelectPackageAsset(Version version, IEnumerable<string> assets, string releaseUrl)
    {
        var suffix = IsInstalled ? "-setup.exe" : PackageSuffix;
        var wanted = $"StrongholdAssistant-v{version}{suffix}";
        return assets.FirstOrDefault(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(IsInstalled
                ? $"发布 v{version} 中没有 setup 安装包，请前往下载页：{releaseUrl}"
                : $"发布 v{version} 中没有 {PackageSuffix} 包");
    }

    public static string BuildInstallerUpdateScript(int pid, string setupPath, string appDir)
    {
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        // 保留用户自选安装目录；静默安装不执行完成页启动项，由此脚本在成功后启动。
        var arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR=\"{appDir}\"";
        return $$"""
            $ErrorActionPreference = 'Stop'
            $old = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
            if ($old) { $old.WaitForExit(); $old.Dispose() }
            $installer = Start-Process -FilePath {{Q(setupPath)}} -ArgumentList {{Q(arguments)}} -WindowStyle Hidden -PassThru -Wait
            $code = $installer.ExitCode
            $installer.Dispose()
            if ($code -ne 0) { exit $code }
            Start-Process -FilePath {{Q(Path.Combine(appDir, AppExecutableName))}} -WorkingDirectory {{Q(appDir)}} -WindowStyle Hidden
            """;
    }
}
