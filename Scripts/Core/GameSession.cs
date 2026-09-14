using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Core.Time;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）对局会话：**只持有状态**（游戏时钟 + 地图运行时缓存 + 后续的每玩家子系统状态），
	/// 不持有服务。这样测试可以替换任意实现，而服务集合由 <see cref="CoreServices"/> 承担。
	/// <para>它同时是"多玩家/多地图隔离"与"存档边界"的落点（`DEP-03`）：
	/// 后续每玩家状态（资源池 / 科技树 / 修正器 / 迷雾）依次挂到 <see cref="Player"/> 上。</para>
	/// </summary>
	public sealed class GameSession(MapSession maps, GameClock clock, string sessionId)
	{
		public string SessionId { get; } = sessionId ?? "session";

		public GameClock Clock { get; } = clock;

		public MapSession Maps { get; } = maps;

		public bool IsPaused
		{
			get => Clock.IsPaused;
			set => Clock.Speed = value ? TimeSpeedTier.Paused : TimeSpeedTier.Standard;
		}

		/// <summary>按真实秒推进游戏时间（返回派发的游戏日数）。</summary>
		public int Advance(double realSeconds) => Clock.Advance(realSeconds);

		/// <summary>存档点：把所有脏地图写盘。</summary>
		public void SaveAll() => Maps.FlushAll();
	}
}
