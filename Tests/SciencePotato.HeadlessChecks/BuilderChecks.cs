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
	/// （v0.3 / WP-2.4）**建造者校验**（设计缺口 `D6`）的验收检查：
	/// 「建筑必须由**建造者单位**建造、每建造者同时只建 1 座（每建筑同时仅 1 建造者）、可在**相邻格**建造」。
	/// <para>同时钉住两个旧实现的硬伤：建造只能"盖在自己脚下"（而该格被自己占着 → 永远失败）、
	/// 以及无论成败都把单位置为"忙"（永久卡死）。</para>
	/// </summary>
	internal static class BuilderChecks
	{
		private const string MapId = "build-map";

		public static void RunAll()
		{
			Check.Run("WP-2.4 能力门控：非建造者（民兵）不能下令建造（`D6`）", OnlyBuildersMayBuild);
			Check.Run("WP-2.4 位置：相邻空格可建、隔 2 格与脚下同格被拒（旧实现只能盖脚下 → 永远失败）", SiteMustBeAdjacentAndClear);
			Check.Run("WP-2.4 每建造者同时只建 1 座：施工中再下令被拒，完工后自动释放", BuilderBusyUntilCompletion);
			Check.Run("WP-2.4 无副作用：被拒的建造不产生建筑/任务/占用（资源维度受 `D25` 限制，见用例注释）", RejectedBuildHasNoSideEffects);
			Check.Run("WP-2.4 拆除释放：施工中的建筑被拆 → 建造者回到空闲（`D6` + `CON-06`）", DemolitionReleasesBuilder);
			Check.Run("WP-2.4 域名层：`Building.TryBindBuilder` 拒绝第二个建造者（每建筑同时仅 1）", BuildingRejectsSecondBuilder);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void OnlyBuildersMayBuild()
		{
			Harness h = NewHarness();
			try
			{
				Unit soldier = h.SpawnUnit("swordsman");
				Check.Assert(soldier.IsIdle, "准备：单位初始空闲");

				HexCubePosition site = h.ClearSiteNear(soldier);
				h.ExcuteBuild(soldier, site, "workshop");

				Check.Assert(h.Map.IsClear(MapId, site), "民兵不得开工建造（`Actions` 无 CanBuild）");
				Check.AssertEqual(0, ConstructionTaskCount(h), "不应产生建造任务");
				Check.Assert(soldier.IsIdle, "被拒后单位应仍为空闲（旧实现会把它置为忙）");

				// 工人（有 CanBuild）在同样的位置可以开工
				Unit worker = h.SpawnUnit("worker");
				HexCubePosition workerSite = h.ClearSiteNear(worker);
				h.ExcuteBuild(worker, workerSite, "workshop");
				Check.Assert(!h.Map.IsClear(MapId, workerSite), "工人应能开工建造");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void SiteMustBeAdjacentAndClear()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnUnit("worker");
				HexCubePosition site = h.ClearSiteNear(worker);

				// ① 隔 2 格：拒绝
				HexCubePosition far = new(worker.Position.q + 2, worker.Position.r);
				h.ExcuteBuild(worker, far, "workshop");
				Check.Assert(h.Map.IsClear(MapId, far), "远距离（>1 格）不得建造");
				Check.Assert(worker.IsIdle, "被拒后单位仍空闲");

				// ② 自己脚下（同格）：拒绝（格子被自己占着）
				h.ExcuteBuild(worker, worker.Position, "workshop");
				Check.AssertEqual(worker.GetInfo().UId, h.Map.GetOccupantInfo(MapId, worker.Position).Value.UId, "脚下仍是单位本身");
				Check.AssertEqual(0, ConstructionTaskCount(h), "同格建造不得产生任务");

				// ③ 相邻空格：成功
				h.ExcuteBuild(worker, site, "workshop");
				Check.Assert(!h.Map.IsClear(MapId, site), "相邻空格应开工成功");
				Check.AssertEqual(1, ConstructionTaskCount(h), "应产生 1 条建造任务");
				Check.Assert(!worker.IsIdle, "开工后建造者应处于忙状态");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void BuilderBusyUntilCompletion()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnUnit("worker");
				HexCubePosition site = h.ClearSiteNear(worker);
				HexCubePosition other = h.ClearSiteNear(worker, 1);

				h.ExcuteBuild(worker, site, "workshop");
				Check.Assert(!worker.IsIdle, "开工后忙碌");

				// 施工中再下令（另一块空地）：拒绝
				h.ExcuteBuild(worker, other, "camp");
				Check.Assert(h.Map.IsClear(MapId, other), "忙碌的建造者不得再接第二个工地（每建造者同时 1 座）");
				Check.AssertEqual(1, ConstructionTaskCount(h), "仍只有 1 条建造任务");

				// 完工 → 自动释放
				h.Clock.AdvanceDays(2); // 快配置：建筑 2 日完工
				Check.AssertEqual(0, ConstructionTaskCount(h), "完工后建造任务应被回收");
				Check.Assert(h.Map.GetOccupantInfo(MapId, site).Value.IsReady, "工坊应已完工");
				Check.Assert(worker.IsIdle, "完工应释放建造者（旧实现永久忙）");

				// 释放后可以再开工
				h.ExcuteBuild(worker, other, "camp");
				Check.Assert(!h.Map.IsClear(MapId, other), "释放后的建造者应能接新工地");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void RejectedBuildHasNoSideEffects()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnUnit("worker");
				HexCubePosition site = h.ClearSiteNear(worker);

				HexCubePosition far = new(worker.Position.q + 3, worker.Position.r);
				h.ExcuteBuild(worker, far, "workshop");             // 距离非法
				h.ExcuteBuild(worker, worker.Position, "workshop"); // 同格非法
				h.ExcuteBuild(worker, site, "no-such-building");    // 建筑 Id 非法

				// 注意：**不**用资源池断言"没扣钱" —— `D25`（消耗不落盘）使 `GetOrCreatePool` 每次都读回旧值，
				// 因此资源维度无法区分"扣了"与"没扣"。副作用改用「建筑 / 任务 / 单位状态」三个可观测面判定。
				Check.Assert(h.Map.IsClear(MapId, site), "被拒的建造不得留下建筑");
				Check.AssertEqual(0, ConstructionTaskCount(h), "被拒的建造不得产生任务");
				Check.Assert(worker.IsIdle, "被拒的建造不得占用建造者");

				// 合法建造：建筑与任务都应出现（且只剩 1 条 —— 前三次被拒没有留下任何东西）
				h.ExcuteBuild(worker, site, "workshop");
				Check.Assert(!h.Map.IsClear(MapId, site), "合法建造应产生建筑");
				Check.AssertEqual(1, ConstructionTaskCount(h), "合法建造应产生 1 条任务");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void DemolitionReleasesBuilder()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnUnit("worker");
				HexCubePosition site = h.ClearSiteNear(worker);
				h.ExcuteBuild(worker, site, "workshop");
				Check.Assert(!worker.IsIdle, "开工后忙碌");

				string uid = h.Map.GetOccupantInfo(MapId, site).Value.UId;

				// `MAP-04` 现状：建造只写 `cell.Occupant`、`cell.Building` 恒空 → 拆除需在 domain 层预置权威
				h.MapSession.Get(MapId).SetBuilding(site, h.Map.FindOccupantByUId(MapId, uid));
				h.Construction.RemoveBuildingByPosition(MapId, site);

				Check.Assert(worker.IsIdle, "拆除施工中的建筑应释放建造者");
				Check.AssertEqual(0, ConstructionTaskCount(h), "拆除应连同建造任务一起注销（`CON-06`）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void BuildingRejectsSecondBuilder()
		{
			var building = new Building(new HexCubePosition(0, 0), "camp", "uid-1", 1, "营地");

			var first = new BuilderBinding { BuilderUId = "worker-a", TargetPosition = new HexCubePosition(1, 0) };
			var second = new BuilderBinding { BuilderUId = "worker-b", TargetPosition = new HexCubePosition(2, 0) };

			Check.Assert(building.TryBindBuilder(first), "首个建造者应绑定成功");
			Check.Assert(!building.TryBindBuilder(second), "第二个建造者应被拒（每建筑同时仅 1 个）");
			Check.AssertEqual("worker-a", building.BuilderBinding.BuilderUId, "绑定对象应保持首个");

			bool released = false;
			first.OnRelease = () => released = true;
			building.ReleaseBuilder();
			Check.Assert(released, "释放应触发建造者的空闲回调");
			Check.Assert(building.BuilderBinding == null, "释放后不应再持有绑定");
			Check.Assert(building.TryBindBuilder(second), "释放后应能重新绑定");
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
			public HexCubePosition Spawn;
			public HexCubePosition Site;

			/// <summary>
			/// 生成一个已就绪的单位（走 `CreateUnit` 的"直接生成"入口；训练队列由 `WP-2.5` 覆盖）。
			/// 落点取内部区域里**第一个空格**，因此连续生成多个单位不会互相覆盖。
			/// </summary>
			public Unit SpawnUnit(string unitId)
			{
				MapCell cell = Map.GetAllCells(MapId).First(c => Map.IsClear(MapId, c.Position)
					&& c.Position.q is >= 3 and <= 5 && c.Position.r is >= 3 and <= 5);

				Map.AddPopulation(MapId, cell.Position, 0, 9, 1); // 生成消耗人口
				Units.CreateUnit(MapId, unitId, cell.Position, OwnerId);
				Clock.AdvanceDays(3);                             // 快配置：训练 3 日 → 就绪

				string uid = Map.GetOccupantInfo(MapId, cell.Position).Value.UId;
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}

			/// <summary>单位半径 1 内的候选工地格（空格 + 内部区域；序号 0 = 第一个）。</summary>
			public HexCubePosition ClearSiteNear(Unit unit, int index = 0)
				=> unit.Position.InRadius(1)
					.Where(pos => pos != unit.Position && Map.IsClear(MapId, pos) && pos.q is >= 2 and <= 6 && pos.r is >= 2 and <= 6)
					.ElementAt(index);

			/// <summary>走公开入口下建造令（单位动作协议：`CanBuild` + 目标格 + 建筑 Id）。</summary>
			public void ExcuteBuild(Unit unit, HexCubePosition position, string buildingId)
				=> Units.ExcuteAction(MapId, unit.GetInfo().UId, position, buildingId, "CanBuild");

			public float Wood() => Resources.GetOrCreatePool(MapId, OwnerId).GetValue("Wood");
		}

		private static Harness NewHarness(int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp24-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			CoreServices core = ConfigFixtures.BuildCore(FastConfigSource()); // 建筑 2 日 / 单位 3 日
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

			var session = new MapSession(new InMemoryMapRepository());
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

			// 内部格铺平原 + 资源到位
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				if (cell.Position.q is >= 2 and <= 6 && cell.Position.r is >= 2 and <= 6)
					map.SetTerrain(MapId, cell.Position, plain);

			MapCell spawnCell = map.GetAllCells(MapId).First(c => map.IsClear(MapId, c.Position)
				&& c.Position.q == 4 && c.Position.r == 4);
			HexCubePosition site = spawnCell.Position.InRadius(1)
				.First(pos => pos != spawnCell.Position && map.IsClear(MapId, pos) && pos.q is >= 2 and <= 6);

			resources.AddResource("Wood", 300f, MapId, ownerId);
			resources.AddResource("Gold", 300f, MapId, ownerId);

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
				Spawn = spawnCell.Position,
				Site = site,
			};
		}

		/// <summary>快配置：建筑 2 日 / 单位 3 日（其余字段与真实表一致），本用例只关心建造者门控。</summary>
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

		private static int ConstructionTaskCount(Harness h)
			=> h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Construction");

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

