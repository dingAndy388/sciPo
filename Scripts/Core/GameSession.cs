using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）对局会话：**只持有状态**（游戏时钟 + 地图运行时缓存 + 玩家表），
	/// 不持有服务。这样测试可以替换任意实现，而服务集合由 <see cref="CoreServices"/> 承担。
	/// <para>它同时是"多玩家/多地图隔离"与"存档边界"的落点（`DEP-03`）：
	/// v0.6.0 / WP-4.18 起玩家表（<see cref="Players"/>）在这里显式化 —— \"谁是人类、谁是 AI\"不再是隐式约定，
	/// 每玩家的子系统（资源池 / 科技树 / 修正器 / 迷雾）都以 ownerId 为键挂在服务侧。</para>
	/// </summary>
	public sealed class GameSession
	{
		public string SessionId { get; }

		public GameClock Clock { get; }

		public MapSession Maps { get; }

		private readonly List<PlayerContext> _players;

		public GameSession(MapSession maps, GameClock clock, string sessionId)
			: this(maps, clock, sessionId, null)
		{
		}

		/// <param name="players">
		/// （v0.6.0 / WP-4.18）玩家表；<c>null</c> / 空 = 单人类玩家（owner=1 的旧口径，保持既有用例与工具的语义）。
		/// </param>
		public GameSession(MapSession maps, GameClock clock, string sessionId, IEnumerable<PlayerContext> players)
		{
			Maps = maps ?? throw new ArgumentNullException(nameof(maps));
			Clock = clock ?? throw new ArgumentNullException(nameof(clock));
			SessionId = string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId;

			_players = players == null
				? new List<PlayerContext> { PlayerContext.Human() }
				: players.Where(p => p != null)
						 .GroupBy(p => p.OwnerId)
						 .Select(g => g.First())
						 .OrderBy(p => p.OwnerId)
						 .ToList();

			if (_players.Count == 0) _players.Add(PlayerContext.Human());
		}

		/// <summary>玩家表（按 ownerId 升序，确定性遍历顺序 —— AI 决策与月结都依赖它）。</summary>
		public IReadOnlyList<PlayerContext> Players => _players;

		/// <summary>全部势力的 ownerId（AI 与人类的统一遍历入口）。</summary>
		public IReadOnlyList<int> OwnerIds => _players.Select(p => p.OwnerId).ToList();

		/// <summary>人类玩家的 ownerId（无人类时 = 0：纯 AI 推演场景）。</summary>
		public int HumanOwnerId => _players.FirstOrDefault(p => p.IsHuman)?.OwnerId ?? 0;

		public PlayerContext GetPlayer(int ownerId) => _players.FirstOrDefault(p => p.OwnerId == ownerId);

		public bool HasPlayer(int ownerId) => GetPlayer(ownerId) != null;

		/// <summary>该 owner 是否人类玩家（事件引擎与决策暂停的开关）。</summary>
		public bool IsHuman(int ownerId) => GetPlayer(ownerId)?.IsHuman ?? false;

		/// <summary>
		/// 加入一个势力（开局/AI 注册用）。同一 ownerId 重复加入返回 <c>false</c>（不覆盖既有身份）。
		/// </summary>
		public bool AddPlayer(PlayerContext player)
		{
			if (player == null || HasPlayer(player.OwnerId)) return false;

			_players.Add(player);
			_players.Sort((a, b) => a.OwnerId.CompareTo(b.OwnerId));
			return true;
		}

		/// <summary>当前游戏日（从 0 起）。</summary>
		public int CurrentDay => (int)Clock.CurrentDay;

		/// <summary>当前流速档位（三档 1/3/6 日每真实秒；<see cref="TimeSpeedTier.Paused"/> = 暂停）。</summary>
		public TimeSpeedTier Speed
		{
			get => Clock.Speed;
			set => Clock.Speed = value;
		}

		public bool IsPaused
		{
			get => Clock.IsPaused;
			set => Clock.Speed = value ? TimeSpeedTier.Paused : TimeSpeedTier.Standard;
		}

		/// <summary>
		/// 按**真实秒**推进游戏时间（返回派发的游戏日数）。
		/// <para>（v0.3 / WP-1.5）日边界上的派发由 <see cref="GameTimeService"/> 负责：
		/// 本方法只动时钟，注册在总线上的周期任务会逐日收到 <c>OnTick(1 日)</c>。</para>
		/// </summary>
		public int Advance(double realSeconds) => Clock.Advance(realSeconds);

		/// <summary>存档点：把所有脏地图写盘。</summary>
		public void SaveAll() => Maps.FlushAll();
	}
}
