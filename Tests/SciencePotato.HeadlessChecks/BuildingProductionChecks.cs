using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using System;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-2.7）**建筑产出接线**的验收检查：建筑表 Id 与数值对齐设计稿，
	/// 并把「建筑 Modifier（Absolute）→ 月结（`GrowInterval=30`）→ 资源池」这条链路端到端跑通。
	/// <para>**M0-2 ②** 的门槛：建成 School（学院）后 6 个月内存出 250 idea/月。</para>
	/// </summary>
	internal static class BuildingProductionChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-2.7 建筑表：4 条原型建筑 + 设计口径字段（营地人口 / School 250 idea/月）", BuildingTableMatchesDesign);
			Check.Run("WP-2.7 建筑 Id 收敛：旧原型 Id（house/library/barracks）不再存在", LegacyBuildingIdsGone);
			Check.Run("M0-2 ② 建成 School 后 6 个月内存出 250 idea/月（拆除后立即停止）", SchoolProducesIdeaMonthly);
		}

		// ────────────────────────── 配置 ──────────────────────────

		private static void BuildingTableMatchesDesign()
		{
			ConfigTables tables = ConfigFixtures.BuildRealCore().Tables;

			Check.AssertEqual(12, tables.AllBuildings().Count(), "建筑条目数（WP-2.6：营地/工坊/学院/军营 各 3 级 = 12）");

			IBuildingConfig camp = tables.Buildings.GetBuildingConfig("camp");
			Check.AssertEqual("营地", camp.Name, "营地名称");
			Check.Assert(camp.IsHousing, "营地是住房建筑");
			Check.AssertEqual(9, camp.PopulationCap, "营地人口上限（设计：9）");
			Check.AssertEqual(1, camp.PopulationRadius, "营地人口半径（设计：1 格）");
			Check.AssertEqual(300, camp.PopulationGrowthInterval, "营地人口增长间隔（设计 300 秒 → 日口径 300 日）");
			Check.AssertEqual(90f, camp.Duration, "营地建造时间（设计：90 日）");
			Check.Assert(camp.VisionRadius >= 1, "营地必须提供视野（M0-2 ③）");

			IBuildingConfig school = tables.Buildings.GetBuildingConfig("school");
			Check.AssertEqual("学院", school.Name, "School 名称");
			Check.AssertEqual(240f, school.Duration, "School 建造时间（设计：240 日）");
			Modifier growth = school.Modifiers.Single();
			Check.AssertEqual("IdeaGrowth", growth.Target, "School 的产出 Target");
			Check.AssertEqual("Absolute", growth.Type, "School 产出应为 Absolute（月结基数，不做百分比）");
			Check.AssertEqual(250f, growth.Value, "School 每月 idea 产出（设计：250/月）");
			Check.Assert(school.Actions.Contains("CanResearch"), "School 应可执行研究动作");

			IBuildingConfig workshop = tables.Buildings.GetBuildingConfig("workshop");
			Check.AssertEqual("工坊", workshop.Name, "工坊名称");
			Check.AssertEqual(90f, workshop.Duration, "工坊建造时间（设计：90 日）");
			Check.AssertEqual(0, workshop.Modifiers.Count, "工坊无资源产出（设计：只训练工人）");

			IBuildingConfig militaryCamp = tables.Buildings.GetBuildingConfig("military_camp");
			Check.AssertEqual("军营", militaryCamp.Name, "军营名称");
			Check.AssertEqual(60f, militaryCamp.Duration, "军营建造时间（设计：60 日）");
		}

		private static void LegacyBuildingIdsGone()
		{
			ConfigTables tables = ConfigFixtures.BuildRealCore().Tables;

			foreach (string legacy in new[] { "house", "library", "barracks" })
				Check.Assert(tables.Buildings.GetBuildingConfig(legacy) == null,
					$"旧原型 Id \"{legacy}\" 应已被设计口径的 Id 取代（营地 camp / 学院 school / 军营 military_camp）");
		}

		// ────────────────────────── M0-2 ② ──────────────────────────

		/// <summary>
		/// 端到端：真实配置 + 真实地图/仓储（临时目录）+ 真实时间总线，
		/// 走「开工 → 240 日完工 → 月结读修正器 → 6 个月 +1500 idea → 拆除即停」的完整链路。
		/// </summary>
		private static void SchoolProducesIdeaMonthly()
		{
			string directory = Path.Combine(Path.GetTempPath(), "sp-wp27-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);

			try
			{
				const string mapId = "prod-map";
				const int ownerId = 1;

				CoreServices core = ConfigFixtures.BuildRealCore();

				// D2：时钟默认单帧最多派发 30 日（防卡顿）。无头验收要一次推进数百日，
				// 必须显式放开上限，否则 AdvanceDays(239) 只会派发 30 日、其余记入欠账（本用例曾因此"卡在 day 60"）。
				core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
				GameTimeService time = core.Time;
				var resources = new ResourcesAppService(
					new ResourcesRepository(Path.Combine(directory, "res_")),
					core.Tables.Resources,
					time,
					new ModifierRepository(Path.Combine(directory, "mod_")));
				var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(directory, "mod_")));
				var construction = new ConstructionAppService(
					core.Map,
					resources,
					new TechTreesAppService(
						new TechTreesRepository(Path.Combine(directory, "tech_"), core.Tables.TechTrees),
						core.Tables.TechTrees,
						resources,
						modifier,
						time),
					new BuildingFactory(core.Tables.Buildings),
					core.Tables.Buildings,
					time,
					modifier,
					new FogAppService(ownerId, new FogRepository(Path.Combine(directory, "fog_"))));

				core.Map.GenerateMap(20260914, 8, 8, mapId);

				// 生成器按 Voronoi 锚点铺地形，锚点数随面积缩放（`Density/100 × 面积`）→ 8×8 这种小图常常只剩一个群系
				// （实测本 seed 全图 water）。施工地块因此直接写死为平原，保证用例只检验"建造 → 产出"这条链路。
				MapCell site = core.Map.GetAllCells(mapId).First(c => core.Map.IsClear(mapId, c.Position));
				core.Map.SetTerrain(mapId, site.Position, core.Tables.Terrains.GetById("plain"));
				HexCubePosition position = site.Position;

				// 资源到位（学院：Gold 100 + Wood 50）；建池同时注册 3 条月结任务（GrowInterval=30）
				resources.AddResource("Gold", 1000f, mapId, ownerId);
				resources.AddResource("Wood", 1000f, mapId, ownerId);
				resources.AddResource("Idea", 0f, mapId, ownerId);

				construction.StartConstruction(mapId, "school", position, ownerId);

				// ① 施工期内不应有 idea 产出（产出 Modifier 只在完工回调里注册）
				core.Session.Clock.AdvanceDays(239);
				Check.AssertEqual(0f, IdeaOf(resources, mapId, ownerId), "学院完工前 Idea 不应增长");

				// ② 第 240 日完工：建筑就绪（**占用槽位以 `cell.Occupant` 为准** —— 见下方 ⑤ 的已知缺陷）
				core.Session.Clock.AdvanceDays(1);
				MapOccupantInfo? built = core.Map.GetOccupantInfo(mapId, position);
				Check.Assert(built.HasValue, "学院应占据该地块");
				Check.Assert(built.Value.IsReady, "学院应在 240 日后完工就绪");

				float afterBuild = IdeaOf(resources, mapId, ownerId);

				// ③ 完工后 6 个月：每月 +250，共 6 次（月结读 ModifierManager）
				core.Session.Clock.AdvanceDays(180);
				float sixMonths = IdeaOf(resources, mapId, ownerId);
				Check.AssertEqual(1500f, sixMonths - afterBuild, "完工后 6 个月应累计 6 次 × 250 idea");
				Check.Assert(sixMonths >= 1500f, $"6 个月累计 idea 应达 250/月（实际 {sixMonths}）");

				// ④ 产出可回收：按 sourceId（= 建筑 uid）回收修正器后，月结立刻不再计入该建筑
				modifier.RemoveModifiersBySourceId(mapId, ownerId, built.Value.UId);
				float beforeRemoval = IdeaOf(resources, mapId, ownerId);
				core.Session.Clock.AdvanceDays(30);
				Check.AssertEqual(beforeRemoval, IdeaOf(resources, mapId, ownerId), "修正器回收后不应再有 idea 产出");

				// ⑤ **拆除生效**（v0.3 / WP-3.4 修复 `MAP-04`）：建造现在同时写 `cell.Building` 与占据物槽位，
				// 因此 `RemoveBuildingByPosition` 能真正拆掉建筑、回收修正器，并注销它名下的任务。
				float beforeDemolition = IdeaOf(resources, mapId, ownerId);
				construction.RemoveBuildingByPosition(mapId, position);
				Check.Assert(!core.Map.GetOccupantInfo(mapId, position).HasValue,
					"拆除后地块应被清空（旧实现因 `cell.Building` 恒空而整体失效）");
				core.Session.Clock.AdvanceDays(30);
				Check.AssertEqual(beforeDemolition, IdeaOf(resources, mapId, ownerId), "拆除后不应再产出 idea");
			}
			finally
			{
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		/// <summary>从盘上重新读回资源池（与运行时结算的读法一致），取 Idea 当前值。</summary>
		private static float IdeaOf(ResourcesAppService resources, string mapId, int ownerId)
			=> resources.GetOrCreatePool(mapId, ownerId).GetValue("Idea");
	}
}
