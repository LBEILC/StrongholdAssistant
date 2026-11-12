param([string]$OutputPath, [string]$FullCatalogPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputPath) { $OutputPath = Join-Path $root 'src/plaza/data/catalog.json' }
$arguments = @('run', '--project', (Join-Path $root 'src/StrongholdAssistant.PlazaCatalogExport'), '--', (Join-Path $root 'data'), $OutputPath)
if ($FullCatalogPath) { $arguments += $FullCatalogPath }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '广场 catalog 导出失败。' }
