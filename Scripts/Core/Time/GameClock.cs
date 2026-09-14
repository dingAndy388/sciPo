using System;

namespace SciencePotato.Scripts.Core.Time
{
	/// <summary>
	/// （v0.3 / WP-1.1）游戏时钟：以"游戏日"为唯一时间单位，支持三档流速与暂停。
	/// <para>口径（已与设计确认）：30 日 = 1 月；12 月 = 1 年 = 360 日；第 0 日显示为 <c>0年1月1日</c>。</para>
	/// <para>关键约束：跨越日边界时**逐日**派发 <see cref="DayElapsed"/>，保证"每日掷骰""每 10 日回复 MP"
	/// "每月结算"等周期在一帧跨多日时也不漏算。</para>
	/// </summary>
	public sealed class GameClock
	{
		public const int DaysPerMonth = 30;
		public const int MonthsPerYear = 12;
		public const int DaysPerYear = DaysPerMonth * MonthsPerYear;

		/// <summary>单帧默认最大派发日数（避免掉帧/长时间暂停恢复时出现长循环）。</summary>
		public const int DefaultMaxDaysPerAdvance = 30;

		/// <summary>当前游戏日（从 0 开始递增，含不足一日的小数部分）。</summary>
		public double CurrentDay { get; private set; }

		/// <summary>不足一日的余量 + 因单帧上限未派发的欠账（单位：游戏日）。</summary>
		public double PendingDays { get; private set; }

		/// <summary>单次 <see cref="Advance"/> 最多派发的日数；测试/离线结算可调高。</summary>
		public int MaxDaysPerAdvance { get; set; } = DefaultMaxDaysPerAdvance;

		/// <summary>流速档位；设为 <see cref="TimeSpeedTier.Paused"/> 即冻结游戏时间。</summary>
		public TimeSpeedTier Speed { get; set; } = TimeSpeedTier.Standard;

		public bool IsPaused => Speed == TimeSpeedTier.Paused;

		/// <summary>当前档位对应的"游戏日 / 真实秒"。</summary>
		public double DaysPerSecond => (double)(int)Speed;

		/// <summary>跨越一个游戏日时触发；参数 = 跨越后的整日序号（≥ 1）。</summary>
		public event Action<int> DayElapsed;

		/// <summary>按真实秒推进时钟；返回本次实际派发的日数。</summary>
		public int Advance(double realSeconds)
		{
			if (IsPaused || realSeconds <= 0.0) return 0;
			PendingDays += realSeconds * DaysPerSecond;
			return DispatchPendingDays();
		}

		/// <summary>直接推进 N 个游戏日（离线结算 / 测试用），同样逐日派发；传 0 表示只把欠账派发出去。</summary>
		public int AdvanceDays(double days)
		{
			if (days > 0.0) PendingDays += days;
			return DispatchPendingDays();
		}

		/// <summary>把欠账中已满整日的部分逐日派发出去。</summary>
		private int DispatchPendingDays()
		{
			int dispatched = 0;
			while (PendingDays >= 1.0 && dispatched < MaxDaysPerAdvance)
			{
				PendingDays -= 1.0;
				CurrentDay += 1.0;
				dispatched++;
				DayElapsed?.Invoke((int)CurrentDay);
			}
			return dispatched;
		}

		/// <summary>当前游戏内日期。</summary>
		public GameDate ToDate()
		{
			int total = (int)Math.Floor(CurrentDay);
			int year = total / DaysPerYear;
			int dayOfYear = total % DaysPerYear;
			return new GameDate(year, dayOfYear / DaysPerMonth + 1, dayOfYear % DaysPerMonth + 1);
		}

		/// <summary>格式化为 <c>X年X月X日</c>。</summary>
		public string Format() => ToDate().ToString();

		public override string ToString() => Format();
	}

	/// <summary>（v0.3 / WP-1.1）游戏内日期（显示口径：年从 0 起、月/日从 1 起）。</summary>
	public readonly struct GameDate(int year, int month, int day)
	{
		public readonly int Year = year;
		public readonly int Month = month;
		public readonly int Day = day;

		public override string ToString() => $"{Year}年{Month}月{Day}日";
	}
}
