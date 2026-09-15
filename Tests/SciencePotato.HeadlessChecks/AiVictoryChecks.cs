using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Application;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.6 / WP-6.6）**AI 与胜负**：AI 与人类**同构**地被判定胜负（`WP-4.19` 的参与者就是玩家表），
	/// 且 **出局即停** —— 胜负判定说"既无单位也无建筑"后，AI 不再决策、不再下单（没有僵尸 AI）。
	/// </summary>
	internal static class AiVictoryChecks
	{
		private const string MapId = "ai-victory";

		public static void RunAll()
		{
			Check.Run("WP-6.6 出局即停：AI 被全灭后 `Evaluate` 拒绝决策（只记一次跳过）", EliminatedAiStopsDeciding);
			Check.Run("WP-6.6 出局即摘 tick：AI 的决策任务自己从时间轴消失（不空转）", EliminatedAiUnregistersItsTick);
			Check.Run("WP-6.6 AI 全灭 ⇒ 人类胜（AI 与人类同一套胜负口径）", HumanWinsWhenAiIsWipedOut);
			Check.Run("WP-6.6 人类全灭 ⇒ AI 胜（同构，方向相反）", AiWinsWhenHumanIsWipedOut);
			Check.Run("WP-6.6 一方出局不拖住另一方：人类出局后 AI 照常决策", OneEliminationDoesNotFreezeTheOther);
		}

		/// <summary>开局已启动的双势力场地（人类 1 + AI 2，各自的资源池/月结/AI 引擎都已挂上）。</summary>
		private static CoreServices NewStartedMap(int seed)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(seed, 24, 24, MapId);
			core.Orchestrator.StartMap(MapId);
			return core;
		}

		/// <summary>把某势力的**全部资产**从图上抹掉（模拟"被全灭"）。</summary>
		private static void WipeOut(CoreServices core, int ownerId)
		{
			foreach (IMapOccupant occupant in core.Map.GetOccupants(MapId)
						 .Where(o => o.GetInfo().OwnerId == ownerId).ToList())
			{
				if (occupant.GetInfo().Type == OccupantType.Building)
					core.Map.RemoveBuilding(MapId, occupant.GetInfo().Position);
				else
					core.Map.RemoveOccupantByPosition(MapId, occupant.GetInfo().Position, occupant);
			}

			core.Victory.Evaluate(MapId, "TestCase");
			Check.Assert(!core.Victory.IsAlive(MapId, ownerId), $"准备：{ownerId} 号势力应已被判出局");
		}

		private static void EliminatedAiStopsDeciding()
		{
			CoreServices core = NewStartedMap(20261050);
			Check.Assert(core.AiService.Evaluate(MapId, 2) != null, "准备：AI 活着时应能决策");

			WipeOut(core, 2);
			int decisionsBefore = core.AiService.DecisionsOf(MapId, 2).Count;

			Check.Assert(core.AiService.Evaluate(MapId, 2) == null, "出局后 `Evaluate` 应返回 null（不再决策）");
			Check.AssertEqual(1, core.AiService.EliminatedSkips, "应只记一次\"因出局跳过\"");
			Check.AssertEqual(decisionsBefore, core.AiService.DecisionsOf(MapId, 2).Count, "决策历史不再增长");
		}

		private static void EliminatedAiUnregistersItsTick()
		{
			CoreServices core = NewStartedMap(20261051);
			int interval = core.Ai.DecisionIntervalDays;

			core.Session.Clock.AdvanceDays(interval);
			Check.Assert(core.AiService.DecisionsOf(MapId, 2).Count >= 1, "准备：AI 至少决策过一次");
			int subscribersWithAi = core.Time.SubscriberCount;

			WipeOut(core, 2);
			core.Session.Clock.AdvanceDays(interval); // 这一拍 AI 发现自己出局 ⇒ 自摘 tick
			int decisionsAtDeath = core.AiService.DecisionsOf(MapId, 2).Count;

			Check.Assert(core.Time.SubscriberCount < subscribersWithAi,
				$"AI 出局后其决策任务应被注销（{subscribersWithAi} → {core.Time.SubscriberCount}）");

			core.Session.Clock.AdvanceDays(interval * 3);
			Check.AssertEqual(decisionsAtDeath, core.AiService.DecisionsOf(MapId, 2).Count,
				"再推进 3 个节拍也不该多出决策（没有僵尸 AI）");
		}

		private static void HumanWinsWhenAiIsWipedOut()
		{
			CoreServices core = NewStartedMap(20261052);
			WipeOut(core, 2);

			GameOutcome outcome = core.Victory.OutcomeOf(MapId);
			Check.Assert(outcome.IsOver, "一局应已结束");
			Check.AssertEqual(1, outcome.WinnerOwnerId ?? -1, "胜者 = 人类（AI 被全灭）");
			Check.Assert(core.Victory.StatusOf(MapId).First(s => s.OwnerId == 2).DefeatReason != null,
				"出局方应留下理由（面板/复盘要用）");
		}

		private static void AiWinsWhenHumanIsWipedOut()
		{
			CoreServices core = NewStartedMap(20261053);
			WipeOut(core, 1);

			GameOutcome outcome = core.Victory.OutcomeOf(MapId);
			Check.Assert(outcome.IsOver, "一局应已结束");
			Check.AssertEqual(2, outcome.WinnerOwnerId ?? -1, "胜者 = AI（人类被全灭）—— 胜负口径对双方同构");
			Check.Assert(core.AiService.Evaluate(MapId, 2) != null, "胜者 AI 自己仍能决策（没有被顺带停掉）");
		}

		private static void OneEliminationDoesNotFreezeTheOther()
		{
			CoreServices core = NewStartedMap(20261054);
			WipeOut(core, 1);

			Check.Assert(core.Victory.IsAlive(MapId, 2), "准备：AI 仍活着");
			Check.Assert(core.AiService.Evaluate(MapId, 2) != null, "人类出局后 AI 照常决策");

			int interval = core.Ai.DecisionIntervalDays;
			int before = core.AiService.DecisionsOf(MapId, 2).Count;
			core.Session.Clock.AdvanceDays(interval);
			Check.Assert(core.AiService.DecisionsOf(MapId, 2).Count > before, "时间轴照常推进，AI 继续按节拍决策");
			Check.AssertEqual(0, core.AiService.EliminatedSkips, "AI 自己没有出局（跳过计数为 0）");
		}
	}
}
