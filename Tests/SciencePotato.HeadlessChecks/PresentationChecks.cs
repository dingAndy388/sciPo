using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Intent.Domain;
using SciencePotato.Scripts.Intent.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Presentation;
using SciencePotato.Scripts.TechTree.Application;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.9.7 / `WP-5.4`）**正式表现层的可验证内核**（`I2`）：图层口径 + 交互口径。
	/// <para>表现层的 Godot 节点本身没有单测（渲染无法无头断言），所以把两件"会出错的事"抽成纯 C#：
	/// ① **哪些格子该出现在哪些图层**（`MapLayerModel`）；② **点击 → 意图**（`MapInteractionModel`）。
	/// Godot 侧只剩"把点击坐标转格位 + 按模型挂节点 + 把键交给 i18n"，从而表现层不可能绕过规则（`CON-02`/`CON-08`）。</para>
	/// </summary>
	internal static class PresentationChecks
	{
		private const string MapId = "ui-map";

		public static void RunAll()
		{
			Check.Run("WP-5.4 图层模型：未探索格不出现在任何层；有占据物的格子出现在对应层", LayerMembershipFollowsOccupant);
			Check.Run("WP-5.4 图层模型：雾层不透明度三档（可见 1 / 迷雾 0.55 / 未探索 0）", FogOpacityIsThreeTier);
			Check.Run("WP-5.4 交互·建造：走意图（成功即退出建造模式；占用格给 `intent.rejected`）", BuildModeGoesThroughIntent);
			Check.Run("WP-5.4 交互·点选：返回该格信息（地形 / 占据物 / 人口）", SelectReturnsCellInfo);
			Check.Run("WP-5.4 交互·单位指令：属己可动（真进入移动态），越权给 `intent.not_human`", MoveModeGuardsOwnership);
			Check.Run("WP-5.4 交互·事件与门控：弹窗决策走意图、门控查询随科技解锁变化", EventDecisionAndUiGate);
		}

		private static void LayerMembershipFollowsOccupant()
		{
			CoreServices core = NewField();

			// ① 未探索：表现层不该画出这一格
			var far = new HexCubePosition(18, 18);
			Check.AssertEqual(0, MapLayerModel.LayersOf(core.Map, core.Fog, MapId, 1, far).Count,
				"未探索的格子不应出现在任何图层（否则玩家能看见没去过的地方）");

			// ② 揭开视野：出现地形层 + 雾层，但没有占据物层
			core.Fog.RevealArea(1, far, 2);
			var revealed = MapLayerModel.LayersOf(core.Map, core.Fog, MapId, 1, far).ToList();
			Check.Assert(revealed.Contains(MapLayerModel.Terrain), "已探索格应有地形层");
			Check.Assert(revealed.Contains(MapLayerModel.Fog), "已探索格应有雾层");
			Check.Assert(!revealed.Contains(MapLayerModel.Buildings), "空格不应有建筑层");

			// ③ 建一栋营地 ⇒ 该格出现在建筑层（先揭开，否则未探索格按设计不出现在任何层）
			var site = new HexCubePosition(6, 6);
			core.Fog.RevealArea(1, site, 2);
			Check.Assert(core.Intent.Handle(PlayerIntent.Build(MapId, 1, "camp", site)).Ok, "前置：应能建造营地");
			Check.Assert(MapLayerModel.LayersOf(core.Map, core.Fog, MapId, 1, site).Contains(MapLayerModel.Buildings),
				"有建筑的格子应出现在建筑层");

			// ④ 放一个单位 ⇒ 该格出现在单位层
			var unitCell = new HexCubePosition(8, 8);
			core.Fog.RevealArea(1, unitCell, 2);
			string uid = core.Units.PlaceInitialUnit(MapId, "worker", unitCell, 1);
			Check.Assert(!string.IsNullOrEmpty(uid), "前置：应能放置单位");
			Check.Assert(MapLayerModel.LayersOf(core.Map, core.Fog, MapId, 1, unitCell).Contains(MapLayerModel.Units),
				"有单位的格子应出现在单位层");
		}

		private static void FogOpacityIsThreeTier()
		{
			Check.AssertEqual(1f, MapLayerModel.FogOpacity(FogAppService.Visible), "可见 = 完全不透明");
			Check.AssertEqual(0.55f, MapLayerModel.FogOpacity(FogAppService.Fogged), "迷雾 = 半透明（看得出探索过）");
			Check.AssertEqual(0f, MapLayerModel.FogOpacity(FogAppService.Unexplored), "未探索 = 不画");
		}

		private static void BuildModeGoesThroughIntent()
		{
			CoreServices core = NewField();
			var model = new MapInteractionModel(MapId, 1, core.Map, core.Intent, core.Events, core.UiGate);

			var site = new HexCubePosition(7, 7);
			model.SetBuildMode("camp");
			Check.AssertEqual(InteractionMode.Build, model.Mode, "应进入建造模式");

			InteractionFeedback built = model.Click(site);
			Check.Assert(built.Ok, $"选址建造应成功：{built}");
			Check.AssertEqual(InteractionMode.Select, model.Mode, "开工后应退出建造模式（避免连点误建）");
			Check.AssertEqual("camp", core.Map.GetOccupantInfo(MapId, site).Value.Id, "格子上的占位应是营地");

			// 同一格再建：规则层拒绝，且给出可翻译的键
			model.SetBuildMode("camp");
			InteractionFeedback again = model.Click(site);
			Check.Assert(!again.Ok, "占用格再建应被拒");
			Check.AssertEqual(IntentKeys.Rejected, again.MessageKey, "占用格的拒绝键应是 `intent.rejected`");
			Check.AssertEqual(InteractionMode.Build, model.Mode, "被拒后应留在建造模式（便于换个格子再点）");
		}

		private static void SelectReturnsCellInfo()
		{
			CoreServices core = NewField();
			var model = new MapInteractionModel(MapId, 1, core.Map, core.Intent, core.Events, core.UiGate);

			string uid = core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(9, 9), 1);
			InteractionFeedback info = model.Click(new HexCubePosition(9, 9));

			Check.Assert(info.Ok, "点选应总是成功（只读）");
			Check.Assert(info.Detail != null && info.Detail.Contains("plain"), $"应报出地形：{info.Detail}");
			Check.Assert(info.Detail != null && info.Detail.Contains(uid), $"应报出占据物 uid：{info.Detail}");
			Check.AssertEqual(InteractionMode.Select, model.Mode, "点选不改变模式");
		}

		private static void MoveModeGuardsOwnership()
		{
			CoreServices core = NewField();
			var model = new MapInteractionModel(MapId, 1, core.Map, core.Intent, core.Events, core.UiGate);

			string own = core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(4, 4), 1);
			string ai = core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(10, 10), 2);

			Check.Assert(model.SetMoveMode(own, out _), "自己的单位应能进入指令模式");
			InteractionFeedback move = model.Click(new HexCubePosition(5, 4));
			Check.Assert(move.Ok, $"移动应成功：{move}");
			Check.AssertEqual(InteractionMode.Select, model.Mode, "下令后应退出指令模式");
			Check.Assert(SciencePotato.Scripts.Units.Application.UnitMovementService.IsMoving(
				(SciencePotato.Scripts.Units.Domain.Unit)core.Map.FindOccupantByUId(MapId, own)), "单位应真的进入移动态");

			Check.Assert(!model.SetMoveMode(ai, out string reason), "不能指挥 AI 的单位");
			Check.AssertEqual(IntentKeys.NotHuman, reason, "越权的拒绝键");
		}

		private static void EventDecisionAndUiGate()
		{
			CoreServices core = NewField();
			var model = new MapInteractionModel(MapId, 1, core.Map, core.Intent, core.Events, core.UiGate);

			Check.AssertEqual(0, model.PendingEvents().Count, "尚未触发任何事件 ⇒ 弹窗队列应为空");
			InteractionFeedback bad = model.ConfirmEvent("no_such_event");
			Check.Assert(!bad.Ok, "不存在的事件决策应被拒");
			Check.AssertEqual(IntentKeys.Rejected, bad.MessageKey, "事件决策被拒的键");

			// 门控：收获面板要等「算术」；面板灰化的判据来自科技表（表现层不自己判断）
			Check.Assert(!model.IsUiUnlocked(SciencePotato.Scripts.Common.Application.UiGateService.HarvestPanel),
				"未研究算术 ⇒ 收获面板应关闭（UI 应灰化）");

			core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "counting"));
			core.Session.Clock.AdvanceDays(1);
			core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "arithmetic"));
			core.Session.Clock.AdvanceDays(20);

			Check.Assert(model.IsUiUnlocked(SciencePotato.Scripts.Common.Application.UiGateService.HarvestPanel),
				"研究算术后面板应解锁（UI 应亮起）");
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private static CoreServices NewField(int seed = 20261900)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(seed, 20, 20, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			foreach (int owner in new[] { 1, 2 })
			{
				core.Resources.AddResource("Idea", 50000f, MapId, owner);
				core.Resources.AddResource("BasicMinerals", 50000f, MapId, owner);
				core.Resources.AddResource("Food", 50000f, MapId, owner);
			}
			return core;
		}
	}
}
