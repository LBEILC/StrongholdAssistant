param([switch]$Apply)
# 清理打包、验收和测试被中途打断时留下的临时目录。默认只列出，加 -Apply 才删除。
# 只认这些脚本自己起的名字；不碰发布包、out/public-repo、out/test、指纹缓存和用户数据。
# 删除前先确认没有打包、验收或测试正在运行。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'out'
$temp = [IO.Path]::GetTempPath()
$items = @(
    Get-ChildItem -LiteralPath (Join-Path $out 'release') -Directory -Filter 'publish-*' -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $out -Directory -ErrorAction SilentlyContinue | Where-Object Name -match '^(package-check|export-check|export)-[0-9a-f]{32}$'
    Get-ChildItem -LiteralPath $temp -Directory -Force | Where-Object Name -match '^(StrongholdAssistant-(BundleCheck|UpdateTests|RouteTest|CrashTest|BridgeTest)-.+|StrongholdAssistant-StartupCheck|StrongholdAssistant\.(StartupCheck|Tests|Catalog)|stronghold-(shoot|readme)-chrome)$'
)
$total = 0
foreach ($item in $items) {
    $size = (Get-ChildItem -LiteralPath $item.FullName -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    $total += $size
    Write-Output ('{0,8:N0} MB  {1}' -f ($size / 1MB), $item.FullName)
    if ($Apply) { Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}
$left = @($items | Where-Object { Test-Path -LiteralPath $_.FullName })
if (!$Apply) { Write-Output ('共 {0} 项，{1:N0} MB。确认后运行 scripts/clean.ps1 -Apply 删除。' -f $items.Count, ($total / 1MB)) }
else {
    Write-Output ('已删除 {0} 项，约 {1:N0} MB。' -f ($items.Count - $left.Count), ($total / 1MB))
    foreach ($item in $left) { Write-Output "未能删除（可能仍被占用）：$($item.FullName)" }
}
