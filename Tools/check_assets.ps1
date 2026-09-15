# （v0.9.10 / WP-8.1）**美术资源校验器**：拿 `Document/AssetManifest.csv` 对盘上的文件逐条验收
#
# 作用：美术交付后跑一次，就知道"缺哪些、哪张尺寸不对、哪张没有透明通道、目录里有没有多余文件"。
#   规格见 `Document/ArtSpec.md`；清单由 `Tools/gen_asset_manifest.ps1` 生成（勿手改 CSV）。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File Tools/check_assets.ps1              # 只报告
#   powershell -ExecutionPolicy Bypass -File Tools/check_assets.ps1 -Promote     # 合格的图 → Status 改 delivered
param(
	[switch]$Promote
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'Document\AssetManifest.csv'
if (-not (Test-Path $manifestPath)) { throw "找不到清单：$manifestPath（先跑 Tools/gen_asset_manifest.ps1）" }

Add-Type -AssemblyName System.Drawing

$rows = Import-Csv $manifestPath
$problems = New-Object System.Collections.Generic.List[string]
$present = 0
$ok = 0
$delivered = 0
$expectedFiles = New-Object System.Collections.Generic.HashSet[string]

foreach ($row in $rows) {
	$relative = $row.FilePath -replace '^res://', ''
	$full = Join-Path $root ($relative -replace '/', '\')
	$expectedFiles.Add(($relative -replace '/', '\')) | Out-Null

	if (-not (Test-Path $full)) {
		$problems.Add(("[缺图] {0}（{1}，{2}）" -f $relative, $row.Category, $row.SizePx))
		$row.Status = 'missing'
		continue
	}

	$present++

	if ($row.SizePx -eq '—') { $ok++; continue } # 音频没有尺寸约束

	# 期望尺寸：'366x423' 或 '96x96(九宫格 24)' 两种写法都取前一段
	$sizeText = ($row.SizePx -split '[（(]')[0].Trim()
	if ($sizeText -notmatch '^(\d+)x(\d+)$') { $problems.Add(("[规格] {0} 的 SizePx 写法无法解析：{1}" -f $relative, $row.SizePx)); continue }
	$wantW = [int]$matches[1]
	$wantH = [int]$matches[2]

	# 只有图片才查尺寸/alpha
	if ($relative -notmatch '\.(png|jpg|jpeg|webp)$') { $ok++; continue }

	try {
		$image = [System.Drawing.Image]::FromFile($full)
	} catch {
		$problems.Add(("[坏图] {0} 无法打开：{1}" -f $relative, $_.Exception.Message))
		continue
	}

	try {
		if ($image.Width -ne $wantW -or $image.Height -ne $wantH) {
			$problems.Add(("[尺寸] {0} 实际 {1}x{2}，规格要求 {3}x{4}" -f $relative, $image.Width, $image.Height, $wantW, $wantH))
			continue
		}

		$hasAlpha = $image.PixelFormat.ToString() -match 'Argb|PArgb|Alpha'
		if (-not $hasAlpha) {
			$problems.Add(("[无透明通道] {0} 的 PixelFormat={1}（需要 RGBA8）" -f $relative, $image.PixelFormat))
			continue
		}

		$ok++
		$row.Status = 'delivered'
		$delivered++
	} finally {
		$image.Dispose()
	}
}

# 目录里的"多余文件"（清单没登记、但在资源目录里）—— 防"文件名写错导致悄悄不生效"
$assetRoots = @('Texture', 'Audio') | ForEach-Object { Join-Path $root $_ }
foreach ($assetRoot in $assetRoots) {
	if (-not (Test-Path $assetRoot)) { continue }
	Get-ChildItem -Path $assetRoot -Recurse -File | Where-Object { $_.Extension -in @('.png', '.jpg', '.jpeg', '.webp', '.ogg', '.wav', '.mp3', '.import') -and $_.Name -notlike '*.import' } | ForEach-Object {
		$rel = $_.FullName.Substring($root.Length + 1)
		if (-not $expectedFiles.Contains($rel)) { $problems.Add(("[多余] $rel 不在清单里（文件名写错了？）")) }
	}
}

Write-Host ("清单 {0} 条：已有 {1} 个，合格 {2} 个，其中标记 delivered {3} 个" -f $rows.Count, $present, $ok, $delivered)
Write-Host ("问题 {0} 条：" -f $problems.Count)
$problems | Select-Object -First 40 | ForEach-Object { Write-Host "  $_" }
if ($problems.Count -gt 40) { Write-Host ("  ...（还有 {0} 条）" -f ($problems.Count - 40)) }

if ($Promote) {
	$rows | Export-Csv -Path $manifestPath -NoTypeInformation -Encoding utf8
	Write-Host "已回写 Status（delivered）到 Document/AssetManifest.csv"
}

if ($present -eq 0) { Write-Host '提示：目前一张图都没有 —— 缺图不会崩，表现层会自动回退占位（见 Document/ArtSpec.md 的"回退保证"）。' }
