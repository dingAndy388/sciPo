using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core
{
	/// <summary>（v0.7.0 / WP-4.19）某个势力的存活状态（UI/断言/后续 AI 决策都用它）。</summary>
	public sealed class PlayerStatus
	{
		public int OwnerId { get; init; }

		public bool IsAlive { get; internal set; } = true;

		/// <summary>出局的游戏日（存活时为 <c>null</c>）。</summary>
		public int? DefeatedDay { get; internal set; }

		/// <summary>出局原因（`UnitWiped` = 全灭；`Monthly` = 月结判定；存活时为 <c>null</c>）。</summary>
		public string DefeatReason { get; internal set; }

		public override string ToString()
			=> IsAlive ? $"owner={OwnerId} 存活" : $"owner={OwnerId} 出局（{DefeatReason}，{DefeatedDay} 日）";
	}

	/// <summary>（v0.7.0 / WP-4.19）一局的结果：进行中 / 已分胜负。</summary>
	public sealed class GameOutcome
	{
		public string MapId { get; init; }

		public bool IsOver { get; init; }

		/// <summary>胜者 ownerId；一局已结束但无人存活（同归于尽）时为 <c>null</c>。</summary>
		public int? WinnerOwnerId { get; init; }

		public int Day { get; init; }

		public override string ToString()
			=> IsOver ? $"地图 {MapId} 在 {Day} 日结束：胜者 owner={WinnerOwnerId?.ToString() ?? "无（同归于尽）"}" : $"地图 {MapId} 进行中";
	}

	/// <summary>
	/// （v0.7.0 / WP-4.19）**胜负判定**：口径 = **每月判定 + 全灭**（`D72`，用户已确认）。
	/// <list type="bullet">
	/// <item>**全灭**：某势力**既无单位也无建筑**（含正在交战的进攻方）⇒ 立即出局；</item>
	/// <item>**每月判定**：月结时对每个势力重算一次存活（兜住"全灭发生在两次事件之间"的情形）；</item>
	/// <item>只剩**一个**势力存活 ⇒ 该势力获胜；**零个**存活 ⇒ 同归于尽（胜者为空）。</item>
	/// </list>
	/// <para>为什么判定只在"事件 + 月结"两处触发而不做逐帧扫描：全灭只会由"单位阵亡"或"建筑被毁"造成，
	/// 两者都有推送（`UnitDiedEvent`；建筑被毁随 `WP-4.8`）；月结是兜底节拍。逐帧扫描 10439 格是纯粹的浪费（`R1`）。</para>
	/// <para>AI 与人类是**同一套判据**（`D72`/`D78`）：本类只认 `PlayerContext.OwnerId`，不认识"谁是 AI"。</para>
	/// </summary>
	public sealed class VictoryService
	{
		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly Dictionary<string, Dictionary<int, PlayerStatus>> _status = new(StringComparer.Ordinal);
		private readonly Dictionary<string, GameOutcome> _outcomes = new(StringComparer.Ordinal);

		public VictoryService(
			GameSession session,
			MapAppService map,
			MonthlySettlementService settlement = null,
			IDomainEventBus events = null)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_map = map;

			// 每月判定（`D72` 的第一半）：月结报告已经带 mapId，直接用它
			if (settlement != null) settlement.Settled += report => Evaluate(report.MapId, "Monthly");

			// 全灭（第二半）：单位阵亡是唯一"无月结也要立刻判"的推送（建筑被毁在 `WP-4.8` 里补同一个调用）
			events?.Subscribe<UnitDiedEvent>(evt => Evaluate(evt.MapId, "UnitWiped"));
		}

		/// <summary>该地图上各势力的存活状态（按 ownerId 升序）。</summary>
		public IReadOnlyList<PlayerStatus> StatusOf(string mapId)
			=> mapId != null && _status.TryGetValue(mapId, out Dictionary<int, PlayerStatus> byOwner)
				? byOwner.Values.OrderBy(s => s.OwnerId).ToList()
				: new List<PlayerStatus>();

		/// <summary>该地图的对局结果（未判定过返回"进行中"）。</summary>
		public GameOutcome OutcomeOf(string mapId)
			=> mapId != null && _outcomes.TryGetValue(mapId, out GameOutcome outcome)
				? outcome
				: new GameOutcome { MapId = mapId, IsOver = false };

		public bool IsAlive(string mapId, int ownerId)
			=> StatusOf(mapId).FirstOrDefault(s => s.OwnerId == ownerId)?.IsAlive ?? true;

		/// <summary>
		/// **执行一次判定**（幂等）：已出局的势力不再改状态；一局结束后不再改结果。
		/// </summary>
		/// <param name="trigger">触发原因（`Monthly` / `UnitWiped` / `Manual`），写进出局记录便于排查。</param>
		public GameOutcome Evaluate(string mapId, string trigger = "Manual")
		{
			if (string.IsNullOrWhiteSpace(mapId)) return new GameOutcome { MapId = mapId, IsOver = false };

			Dictionary<int, PlayerStatus> byOwner = GetOrCreate(mapId);
			IReadOnlyList<IMapOccupant> occupants = _map?.GetOccupants(mapId).ToList() ?? new List<IMapOccupant>();

			int day = _session.CurrentDay;
			int alive = 0;
			int? lastAlive = null;

			// 玩家表是权威的"参与者"名单（中立/野怪不在表里，天然不参与胜负）
			foreach (PlayerContext player in _session.Players)
			{
				PlayerStatus status = byOwner[player.OwnerId];
				if (!status.IsAlive) continue;

				bool hasUnits = occupants.Any(o => o != null && o.GetInfo().OwnerId == player.OwnerId
												   && o.GetInfo().Type == OccupantType.Unit);
				bool hasBuildings = occupants.Any(o => o != null && o.GetInfo().OwnerId == player.OwnerId
													   && o.GetInfo().Type == OccupantType.Building);

				if (!hasUnits && !hasBuildings)
				{
					status.IsAlive = false;
					status.DefeatedDay = day;
					status.DefeatReason = trigger;
					continue; // 本回合出局：不计入存活数
				}

				alive++;
				lastAlive = player.OwnerId;
			}

			GameOutcome outcome = alive <= 1
				? new GameOutcome { MapId = mapId, IsOver = true, WinnerOwnerId = alive == 1 ? lastAlive : null, Day = day }
				: new GameOutcome { MapId = mapId, IsOver = false };

			_outcomes[mapId] = outcome;
			return outcome;
		}

		private Dictionary<int, PlayerStatus> GetOrCreate(string mapId)
		{
			if (_status.TryGetValue(mapId, out Dictionary<int, PlayerStatus> existing))
			{
				// 玩家表可能中途加人（`--ai=` / 未来的"中途加入"）：补上缺失的状态行
				foreach (PlayerContext player in _session.Players)
					if (!existing.ContainsKey(player.OwnerId)) existing[player.OwnerId] = new PlayerStatus { OwnerId = player.OwnerId };
				return existing;
			}

			var created = _session.Players.ToDictionary(p => p.OwnerId, p => new PlayerStatus { OwnerId = p.OwnerId });
			_status[mapId] = created;
			return created;
		}
	}
}
