using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Map.Application;
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
	/// （v0.3 / WP-2.3）**人口任务修 bug**（`D11`、`CON-04/05/06`）的验收检查。
	/// <para>**M0-2 ③** 的门槛：营地 90 日建成 → 人口上限（设计稿「半径 1 格内总计 9 人」）与视野生效。</para>
	/// <para>端到端形态与 `WP-2.7` 一致：真实配置 + 真实地图 + 真实任务仓储（临时目录）+ 自建时间总线；
	/// 区别是本用例需要**精确控制间隔对齐**（完工日 vs 人口间隔），所以时钟由用例自持。</para>
	/// </summary>
	internal static class PopulationGrowthChecks
	{
		private const string MapId = "pop-map";

		public static void RunAll()
		{
			Check.Run("WP-2.3 任务归属：人口任务带建筑所有者（`CON-04`，旧实现恒为 0）", HousingTaskCarriesOwner);
			Check.Run("M0-2 ③ 营地 90 日建成 → 视野半径 1 可见 + 人口按 300 日间隔增长", CampUnlocksVisionAndPopulation);
			Check.Run("WP-2.3 人口上限：半径 1 格内**总计** 9 人（`CON-05`，旧实现每格 9 → 最多 63）", PopulationCapIsWithinRadiusTotal);
			Check.Run("WP-2.3 `PopulationGrowth` 修正器接线：+1 Absolute → 每间隔 +2 人", PopulationGrowthModifierIsApplied);
			Check.Run("WP-2.3 小数余量：+50% → 两轮共 +3 人（不丢余量）", FractionalGrowthAccumulates);
			Check.Run("WP-2.3 拆除回收：住房被拆 → 人口任务范围注销且人口停止增长（`CON-06`）", DemolitionStopsPopulation);
			Check.Run("WP-2.3 存档层：人口改动打脏标记，存档点写盘且不重复（`CON-05`）", PopulationIsMarkedDirty);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void HousingTaskCarriesOwner()
		{
			Harness h = NewHarness(ownerId: 2);
			try
			{
				BuildCamp(h);
				h.Clock.AdvanceDays(90);

				TaskSnapshot growth = GrowthTask(h);
				Check.Assert(growth != null, "完工后应注册人口增长任务");
				Check.AssertEqual(2, growth.OwnerId, "任务归属（旧实现硬编码 0 → 多玩家下人口会挂到别人名下）");
				Check.AssertEqual(MapId, growth.MapId, "任务的地图");
				Check.AssertEqual("PopulationGrowth", growth.Type, "任务类型");
				Check.AssertEqual(300f, growth.Target, "人口增长间隔（游戏日）");
				Check.AssertEqual("none", growth.UId, "人口任务以建筑 uid 作业务键（`Id`），`UId` 无实体实例");
				Check.AssertEqual($"PopulationGrowth:none:{growth.Id}", growth.Key, "存储键");
				Check.AssertEqual(1, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "PopulationGrowth"),
					"人口任务只应有一条");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void CampUnlocksVisionAndPopulation()
		{
			Harness h = NewHarness();
			try
			{
				BuildCamp(h);

				h.Clock.AdvanceDays(89);
				Check.Assert(!h.Map.GetOccupantInfo(MapId, h.Site).Value.IsReady, "第 89 日营地不应完工");
				Check.AssertEqual(0, Population(h), "完工前不应有人口");

				h.Clock.AdvanceDays(1); // 第 90 日完工
				MapOccupantInfo? built = h.Map.GetOccupantInfo(MapId, h.Site);
				Check.Assert(built.HasValue && built.Value.IsReady, "第 90 日营地应完工就绪");

				// 视野生效：建造地块 + 半径 1 内的格子都可见（营地 VisionRadius = 1）
				Check.Assert(h.Fog.GetVisibility(h.Site) == FogAppService.Visible, "完工后施工地块应可见");
				foreach (HexCubePosition pos in h.Site.InRadius(1))
					Check.Assert(h.Fog.GetVisibility(pos) == FogAppService.Visible, $"半径 1 内的 {pos.q},{pos.r} 应可见");

				// 人口上限生效：完工后第 300 日出现第 1 人（间隔来自配置）
				h.Clock.AdvanceDays(299);
				Check.AssertEqual(0, Population(h), "完工后 299 日仍不应有人口");
				h.Clock.AdvanceDays(1);
				Check.AssertEqual(1, Population(h), "完工 +300 日应出现第 1 人");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void PopulationCapIsWithinRadiusTotal()
		{
			Harness h = NewHarness(fast: true); // 间隔压到 3 日：本用例只检验上限口径，不是间隔
			try
			{
				BuildCamp(h);
				h.Clock.AdvanceDays(90 + 3 * 12); // 12 个间隔：足够撞上上限

				Check.AssertEqual(9, Population(h), "半径 1 格内人口总计应封顶 9（设计稿口径；旧实现每格 9 → 最多 63）");
				Check.AssertEqual(9, h.Map.GetMapCell(MapId, h.Site).Population, "增长落在聚落中心格（住房所在格）");

				h.Clock.AdvanceDays(3 * 10);
				Check.AssertEqual(9, Population(h), "到达上限后不再增长");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void PopulationGrowthModifierIsApplied()
		{
			Harness h = NewHarness(fast: true);
			try
			{
				h.Modifier.AddModifier(MapId, h.OwnerId, "test-boost",
					new Modifier { Target = "PopulationGrowth", Type = "Absolute", Value = 1f });

				BuildCamp(h);
				h.Clock.AdvanceDays(90 + 3);
				Check.AssertEqual(2, Population(h), "+1 Absolute 后每个间隔应 +2 人（旧实现填了修正器也不生效）");

				h.Clock.AdvanceDays(3);
				Check.AssertEqual(4, Population(h), "两个间隔后 +4 人");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void FractionalGrowthAccumulates()
		{
			Harness h = NewHarness(fast: true);
			try
			{
				h.Modifier.AddModifier(MapId, h.OwnerId, "test-half",
					new Modifier { Target = "PopulationGrowth", Type = "Percent", Value = 0.5f });

				BuildCamp(h);
				h.Clock.AdvanceDays(90 + 3);
				Check.AssertEqual(1, Population(h), "第 1 轮：1.5 → 落 1 人，余量 0.5 保留");

				h.Clock.AdvanceDays(3);
				Check.AssertEqual(3, Population(h), "第 2 轮：0.5 + 1.5 = 2.0 → 再落 2 人（余量不丢）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void DemolitionStopsPopulation()
		{
			Harness h = NewHarness(fast: true);
			try
			{
				BuildCamp(h);
				h.Clock.AdvanceDays(90 + 3);
				int before = Population(h);
				Check.AssertEqual(1, before, "拆除前应有 1 人");

				string uid = h.Map.GetOccupantInfo(MapId, h.Site).Value.UId;

				// `MAP-04` 现状：建造只写 `cell.Occupant`，`cell.Building` 恒空 → `RemoveBuildingByPosition`
				// 走 `GetBuildingInfo` 会整体失效。这里在 **domain 层预置权威建筑**，用来验证 `CON-06` 的接线：
				// 一旦 `WP-3.4` 把占用模型统一，这条路径就会自然生效。
				h.MapSession.Get(MapId).SetBuilding(h.Site, h.Map.FindOccupantByUId(MapId, uid));

				h.Construction.RemoveBuildingByPosition(MapId, h.Site);

				Check.Assert(GrowthTask(h) == null, "拆除后人口任务应被范围注销（`CON-06`）");
				h.Clock.AdvanceDays(3 * 20);
				Check.AssertEqual(before, Population(h), "拆除后人口不应继续增长");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void PopulationIsMarkedDirty()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var mapRepository = new InMemoryMapRepository();
			var session = new MapSession(mapRepository);
			var map = new MapAppService(core.MapGenerator, session);

			map.GenerateMap(20260914, 8, 8, MapId);
			HexCubePosition center = map.GetAllCells(MapId).First(c => map.IsClear(MapId, c.Position)).Position;

			Check.AssertEqual(1, mapRepository.SaveCount, "生成地图时落盘一次");
			Check.Assert(!session.IsDirty(MapId), "刚生成的地图不是脏的");

			Check.AssertEqual(3, map.AddPopulation(MapId, center, 1, 9, 3), "首次可加 3 人");
			Check.Assert(session.IsDirty(MapId), "人口改动必须打脏标记（否则存档点不会写盘 —— `CON-05`）");
			Check.AssertEqual(6, map.AddPopulation(MapId, center, 1, 9, 99), "上限 9：第二次最多再容纳 6 人");
			Check.AssertEqual(0, map.AddPopulation(MapId, center, 1, 9, 1), "达上限后不再增加");
			Check.AssertEqual(9, map.GetPopulationWithin(MapId, center, 1), "范围内总计");

			session.Flush(MapId);
			Check.AssertEqual(2, mapRepository.SaveCount, "存档点写盘一次");
			Check.Assert(!session.IsDirty(MapId), "写盘后清除脏标记");
			Check.AssertEqual(9, map.GetPopulationWithin(MapId, center, 1), "写盘不改变内存态人口");
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public GameClock Clock;
			public GameTimeService Time;
			public TaskRepository Tasks;
			public MapSession MapSession;
			public MapAppService Map;
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public ConstructionAppService Construction;
			public FogAppService Fog;
			public ConfigTables Tables;
			public HexCubePosition Site;
		}

		private static Harness NewHarness(int ownerId = 1, bool fast = false)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp23-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			CoreServices core = fast
				? ConfigFixtures.BuildCore(FastConfigSource())
				: ConfigFixtures.BuildRealCore();
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue }; // `D28`：跨 >30 日的推进必须放开单帧上限
			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"));
			var time = new GameTimeService(clock, tasks);

			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_")),
				core.Tables.Resources,
				time,
				new ModifierRepository(Path.Combine(dir, "mod_")));
			var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(dir, "mod_")));
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees),
				core.Tables.TechTrees,
				resources,
				modifier,
				time);
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_")));

			// 本用例需要 domain 句柄（预置权威建筑 / 读竣工信息）→ 自建会话与地图服务
			var mapRepository = new InMemoryMapRepository();
			var session = new MapSession(mapRepository);
			var map = new MapAppService(core.MapGenerator, session);

			var construction = new ConstructionAppService(
				map,
				resources,
				tech,
				new BuildingFactory(core.Tables.Buildings),
				core.Tables.Buildings,
				time,
				modifier,
				fog);

			map.GenerateMap(20260914, 8, 8, MapId);

			// 生成器按 Voronoi 铺地形、小图常只剩一个群系 → 施工地块直接写死为平原，
			// 并挑一个**内部格**（q/r ∈ [2,5]），保证半径 1 的邻格都在图内。
			MapCell site = map.GetAllCells(MapId).First(c => map.IsClear(MapId, c.Position)
				&& c.Position.q is >= 2 and <= 5 && c.Position.r is >= 2 and <= 5);
			map.SetTerrain(MapId, site.Position, core.Tables.Terrains.GetById("plain"));

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = clock,
				Time = time,
				Tasks = tasks,
				MapSession = session,
				Map = map,
				Resources = resources,
				Modifier = modifier,
				Construction = construction,
				Fog = fog,
				Tables = core.Tables,
				Site = site.Position,
			};
		}

		private static void BuildCamp(Harness h)
		{
			h.Resources.AddResource("Wood", 200f, MapId, h.OwnerId); // 营地造价：Wood 20
			h.Construction.StartConstruction(MapId, "camp", h.Site, h.OwnerId);
		}

		/// <summary>
		/// 测试用配置源：把真实 <c>Config/Buildings.json</c> 里营地的人口增长间隔 300 日改成 3 日，
		/// 让「撞上限 / 多轮增长 / 小数余量」这类用例不必推进上千日（任务快照是**每日每个任务写盘一次**，
		/// 长跑用例的 IO 会线性膨胀）。间隔本身由 M0-2 ③ 的用例在**真实配置**下验证。
		/// </summary>
		private static InMemoryConfigSource FastConfigSource()
		{
			const string realInterval = "\"PopulationGrowthInterval\": 300";
			string buildings = File.ReadAllText(ConfigFixtures.TablePath("Buildings"));
			Check.Assert(buildings.Contains(realInterval), "真实建筑表里营地人口间隔应仍是 300 日（本用例只是临时压缩它）");

			return ConfigFixtures.RealConfigSourceWith("Buildings", buildings.Replace(realInterval, "\"PopulationGrowthInterval\": 3"));
		}

		private static int Population(Harness h) => h.Map.GetPopulationWithin(MapId, h.Site, 1);

		private static TaskSnapshot GrowthTask(Harness h)
			=> h.Tasks.GetCurrentTasks(MapId).FirstOrDefault(t => t.Type == "PopulationGrowth");

		private static void Cleanup(string dir)
		{
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch (IOException)
			{
				// 临时目录清理失败不影响验收结果
			}
		}
	}
}

