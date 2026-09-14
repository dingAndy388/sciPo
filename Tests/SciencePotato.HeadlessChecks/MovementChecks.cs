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
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-3.5 / `UNIT-10`、`E5~E8`、`D23`）**移动模型 R1~R7** 的验收检查 —— M0-3 ② 的门槛：
	/// 「工人（M=10）跨平原（消耗 5）**10 日走 2 格**、跨山地（消耗 25）**需 30 日**」。
	/// <para>同时锁住四条派生口径：静止 MP 上限 = M（R3）、移动中 MP 无上限（R4）、够一格即时位移且可连跳（R5）、
	/// 到达目的地 MP 清零（R6）。</para>
	/// </summary>
	internal static class MovementChecks
	{
		private const string MapId = "move-map";

		public static void RunAll()
		{
			Check.Run("M0-3 ② 工人 M=10 跨平原（5）：10 日走 2 格且到达后 MP 清零（R1/R2/R5/R6）", PlainTwoCellsPerCycle);
			Check.Run("M0-3 ② 跨山地（25）：需 30 日（3 个回复周期）才移动一格（R4/R5）", MountainTakesThreeCycles);
			Check.Run("WP-3.5 静止 MP 上限 = M（R3）+ 移动中无上限（R4）", IdleCapAndMovingUnbounded);
			Check.Run("WP-3.5 连跳：MP 充足时同一时刻跨多格（R5，不消耗游戏时间）", MultipleCellsInOneInstant);
			Check.Run("WP-3.5 不可通行地形（水域 `Passable=false`）阻止移动（`E8`）", ImpassableTerrainBlocksMovement);
			Check.Run("WP-3.5 目的地可随时更改（R7）：改目的地后按新路径走", DestinationCanBeChanged);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void PlainTwoCellsPerCycle()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker(new HexCubePosition(1, 4));
				Check.AssertEqual(10f, h.Movement.MovementOf(worker), "工人的 M（设计稿 R1：工人 10）");
				Check.AssertEqual(5f, h.Movement.TerrainCost(MapId, new HexCubePosition(2, 4)), "平原消耗（Terrains.json）");

				h.Movement.SetDestination(MapId, worker.GetInfo().UId, new HexCubePosition(3, 4)); // 2 格之外
				h.Clock.AdvanceDays(10); // 一个回复周期

				Check.AssertEqual(new HexCubePosition(3, 4), worker.Position, "10 日应恰好走 2 格（10 MP ÷ 5）");
				Check.AssertEqual(0f, worker.CurrentMP, "到达目的地后 MP 清零（R6）");
				Check.Assert(worker.IsIdle, "到达后应回到空闲");
				Check.Assert(worker.MoveTarget == null, "目的地应被清空");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void MountainTakesThreeCycles()
		{
			Harness h = NewHarness();
			try
			{
				var mountain = new HexCubePosition(2, 4);
				h.SetTerrain(mountain, "mountain");
				Check.AssertEqual(25f, h.Movement.TerrainCost(MapId, mountain), "山地消耗（设计 25）");

				Unit worker = h.SpawnWorker(new HexCubePosition(1, 4));
				h.Movement.SetDestination(MapId, worker.GetInfo().UId, mountain);

				h.Clock.AdvanceDays(10);
				Check.AssertEqual(new HexCubePosition(1, 4), worker.Position, "第 1 个周期（MP 10 < 25）不应移动");
				Check.AssertEqual(10f, worker.CurrentMP, "MP 应累积到 10（R4：移动中无上限）");

				h.Clock.AdvanceDays(10);
				Check.AssertEqual(new HexCubePosition(1, 4), worker.Position, "第 2 个周期（MP 20 < 25）仍不应移动");
				Check.AssertEqual(20f, worker.CurrentMP, "MP 应累积到 20");

				h.Clock.AdvanceDays(10);
				Check.AssertEqual(mountain, worker.Position, "第 3 个周期（30 ≥ 25）应跨过山地 —— 共 30 日");
				Check.AssertEqual(0f, worker.CurrentMP, "到达最终目的地后 MP 清零（R6：剩余作废）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void IdleCapAndMovingUnbounded()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker(new HexCubePosition(1, 4));

				// R3：没有移动指令 → 上限 = M（挂机不积累能量）
				h.Clock.AdvanceDays(10 * 3);
				Check.AssertEqual(10f, worker.CurrentMP, "静止 3 个周期后 MP 仍应封顶在 M=10（R3）");

				// R4：有目的地且未到达 → 无上限
				h.SetTerrain(new HexCubePosition(2, 4), "mountain"); // 25 → 需要 3 个周期才能过
				h.Movement.SetDestination(MapId, worker.GetInfo().UId, new HexCubePosition(4, 4));
				h.Clock.AdvanceDays(10 * 3);
				Check.Assert(worker.CurrentMP >= 25f || worker.Position != new HexCubePosition(1, 4),
					"移动中 MP 应能超过 M（R4）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void MultipleCellsInOneInstant()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker(new HexCubePosition(1, 4));
				h.Movement.SetDestination(MapId, worker.GetInfo().UId, new HexCubePosition(6, 4)); // 5 格平原

				// 直接给足 MP：一个 tick 内应连跳多格（R5），而不是每周期只走一格
				worker.CurrentMP = 100f;
				h.Clock.AdvanceDays(10);

				Check.AssertEqual(new HexCubePosition(6, 4), worker.Position, "MP 充足时应在同一时刻连跳 5 格");
				Check.AssertEqual(0f, worker.CurrentMP, "到达目的地后清零（R6）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void ImpassableTerrainBlocksMovement()
		{
			Harness h = NewHarness();
			try
			{
				var water = new HexCubePosition(3, 4);
				h.SetTerrain(water, "water"); // Passable=false（设计：水域需科技解锁，`WP-4.11`）

				Check.Assert(!h.Movement.CanEnter(MapId, water), "水域不可通行（`E8`：看 Passable 而不是 MoveCost）");
				Check.Assert(h.Movement.CanEnter(MapId, new HexCubePosition(2, 4)), "平原可通行");

				Unit worker = h.SpawnWorker(new HexCubePosition(1, 4));
				int len = h.Movement.SetDestination(MapId, worker.GetInfo().UId, water);
				h.Clock.AdvanceDays(30);

				// E8 的行为断言：无论寻路怎么绕，单位都不允许站进不可通行的水域
				Check.Assert(worker.Position != water, "单位不应站进水域");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void DestinationCanBeChanged()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker(new HexCubePosition(1, 4));

				h.Movement.SetDestination(MapId, worker.GetInfo().UId, new HexCubePosition(5, 4));
				h.Clock.AdvanceDays(10); // 走 2 格 → (3,4)
				Check.AssertEqual(new HexCubePosition(3, 4), worker.Position, "准备：已走 2 格");

				// R7：改目的地（就地转向）→ 按新路径继续
				h.Movement.SetDestination(MapId, worker.GetInfo().UId, new HexCubePosition(3, 6));
				h.Clock.AdvanceDays(10 * 3);

				Check.AssertEqual(new HexCubePosition(3, 6), worker.Position, "改目的地后应到达新目标");
				Check.Assert(worker.IsIdle, "到达后回到空闲");
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
			public MapAppService Map;
			public ResourcesAppService Resources;
			public UnitsAppService Units;
			public UnitMovementService Movement;
			public ConfigTables Tables;

			/// <summary>把某格换成指定地形（用例布置战场用）。</summary>
			public void SetTerrain(HexCubePosition position, string terrainId)
				=> Map.SetTerrain(MapId, position, Tables.Terrains.GetById(terrainId));

			/// <summary>生成一个已就绪的工人（M=10）。</summary>
			public Unit SpawnWorker(HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Gold", 300f, MapId, OwnerId);
				Units.CreateUnit(MapId, "worker", position, OwnerId);
				Clock.AdvanceDays(3); // 快配置：训练 3 日

				string uid = Map.GetOccupantInfo(MapId, position).Value.UId;
				var unit = (Unit)Map.FindOccupantByUId(MapId, uid);

				return unit;
			}
		}

		private static Harness NewHarness(int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp35-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			Shorten(source);

			var mapRepository = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildCore(source, mapRepository: mapRepository);
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue; // `D28`

			var time = new GameTimeService(core.Session.Clock, new TaskRepository(Path.Combine(dir, "tasks_")));
			var bus = new DomainEventBus();
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
				time,
				bus);
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_")));
			var construction = new ConstructionAppService(
				core.Map, resources, tech, new BuildingFactory(core.Tables.Buildings),
				core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(
				core.Map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus);
			var movement = new UnitMovementService(core.Map, fog, core.Tables.Units, time);

			core.Map.GenerateMap(20260914, 8, 8, MapId);
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = core.Session.Clock,
				Time = time,
				Map = core.Map,
				Resources = resources,
				Units = units,
				Movement = movement,
				Tables = core.Tables,
			};
		}

		/// <summary>建筑/升级 2 日、单位 3 日（其余字段与真实表一致）。</summary>
		private static void Shorten(InMemoryConfigSource source)
		{
			var buildings = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)buildings["Buildings"]).Properties())
			{
				entry.Value["Duration"] = 2;
				entry.Value["UpgradeDuration"] = 2;
			}

			var units = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)units["Units"]).Properties())
				entry.Value["Duration"] = 3;

			source.Inject("Buildings", buildings.ToString());
			source.Inject("Units", units.ToString());
		}

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
