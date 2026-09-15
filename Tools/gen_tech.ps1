# （v0.8.1 / WP-7.3）科技表生成器：直接解析 design/research_tree.md 的定宽表 → Config/TechTrees.json
# 用法： powershell -ExecutionPolicy Bypass -File Tools/gen_tech.ps1
# 设计取舍（D109）：
#   · 分支 数学/物理/化学 → 树 Id math/physics/chemistry（数学/物理/化学；旧示例表的 `science`/`military` 已废弃）
#   · 节点 Id = 设计稿中文名，**唯一例外**是已被其它表/用例引用的三个遗留 Id（计数 counting、
#     简单机械直觉 simple_machine_intuition、毕达哥拉斯学派 mathematics）—— 改名成本 > 收益
#   · 效果原文原样存进 EffectText；能映射到当前机制的再生成 Modifiers，映射不了的不硬凑
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$doc = Join-Path $root 'design\research_tree.md'
$out = Join-Path $root 'Config\TechTrees.json'

# 节点 Id 口径（D109 修订）：**英文 slug**，中文名进节点的 Name 字段。
# 依据 = design/research_tree.md 的 93 行逐条转写；跨树同名（物理/化学各有一个"原子论"）树内唯一即可，
# 引用时写 `physics:atomism` / `chemistry:atomism` 无歧义。
# `UnlocksUi`（WP-4.14）：设计稿"解锁资源面板 / 开启研究功能 / 解锁资源收获面板"落在哪条节点上。
$unlocksUi = @{
  '计数' = @('resource_panel', 'research')
  '算术' = @('harvest_panel')
}
$slug = @{
  '计数' = 'counting'; '算术' = 'arithmetic'; '度量' = 'measurement'; '基础几何' = 'basic_geometry'
  '记数系统' = 'numeral_system'; '初等代数' = 'elementary_algebra'; '简单数列' = 'simple_sequences'
  '初步测量' = 'preliminary_survey'; '毕达哥拉斯学派' = 'pythagorean_school'; '几何原本' = 'elements'
  '圆锥曲线' = 'conic_sections'; '数论' = 'number_theory'; '穷竭法' = 'method_of_exhaustion'
  '球面几何' = 'spherical_geometry'; '丢番图方程' = 'diophantine_equations'; '三角学萌芽' = 'early_trigonometry'
  '机械几何' = 'mechanical_geometry'; '印度-阿拉伯数字' = 'hindu_arabic_numerals'; '代数学' = 'algebra'
  '算法' = 'algorithms'; '二次方程求根' = 'quadratic_formula'; '组合数学' = 'combinatorics'
  '球面三角学' = 'spherical_trigonometry'; '阿拉伯几何' = 'arabic_geometry'
  '代数几何萌芽' = 'early_algebraic_geometry'; '斐波那契数列' = 'fibonacci_sequence'
  '透视几何' = 'perspective_geometry'; '无限与极限' = 'infinity_and_limits'
  '概率论萌芽' = 'early_probability'; '方程与曲线' = 'equations_and_curves'
  '简单机械直觉' = 'simple_machine_intuition'; '影子观测' = 'shadow_observation'; '声学初探' = 'acoustics'
  '材料硬度' = 'material_hardness'; '浮沉观察' = 'buoyancy_observation'; '镜面反射' = 'mirror_reflection'
  '斜面原理' = 'inclined_plane'; '轮子与滚动' = 'wheel_and_rolling'; '亚里士多德运动论' = 'aristotelian_motion'
  '杠杆平衡' = 'lever_balance'; '浮力定律' = 'buoyancy_law'; '重心理论' = 'center_of_gravity'
  '简单机械组合' = 'compound_machines'; '反射定律' = 'law_of_reflection'; '折射观察' = 'refraction_observation'
  '燃烧四元素说' = 'four_elements_combustion'; '原子论' = 'atomism'; '抛体运动分析' = 'projectile_analysis'
  '滑轮组' = 'pulley_systems'; '流体静力学' = 'hydrostatics'; '冲力理论' = 'impetus_theory'
  '阿尔哈曾光学' = 'alhazen_optics'; '放大透镜' = 'magnifying_lens'; '速度与加速度' = 'speed_and_acceleration'
  '抛体运动定量化' = 'projectile_quantification'; '时间测量革新' = 'time_measurement'
  '静力学分析' = 'statics_analysis'; '彩虹成因' = 'rainbow_formation'
  '燃烧与氧化萌芽' = 'early_combustion_oxidation'; '潜热概念雏形' = 'latent_heat'
  '振动与波' = 'vibration_and_waves'; '静电观察' = 'static_electricity'; '磁石研究' = 'magnetism'
  '透视学与视觉' = 'perspective_and_vision'; '冲力与角动量' = 'impetus_and_angular_momentum'
  '火的驯服' = 'taming_of_fire'; '制陶术' = 'pottery'; '酿造发酵' = 'fermentation'; '天然染料' = 'natural_dyes'
  '冶金萌芽' = 'early_metallurgy'; '草木灰制碱' = 'potash_alkali'; '合金尝试' = 'alloying'
  '玻璃初制' = 'early_glassmaking'; '四元素说' = 'four_elements'; '金属精炼' = 'metal_refining'
  '矿物分类' = 'mineral_classification'; '酸碱概念' = 'acid_base_concepts'; '蒸馏技术' = 'distillation'
  '燃烧与空气' = 'combustion_and_air'; '金属置换' = 'metal_displacement'; '炼金术兴起' = 'alchemy_rise'
  '矿物酸制备' = 'mineral_acids'; '酒精提纯' = 'alcohol_purification'
  '制药化学' = 'pharmaceutical_chemistry'; '盐类研究' = 'salts_research'
  '硝石与火药' = 'saltpeter_and_gunpowder'; '金属冶炼革新' = 'metallurgy_innovation'
  '气体收集' = 'gas_collection'; '化合与混合' = 'compounds_and_mixtures'
  '煅烧与焙烧' = 'calcination_and_roasting'; '酸碱理论深化' = 'acid_base_theory'
  '炼金术符号体系' = 'alchemical_symbols'
}
$treeOf = @{ '数学' = 'math'; '物理' = 'physics'; '化学' = 'chemistry' }

function NodeId([string]$name) {
  if (-not $slug.ContainsKey($name)) { throw "缺少 slug 映射：$name（slug 表须覆盖 design/research_tree.md 的 93 行）" }
  return $slug[$name]
}

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

$trees = @{ math = @(); physics = @(); chemistry = @() }
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
$treeIds = @('math', 'physics', 'chemistry')
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
    $uiJson = if ($unlocksUi.ContainsKey($n.Name)) { '[ ' + (($unlocksUi[$n.Name] | ForEach-Object { '"' + $_ + '"' }) -join ', ') + ' ]' } else { '[]' }
    [void]$sb.AppendLine("        `"$($n.Id)`": { `"Id`": `"$($n.Id)`", `"Name`": `"$($n.Name)`", `"Prerequisites`": $preJson, `"Cost`": $($n.Cost), `"Duration`": $($n.Duration), `"Modifiers`": $modJson, `"UnlocksUi`": $uiJson, `"EffectText`": `"$effect`" }$tail")
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
