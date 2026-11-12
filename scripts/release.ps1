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
        # 两者只读同一提交，输出到不同目录，可以各自与打包同时运行。
        $exportJob = @(foreach ($script in @('scripts/export-public.ps1', 'tools/check-export.ps1')) {
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
    if ($first -le 1) {
        Measure-Step Package { Run { powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 $Version } }
    }
    if ($first -le 2) {
        Measure-Step Check { Run { powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-package.ps1 -ZipPath $zip -Version $Version -SetupPath $setup -OfflineOnly } }
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
                Run { git -C $public add -- $files }
                Run { git -C $public commit -m "Release v$Version" -- $files }
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
            Run { gh release create "v$Version" --repo $repo --verify-tag --title "卫戍协议助手 v$Version" --notes-file $body $setup $zip (Join-Path $release 'SHA256SUMS.txt') (Join-Path $release 'release-notes.json') }
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
