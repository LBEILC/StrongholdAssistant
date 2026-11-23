param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Version,
    [ValidateSet('Preflight','Package','Export','Check','Repository','Release','Verify')][string]$From = 'Preflight',
    [switch]$LocalOnly
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$public = Join-Path $root 'out/public-repo'
$release = Join-Path $root 'out/release'
$body = Join-Path $release "release-body-$Version.md"
$zip = Join-Path $release "StrongholdAssistant-v$Version-win-x64.zip"
$setup = Join-Path $release "StrongholdAssistant-v$Version-setup.exe"
$steps = @('Preflight','Package','Check','Export','Repository','Release','Verify')
$first = [Array]::IndexOf($steps, $From)
$times = [ordered]@{}
$total = [Diagnostics.Stopwatch]::StartNew()
$step = 'Preflight'
$exportJob = $null
function Run([scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "命令执行失败：$Command" }
}
function Measure-Step([string]$Name, [scriptblock]$Command) {
    $script:step = $Name
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try { & $Command } finally { $times[$Name] = [Math]::Round($watch.Elapsed.TotalSeconds, 2) }
}
try {
    # 续跑也核对基本身份，但不重复构建或上传。
    Measure-Step Preflight {
        Run { powershell -NoProfile -ExecutionPolicy Bypass -File scripts/export-public.ps1 -ScanOnly }
        # 干净工作树首次导出时，宿主项目要求先有界面产物。
        if (!(Test-Path 'src/ui/dist/index.html')) {
            if (!(Test-Path 'src/ui/node_modules')) { Run { npm --prefix src/ui ci } }
            Run { npm --prefix src/ui run build }
        }
        $catalogCheck = Join-Path $release 'catalog-check/catalog.json'
        & "$PSScriptRoot/export-plaza-catalog.ps1" -OutputPath $catalogCheck
        # Git 在 Windows 检出时可能转换换行符。
        $expected = [IO.File]::ReadAllText((Join-Path $root 'src/plaza/data/catalog.json')).Replace("`r`n", "`n")
        if ([IO.File]::ReadAllText($catalogCheck).Replace("`r`n", "`n") -cne $expected) {
            throw '广场目录与宿主数据不一致，请先运行 scripts/export-plaza-catalog.ps1，提交目录并部署广场。'
        }
        [xml]$props = Get-Content Directory.Build.props -Raw
        $declared = [string]@($props.Project.PropertyGroup.Version)[0]
        if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $declared) { throw '版本必须与 Directory.Build.props 一致。' }
        if (!(Test-Path "src/ui/release-notes/$Version.json")) { throw '缺少当前版本更新说明。' }
        $dirty = @(& git -c core.quotepath=false status --porcelain --untracked-files=all)
        if ($LASTEXITCODE -ne 0) { throw '读取工作区状态失败。' }
        if (@($dirty | Where-Object { $_.Substring(3) -ne 'docs/design.md' }).Count) { throw '请先提交工作区改动（docs/design.md 除外）。' }
        if (!(Test-Path (Join-Path $public '.git'))) { throw '缺少 out/public-repo。' }
        $remote = & git -C $public remote get-url origin
        if ($LASTEXITCODE -ne 0 -or $remote -notmatch '^(?:https://github\.com/|git@github\.com:)([^/]+/[^/]+?)(?:\.git)?$') { throw '公开仓库 origin 必须是 GitHub 仓库。' }
        $script:repo = $Matches[1]
        $internalRemote = & git remote get-url origin
        if ($LASTEXITCODE -ne 0 -or $remote -eq $internalRemote) { throw '公开仓库不能指向内部仓库。' }
        if ((& git -C $public config --local --get stronghold.publicExport) -ne '1') { throw '公开仓库缺少 publicExport 标记。' }
        if ((& git -C $public branch --show-current) -ne 'main') { throw '公开工作副本必须在 main 分支。' }
        if (Get-NetTCPConnection -LocalPort 5178 -State Listen -ErrorAction SilentlyContinue) { throw '5178 端口被占用，请先关闭 UI 预览服务。' }
        if (!$LocalOnly) {
            if (!(Test-Path -LiteralPath $body)) { throw "请由设计先写好 $body。" }
            Run { gh auth status }
            $metadata = & gh api "repos/$repo" | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0 -or $metadata.private -ne $false) { throw '远程必须是可访问的公开仓库。' }
        } else { Write-Output '本地验收：不检查 GitHub 登录和正文，不执行提交、推送或 Release 操作。' }
    }
    if ($first -le 3) {
        . (Join-Path $root 'tools/export-fingerprint.ps1')
        $fingerprint = Get-ExportFingerprint $root
        $cache = Join-Path $root 'out/export-check.txt'
        $exportScripts = @('scripts/export-public.ps1')
        if ($fingerprint -and (Test-Path -LiteralPath $cache) -and
            (Get-Content -LiteralPath $cache -Raw).Trim() -eq $fingerprint) {
            Write-Output '导出机制没变，跳过导出检查'
        } else { $exportScripts += 'tools/check-export.ps1' }
        # 两者只读同一提交，输出到不同目录，可以各自与打包同时运行。
        $exportJob = @(foreach ($script in $exportScripts) {
            Start-Job -ArgumentList $root, $script -ScriptBlock {
                param($root, $script)
                $ErrorActionPreference = 'Stop'
                Set-Location -LiteralPath $root
                $watch = [Diagnostics.Stopwatch]::StartNew()
                & powershell -NoProfile -ExecutionPolicy Bypass -File $script
                if ($LASTEXITCODE -ne 0) { throw "$script 失败。" }
                Write-Output "${script} 耗时：$($watch.Elapsed.TotalSeconds.ToString('F2')) 秒"
            }
        })
    }
    $reusePackage = & (Join-Path $PSScriptRoot 'package.ps1') $Version -ReuseOnly
    if ($first -le 1) {
        Measure-Step Package { Run { powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 $Version } }
    }
    if ($first -le 2) {
        if ($reusePackage) { Write-Output '已有包检查通过，跳过 Check。' }
        else {
            Measure-Step Check { Run { powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-package.ps1 -ZipPath $zip -Version $Version -SetupPath $setup -OfflineOnly } }
            $packageCache = Join-Path $release "package-$Version.json"
            if (Test-Path -LiteralPath $packageCache) {
                $record = Get-Content -LiteralPath $packageCache -Raw -Encoding UTF8 | ConvertFrom-Json
                $record.checked = $true
                $record | ConvertTo-Json | Set-Content -LiteralPath $packageCache -Encoding UTF8
            }
        }
    }
    if ($first -le 3) {
        Measure-Step Export {
            $exportJob | Wait-Job | Receive-Job -ErrorAction Stop
            if ($exportJob.State -ne 'Completed') { throw '导出检查失败。' }
        }
    }
    if ($LocalOnly) { Write-Output '本地验收结束；未推送、未创建 Release。'; exit 0 }
    if ($first -le 4) {
        Measure-Step Repository {
            $files = @(& git -c core.quotepath=false -C $public ls-files --modified --deleted --others --exclude-standard)
            if ($LASTEXITCODE -ne 0) { throw '读取公开仓库变更失败。' }
            if ($files.Count) {
                Run { git -C $public add -A }
                Run { git -C $public commit -m "Release v$Version" }
            }
            $tag = & git -C $public tag --list "v$Version"
            if (!$tag) { Run { git -C $public tag "v$Version" } }
            $head = & git -C $public rev-parse HEAD
            $tagHead = & git -C $public rev-list -n 1 "v$Version"
            if ($LASTEXITCODE -ne 0 -or $head -ne $tagHead) { throw '版本标签已指向其他提交，停止发布。' }
            Run { git -C $public push origin main "refs/tags/v$Version" }
        }
    }
    if ($first -le 5) {
        Measure-Step Release {
            if (!(Test-Path -LiteralPath $body)) { throw "缺少 $body。" }
            $previous = & gh release list --repo $repo --exclude-drafts --exclude-pre-releases --limit 4 --json tagName
            if ($LASTEXITCODE -ne 0) { throw '读取已发布版本失败。' }
            $tags = @($previous | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object {
                # 只给 1.3.7 起的版本出差量包：1.3.3～1.3.6 的更新脚本在宿主刚退出时文件还被占用就放弃（400），
                # 不给它们差量包，它们会直接下全量包；更早的版本用老更新器，本来就用不上差量包
                $_.tagName -match '^v\d+\.\d+\.\d+$' -and [version]$_.tagName.Substring(1) -lt [version]$Version -and [version]$_.tagName.Substring(1) -ge [version]'1.3.7'
            } | Select-Object -First 3 -ExpandProperty tagName)
            $deltas = @()
            foreach ($tag in $tags) {
                $baseDir = Join-Path $release "bases/$tag"
                New-Item -ItemType Directory -Force -Path $baseDir | Out-Null
                $baseName = "StrongholdAssistant-$tag-win-x64.zip"
                # 本地留着当时发布的 zip 就直接用（GitHub 下载常常很慢），仍按线上 SHA256SUMS 核对；没有才下载
                $local = Join-Path $release $baseName
                Run { gh release download $tag --repo $repo --dir $baseDir --pattern SHA256SUMS.txt --clobber }
                $sums = Get-Content -LiteralPath (Join-Path $baseDir 'SHA256SUMS.txt')
                $base = $local
                if (!(Test-Path -LiteralPath $local) -or "$((Get-FileHash -LiteralPath $local).Hash)  $baseName" -notin $sums) {
                    $base = Join-Path $baseDir $baseName
                    Run { gh release download $tag --repo $repo --dir $baseDir --pattern $baseName --clobber }
                    if ("$((Get-FileHash -LiteralPath $base).Hash)  $baseName" -notin $sums) { throw "旧版校验失败：$tag" }
                }
                $delta = Join-Path $release "StrongholdAssistant-v$Version-from-$tag-delta.zip"
                & "$PSScriptRoot/package-delta.ps1" -Previous $base -Current $zip -Output $delta
                if ($base -ne $local) { Remove-Item -LiteralPath $base }
                $deltas += $delta
            }
            $sums = Join-Path $release 'SHA256SUMS.txt'
            $lines = @(Get-Content -LiteralPath $sums | Where-Object { $_ -notmatch '-delta\.zip$' })
            $lines += @(foreach ($delta in $deltas) {
                "$((Get-FileHash -LiteralPath $delta).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($delta))"
            })
            [IO.File]::WriteAllLines($sums, [string[]]$lines, [Text.Encoding]::ASCII)
            Run { gh release create "v$Version" --repo $repo --verify-tag --title "高效雅雅·卫戍协议助手 v$Version" --notes-file $body $setup $zip @deltas (Join-Path $release 'SHA256SUMS.txt') (Join-Path $release 'release-notes.json') }
        }
    }
    Measure-Step Verify {
        $info = & gh api "repos/$repo/releases/tags/v$Version" | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw '读取 Release 附件失败。' }
        $sumsPath = Join-Path $release 'SHA256SUMS.txt'
        $expected = @{}
        foreach ($line in Get-Content -LiteralPath $sumsPath) {
            if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw 'SHA256SUMS.txt 格式错误。' }
            $expected[$Matches[2]] = $Matches[1].ToLowerInvariant()
        }
        $expected['SHA256SUMS.txt'] = (Get-FileHash -LiteralPath $sumsPath -Algorithm SHA256).Hash.ToLowerInvariant()
        foreach ($name in $expected.Keys) {
            $asset = @($info.assets | Where-Object name -eq $name)
            if ($asset.Count -ne 1 -or $asset[0].digest -cne "sha256:$($expected[$name])") { throw "附件 digest 不匹配：$name；保留 Release，请设计处理。" }
        }
        foreach ($file in @($setup, $zip)) {
            $url = "https://github.com/$repo/releases/download/v$Version/$([IO.Path]::GetFileName($file))"
            $status = & curl.exe --silent --show-error --head --output NUL --write-out '%{http_code}' $url
            if ($LASTEXITCODE -ne 0 -or $status -ne '302') { throw "下载直链 HEAD 应返回 302，实际 $status；保留 Release：$url" }
            Write-Output $url
        }
        Write-Output "https://github.com/$repo/releases/tag/v$Version"
    }
    exit 0
} catch {
    Write-Host "失败步骤：$step。$($_.Exception.Message)"
    $resume = if ($step -eq 'Preflight') { $From } else { $step }
    Write-Host "处理后续跑：scripts/release.ps1 $Version -From $resume $(if ($LocalOnly) { '-LocalOnly' })"
    if ($step -eq 'Release') { Write-Host '若上传实际已完成，请先检查附件，再用 -From Verify 核对，避免重复创建。' }
    throw
} finally {
    if ($exportJob) { $exportJob | Stop-Job; $exportJob | Remove-Job }
    foreach ($file in @($setup, $zip)) {
        if (Test-Path -LiteralPath $file) { Write-Output "$file ($((Get-Item -LiteralPath $file).Length) bytes)" }
    }
    foreach ($name in $times.Keys) { Write-Output "${name}：$($times[$name]) 秒" }
    Write-Output "总耗时：$($total.Elapsed.TotalSeconds.ToString('F2')) 秒（Export 在并行时为等待耗时）"
}
