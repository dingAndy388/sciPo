# （v0.8.4 / WP-4.4）修正作用面接线：让 6 个"注册了但没人读"的目标名真正生效。
# 用法： powershell -ExecutionPolicy Bypass -File Tools/wire_modifier_consumers.ps1
# 口径（§20 D111）：耗时类 = `(base + ΣAbsolute) × (1 + ΣPercent)`（-30 日 = Absolute -30；-10% = Percent -0.1）；
# 造价/需求类 = `base × (1 + ΣPercent)`（用 base 传入，Absolute 视为平摊减免）。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$crlf = [char]13 + [char]10

function Patch([string]$rel, [object[][]]$pairs) {
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

# ── ① 建造：耗时（BuildingSpeed）+ 升级造价折扣（BuildingUpgradeCost）
$build = @(
  ,@('		public bool StartConstruction(string mapId, string buildingId, HexCubePosition position, int ownerId, BuilderBinding builder = null)',
     '		/// <summary>' + $crlf +
     '		/// （v0.8.4 / `WP-4.4`）**建造/升级耗时**：读 `BuildingSpeed`。' + $crlf +
     '		/// <para>设计稿同时用两种写法：`-30 日`（Absolute）与 `-10%`（Percent）—— 两者用同一公式结算，' + $crlf +
     '		/// 因此这里传"基础天数"作 base：`(base + ΣAbsolute) × (1 + ΣPercent)`。</para>' + $crlf +
     '		/// </summary>' + $crlf +
     '		private float ScaledBuildDays(float baseDays, string mapId, int ownerId)' + $crlf +
     '			=> _modifier == null ? baseDays : Math.Max(0.1f, _modifier.GetValue(mapId, ownerId, "BuildingSpeed", baseDays));' + $crlf + $crlf +
     '		/// <summary>（v0.8.4 / `WP-4.4`）**升级造价折扣**：读 `BuildingUpgradeCost`（`base × (1 + ΣPercent)`）。</summary>' + $crlf +
     '		private float ScaledUpgradeCost(float baseCost, string mapId, int ownerId)' + $crlf +
     '			=> _modifier == null ? baseCost : Math.Max(0f, _modifier.GetValue(mapId, ownerId, "BuildingUpgradeCost", baseCost));' + $crlf + $crlf +
     '		public bool StartConstruction(string mapId, string buildingId, HexCubePosition position, int ownerId, BuilderBinding builder = null)')
  ,@('LinearTask buildTask = new(0, config.Duration, config.BuildingId, "Construction", false, uid, mapId, ownerId);',
     'LinearTask buildTask = new(0, ScaledBuildDays(config.Duration, mapId, ownerId), config.BuildingId, "Construction", false, uid, mapId, ownerId);')
)
Patch 'Scripts\Construction\Application\ConstructionAppService.cs' $build

# 升级任务时长 + 升级消耗（用正则，容忍空白差异）
$path = Join-Path $root 'Scripts\Construction\Application\ConstructionAppService.cs'
$text = [System.IO.File]::ReadAllText($path)
$before = $text
$text = [regex]::Replace($text, 'new\(0, config\.UpgradeDuration,', 'new(0, ScaledBuildDays(config.UpgradeDuration, mapId, ownerId),')
$text = [regex]::Replace($text, '(?s)var consumptions = \(from item in \(config\.UpgradeCost.*?ToList\(\);',
  'var consumptions = (from item in (config.UpgradeCost ?? new Dictionary<string, float>())' + $crlf +
  '								select new Consumption(item.Key, ScaledUpgradeCost(item.Value, mapId, ownerId))).ToList();')
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("  ConstructionAppService.cs：正则补丁 " + $(if ($text -ne $before) { '已应用' } else { '未命中' }))

# ── ② 训练：耗时（UnitTrainingSpeed）
$train = @(
  ,@('		public bool TrainUnit(string mapId, string buildingUid, string unitId)',
     '		/// <summary>（v0.8.4 / `WP-4.4`）**训练耗时**：读 `UnitTrainingSpeed`（工坊/军营 lv.II +20%、lv.III +50%）。</summary>' + $crlf +
     '		private float ScaledTrainingDays(float baseDays, string mapId, int ownerId)' + $crlf +
     '			=> _modifier == null ? baseDays : Math.Max(0.1f, _modifier.GetValue(mapId, ownerId, "UnitTrainingSpeed", baseDays));' + $crlf + $crlf +
     '		public bool TrainUnit(string mapId, string buildingUid, string unitId)')
  ,@('LinearTask trainingTask = new(0, config.Duration, config.UnitId, "Training", false, uid, mapId, ownerId);',
     'LinearTask trainingTask = new(0, ScaledTrainingDays(config.Duration, mapId, ownerId), config.UnitId, "Training", false, uid, mapId, ownerId);')
  ,@('LinearTask trainingTask = new(0, order.Duration, unitConfig.UnitId, "Training", false, order.UId, mapId, building.GetInfo().OwnerId);',
     'LinearTask trainingTask = new(0, ScaledTrainingDays(order.Duration, mapId, building.GetInfo().OwnerId), unitConfig.UnitId, "Training", false, order.UId, mapId, building.GetInfo().OwnerId);')
)
Patch 'Scripts\Units\Application\UnitsAppService.cs' $train

# ── ③ 科研：耗时（ResearchSpeed）
$research = @(
  ,@('			float duration = tree.GetDuration(nodeId);',
     '			// （v0.8.4 / `WP-4.4`）研究耗时读 `ResearchSpeed`：设计稿"研究效率"类效果的消费点' + $crlf +
     '			float duration = tree.GetDuration(nodeId);' + $crlf +
     '			if (_modifier != null) duration = Math.Max(0.1f, _modifier.GetValue(mapId, ownerId, "ResearchSpeed", duration));')
)
Patch 'Scripts\TechTrees\Application\TechTreesAppService.cs' $research
