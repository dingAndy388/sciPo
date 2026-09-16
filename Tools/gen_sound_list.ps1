# （v0.9.11 / WP-8.4 前置）**音频清单生成器**：把音效契约（id/触发点/时长建议）→ Document/SoundManifest.csv + Document/SoundList.md
#
# 为什么单独一套文档：美术与音频**组内分工不同** —— 图看 ArtSpec/AssetList，音看 SoundSpec/SoundList，两份清单不重叠。
# id 与代码同源：`Scripts/Audio/Domain/SoundCatalog.cs` 是 id 的唯一出处，改 id 必须两边一起改（无头用例 `SoundChecks` 会锁住）。
#
# 用法：powershell -ExecutionPolicy Bypass -File Tools/gen_sound_list.ps1
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$docDir = Join-Path $root 'Document'

# Category, Id, DurationHint, Trigger（什么时候响）, Priority, Notes
$SFX_SPEC = @(
	@('UI',        'ui_click',          '0.05-0.15s', '任何意图被接受（建造/训练/研究/移动/事件决策成功）', 'P1', '最常用的反馈音'),
	@('UI',        'ui_cancel',         '0.05-0.20s', '右键 / ESC 退出建造或单位指令模式', 'P1', ''),
	@('UI',        'ui_reject',         '0.10-0.30s', '任何意图被拒绝（资源不足 / 门控未解锁 / 格位占用 / 越权）', 'P1', '拒绝原因很多，提示音只一条'),
	@('UI',        'ui_hover',          '0.05s',      '鼠标悬停按钮或格子（可选，可关）', 'P2', '音要极轻，避免吵'),
	@('UI',        'ui_panel_open',     '0.10-0.30s', '打开面板（研究 / 建造 / 资源）', 'P2', ''),
	@('UI',        'ui_panel_close',    '0.10-0.30s', '关闭面板', 'P2', ''),
	@('UI',        'ui_tab',            '0.05-0.15s', '分类页签切换', 'P2', ''),
	@('UI',        'game_start',        '0.5-1.5s',   '新开局成功（生成地图 + 势力就位）', 'P1', '可与 BGM 起播叠加'),
	@('UI',        'game_save',         '0.20-0.50s', '存档完成（自动存档点也算）', 'P1', ''),
	@('UI',        'game_load',         '0.20-0.80s', '读档完成', 'P1', ''),
	@('UI',        'victory',           '1-3s',       '胜负判定：己方获胜', 'P1', '可带一点情绪上扬'),
	@('UI',        'defeat',            '1-3s',       '胜负判定：己方出局', 'P1', ''),
	@('Build',     'build_start',       '0.30-0.80s', '施工点落位（建造意图成功）', 'P1', ''),
	@('Build',     'build_complete',    '0.5-1.5s',   '建筑完工（BuildingCompletedEvent）', 'P1', ''),
	@('Build',     'upgrade_start',     '0.30-0.80s', '开始升级建筑', 'P1', ''),
	@('Build',     'upgrade_complete',  '0.5-1.5s',   '升级完成（BuildingUpgradedEvent）', 'P1', ''),
	@('Build',     'building_captured', '0.5-1.5s',   '建筑易主（BuildingCapturedEvent）', 'P1', ''),
	@('Build',     'building_destroyed','0.5-1.5s',   '建筑被拆除/摧毁（BuildingRemovedEvent）', 'P1', ''),
	@('Build',     'train_start',       '0.20-0.50s', '建筑下单训练', 'P1', ''),
	@('Build',     'unit_trained',      '0.30-0.80s', '单位训练完成（UnitTrainedEvent）', 'P1', ''),
	@('Unit',      'unit_move_order',   '0.10-0.30s', '移动指令下达（可随时改目的地）', 'P1', ''),
	@('Unit',      'unit_attack',       '0.20-0.60s', '攻击命中（可做 3 个变体；同帧只响一次）', 'P1', '变体命名 unit_attack_01/02/03'),
	@('Unit',      'unit_hit',          '0.20-0.50s', '己方单位受击', 'P2', ''),
	@('Unit',      'unit_died',         '0.30-0.80s', '单位阵亡（UnitDiedEvent，敌我共用）', 'P1', ''),
	@('Unit',      'unit_loot',         '0.20-0.50s', '击杀掉落拾取', 'P2', ''),
	@('Tech',      'research_start',    '0.30-0.80s', '研究开工', 'P1', ''),
	@('Tech',      'research_complete', '0.60-1.5s',  '研究完成（ResearchCompletedEvent）', 'P1', ''),
	@('Tech',      'tech_ui_unlocked',  '0.30-1.0s',  '科技解锁界面能力（如收获面板首次可用）', 'P2', ''),
	@('Event',     'event_trigger',     '1-2s',       '随机事件触发（GameEventTriggeredEvent，触发即暂停）', 'P1', '要"打断感"，但别盖过弹窗阅读'),
	@('Event',     'event_accept',      '0.30-0.80s', '事件弹窗：确定', 'P1', ''),
	@('Event',     'event_dismiss',     '0.20-0.50s', '事件弹窗：忽略/跳过', 'P2', ''),
	@('Resource',  'monthly_settle',    '0.5-1.5s',   '月结到账（产出 - 维护费）', 'P1', '每月一次，别做太响'),
	@('Resource',  'resource_low',      '0.30-0.80s', '资源告警（低于阈值 / 因资源不足被拒）', 'P2', ''),
	@('Resource',  'population_growth', '0.20-0.50s', '人口 +1（住房节拍）', 'P2', ''),
	@('Resource',  'deficit_warning',   '0.50-1.2s',  '赤字减员（连续赤字月结算）', 'P2', ''),
	@('Env',       'fog_reveal',        '0.20-0.50s', '视野揭开（迷雾首次变可见）', 'P2', '')
)

# 音乐（BGM）：文件用 ogg（无缝循环）
$BGM_SPEC = ,@('BGM', 'main', '60-120s（无缝循环）', '开局起循环播放；暂停菜单淡出', 'P1', 'N4 已有文件（44.1kHz 立体声）')

$rows = New-Object System.Collections.Generic.List[object]

function Add-SoundRow([string[]]$spec) {
	$category = $spec[0]; $id = $spec[1]; $duration = $spec[2]; $trigger = $spec[3]; $priority = $spec[4]; $notes = $spec[5]
	$isBgm = $category -eq 'BGM'
	$relative = if ($isBgm) { "Audio/BGM/$id.ogg" } else { "Audio/SFX/$id.wav" }
	$format = if ($isBgm) { 'ogg 44.1kHz 立体声 无缝循环' } else { 'wav 44.1kHz 16-bit 单声道' }

	$full = Join-Path $root ($relative -replace '/', '\')
	$rows.Add([pscustomobject]@{
		Category     = $category
		Id           = $id
		FilePath     = 'res://' + $relative
		Format       = $format
		DurationHint = $duration
		Trigger      = $trigger
		Priority     = $priority
		Status       = if (Test-Path $full) { 'present' } else { 'missing' }
		Owner        = '待定'
		License      = '待定'
		Notes        = $notes
	})
}

foreach ($spec in $SFX_SPEC) { Add-SoundRow $spec }
foreach ($spec in $BGM_SPEC) { Add-SoundRow $spec }

$csvPath = Join-Path $docDir 'SoundManifest.csv'
$rows | Export-Csv -Path $csvPath -NoTypeInformation -Encoding utf8

$minimal = @('ui_click','ui_reject','build_start','build_complete','unit_trained','unit_move_order','unit_attack','research_complete','event_trigger')
$md = New-Object System.Collections.Generic.List[string]
$md.Add('# Science Potato · 音频资源清单（自动生成，勿手改）')
$md.Add('')
$md.Add('> 由 `Tools/gen_sound_list.ps1` 从音效契约（`Scripts/Audio/Domain/SoundCatalog.cs`）生成 —— **id 就是文件名**，与代码同源、不会漂移。')
$md.Add("> 规格与验收见 `Document/SoundSpec.md`；署名与许可见 `Document/Credits.md`。")
$md.Add("> 合计 **$($rows.Count)** 个文件（音效 $($SFX_SPEC.Count) + 音乐 $($BGM_SPEC.Count)）；当前缺 **$(@($rows | Where-Object { $_.Status -eq 'missing' }).Count)** 个。")
$md.Add('')
$md.Add('## 最小可玩音效包（先做这 9 条就能听出手感）')
$md.Add('')
$md.Add('| 文件 | 说明 |')
$md.Add('| :--- | :--- |')
foreach ($id in $minimal) {
	$row = $rows | Where-Object { $_.Id -eq $id } | Select-Object -First 1
	$md.Add("| ``res://Audio/SFX/$id.wav`` | $($row.Trigger) |")
}
$md.Add('')
foreach ($group in ($rows | Group-Object Category)) {
	$md.Add("## $($group.Name)（$($group.Count) 条）")
	$md.Add('')
	$md.Add('| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |')
	$md.Add('| :--- | :--- | :--- | :--- | :--- | :--- |')
	foreach ($r in $group.Group) {
		$file = ($r.FilePath -replace 'res://', '')
		$notes = if ($r.Notes) { $r.Notes } else { '' }
		$md.Add("| ``$file`` | $($r.Format) | $($r.DurationHint) | $($r.Trigger) | $($r.Priority) | $notes |")
	}
	$md.Add('')
}
$mdPath = Join-Path $docDir 'SoundList.md'
[System.IO.File]::WriteAllLines($mdPath, $md, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "已生成：$csvPath（$($rows.Count) 行）"
Write-Host "已生成：$mdPath"
Write-Host ("音效 {0} 条（P1 {1} / P2 {2}）+ 音乐 {3} 首" -f $SFX_SPEC.Count, @($SFX_SPEC | Where-Object { $_[4] -eq 'P1' }).Count, @($SFX_SPEC | Where-Object { $_[4] -eq 'P2' }).Count, $BGM_SPEC.Count)