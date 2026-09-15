# （v0.8.2 / WP-7.3）把用例里对"旧示例科技表"的引用迁移到设计稿真表。
# 用法：先 git checkout -- 被改坏的用例文件，再 powershell -File Tools/migrate_tech_refs.ps1
# 口径（D109 修订）：树 Id math/physics/chemistry；节点 Id 英文 slug（见 Tools/gen_tech.ps1 的 slug 表）；
# 并发类用例的样本 = chemistry 树（夹具先把根 taming_of_fire 标为已研究，pottery / potash_alkali 即"同树前置已满足"的兄弟）。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tests = Join-Path $root 'Tests\SciencePotato.HeadlessChecks'
$crlf = [char]13 + [char]10
$tab = [char]9

function Patch([string]$file, [object[][]]$pairs) {
  $path = Join-Path $tests $file
  $text = [System.IO.File]::ReadAllText($path)
  $miss = @()
  foreach ($p in $pairs) {
    if ($text.Contains($p[0])) { $text = $text.Replace($p[0], $p[1]) } else { $miss += $p[0] }
  }
  [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host ("  {0}：替换 {1} 条，未命中 {2}" -f $file, ($pairs.Count - $miss.Count), $miss.Count)
  foreach ($m in $miss) { Write-Host ("    MISS: " + $m.Substring(0, [Math]::Min(50, $m.Length))) }
}

# ① 节点/树 id 平移（这 4 个文件只用"根节点 + 本树前置"这一层语义）
$basic = @(
  ,@('"science", "mathematics"', '"math", "counting"')
  ,@('"science"', '"math"')
)
Patch 'DomainEventChecks.cs' $basic
Patch 'EventEngineChecks.cs' $basic
Patch 'SaveUnitChecks.cs' @(,@('"science"', '"math"'))

$prereq = @(
  ,@('"science"', '"math"')
  ,@('"writing"', '"counting"')      # 用例要的是"无前置的根" ⇒ 设计稿的 math 树根 = 计数
  ,@('"mathematics"', '"arithmetic"') # 用例要的是"本树前置"这一段链 ⇒ 算术 ← 计数
)
Patch 'TechPrerequisiteChecks.cs' $prereq

# ② 升级门控夹具：直接落库（bypass 前置链）——这些用例测的是"建筑侧门控"，不该被科技树深度绑架
$upgrade = @(
  ,@('Tech.Research(MapId, OwnerId, treeId, nodeId);', 'Tech.GetOrCreateTechTree(MapId, OwnerId, treeId).Research(nodeId);')
  ,@('("science", "writing")', '("math", "basic_geometry")')        # camp lv.I→II 的设计稿前置 = 基础几何
  ,@('("science", "counting")', '("math", "counting")')
  ,@('("science", "mathematics")', '("math", "pythagorean_school")')
)
Patch 'UpgradeChecks.cs' $upgrade

# ③ 并发语义：样本换到 chemistry 树（根 taming_of_fire 先由夹具落库）
$conc = @(
  ,@('new[] { "military", "science", "physics" }', 'new[] { "math", "physics", "chemistry" }')
  ,@('"science"', '"chemistry"')
  ,@('"writing"', '"pottery"')
  ,@('"counting"', '"potash_alkali"')
  ,@('"military"', '"physics"')
  ,@('"melee_weapons"', '"simple_machine_intuition"')
  ,@('h.Clock.AdvanceDays(5);', 'h.Clock.AdvanceDays(30);')          # 物理树样本 30 日
  ,@('"writing 是根节点"', '"pottery 前置（火的驯服）满足 → 可研究"')
  ,@('"counting 本身够格研究"', '"potash_alkali 本身够格研究"')
  ,@('"counting（0 日）应在次日完成"', '"potash_alkali（12 日）应在此完成"')
  ,@('"科学树', '"化学树')
  ,@('"军事树', '"物理树')
  ,@('军事树', '物理树')
  ,@('科学树', '化学树')
  ,@('// science 树的两个根节点', '// chemistry 树样本（根 taming_of_fire 由夹具落库）')
  ,@('// 15 日 → writing 完成', '// 15 日 → pottery 完成')
  ,@('"Research:science:writing"', '"Research:chemistry:pottery"')
)
Patch 'TechTreeConcurrencyChecks.cs' $conc

# ④ 并发文件的两处"行前插入"（夹具落库根节点）+ 一处天数修正：按行处理（从后往前插入，避免下标漂移）
$path = Join-Path $tests 'TechTreeConcurrencyChecks.cs'
$lines = New-Object System.Collections.Generic.List[string]
$lines.AddRange([System.IO.File]::ReadAllLines($path))
$pottery = 'h.Tech.Research(MapId, h.OwnerId, "chemistry", "pottery");'
$physicsLine = 'h.Tech.Research(MapId, h.OwnerId, "physics", "simple_machine_intuition");'
$insertPottery = 'h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "chemistry").Research("taming_of_fire");'
$insertMath = 'h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "math").Research("counting");'
$inserts = 0
for ($i = $lines.Count - 1; $i -ge 0; $i--) {
  $trim = $lines[$i].Trim()
  if ($trim -eq $pottery) {
    $indent = ([regex]::Match($lines[$i], '^\t*')).Value
    $lines.Insert($i, $indent + $insertPottery); $inserts++
  }
  elseif ($trim -eq $physicsLine) {
    $indent = ([regex]::Match($lines[$i], '^\t*')).Value
    $lines.Insert($i, $indent + $insertMath); $inserts++
  }
  elseif ($trim -eq 'h.Clock.AdvanceDays(1);' -and $i + 1 -lt $lines.Count -and $lines[$i + 1] -match 'potash_alkali') {
    $lines[$i] = $lines[$i].Replace('AdvanceDays(1)', 'AdvanceDays(15)')   # potash_alkali 12 日
  }
}
[System.IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("  TechTreeConcurrencyChecks.cs：插入 {0} 行夹具落库" -f $inserts)
