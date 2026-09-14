namespace SciencePotato.Scripts.Core.Time
{
	/// <summary>
	/// （v0.3 / WP-1.5）**时间口径的唯一出处**。
	/// <para>设计口径（§10 规格锁定）：**游戏日是唯一时间单位** —— 30 日 = 1 月、360 日 = 1 年；
	/// 配置表里的所有时长字段（`Duration` / `GrowInterval` / `PopulationGrowthInterval`）一律填 **日**。</para>
	/// <para>因此"秒"只允许出现在**宿主适配层**（<c>GodotTimeDriver</c> 把真实帧时间换算为游戏日）：
	/// 核心与配置里再出现秒，就是 `TIME-13` 的复发。本类同时被
	/// <see cref="SciencePotato.Scripts.Core.Config.ConfigValidator"/> 用作"配置单位自检"的边界。</para>
	/// </summary>
	public static class TimeConstants
	{
		// ────────────────────── 日历 ──────────────────────
		// 与 GameClock 保持单点定义：改动日历只需改 GameClock，本类自动跟随。

		/// <summary>每月天数（30）。</summary>
		public const int DaysPerMonth = GameClock.DaysPerMonth;

		/// <summary>每年月数（12）。</summary>
		public const int MonthsPerYear = GameClock.MonthsPerYear;

		/// <summary>每年天数（360）。</summary>
		public const int DaysPerYear = GameClock.DaysPerYear;

		/// <summary>
		/// 一次 <c>ITickable.OnTick</c> 交付的时间量：**1 游戏日**。
		/// <para><see cref="GameClock"/> 逐日派发 <c>DayElapsed</c>，所以每个订阅者每过一天恰好收到一次 tick，
		/// "每日掷骰""每 10 日回复"在一帧跨多日时也不会漏算（`A3` / `TIME-12`）。</para>
		/// </summary>
		public const float DaysPerTick = 1f;

		// ────────────────────── 硬编码节拍（WP-1.5 收拢）──────────────────────
		// 收拢前这些数字散落在各 AppService 里（EventAppService.cs:39、UnitsAppService.cs:145,296）；
		// 单位由"秒"改为"日"后，节拍集中在此，改动只需一处。

		/// <summary>事件引擎：每日掷一次骰（`TriggerChance` 的口径是「%/日」，见 design/events.md）。</summary>
		public const float EventRollDays = 1f;

		/// <summary>单位攻击结算：每 1 日一次（design 侧"持续伤害"的最小节拍）。</summary>
		public const float UnitAttackDays = 1f;

		/// <summary>
		/// 单位移动 / MP 恢复：每 **10 日** 一次。
		/// <para>依据：design/unit.md 写"MP 每 10 秒恢复一次"，而 M0-3 ② 的验收是
		/// "工人（M=10）跨平原（5）**10 日**走 2 格、跨山地（25）需 30 日" → 秒口径重标定为日即 10 日。</para>
		/// </summary>
		public const float UnitMoveDays = 10f;

		/// <summary>
		/// 经济结算（资源自然增长 / 建筑产出到账）的**缺省**间隔：30 日 = 月结（`C2` / `TIME-14`）。
		/// <para>权威值来自 `Config/Resources.json` 的 <c>GrowInterval</c>；本常量用于配置缺省与检查断言。</para>
		/// </summary>
		public const float ResourceSettlementDays = DaysPerMonth;

		// ────────────────────── 配置单位自检边界（ConfigValidator 用）──────────────────────

		/// <summary>合法"日"值的下界：>= 1 日才有意义（0 另有语义：不增长 / 瞬间完成 / 永久）。</summary>
		public const float MinPlausibleDays = 1f;

		/// <summary>
		/// 合法"日"值的上界（= 1 年，360 日）。超过它几乎只可能是**尚未重标定的秒值**
		/// （如旧 `GrowInterval=300`、建造 `Duration=600`），因此校验器只记 warning 而不阻断：
		/// 设计上的时长上限本来就在 1 年以内（`A4`：建造 30~300、研究 0~180、事件 5~30、人口 180~300、经济 30）。
		/// </summary>
		public const float MaxPlausibleDays = DaysPerYear;
	}
}
