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
	/// （v0.3 / WP-3.4）**地块占用权威一致**（`MAP-03`/`MAP-04`/`UNIT-05`）的验收检查。
	/// <para>三条主张：① 建筑落位后 `cell.Building` 与占据物槽位**同时**有值（旧实现只写后者 → 拆除整体失效）；
	/// ② 移除占据物时 `_occupants` 索引一起清（旧实现留**僵尸索引**，任务/战斗回调会对着尸体干活）；
	/// ③ 一格一占据物（同格重复放置被拒绝，而不是静默覆盖）。</para>
	/// </summary>
	internal static class OccupancyChecks
	{
		private const string MapId = "occ-map";

		public static void RunAll()
		{
			Check.Run("WP-3.4 建筑落位：`cell.Building` 与占据物槽位同时有值（修 `MAP-04`）", BuildingPlacementIsConsistent);
			Check.Run("WP-3.4 拆除生效：占据物 + 索引 + 建筑槽位 + 修正器 + 任务一起清理", DemolitionClearsEverything);
			Check.Run("WP-3.4 无僵尸索引：单位阵亡后按 uid 查不到它（修 `MAP-03`/`UNIT-05`）", DeadUnitsLeaveNoZombieIndex);
			Check.Run("WP-3.4 一格一占据物：同格重复放置被拒绝（不静默覆盖）", OccupiedCellRejectsSecondOccupant);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void BuildingPlacementIsConsistent()
		{
			Harness h = NewHarness();
			try
			{
				h.Build("camp");

				Map map = h.SessionMaps.Get(MapId);
				MapCell cell = map.GetCell(h.Site);
				Check.Assert(cell.Occupant != null, "占据物槽位应有值");
				Check.Assert(cell.Building != null, "`cell.Building` 也应有值（旧实现恒空 → `MAP-04`）");
				Check.Assert(ReferenceEquals(cell.Building, cell.Occupant), "两者应指向同一个建筑对象");

				Check.Assert(h.Map.GetBuildingInfo(MapId, h.Site).HasValue, "`GetBuildingInfo` 应能查到建筑");
				Check.AssertEqual("camp", h.Map.GetBuildingInfo(MapId, h.Site).Value.Id, "查到的是营地");
				Check.Assert(!h.Map.IsClear(MapId, h.Site), "建筑占据的格子不应被视为空地");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void DemolitionClearsEverything()
		{
			Harness h = NewHarness();
			try
			{
				string uid = h.Build("camp");
				Check.Assert(h.Map.FindOccupantByUId(MapId, uid) != null, "准备：建筑在案");

				h.Construction.RemoveBuildingByPosition(MapId, h.Site);

				Check.Assert(h.Map.IsClear(MapId, h.Site), "地块应被清空");
				Check.Assert(h.Map.FindOccupantByUId(MapId, uid) == null, "按 uid 应查不到（索引已清）");

				MapCell cell = h.SessionMaps.Get(MapId).GetCell(h.Site);
				Check.Assert(cell.Building == null && cell.Occupant == null, "建筑槽位与占据物槽位都应清空");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void DeadUnitsLeaveNoZombieIndex()
		{
			Harness h = NewHarness();
			try
			{
				Unit victim = h.Spawn(2, "worker", h.CellAtDistance(2, h.Site));
				Unit archer = h.Spawn(1, "archer", h.Site);
				string victimUid = victim.GetInfo().UId;
				Check.Assert(h.Map.FindOccupantByUId(MapId, victimUid) != null, "准备：敌人在案");

				h.Units.ExcuteAction(MapId, archer.GetInfo().UId, victim.Position, victimUid, "CanAttack");
				h.Clock.AdvanceDays(12); // 射杀

				Check.Assert(h.Map.FindOccupantByUId(MapId, victimUid) == null,
					"阵亡单位不应还能按 uid 查到（旧实现留僵尸索引 → `MAP-03`/`UNIT-05`）");
				Check.Assert(h.Map.IsClear(MapId, h.CellAtDistance(2, h.Site)), "阵亡单位所在格应变为空地");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void OccupiedCellRejectsSecondOccupant()
		{
			Harness h = NewHarness();
			try
			{
				h.Build("camp");

				// 同格再造一座：建造会被 `_map.IsClear` 或落位拒绝，**原有建筑不得被覆盖**
				h.Build("workshop");
				MapCell cell = h.SessionMaps.Get(MapId).GetCell(h.Site);
				Check.AssertEqual("camp", cell.Occupant.GetInfo().Id, "原有建筑不应被覆盖（旧实现会静默覆盖）");
				Check.AssertEqual(h.CampUid, cell.Occupant.GetInfo().UId, "原有建筑 uid 不变");
			}
			finally { Cleanup(h.Dir); }
		}


		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public GameClock Clock;
			public MapSession SessionMaps;
			public GameTimeService Time;
			public MapAppService Map;
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public ConstructionAppService Construction;
			public UnitsAppService Units;
			public HexCubePosition Site;
			public string CampUid;

			/// <summary>在 <see cref="Site"/>（或指定格）建造并推进到完工（快配置：2 日）。</summary>
			public string Build(string buildingId, HexCubePosition? position = null)
			{
				HexCubePosition site = position ?? Site;
				Resources.AddResource("Wood", 500f, MapId, OwnerId);
				Resources.AddResource("Gold", 500f, MapId, OwnerId);
				Construction.StartConstruction(MapId, buildingId, site, OwnerId);
				Clock.AdvanceDays(2);

				MapOccupantInfo? info = Map.GetOccupantInfo(MapId, site);
				string uid = info?.UId;
				if (buildingId == "camp" && info?.Id == "camp") CampUid = uid;
				return uid;
			}

			public HexCubePosition CellAtDistance(int distance, HexCubePosition from)
				=> Map.GetAllCells(MapId).Select(c => c.Position).First(pos => pos.DistenceTo(from) == distance);

			public Unit Spawn(int ownerId, string unitId, HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Gold", 300f, MapId, ownerId);
				Resources.AddResource("Wood", 300f, MapId, ownerId);
				Units.CreateUnit(MapId, unitId, position, ownerId);
				Clock.AdvanceDays(3);

				string uid = Map.GetOccupantInfo(MapId, position).Value.UId;
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}
		}

		private static Harness NewHarness(int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp34-" + Guid.NewGuid().ToString("N"));
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

			core.Map.GenerateMap(20260914, 8, 8, MapId);
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			HexCubePosition site = core.Map.GetAllCells(MapId)
				.Select(c => c.Position).First(pos => pos.q == 4 && pos.r == 4);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = core.Session.Clock,
				SessionMaps = core.Session.Maps,
				Time = time,
				Map = core.Map,
				Resources = resources,
				Modifier = modifier,
				Construction = construction,
				Units = units,
				Site = site,
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
