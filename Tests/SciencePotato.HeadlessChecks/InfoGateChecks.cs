using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Units.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.8.9 / B4）**信息与门控**的验收检查：`WP-4.10` 迷雾按 owner · `WP-4.13` 产出归因 ·
	/// `WP-4.14` UI 门控 · `WP-4.15` 建造者绑定落盘。
	/// </summary>
	internal static class InfoGateChecks
	{
		private const string MapId = "info-gates";

		public static void RunAll()
		{
			Check.Run("WP-4.10 迷雾按 owner：AI 与玩家各看各的；离开视野只降到迷雾（不回退未探索）", FogIsPerOwner);
			Check.Run("WP-4.13 产出归因：拆出每条来源并判定 `SourceId` 语义（建筑/科技）", AttributionListsSources);
			Check.Run("WP-4.14 UI 门控：科技 `UnlocksUi` 决定面板/研究功能是否可用", UiGatesFollowTech);
			Check.Run("WP-4.15 建造者绑定落盘：存档往返后仍绑着同一个工人", BuilderBindingSurvivesSave);
		}

		private static (CoreServices Core, MapAppService Map) NewField(int seed = 20261400)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(seed, 20, 20, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			return (core, core.Map);
		}

		private static void FogIsPerOwner()
		{
			(CoreServices core, _) = NewField(20261401);
			var humanCell = new HexCubePosition(5, 5);
			var aiCell = new HexCubePosition(12, 12);

			core.Fog.RevealArea(1, humanCell, 2);   // 玩家揭一片
			core.Fog.RevealArea(2, aiCell, 2);      // AI 自己揭另一片

			Check.Assert(core.Fog.GetVisibility(1, humanCell) == FogAppService.Visible, "玩家应看得见自己揭开的格子");
			Check.Assert(core.Fog.GetVisibility(2, humanCell) == FogAppService.Unexplored, "AI 不应看见玩家揭开的格子（按 owner 隔离）");
			Check.Assert(core.Fog.GetVisibility(2, aiCell) == FogAppService.Visible, "AI 应看得见自己揭开的格子");
			Check.Assert(core.Fog.GetVisibility(1, aiCell) == FogAppService.Unexplored, "玩家不应看见 AI 揭开的格子");
			Check.Assert(core.Fog.LoadedOwners.Contains(1) && core.Fog.LoadedOwners.Contains(2), "两个势力都应有迷雾状态");

			// 永久清除语义：离开视野 ⇒ 迷雾（不是未探索）；旧的无 owner 签名仍指向人类玩家
			core.Fog.ResetArea(1, humanCell, 2);
			Check.Assert(core.Fog.GetVisibility(1, humanCell) == FogAppService.Fogged, "离开视野应降级为迷雾（探索过的地不再变回未知）");
			Check.Assert(core.Fog.GetVisibility(humanCell) == core.Fog.GetVisibility(1, humanCell), "无 owner 的旧签名应等同人类玩家（兼容层）");
		}
		private static void AttributionListsSources()
		{
			(CoreServices core, _) = NewField(20261402);
			IResourceConfig food = core.Tables.AllResources().Single(r => r.Name == "Food");

			Building farm = new SciencePotato.Scripts.Construction.Domain.BuildingFactory(core.Tables.Buildings)
				.CreateBuilding("farm", new HexCubePosition(5, 5), 1);
			farm.IsReady = true;
			core.Map.PlaceBuilding(MapId, farm.GetInfo().Position, farm);
			core.Modifiers.AddModifiers(MapId, 1, farm.GetInfo().UId, core.Tables.Buildings.GetBuildingConfig("farm").Modifiers);
			core.Modifiers.AddModifiers(MapId, 1, "counting", new List<Modifier>
			{
				new Modifier { Target = "FoodGrowth", Type = "Percent", Value = 0.1f },
			}, ModifierStage.Tech);

			IReadOnlyList<ProductionAttribution> rows = core.Attribution.Attribute(MapId, 1, food);
			Check.Assert(rows.Count >= 2, $"应拆出至少两条来源（实际 {rows.Count}）");

			ProductionAttribution building = rows.First(r => r.SourceId == farm.GetInfo().UId);
			Check.AssertEqual("building", building.Kind, "农田的 SourceId 应判为建筑");
			Check.AssertEqual(12f, building.Absolute, "农田应贡献 +12 食物/月（Absolute）");

			ProductionAttribution tech = rows.First(r => r.SourceId == "counting");
			Check.AssertEqual("tech", tech.Kind, "科技节点 Id 应判为科技（`D115` 语义规范）");
			Check.AssertEqual(0.1f, tech.Percent, "该科技应贡献 +10%");

			Check.AssertEqual("base", core.Attribution.KindOf(MapId, null), "空 SourceId 应判为 base");
			Check.AssertEqual("other", core.Attribution.KindOf(MapId, "some_event_id"), "既不是建筑也不是科技的 Id 归 other");
		}

		private static void UiGatesFollowTech()
		{
			(CoreServices core, _) = NewField(20261403);

			Check.Assert(!core.UiGate.IsResearchUnlocked(MapId, 1), "准备：尚未研究「计数」⇒ 研究功能应关闭");
			Check.Assert(!core.UiGate.IsResourcePanelUnlocked(MapId, 1), "准备：资源面板应关闭");
			Check.Assert(!core.UiGate.IsHarvestPanelUnlocked(MapId, 1), "准备：收获面板应关闭（需「算术」）");

			core.Tech.Research(MapId, 1, "science", "counting");
			core.Session.Clock.AdvanceDays(1); // 计数 0 日 ⇒ 次日完成
			Check.Assert(core.UiGate.IsResearchUnlocked(MapId, 1), "研究「计数」后应开启研究功能");
			Check.Assert(core.UiGate.IsResourcePanelUnlocked(MapId, 1), "研究「计数」后应开启资源面板");
			Check.Assert(!core.UiGate.IsHarvestPanelUnlocked(MapId, 1), "收获面板要等「算术」（当前占位表里挂在 writing 上）");

			core.Tech.Research(MapId, 1, "science", "writing");
			core.Session.Clock.AdvanceDays(15); // writing 15 日
			Check.Assert(core.UiGate.IsHarvestPanelUnlocked(MapId, 1), "研究占位节点后收获面板应开启");

			Check.Assert(!core.UiGate.IsResearchUnlocked(MapId, 2), "AI 未研究 ⇒ 其研究功能仍关闭（按 owner 隔离）");
		}

		private static void BuilderBindingSurvivesSave()
		{
			(CoreServices core, MapAppService map) = NewField(20261404);
			string workerUid = core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(4, 4), 1);
			Check.Assert(workerUid != null, "准备：工人应能落位");
			var site = new HexCubePosition(5, 5);

			Check.Assert(core.Construction.StartConstruction(MapId, "camp", site, 1,
				new SciencePotato.Scripts.Construction.Domain.BuilderBinding { BuilderUId = workerUid, TargetPosition = site }),
				"准备：应能开工");

			core.WorldSave.SaveWorld(MapId, 1);
			Check.Assert(core.WorldSave.LoadWorld(MapId, 1), "读档应成功");

			var building = map.FindOccupantByUId(MapId, map.GetMapCell(MapId, site)?.Building?.GetInfo().UId)
				as SciencePotato.Scripts.Construction.Domain.Building;
			Check.Assert(building != null, "读档后建筑应还在");
			Check.AssertEqual(workerUid, building.BuilderBinding?.BuilderUId, "读档后应仍绑着同一个工人（`WP-4.15`）");

			var worker = map.FindOccupantByUId(MapId, workerUid) as SciencePotato.Scripts.Units.Domain.Unit;
			Check.Assert(worker != null, "读档后工人应还在");
			Check.Assert(!worker.IsIdle, "读档后工人应仍是忙的（绑定被恢复，不是只写进存档）");
		}
	}
}
