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
			Dictionary<string, HashSet<string>> techNodes = BuildTechNodeIndex(tables);
			ModifierTargetRegistry registry = ModifierTargetRegistry.From(tables);

			ValidateTerrains(tables, report);
			ValidateResources(tables, report);
			ValidateBuildings(tables, report, terrainIds, resourceIds, techNodes, registry);
			ValidateUnits(tables, report, terrainIds, resourceIds, techNodes, registry);
			ValidateTechTrees(tables, report, registry);
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
			Dictionary<string, HashSet<string>> techNodes, ModifierTargetRegistry registry)
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
			}
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
				if (unit.MoveRechargePerTick < 0f) report.Error("Units", key, $"MoveRechargePerTick={unit.MoveRechargePerTick} 不能为负");
				else if (unit.MoveRechargePerTick == 0f) report.Warn("Units", key, "MoveRechargePerTick=0：移动力不会自然恢复");
				if (unit.AttackDamage > 0f && unit.Attack <= 0)
					report.Warn("Units", key, "AttackDamage>0 但 Attack<=0：两套攻击口径混用，请确认哪个被使用");
				if (unit.PopulationCost < 0) report.Error("Units", key, $"PopulationCost={unit.PopulationCost} 不能为负");
				else if (unit.PopulationCost == 0) report.Warn("Units", key, "PopulationCost=0：该单位不占人口");

				if (unit.Duration < 0f) report.Error("Units", key, $"Duration={unit.Duration} 不能为负");
				else if (unit.Duration == 0f) report.Warn("Units", key, "Duration=0：瞬间训练完成");
				else ValidateDayUnit(report, "Units", key, "Duration", unit.Duration);
			}
		}

		// ────────────────────────── TechTrees ──────────────────────────

		private static void ValidateTechTrees(ConfigTables tables, ConfigReport report, ModifierTargetRegistry registry)
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

					// 同表内闭合：前置必须落在同一棵树内（§13.z「引用完整性先只校验同表内闭合」→ error）
					foreach (string prerequisite in node.Prerequisites ?? new List<string>())
					{
						if (!NotEmpty(prerequisite))
						{
							report.Error("TechTrees", $"{treeId}/{key}", "前置列表中存在空 Id");
							continue;
						}
						if (prerequisite == key)
						{
							report.Error("TechTrees", $"{treeId}/{key}", "前置指向自己（自环）");
							continue;
						}
						if (!nodes.ContainsKey(prerequisite))
							report.Error("TechTrees", $"{treeId}/{key}",
								$"前置 \"{prerequisite}\" 不在同一棵树内（{treeId} 现有节点：{string.Join(", ", nodes.Keys)}）");
					}
				}

				ValidatePrerequisiteCycles(nodes, treeId, report);
			}
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
				if (gameEvent.TriggerChance < 0f || gameEvent.TriggerChance > 1f)
					report.Error("Events", key, $"TriggerChance={gameEvent.TriggerChance} 必须落在 [0,1]");
				else if (gameEvent.TriggerChance == 0f)
					report.Warn("Events", key, "TriggerChance=0：该事件永远不会触发");
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

		/// <summary>全树节点索引：treeId → 节点 Id 集合（供建筑/单位/事件的跨表引用校验）。</summary>
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

		/// <summary>前置成环检测（迭代式 DFS 三色）：成环的科技永远无法研发。</summary>
		private static void ValidatePrerequisiteCycles(Dictionary<string, ITechNodeConfig> nodes, string treeId, ConfigReport report)
		{
			const int InProgress = 1;
			const int Done = 2;
			var state = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (string start in nodes.Keys)
			{
				if (state.ContainsKey(start)) continue;
				state[start] = InProgress;
				var stack = new Stack<(string Node, int Next)>();
				stack.Push((start, 0));

				while (stack.Count > 0)
				{
					(string node, int next) = stack.Pop();
					List<string> prerequisites = nodes.TryGetValue(node, out ITechNodeConfig config) ? config?.Prerequisites : null;
					if (prerequisites == null || next >= prerequisites.Count)
					{
						state[node] = Done;
						continue;
					}

					stack.Push((node, next + 1));
					string prerequisite = prerequisites[next];
					// 空 Id / 缺失前置 / 自环都由前面的规则单独报错，这里只负责"环"
					if (!NotEmpty(prerequisite) || prerequisite == node || !nodes.ContainsKey(prerequisite)) continue;

					if (state.TryGetValue(prerequisite, out int prerequisiteState) && prerequisiteState == InProgress)
					{
						report.Error("TechTrees", $"{treeId}/{prerequisite}", "前置关系成环：该科技将永远无法研发");
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
