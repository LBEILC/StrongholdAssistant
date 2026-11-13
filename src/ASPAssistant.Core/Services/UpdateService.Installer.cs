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
            $setupPath = {{Q(Path.GetFullPath(setupPath))}}
            try {
                $old = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
                if ($old) {
                    try { if (-not $old.WaitForExit(30000)) { throw '等待旧进程退出超时' } }
                    finally { $old.Dispose() }
                }
                $installer = Start-Process -FilePath $setupPath -ArgumentList {{Q(arguments)}} -WindowStyle Hidden -PassThru -Wait
                $code = $installer.ExitCode
                $installer.Dispose()
                if ($code -ne 0) { throw "安装器退出代码：$code" }
                Start-Process -FilePath {{Q(Path.Combine(appDir, AppExecutableName))}} -WorkingDirectory {{Q(appDir)}} -ArgumentList '--keep-identity' -WindowStyle Hidden
            } catch {
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.MessageBox]::Show("卫戍协议助手更新没有完成。重新运行这个安装包就能修好：$setupPath", '卫戍协议助手') | Out-Null
                exit 1
            }
            """;
    }
}
