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
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-2.5）**训练绑定建筑 + 队列**（`UNIT-11`、`D10`、`E2/E3/E4`）的验收检查。
	/// <para>**M0-2 ④** 的门槛：工坊训练工人 30 日后单位出现且人口 −1。</para>
	/// </summary>
	internal static class TrainingQueueChecks
	{
		private const string MapId = "train-map";

		public static void RunAll()
		{
			Check.Run("WP-2.5 建筑表：TrainableUnits / TrainingQueueLimit / CanTrain 口径正确（真实配置 0 error/warning）", BuildingTableDeclaresTraining);
			Check.Run("M0-2 ④ 工坊 90 日建成 → 训练工人 30 日后单位落位且人口 −1", WorkshopTrainsWorker);
			Check.Run("WP-2.5 队列：上限 5、同时只训练 1 个、完成后自动推进下一个（`E3`）", QueueSerializesTraining);
			Check.Run("WP-2.5 门控：非训练建筑 / 名单外单位 / 资源不足 / 人口不足 / 未完工 全部拒绝", TrainingGatesRejectInvalid);
			Check.Run("WP-2.5 任务键：两座工坊同时训练同名单位不再互相覆盖（`TIME-03`）", SameUnitDifferentInstanceKeepBothTasks);
			Check.Run("WP-2.5 人口在**完成时**扣（`E2`）+ 落位在建筑相邻格（`E4`）", PopulationConsumedOnCompletion);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void BuildingTableDeclaresTraining()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			ConfigTables tables = core.Tables;

			Check.Assert(!core.ConfigReport.HasErrors, $"真实配置不应有 error（含 TrainableUnits 引用完整性），实际：{core.ConfigReport.Summary()}");

			// 真实配置允许存在白名单 warning（如 science/counting 的 Duration=0），但不能有训练相关的新 warning
			int trainingWarnings = core.ConfigReport.Issues.Count(issue => issue.Level == ConfigIssueLevel.Warning
				&& (issue.ToString().Contains("TrainableUnits") || issue.ToString().Contains("TrainingQueueLimit")));
			Check.AssertEqual(0, trainingWarnings, $"训练字段不应有 warning，实际：{core.ConfigReport.ToLines()}");

			IBuildingConfig workshop = tables.Buildings.GetBuildingConfig("workshop");
			Check.Assert(workshop.TrainableUnits.SequenceEqual(new[] { "worker" }), "工坊可训练工人（设计稿）");
			Check.AssertEqual(5, workshop.TrainingQueueLimit, "工坊训练队列上限（设计稿：5）");
			Check.Assert(workshop.Actions.Contains("CanTrain"), "工坊应声明 CanTrain 能力");

			IBuildingConfig military = tables.Buildings.GetBuildingConfig("military_camp");
			Check.Assert(military.TrainableUnits.SequenceEqual(new[] { "swordsman", "archer" }), "军营可训练民兵/弓箭手");

			foreach (string none in new[] { "camp", "school" })
			{
				IBuildingConfig config = tables.Buildings.GetBuildingConfig(none);
				Check.AssertEqual(0, config.TrainableUnits.Count, $"{none} 不训练任何单位");
				Check.AssertEqual(0, config.TrainingQueueLimit, $"{none} 的队列上限为 0（不排队）");
			}

			// 反向完整性：每个**玩家**单位都至少能被某个建筑训练（否则玩家永远造不出来）；
			// 敌方单位不参与训练系统（`WP-3.8`），不该出现在任何建筑的可训练列表里。
			List<string> trainable = tables.AllBuildings().SelectMany(b => b.TrainableUnits).ToList();
			foreach (IUnitConfig unit in tables.AllUnits())
			{
				if (unit.IsHostile)
				{
					Check.Assert(!trainable.Contains(unit.UnitId), $"{unit.UnitId} 是敌方单位，不应被任何建筑列为可训练");
					continue;
				}

				Check.Assert(trainable.Contains(unit.UnitId), $"{unit.UnitId} 应至少被一个建筑列为可训练");
			}
		}

		private static void WorkshopTrainsWorker()
		{
			Harness h = NewHarness(); // 真实配置：工坊 90 日 / 工人 30 日
			try
			{
				h.BuildCampAndWorkshop(); // 建营地 + 工坊（含 90 日施工）并注入人口
				Check.Assert(h.Map.GetOccupantInfo(MapId, h.Shop).Value.IsReady, "工坊应在 90 日后完工");

				int populationBefore = h.Map.GetPopulationWithin(MapId, h.Shop, 1);
				Check.Assert(populationBefore >= 1, "工坊半径 1 内应有人口（营地提供）");
				Check.AssertEqual(0, UnitCount(h), "训练前不应有单位");

				Check.Assert(h.Units.TrainUnit(MapId, h.ShopUid, "worker"), "应能入队训练工人");

				h.Clock.AdvanceDays(29);
				Check.AssertEqual(0, UnitCount(h), "第 29 日工人不应出现");
				Check.AssertEqual(populationBefore, h.Map.GetPopulationWithin(MapId, h.Shop, 1), "完成前人口不应被扣");

				h.Clock.AdvanceDays(1);
				Check.AssertEqual(1, UnitCount(h), "第 30 日工人应出现（**M0-2 ④**）");

				Unit worker = OnlyUnit(h);
				Check.AssertEqual("worker", worker.GetInfo().Id, "单位模板");
				Check.Assert(worker.IsReady, "单位应就绪");
				Check.Assert(worker.Position.DistenceTo(h.Shop) <= 1, "单位应落在建筑格或相邻格（E4）");
				Check.AssertEqual(1, worker.GetInfo().OwnerId, "单位归属");
				Check.AssertEqual(populationBefore - 1, h.Map.GetPopulationWithin(MapId, h.Shop, 1), "完成时人口应 −1（E2）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void QueueSerializesTraining()
		{
			Harness h = NewHarness(fast: true);
			try
			{
				h.Clock.AdvanceDays(2); // 快配置：建筑 2 日完工
				h.BuildCampAndWorkshop();

				for (int i = 0; i < 5; i++)
					Check.Assert(h.Units.TrainUnit(MapId, h.ShopUid, "worker"), $"第 {i + 1} 个订单应在队列上限内");
				Check.Assert(!h.Units.TrainUnit(MapId, h.ShopUid, "worker"), "第 6 个订单应被队列上限拒绝");

				Building shop = (Building)h.Map.FindOccupantByUId(MapId, h.ShopUid);
				Check.AssertEqual(5, shop.TrainingQueueCount, "队列长度 = 上限 5");
				Check.AssertEqual(1, TrainingTaskCount(h), "同时只训练 1 个：只有 1 条训练任务快照");

				h.Clock.AdvanceDays(3);
				Check.AssertEqual(1, UnitCount(h), "第 1 个订单完成后只应有 1 个单位");
				Check.AssertEqual(4, shop.TrainingQueueCount, "队列应缩短到 4");
				Check.AssertEqual(1, TrainingTaskCount(h), "下一个订单应已自动开工（仍只有 1 条任务）");

				h.Clock.AdvanceDays(3 * 4);
				Check.AssertEqual(5, UnitCount(h), "5 个订单全部完成后应有 5 个单位");
				Check.AssertEqual(0, shop.TrainingQueueCount, "队列应排空");
				Check.AssertEqual(0, TrainingTaskCount(h), "没有在训任务");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void TrainingGatesRejectInvalid()
		{
			Harness h = NewHarness(fast: true);
			try
			{
				h.Clock.AdvanceDays(2);

				// ① 未完工的建筑不能训练（施工中）
				h.Resources.AddResource("Wood", 100f, MapId, h.OwnerId);
				h.Construction.StartConstruction(MapId, "workshop", h.Shop, h.OwnerId);
				string pendingUid = h.Map.GetOccupantInfo(MapId, h.Shop).Value.UId;
				Check.Assert(!h.Units.TrainUnit(MapId, pendingUid, "worker"), "施工中的建筑不能训练");

				h.Clock.AdvanceDays(2); // 完工

				// ② 名单外的单位（工坊只能训练工人）
				Check.Assert(!h.Units.TrainUnit(MapId, pendingUid, "archer"), "工坊不能训练弓箭手（不在 TrainableUnits 里）");

				// ③ 非训练建筑（营地）
				Check.Assert(!h.Units.TrainUnit(MapId, h.CampUid, "worker"), "营地不是训练建筑");

				// ④ 不存在的单位 / 建筑
				Check.Assert(!h.Units.TrainUnit(MapId, pendingUid, "dragon"), "不存在的单位 Id 应被拒绝");
				Check.Assert(!h.Units.TrainUnit(MapId, "no-such-building", "worker"), "不存在的建筑 uid 应被拒绝");

				// ⑤ 资源不足（把 Gold 花光）
				ResourcesPool pool = h.Resources.GetOrCreatePool(MapId, h.OwnerId);
				h.Resources.CreateResourceConsumption(new Consumption("Gold", pool.GetValue("Gold")), MapId, h.OwnerId).Consume();
				Check.AssertEqual(0f, pool.GetValue("Gold"), "准备：Gold 归零");
				Check.Assert(!h.Units.TrainUnit(MapId, pendingUid, "worker"), "资源不足应被拒绝");

				// ⑥ 人口不足（人口为 0）
				h.Resources.AddResource("Gold", 500f, MapId, h.OwnerId);
				h.Map.ConsumePopulation(MapId, h.Camp, 1, 99);
				Check.AssertEqual(0, h.Map.GetPopulationWithin(MapId, h.Shop, 1), "准备：人口归零");
				Check.Assert(!h.Units.TrainUnit(MapId, pendingUid, "worker"), "人口不足应被拒绝");

				Check.AssertEqual(0, UnitCount(h), "所有被拒绝的请求都不应产生单位");
				Check.AssertEqual(0, TrainingTaskCount(h), "所有被拒绝的请求都不应产生任务");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void SameUnitDifferentInstanceKeepBothTasks()
		{
			Harness h = NewHarness(fast: true);
			try
			{
				h.Clock.AdvanceDays(2);
				h.BuildCampAndWorkshop();

				// 第二座工坊（放在营地另一侧的空格）
				HexCubePosition secondShop = h.Shop.InRadius(1)
					.First(pos => pos != h.Shop && h.Map.IsClear(MapId, pos)
						&& h.Map.GetMapCell(MapId, pos).Terrain?.Passable == true);
				h.Resources.AddResource("Wood", 100f, MapId, h.OwnerId);
				h.Construction.StartConstruction(MapId, "workshop", secondShop, h.OwnerId);
				h.Clock.AdvanceDays(2);

				string secondUid = h.Map.GetOccupantInfo(MapId, secondShop).Value.UId;
				Check.Assert(h.Units.TrainUnit(MapId, h.ShopUid, "worker"), "第一座工坊入队");
				Check.Assert(h.Units.TrainUnit(MapId, secondUid, "worker"), "第二座工坊入队");

				List<TaskSnapshot> trainings = TrainingTasks(h);
				Check.AssertEqual(2, trainings.Count, "同名单位的两条训练任务应同时存在（旧实现以 UnitId 为键 → 只剩 1 条）");
				Check.AssertEqual(2, trainings.Select(t => t.Key).Distinct().Count(), "两条任务的存储键应不同");
				Check.Assert(trainings.All(t => t.Type == "Training" && t.Id == "worker"), "任务类型与业务键");
				Check.Assert(trainings.All(t => t.UId != "none"), "训练任务以单位实例 uid 作键（落位凭据）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void PopulationConsumedOnCompletion()
		{
			Harness h = NewHarness(fast: true);
			try
			{
				h.Clock.AdvanceDays(2);
				h.BuildCampAndWorkshop();

				int before = h.Map.GetPopulationWithin(MapId, h.Shop, 1);
				Check.Assert(before >= 2, "准备：营地提供 ≥2 人口");

				Check.Assert(h.Units.TrainUnit(MapId, h.ShopUid, "worker"), "入队 1 个工人（E2：扣人口在完成时）");
				Check.AssertEqual(before, h.Map.GetPopulationWithin(MapId, h.Shop, 1), "入队时不扣人口");

				h.Clock.AdvanceDays(3);
				Check.AssertEqual(before - 1, h.Map.GetPopulationWithin(MapId, h.Shop, 1), "完成时扣 1 人口");

				Unit worker = OnlyUnit(h);
				Check.Assert(worker.Position != h.Shop, "落位不在建筑格（建筑自己占据该格）");
				Check.AssertEqual(1, worker.Position.DistenceTo(h.Shop), "落位在建筑相邻格（E4）");
				Check.Assert(!h.Map.IsClear(MapId, worker.Position), "落位格应被单位占据");
				Check.AssertEqual(worker.GetInfo().UId, h.Map.GetOccupantInfo(MapId, worker.Position).Value.UId, "按位置可取回该单位");
			}
			finally { Cleanup(h.Dir); }
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
			public UnitsAppService Units;
			public FogAppService Fog;
			public ConfigTables Tables;
			public HexCubePosition Camp;
			public HexCubePosition Shop;
			public string CampUid;
			public string ShopUid;

			/// <summary>开局：建营地 + 工坊（相邻），并注入人口 —— 训练用例不依赖 300 日的人口自然增长（那段由 `WP-2.3` 覆盖）。</summary>
			public void BuildCampAndWorkshop()
			{
				Resources.AddResource("Wood", 200f, MapId, OwnerId);
				Construction.StartConstruction(MapId, "camp", Camp, OwnerId);
				Construction.StartConstruction(MapId, "workshop", Shop, OwnerId);
				Clock.AdvanceDays(90); // 真实配置下 90 日完工（快配置下 ≥2 日即可）

				CampUid = Map.GetOccupantInfo(MapId, Camp).Value.UId;
				ShopUid = Map.GetOccupantInfo(MapId, Shop).Value.UId;
				Resources.AddResource("Gold", 500f, MapId, OwnerId);
				Map.AddPopulation(MapId, Camp, 1, 9, 5); // 5 人：够 5 个订单 / 或 2 人以上即可
			}
		}

		private static Harness NewHarness(int ownerId = 1, bool fast = false)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp25-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			CoreServices core = fast
				? ConfigFixtures.BuildCore(FastConfigSource())
				: ConfigFixtures.BuildRealCore();
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue }; // `D28`
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
			var units = new UnitsAppService(
				map,
				tech,
				resources,
				construction,
				time,
				core.Tables.Units,
				new UnitFactory(core.Tables.Units),
				fog,
				core.Tables.Buildings);

			map.GenerateMap(20260914, 8, 8, MapId);

			// 施工地块写死为平原（小图常只剩一个群系），并挑两个**相邻的内部格**（营地 / 工坊）
			MapCell camp = map.GetAllCells(MapId).First(c => map.IsClear(MapId, c.Position)
				&& c.Position.q is >= 3 and <= 4 && c.Position.r is >= 3 and <= 4);
			HexCubePosition shop = camp.Position.InRadius(1).First(pos => pos != camp.Position
				&& map.IsClear(MapId, pos) && pos.q is >= 3 and <= 4 && pos.r is >= 3 and <= 4);

			// 内部格全部铺平原：用例（比如第二座工坊选邻居格）不应被 Voronoi 水地形挡住
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				if (cell.Position.q is >= 2 and <= 6 && cell.Position.r is >= 2 and <= 6)
					map.SetTerrain(MapId, cell.Position, plain);

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
				Units = units,
				Fog = fog,
				Tables = core.Tables,
				Camp = camp.Position,
				Shop = shop,
			};
		}

		/// <summary>
		/// 快配置：把建筑/单位时长压到 2~3 日（其余字段与真实表一致），
		/// 让「队列串行 / 门控 / 任务键 / 扣人口」这类用例不必推进数百日。
		/// **时长口径本身**由 `M0-2 ④` 的用例在真实配置下验证（工坊 90 日 / 工人 30 日）。
		/// </summary>
		private static InMemoryConfigSource FastConfigSource()
		{
			string buildings = File.ReadAllText(ConfigFixtures.TablePath("Buildings"));
			Check.Assert(buildings.Contains("\"Duration\": 90"), "真实建筑表仍应是 90 日量级（本用例只是临时压缩）");

			string units = File.ReadAllText(ConfigFixtures.TablePath("Units"));
			Check.Assert(units.Contains("\"Duration\": 30"), "真实单位表里工人仍是 30 日");

			InMemoryConfigSource source = ConfigFixtures.RealConfigSourceWith("Buildings", buildings
				.Replace("\"Duration\": 90", "\"Duration\": 2")
				.Replace("\"Duration\": 240", "\"Duration\": 2")
				.Replace("\"Duration\": 60", "\"Duration\": 2"));
			source.Inject("Units", units
				.Replace("\"Duration\": 30", "\"Duration\": 3")
				.Replace("\"Duration\": 35", "\"Duration\": 3")
				.Replace("\"Duration\": 50", "\"Duration\": 3"));
			return source;
		}

		private static List<Unit> UnitsOf(Harness h)
			=> h.Map.GetAllCells(MapId)
				.Select(cell => cell.Occupant)
				.OfType<Unit>()
				.ToList();

		private static int UnitCount(Harness h) => UnitsOf(h).Count;

		private static Unit OnlyUnit(Harness h)
		{
			List<Unit> units = UnitsOf(h);
			Check.AssertEqual(1, units.Count, "应恰好有 1 个单位");
			return units[0];
		}

		private static List<TaskSnapshot> TrainingTasks(Harness h)
			=> h.Tasks.GetCurrentTasks(MapId).Where(t => t.Type == "Training").ToList();

		private static int TrainingTaskCount(Harness h) => TrainingTasks(h).Count;

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
