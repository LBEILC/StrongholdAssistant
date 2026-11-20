param([string]$Publish, [string]$Zip)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Zip) { Remove-Item -LiteralPath $Zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($Zip, [IO.Compression.ZipArchiveMode]::Create, [Text.Encoding]::UTF8)
try {
    # 顺序解压时先写完数据和依赖，再开放宿主和快捷方式使用的入口。
    $entryPoints = Get-Item -LiteralPath (Join-Path $Publish 'app/StrongholdAssistant.exe'), (Join-Path $Publish 'StrongholdAssistant.exe')
    $files = Get-ChildItem -LiteralPath $Publish -File -Recurse | Where-Object { $_.FullName -notin $entryPoints.FullName }
    foreach ($file in @($files) + $entryPoints) {
        $entry = $file.FullName.Substring($Publish.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Fastest) | Out-Null
    }
} finally { $archive.Dispose() }
