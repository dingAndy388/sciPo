using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.0 / WP-4.19）**胜负判定**的验收检查：每月判定 + 全灭 + 一局结束的判据。
	/// <para>口径（`D72`，用户已确认）：某势力**既无单位也无建筑**即出局；只剩一个存活者 ⇒ 该势力获胜；
	/// 零个存活 ⇒ 同归于尽。AI 与人类**同一套判据**（`D78`）。</para>
	/// </summary>
	internal static class VictoryChecks
	{
		private const string MapId = "victory";

		public static void RunAll()
		{
			Check.Run("WP-4.19 胜负：初始时所有势力存活、对局进行中", AllAliveAtStart);
			Check.Run("WP-4.19 胜负：全灭（无单位无建筑）⇒ 立刻出局，另一方获胜", WipeOutDecidesWinner);
			Check.Run("WP-4.19 胜负：每月判定兜底（不依赖阵亡推送也能发现全灭）", MonthlyBeatCatchesWipe);
			Check.Run("WP-4.19 胜负：三方中先出局的不影响其余（出局记录只写一次）", MultiPlayerEliminationOrder);
			Check.Run("WP-4.19 胜负：势力还有建筑时不算全灭（住房即算活着）", BuildingsKeepPlayerAlive);
		}

		/// <summary>造一个"两个势力、各自有出生点与开局单位"的会话（复用生产装配的 `SessionSetupService`）。</summary>
		private static CoreServices NewTwoPlayerCore()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Map.GenerateMap(20260926, 24, 24, MapId);
			core.Orchestrator.StartMap(MapId); // 生成即开局：出生点 + 开局单位（人类与 AI 各 1 个工人）
			return core;
		}

		private static void AllAliveAtStart()
		{
			CoreServices core = NewTwoPlayerCore();
			GameOutcome outcome = core.Victory.Evaluate(MapId, "Manual");

			Check.Assert(!outcome.IsOver, "开局时对局应仍在进行");
			Check.AssertEqual(2, core.Victory.StatusOf(MapId).Count, "两个势力都在状态表里");
			Check.Assert(core.Victory.StatusOf(MapId).All(s => s.IsAlive), "开局时两个势力都存活");
			Check.Assert(core.Victory.IsAlive(MapId, 1) && core.Victory.IsAlive(MapId, 2), "人类与 AI 同样存活");
		}

		private static void WipeOutDecidesWinner()
		{
			CoreServices core = NewTwoPlayerCore();

			// 抹掉 AI 的全部占据物（单位 + 建筑）⇒ 全灭
			RemoveAllOwnedBy(core, 2);

			// 借一次单位阵亡推送触发"全灭判定"（等价于真实战斗路径）
			core.DomainEvents.Publish(new UnitDiedEvent(MapId, 1, "probe", "worker", new HexCubePosition(0, 0), "none"));

			GameOutcome outcome = core.Victory.OutcomeOf(MapId);
			Check.Assert(outcome.IsOver, "只剩一个势力存活 ⇒ 对局结束");
			Check.AssertEqual(1, outcome.WinnerOwnerId ?? -1, "胜者应是人类（owner=1）");
			Check.Assert(!core.Victory.IsAlive(MapId, 2), "AI 应已出局");
			Check.AssertEqual("UnitWiped", core.Victory.StatusOf(MapId).First(s => s.OwnerId == 2).DefeatReason, "出局原因");
		}

		private static void MonthlyBeatCatchesWipe()
		{
			CoreServices core = NewTwoPlayerCore();
			RemoveAllOwnedBy(core, 2);

			// 不推任何领域事件，只推进 30 日 ⇒ 月结推送应当兜住
			core.Settlement.StartSettlement(MapId, 1);
			core.Session.Clock.AdvanceDays(TimeConstants.DaysPerMonth);

			Check.Assert(core.Victory.OutcomeOf(MapId).IsOver, "月结判定应发现全灭（`D72` 的第一半）");
			Check.AssertEqual("Monthly", core.Victory.StatusOf(MapId).First(s => s.OwnerId == 2).DefeatReason, "出局原因应为月结判定");
		}

		private static void MultiPlayerEliminationOrder()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Session.AddPlayer(PlayerContext.Ai(3));
			core.Map.GenerateMap(20260927, 24, 24, MapId);
			core.Orchestrator.StartMap(MapId);

			RemoveAllOwnedBy(core, 3);
			CoreServices _ = core;
			core.Victory.Evaluate(MapId, "Manual");
			Check.Assert(!core.Victory.IsAlive(MapId, 3), "AI-3 应出局");
			Check.Assert(!core.Victory.OutcomeOf(MapId).IsOver, "还有两个势力存活 ⇒ 对局继续");

			int defeatDay = core.Victory.StatusOf(MapId).First(s => s.OwnerId == 3).DefeatedDay ?? -1;
			core.Victory.Evaluate(MapId, "Manual");
			Check.AssertEqual(defeatDay, core.Victory.StatusOf(MapId).First(s => s.OwnerId == 3).DefeatedDay ?? -1,
				"重复判定不应改写已出局势力的出局日（幂等）");

			RemoveAllOwnedBy(core, 2);
			core.Victory.Evaluate(MapId, "Manual");
			Check.Assert(core.Victory.OutcomeOf(MapId).IsOver, "只剩人类 ⇒ 对局结束");
			Check.AssertEqual(1, core.Victory.OutcomeOf(MapId).WinnerOwnerId ?? -1, "胜者 = owner=1");
		}

		private static void BuildingsKeepPlayerAlive()
		{
			CoreServices core = NewTwoPlayerCore();

			// 只抹掉 AI 的单位（留下建筑）⇒ 不该判出局
			foreach (IMapOccupant occupant in core.Map.GetOccupants(MapId).ToList())
			{
				MapOccupantInfo info = occupant.GetInfo();
				if (info.OwnerId != 2 || info.Type != OccupantType.Unit) continue;
				core.Map.RemoveOccupantByPosition(MapId, info.Position, occupant);
			}

			PlaceBuildingFor(core, 2, new HexCubePosition(3, 3));

			core.Victory.Evaluate(MapId, "Manual");
			Check.Assert(core.Victory.IsAlive(MapId, 2), "有建筑就不算全灭（住房是胜负的最低资产）");
			Check.Assert(!core.Victory.OutcomeOf(MapId).IsOver, "对局应继续");
		}

		// ────────────── 夹具 ──────────────

		/// <summary>抹掉某势力的全部占据物（模拟"被打光"）。</summary>
		private static void RemoveAllOwnedBy(CoreServices core, int ownerId)
		{
			foreach (IMapOccupant occupant in core.Map.GetOccupants(MapId).ToList())
			{
				MapOccupantInfo info = occupant.GetInfo();
				if (info.OwnerId != ownerId) continue;

				core.Map.RemoveOccupantByPosition(MapId, info.Position, occupant);
			}
		}

		private static void PlaceBuildingFor(CoreServices core, int ownerId, HexCubePosition position)
		{
			core.Map.SetTerrain(MapId, position, core.Tables.Terrains.GetById("plain"));
			core.Map.PlaceBuilding(MapId, position,
				new SciencePotato.Scripts.Construction.Domain.BuildingFactory(core.Tables.Buildings).CreateBuilding("camp", position, ownerId));
		}
	}
}
