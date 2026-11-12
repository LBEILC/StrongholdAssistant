param([string]$Publish, [string]$Zip)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Zip) { Remove-Item -LiteralPath $Zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($Zip, [IO.Compression.ZipArchiveMode]::Create, [Text.Encoding]::UTF8)
try {
    foreach ($file in Get-ChildItem -LiteralPath $Publish -File -Recurse) {
        $entry = $file.FullName.Substring($Publish.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Fastest) | Out-Null
    }
} finally { $archive.Dispose() }
