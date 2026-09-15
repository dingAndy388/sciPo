using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.8.8 / B3）**地图与建筑结构**的验收检查：`WP-4.9` 区域/附属建筑 · `WP-4.16` 建筑前置 ·
	/// `WP-4.17` 人口模型（聚落级容量 / 拆房减员）· `WP-4.11` 地形通行解锁。
	/// </summary>
	internal static class StructureMechanicChecks
	{
		private const string MapId = "structure-mechanics";

		public static void RunAll()
		{
			Check.Run("WP-4.16 建筑前置 + WP-4.9 附属建筑：日晷只能建在学院格上（无学院/非学院格被拒）", AttachmentNeedsHost);
			Check.Run("WP-4.9 区域升级不动附属；宿主被拆 ⇒ 附属一并消失（不留僵尸索引）", AttachmentSurvivesUpgradeButNotHostRemoval);
			Check.Run("WP-4.9 建筑两格内无迷雾（完工即揭开 ≥2 格）", BuildingsRevealTwoCells);
			Check.Run("WP-4.17 聚落级容量：相邻住房不叠加；拆掉唯一住房 ⇒ 该区域人口减员", SettlementCapacityDoesNotStack);
			Check.Run("WP-4.11 地形通行解锁：水域默认不可进；`Passable:water` 解锁且 `TerrainCost:water` 改写成本", TerrainUnlockByModifier);
		}

		private static (CoreServices Core, MapAppService Map) NewField(int seed = 20261300)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Map.GenerateMap(seed, 20, 20, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			return (core, core.Map);
		}

		private static Unit PlaceWorker(CoreServices core, HexCubePosition position)
		{
			string uid = core.Units.PlaceInitialUnit(MapId, "worker", position, 1);
			Check.Assert(uid != null, "工人应能落位");
			return (Unit)core.Map.FindOccupantByUId(MapId, uid);
		}

		private static Building PlaceBuilding(CoreServices core, string buildingId, HexCubePosition position, bool ready = true)
		{
			Building building = new BuildingFactory(core.Tables.Buildings).CreateBuilding(buildingId, position, 1);
			building.IsReady = ready;
			Check.Assert(core.Map.PlaceBuilding(MapId, position, building), $"{buildingId}@{position} 应能落位");
			return building;
		}

		private static BuilderBinding Bind(Unit worker) => new() { BuilderUId = worker.GetInfo().UId, TargetPosition = worker.Position };

		private static void AttachmentNeedsHost()
		{
			(CoreServices core, MapAppService map) = NewField(20261301);
			Unit worker = PlaceWorker(core, new HexCubePosition(4, 4));
			var schoolCell = new HexCubePosition(5, 5);

			// ① 没有学院 ⇒ 建筑前置不满足
			Check.Assert(!core.Construction.StartConstruction(MapId, "sundial", schoolCell, 1, Bind(worker)),
				"没有学院时不得建日晷（建筑前置）");

			// ② 有学院、但目标格不是学院 ⇒ 拒绝
			Building school = PlaceBuilding(core, "school", schoolCell);
			Check.Assert(!core.Construction.StartConstruction(MapId, "sundial", new HexCubePosition(7, 7), 1, Bind(worker)),
				"日晷只能建在学院所在格");

			// ③ 建在学院格 ⇒ 成功，且**不动**宿主：cell.Building 仍是学院
			Check.Assert(core.Construction.StartConstruction(MapId, "sundial", schoolCell, 1, Bind(worker)),
				"有学院且落在学院格 ⇒ 应能开工");
			core.Session.Clock.AdvanceDays(30);   // 日晷 30 日完工

			MapCell cell = map.GetMapCell(MapId, schoolCell);
			Check.AssertEqual("school", cell.Building.GetInfo().Id, "宿主仍在 cell.Building 上（附属不顶替宿主）");
			Check.AssertEqual(1, cell.Attachments.Count, "附属建筑应挂在本格的 Attachments 上");

			IMapOccupant sundial = cell.Attachments[0];
			Check.AssertEqual("sundial", sundial.GetInfo().Id, "附属建筑应是日晷");
			Check.AssertEqual(school.GetInfo().UId, ((Building)sundial).HostUId, "HostUId 应指向宿主");
			Check.Assert(map.FindOccupantByUId(MapId, sundial.GetInfo().UId) != null, "附属建筑应能被 uid 查到（产出/迷雾/AI 都算它）");
			Check.AssertEqual(2, map.GetOccupants(MapId).Count(o => o.GetInfo().Type == OccupantType.Building && o.GetInfo().OwnerId == 1),
				"`GetOccupants` 应同时看到宿主与附属（两栋建筑）");
		}

		private static void AttachmentSurvivesUpgradeButNotHostRemoval()
		{
			(CoreServices core, MapAppService map) = NewField(20261302);
			Unit worker = PlaceWorker(core, new HexCubePosition(4, 4));
			var schoolCell = new HexCubePosition(5, 5);
			Building school = PlaceBuilding(core, "school", schoolCell);

			Check.Assert(core.Construction.StartConstruction(MapId, "sundial", schoolCell, 1, Bind(worker)), "准备：日晷应能开工");
			core.Session.Clock.AdvanceDays(30);
			IMapOccupant sundial = map.GetMapCell(MapId, schoolCell).Attachments[0];

			// 学院升级（就地改 Id）⇒ 附属不受影响（设计稿：区域建筑升级，附属建筑不会消失）
			school.ApplyUpgrade("school_ii", "学院 lv.II");
			Check.AssertEqual(1, map.GetMapCell(MapId, schoolCell).Attachments.Count, "学院升级后附属仍在");
			Check.Assert(map.FindOccupantByUId(MapId, sundial.GetInfo().UId) != null, "升级后附属的 uid 仍可查");

			// 拆宿主 ⇒ 附属一并消失（无僵尸索引）
			core.Construction.RemoveBuildingByPosition(MapId, schoolCell);
			Check.AssertEqual(0, map.GetMapCell(MapId, schoolCell).Attachments.Count, "宿主被拆后附属应一并清掉");
			Check.Assert(map.FindOccupantByUId(MapId, sundial.GetInfo().UId) == null, "附属的 uid 不应再被查到");
		}

		private static void BuildingsRevealTwoCells()
		{
			(CoreServices core, MapAppService map) = NewField(20261303);
			var schoolCell = new HexCubePosition(5, 5);
			PlaceBuilding(core, "school", schoolCell);

			// 完工回调里 `RevealArea` 用的是 `max(2, VisionRadius)`（设计稿"建筑两格内无迷雾"）
			core.Construction.StartConstruction(MapId, "camp", new HexCubePosition(8, 8), 1, Bind(PlaceWorker(core, new HexCubePosition(7, 8))));
			core.Session.Clock.AdvanceDays(90);

			Check.Assert(core.Fog.GetVisibility(new HexCubePosition(7, 7)) > 0.5f, "距离 2 格（对角）的格子不应还有迷雾");
		}

		private static void SettlementCapacityDoesNotStack()
		{
			(CoreServices core, MapAppService map) = NewField(20261304);
			var campCell = new HexCubePosition(5, 5);
			PlaceBuilding(core, "camp", campCell);
			PopulationModelService model = core.Population;

			int single = model.CapacityAt(MapId, 1, campCell);
			Check.Assert(single > 0, $"准备：营地应给出正容量（实际 {single}）");

			// 相邻再放一座营地：设计稿口径"多住房不叠加" ⇒ 容量不变
			PlaceBuilding(core, "camp", new HexCubePosition(6, 5));
			Check.AssertEqual(single, model.CapacityAt(MapId, 1, campCell), "相邻住房不应叠加容量（取覆盖该点的最大上限）");

			// 人口填到上限，再拆掉唯一的住房 ⇒ 人口被压到 0（减员）
			core.Map.AddPopulation(MapId, campCell, 1, 999, single);
			Check.AssertEqual(single, map.GetPopulationWithin(MapId, campCell, 1), "准备：人口已达到上限");

			core.Map.RemoveBuilding(MapId, campCell);
			core.Map.RemoveBuilding(MapId, new HexCubePosition(6, 5));
			int trimmed = model.TrimAfterHousingLost(MapId, 1, campCell, 1);
			Check.AssertEqual(single, trimmed, $"没有住房覆盖这片地 ⇒ 应减员到容量 0（实际减 {trimmed}）");
			Check.AssertEqual(0, map.GetPopulationWithin(MapId, campCell, 1), "人口应被压到 0");
			Check.Assert(model.TrimCount > 0, "减员应记账");
		}

		private static void TerrainUnlockByModifier()
		{
			(CoreServices core, MapAppService map) = NewField(20261305);
			var waterCell = new HexCubePosition(9, 9);
			map.SetTerrain(MapId, waterCell, core.Tables.Terrains.GetById("water"));

			Check.Assert(!core.Units.Movement.CanEnter(MapId, 1, waterCell), "水域默认不可进入（Terrains.Passable=false）");
			Check.Assert(!core.Units.Movement.IsTerrainUnlocked(MapId, 1, "water"), "准备：玩家尚未解锁水域");

			// 科技"浮力定律"：解锁水域通行（移动消耗 5.0）
			core.Modifiers.AddModifiers(MapId, 1, "physics_buoyancy", new List<Modifier>
			{
				new Modifier { Target = "Passable:water", Type = "Absolute", Value = 1f },
				new Modifier { Target = "TerrainCost:water", Type = "Absolute", Value = 5f },
			}, ModifierStage.Tech);

			Check.Assert(core.Units.Movement.IsTerrainUnlocked(MapId, 1, "water"), "`Passable:water` 应解锁该地形");
			Check.Assert(core.Units.Movement.CanEnter(MapId, 1, waterCell), "解锁后水域应可进入");
			Check.AssertEqual(5f, core.Units.Movement.TerrainCost(MapId, 1, waterCell), "`TerrainCost:water` 应改写移动成本为 5.0");

			// 跨 owner 隔离：AI（2）没有该科技 ⇒ 仍不可进入
			Check.Assert(!core.Units.Movement.CanEnter(MapId, 2, waterCell), "AI 未解锁 ⇒ 仍不可进入（按 owner 隔离）");
			Check.AssertEqual(core.Tables.Terrains.GetById("water").MoveCost, core.Units.Movement.TerrainCost(MapId, 2, waterCell), "AI 侧成本仍是地形表里的值（未被科技改写）");
		}
	}
}
