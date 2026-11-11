param([Parameter(Mandatory = $true, Position = 0)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$declared = @($props.Project.PropertyGroup.Version)[0].InnerText
if (!$declared) { $declared = [string]@($props.Project.PropertyGroup.Version)[0] }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $declared) {
    throw '参数版本必须与 Directory.Build.props 的发布版本一致。'
}
function Run([scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "命令执行失败：$Command" }
}
Run { npm --prefix src/ui ci }
Run { npm --prefix src/ui run build }
Run { dotnet test src/StrongholdAssistant.Tests -c Release }
$release = [IO.Path]::GetFullPath((Join-Path $root 'out/release'))
$publish = Join-Path $release ('publish-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publish -Force | Out-Null
Run { dotnet publish src/StrongholdAssistant.Host -c Release -r win-x64 --self-contained true -p:ReleaseBuild=true -p:DebugType=None -p:DebugSymbols=false -o $publish }
foreach ($name in @('LICENSE','LICENSE.upstream','NOTICE','ASSETS.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $publish
}
# 与上游独立运行包一致，随包携带本机已安装的 x64 VC++ 运行库。
foreach ($name in @('msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll','msvcp140_codecvt_ids.dll','vcruntime140.dll','vcruntime140_1.dll','concrt140.dll')) {
    Copy-Item -LiteralPath (Join-Path ([Environment]::SystemDirectory) $name) -Destination (Join-Path $publish 'runtimes/win-x64/native')
}
Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object { $_.Extension -in @('.pdb', '.xml') -or $_.Name -eq 'createdump.exe' } | Remove-Item -Force
# Maa 的包通过内容复制带入所有平台文件；只裁剪本次生成的发布目录。
$unused = @(Get-ChildItem -LiteralPath (Join-Path $publish 'runtimes') -Directory | Where-Object Name -ne 'win-x64')
$unused += Get-Item -LiteralPath (Join-Path $publish 'MaaAgentBinary') -ErrorAction SilentlyContinue
foreach ($item in $unused) {
    $target = [IO.Path]::GetFullPath($item.FullName)
    if (!$target.StartsWith($publish + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw '裁剪路径必须位于本次发布目录内。'
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}
$zip = Join-Path $release "StrongholdAssistant-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create, [Text.Encoding]::UTF8)
try {
    foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse) {
        $entry = $file.FullName.Substring($publish.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Run { dotnet restore installer/Compiler.csproj }
Run { dotnet msbuild installer/Compiler.csproj -t:CompileInstaller "-p:InstallerVersion=$declared" "-p:PublishDir=$publish" "-p:ReleaseDir=$release" }
$setup = Join-Path $release "StrongholdAssistant-v$Version-setup.exe"
$sums = foreach ($file in @($zip, $setup)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($file))"
}
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), ($sums -join "`n") + "`n", [Text.Encoding]::ASCII)
Write-Output "打包完成：$zip ($((Get-Item -LiteralPath $zip).Length) bytes)"
Write-Output "安装包：$setup ($((Get-Item -LiteralPath $setup).Length) bytes)"
