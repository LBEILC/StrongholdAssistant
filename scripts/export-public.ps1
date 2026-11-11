param([Parameter(Position = 0)][string]$Destination = 'out/public-repo')
# git 输出 UTF-8，Windows PowerShell 5.1 默认按系统代码页读，中文文件名会乱码。
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$target = if ([IO.Path]::IsPathRooted($Destination)) { [IO.Path]::GetFullPath($Destination) }
    else { [IO.Path]::GetFullPath((Join-Path (Get-Location) $Destination)) }
if (!(Test-Path -LiteralPath (Join-Path $target '.git') -PathType Container)) { throw '目标必须是独立的 Git 仓库。' }
$top = git -C $target rev-parse --show-toplevel
$marker = git -C $target config --local --get stronghold.publicExport
if ($LASTEXITCODE -ne 0 -or $marker -ne '1' -or [IO.Path]::GetFullPath($top) -ne $target -or
    $target -eq $root -or $root.StartsWith($target.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw '目标仓库缺少 publicExport 标记或路径不安全。'
}
$temp = Join-Path $root ('out/export-' + [Guid]::NewGuid().ToString('N'))
$snapshot = Join-Path $temp 'snapshot'
$stage = Join-Path $temp 'stage'
New-Item -ItemType Directory -Path $snapshot,$stage -Force | Out-Null
try {
    $archive = Join-Path $temp 'commit.zip'
    git -C $root archive HEAD --format=zip -o $archive
    if ($LASTEXITCODE -ne 0) { throw '读取当前提交失败。' }
    Expand-Archive -LiteralPath $archive -DestinationPath $snapshot
    $policy = [IO.File]::ReadAllText((Join-Path $snapshot 'docs/internal.md'))
    $block = [regex]::Match($policy, '(?s)```forbidden\s*\r?\n(.*?)```')
    if (!$block.Success) { throw '未找到禁用词规则。' }
    $rules = @($block.Groups[1].Value -split '\r?\n' | Where-Object { $_.Trim() })
    # 在提交快照中解释 .gitignore，避免工作区修改影响导出。
    git -C $snapshot init --quiet
    $files = git -c core.quotepath=false -C $snapshot ls-files --others --exclude-standard -- Directory.Build.props StrongholdAssistant.sln src tests scripts data README.md LICENSE LICENSE.upstream NOTICE ASSETS.md .gitignore .gitattributes .github branding installer
    if ($LASTEXITCODE -ne 0) { throw '读取导出文件列表失败。' }
    $hits = @()
    foreach ($file in $files) {
        $source = Join-Path $snapshot $file
        if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "不导出符号链接：$file" }
        $bytes = [IO.File]::ReadAllBytes($source)
        # 二进制同样按字节映射扫描，并覆盖 UTF-16 文本。
        $texts = @([Text.Encoding]::UTF8.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes), [Text.Encoding]::BigEndianUnicode.GetString($bytes))
        foreach ($rule in $rules) {
            if ($texts | Where-Object { [regex]::IsMatch($_, $rule, [Text.RegularExpressions.RegexOptions]::IgnoreCase) }) {
                $hits += "$file : $rule"
            }
        }
        $output = Join-Path $stage $file
        New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
        [IO.File]::WriteAllBytes($output, $bytes)
    }
    if ($hits.Count) { throw ("导出命中禁用词，目标未修改：`n" + ($hits -join "`n")) }
    # 删除前再次解析每个目标，所有操作仅限已确认的导出仓库。
    foreach ($item in Get-ChildItem -LiteralPath $target -Force) {
        if ($item.Name -eq '.git') { continue }
        $full = [IO.Path]::GetFullPath($item.FullName)
        if (!$full.StartsWith($target.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '目标路径越界。' }
        Remove-Item -LiteralPath $full -Force -Recurse
    }
    Get-ChildItem -LiteralPath $stage -Force | Copy-Item -Destination $target -Recurse -Force
    Write-Output "导出完成：$($files.Count) 个文件；来源提交 $(git -C $root rev-parse HEAD)"
} finally {
    $safe = [IO.Path]::GetFullPath($temp)
    if ($safe.StartsWith((Join-Path $root 'out') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $safe -Recurse -Force
    }
}
