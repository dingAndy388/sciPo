using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Events.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-2.8）**事件引擎**的验收检查：字段改为 `TriggerChancePerDay`（口径 = %/日）、
	/// 每个游戏日恰好掷一次骰、生效中不重复触发、持续期到期回收。
	/// <para>**M0-2 ⑤** 的门槛：固定种子下 3 年（1080 日）的事件触发次数落在期望区间（可复现）。</para>
	/// </summary>
	internal static class EventEngineChecks
	{
		private const int ThreeYearsInDays = 1080;

		public static void RunAll()
		{
			Check.Run("WP-2.8 字段改名：TriggerChancePerDay 生效且与设计 %/日 一致（旧名有提示）", FieldRenameIsEffective);
			Check.Run("M0-2 ⑤ 固定种子 3 年触发次数落在期望区间（且可复现）", ThreeYearTriggerCountsStayInRange);
			Check.Run("WP-2.8 按日掷骰：3 年恰好掷 1080 次/事件（不是按帧、不漏算）", RollsArePerGameDay);
			Check.Run("WP-2.8 概率标定：5%/日 事件在 1080 日的频率落在 [3.5%,6.5%]", ProbabilityIsCalibrated);
			Check.Run("WP-2.8 前置：资源/科技不足不触发；资源足够才触发并扣除", PrerequisitesGateTriggers);
			Check.Run("WP-2.8 持续期：到期自动回收（0=永久只触发一次）+ 生效中不重复触发 + 引擎幂等", DurationAndNoStackingWork);
		}

		// ────────────────────────── 字段改名 ──────────────────────────

		private static void FieldRenameIsEffective()
		{
			ConfigTables tables = ConfigFixtures.BuildRealCore().Tables;
			List<IEventConfig> events = tables.Events.GetAllEvents();

			Check.AssertEqual(3, events.Count, "事件条目数（最小样例）");
			Check.AssertEqual(0.002f, events.Single(e => e.EventId == "gold_rush").TriggerChancePerDay, "淘金热 触发概率（设计 0.2%/日）");
			Check.AssertEqual(0.001f, events.Single(e => e.EventId == "plague").TriggerChancePerDay, "瘟疫 触发概率（设计 0.1%/日）");
			Check.AssertEqual(0.0005f, events.Single(e => e.EventId == "enlightenment").TriggerChancePerDay, "启蒙时代 触发概率（设计 0.05%/日）");
			Check.AssertEqual(0, events.Single(e => e.EventId == "enlightenment").Duration, "启蒙时代 持续期（设计：永久）");

			// 旧字段名不再被读取：`TriggerChance` 会被静默忽略 → 概率为 0 → 校验器必须提示"永不触发"并点名旧字段
			ConfigReport report = ReportFor("Events", """
			{ "Events": [ { "EventId": "legacy", "Name": "旧字段", "TriggerChance": 0.5, "Duration": 10, "Modifiers": [] } ] }
			""");

			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Warning
					&& i.Id == "legacy" && i.Message.Contains("TriggerChancePerDay=0") && i.Message.Contains("TriggerChance")),
				"旧字段名导致概率为 0 时，应提示改名（否则事件永不触发）：" + string.Join(" | ", report.Issues.Select(i => i.ToString())));
		}

		// ────────────────────────── M0-2 ⑤ ──────────────────────────

		private static void ThreeYearTriggerCountsStayInRange()
		{
			const int seed = 20260914;
			Dictionary<string, int> first = RunThreeYears(seed);
			Dictionary<string, int> second = RunThreeYears(seed);

			Console.WriteLine("       [diag] 3 年触发次数：" + string.Join(", ",
				first.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")));

			Check.AssertEqual(first.Count, second.Count, "两次运行的触发事件种类数应一致");
			foreach (var kv in first)
				Check.AssertEqual(kv.Value, second[kv.Key], $"固定种子下 {kv.Key} 的触发次数应可复现");

			// 期望区间 = 均值 ± 3.5σ（二项分布）；下界取 0 —— 资源前置被扣空后会天然"封顶"，只会让实际值偏低
			AssertInRange(first, "plague", 0.001f);
			AssertInRange(first, "gold_rush", 0.002f);
			AssertInRange(first, "enlightenment", 0.0005f);
			Check.Assert(first.Values.Sum() >= 1, "固定种子下 3 年至少应触发 1 次事件（否则统计口径没跑起来）");
		}

		/// <summary>真实事件表 + 固定种子跑 3 年：先让科技/资源就绪，再看触发次数。</summary>
		private static Dictionary<string, int> RunThreeYears(int seed)
		{
			Harness harness = Harness.Build(eventsJson: null, seed: seed, Food: 500f, idea: 100f);
			try
			{
				// 启蒙时代的前置是「science/mathematics 已研究」：先研究 writing → mathematics
				harness.Tech.Research(harness.MapId, harness.OwnerId, "science", "writing");
				harness.Clock.AdvanceDays(15);
				harness.Tech.Research(harness.MapId, harness.OwnerId, "science", "mathematics");
				harness.Clock.AdvanceDays(30);

				harness.Clock.AdvanceDays(ThreeYearsInDays);
				return new Dictionary<string, int>(harness.Events.GetTriggerCounts(harness.MapId, harness.OwnerId));
			}
			finally
			{
				harness.Dispose();
			}
		}

		private static void AssertInRange(Dictionary<string, int> counts, string eventId, float perDay)
		{
			if (!counts.TryGetValue(eventId, out int actual)) actual = 0;

			double mean = ThreeYearsInDays * perDay;
			double sigma = Math.Sqrt(ThreeYearsInDays * perDay * (1.0 - perDay));
			double upper = Math.Ceiling(mean + 3.5 * sigma);

			Check.Assert(actual >= 0 && actual <= upper,
				$"{eventId}：3 年触发 {actual} 次，期望 {mean:0.##} ± {sigma:0.##}（上限 {upper}）");
		}

		// ────────────────────────── 节拍与概率 ──────────────────────────

		private static void RollsArePerGameDay()
		{
			Harness harness = Harness.Build(eventsJson: OneEventJson("daily", 0.01f, 10), seed: 7);
			try
			{
				harness.Clock.AdvanceDays(ThreeYearsInDays);
				Check.AssertEqual(ThreeYearsInDays, harness.Events.RollCount,
					"1 个事件的 1080 日应恰好掷 1080 次骰（若按帧/按秒判定，这个数会大得离谱）");
			}
			finally
			{
				harness.Dispose();
			}
		}

		private static void ProbabilityIsCalibrated()
		{
			const float perDay = 0.05f;
			Harness harness = Harness.Build(eventsJson: OneEventJson("calib", perDay, 1), seed: 4242);
			try
			{
				harness.Clock.AdvanceDays(ThreeYearsInDays);

				int triggers = harness.Events.GetTriggerCounts(harness.MapId, harness.OwnerId)["calib"];
				double frequency = triggers / (double)ThreeYearsInDays;

				Check.Assert(frequency >= 0.035 && frequency <= 0.065,
					$"5%/日 事件的实际频率 {frequency:P2}（{triggers}/{ThreeYearsInDays}）应落在 [3.5%,6.5%]");
			}
			finally
			{
				harness.Dispose();
			}
		}

		// ────────────────────────── 前置 / 持续期 ──────────────────────────

		private static void PrerequisitesGateTriggers()
		{
			// ① 资源前置：Food 200。初始储备已够 → 先清空再验"不足不触发"；补到 400 → 触发
			//    注意：因 `D25`（`Consume()` 不回写资源池），扣除不落盘 → 前置资源在下次读盘时"复活"，
			//    所以这里**不能**断言"扣空后不再触发"。`D25` 同时影响建造/研发/事件三处扣费，已挂 §18.4。
			//    （v0.6.3 / WP-7.1）"不足"必须是显式构造的：设计口径下 Food 有初始储备（300），
			//    旧写法靠"初始 0"成立，现在要先把它花掉 —— 否则测的是"够用也能触发"。
			Harness poor = Harness.Build(eventsJson: ResourceEventJson("costly", 1f, 5, "Food", 200f), seed: 1);
			try
			{
				SciencePotato.Scripts.Resources.Domain.ResourcesPool empty =
					poor.Resources.GetOrCreatePool(poor.MapId, poor.OwnerId);
				poor.Resources.AddResource("Food", -empty.GetValue("Food"), poor.MapId, poor.OwnerId);
				Check.AssertEqual(0f, poor.Resources.GetOrCreatePool(poor.MapId, poor.OwnerId).GetValue("Food"), "准备：Food 清空");

				poor.Clock.AdvanceDays(30);
				Check.AssertEqual(0, poor.Events.GetTriggerCounts(poor.MapId, poor.OwnerId).Count, "资源不足时不应触发");
			}
			finally { poor.Dispose(); }

			Harness rich = Harness.Build(eventsJson: ResourceEventJson("costly", 1f, 5, "Food", 200f), seed: 1, Food: 400f);
			try
			{
				rich.Clock.AdvanceDays(30);
				Check.Assert(rich.Events.GetTriggerCounts(rich.MapId, rich.OwnerId).TryGetValue("costly", out int costly) && costly >= 1,
					"资源足够时应触发（实际触发 " + (rich.Events.GetTriggerCounts(rich.MapId, rich.OwnerId).GetValueOrDefault("costly")) + " 次）");
			}
			finally { rich.Dispose(); }

			// ② 科技前置：science/writing 未研究 → 不触发；研究完成后可触发（研究要花 30 idea，故需给资源）
			Harness gated = Harness.Build(eventsJson: TechEventJson("scholarly", 1f, 5), seed: 2, idea: 100f);
			try
			{
				gated.Clock.AdvanceDays(30);
				Check.AssertEqual(0, gated.Events.GetTriggerCounts(gated.MapId, gated.OwnerId).Count, "科技前置未满足时不应触发");

				gated.Tech.Research(gated.MapId, gated.OwnerId, "science", "writing");
				gated.Clock.AdvanceDays(17);
				Check.Assert(gated.Events.GetTriggerCounts(gated.MapId, gated.OwnerId).ContainsKey("scholarly"),
					"科技前置满足后应能触发");
			}
			finally { gated.Dispose(); }
		}

		private static void DurationAndNoStackingWork()
		{
			// p=1（每日必中）、Duration=10：效果覆盖第 1~10 日，期间不重复触发；第 11 日到期并重新触发
			Harness harness = Harness.Build(eventsJson: OneEventJson("always", 1f, 10), seed: 3);
			try
			{
				harness.Clock.AdvanceDays(1);
				List<ActiveEvent> actives = harness.Events.GetActiveEvents(harness.MapId, harness.OwnerId).ToList();
				Check.AssertEqual(1, actives.Count, "第 1 日应触发并生效");
				Check.AssertEqual(9, actives[0].RemainingDays, "触发当日起算：第 1 日结束时剩余 9 日");

				harness.Clock.AdvanceDays(4); // 第 5 日
				Check.AssertEqual(5, harness.Events.GetActiveEvents(harness.MapId, harness.OwnerId)[0].RemainingDays, "第 5 日剩余 5 日");
				Check.AssertEqual(1, harness.Events.GetTriggerCounts(harness.MapId, harness.OwnerId)["always"],
					"持续期内不应重复触发（否则修正器会叠加）");

				harness.Clock.AdvanceDays(4); // 第 9 日仍未到期
				Check.AssertEqual(1, harness.Events.GetActiveEvents(harness.MapId, harness.OwnerId)[0].RemainingDays, "第 9 日剩余 1 日");
				Check.AssertEqual(1, harness.Events.GetTriggerCounts(harness.MapId, harness.OwnerId)["always"],
					"持续期内不应重复触发（否则修正器会叠加）");

				harness.Clock.AdvanceDays(1); // 第 10 日结束：到期并回收（当日不会重新触发）
				Check.AssertEqual(0, harness.Events.GetActiveEvents(harness.MapId, harness.OwnerId).Count, "到期后生效列表应清空");
				Check.AssertEqual(1, harness.Events.GetTriggerCounts(harness.MapId, harness.OwnerId)["always"], "到期当日不应重新触发");

				harness.Clock.AdvanceDays(1); // 第 11 日：可以重新触发
				Check.AssertEqual(2, harness.Events.GetTriggerCounts(harness.MapId, harness.OwnerId)["always"],
					"第 11 日应已到期并可重新触发（若未到期，计数仍是 1）");
			}
			finally { harness.Dispose(); }

			// Duration=0（永久）：3 年只触发一次，且一直处于生效中
			Harness permanent = Harness.Build(eventsJson: OneEventJson("forever", 1f, 0), seed: 5);
			try
			{
				permanent.Clock.AdvanceDays(ThreeYearsInDays);
				Check.AssertEqual(1, permanent.Events.GetTriggerCounts(permanent.MapId, permanent.OwnerId)["forever"],
					"永久事件只应触发一次（G8：生效中不重复触发）");
				Check.Assert(permanent.Events.IsActive(permanent.MapId, permanent.OwnerId, "forever"), "永久事件应一直生效");
				Check.AssertEqual(ActiveEvent.Permanent,
					permanent.Events.GetActiveEvents(permanent.MapId, permanent.OwnerId)[0].RemainingDays, "永久事件的剩余天数哨兵值");
			}
			finally { permanent.Dispose(); }

			// 引擎幂等（EVT-03）：重复启动不叠加日节拍任务
			Harness idempotent = Harness.Build(eventsJson: OneEventJson("idem", 0.01f, 1), seed: 9);
			try
			{
				int subscribers = idempotent.Time.SubscriberCount;
				idempotent.Events.StartEventsEngine(idempotent.MapId, idempotent.OwnerId);
				Check.AssertEqual(subscribers, idempotent.Time.SubscriberCount, "重复 StartEventsEngine 不应再注册任务");
			}
			finally { idempotent.Dispose(); }
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private static string OneEventJson(string eventId, float perDay, int duration)
			=> $$"""
			{ "Events": [ { "EventId": "{{eventId}}", "Name": "样例事件", "TriggerChancePerDay": {{perDay}}, "Duration": {{duration}},
			  "Modifiers": [ { "Target": "IdeaGrowth", "Type": "Absolute", "Value": 10 } ],
			  "ResourcePrerequisites": {}, "TechPrerequisites": {} } ] }
			""";

		private static string ResourceEventJson(string eventId, float perDay, int duration, string resource, float amount)
			=> $$"""
			{ "Events": [ { "EventId": "{{eventId}}", "Name": "资源前置事件", "TriggerChancePerDay": {{perDay}}, "Duration": {{duration}},
			  "Modifiers": [ { "Target": "IdeaGrowth", "Type": "Percent", "Value": 0.1 } ],
			  "ResourcePrerequisites": { "{{resource}}": {{amount}} }, "TechPrerequisites": {} } ] }
			""";

		private static string TechEventJson(string eventId, float perDay, int duration)
			=> $$"""
			{ "Events": [ { "EventId": "{{eventId}}", "Name": "科技前置事件", "TriggerChancePerDay": {{perDay}}, "Duration": {{duration}},
			  "Modifiers": [ { "Target": "IdeaGrowth", "Type": "Percent", "Value": 0.1 } ],
			  "ResourcePrerequisites": {}, "TechPrerequisites": { "science": ["writing"] } } ] }
			""";

		/// <summary>用给定文本覆写真实配置中的某一张表，装配（不因 error 抛出）并取回校验报告。</summary>
		private static ConfigReport ReportFor(string tableName, string json)
			=> ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith(tableName, json), failOnConfigErrors: false).ConfigReport;

		/// <summary>事件引擎的测试装配：真实配置表 + 临时目录仓储 + 真实日节拍总线 + 固定种子随机源。</summary>
		private sealed class Harness
		{
			public string Directory;
			public GameClock Clock;
			public GameTimeService Time;
			public ResourcesAppService Resources;
			public TechTreesAppService Tech;
			public EventAppService Events;
			public string MapId = "evt-map";
			public int OwnerId = 1;

			public static Harness Build(string eventsJson, int seed, float Food = 0f, float idea = 0f)
			{
				CoreServices core = ConfigFixtures.BuildRealCore();

				var harness = new Harness
				{
					Directory = Path.Combine(Path.GetTempPath(), "sp-wp28-" + Guid.NewGuid().ToString("N")),
					Clock = core.Session.Clock,
					Time = core.Time,
				};
				System.IO.Directory.CreateDirectory(harness.Directory);

				// D2：一次推进 1080 日必须放开"单帧最多 30 日"，否则剩余日数会记入欠账
				harness.Clock.MaxDaysPerAdvance = int.MaxValue;

				var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(harness.Directory, "mod_")));
				harness.Resources = new ResourcesAppService(
					new ResourcesRepository(Path.Combine(harness.Directory, "res_")),
					core.Tables.Resources,
					harness.Time,
					new ModifierRepository(Path.Combine(harness.Directory, "mod_")));
				harness.Tech = new TechTreesAppService(
					new TechTreesRepository(Path.Combine(harness.Directory, "tech_"), core.Tables.TechTrees),
					core.Tables.TechTrees,
					harness.Resources,
					modifier,
					harness.Time);

				IEventConfigRepository eventRepo = eventsJson == null ? core.Tables.Events : new EventConfigRepository(eventsJson);
				harness.Events = new EventAppService(eventRepo, harness.Resources, harness.Tech, modifier, harness.Time, new SystemRandom(seed));

				if (Food > 0f) harness.Resources.AddResource("Food", Food, harness.MapId, harness.OwnerId);
				if (idea > 0f) harness.Resources.AddResource("Idea", idea, harness.MapId, harness.OwnerId);

				harness.Events.StartEventsEngine(harness.MapId, harness.OwnerId);
				return harness;
			}

			public void Dispose()
			{
				if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
			}
		}
	}
}
