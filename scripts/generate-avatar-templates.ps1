param([string]$SourceDirectory = (Join-Path $PSScriptRoot '../docs/research/refs/arknights-game-resource-62fe51e'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$map = Get-Content -LiteralPath (Join-Path $SourceDirectory 'avatar_map.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$operators = (Get-Content -LiteralPath (Join-Path $root 'data/operators.json') -Raw -Encoding UTF8 | ConvertFrom-Json).operators
$names = @($operators | Where-Object { $null -eq $_.obtain } | ForEach-Object { $_.name })
$count = 0
$bytes = 0
foreach ($entry in $map.files.PSObject.Properties) {
    $name = [string]$entry.Value
    $file = $entry.Name
    if ($name -notin $names -or [IO.Path]::GetFileName($file) -ne $file -or [IO.Path]::GetFileName($name) -ne $name) {
        throw "头像映射包含无效干员或路径：$file -> $name"
    }
    $directory = Join-Path $root "data/operator_avatars/$name"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $destination = Join-Path $directory $file
    $source = [Drawing.Bitmap]::new((Join-Path $SourceDirectory "avatar/$file"))
    try {
        # 与已有模板一样生成 128×128 PNG，原图只从本地读取。
        $resized = [Drawing.Bitmap]::new($source, 128, 128)
        try { $resized.Save($destination, [Drawing.Imaging.ImageFormat]::Png) }
        finally { $resized.Dispose() }
    } finally { $source.Dispose() }
    $count++
    $bytes += (Get-Item -LiteralPath $destination).Length
}
Write-Output "Source: $($map.source) commit $($map.commit)"
Write-Output "Generated: $count PNG, $bytes bytes"
