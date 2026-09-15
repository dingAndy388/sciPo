# （v0.9.10 / WP-8.1）**美术清单生成器**：把四张配置表 + UI/音频规格 → `Document/AssetManifest.csv` 与 `Document/AssetList.md`
#
# 为什么必须生成而不是手写：表的 id 就是文件名，手写清单一定会和表漂移
# （旧 manifest 只有 47 行、且地形尺寸写的 352x406 与代码几何 366x423 不符 —— 就是手抄病的证据）。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File Tools/gen_asset_manifest.ps1
#   powershell -ExecutionPolicy Bypass -File Tools/gen_asset_manifest.ps1 -WhatIfMissing   # 只打印缺图统计
#
# 输出列（固定顺序，别改）：Category,Id,ConfigSource,FilePath,SizePx,Anchor,Status,Owner,License,Notes
#   Status 由**文件是否在盘上**自动判定：missing（还没有）/ present（有文件，等校验/等风格评审）
#   Owner/License 由美术交付时填写（授权口径见 Document/ArtSpec.md）
param(
	[switch]$WhatIfMissing
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$configDir = Join-Path $root 'Config'
$docDir = Join-Path $root 'Document'

# ─────────────── 规格常量（与 Document/ArtSpec.md 一一对应） ───────────────

# 地形：宽 = 列步长(CellXStep=366)；高 = 行步长(CellYStep=317.25) × 4/3 = 423
$TERRAIN_SIZE = '366x423'
$BUILDING_SIZE = '256x256'
$UNIT_SIZE = '128x128'
$RESOURCE_SIZE = '64x64'
$EVENT_SIZE = '512x288'
$TECH_SIZE = '96x96'

# UI 规格（28 项：23 必需 + 5 可选）。SizePx 的 '九宫格' 表示按该尺寸画、四边留边距可拉伸。
$UI_SPEC = @(
	@('panel_bg',         '96x96(九宫格 24)', 'Center', 'P1', '通用面板底（研究/建造/事件弹窗共用）'),
	@('button_normal',    '96x96(九宫格 24)', 'Center', 'P1', '按钮：常态'),
	@('button_hover',     '96x96(九宫格 24)', 'Center', 'P1', '按钮：悬停'),
	@('button_pressed',   '96x96(九宫格 24)', 'Center', 'P1', '按钮：按下'),
	@('button_icon',      '64x64(九宫格 16)', 'Center', 'P1', '图标按钮底'),
	@('tab_normal',       '64x64(九宫格 16)', 'Center', 'P1', '分类页签：常态'),
	@('tab_active',       '64x64(九宫格 16)', 'Center', 'P1', '分类页签：选中'),
	@('icon_speed_pause', '48x48',            'Center', 'P1', '时间：暂停'),
	@('icon_speed_1x',    '48x48',            'Center', 'P1', '时间：1 档'),
	@('icon_speed_3x',    '48x48',            'Center', 'P1', '时间：3 档'),
	@('icon_speed_6x',    '48x48',            'Center', 'P1', '时间：6 档'),
	@('icon_population',  '48x48',            'Center', 'P1', '人口'),
	@('icon_research',    '48x48',            'Center', 'P1', '研究面板入口'),
	@('icon_build',       '48x48',            'Center', 'P1', '建造入口'),
	@('icon_move',        '48x48',            'Center', 'P1', '指令：移动'),
	@('icon_attack',      '48x48',            'Center', 'P1', '指令：攻击'),
	@('icon_hold',        '48x48',            'Center', 'P1', '指令：待命'),
	@('select_hex',       '366x423',          'Center', 'P1', '选中格高亮（与地形同几何）'),
	@('tech_node_frame',  '96x96',            'Center', 'P1', '科技节点框（4 态用 modulate，不用 4 张）'),
	@('progress_fill',    '32x32(九宫格 8)',  'Center', 'P1', '进度条填充（也可纯色，代码兜底）'),
	@('main_menu_bg',     '1920x1080',        'Center', 'P1', '主菜单背景'),
	@('victory_bg',       '1920x1080',        'Center', 'P1', '结局：胜利'),
	@('defeat_bg',        '1920x1080',        'Center', 'P1', '结局：失败'),
	@('logo',             '512x256',          'Center', 'P2', '标题 logo（可选）'),
	@('range_hex',        '366x423',          'Center', 'P2', '攻击/视野范围覆盖（可选）'),
	@('cursor_default',   '32x32',            'Center', 'P2', '光标：默认（可选）'),
	@('cursor_build',     '32x32',            'Center', 'P2', '光标：建造（可选）'),
	@('cursor_attack',    '32x32',            'Center', 'P2', '光标：攻击（可选）')
)

# 音频：BGM 一首（N4 已有）；音效按 N4 决定暂缓，不列进清单
# 注意 `,@(...)`：PowerShell 会把"单元素数组"展开 ⇒ 必须用逗号强制成"数组的数组"
$AUDIO_SPEC = ,@(
	'BGM_main', 'Audio/BGM/main.ogg', '—', 'P1', '主 BGM：ogg、44.1kHz 立体声、无缝循环、60~120s'
)


# ─────────────── 工具函数 ───────────────

$rows = New-Object System.Collections.Generic.List[object]

function Add-Row([string]$category, [string]$id, [string]$configSource, [string]$relativePath, [string]$sizePx, [string]$anchor, [string]$priority, [string]$notes) {
	$full = Join-Path $root ($relativePath -replace '/', '\')
	$status = if (Test-Path $full) { 'present' } else { 'missing' }

	$rows.Add([pscustomobject]@{
		Category     = $category
		Id           = $id
		ConfigSource = $configSource
		FilePath     = 'res://' + $relativePath
		SizePx       = $sizePx
		Anchor       = $anchor
		Status       = $status
		Owner        = '待定'
		License      = '待定'
		Notes        = if ($priority -eq 'P1') { $notes } else { "$notes【P2 可选】" }
	})
}

function Read-Table([string]$fileName) {
	$path = Join-Path $configDir $fileName
	if (-not (Test-Path $path)) { throw "配置表不存在：$path" }
	return Get-Content $path -Raw | ConvertFrom-Json
}

# ─────────────── 从表里抽 id（表即文件名：id 一律小写+下划线，与 `FilePath` 完全一致） ───────────────

$terrains = Read-Table 'Terrains.json'
foreach ($t in $terrains.Terrains) {
	# 数组：id 在 `Id` 字段（旧清单手抄时漏了这一点）
	Add-Row 'Terrain' $t.Id 'Config/Terrains.json' "Texture/Terrain/$($t.Id).png" $TERRAIN_SIZE 'Center' 'P1' "$($t.Name)（平顶六边形：左右尖角贴边、上下边水平、四角透明）"
}

$buildings = Read-Table 'Buildings.json'
foreach ($b in $buildings.Buildings.PSObject.Properties) {
	$level = if ($b.Value.UpgradeTo) { "可升级→$($b.Value.UpgradeTo)" } else { '末级' }
	Add-Row 'Building' $b.Name 'Config/Buildings.json' "Texture/Building/$($b.Name).png" $BUILDING_SIZE 'BottomCenter' 'P1' "$($b.Value.Name)（$level；脚底贴下边、水平居中）"
}

$units = Read-Table 'Units.json'
foreach ($u in $units.Units.PSObject.Properties) {
	$side = if ($u.Value.IsHostile) { '敌方' } else { '玩家' }
	Add-Row 'Unit' $u.Name 'Config/Units.json' "Texture/Unit/$($u.Name).png" $UNIT_SIZE 'BottomCenter' 'P1' "$side 单位（脚底贴下边、朝右下）"
}

$resources = Read-Table 'Resources.json'
foreach ($r in $resources.Resources) {
	Add-Row 'Resource' $r.Name 'Config/Resources.json' "Texture/Resource/$($r.Name).png" $RESOURCE_SIZE 'Center' 'P1' '顶栏 / 面板小图标'
}

$events = Read-Table 'Events.json'
foreach ($e in $events.Events) {
	Add-Row 'Event' $e.EventId 'Config/Events.json' "Texture/Event/$($e.EventId).png" $EVENT_SIZE 'Center' 'P1' "$($e.Name)（事件弹窗插图，16:9）"
}

$techs = Read-Table 'TechTrees.json'
foreach ($tree in $techs.TechTrees.PSObject.Properties) {
	foreach ($node in $tree.Value.Techs.PSObject.Properties) {
		Add-Row 'TechIcon' "$($tree.Name)/$($node.Name)" 'Config/TechTrees.json' "Texture/Tech/$($tree.Name)/$($node.Name).png" $TECH_SIZE 'Center' 'P1' "$($node.Value.Name)（$($tree.Name) 树）"
	}
}

foreach ($ui in $UI_SPEC) {
	Add-Row 'UI' $ui[0] 'Document/ArtSpec.md' "Texture/UI/$($ui[0]).png" $ui[1] $ui[2] $ui[3] $ui[4]
}

foreach ($audio in $AUDIO_SPEC) {
	Add-Row 'Audio' $audio[0] 'Document/ArtSpec.md' $audio[1] $audio[2] '—' $audio[3] $audio[4]
}

# ─────────────── 输出 ───────────────

$byCategory = $rows | Group-Object Category | Sort-Object Name
$missing = @($rows | Where-Object { $_.Status -eq 'missing' })

if ($WhatIfMissing) {
	foreach ($g in $byCategory) {
		$m = @($g.Group | Where-Object { $_.Status -eq 'missing' }).Count
		Write-Host ("  {0,-10} 共 {1,3} 个，缺 {2,3} 个" -f $g.Name, $g.Count, $m)
	}
	Write-Host ("总计 {0} 个文件，缺 {1} 个" -f $rows.Count, $missing.Count)
	exit 0
}

if (-not (Test-Path $docDir)) { New-Item -ItemType Directory -Path $docDir | Out-Null }

$csvPath = Join-Path $docDir 'AssetManifest.csv'
$rows | Export-Csv -Path $csvPath -NoTypeInformation -Encoding utf8

$md = New-Object System.Collections.Generic.List[string]
$md.Add('# Science Potato · 美术资源清单（自动生成，勿手改）')
$md.Add('')
$md.Add('> 由 `Tools/gen_asset_manifest.ps1` 从四张配置表 + `Document/ArtSpec.md` 生成 —— **表的 id 就是文件名**，清单不会和表漂移。')
$md.Add("> 合计 **$($rows.Count)** 个文件；当前缺 **$($missing.Count)** 个。规格与验收见 `Document/ArtSpec.md`。")
$md.Add('')
$md.Add('| 类别 | 数量 | 缺 | 典型尺寸 | 存放目录 |')
$md.Add('| :--- | ---: | ---: | :--- | :--- |')
$dirOf = @{
	Terrain = 'res://Texture/Terrain/'; Building = 'res://Texture/Building/'; Unit = 'res://Texture/Unit/'
	Resource = 'res://Texture/Resource/'; Event = 'res://Texture/Event/'; TechIcon = 'res://Texture/Tech/{树}/'
	UI = 'res://Texture/UI/'; Audio = 'res://Audio/'
}
foreach ($g in $byCategory) {
	$size = ($g.Group | Select-Object -First 1).SizePx
	$m = @($g.Group | Where-Object { $_.Status -eq 'missing' }).Count
	$md.Add("| $($g.Name) | $($g.Count) | $m | $size | $($dirOf[$g.Name]) |")
}
$md.Add('')
$md.Add('## 交付分包建议（每包到位即可见效果，缺图自动回退占位）')
$md.Add('')
$md.Add('| 包 | 内容 | 数量 |')
$md.Add('| :--- | :--- | ---: |')
$md.Add('| 01 最小可玩包 | 5 地形 + camp/farm/mine + worker/swordsman + 3 资源图标 + panel_bg + 按钮三态 | 16 |')
$md.Add('| 02 建筑全量 | 其余 21 张建筑（含 I/II/III 各级） | 21 |')
$md.Add('| 03 单位与敌人 | 其余 13 张单位 | 13 |')
$md.Add('| 04 事件插图 | 20 张 | 20 |')
$md.Add('| 05 科技图标 | 93 张（按树分目录） | 93 |')
$md.Add('| 06 UI 与结局 | 其余 UI 25 张 | 25 |')
$md.Add('| 07 BGM | 1 首（已有则跳过） | 1 |')
$md.Add('')
foreach ($g in $byCategory) {
	$md.Add("## $($g.Name)（$($g.Count) 个）")
	$md.Add('')
	$md.Add('| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |')
	$md.Add('| :--- | :--- | :--- | :--- | :--- |')
	foreach ($r in ($g.Group | Sort-Object Id)) {
		$file = ($r.FilePath -replace 'res://', '')
		$md.Add("| ``$file`` | $($r.SizePx) | $($r.Anchor) | $($r.Status) | $($r.Notes) |")
	}
	$md.Add('')
}
$mdPath = Join-Path $docDir 'AssetList.md'
[System.IO.File]::WriteAllLines($mdPath, $md, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "已生成：$csvPath（$($rows.Count) 行）"
Write-Host "已生成：$mdPath"
foreach ($g in $byCategory) {
	$m = @($g.Group | Where-Object { $_.Status -eq 'missing' }).Count
	Write-Host ("  {0,-10} 共 {1,3} 个，缺 {2,3} 个" -f $g.Name, $g.Count, $m)
}

