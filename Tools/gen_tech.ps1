# （v0.8.1 / WP-7.3）科技表生成器：直接解析 design/research_tree.md 的定宽表 → Config/TechTrees.json
# 用法： powershell -ExecutionPolicy Bypass -File Tools/gen_tech.ps1
# 设计取舍（D109）：
#   · 分支 数学/物理/化学 → 树 Id science/physics/chemistry（沿用既有树 Id，不新造）
#   · 节点 Id = 设计稿中文名，**唯一例外**是已被其它表/用例引用的三个遗留 Id（计数 counting、
#     简单机械直觉 simple_machine_intuition、毕达哥拉斯学派 mathematics）—— 改名成本 > 收益
#   · 效果原文原样存进 EffectText；能映射到当前机制的再生成 Modifiers，映射不了的不硬凑
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$doc = Join-Path $root 'design\research_tree.md'
$out = Join-Path $root 'Config\TechTrees.json'

$legacyId = @{ '计数' = 'counting'; '简单机械直觉' = 'simple_machine_intuition'; '毕达哥拉斯学派' = 'mathematics' }
$treeOf = @{ '数学' = 'science'; '物理' = 'physics'; '化学' = 'chemistry' }

function NodeId([string]$name) { if ($legacyId.ContainsKey($name)) { return $legacyId[$name] } return $name }

function GrowthTarget([string]$word) {
  if ($word -match '农田|食物|Food') { return 'FoodGrowth' }
  if ($word -match '矿物|石材|Mineral') { return 'MineralGrowth' }
  if ($word -match 'Idea|灵感') { return 'IdeaGrowth' }
  return $null
}

# 效果从句 → Modifier 列表（返回空数组表示"当前机制还没接上"，只留 EffectText）
function MapEffect([string]$clause) {
  $m = @()
  $c = $clause.Trim().TrimEnd('。')
  if ($c -match '^(.+?) 产出 \+(\d+)/年$') {
    $t = GrowthTarget $Matches[1]; if ($t) { $m += @{ Target = $t; Type = 'Absolute'; Value = [double]$Matches[2] / 12.0 } }
  }
  elseif ($c -match '^(.+?) 产出 \+(\d+)/月$') {
    $t = GrowthTarget $Matches[1]; if ($t) { $m += @{ Target = $t; Type = 'Absolute'; Value = [double]$Matches[2] } }
  }
  elseif ($c -match '^(.+?) 产出 \+(\d+)%$') {
    $word = $Matches[1]; $p = [double]$Matches[2] / 100.0
    if ($word -match '所有资源') { foreach ($t in 'FoodGrowth','MineralGrowth','IdeaGrowth') { $m += @{ Target = $t; Type = 'Percent'; Value = $p } } }
    else { $t = GrowthTarget $word; if ($t) { $m += @{ Target = $t; Type = 'Percent'; Value = $p } } }
  }
  elseif ($c -match '^基础石材存储上限 \+(\d+)$') { $m += @{ Target = 'BasicMineralsLimit'; Type = 'Absolute'; Value = [double]$Matches[1] } }
  elseif ($c -match '^食物存储上限 \+(\d+)%$') { $m += @{ Target = 'FoodLimit'; Type = 'Percent'; Value = [double]$Matches[1] / 100.0 } }
  elseif ($c -match '^资源上限 \+(\d+)%$') { $m += @{ Target = 'ResourceLimit'; Type = 'Percent'; Value = [double]$Matches[1] / 100.0 } }
  elseif ($c -match '^建筑 HP \+(\d+)%$') { $m += @{ Target = 'HP'; Type = 'Percent'; Value = [double]$Matches[1] / 100.0 } }
  elseif ($c -match '^视野半径 \+(\d+)$') { $m += @{ Target = 'VisionRadius'; Type = 'Absolute'; Value = [double]$Matches[1] } }
  elseif ($c -match '^单位移动速度 \+(\d+)%$') { $m += @{ Target = 'UnitSpeed'; Type = 'Percent'; Value = [double]$Matches[1] / 100.0 } }
  elseif ($c -match '^单位攻击力 \+(\d+)%$') { $m += @{ Target = 'UnitAttack'; Type = 'Percent'; Value = [double]$Matches[1] / 100.0 } }
  elseif ($c -match '^(?:所有)?建筑建造时间 -(\d+)日$') { $m += @{ Target = 'BuildingSpeed'; Type = 'Absolute'; Value = -[double]$Matches[1] } }
  elseif ($c -match '^(?:所有)?建筑建造时间 -(\d+)%$') { $m += @{ Target = 'BuildingSpeed'; Type = 'Percent'; Value = -[double]$Matches[1] / 100.0 } }
  elseif ($c -match '^(?:所有)?建筑升级消耗 -(\d+)%$') { $m += @{ Target = 'BuildingUpgradeCost'; Type = 'Percent'; Value = -[double]$Matches[1] / 100.0 } }
  elseif ($c -match '^食物消耗 -(\d+)%$') { $m += @{ Target = 'FoodConsumption'; Type = 'Percent'; Value = -[double]$Matches[1] / 100.0 } }
  elseif ($c -match '^人口增长速度 \+(\d+)%$') { $m += @{ Target = 'PopulationGrowth'; Type = 'Percent'; Value = [double]$Matches[1] / 100.0 } }
  return $m
}


# ────────────── 解析 + 生成 ──────────────
$rows = Get-Content $doc | Where-Object { $_ -match '^\s{2}\S' -and $_ -notmatch '^\s+-{3,}' }
$nodes = @()
foreach ($r in $rows) {
  $p = ($r -replace '\s{2,}', '|').Trim('|').Split('|')
  if ($p.Count -lt 7) { continue }
  if ($p[1] -notmatch '^(数学|物理|化学)$') { continue }
  $nodes += [pscustomobject]@{ Name = $p[0]; Branch = $p[1]; Pre = $p[2]; Effect = $p[4]; Cost = $p[5]; Dur = $p[6] }
}
if ($nodes.Count -ne 93) { throw "解析到 $($nodes.Count) 个节点（期望 93）" }

$trees = @{ science = @(); physics = @(); chemistry = @() }
foreach ($n in $nodes) {
  $pre = @()
  if ($n.Pre -ne '无') {
    foreach ($token in [regex]::Matches($n.Pre, '\[(.+?)\]')) {
      $ref = $token.Groups[1].Value
      $refBranch = ($nodes | Where-Object { $_.Name -eq $ref } | Select-Object -First 1).Branch
      $refId = NodeId $ref
      $pre += $(if ($refBranch -eq $n.Branch) { $refId } else { "$($treeOf[$refBranch]):$refId" })
    }
  }
  $cost = if ($n.Cost -match '^(\d+(\.\d+)?)\s*idea$') { [double]$Matches[1] } else { [double]($n.Cost -replace '[^\d.]', '') }
  $dur = [double]($n.Dur -replace '[^\d.]', '')
  $mods = @()
  foreach ($clause in ($n.Effect -split '；')) { if ($clause.Trim()) { $mods += MapEffect $clause } }
  $trees[$treeOf[$n.Branch]] += [pscustomobject]@{
    Id = (NodeId $n.Name); Name = $n.Name; Prerequisites = $pre
    Cost = $cost; Duration = $dur; Modifiers = $mods; EffectText = $n.Effect
  }
}

# 同一目标重复出现时保留首个（设计稿里"所有资源产出"与单资源从句可能重叠）
foreach ($tid in @('science','physics','chemistry')) {
  foreach ($node in $trees[$tid]) {
    $seen = @{}; $keep = @()
    foreach ($mod in $node.Modifiers) { $k = "$($mod.Target)|$($mod.Type)"; if (-not $seen.ContainsKey($k)) { $seen[$k] = 1; $keep += $mod } }
    $node.Modifiers = $keep
  }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('  "TechTrees": {')
$treeIds = @('science', 'physics', 'chemistry')
for ($t = 0; $t -lt $treeIds.Count; $t++) {
  $tid = $treeIds[$t]; $list = @($trees[$tid])
  [void]$sb.AppendLine("    `"$tid`": {")
  [void]$sb.AppendLine('      "Concurrency": 1,')
  [void]$sb.AppendLine('      "Techs": {')
  for ($i = 0; $i -lt $list.Count; $i++) {
    $n = $list[$i]
    $preJson = if ($n.Prerequisites.Count -eq 0) { '[]' } else { '[ ' + (($n.Prerequisites | ForEach-Object { "`"$_`"" }) -join ', ') + ' ]' }
    $modJson = if ($n.Modifiers.Count -eq 0) { '[]' } else { '[ ' + (($n.Modifiers | ForEach-Object { "{ `"Target`": `"$($_.Target)`", `"Type`": `"$($_.Type)`", `"Value`": $($_.Value) }" }) -join ', ') + ' ]' }
    $effect = ($n.EffectText -replace '\\', '') -replace '"', '\"'
    $tail = if ($i -lt $list.Count - 1) { ',' } else { '' }
    [void]$sb.AppendLine("        `"$($n.Id)`": { `"Id`": `"$($n.Id)`", `"Name`": `"$($n.Name)`", `"Prerequisites`": $preJson, `"Cost`": $($n.Cost), `"Duration`": $($n.Duration), `"Modifiers`": $modJson, `"EffectText`": `"$effect`" }$tail")
  }
  [void]$sb.AppendLine('      }')
  $treeTail = if ($t -lt $treeIds.Count - 1) { ',' } else { '' }
  [void]$sb.AppendLine("    }$treeTail")
}
[void]$sb.AppendLine('  }')
[void]$sb.AppendLine('}')
Set-Content -Path $out -Value $sb.ToString() -Encoding UTF8 -NoNewline

$parsed = Get-Content $out -Raw | ConvertFrom-Json
$total = 0; $withMods = 0
foreach ($tid in $treeIds) {
  $c = @($parsed.TechTrees.$tid.Techs.PSObject.Properties).Count
  $total += $c
  Write-Host "  $tid : $c 节点"
  foreach ($p in $parsed.TechTrees.$tid.Techs.PSObject.Properties) { if (@($p.Value.Modifiers).Count -gt 0) { $withMods++ } }
}
Write-Host "总节点=$total ；带 Modifiers=$withMods ；仅留 EffectText=$(($total - $withMods))"
