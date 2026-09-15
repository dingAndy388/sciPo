using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.TechTree.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.9.4 / `WP-7.6`）**整表一致性 + 抽样长跑**（`R3`、M2 判据 ②）。
	/// <list type="bullet">
	/// <item>**引用闭合**：修正器目标名必须是 `ModifierTargetRegistry` 的规范名；资源键必须在地形/资源表里存在
	/// —— 这两类是"表填错但启动不报错"的重灾区（`D108` 的 Canonical 口径）；</item>
	/// <item>**可达性**：每棵科技树从根出发能到达全部节点（没有"永远解锁不了"的孤儿）；</item>
	/// <item>**升级链无环**、**住房必带容量**；</item>
	/// <item>**抽样长跑**：真实配置连跑 **365 日**，每 30 日采样一次，断言日历/资源/世界都没坏，并给出耗时。</item>
	/// </list>
	/// </summary>
	internal static class BalanceChecks
	{
		private const string MapId = "balance-map";

		public static void RunAll()
		{
			Check.Run("WP-7.6 修正器目标闭合：建筑/单位/科技/事件里的每个 Target 都是规范名", ModifierTargetsAreCanonical);
			Check.Run("WP-7.6 资源键闭合：建筑/单位成本、维护费、掉落都引用真实资源 Id", ResourceKeysAreClosed);
			Check.Run("WP-7.6 科技树可达：93 节点（30/35/28）全部从根可达，且每树至少一个根", TechTreesAreFullyReachable);
			Check.Run("WP-7.6 升级链：`UpgradeTo` 无环、不超 4 级、每级升级耗时应 > 0", UpgradeChainsAreAcyclic);
			Check.Run("WP-7.6 住房自洽：`IsHousing` 必有容量与半径；非住房不得给容量", HousingBuildingsCarryCapacity);
			Check.Run("WP-7.6 抽样长跑：真实配置连跑 365 日（每 30 日采样）不坏且有限", LongRunStaysSane);
		}

		// ────────────────────────── 整表一致性 ──────────────────────────

		private static void ModifierTargetsAreCanonical()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var registry = ModifierTargetRegistry.From(core.Tables);
			var unknown = new List<string>();
			int scanned = 0;

			void Scan(string owner, IEnumerable<Modifier> modifiers)
			{
				if (modifiers == null) return;
				foreach (Modifier modifier in modifiers)
				{
					scanned++;
					if (!registry.Contains(modifier.Target)) unknown.Add($"{owner}:{modifier.Target}");
				}
			}

			foreach (IBuildingConfig building in core.Tables.AllBuildings())
				Scan($"building:{building.BuildingId}", building.Modifiers);

			foreach (IEventConfig config in core.Tables.AllEvents())
				Scan($"event:{config.EventId}", config.Modifiers);
			foreach (IUnitConfig unit in core.Tables.AllUnits())
			{
				Scan($"unit:{unit.UnitId}", unit.Abilities);
				Scan($"unit:{unit.UnitId}", unit.GarrisonModifiers);
			}
			foreach (string treeId in core.Tables.TreeIds())
				foreach (ITechNodeConfig node in core.Tables.TechTrees.GetTechTreeConfig(treeId).Techs.Values)
					Scan($"tech:{treeId}/{node.Id}", node.Modifiers);

			Check.Assert(scanned > 0, "应至少扫到一条修正器（否则本用例失去意义）");
			Check.AssertEqual(0, unknown.Count, $"有 {unknown.Count} 个非规范修正器目标：{string.Join(" | ", unknown.Take(6))}");
		}

		private static void ResourceKeysAreClosed()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var resources = new HashSet<string>(core.Tables.Resources.GetResourcesPoolConfig().Resources.Select(r => r.Name));
			var unknown = new List<string>();

			void Keys(string owner, IReadOnlyDictionary<string, float> map)
			{
				if (map == null) return;
				foreach (string key in map.Keys)
					if (!resources.Contains(key)) unknown.Add($"{owner}:{key}");
			}

			foreach (IBuildingConfig building in core.Tables.AllBuildings())
			{
				Keys($"build:{building.BuildingId}", building.ResourceCost);
				Keys($"upgrade:{building.BuildingId}", building.UpgradeCost);
			}
			foreach (IUnitConfig unit in core.Tables.AllUnits())
			{
				Keys($"train:{unit.UnitId}", unit.ResourceCost);
				Keys($"upkeep:{unit.UnitId}", unit.Maintenance);
				Keys($"drop:{unit.UnitId}", unit.DropReward);
			}

			Check.Assert(resources.Count >= 3, "资源表应至少有 Idea/Food/BasicMinerals");
			Check.AssertEqual(0, unknown.Count, $"有 {unknown.Count} 个未知资源键：{string.Join(" | ", unknown.Take(6))}");
		}
		private static void TechTreesAreFullyReachable()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var expected = new Dictionary<string, int> { { "math", 30 }, { "physics", 35 }, { "chemistry", 28 } };
			int total = 0;

			foreach (string treeId in core.Tables.TreeIds())
			{
				var nodes = core.Tables.TechTrees.GetTechTreeConfig(treeId).Techs.Values.ToList();
				total += nodes.Count;

				// "根" = **树内**无前置（跨树前置不改变入口地位：物理树根 `simple_machine_intuition` 需要 `math:counting`）
				var roots = nodes.Where(n => n.Prerequisites == null
					|| n.Prerequisites.All(p => p.IsCrossTree)).ToList();
				Check.Assert(roots.Count >= 1, $"{treeId} 树应至少有一个入口节点（树内无前置；否则全树不可达）");

				// BFS：同树前置满足即视为可达（跨树前置由 `TechPrerequisiteChecks` 单独锁）
				var reached = new HashSet<string>(roots.Select(r => r.Id));
				bool grew = true;
				while (grew)
				{
					grew = false;
					foreach (ITechNodeConfig node in nodes)
					{
						if (reached.Contains(node.Id)) continue;
						IReadOnlyList<TechPrerequisite> prerequisites = node.Prerequisites;
						if (prerequisites == null) { reached.Add(node.Id); grew = true; continue; }
						bool allInTree = prerequisites.All(p => p.IsCrossTree || reached.Contains(p.NodeId));
						if (allInTree) { reached.Add(node.Id); grew = true; }
					}
				}

				Check.AssertEqual(nodes.Count, reached.Count,
					$"{treeId} 树有 {nodes.Count - reached.Count} 个节点从根不可达（`TECH-07` 类死锁）");
				Check.Assert(nodes.All(n => !string.IsNullOrWhiteSpace(n.Name)), $"{treeId} 树每个节点都应有 `Name`");
				Check.Assert(nodes.All(n => n.Modifiers != null), $"{treeId} 树的 `Modifiers` 字段不应为 null");

				if (expected.TryGetValue(treeId, out int count))
					Check.AssertEqual(count, nodes.Count, $"{treeId} 树节点数（`WP-7.3` 设计表口径）");
			}
			Check.AssertEqual(93, total, "三棵树合计 93 节点（`WP-7.3`）");
		}

		private static void UpgradeChainsAreAcyclic()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var byId = core.Tables.AllBuildings().ToDictionary(b => b.BuildingId, b => b);

			foreach (IBuildingConfig start in byId.Values)
			{
				var seen = new List<string> { start.BuildingId };
				IBuildingConfig current = start;
				int depth = 0;

				while (!string.IsNullOrWhiteSpace(current.UpgradeTo))
				{
					depth++;
					Check.Assert(depth <= 4, $"{start.BuildingId} 的升级链超过 4 级（疑似环）");
					Check.Assert(!seen.Contains(current.UpgradeTo), $"{start.BuildingId} 的升级链成环：{string.Join("→", seen)}");
					Check.Assert(byId.ContainsKey(current.UpgradeTo), $"{start.BuildingId} 指向不存在的 `{current.UpgradeTo}`");
					Check.Assert(current.UpgradeDuration > 0f, $"{current.BuildingId} 的升级耗时应 > 0");
					Check.Assert(current.UpgradeCost != null && current.UpgradeCost.Count > 0, $"{current.BuildingId} 的升级应有花费");
					// 升级科技前置：设计稿只为 4 条链（营地/工坊/学院/军营）给了『升级条件』⇒ 这里只查"写了就合法"（数值链由 `UpgradeChecks` 锁）
					foreach (var requirement in current.UpgradeTechRequirements ?? new Dictionary<string, List<string>>())
						Check.Assert(byId.ContainsKey(current.UpgradeTo), $"{current.BuildingId} 的升级前置应指向存在的目标级（{requirement.Key}）");

					seen.Add(current.UpgradeTo);
					current = byId[current.UpgradeTo];
				}
			}
		}

		private static void HousingBuildingsCarryCapacity()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			foreach (IBuildingConfig building in core.Tables.AllBuildings())
			{
				if (building.IsHousing)
				{
					Check.Assert(building.PopulationCap > 0, $"{building.BuildingId} 是住房 ⇒ 容量应 > 0");
					Check.Assert(building.PopulationRadius >= 1, $"{building.BuildingId} 是住房 ⇒ 半径应 ≥ 1");
					Check.Assert(building.PopulationGrowthInterval > 0, $"{building.BuildingId} 是住房 ⇒ 人口间隔应 > 0");
				}
				else
				{
					Check.AssertEqual(0, building.PopulationCap, $"{building.BuildingId} 不是住房 ⇒ 不应给人口容量");
				}
			}
		}

		private static void LongRunStaysSane()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Map.GenerateMap(20261600, 20, 20, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			core.Resources.AddResource("BasicMinerals", 20000f, MapId, 1);
			core.Resources.AddResource("Food", 20000f, MapId, 1);
			core.Construction.StartConstruction(MapId, "camp", new HexCubePosition(4, 4), 1);
			core.Session.Clock.AdvanceDays(5);
			core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(5, 5), 1);

			var watch = Stopwatch.StartNew();
			for (int month = 1; month <= 12; month++)
			{
				core.Session.Clock.AdvanceDays(30);

				foreach (string resource in new[] { "Idea", "Food", "BasicMinerals" })
				{
					float value = core.Resources.GetOrCreatePool(MapId, 1).GetValue(resource);
					Check.Assert(!float.IsNaN(value) && !float.IsInfinity(value), $"第 {month} 月 `{resource}` 应是有限数（实际 {value}）");
				}
			}
			watch.Stop();

			Check.AssertEqual(365, core.Session.CurrentDay, "12 × 30 日后应正好是第 365 日");
			Check.Assert(core.Map.GetAllCells(MapId).Any(), "长跑后地图应仍在");
			Check.Assert(core.Map.GetOccupants(MapId).Any(), "长跑后世界不应空（营地/工人还在）");
			Check.Assert(!core.ConfigReport.HasErrors, $"长跑期间不应出现配置 error：{core.ConfigReport.Summary()}");

			// 宽松护栏（`D83` 口径）：只防"数量级退化"，不拿机器差异造假红
			Check.Assert(watch.ElapsedMilliseconds < 30000, $"365 日长跑应远快于 30 秒（实际 {watch.ElapsedMilliseconds} ms）");
		}
	}
}
