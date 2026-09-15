# （v0.9.0 / WP-7.3 + WP-7.6）把用例/配置里对**旧示例科技表**的引用迁移到设计稿真表（93 节点）。
# 用法： powershell -ExecutionPolicy Bypass -File Tools/migrate_tech_v2.ps1
# 口径（D110 诊断 + D109 命名）：树 Id = math/physics/chemistry；节点 Id = 英文 slug（见 gen_tech.ps1）；
# 旧示例表的 `science`/`military` 树与 `writing`/`mathematics` 单链**已废弃**。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tests = Join-Path $root 'Tests\SciencePotato.HeadlessChecks'
$crlf = [char]13 + [char]10
$TAB = [char]9

function Patch([string]$file, [object[][]]$pairs) { PatchFile (Join-Path 'Tests\SciencePotato.HeadlessChecks' $file) $pairs }

function PatchFile([string]$rel, [object[][]]$pairs) {
  $path = Join-Path $root $rel
  $text = [System.IO.File]::ReadAllText($path)
  $miss = @()
  foreach ($p in $pairs) {
    if ($text.Contains($p[0])) { $text = $text.Replace($p[0], $p[1]) } else { $miss += $p[0] }
  }
  [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host ("  {0}：命中 {1} / 未命中 {2}" -f $rel, ($pairs.Count - $miss.Count), $miss.Count)
  foreach ($m in $miss) { Write-Host ("    MISS: " + $m.Substring(0, [Math]::Min(60, $m.Length))) }
}

# ① 配置表：AI 流派偏好 + 建筑升级前置（按设计稿『升级条件』列）+ 事件前置
PatchFile 'Config\AI.json' @(, @('"SciencePreference": "science"', '"SciencePreference": "math"'))
PatchFile 'Config\Events.json' @(, @('"TechPrerequisites": { "science": [ "mathematics" ] }', '"TechPrerequisites": { "math": [ "counting" ] }'))
PatchFile 'Config\Buildings.json' @(
  , @('"UpgradeTechRequirements": { "science": [ "mathematics" ] }', '"UpgradeTechRequirements": { "math": [ "basic_geometry" ] }')
  , @('"UpgradeTechRequirements": { "physics": [ "simple_machine_intuition" ] }', '"UpgradeTechRequirements": { "math": [ "preliminary_survey" ] }')
  , @('"UpgradeTechRequirements": { "military": [ "melee_weapons" ] }', '"UpgradeTechRequirements": { "math": [ "preliminary_survey" ] }')
  , @('"UpgradeTechRequirements": { "military": [ "advanced_armor" ] }', '"UpgradeTechRequirements": { "physics": [ "lever_balance" ] }')
)

# ② 只用"根节点 + 本树前置"这一层语义的文件
$basic = @(
  , @('"science", "mathematics"', '"math", "counting"')
  , @('"science"', '"math"')
  , @('"writing"', '"counting"')
)
Patch 'DomainEventChecks.cs' $basic
Patch 'EventEngineChecks.cs' $basic
Patch 'SaveUnitChecks.cs' $basic

# ③ 前置机制用例：`counting` 仍是数学树根 ✓；"本树前置"这一段用 算术 ← 计数（设计稿）
Patch 'TechPrerequisiteChecks.cs' @(
  , @('"science"', '"math"')
  , @('"writing"', '"counting"')
  , @('"mathematics"', '"arithmetic"')
  , @('science/counting', 'math/counting')
)

# ④ ConfigTableChecks：节点可检索断言换设计表真节点
Patch 'ConfigTableChecks.cs' @(
  , @('GetTechNodeConfig("science", "mathematics")', 'GetTechNodeConfig("math", "pythagorean_school")')
  , @('GetTechNodeConfig("science", "counting")', 'GetTechNodeConfig("math", "counting")')
  , @('science/mathematics 节点应可检索', 'math/pythagorean_school 节点应可检索')
)

# ⑤ AI 配置用例：流派偏好现在应是 `math`
Patch 'AiConfigChecks.cs' @(, @('"science"', '"math"'))

# ⑥ UI 门控用例（WP-4.14）：收获面板现在挂在 算术 上（旧占位 writing 已废弃）
Patch 'InfoGateChecks.cs' @(
  , @('Research(MapId, 1, "science", "writing")', 'Research(MapId, 1, "math", "算术")')
  , @('Research(MapId, 1, "science", "counting")', 'Research(MapId, 1, "math", "counting")')
  , @('占位节点', '算术 节点')
)

# ⑦ 升级门控：夹具直落（避免前置链深度绑架用例）；节点名换成设计表真节点
Patch 'UpgradeChecks.cs' @(
  , @('Tech.Research(MapId, OwnerId, treeId, nodeId);', 'Tech.GetOrCreateTechTree(MapId, OwnerId, treeId).Research(nodeId);')
  , @('Research("science", "writing");', 'Research("math", "arithmetic");')
  , @('Research("science", "counting");', 'Research("math", "counting");')
  , @('Research("science", "mathematics");', 'Research("math", "pythagorean_school");')
  , @('h.Research("science", "writing");', 'h.Research("math", "basic_geometry");')
  , @('h.Research("science", "mathematics");', 'h.Research("math", "pythagorean_school");')
)
Write-Host '配置表 + 6 个用例文件迁移完成。'

# ⑧ 并发语义用例：样本换到 chemistry 树（根 `taming_of_fire` 用 **app 级**研究 + 推进天数落库）
Patch 'TechTreeConcurrencyChecks.cs' @(
  , @('new[] { "military", "science", "physics" }', 'new[] { "math", "physics", "chemistry" }')
  , @('"military"', '"physics"')
  , @('"science"', '"chemistry"')
  , @('"writing"', '"pottery"')
  , @('"counting"', '"potash_alkali"')
  , @('"melee_weapons"', '"simple_machine_intuition"')
  , @('h.Clock.AdvanceDays(5);', 'h.Clock.AdvanceDays(30);')
  , @('"writing 是根节点"', '"pottery 前置（火的驯服）满足 → 可研究"')
  , @('"counting 本身够格研究"', '"potash_alkali 本身够格研究"')
  , @('"counting（0 日）应在次日完成"', '"potash_alkali（12 日）应在此完成"')
  , @('军事树', '物理树')
  , @('科学树', '化学树')
  , @('"Research:science:writing"', '"Research:chemistry:pottery"')
)

# ⑧b 行级：把"夹具落库根"换成 app 级研究 + 推进到完成（域内直落会被读路径忽略，见 D110）
$path = Join-Path $tests 'TechTreeConcurrencyChecks.cs'
$lines = New-Object System.Collections.Generic.List[string]
$lines.AddRange([System.IO.File]::ReadAllLines($path))
$pottery = 'h.Tech.Research(MapId, h.OwnerId, "chemistry", "pottery");'
$physicsLine = 'h.Tech.Research(MapId, h.OwnerId, "physics", "simple_machine_intuition");'
$insertPottery = @(
  'h.Tech.Research(MapId, h.OwnerId, "chemistry", "taming_of_fire");',
  'h.Clock.AdvanceDays(10);   // 火的驯服 10 日 ⇒ 之后 potash_alkali / pottery 前置已满足'
)
$insertMath = @(
  'h.Tech.Research(MapId, h.OwnerId, "math", "counting");',
  'h.Clock.AdvanceDays(1);    // 计数 0 日'
)
$inserts = 0
for ($i = $lines.Count - 1; $i -ge 0; $i--) {
  $trim = $lines[$i].Trim()
  if ($trim -eq $pottery) {
    $indent = ([regex]::Match($lines[$i], '^\t*')).Value
    $lines.Insert($i, $indent + $insertPottery[0])
    $lines.Insert($i + 1, $indent + $insertPottery[1])
    $inserts++
  }
  elseif ($trim -eq $physicsLine) {
    $indent = ([regex]::Match($lines[$i], '^\t*')).Value
    $lines.Insert($i, $indent + $insertMath[0])
    $lines.Insert($i + 1, $indent + $insertMath[1])
    $inserts++
  }
  elseif ($trim -eq 'h.Clock.AdvanceDays(1);' -and $i + 1 -lt $lines.Count -and $lines[$i + 1] -match 'potash_alkali') {
    $lines[$i] = $lines[$i].Replace('AdvanceDays(1)', 'AdvanceDays(15)')
  }
}
[System.IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("  并发用例：插入 {0} 处 app 级探根指令" -f $inserts)

