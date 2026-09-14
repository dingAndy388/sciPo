using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.TechTree.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core.Config
{
	/// <summary>
	/// （v0.3 / WP-1.4）配置表**内容**校验器：表非空 / Id 一致性 / 引用完整性 / 枚举与数值合法性。
	/// <para>分级原则（§13.z 风险 3：「一上来就启用引用完整性校验必然全表报错 → M0-1 ② 无法通过」）：</para>
	/// <list type="bullet">
	/// <item>**error**：确定的错误（表为空、Id 缺失或与 key 不符、重复 Id、非法 Modifier.Type、同树内前置缺失/成环、
	/// 负数消耗与上限、概率越界…）。M0-1 ② 要求真实配置 **0 error**。</item>
	/// <item>**warning**：可能是有意为之 / 受原型数据规模所限（跨表科技节点缺失、Modifier Target 名未登记、权重和不等于 1、
	/// 人口字段与 IsHousing 不一致、"瞬间完成"的 Duration=0…）。warning **不阻断启动**。</item>
	/// </list>
	/// <para>引用完整性优先保证**同表内闭合**（科技树前置），跨表引用先全部降级为 warning —— 这两条正是 §13.z 对该工作包的预设。</para>
	/// </summary>
	public static class ConfigValidator
	{
		/// <summary>
		/// 合法 <c>Modifier.Type</c> 字面量。注意 <c>ModifierAppService</c> 目前只把 "Percent" 识别为
		/// <see cref="ModifierType.Percentage"/>，**其余任何拼写都会被静默当成 Absolute**（`MOD-05`）；
		/// 因此在启动期把未知拼写判为 error，而不是等运行时算错数值。
		/// </summary>
		private static readonly HashSet<string> ModifierTypeLiterals = new(StringComparer.Ordinal) { "Percent", "Absolute" };

		public static void Validate(ConfigTables tables, ConfigReport report)
		{
			if (tables == null) throw new ArgumentNullException(nameof(tables));
			if (report == null) throw new ArgumentNullException(nameof(report));

			// 跨表引用所需的索引（表缺失时为空集合，不再重复报错 —— 装载段已记 error）
			var terrainIds = new HashSet<string>(tables.AllTerrains().Select(t => t?.Id).Where(NotEmpty), StringComparer.Ordinal);
			var resourceIds = new HashSet<string>(tables.AllResources().Select(r => r?.Name).Where(NotEmpty), StringComparer.Ordinal);
			var unitIds = new HashSet<string>(tables.AllUnits().Select(u => u?.UnitId).Where(NotEmpty), StringComparer.Ordinal);
			Dictionary<string, HashSet<string>> techNodes = BuildTechNodeIndex(tables);
			ModifierTargetRegistry registry = ModifierTargetRegistry.From(tables);

			ValidateTerrains(tables, report);
			ValidateResources(tables, report);
			ValidateBuildings(tables, report, terrainIds, resourceIds, techNodes, registry, unitIds);
			ValidateUnits(tables, report, terrainIds, resourceIds, techNodes, registry);
			ValidateTechTrees(tables, report, techNodes, registry);
			ValidateEvents(tables, report, resourceIds, techNodes, registry);
			ValidateGenerator(tables, report);
		}

		// ────────────────────────── Terrains ──────────────────────────

		private static void ValidateTerrains(ConfigTables tables, ConfigReport report)
		{
			if (tables.Terrains == null) return; // 装载失败已记 error

			List<ITerrainData> terrains = tables.AllTerrains().ToList();
			if (terrains.Count == 0)
			{
				report.Error("Terrains", null, "表为空：至少需要 1 条地形");
				return;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);
			float weightSum = 0f;
			foreach (ITerrainData terrain in terrains)
			{
				string id = terrain?.Id;
				if (!NotEmpty(id))
				{
					report.Error("Terrains", null, "存在 Id 为空的地形条目");
					continue;
				}
				if (!seen.Add(id)) report.Error("Terrains", id, "Id 重复");
				if (!NotEmpty(terrain.Name)) report.Warn("Terrains", id, "Name 为空（UI 将显示空名）");
				if (terrain.Weight <= 0f) report.Error("Terrains", id, $"Weight={terrain.Weight} 必须大于 0（否则永远不会被生成）");
				if (terrain.MoveCost < 0f) report.Error("Terrains", id, $"MoveCost={terrain.MoveCost} 不能为负");
				if (!terrain.Passable && !NotEmpty(terrain.UnlockTech))
					report.Warn("Terrains", id, "Passable=false 且未填 UnlockTech：该地形将永远无法进入（若非本意请补解锁科技）");
				if (terrain.Passable && terrain.MoveCost <= 0f)
					report.Warn("Terrains", id, "Passable=true 但 MoveCost<=0：移动消耗为 0，建议显式给正数");
				weightSum += terrain.Weight;
			}

			// 权重是相对值，但 5 类地形按指南给出的就是"占比"，和为 1 才符合预期分布
			if (Math.Abs(weightSum - 1f) > 0.01f)
				report.Warn("Terrains", null, $"Weight 总和={weightSum:0.###}，不等于 1（Voronoi 生成器按权重比例分配，非 1 只影响可读性）");
		}

		// ────────────────────────── Resources ──────────────────────────

		private static void ValidateResources(ConfigTables tables, ConfigReport report)
		{
			if (tables.Resources == null) return;

			List<IResourceConfig> resources = tables.AllResources().ToList();
			if (resources.Count == 0)
			{
				report.Error("Resources", null, "表为空：至少需要 1 条资源（建造/研发的消耗都以资源名为 key）");
				return;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (IResourceConfig resource in resources)
			{
				string id = resource?.Name;
				if (!NotEmpty(id))
				{
					report.Error("Resources", null, "存在 Name 为空的资源条目");
					continue;
				}
				if (!seen.Add(id)) report.Error("Resources", id, "资源名重复");
				if (!NotEmpty(resource.Description)) report.Warn("Resources", id, "Description 为空");
				if (resource.GrowInterval < 0) report.Error("Resources", id, $"GrowInterval={resource.GrowInterval} 不能为负（0=不自动增长）");
				else ValidateDayUnit(report, "Resources", id, "GrowInterval", resource.GrowInterval);
				if (resource.GrowInterval == 0 && resource.BaseGrowth > 0f)
					report.Warn("Resources", id, "GrowInterval=0（不自动增长）但 BaseGrowth>0：增长率不会被使用");
				if (resource.BaseGrowth < 0f) report.Error("Resources", id, $"BaseGrowth={resource.BaseGrowth} 不能为负");
				if (resource.BaseLimit <= 0f) report.Error("Resources", id, $"BaseLimit={resource.BaseLimit} 必须大于 0");
				if (resource.BaseValue < 0f) report.Error("Resources", id, $"BaseValue={resource.BaseValue} 不能为负");
				if (resource.BaseValue > resource.BaseLimit)
					report.Error("Resources", id, $"BaseValue={resource.BaseValue} 超过 BaseLimit={resource.BaseLimit}");
				if (resource.DependentModifiers == null || resource.DependentModifiers.Count == 0)
					report.Warn("Resources", id, "DependentModifiers 为空：该资源不响应任何 modifier（指南要求留空也要写 []）");
			}
		}

		// ────────────────────────── Buildings ──────────────────────────

		private static void ValidateBuildings(ConfigTables tables, ConfigReport report,
			HashSet<string> terrainIds, HashSet<string> resourceIds,
			Dictionary<string, HashSet<string>> techNodes, ModifierTargetRegistry registry,
			HashSet<string> unitIds)
		{
			if (tables.Buildings == null) return;

			List<IBuildingConfig> buildings = tables.AllBuildings().ToList();
			if (buildings.Count == 0)
			{
				report.Error("Buildings", null, "表为空：至少需要 1 条建筑");
				return;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (IBuildingConfig building in buildings)
			{
				string key = building?.BuildingId;
				if (!NotEmpty(key))
				{
					report.Error("Buildings", null, "存在 BuildingId 为空的建筑条目");
					continue;
				}
				if (!seen.Add(key)) report.Error("Buildings", key, "BuildingId 重复");
				// key 是字典检索入口，BuildingId 是运行时引用入口：两者不一致会导致「按 Id 查不到」的隐性失效
				if (tables.Buildings.GetBuildingConfig(key)?.BuildingId != key)
					report.Error("Buildings", key, "字典 key 与条目 BuildingId 不一致（按 Id 检索会失败）");

				if (!NotEmpty(building.Name)) report.Warn("Buildings", key, "Name 为空（UI 将显示空名）");
				ValidateCosts(report, "Buildings", key, building.ResourceCost, resourceIds, "ResourceCost");
				ValidateTerrainRequirements(report, "Buildings", key, building.TerrainRequirements, terrainIds);
				ValidateTechRequirements(report, "Buildings", key, building.TechRequirements, techNodes, "TechRequirements");
				ValidateModifiers(report, "Buildings", key, building.Modifiers, registry);

				if (building.Duration < 0f) report.Error("Buildings", key, $"Duration={building.Duration} 不能为负");
				else if (building.Duration == 0f) report.Warn("Buildings", key, "Duration=0：瞬间建成");
				else ValidateDayUnit(report, "Buildings", key, "Duration", building.Duration);
				ValidateDayUnit(report, "Buildings", key, "PopulationGrowthInterval", building.PopulationGrowthInterval);
				if (building.VisionRadius < 0) report.Error("Buildings", key, $"VisionRadius={building.VisionRadius} 不能为负");

				if (building.IsHousing)
				{
					if (building.PopulationCap <= 0) report.Warn("Buildings", key, "IsHousing=true 但 PopulationCap<=0：住房不提供人口上限");
					if (building.PopulationRadius <= 0) report.Warn("Buildings", key, "IsHousing=true 但 PopulationRadius<=0：人口辐射半径为 0");
				}
				else if (building.PopulationCap > 0 || building.PopulationRadius > 0 || building.PopulationGrowthInterval > 0)
					report.Warn("Buildings", key, "非住房建筑却填了人口字段（指南要求 IsHousing=false 时三项均为 0）");

				ValidateActionList(report, "Buildings", key, building.Actions);
				ValidateTrainableUnits(report, key, building, unitIds);
				ValidateUpgrade(report, tables, key, building, resourceIds, techNodes);
			}
		}

		/// <summary>
		/// （v0.3 / WP-2.5）**可训练单位名单**的校验（`UNIT-11`）：
		/// <list type="bullet">
		/// <item><c>TrainableUnits</c> 里的 Id 必须存在于单位表 → 否则 **error**（否则该建筑永远训练不出任何东西，
		/// 且是"填错一个字母就静默失效"的典型 —— 与 `MOD-05` 同类教训）；</item>
		/// <item><c>Actions</c> 声明了 <c>CanTrain</c> 但名单为空 → **warning**（UI 会出现按钮却永远失败）；</item>
		/// <item>名单非空但建筑没有 <c>CanTrain</c> 动作 → **warning**（服务端可训练、UI 却不给入口）；</item>
		/// <item><c>TrainingQueueLimit &lt; 0</c> → error；<c>== 0</c> → warning（无法排队）。</item>
		/// </list>
		/// <para>反向检查（"某个单位没有任何建筑能训练它"）放在 <c>ValidateUnits</c> 里做，那里有全表视野。</para>
		/// </summary>
		private static void ValidateTrainableUnits(ConfigReport report, string key, IBuildingConfig building, HashSet<string> unitIds)
		{
			List<string> trainable = building.TrainableUnits ?? new List<string>();
			bool declareCanTrain = (building.Actions ?? new List<string>()).Contains("CanTrain");

			if (declareCanTrain && trainable.Count == 0)
				report.Warn("Buildings", key, "Actions 含 CanTrain 但 TrainableUnits 为空：UI 有入口却永远训练失败");
			if (!declareCanTrain && trainable.Count > 0)
				report.Warn("Buildings", key, "填了 TrainableUnits 但 Actions 缺 CanTrain：服务端可训练、UI 没有入口（`WP-4.14` 门控）");

			foreach (string unitId in trainable)
			{
				if (!NotEmpty(unitId)) report.Error("Buildings", key, "TrainableUnits 含空 Id");
				else if (!unitIds.Contains(unitId)) report.Error("Buildings", key, $"TrainableUnits 引用了不存在的单位 Id「{unitId}」");
			}

			if (building.TrainingQueueLimit < 0) report.Error("Buildings", key, $"TrainingQueueLimit={building.TrainingQueueLimit} 不能为负");
			else if (building.TrainingQueueLimit == 0 && trainable.Count > 0)
				report.Warn("Buildings", key, "TrainableUnits 非空但 TrainingQueueLimit=0：该建筑无法排队训练");
		}

		/// <summary>
		/// （v0.3 / WP-2.6）**升级链**校验（`CON-09` / `D4`）：
		/// <list type="bullet">
		/// <item><c>UpgradeTo</c> 指向不存在的建筑 → **error**（悬空的晋级指针 = 该等级永远升不上去，且只会在运行时静默失败）；</item>
		/// <item><c>UpgradeTo == 自己</c> → **error**（自环）；</item>
		/// <item>升级消耗的资源 Id / 科技前置的树与节点 → 与建造口径同级（资源错 = error，科技错 = warning，见 `WP-2.1` 的分级理由）；</item>
		/// <item>有 <c>UpgradeTo</c> 却缺 <c>UpgradeDuration</c>（0 日）或反之 → **warning**（升级要么瞬间完成、要么是死配置）。</item>
		/// </list>
		/// </summary>
		private static void ValidateUpgrade(ConfigReport report, ConfigTables tables, string key, IBuildingConfig building,
			HashSet<string> resourceIds, Dictionary<string, HashSet<string>> techNodes)
		{
			string upgradeTo = building.UpgradeTo;
			bool hasTarget = !string.IsNullOrWhiteSpace(upgradeTo);
			if (!hasTarget) return;

			if (upgradeTo == key)
				report.Error("Buildings", key, "UpgradeTo 指向自己（升级自环）");
			else if (tables.Buildings.GetBuildingConfig(upgradeTo) == null)
				report.Error("Buildings", key, $"UpgradeTo 引用了不存在的建筑 Id「{upgradeTo}」");

			if (building.UpgradeDuration <= 0f)
				report.Warn("Buildings", key, "填了 UpgradeTo 但 UpgradeDuration=0：升级瞬间完成（或忘填）");
			else ValidateDayUnit(report, "Buildings", key, "UpgradeDuration", building.UpgradeDuration);

			ValidateCosts(report, "Buildings", key, building.UpgradeCost, resourceIds, "UpgradeCost");
			ValidateTechRequirements(report, "Buildings", key, building.UpgradeTechRequirements, techNodes, "UpgradeTechRequirements");
		}

		// ────────────────────────── Units ──────────────────────────

		private static void ValidateUnits(ConfigTables tables, ConfigReport report,
			HashSet<string> terrainIds, HashSet<string> resourceIds,
			Dictionary<string, HashSet<string>> techNodes, ModifierTargetRegistry registry)
		{
			if (tables.Units == null) return;

			List<IUnitConfig> units = tables.AllUnits().ToList();
			if (units.Count == 0)
			{
				report.Error("Units", null, "表为空：至少需要 1 条单位");
				return;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (IUnitConfig unit in units)
			{
				string key = unit?.UnitId;
				if (!NotEmpty(key))
				{
					report.Error("Units", null, "存在 UnitId 为空的单位条目");
					continue;
				}
				if (!seen.Add(key)) report.Error("Units", key, "UnitId 重复");
				if (tables.Units.GetUnitConfig(key)?.UnitId != key)
					report.Error("Units", key, "字典 key 与条目 UnitId 不一致（按 Id 检索会失败）");

				ValidateCosts(report, "Units", key, unit.ResourceCost, resourceIds, "ResourceCost");
				ValidateTerrainRequirements(report, "Units", key, unit.TerrainRequirements, terrainIds);
				ValidateTechRequirements(report, "Units", key, unit.TechRequirements, techNodes, "TechRequirements");
				ValidateActionList(report, "Units", key, unit.Actions);

				if (unit.HP <= 0f) report.Error("Units", key, $"HP={unit.HP} 必须大于 0");
				if (unit.Attack < 0) report.Error("Units", key, $"Attack={unit.Attack} 不能为负");
				if (unit.AttackDamage < 0f) report.Error("Units", key, $"AttackDamage={unit.AttackDamage} 不能为负");
				if (unit.AttackRadius < 0) report.Error("Units", key, $"AttackRadius={unit.AttackRadius} 不能为负");
				if (unit.Movement < 0) report.Error("Units", key, $"Movement={unit.Movement} 不能为负");
				else if (unit.Movement == 0) report.Warn("Units", key, "Movement=0：单位无法移动");
				if (unit.VisionRadius < 0) report.Error("Units", key, $"VisionRadius={unit.VisionRadius} 不能为负");
				if (unit.AttackDamage > 0f && unit.Attack <= 0)
					report.Warn("Units", key, "AttackDamage>0 但 Attack<=0：两套攻击口径混用，请确认哪个被使用");
				if (unit.PopulationCost < 0) report.Error("Units", key, $"PopulationCost={unit.PopulationCost} 不能为负");
				else if (unit.PopulationCost == 0) report.Warn("Units", key, "PopulationCost=0：该单位不占人口");

				if (unit.Duration < 0f) report.Error("Units", key, $"Duration={unit.Duration} 不能为负");
				else if (unit.Duration == 0f) report.Warn("Units", key, "Duration=0：瞬间训练完成");
				else ValidateDayUnit(report, "Units", key, "Duration", unit.Duration);
			}

			// 反向完整性（v0.3 / WP-2.5）：设计稿规定"所有单位由建筑产出"，因此每个单位至少要被
			// 某个建筑列入 TrainableUnits，否则玩家永远造不出它（填表规模化后极易出现，例如新增单位忘了挂建筑）。
			var trainableAnywhere = new HashSet<string>(
				tables.AllBuildings().SelectMany(building => building?.TrainableUnits ?? new List<string>()).Where(NotEmpty),
				StringComparer.Ordinal);
			foreach (string key in seen)
				if (!trainableAnywhere.Contains(key))
					report.Warn("Units", key, "没有任何建筑把它列入 TrainableUnits：玩家无法训练该单位（敌方刷新不受影响）");
		}

		// ────────────────────────── TechTrees ──────────────────────────

		/// <summary>
		/// 科技树校验（`WP-1.4` 初版 + `WP-2.1` 跨树前置升级）。
		/// <para>前置的**分级口径**（`WP-2.1` 起）：</para>
		/// <list type="bullet">
		/// <item>本树前置（`TreeId` 为空或等于本树）缺节点 / 自环 → **error**（原口径不变）。</item>
		/// <item>跨树前置（`TreeId` 非空）的**树 Id 不存在**或**节点 Id 在目标树中不存在** → **error**：
		/// 这两种填法都等于"该科技永久不可解锁"，正是 `TECH-07` 的成因，不能再降级放行（`D13` 的升级点）。</item>
		/// <item>建筑 / 单位 / 事件表的 `TechRequirements`（另一张表对科技树的引用）仍按 warning，见
		/// <see cref="ValidateTechRequirements"/> —— 那属于"表间引用先降级"的既有口径。</item>
		/// </list>
		/// </summary>
		private static void ValidateTechTrees(ConfigTables tables, ConfigReport report,
			Dictionary<string, HashSet<string>> techNodeIndex, ModifierTargetRegistry registry)
		{
			if (tables.TechTrees == null) return;

			List<string> treeIds = tables.TreeIds().ToList();
			if (treeIds.Count == 0)
			{
				report.Error("TechTrees", null, "表为空：至少需要 1 棵科技树");
				return;
			}

			foreach (string treeId in treeIds)
			{
				if (!NotEmpty(treeId))
				{
					report.Error("TechTrees", null, "存在 Id 为空的科技树");
					continue;
				}

				ITechTreeConfig tree = tables.TechTrees.GetTechTreeConfig(treeId);
				Dictionary<string, ITechNodeConfig> nodes = tree?.Techs;

				// （v0.3 / WP-2.9）研究并发上限：< 1 = 该树永远无法开工研究（静默死锁，必须 error）；
				// > 1 是"一树多研发"的预留能力，合法但提示（设计稿当前口径是 1）。
				int concurrency = tree?.Concurrency ?? 0;
				if (concurrency < 1)
					report.Error("TechTrees", treeId, $"Concurrency={concurrency} 必须 ≥ 1（否则该树永远无法开工研究）");
				else if (concurrency > 1)
					report.Warn("TechTrees", treeId, $"Concurrency={concurrency}：该树可同时进行多项研发（设计稿当前口径为 1）");

				if (nodes == null || nodes.Count == 0)
				{
					report.Warn("TechTrees", treeId, "该科技树没有任何节点");
					continue;
				}

				foreach (KeyValuePair<string, ITechNodeConfig> pair in nodes)
				{
					string key = pair.Key;
					ITechNodeConfig node = pair.Value;
					if (!NotEmpty(key))
					{
						report.Error("TechTrees", treeId, "存在 key 为空的科技节点");
						continue;
					}
					if (node?.Id != key) report.Error("TechTrees", $"{treeId}/{key}", "节点 key 与 Id 不一致（按 Id 检索会失败）");
					if (node.Cost < 0f) report.Error("TechTrees", $"{treeId}/{key}", $"Cost={node.Cost} 不能为负");
					if (node.Duration < 0f) report.Error("TechTrees", $"{treeId}/{key}", $"Duration={node.Duration} 不能为负");
					else if (node.Duration == 0f) report.Warn("TechTrees", $"{treeId}/{key}", "Duration=0：瞬间研发完成");
					else ValidateDayUnit(report, "TechTrees", $"{treeId}/{key}", "Duration", node.Duration);
					ValidateModifiers(report, "TechTrees", $"{treeId}/{key}", node.Modifiers, registry);

					// 前置校验（`WP-2.1`）：本树闭合 → error；跨树 → 树与节点都必须真实存在（否则永久不可解锁）
					foreach (TechPrerequisite prerequisite in node.Prerequisites ?? new List<TechPrerequisite>())
					{
						if (prerequisite == null)
						{
							report.Error("TechTrees", $"{treeId}/{key}", "前置列表中存在 null 条目");
							continue;
						}
						if (!NotEmpty(prerequisite.NodeId))
						{
							report.Error("TechTrees", $"{treeId}/{key}", "前置列表中存在空节点 Id");
							continue;
						}

						if (!prerequisite.IsCrossTree || prerequisite.TreeId == treeId)
						{
							if (prerequisite.NodeId == key)
								report.Error("TechTrees", $"{treeId}/{key}", "前置指向自己（自环）");
							else if (!nodes.ContainsKey(prerequisite.NodeId))
								report.Error("TechTrees", $"{treeId}/{key}",
									$"前置 \"{prerequisite.NodeId}\" 不在同一棵树内（{treeId} 现有节点：{string.Join(", ", nodes.Keys)}）");
							continue;
						}

						if (!techNodeIndex.TryGetValue(prerequisite.TreeId, out HashSet<string> otherNodes))
						{
							report.Error("TechTrees", $"{treeId}/{key}",
								$"跨树前置引用了不存在的科技树 \"{prerequisite.TreeId}\"（TechTrees 表：{string.Join(", ", techNodeIndex.Keys)}）");
							continue;
						}
						if (!otherNodes.Contains(prerequisite.NodeId))
							report.Error("TechTrees", $"{treeId}/{key}",
								$"跨树前置 \"{prerequisite.TreeId}:{prerequisite.NodeId}\" 在该树中不存在 → 此科技将永久不可解锁（TECH-07）");
					}
				}
			}

			// 成环检测放在**树循环之外**：边表跨树统一建好后做一次全局 DFS（`WP-2.1`）
			ValidatePrerequisiteCycles(tables, report);
		}

		/// <summary>
		/// 前置成环检测（迭代式 DFS 三色）：成环的科技永远无法研发。
		/// <para>（v0.3 / WP-2.1）图**跨树统一建边**：跨树前置让环可以跨树出现（例：物理 A ← 数学 B、数学 B ← 物理 A），
		/// 逐树各自 DFS 是检测不到的。</para>
		/// </summary>
		private static void ValidatePrerequisiteCycles(ConfigTables tables, ConfigReport report)
		{
			const int InProgress = 1;
			const int Done = 2;
			Dictionary<string, List<string>> edges = BuildPrerequisiteEdges(tables);
			var state = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (string start in edges.Keys)
			{
				if (state.ContainsKey(start)) continue;
				state[start] = InProgress;
				var stack = new Stack<(string Node, int Next)>();
				stack.Push((start, 0));

				while (stack.Count > 0)
				{
					(string node, int next) = stack.Pop();
					List<string> prerequisites = edges.TryGetValue(node, out List<string> list) ? list : null;
					if (prerequisites == null || next >= prerequisites.Count)
					{
						state[node] = Done;
						continue;
					}

					stack.Push((node, next + 1));
					string prerequisite = prerequisites[next];
					// 空 Id / 自环 / 不存在的节点都由前置校验单独报错，这里只负责"环"
					if (!edges.ContainsKey(prerequisite)) continue;

					if (state.TryGetValue(prerequisite, out int prerequisiteState) && prerequisiteState == InProgress)
					{
						report.Error("TechTrees", prerequisite, "前置关系成环：该科技将永远无法研发");
						continue;
					}
					if (!state.ContainsKey(prerequisite))
					{
						state[prerequisite] = InProgress;
						stack.Push((prerequisite, 0));
					}
				}
			}
		}

		/// <summary>
		/// （v0.3 / WP-2.1）跨树统一的前置边表：<c>"treeId/nodeId" → 前置键列表</c>。
		/// <para>自环、空 Id、指向不存在节点的边在入表时丢弃（各自有专门的报错项），保证 DFS 只见合法边。</para>
		/// </summary>
		private static Dictionary<string, List<string>> BuildPrerequisiteEdges(ConfigTables tables)
		{
			var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);

			foreach (string treeId in tables.TreeIds())
			{
				if (!NotEmpty(treeId)) continue;

				ITechTreeConfig tree = tables.TechTrees.GetTechTreeConfig(treeId);
				if (tree?.Techs == null) continue;

				foreach (KeyValuePair<string, ITechNodeConfig> pair in tree.Techs)
				{
					if (!NotEmpty(pair.Key) || pair.Value?.Prerequisites == null) continue;

					string self = $"{treeId}/{pair.Key}";
					var targets = new List<string>();
					foreach (TechPrerequisite prerequisite in pair.Value.Prerequisites)
					{
						if (prerequisite == null || !NotEmpty(prerequisite.NodeId)) continue;

						string target = prerequisite.IsCrossTree
							? $"{prerequisite.TreeId}/{prerequisite.NodeId}"
							: $"{treeId}/{prerequisite.NodeId}";

						if (target != self) targets.Add(target);
					}
					edges[self] = targets;
				}
			}

			return edges;
		}

		// ────────────────────────── Events ──────────────────────────

		private static void ValidateEvents(ConfigTables tables, ConfigReport report,
			HashSet<string> resourceIds, Dictionary<string, HashSet<string>> techNodes, ModifierTargetRegistry registry)
		{
			if (tables.Events == null) return;

			List<IEventConfig> events = tables.AllEvents().ToList();
			if (events.Count == 0)
			{
				report.Error("Events", null, "表为空：至少需要 1 条事件（每日判定是事件玩法的入口）");
				return;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (IEventConfig gameEvent in events)
			{
				string key = gameEvent?.EventId;
				if (!NotEmpty(key))
				{
					report.Error("Events", null, "存在 EventId 为空的事件条目");
					continue;
				}
				if (!seen.Add(key)) report.Error("Events", key, "EventId 重复");
				if (!NotEmpty(gameEvent.Name)) report.Warn("Events", key, "Name 为空（UI 将显示空名）");

				// （v0.3 / WP-2.8）`TriggerChance` → `TriggerChancePerDay`（口径 = %/日）。
				// 改名后旧表会静默落到默认值 0（事件永不触发），因此 0 的警告要直接点出旧字段名。
				if (gameEvent.TriggerChancePerDay < 0f || gameEvent.TriggerChancePerDay > 1f)
					report.Error("Events", key, $"TriggerChancePerDay={gameEvent.TriggerChancePerDay} 必须落在 [0,1]（口径 = %/日，0.2%/日 填 0.002）");
				else if (gameEvent.TriggerChancePerDay == 0f)
					report.Warn("Events", key, "TriggerChancePerDay=0：该事件永远不会触发（若旧表仍写 TriggerChance，请改名为 TriggerChancePerDay）");
				if (gameEvent.Duration < 0) report.Error("Events", key, $"Duration={gameEvent.Duration} 不能为负（0=永久）");
				else ValidateDayUnit(report, "Events", key, "Duration", gameEvent.Duration);

				ValidateModifiers(report, "Events", key, gameEvent.Modifiers, registry);
				ValidateResourceCosts(report, "Events", key, gameEvent.ResourcePrerequisites, resourceIds, "ResourcePrerequisites", warnWhenEmpty: false);
				ValidateTechRequirements(report, "Events", key, gameEvent.TechPrerequisites, techNodes, "TechPrerequisites");
			}
		}

		// ────────────────────────── Generator ──────────────────────────

		private static void ValidateGenerator(ConfigTables tables, ConfigReport report)
		{
			if (tables.Generator == null) return;

			float density = tables.Generator.Density;
			if (density <= 0f) report.Error("Generator", null, $"Density={density} 必须大于 0（按地图面积百分比放置锚点）");
			else if (density > 50f) report.Warn("Generator", null, $"Density={density} 过大：锚点过多会让 Voronoi 分布退化成噪声");
		}

		// ────────────────────────── 共享规则 ──────────────────────────

		/// <summary>
		/// （v0.3 / WP-1.5）**配置单位自检**：所有时长字段的单位都是"游戏日"。
		/// <para>负数与"0 的特殊含义"由各表自身规则负责；这里只抓一件最容易静默出错的事 ——
		/// **疑似仍是秒值**：超过 <see cref="TimeConstants.MaxPlausibleDays"/>（1 年）即记 warning。
		/// 设计上的时长上限都在 1 年以内（`A4`：建造 30~300、研究 0~180、事件 5~30、人口 180~300、经济 30），
		/// 所以 >360 的值基本只可能是漏改的秒口径（如旧的 `GrowInterval=300`/建造 `Duration=600`）。</para>
		/// <para>为什么是 warning 而不是 error：口径错误不会让解析失败，但会让节奏慢 N 倍且"不易察觉"，
		/// 需要提示而无需阻断原型启动（分级原则同 `D12`）。</para>
		/// </summary>
		private static void ValidateDayUnit(ConfigReport report, string table, string ownerId, string field, float days)
		{
			if (days <= TimeConstants.MaxPlausibleDays) return;

			report.Warn(table, ownerId,
				$"{field}={days} 超过 1 年（{TimeConstants.MaxPlausibleDays:0} 日）：时长字段的单位应为**游戏日**，" +
				"该值疑似仍是秒口径，请重标定（v0.3 / WP-1.5，`TIME-13`）");
		}

		/// <summary>全树节点索引：treeId → 节点 Id 集合（供建筑/单位/事件的跨表引用校验与跨树前置校验）。</summary>
		private static Dictionary<string, HashSet<string>> BuildTechNodeIndex(ConfigTables tables)
		{
			var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
			foreach (string treeId in tables.TreeIds())
			{
				if (!NotEmpty(treeId)) continue;
				ITechTreeConfig tree = tables.TechTrees.GetTechTreeConfig(treeId);
				index[treeId] = new HashSet<string>(tree?.Techs?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
			}
			return index;
		}

		private static void ValidateCosts(ConfigReport report, string table, string ownerId,
			Dictionary<string, float> costs, HashSet<string> resourceIds, string field)
			=> ValidateResourceCosts(report, table, ownerId, costs, resourceIds, field, warnWhenEmpty: true);

		/// <summary>
		/// 资源相关字段校验（ResourceCost / ResourcePrerequisites 同构）：
		/// 未知资源名先记 warning（原型表可能引用尚未填写的资源，不阻断启动），负值一律 error。
		/// </summary>
		private static void ValidateResourceCosts(ConfigReport report, string table, string ownerId,
			Dictionary<string, float> costs, HashSet<string> resourceIds, string field, bool warnWhenEmpty)
		{
			if (costs == null || costs.Count == 0)
			{
				if (warnWhenEmpty) report.Warn(table, ownerId, $"{field} 为空：该项免费");
				return;
			}

			foreach (KeyValuePair<string, float> cost in costs)
			{
				if (!NotEmpty(cost.Key))
				{
					report.Error(table, ownerId, $"{field} 中存在空资源名");
					continue;
				}
				if (cost.Value < 0f) report.Error(table, ownerId, $"{field}[{cost.Key}]={cost.Value} 不能为负");
				if (resourceIds.Count > 0 && !resourceIds.Contains(cost.Key))
					report.Warn(table, ownerId, $"{field} 引用了未定义的资源 \"{cost.Key}\"（Resources 表：{string.Join(", ", resourceIds)}）");
			}
		}

		// ── 剩余共享规则 ──

		private static void ValidateTerrainRequirements(ConfigReport report, string table, string ownerId,
			List<string> requiredTerrains, HashSet<string> terrainIds)
		{
			if (requiredTerrains == null || requiredTerrains.Count == 0)
			{
				report.Warn(table, ownerId, "TerrainRequirements 为空：该条目可在任意地形放置");
				return;
			}

			foreach (string terrainId in requiredTerrains)
			{
				if (!NotEmpty(terrainId))
				{
					report.Error(table, ownerId, "TerrainRequirements 中存在空地形 Id");
					continue;
				}
				if (terrainIds.Count > 0 && !terrainIds.Contains(terrainId))
					report.Warn(table, ownerId, $"TerrainRequirements 引用了未定义的地形 \"{terrainId}\"（Terrains 表：{string.Join(", ", terrainIds)}）");
			}
		}

		/// <summary>
		/// 科技前提校验：树 Id 必须存在（error，按树查表会直接失败）；
		/// 节点 Id 缺失先记 warning —— 跨树前置的关系调整是 WP-2.1 的工作，此处不阻断（§13.z）。
		/// </summary>
		private static void ValidateTechRequirements(ConfigReport report, string table, string ownerId,
			Dictionary<string, List<string>> techRequirements, Dictionary<string, HashSet<string>> techNodes, string field)
		{
			if (techRequirements == null || techRequirements.Count == 0) return;

			foreach (KeyValuePair<string, List<string>> requirement in techRequirements)
			{
				if (!NotEmpty(requirement.Key))
				{
					report.Error(table, ownerId, $"{field} 中存在空科技树 Id");
					continue;
				}
				if (!techNodes.TryGetValue(requirement.Key, out HashSet<string> nodes))
				{
					report.Error(table, ownerId, $"{field} 引用了不存在的科技树 \"{requirement.Key}\"（TechTrees 表：{string.Join(", ", techNodes.Keys)}）");
					continue;
				}
				foreach (string nodeId in requirement.Value ?? new List<string>())
				{
					if (!NotEmpty(nodeId))
					{
						report.Error(table, ownerId, $"{field}[{requirement.Key}] 中存在空节点 Id");
						continue;
					}
					if (!nodes.Contains(nodeId))
						report.Warn(table, ownerId, $"{field} 引用了 {requirement.Key} 树中不存在的节点 \"{nodeId}\"（跨表引用先记 warning，见 WP-2.1）");
				}
			}
		}

		/// <summary>
		/// Modifier 校验。<c>Type</c> 拼写错误判 error：<c>ModifierAppService</c> 只认 "Percent"，其余一律静默按 Absolute 处理
		/// （`MOD-05`），等于游戏内数值悄悄错掉。Target 未登记先记 warning，等 WP-2.7 收敛目标名后再升级为 error。
		/// </summary>
		private static void ValidateModifiers(ConfigReport report, string table, string ownerId,
			List<Modifier> modifiers, ModifierTargetRegistry registry)
		{
			if (modifiers == null) return;

			for (int i = 0; i < modifiers.Count; i++)
			{
				Modifier modifier = modifiers[i];
				string where = $"Modifiers[{i}]";

				if (!NotEmpty(modifier.Target)) report.Error(table, ownerId, $"{where} 未填 Target");
				else if (!registry.Contains(modifier.Target))
					report.Warn(table, ownerId, $"{where} 的 Target \"{modifier.Target}\" 不在 Target 登记表中（拼错的 modifier 会被静默忽略，见 MOD-05）");

				if (!NotEmpty(modifier.Type)) report.Error(table, ownerId, $"{where} 未填 Type（合法值：Percent / Absolute）");
				else if (!ModifierTypeLiterals.Contains(modifier.Type))
					report.Error(table, ownerId, $"{where} 的 Type=\"{modifier.Type}\" 非法：仅 Percent / Absolute，其余拼写会被静默当成 Absolute（MOD-05）");

				if (modifier.Value == 0f) report.Warn(table, ownerId, $"{where} 的 Value=0：该 modifier 没有效果");
			}
		}

		/// <summary>动作列表：只校验「不出现空动作名」。动作白名单尚未定稿（指南仅列 CanResearch），此处不做白名单以免假警报。</summary>
		private static void ValidateActionList(ConfigReport report, string table, string ownerId, List<string> actions)
		{
			if (actions == null) return;
			foreach (string action in actions)
				if (!NotEmpty(action)) report.Error(table, ownerId, "Actions 中存在空动作名");
		}

		private static bool NotEmpty(string value) => !string.IsNullOrWhiteSpace(value);
	}
}
