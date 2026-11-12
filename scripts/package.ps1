param([Parameter(Mandatory = $true, Position = 0)][string]$Version, [switch]$ForceTests, [switch]$ReuseOnly)
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
# 只记录已提交且干净的产品输入；测试文件不影响包指纹。
$release = [IO.Path]::GetFullPath((Join-Path $root 'out/release'))
$zip = Join-Path $release "StrongholdAssistant-v$Version-win-x64.zip"
$setup = Join-Path $release "StrongholdAssistant-v$Version-setup.exe"
$packageCache = Join-Path $release "package-$Version.json"
$paths = @('src', 'data', 'installer', 'branding', 'Directory.Build.props', 'LICENSE', 'LICENSE.upstream', 'NOTICE', 'ASSETS.md', ':(exclude)src/StrongholdAssistant.Tests')
$entries = @(& git -c core.quotepath=false ls-tree -r HEAD -- src data installer branding Directory.Build.props LICENSE LICENSE.upstream NOTICE ASSETS.md | Where-Object { ($_ -split "`t", 2)[1] -notlike 'src/StrongholdAssistant.Tests/*' })
if ($LASTEXITCODE -ne 0) { throw '读取打包输入失败。' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $fingerprint = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($entries -join "`n"))) } finally { $sha.Dispose() }
$clean = !(& git status --porcelain --untracked-files=all -- $paths)
if ($LASTEXITCODE -ne 0) { throw '读取打包工作区状态失败。' }
$previous = if (Test-Path -LiteralPath $packageCache) { Get-Content -LiteralPath $packageCache -Raw -Encoding UTF8 | ConvertFrom-Json }
$reusable = $clean -and $previous.fingerprint -eq $fingerprint -and (Test-Path -LiteralPath $zip) -and (Test-Path -LiteralPath $setup)
# 供发版脚本查询是否连包检查也可复用；未通过检查的包仍须 Check。
if ($ReuseOnly) { return ($reusable -and $previous.checked -eq $true) }
$timer = [Diagnostics.Stopwatch]::StartNew()
& (Join-Path $PSScriptRoot 'test-all.ps1') -ForceTests:$ForceTests
Write-Output "测试阶段：$($timer.Elapsed.TotalSeconds.ToString('F1')) 秒"
if ($reusable) { Write-Output '产品没变，复用已有的包'; return }
if (Test-Path -LiteralPath $packageCache) { Remove-Item -LiteralPath $packageCache }
$timer.Restart()
$lockHash = (Get-FileHash -LiteralPath 'src/ui/package-lock.json' -Algorithm SHA256).Hash
$npmCache = 'out/npm-lock.txt'
if (!(Test-Path 'src/ui/node_modules') -or !(Test-Path $npmCache) -or (Get-Content $npmCache -Raw).Trim() -ne $lockHash) {
    if (Test-Path $npmCache) { Remove-Item -LiteralPath $npmCache }
    Run { npm --prefix src/ui ci }
    New-Item -ItemType Directory -Path out -Force | Out-Null
    Set-Content -LiteralPath $npmCache -Value $lockHash -Encoding ASCII
} else { Write-Output '依赖锁未变，跳过 npm ci。' }
Run { npm --prefix src/ui run build }
Write-Output "界面构建：$($timer.Elapsed.TotalSeconds.ToString('F1')) 秒"
$timer.Restart()
$release = [IO.Path]::GetFullPath((Join-Path $root 'out/release'))
$publish = Join-Path $release ('publish-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publish -Force | Out-Null
# 项目依赖变化或测试 restore 覆盖了 RID 资产时才重新恢复。
$restoreFiles = & git ls-files -- '*.csproj' '*.props' '*.targets' '*NuGet.Config' '*nuget.config' 'global.json'
if ($LASTEXITCODE -ne 0) { throw '读取 restore 输入失败。' }
$restoreHash = (@($restoreFiles | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }) -join ':')
$restoreCache = 'out/publish-restore.txt'
$assetsPath = 'src/StrongholdAssistant.Host/obj/project.assets.json'
$hasRid = $false
if (Test-Path $assetsPath) {
    $assets = Get-Content $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $hasRid = @($assets.targets.PSObject.Properties.Name | Where-Object { $_ -like '*/win-x64' }).Count -gt 0
}
if (!$hasRid -or !(Test-Path $restoreCache) -or (Get-Content $restoreCache -Raw).Trim() -ne $restoreHash) {
    Run { dotnet restore src/StrongholdAssistant.Host -r win-x64 -p:ReleaseBuild=true }
    Set-Content $restoreCache $restoreHash -Encoding ASCII
}
Run { dotnet publish src/StrongholdAssistant.Host -c Release -r win-x64 --self-contained true --no-restore -p:ReleaseBuild=true -p:DebugType=None -p:DebugSymbols=false -o $publish }
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
Write-Output "发布目录：$($timer.Elapsed.TotalSeconds.ToString('F1')) 秒"
$timer.Restart()
$zipJob = Start-Job -ScriptBlock {
    param($script, $publish, $zip)
    & $script -Publish $publish -Zip $zip
} -ArgumentList (Join-Path $PSScriptRoot 'package-zip.ps1'), $publish, $zip
try {
    # 仅项目文件变化或资产不存在时恢复安装器依赖。
    $compilerHash = (Get-FileHash installer/Compiler.csproj).Hash
    $compilerCache = 'out/installer-restore.txt'
    if (!(Test-Path installer/obj/project.assets.json) -or !(Test-Path $compilerCache) -or (Get-Content $compilerCache -Raw).Trim() -ne $compilerHash) {
        Run { dotnet restore installer/Compiler.csproj }
        Set-Content $compilerCache $compilerHash -Encoding ASCII
    }
    Run { dotnet msbuild installer/Compiler.csproj -t:CompileInstaller "-p:InstallerVersion=$declared" "-p:PublishDir=$publish" "-p:ReleaseDir=$release" }
    $zipJob | Wait-Job | Receive-Job -ErrorAction Stop
    if ($zipJob.State -ne 'Completed') { throw 'ZIP 压缩失败。' }
} finally { $zipJob | Stop-Job; $zipJob | Remove-Job }
Write-Output "压缩与安装包并行：$($timer.Elapsed.TotalSeconds.ToString('F1')) 秒"
$setup = Join-Path $release "StrongholdAssistant-v$Version-setup.exe"
$notes = Join-Path $release 'release-notes.json'
Copy-Item -LiteralPath (Join-Path $root "src/ui/release-notes/$Version.json") -Destination $notes
$sums = foreach ($file in @($zip, $setup, $notes)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($file))"
}
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), ($sums -join "`n") + "`n", [Text.Encoding]::ASCII)
Write-Output "打包完成：$zip ($((Get-Item -LiteralPath $zip).Length) bytes)"
Write-Output "安装包：$setup ($((Get-Item -LiteralPath $setup).Length) bytes)"

if ($clean -and !(& git status --porcelain --untracked-files=all -- $paths)) {
    @{ fingerprint = $fingerprint; checked = $false } | ConvertTo-Json | Set-Content -LiteralPath $packageCache -Encoding UTF8
}
