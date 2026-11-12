param([Parameter(Position = 0)][string]$Destination = 'out/public-repo', [switch]$ScanOnly)
# git 输出 UTF-8，Windows PowerShell 5.1 默认按系统代码页读，中文文件名会乱码。
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ($ScanOnly) {
    # 通过现有 Node 工具链批量读取提交，不解压、不写目标目录。
    $scanner = @'
const {spawnSync} = require('node:child_process');
function git(args, options = {}) {
    const result = spawnSync('git', args, {maxBuffer: 512 * 1024 * 1024, ...options});
    if (result.status !== 0) throw new Error(result.stderr.toString());
    return result.stdout;
}
const policy = git(['show', 'HEAD:docs/internal.md']).toString('utf8');
const block = policy.match(/```forbidden\s*\r?\n([\s\S]*?)```/);
if (!block) throw new Error('未找到禁用词规则。');
const rules = block[1].split(/\r?\n/).filter(r => r.trim());
const patterns = rules.map(r => new RegExp(r, 'i'));
const entries = git(['ls-tree', '-rz', 'HEAD', '--', 'Directory.Build.props', 'StrongholdAssistant.sln',
    'src', 'tests', 'scripts', 'data', 'README.md', 'LICENSE', 'LICENSE.upstream', 'NOTICE',
    'ASSETS.md', '.gitignore', '.gitattributes', '.github', 'branding', 'installer'])
    .toString('utf8').split('\0').filter(Boolean).map(line => {
        const [meta, file] = line.split('\t');
        const [mode, type, hash] = meta.split(' ');
        if (mode === '120000' || type !== 'blob') throw new Error('不导出符号链接或子模块：' + file);
        return {file, hash};
    });
const blobs = git(['cat-file', '--batch'], {input: entries.map(e => e.hash).join('\n') + '\n'});
const hits = [];
let offset = 0;
for (const {file} of entries) {
    const end = blobs.indexOf(10, offset);
    const size = Number(blobs.toString('ascii', offset, end).split(' ')[2]);
    const bytes = blobs.subarray(end + 1, end + 1 + size);
    offset = end + 2 + size;
    const even = bytes.subarray(0, bytes.length - bytes.length % 2);
    const texts = [bytes.toString('utf8'), even.toString('utf16le'), Buffer.from(even).swap16().toString('utf16le')];
    for (let i = 0; i < patterns.length; i++)
        if (texts.some(text => patterns[i].test(text))) hits.push(file + ' : ' + rules[i]);
}
if (hits.length) { console.error('导出命中禁用词，目标未修改：\n' + hits.join('\n')); process.exit(1); }
console.log('禁用词扫描通过：' + entries.length + ' 个文件；来源当前提交。');
'@
    Push-Location -LiteralPath $root
    try {
        $savedEncoding = $OutputEncoding
        $OutputEncoding = [Text.Encoding]::UTF8
        $scanner | & node
        if ($LASTEXITCODE -ne 0) { throw '禁用词扫描失败。' }
    } finally { $OutputEncoding = $savedEncoding; Pop-Location }
    return
}
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
    # 直接解压 Git 快照，避免 PowerShell 对每个文件的管道与进度处理开销。
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $snapshot)
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
        if ([IO.File]::GetAttributes($source) -band [IO.FileAttributes]::ReparsePoint) { throw "不导出符号链接：$file" }
        $bytes = [IO.File]::ReadAllBytes($source)
        # 二进制同样按字节映射扫描，并覆盖 UTF-16 文本。
        $texts = @([Text.Encoding]::UTF8.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes), [Text.Encoding]::BigEndianUnicode.GetString($bytes))
        foreach ($rule in $rules) {
            foreach ($text in $texts) {
                if ([regex]::IsMatch($text, $rule, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                    $hits += "$file : $rule"
                    break
                }
            }
        }
        $output = Join-Path $stage $file
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
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
