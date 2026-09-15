using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Intent.Application;
using SciencePotato.Scripts.Intent.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Presentation
{
	/// <summary>（v0.9.7 / `WP-5.4`）交互模式：点选 / 选址建造 / 单位指令。</summary>
	public enum InteractionMode
	{
		Select = 0,
		Build = 1,
		MoveUnit = 2,
	}

	/// <summary>（v0.9.7 / `WP-5.4`）一次点击的结果：成/败 + **i18n 键** + 可读细节（调试/用例断言用）。</summary>
	public sealed class InteractionFeedback
	{
		public bool Ok { get; init; }

		/// <summary>失败原因键（成功为 <c>null</c>）；取值见 `IntentKeys`。</summary>
		public string MessageKey { get; init; }

		/// <summary>细节（选中的地形/占据物描述、拒绝的说明）。**不是文案**，不参与 i18n。</summary>
		public string Detail { get; init; }

		public override string ToString() => Ok ? $"ok({Detail})" : $"rejected({MessageKey}: {Detail})";
	}

	/// <summary>
	/// （v0.9.7 / `WP-5.4`）**地图交互模型**（`I2` 后半段）：把"玩家点了哪一格"翻译成 `PlayerIntent`，
	/// 其余一律交给 `WP-5.7` 的 <see cref="IActionHandler"/>。
	/// <list type="bullet">
	/// <item>表现层**不碰应用服务**：写操作全走意图（`CON-02`/`CON-08` 收口），本类只额外持有**查询**服务（事件队列 / UI 门控）；</item>
	/// <item>纯 C#（无 Godot 依赖）⇒ 无头用例能锁住"选址建造""点选看信息""单位指令""事件决策""UI 门控"的完整口径；</item>
	/// <item>Godot 侧只做两件事：把点击坐标转成格位、把 <see cref="InteractionFeedback.MessageKey"/> 交给 i18n 显示。</item>
	/// </list>
	/// </summary>
	public sealed class MapInteractionModel
	{
		private readonly MapAppService _map;
		private readonly IActionHandler _intent;
		private readonly EventAppService _events;
		private readonly UiGateService _uiGate;

		public MapInteractionModel(string mapId, int ownerId, MapAppService map, IActionHandler intent,
			EventAppService events = null, UiGateService uiGate = null)
		{
			MapId = mapId;
			OwnerId = ownerId;
			_map = map;
			_intent = intent;
			_events = events;
			_uiGate = uiGate;
		}

		public string MapId { get; set; }

		public int OwnerId { get; }

		public InteractionMode Mode { get; private set; } = InteractionMode.Select;

		/// <summary>`Build` 模式下待建的建筑 Id（`null` = 未选）。</summary>
		public string PendingBuildingId { get; private set; }

		/// <summary>`MoveUnit` 模式下待指挥的单位 uid（`null` = 未选）。</summary>
		public string PendingUnitUid { get; private set; }

		// ────────────────────────── 模式 ──────────────────────────

		/// <summary>切换到"选址建造"：只记意图，真正开工发生在点击时（点了才有位置）。</summary>
		public void SetBuildMode(string buildingId)
		{
			Mode = InteractionMode.Build;
			PendingBuildingId = buildingId;
			PendingUnitUid = null;
		}

		/// <summary>切换到"单位指令"：校验单位存在且属己（否则返回 false 并给原因键）。</summary>
		public bool SetMoveMode(string unitUid, out string reasonKey)
		{
			reasonKey = null;
			if (string.IsNullOrWhiteSpace(unitUid)) { reasonKey = IntentKeys.NoTarget; return false; }

			var occupant = _map?.FindOccupantByUId(MapId, unitUid);
			if (occupant == null) { reasonKey = IntentKeys.NoUnit; return false; }
			if (occupant.GetInfo().OwnerId != OwnerId) { reasonKey = IntentKeys.NotHuman; return false; }

			Mode = InteractionMode.MoveUnit;
			PendingUnitUid = unitUid;
			PendingBuildingId = null;
			return true;
		}

		/// <summary>回到点选模式（右键 / ESC）。</summary>
		public void Cancel()
		{
			Mode = InteractionMode.Select;
			PendingBuildingId = null;
			PendingUnitUid = null;
		}
		// ────────────────────────── 点击 ──────────────────────────

		/// <summary>处理一次点击：按当前模式提交意图（或返回该格的选中信息）。</summary>
		public InteractionFeedback Click(HexCubePosition position)
		{
			if (string.IsNullOrWhiteSpace(MapId))
				return new InteractionFeedback { Ok = false, MessageKey = IntentKeys.NoMap };

			switch (Mode)
			{
				case InteractionMode.Build:
					return SubmitBuild(position);

				case InteractionMode.MoveUnit:
					return SubmitMove(position);

				default:
					return Select(position);
			}
		}

		private InteractionFeedback SubmitBuild(HexCubePosition position)
		{
			if (string.IsNullOrWhiteSpace(PendingBuildingId))
				return new InteractionFeedback { Ok = false, MessageKey = IntentKeys.NoTarget, Detail = "未选择要建造的建筑" };

			string buildingId = PendingBuildingId;
			IntentResult result = _intent.Handle(PlayerIntent.Build(MapId, OwnerId, buildingId, position));
			if (!result.Ok)
				return new InteractionFeedback { Ok = false, MessageKey = result.MessageKey, Detail = $"建造 {buildingId} @ {position} 被拒" };

			Cancel(); // 开工即退出建造模式（再点一次才会建第二栋 ⇒ 避免误建，口径见 `D122`）
			return new InteractionFeedback { Ok = true, Detail = $"开工建造 {buildingId} @ {position}" };
		}

		private InteractionFeedback SubmitMove(HexCubePosition position)
		{
			if (string.IsNullOrWhiteSpace(PendingUnitUid))
				return new InteractionFeedback { Ok = false, MessageKey = IntentKeys.NoTarget, Detail = "未选择要指挥的单位" };

			string unitUid = PendingUnitUid;
			IntentResult result = _intent.Handle(PlayerIntent.Move(MapId, OwnerId, unitUid, position));
			if (!result.Ok)
				return new InteractionFeedback { Ok = false, MessageKey = result.MessageKey, Detail = $"单位 {unitUid} 移动被拒" };

			Cancel();
			return new InteractionFeedback { Ok = true, Detail = $"单位 {unitUid} 前往 {position}" };
		}

		private InteractionFeedback Select(HexCubePosition position)
		{
			MapCell cell = _map?.GetMapCell(MapId, position);
			MapOccupantInfo? info = _map?.GetOccupantInfo(MapId, position);

			string terrainId = cell?.Terrain?.Id ?? "(无)";
			string occupant = info == null
				? "(空)"
				: $"{info.Value.Id}(uid={info.Value.UId}, owner={info.Value.OwnerId}, ready={info.Value.IsReady})";
			return new InteractionFeedback { Ok = true, Detail = $"{position} 地形={terrainId} 占据物={occupant} 人口={cell?.Population ?? 0}" };
		}

		// ────────────────────────── 弹窗与门控（只查，不写） ──────────────────────────

		/// <summary>待决事件（`WP-4.12` 的队列）：事件弹窗的数据源。</summary>
		public IReadOnlyList<PendingEventDecision> PendingEvents()
			=> _events?.GetPendingDecisions(MapId, OwnerId) ?? System.Array.Empty<PendingEventDecision>();

		/// <summary>弹窗上的"确定"：把决策交给意图层（而不是直接调事件服务）。</summary>
		public InteractionFeedback ConfirmEvent(string eventId)
		{
			if (string.IsNullOrWhiteSpace(eventId))
				return new InteractionFeedback { Ok = false, MessageKey = IntentKeys.NoTarget };

			IntentResult result = _intent.Handle(PlayerIntent.DecideEvent(MapId, OwnerId, eventId));
			return result.Ok
				? new InteractionFeedback { Ok = true, Detail = $"已决策 {eventId}" }
				: new InteractionFeedback { Ok = false, MessageKey = result.MessageKey, Detail = $"事件 {eventId} 决策被拒" };
		}

		/// <summary>界面能力是否已解锁（表现层据此灰化面板；门控口径见 `WP-4.14`）。</summary>
		public bool IsUiUnlocked(string uiKey) => _uiGate?.IsUnlocked(MapId, OwnerId, uiKey) ?? false;
	}
}
