using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Intent.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Domain;
using SciencePotato.Scripts.Units.Application;

namespace SciencePotato.Scripts.Intent.Application
{
	/// <summary>
	/// （v0.9.4 / `WP-5.7`）**人类玩家的意图处理器**：UI 提交意图 → 这里做全部校验 → 再调应用服务。
	/// <list type="bullet">
	/// <item>**谁能做**：`OwnerId` 必须是人类势力（`GameSession.IsHuman`）⇒ 表现层不可能指挥 AI；</item>
	/// <item>**能不能做**：界面能力（`UiGateService`，`WP-4.14`）、科技可研究性（`CanResearch`）、
	/// 目标合法性（单位存在且属己、格子可建/可进）；</item>
	/// <item>**做了什么**：只调用与玩家同一套应用服务（`D88` 口径），不绕过任何规则；</item>
	/// <item>**失败给键**：所有拒绝都带 <see cref="IntentKeys"/> 里的键，表现层翻译成文案（`R4` / `D90`）。</item>
	/// </list>
	/// <para><see cref="CanRequest"/> 是**只读预览**（按钮灰化用），权威判断在 <see cref="Handle"/>：
	/// 资源是否够、任务槽是否满这类"执行时刻才知道"的规则由应用服务回答。</para>
	/// </summary>
	public sealed class HumanIntentHandler : IActionHandler
	{
		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly ConstructionAppService _construction;
		private readonly UnitsAppService _units;
		private readonly TechTreesAppService _tech;
		private readonly UiGateService _uiGate;
		private readonly EventAppService _events;

		public HumanIntentHandler(
			GameSession session,
			MapAppService map,
			ConstructionAppService construction,
			UnitsAppService units,
			TechTreesAppService tech,
			UiGateService uiGate,
			EventAppService events)
		{
			_session = session;
			_map = map;
			_construction = construction;
			_units = units;
			_tech = tech;
			_uiGate = uiGate;
			_events = events;
		}

		// ────────────────────────── 只读预检 ──────────────────────────

		/// <inheritdoc />
		public bool CanRequest(PlayerIntent intent, out string reasonKey)
		{
			reasonKey = null;
			if (intent == null) { reasonKey = IntentKeys.NoIntent; return false; }
			if (string.IsNullOrWhiteSpace(intent.MapId)) { reasonKey = IntentKeys.NoMap; return false; }
			if (!_session.IsHuman(intent.OwnerId)) { reasonKey = IntentKeys.NotHuman; return false; }

			switch (intent.Kind)
			{
				case IntentKind.Build:
					if (string.IsNullOrWhiteSpace(intent.Id) || !intent.HasTarget) { reasonKey = IntentKeys.NoTarget; return false; }
					if (_map == null || !_map.IsClear(intent.MapId, intent.Target)) { reasonKey = IntentKeys.Rejected; return false; }
					return true;

				case IntentKind.Upgrade:
					if (string.IsNullOrWhiteSpace(intent.SourceUid)) { reasonKey = IntentKeys.NoTarget; return false; }
					return OwnedUid(intent, out reasonKey);

				case IntentKind.Train:
					if (string.IsNullOrWhiteSpace(intent.SourceUid) || string.IsNullOrWhiteSpace(intent.Id)) { reasonKey = IntentKeys.NoTarget; return false; }
					return OwnedUid(intent, out reasonKey);

				case IntentKind.Research:
					if (string.IsNullOrWhiteSpace(intent.TreeId) || string.IsNullOrWhiteSpace(intent.Id)) { reasonKey = IntentKeys.NoTarget; return false; }
					if (!IsRootNode(intent) && (_uiGate == null || !_uiGate.IsResearchUnlocked(intent.MapId, intent.OwnerId)))
					{
						reasonKey = IntentKeys.UiLocked;
						return false;
					}
					if (_tech == null || !_tech.CanResearch(intent.MapId, intent.OwnerId, intent.TreeId, intent.Id)) { reasonKey = IntentKeys.Prerequisite; return false; }
					return true;

				case IntentKind.MoveUnit:
					if (string.IsNullOrWhiteSpace(intent.SourceUid) || !intent.HasTarget) { reasonKey = IntentKeys.NoTarget; return false; }
					if (!OwnedUid(intent, out reasonKey)) return false;
					if (_units?.Movement == null || !_units.Movement.CanEnter(intent.MapId, intent.OwnerId, intent.Target))
					{
						reasonKey = IntentKeys.Rejected;
						return false;
					}
					return true;

				case IntentKind.ResolveEvent:
					if (string.IsNullOrWhiteSpace(intent.Id)) { reasonKey = IntentKeys.NoTarget; return false; }
					return true;

				default:
					reasonKey = IntentKeys.UnknownKind;
					return false;
			}
		}

		// ────────────────────────── 执行（权威） ──────────────────────────

		/// <inheritdoc />
		public IntentResult Handle(PlayerIntent intent)
		{
			if (intent == null) return IntentResult.Reject(IntentKeys.NoIntent);
			if (string.IsNullOrWhiteSpace(intent.MapId)) return IntentResult.Reject(IntentKeys.NoMap);
			if (!_session.IsHuman(intent.OwnerId)) return IntentResult.Reject(IntentKeys.NotHuman);

			switch (intent.Kind)
			{
				case IntentKind.Build:
					if (string.IsNullOrWhiteSpace(intent.Id) || !intent.HasTarget) return IntentResult.Reject(IntentKeys.NoTarget);
					return _construction.StartConstruction(intent.MapId, intent.Id, intent.Target, intent.OwnerId)
						? IntentResult.Success()
						: IntentResult.Reject(IntentKeys.Rejected);

				case IntentKind.Upgrade:
					if (string.IsNullOrWhiteSpace(intent.SourceUid)) return IntentResult.Reject(IntentKeys.NoTarget);
					if (!OwnedUid(intent, out string upgradeReason)) return IntentResult.Reject(upgradeReason);
					return _construction.StartUpgrade(intent.MapId, intent.SourceUid, intent.OwnerId)
						? IntentResult.Success()
						: IntentResult.Reject(IntentKeys.Rejected);

				case IntentKind.Train:
					if (string.IsNullOrWhiteSpace(intent.SourceUid) || string.IsNullOrWhiteSpace(intent.Id)) return IntentResult.Reject(IntentKeys.NoTarget);
					if (!OwnedUid(intent, out string trainReason)) return IntentResult.Reject(trainReason);
					return _units.TrainUnit(intent.MapId, intent.SourceUid, intent.Id)
						? IntentResult.Success()
						: IntentResult.Reject(IntentKeys.Rejected);

				case IntentKind.Research:
					if (string.IsNullOrWhiteSpace(intent.TreeId) || string.IsNullOrWhiteSpace(intent.Id)) return IntentResult.Reject(IntentKeys.NoTarget);
					if (!IsRootNode(intent) && (_uiGate == null || !_uiGate.IsResearchUnlocked(intent.MapId, intent.OwnerId))) return IntentResult.Reject(IntentKeys.UiLocked);
					if (_tech == null) return IntentResult.Reject(IntentKeys.Rejected);
					if (!_tech.CanResearch(intent.MapId, intent.OwnerId, intent.TreeId, intent.Id)) return IntentResult.Reject(IntentKeys.Prerequisite);
					_tech.Research(intent.MapId, intent.OwnerId, intent.TreeId, intent.Id);
					return IntentResult.Success();

				case IntentKind.MoveUnit:
					if (string.IsNullOrWhiteSpace(intent.SourceUid) || !intent.HasTarget) return IntentResult.Reject(IntentKeys.NoTarget);
					if (!OwnedUid(intent, out string moveReason)) return IntentResult.Reject(moveReason);
					if (_units?.Movement == null) return IntentResult.Reject(IntentKeys.Rejected);
					_units.Movement.SetDestination(intent.MapId, intent.SourceUid, intent.Target);
					return IntentResult.Success();

				case IntentKind.ResolveEvent:
					if (string.IsNullOrWhiteSpace(intent.Id)) return IntentResult.Reject(IntentKeys.NoTarget);
					return _events.Resolve(intent.MapId, intent.OwnerId, intent.Id)
						? IntentResult.Success()
						: IntentResult.Reject(IntentKeys.Rejected);

				default:
					return IntentResult.Reject(IntentKeys.UnknownKind);
			}
		}

		/// <summary>
		/// 该节点是否是所在树的**根**（无前置）。根节点是"研究功能的入口"：面板门控（`UnlocksUi: research`）
		/// 只约束非根节点，否则"第一条科技"永远进不去（计数自己就是解锁研究面板的那条科技）。
		/// </summary>
		private bool IsRootNode(PlayerIntent intent)
		{
			if (_tech == null) return false;

			var tree = _tech.GetOrCreateTechTree(intent.MapId, intent.OwnerId, intent.TreeId);
			if (tree == null || !tree.Nodes.TryGetValue(intent.Id, out var node) || node.Config == null) return false;

			return node.Config.Prerequisites == null || node.Config.Prerequisites.Count == 0;
		}

		/// <summary>目标 uid 存在且属于发起者（建筑/单位通用）。</summary>
		private bool OwnedUid(PlayerIntent intent, out string reasonKey)
		{
			reasonKey = null;
			if (_map == null) { reasonKey = IntentKeys.NoUnit; return false; }

			var occupant = _map.FindOccupantByUId(intent.MapId, intent.SourceUid);
			if (occupant == null) { reasonKey = IntentKeys.NoUnit; return false; }

			MapOccupantInfo info = occupant.GetInfo();
			if (info.OwnerId != intent.OwnerId)
			{
				reasonKey = IntentKeys.NotHuman;
				return false;
			}
			return true;
		}
	}
}
