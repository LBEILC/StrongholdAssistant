param([switch]$ForceTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$cache = Join-Path $root 'out/test-pass.txt'
# src 包含测试样图；data 包含模型、图标及测试使用的游戏数据。
function Fingerprint {
    $hashes = & git rev-parse HEAD:src HEAD:data HEAD:Directory.Build.props
    if ($LASTEXITCODE -ne 0) { throw '无法读取测试输入树。' }
    return $hashes -join ':'
}
function IsClean {
    $changes = & git status --porcelain --untracked-files=all -- src data Directory.Build.props
    if ($LASTEXITCODE -ne 0) { throw '无法读取测试输入工作区状态。' }
    return !$changes
}
$fingerprint = Fingerprint
$clean = IsClean
if (!$ForceTests -and $clean -and (Test-Path -LiteralPath $cache)) {
    $pass = Get-Content -LiteralPath $cache -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($pass.fingerprint -eq $fingerprint) {
        Write-Output "这份代码已跑过全量测试（$($pass.time)）"
        return
    }
}
# 先移除旧凭据；失败或中断绝不能留下可命中的通过记录。
if (Test-Path -LiteralPath $cache) { Remove-Item -LiteralPath $cache }
$timer = [Diagnostics.Stopwatch]::StartNew()
& dotnet test src/StrongholdAssistant.Tests -c Release --logger 'trx;LogFileName=tests.trx' --results-directory out/test-results
if ($LASTEXITCODE -ne 0) { throw '全量测试失败；未写入通过指纹。修复后重新运行 scripts/test-all.ps1。' }
if ($clean -and (IsClean) -and (Fingerprint) -eq $fingerprint) {
    @{ fingerprint = $fingerprint; time = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $cache -Encoding UTF8
} else { Write-Output '测试通过，但测试输入有未提交改动，不记录通过指纹。' }
Write-Output "全量测试耗时：$($timer.Elapsed.TotalSeconds.ToString('F1')) 秒"
