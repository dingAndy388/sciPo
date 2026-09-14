using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;

namespace SciencePotato.Scripts.Autoload
{
	/// <summary>
	/// （v0.3 / WP-1.2）Godot 侧**唯一的"真实时间 → 游戏日"适配器**（轻量，不含任何玩法逻辑）。
	/// <para>职责：每帧把真实秒交给 <see cref="GameClock.Advance"/>；日边界由
	/// <see cref="GameTimeService"/> 逐日派发给注册的任务。玩法侧只接触"游戏日"，永不接触秒（`TIME-05` / `TIME-13`）。</para>
	/// <para>由 <c>ServiceContainer._Ready</c> 创建并挂到自身之下（因此不需要改场景/autoload 列表）；
	/// 无头环境不实例化本类，测试用 <c>ManualTimeDriver</c> + <see cref="GameTimeService"/> 直接推进。</para>
	/// </summary>
	public partial class GodotTimeDriver : Node, ITimeDriver
	{
		/// <summary>权威时钟（由 <c>CoreBootstrap</c> 创建、随 <c>GameSession</c> 持有）。</summary>
		public GameClock Clock { get; private set; }

		/// <summary>游戏日节拍总线（应用服务的周期任务注册在这里）。</summary>
		public GameTimeService TimeService { get; private set; }

		/// <summary>累计推进的真实秒数（调试/表现层可用）。</summary>
		public double ElapsedRealSeconds { get; private set; }

		/// <summary>本帧被单帧上限截断而顺延的日数（>0 表示掉帧严重）——透出 <see cref="GameClock.PendingDays"/> 便于观测。</summary>
		public double PendingDays => Clock?.PendingDays ?? 0d;

		public bool IsConfigured => Clock != null;

		public bool IsPaused => Clock?.IsPaused ?? true;

		/// <summary>装配入口：与组合根共用同一个 <see cref="GameClock"/> 与节拍总线。</summary>
		public void Configure(GameClock clock, GameTimeService timeService)
		{
			Clock = clock;
			TimeService = timeService;
		}

		/// <summary>按真实秒推进游戏时间；返回本次实际派发的日数（0 = 暂停或参数非法）。</summary>
		public int Advance(double realDeltaSeconds)
		{
			if (Clock == null || realDeltaSeconds <= 0d) return 0;
			ElapsedRealSeconds += realDeltaSeconds;
			return Clock.Advance(realDeltaSeconds);
		}

		public override void _Process(double delta) => Advance(delta);

		/// <summary>切换流速档位（三档 1/3/6 日每真实秒；<see cref="TimeSpeedTier.Paused"/> = 暂停）。</summary>
		public void SetSpeedTier(TimeSpeedTier tier)
		{
			if (Clock != null) Clock.Speed = tier;
		}

		/// <summary>暂停/恢复：恢复时回到标准档（M1 的调试面板与事件决策都会用到）。</summary>
		public void SetPaused(bool paused)
		{
			if (Clock != null) Clock.Speed = paused ? TimeSpeedTier.Paused : TimeSpeedTier.Standard;
		}

		/// <summary>暂停 ⇄ 标准 档切换。</summary>
		public bool TogglePause()
		{
			SetPaused(!IsPaused);
			return IsPaused;
		}
	}
}
