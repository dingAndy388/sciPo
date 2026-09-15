<#
================================================================================
  Science Potato - 一键三层验收（Tools/verify.ps1）
================================================================================
  用途（v0.6.7 / 工作流 W1）：
    把"主工程构建 → 无头检查 → Godot 无头冒烟"三步收成一条命令，
    并把完整输出写进 %TEMP%\sp-verify.txt（控制台只打结论行）。

  为什么需要它：单条命令有时长上限，而三层验收合起来要 40~60 秒；
  分成"后台启动 + 下一轮读一行结论"最稳，也顺手成为你/CI 的复现入口。

  用法：
    powershell -File Tools/verify.ps1                      # 全量（4 路并行分片）
    powershell -File Tools/verify.ps1 -Groups MonthlySet   # 只跑匹配的组（快）
    powershell -File Tools/verify.ps1 -Serial              # 串行跑全量（排查闪烁时用）
    powershell -File Tools/verify.ps1 -SkipSmoke           # 跳过 Godot 冒烟
    powershell -File Tools/verify.ps1 -FullSmoke           # 冒烟用 73×143 全尺寸

  退出码：0 = 三层全绿；1 = 有失败（细节见日志文件）。
================================================================================
#>
param(
	[string]$Groups = "",
	[switch]$SkipSmoke,
	[switch]$Serial,
	[switch]$FullSmoke
)

$ErrorActionPreference = 'Continue'

$root = Split-Path -Parent $PSScriptRoot
$log = Join-Path $env:TEMP 'sp-verify.txt'
$exe = Join-Path $root 'Tests\SciencePotato.HeadlessChecks\bin\Debug\net8.0\SciencePotato.HeadlessChecks.exe'
$godot = 'E:\Godot_v4.6-stable_mono_win64\Godot_v4.6-stable_mono_win64.exe'
$shards = 4

function Write-Log([string]$text) { Add-Content -Path $log -Value $text }
function Say([string]$text) { Write-Host $text }

Set-Content -Path $log -Value "=== Science Potato verify: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="
Write-Log "groups='$Groups' serial=$Serial skipSmoke=$SkipSmoke"

# ---- 0) 清残留进程：挂死的测试/引擎进程会让后面每一步都变慢（本轮踩过）----
foreach ($name in @('SciencePotato.HeadlessChecks', 'Godot_v4.6-stable_mono_win64')) {
	Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
		Write-Log "[cleanup] kill $($_.ProcessName) pid=$($_.Id)"
		Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
	}
}

# ---- 1) 构建（先主工程，再测试工程；都在同一步里串行，别和重活并发）----
Say '[1/3] build ...'
$sw = [Diagnostics.Stopwatch]::StartNew()
$buildMain = & dotnet build (Join-Path $root 'Science Potato.csproj') -v q --nologo 2>&1 | Out-String
$buildTests = & dotnet build (Join-Path $root 'Tests\SciencePotato.HeadlessChecks\SciencePotato.HeadlessChecks.csproj') -v q --nologo 2>&1 | Out-String
$buildMs = $sw.ElapsedMilliseconds
Write-Log "--- build (${buildMs}ms) ---"
Write-Log $buildMain.Trim()
Write-Log $buildTests.Trim()

$buildOk = ($buildMain -notmatch 'error [A-Z]{2}\d') -and ($buildTests -notmatch 'error [A-Z]{2}\d')
if (-not $buildOk) {
	Say "VERIFY FAIL build（详情：$log）"
	exit 1
}
if (-not (Test-Path $exe)) {
	Say "VERIFY FAIL 未找到测试可执行文件：$exe"
	exit 1
}

# ---- 2) 无头检查（默认并行分片；-Serial 串行）----
Say '[2/3] headless checks ...'
$sw.Restart()
$allGroups = @(& $exe --list-groups | Where-Object { $_ -and $_.Trim().Length -gt 0 })
if ($allGroups.Count -eq 0) { $allGroups = @('Wiring') }

if ($Groups) {
	$targets = @($allGroups | Where-Object { $_ -like "*$Groups*" })
	if ($targets.Count -eq 0) { $targets = @($Groups) }
	$buckets = @(, $targets)                       # 指定组 → 直接一个分片（本来就快）
} elseif ($Serial) {
	$buckets = @(, $allGroups)
} else {
	# 按"重量"不知道，就按名字轮转分片：每组独立进程、临时目录都是 GUID，可安全并行
	$buckets = @()
	for ($i = 0; $i -lt $shards; $i++) { $buckets += , @($allGroups | Where-Object { $allGroups.IndexOf($_) % $shards -eq $i }) }
	$buckets = @($buckets | Where-Object { $_.Count -gt 0 })
}

$procs = @()
$files = @()
for ($i = 0; $i -lt $buckets.Count; $i++) {
	$file = Join-Path $env:TEMP "sp-shard-$i.txt"
	$files += $file
	$argList = @()
	foreach ($g in $buckets[$i]) { $argList += "--only-group=$g" }   # 精确匹配：避免 "Config" 撞上 "ConfigTable"
	$procs += Start-Process -FilePath $exe -ArgumentList $argList -PassThru -RedirectStandardOutput $file
}
$procs | Wait-Process

$passed = 0; $failed = 0; $resultLines = @()
for ($i = 0; $i -lt $files.Count; $i++) {
	$content = Get-Content $files[$i] -ErrorAction SilentlyContinue
	$content | ForEach-Object { Write-Log $_ }
	$rl = ($content | Select-String '结果：' | Select-Object -Last 1)
	if ($rl) {
		$resultLines += "shard${i}: $($rl.Line.Trim())"
		if ($rl.Line -match '通过 (\d+) / 失败 (\d+)') { $passed += [int]$Matches[1]; $failed += [int]$Matches[2] }
	} else {
		$resultLines += "shard${i}: (无结果行)"
		$failed += 1
	}
}
$checkMs = $sw.ElapsedMilliseconds
Write-Log "--- headless (${checkMs}ms) ---"
$resultLines | ForEach-Object { Write-Log $_ }

if ($failed -gt 0) {
	Say "VERIFY FAIL headless（通过 $passed / 失败 $failed；详情：$log）"
	exit 1
}

# ---- 3) Godot 无头冒烟（真引擎：装配/资源/落盘/表现层）----
$smokeLine = 'skipped'
if (-not $SkipSmoke) {
	Say '[3/3] godot smoke ...'
	$sw.Restart()
	$sizeArg = if ($FullSmoke) { '--size=143x73' } else { '--size=24x24' }
	$smokeOut = Join-Path $env:TEMP 'sp-smoke-verify.txt'
	$smokeErr = Join-Path $env:TEMP 'sp-smoke-verify.err.txt'
	$p = Start-Process -FilePath $godot -ArgumentList '--headless', '--path', $root,
		'res://Scene/Dev/smoke_report.tscn', '--quit-after', '6000', '--log-file', (Join-Path $env:TEMP 'sp-smoke.log'),
		'--', '--smoke', $sizeArg -Wait -PassThru -RedirectStandardOutput $smokeOut -RedirectStandardError $smokeErr

	$smoke = Get-Content $smokeOut -ErrorAction SilentlyContinue
	$summary = ($smoke | Select-String '汇总' | Select-Object -Last 1)
	$smokeLine = if ($summary) { $summary.Line.Trim() } else { '(无汇总行)' }
	Write-Log "--- smoke exit=$($p.ExitCode) ($($sw.ElapsedMilliseconds)ms) ---"
	$smoke | ForEach-Object { Write-Log $_ }

	if ($p.ExitCode -ne 0) {
		Say "VERIFY FAIL smoke（exit=$($p.ExitCode)；详情：$log）"
		exit 1
	}
}

Say "VERIFY OK  headless=$passed/$($passed + $failed)  smoke=$smokeLine  日志=$log"
exit 0
