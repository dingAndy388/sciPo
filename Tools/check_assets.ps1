# （v0.9.11）**资源校验器（图 + 音）**：拿两份清单对盘上的文件逐条验收
#
#   Document/AssetManifest.csv  —— 图（由 Tools/gen_asset_manifest.ps1 生成）
#   Document/SoundManifest.csv  —— 音（由 Tools/gen_sound_list.ps1 生成）
#
# 能自动查：存在性 / 图片尺寸 / 图片透明通道 / 音频扩展名 / 目录里的“多余文件”
# 查不了（人工抽查）：音频响度与时长、图片风格 —— 见 Document/ArtSpec.md §11、Document/SoundSpec.md §4
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File Tools/check_assets.ps1
#   powershell -ExecutionPolicy Bypass -File Tools/check_assets.ps1 -Promote   # 合格的 → Status=delivered
param([switch]$Promote)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$docDir = Join-Path $root 'Document'
Add-Type -AssemblyName System.Drawing

$problems = New-Object System.Collections.Generic.List[string]
$expectedFiles = New-Object System.Collections.Generic.HashSet[string]
$promoted = 0

function Test-Row($row, [string]$manifestName) {
	$relative = $row.FilePath -replace '^res://', ''
	$full = Join-Path $root ($relative -replace '/', '\')
	$expectedFiles.Add(($relative -replace '/', '\')) | Out-Null

	if (-not (Test-Path $full)) {
		$problems.Add("[缺文件] $relative（$manifestName / $($row.Category) / $($row.Id)）")
		$row.Status = 'missing'
		return $false
	}

	$extension = [System.IO.Path]::GetExtension($relative).ToLowerInvariant()
	if ($extension -in @('.ogg', '.wav', '.mp3')) {
		if ($relative -match 'SFX' -and $extension -ne '.wav') { $problems.Add("[格式] $relative 音效应为 .wav"); return $false }
		if ($relative -match 'BGM' -and $extension -ne '.ogg') { $problems.Add("[格式] $relative 音乐应为 .ogg"); return $false }
		$row.Status = 'delivered'
		return $true
	}

	if (-not ($extension -in @('.png', '.jpg', '.jpeg', '.webp'))) { return $true }

	$sizeText = ($row.SizePx -split '[（(]')[0].Trim()
	if ($sizeText -notmatch '^(\d+)x(\d+)$') { $problems.Add("[规格] $relative 的 SizePx 无法解析：$($row.SizePx)"); return $false }
	$wantW = [int]$matches[1]; $wantH = [int]$matches[2]

	try { $image = [System.Drawing.Image]::FromFile($full) } catch { $problems.Add("[坏图] $relative 无法打开：$($_.Exception.Message)"); return $false }
	try {
		if ($image.Width -ne $wantW -or $image.Height -ne $wantH) {
			$problems.Add("[尺寸] $relative 实际 $($image.Width)x$($image.Height)，规格 $wantW`x$wantH")
			return $false
		}
		if ($image.PixelFormat.ToString() -notmatch 'Argb|PArgb|Alpha') {
			$problems.Add("[无透明通道] $relative 的 PixelFormat=$($image.PixelFormat)（需要 RGBA8）")
			return $false
		}
		$row.Status = 'delivered'
		return $true
	} finally { $image.Dispose() }
}

foreach ($name in @('AssetManifest.csv', 'SoundManifest.csv')) {
	$manifestPath = Join-Path $docDir $name
	if (-not (Test-Path $manifestPath)) { Write-Host "跳过（不存在）：Document/$name"; continue }

	$rows = Import-Csv $manifestPath
	$present = 0
	$delivered = 0

	foreach ($row in $rows) { if (Test-Row $row $name) { $present++ } }
	foreach ($row in $rows) { if ($row.Status -eq 'delivered') { $delivered++ } }

	if ($Promote) { $rows | Export-Csv -Path $manifestPath -NoTypeInformation -Encoding utf8; $promoted += $delivered }
	Write-Host ("{0,-20} 共 {1,3} 条：已有 {2,3}，合格 {3,3}" -f $name, $rows.Count, $present, $delivered)
}

foreach ($assetRoot in @('Texture', 'Audio')) {
	$full = Join-Path $root $assetRoot
	if (-not (Test-Path $full)) { continue }
	Get-ChildItem -Path $full -Recurse -File | Where-Object { $_.Name -notlike '*.import' } | ForEach-Object {
		$rel = $_.FullName.Substring($root.Length + 1)
		if (-not $expectedFiles.Contains($rel)) { $problems.Add("[多余] $rel 不在任何清单里（文件名写错了？）") }
	}
}

Write-Host ("问题 {0} 条：" -f $problems.Count)
$problems | Select-Object -First 40 | ForEach-Object { Write-Host "  $_" }
if ($problems.Count -gt 40) { Write-Host ("  ...（还有 {0} 条）" -f ($problems.Count - 40)) }
if ($Promote) { Write-Host "已回写 Status=delivered（$promoted 条）" }
if ($problems.Count -eq 0) { Write-Host '✅ 清单与盘上文件一致。' }