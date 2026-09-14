using SciencePotato.Scripts.Core.Time;
using System;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>（v0.3 / WP-1.1）游戏时钟的验收检查：对应 M0-1 ①②③。</summary>
	internal static class ClockChecks
	{
		public static void RunAll()
		{
			Check.Run("M0-1 ① AdvanceDays(1080) → 逐日派发 1080 次", DispatchesEveryDay);
			Check.Run("M0-1 ① 长跑无漏算（CurrentDay/PendingDays 收敛）", LongRunNoLoss);
			Check.Run("M0-1 ③ 三档流速在游戏日维度一致（60 日）", SpeedTiersSameGameDays);
			Check.Run("M0-1 ③ 「每 10 日」节拍在三档下均为 6 次", TenDayTickStableAcrossTiers);
			Check.Run("日期换算（0 日 = 0年1月1日 / 360 日 = 1年1月1日）", DateFormat);
			Check.Run("暂停：游戏时间冻结、暂停期间真实时间被丢弃", PauseFreezesGameTime);
			Check.Run("单帧上限：超出部分记为欠账且不丢失", MaxDaysPerFrameKeepsDebt);
			Check.Run("替身：ManualTimeDriver 可手动推进", ManualDriverAdvances);
			Check.Run("替身：InMemoryFileSystem 读写/枚举/删除", InMemoryFileSystemWorks);
		}

		private static void DispatchesEveryDay()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			int events = 0;
			clock.DayElapsed += _ => events++;

			int dispatched = clock.AdvanceDays(1080);

			Check.AssertEqual(1080, dispatched, "派发日数");
			Check.AssertEqual(1080, events, "DayElapsed 触发次数");
			Check.AssertEqual(1080d, clock.CurrentDay, "CurrentDay");
			Check.AssertEqual(0d, clock.PendingDays, "PendingDays");
		}

		private static void LongRunNoLoss()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			int events = 0;
			clock.DayElapsed += _ => events++;

			// 第三档（6 日/秒）连续推进 180 秒 = 1080 日 = 3 年
			for (int second = 0; second < 180; second++)
			{
				clock.Speed = (second % 2 == 0) ? TimeSpeedTier.Fastest : TimeSpeedTier.Fastest;
				clock.Advance(1.0);
			}

			Check.AssertEqual(1080d, clock.CurrentDay, "CurrentDay");
			Check.AssertEqual(1080, events, "事件总数");
			Check.Assert(clock.PendingDays < 1d, "PendingDays 应小于 1 日");
		}

		private static void SpeedTiersSameGameDays()
		{
			Check.AssertEqual(60, DaysAdvanced(TimeSpeedTier.Standard, 60.0), "标准档 60 真实秒");
			Check.AssertEqual(60, DaysAdvanced(TimeSpeedTier.Fast, 20.0), "第二档 20 真实秒");
			Check.AssertEqual(60, DaysAdvanced(TimeSpeedTier.Fastest, 10.0), "第三档 10 真实秒");
		}

		private static int DaysAdvanced(TimeSpeedTier tier, double realSeconds)
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue, Speed = tier };
			int days = 0;
			clock.DayElapsed += _ => days++;
			clock.Advance(realSeconds);
			return days;
		}

		private static void TenDayTickStableAcrossTiers()
		{
			Check.AssertEqual(6, TenDayTicks(TimeSpeedTier.Standard, 60.0), "标准档 60 日内的 10 日节拍");
			Check.AssertEqual(6, TenDayTicks(TimeSpeedTier.Fast, 20.0), "第二档 60 日内的 10 日节拍");
			Check.AssertEqual(6, TenDayTicks(TimeSpeedTier.Fastest, 10.0), "第三档 60 日内的 10 日节拍");
		}

		private static int TenDayTicks(TimeSpeedTier tier, double realSeconds)
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue, Speed = tier };
			int ticks = 0;
			clock.DayElapsed += day => { if (day % 10 == 0) ticks++; };
			clock.Advance(realSeconds);
			return ticks;
		}

		private static void DateFormat()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			Check.AssertEqual("0年1月1日", clock.Format(), "第 0 日");

			clock.AdvanceDays(30);
			Check.AssertEqual("0年2月1日", clock.Format(), "第 30 日（1 个月后）");

			clock.AdvanceDays(30);
			Check.AssertEqual("0年3月1日", clock.Format(), "第 60 日");

			var year = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			year.AdvanceDays(360);
			Check.AssertEqual("1年1月1日", year.Format(), "第 360 日（1 年后）");
		}

		private static void PauseFreezesGameTime()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			clock.Speed = TimeSpeedTier.Paused;

			int dispatched = clock.Advance(600.0);

			Check.AssertEqual(0, dispatched, "暂停期间派发日数");
			Check.AssertEqual(0d, clock.CurrentDay, "暂停期间 CurrentDay");
			Check.AssertEqual(0d, clock.PendingDays, "暂停期间不应堆积欠账");
			Check.Assert(clock.IsPaused, "IsPaused 应为 true");
		}

		private static void MaxDaysPerFrameKeepsDebt()
		{
			var clock = new GameClock(); // 默认单帧上限 30 日
			Check.AssertEqual(30, GameClock.DefaultMaxDaysPerAdvance, "默认上限常数");

			int first = clock.AdvanceDays(100);

			Check.AssertEqual(30, first, "首次派发应被上限截断");
			Check.AssertEqual(30d, clock.CurrentDay, "CurrentDay");
			Check.AssertEqual(70d, clock.PendingDays, "欠账应保留");

			int total = first;
			while (clock.PendingDays >= 1d)
				total += clock.AdvanceDays(0);

			Check.AssertEqual(100, total, "补齐后总派发日数");
			Check.AssertEqual(100d, clock.CurrentDay, "补齐后 CurrentDay");
		}

		private static void ManualDriverAdvances()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			var driver = new ManualTimeDriver(clock);

			driver.Advance(30.0); // 标准档 = 30 游戏日

			Check.AssertEqual(30d, driver.Clock.CurrentDay, "手动驱动力推进量");
		}

		private static void InMemoryFileSystemWorks()
		{
			var fs = new InMemoryFileSystem();
			Check.Assert(!fs.Exists("user://maps/m1.json"), "初始不应存在");

			fs.WriteAllText("user://maps/m1.json", "{\"Id\":\"m1\"}");

			Check.Assert(fs.Exists("user://maps/m1.json"), "写入后应存在");
			Check.AssertEqual("{\"Id\":\"m1\"}", fs.ReadAllText("user://maps/m1.json"), "读取内容");

			int listed = 0;
			foreach (string _ in fs.ListFiles("user://maps/")) listed++;
			Check.AssertEqual(1, listed, "枚举文件数");

			fs.Delete("user://maps/m1.json");
			Check.Assert(!fs.Exists("user://maps/m1.json"), "删除后不应存在");
			Check.AssertEqual(null, fs.ReadAllText("user://maps/m1.json"), "读取不存在文件应返回 null");
		}
	}
}
