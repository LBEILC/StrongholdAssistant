param([switch]$ForceTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$cache = Join-Path $root 'out/test-pass.txt'
# 比较提交中的输入 blob；界面、文档与脚本不参与 .NET 测试指纹。
function Inputs {
    $entries = @(& git -c core.quotepath=false ls-tree -r HEAD -- src data Directory.Build.props)
    if ($LASTEXITCODE -ne 0) { throw '无法读取测试输入树。' }
    return @($entries | Where-Object { ($_ -split "`t", 2)[1] -notlike 'src/ui/*' })
}
function IsClean {
    $changes = & git status --porcelain --untracked-files=all -- src data Directory.Build.props ':(exclude)src/ui'
    if ($LASTEXITCODE -ne 0) { throw '无法读取测试输入工作区状态。' }
    return !$changes
}
$inputs = @(Inputs)
$clean = IsClean
$filter = @()
if (!$ForceTests -and $clean -and (Test-Path -LiteralPath $cache)) {
    $pass = Get-Content -LiteralPath $cache -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($pass.inputs) {
        $changed = @(Compare-Object @($pass.inputs) $inputs | ForEach-Object { ($_.InputObject -split "`t", 2)[1] } | Sort-Object -Unique)
        if (!$changed.Count) {
            Write-Output "这份代码已跑过全量测试（$($pass.time)）"
            return
        }
        if (!@($changed | Where-Object { $_ -notlike 'src/StrongholdAssistant.Tests/*.cs' }).Count) {
            foreach ($file in $changed) {
                $sources = @()
                if (Test-Path -LiteralPath $file) { $sources += [IO.File]::ReadAllText((Join-Path $root $file)) }
                $old = @($pass.inputs | Where-Object { ($_ -split "`t", 2)[1] -eq $file })
                if ($old.Count) { $sources += (& git show "$($pass.commit):$file") -join "`n" }
                foreach ($source in $sources) {
                    $namespace = [regex]::Match($source, 'namespace\s+([\w.]+)\s*[;{]').Groups[1].Value
                    $classes = [regex]::Matches($source, '\bclass\s+(\w+)')
                    # 夹具或共享辅助代码可能影响其他类，此时仍跑全量。
                    if (!$namespace -or $classes.Count -ne 1 -or $source -notmatch '\[(Fact|Theory)\]') { $filter = @(); break }
                    $filter += "FullyQualifiedName~$namespace.$($classes[0].Groups[1].Value)."
                }
                if (!$filter.Count) { break }
            }
        }
    }
}
if (Test-Path -LiteralPath $cache) { Remove-Item -LiteralPath $cache }
& dotnet build src/StrongholdAssistant.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw '测试项目构建失败。' }
$arguments = @('test', 'src/StrongholdAssistant.Tests', '-c', 'Release', '--no-build', '--logger', 'trx;LogFileName=tests.trx', '--results-directory', 'out/test-results')
$label = '全量测试'
if ($filter.Count) {
    $label = '改动测试类'
    $selection = ($filter | Sort-Object -Unique) -join '|'
    Write-Output "只跑改动测试类：$selection"
    $arguments += @('--filter', $selection)
}
$timer = [Diagnostics.Stopwatch]::StartNew()
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '测试失败；未写入通过指纹。修复后重新运行 scripts/test-all.ps1。' }
if ($clean -and (IsClean) -and ((Inputs) -join "`n") -ceq ($inputs -join "`n")) {
    @{ inputs = $inputs; commit = (& git rev-parse HEAD); time = (Get-Date).ToString('o') } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $cache -Encoding UTF8
} else { Write-Output '测试通过，但测试输入有未提交改动，不记录通过指纹。' }
Write-Output "${label}耗时（不含编译）：$($timer.Elapsed.TotalSeconds.ToString('F1')) 秒"
