using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Resources.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.6.0 / WP-4.18）**某个势力在某张地图上的启动明细**（开局自检 / 冒烟报告 / 断言用）。
	/// <para>它是"装配真的通电了"的可观测证据：每个 <c>true</c> 都对应一个**具体的服务调用成功**，
	/// 而不是"服务对象非空"这种弱断言。</para>
	/// </summary>
	public sealed class PlayerStartReport
	{
		public string MapId { get; init; }

		public int OwnerId { get; init; }

		public bool IsHuman { get; init; }

		public string DisplayName { get; init; }

		/// <summary>资源池已就绪（含该资源的成长/月结任务都已挂上）。</summary>
		public bool ResourcesPoolStarted { get; set; }

		/// <summary>月度结算任务由**本次调用**新登记（重复启动时为 <c>false</c>，见幂等口径）。</summary>
		public bool SettlementStarted { get; set; }

		/// <summary>事件引擎由**本次调用**新登记（只有人类玩家会为 <c>true</c>）。</summary>
		public bool EventsStarted { get; set; }

		/// <summary>本次是否为"重复启动"（同一 `(mapId, ownerId)` 已经启动过 → 只做幂等补充）。</summary>
		public bool Repeat { get; set; }

		/// <summary>（v0.6.4 / WP-5.9）出生点（布置未做时为 <c>null</c>）。</summary>
		public HexCubePosition? Spawn { get; set; }

		/// <summary>（v0.6.4 / WP-5.9）开局放下的单位数（人类与 AI 应相同）。</summary>
		public int InitialUnitCount { get; set; }

		/// <summary>（v0.7.3 / WP-6.2）AI 决策循环由**本次调用**新挂上（人类势力恒为 <c>false</c>）。</summary>
		public bool AiEngineStarted { get; set; }

		public override string ToString()
			=> $"{DisplayName}(owner={OwnerId},{(IsHuman ? "人类" : "AI")}) 资源池={ResourcesPoolStarted} 月结={SettlementStarted} 事件={EventsStarted}" +
			   $"{(Spawn.HasValue ? $" 出生点=({Spawn.Value.q},{Spawn.Value.r}) 单位×{InitialUnitCount}" : string.Empty)}{(Repeat ? " [重复启动]" : string.Empty)}";
	}

	/// <summary>
	/// （v0.6.0 / WP-4.18）**会话级子系统编排器**：按玩家表逐 owner 启动/查询会话级子系统。
	/// <para>为什么需要单独一层（`ROOT-2` / `DEP-03`）：月结、资源成长、事件引擎都是**每个势力各一份**的周期任务，
	/// 它们必须挂在"会话 + 玩家表"这一层被统一驱动 —— 否则"加一个 AI"意味着在每个应用服务里各找一遍启动点，
	/// 漏掉任何一处就会得到"AI 有资源池但没有月结"这种静默半失效（正是 M0 系列的病根）。</para>
	/// <para>本类**只做编排**，不含任何玩法规则：规则仍归各应用服务（月结在 <see cref="MonthlySettlementService"/>、
	/// 事件在 <see cref="EventAppService"/>）。</para>
	/// <para>幂等口径：同一 `(mapId, ownerId)` 重复 <see cref="StartMap"/> 不会重复登记任务
	/// （各服务自身也有幂等登记表；本类再记一层是为了给"本次是否真的启动了"留下可断言的痕迹）。</para>
	/// <para>⚠️ 迷雾（`FogAppService`）当前仍是**单 owner 实例**，按 owner 拆分属 `WP-4.10`（`FOG-01` 收口），
	/// 因此本类暂不逐 owner 加载迷雾；在那之前迷雾服务人类玩家。</para>
	/// </summary>
	public sealed class SessionOrchestrator
	{
		private readonly GameSession _session;
		private readonly ResourcesAppService _resources;
		private readonly MonthlySettlementService _settlement;
		private readonly EventAppService _events;

		/// <summary>`mapId` → `ownerId` → 最近一次启动明细（重复启动时被覆盖为本次结果）。</summary>
		private readonly Dictionary<string, Dictionary<int, PlayerStartReport>> _reports = new(StringComparer.Ordinal);

		/// <summary>（v0.6.4 / WP-5.9）开局布置（可空 = 不做布置，只启动子系统）。</summary>
		private SessionSetupService _setup;

		/// <summary>（v0.7.3 / WP-6.2）AI 决策循环（可空 = 本局没有 AI）。</summary>
		private AiService _ai;

		/// <summary>（v0.6.4 / WP-5.9）挂上开局布置（由组合根在装配末尾调用，避免"忘了布置"）。</summary>
		public void AttachSetup(SessionSetupService setup) => _setup = setup;

		/// <summary>
		/// （v0.7.3 / WP-6.2）挂上 AI 决策服务（由组合根在装配末尾调用）。
		/// <para>它只对**非人类**势力启动（人类的行为来自玩家操作，不是决策循环）。</para>
		/// </summary>
		public void AttachAi(AiService ai) => _ai = ai;

		/// <summary>（v0.6.4 / WP-5.9）某势力的出生点（未布置过返回 <c>null</c>）。</summary>
		public PlayerSpawn SpawnOf(string mapId, int ownerId) => _setup?.SpawnOf(mapId, ownerId);

		public SessionOrchestrator(
			GameSession session,
			ResourcesAppService resources,
			MonthlySettlementService settlement = null,
			EventAppService events = null)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_resources = resources;
			_settlement = settlement;
			_events = events;
		}

		/// <summary>玩家表（按 ownerId 升序）。</summary>
		public IReadOnlyList<PlayerContext> Players => _session.Players;

		/// <summary>全部势力的 ownerId（AI 决策循环与月结遍历的入口）。</summary>
		public IReadOnlyList<int> OwnerIds => _session.OwnerIds;

		/// <summary>最近一次 <see cref="StartMap"/> 启动的势力数（0 = 地图不存在或未启动）。</summary>
		public int LastStartedPlayerCount { get; private set; }

		/// <summary>最近一次 <see cref="StartMap"/> 的跳过原因（成功为 <c>null</c>；冒烟报告用）。</summary>
		public string LastStartSkippedReason { get; private set; }

		/// <summary>逐玩家遍历（顺序 = 玩家表顺序，确定性）—— 后续 AI tick 也接在这里。</summary>
		public void ForEachPlayer(Action<PlayerContext> action)
		{
			if (action == null) return;
			foreach (PlayerContext player in _session.Players) action(player);
		}

		public bool IsStarted(string mapId) => mapId != null && _reports.ContainsKey(mapId);

		/// <summary>某张地图上某势力的启动明细（从未启动过返回 <c>null</c>）。</summary>
		public PlayerStartReport ReportOf(string mapId, int ownerId)
		{
			if (mapId == null) return null;
			return _reports.TryGetValue(mapId, out Dictionary<int, PlayerStartReport> byOwner)
				&& byOwner.TryGetValue(ownerId, out PlayerStartReport report)
					? report : null;
		}

		/// <summary>某张地图上全部势力的启动明细（按 ownerId 升序）。</summary>
		public IReadOnlyList<PlayerStartReport> ReportsOf(string mapId)
			=> mapId != null && _reports.TryGetValue(mapId, out Dictionary<int, PlayerStartReport> byOwner)
				? byOwner.Values.OrderBy(r => r.OwnerId).ToList()
				: new List<PlayerStartReport>();

		/// <summary>
		/// **启动一张地图上的全部势力子系统**（开局 / 生成地图 / 读档后调用）。
		/// <para>逐 owner 依次：① 资源池（含资源成长任务）→ ② 月度结算任务 → ③ 事件引擎（**仅人类**）。</para>
		/// <para>地图不存在时**不启动任何东西**（也不创建任何存档分区），原因写进 <see cref="LastStartSkippedReason"/>。</para>
		/// </summary>
		public IReadOnlyList<PlayerStartReport> StartMap(string mapId)
		{
			var started = new List<PlayerStartReport>();
			LastStartedPlayerCount = 0;
			LastStartSkippedReason = null;

			if (string.IsNullOrWhiteSpace(mapId))
			{
				LastStartSkippedReason = "mapId 为空";
				return started;
			}

			// 地图必须已在会话里（`MapSession.Get` 会按需读档）：否则启动子系统只会在存档里留下孤儿分区
			if (_session.Maps.Get(mapId) == null)
			{
				LastStartSkippedReason = $"地图 {mapId} 不在会话缓存/存档中";
				return started;
			}

			// ⑨ 开局布置（v0.6.4 / WP-5.9）：出生点 / 开局单位 / 开局资源 / 人类视野。
			//    放在"逐玩家启动子系统"之前：先把势力摆到图上，再给他们各自的资源池与节拍（幂等，重复调用无副作用）
			IReadOnlyList<PlayerSpawn> spawns = _setup?.Setup(mapId) ?? new List<PlayerSpawn>();

			if (!_reports.TryGetValue(mapId, out Dictionary<int, PlayerStartReport> byOwner))
			{
				byOwner = new Dictionary<int, PlayerStartReport>();
				_reports[mapId] = byOwner;
			}

			foreach (PlayerContext player in _session.Players)
			{
				bool repeat = byOwner.ContainsKey(player.OwnerId);

				var report = new PlayerStartReport
				{
					MapId = mapId,
					OwnerId = player.OwnerId,
					IsHuman = player.IsHuman,
					DisplayName = player.DisplayName,
					Repeat = repeat,
					Spawn = spawns.FirstOrDefault(s => s.OwnerId == player.OwnerId)?.Position,
					InitialUnitCount = spawns.FirstOrDefault(s => s.OwnerId == player.OwnerId)?.UnitUIds.Count ?? 0,
				};

				// ① 资源池：缺省池 + 成长任务（每个势力各自一套，互不影响）
				if (_resources != null) report.ResourcesPoolStarted = _resources.GetOrCreatePool(mapId, player.OwnerId) != null;

				// ② 月度结算：每个势力一份（人类与 AI 都要付维护费、都要挨饿）
				if (!repeat && _settlement != null) report.SettlementStarted = _settlement.StartSettlement(mapId, player.OwnerId);

				// ③ 事件引擎：只给人类玩家（`G8` / `WP-4.12` 口径：随机事件要打断的是玩家的决策，AI 不需要被打断）
				if (player.IsHuman && _events != null) report.EventsStarted = _events.StartEventsEngine(mapId, player.OwnerId);

				// ④ AI 决策循环：只给**非人类**势力（v0.7.3 / WP-6.2）—— 人类的行为来自玩家操作
				if (!player.IsHuman && _ai != null) report.AiEngineStarted = _ai.StartEngine(mapId, player.OwnerId);

				byOwner[player.OwnerId] = report;
				started.Add(report);
			}

			LastStartedPlayerCount = started.Count;
			return started;
		}

		/// <summary>（v0.9.9 / `WP-5.11` 收口）读档后作废"已挂上"的引擎痕迹（见 `AiService.ResetEngines`）。</summary>
		/// <returns>被作废的势力数。</returns>
		public int ResetEngines(string mapId) => _ai?.ResetEngines(mapId) ?? 0;

		/// <summary>
		/// **结束一张地图上的势力子系统**（换图 / 弃档）。
		/// <para>只清"本类记下的启动痕迹"：逐个摘任务需要 `ITimeService` 的按 owner 注销能力（`WP-5.6`），
		/// 在那之前由存档/读档流程的 <c>ITimeService.Reset()</c> 统一作废（口径见 `WorldSaveService.LoadWorld` ③.5）。</para>
		/// </summary>
		public void StopMap(string mapId)
		{
			if (mapId == null) return;
			_reports.Remove(mapId);
		}
	}
}
