using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.TechTree.Domain;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-1.5 + WP-1.2）**口径重标定**的验收检查：秒 → 游戏日。
	/// <para>核心主张只有一句：**配置里写的数字就是游戏日，且到期由逐日派发保证**。因此这里同时盯住三件事：</para>
	/// <list type="number">
	/// <item>节拍常量集中在 <see cref="TimeConstants"/>（事件 1 日 / 攻击 1 日 / 移动 10 日 / 月结 30 日）；</item>
	/// <item>真实配置里的时长字段全落在 [1, 360] 日，且**过大的值被判 warning**（疑似秒值残留）；</item>
	/// <item>把真实配置装进任务、用 <see cref="GameTimeService"/> 驱动 1080 日，结算次数与"日"口径吻合
	/// （M0-1 ③ 与 M0-2 ②⑤ 的前置）。</item>
	/// </list>
	/// </summary>
	internal static class TimeBaselineChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-1.5 节拍常量：事件 1 日 / 攻击 1 日 / 移动 10 日 / 月结 30 日（单点定义）", BeatConstants);
			Check.Run("WP-1.5 逐日派发：一帧跨多日仍逐日派发（1080 日 → 1080 次）", DailyDispatchThroughBus);
			Check.Run("WP-1.5 帧内累积：不足一日的帧不触发 tick（4 × 0.25 秒 → 1 次）", SubDayFramesDoNotTickEarly);
			Check.Run("WP-1.5 月结到账：真实 Resources 表 GrowInterval=30 → 1080 日结算 36 次", MonthlySettlementFromRealConfig);
			Check.Run("WP-1.5 三档一致：1/3/6 日每真实秒下「月结」次数相同", TiersAgreeOnSettlementCount);
			Check.Run("WP-1.5 暂停：暂停期间总线不派发任何 tick", PauseStopsBus);
			Check.Run("WP-1.5 单位节拍：移动 10 日/次、攻击 1 日/次（1080 日 → 108 / 1080 次）", UnitBeatsFollowConstants);
			Check.Run("WP-1.5 口径自检：真实 5 张表的时长字段全部落在 [1,360] 日且无口径警告", RealConfigDurationsAreDayBased);
			Check.Run("WP-1.5 口径自检：秒值残留（Duration=600）只判 warning、不阻断启动", SecondUnitResidueWarnsOnly);
			Check.Run("WP-1.2 适配器：组合根 + ManualTimeDriver 推进 1 个月触发 1 次月结（M1 前置）", DriverAdvancesOneMonth);
		}

		// ────────────────────────── 节拍与常量 ──────────────────────────

		private static void BeatConstants()
		{
			Check.AssertEqual(30, TimeConstants.DaysPerMonth, "每月天数");
			Check.AssertEqual(12, TimeConstants.MonthsPerYear, "每年月数");
			Check.AssertEqual(360, TimeConstants.DaysPerYear, "每年天数");
			Check.AssertEqual(1f, TimeConstants.DaysPerTick, "每次 tick 交付的日数");
			Check.AssertEqual(1f, TimeConstants.EventRollDays, "事件掷骰节拍（日）");
			Check.AssertEqual(1f, TimeConstants.UnitAttackDays, "单位攻击节拍（日）");
			Check.AssertEqual(10f, TimeConstants.UnitMoveDays, "单位移动/MP 恢复节拍（日）");
			Check.AssertEqual(30f, TimeConstants.ResourceSettlementDays, "经济结算节拍（日）= 月结");
			Check.AssertEqual(360f, TimeConstants.MaxPlausibleDays, "单位自检上界（日）");

			// 日历定义与时钟同源（改 GameClock 即改口径）
			Check.AssertEqual(GameClock.DaysPerMonth, TimeConstants.DaysPerMonth, "TimeConstants 与 GameClock 的每月天数一致");
			Check.AssertEqual(GameClock.DaysPerYear, (int)TimeConstants.MaxPlausibleDays, "自检上界 = 1 年");
		}

		// ────────────────────────── 逐日派发 ──────────────────────────

		private static void DailyDispatchThroughBus()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			var bus = new GameTimeService(clock);
			int rolls = 0;

			// 与 EventAppService 完全一致的注册方式：Target = EventRollDays
			var eventTask = new IntervalTask(0, TimeConstants.EventRollDays, "evt_map_1", "EventTick", "none", "map", 1);
			eventTask.OnCompleted += () => rolls++;
			bus.Register(eventTask);

			// 第三档：每次 Advance(1 真实秒) 跨 6 个游戏日 → 单帧跨多日也不漏掷（A3）
			clock.Speed = TimeSpeedTier.Fastest;
			for (int second = 0; second < 180; second++)
				clock.Advance(1.0);

			Check.AssertEqual(1080, (int)clock.CurrentDay, "推进后的游戏日");
			Check.AssertEqual(1080, rolls, "每日掷骰次数");
			Check.AssertEqual(1, bus.SubscriberCount, "注册中的周期任务数");
		}

		private static void SubDayFramesDoNotTickEarly()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			var bus = new GameTimeService(clock);
			int ticks = 0;

			var task = new IntervalTask(0, TimeConstants.EventRollDays, "evt", "EventTick", "none", "map", 0);
			task.OnCompleted += () => ticks++;
			bus.Register(task);

			clock.Speed = TimeSpeedTier.Standard; // 1 日 / 真实秒
			clock.Advance(0.25);
			clock.Advance(0.25);
			clock.Advance(0.25);
			Check.AssertEqual(0, ticks, "不足一日不应触发 tick");

			clock.Advance(0.25);
			Check.AssertEqual(1, ticks, "满一日后应触发 1 次 tick");
			Check.AssertEqual(1, (int)clock.CurrentDay, "CurrentDay");
		}

		// ────────────────────────── 经济月结 ──────────────────────────

		private static void MonthlySettlementFromRealConfig()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			List<IResourceConfig> resources = core.Tables.AllResources().ToList();

			Check.AssertEqual(3, resources.Count, "真实资源表条目数");
			foreach (IResourceConfig resource in resources)
				Check.AssertEqual(30, resource.GrowInterval, $"{resource.Name}.GrowInterval 应为月结 30 日（v0.3 / WP-1.5）");

			// Idea 的 BaseGrowth=0，但必须仍按月结算：否则建筑 Modifier（School 250 idea/月）永远无处落地
			Check.AssertEqual(0f, resources.First(r => r.Name == "Idea").BaseGrowth, "Idea.BaseGrowth（全靠建筑/科技产出）");

			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			var bus = new GameTimeService(clock);
			var settled = new Dictionary<string, int>();

			foreach (IResourceConfig resource in resources)
			{
				string name = resource.Name;
				settled[name] = 0;
				var task = new IntervalTask(0, resource.GrowInterval, name, "ResourceGrowth", "none", "map", 1);
				task.OnCompleted += () => settled[name]++;
				bus.Register(task);
			}

			clock.Speed = TimeSpeedTier.Fastest;
			for (int second = 0; second < 180; second++) // 180 × 6 = 1080 日 = 3 年
				clock.Advance(1.0);

			foreach (KeyValuePair<string, int> pair in settled)
				Check.AssertEqual(36, pair.Value, $"{pair.Key} 在 1080 日内的结算次数（1080 / 30）");
		}

		private static void TiersAgreeOnSettlementCount()
		{
			Check.AssertEqual(36, Settlements(TimeSpeedTier.Standard, 1080.0), "标准档：1080 真实秒 = 1080 日");
			Check.AssertEqual(36, Settlements(TimeSpeedTier.Fast, 360.0), "第二档：360 真实秒 = 1080 日");
			Check.AssertEqual(36, Settlements(TimeSpeedTier.Fastest, 180.0), "第三档：180 真实秒 = 1080 日");
		}

		private static int Settlements(TimeSpeedTier tier, double realSeconds)
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue, Speed = tier };
			var bus = new GameTimeService(clock);
			int settlements = 0;

			var task = new IntervalTask(0, TimeConstants.ResourceSettlementDays, "Food", "ResourceGrowth", "none", "map", 1);
			task.OnCompleted += () => settlements++;
			bus.Register(task);

			clock.Advance(realSeconds);
			return settlements;
		}

		private static void PauseStopsBus()
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue };
			var bus = new GameTimeService(clock);
			int ticks = 0;

			var task = new IntervalTask(0, TimeConstants.EventRollDays, "evt", "EventTick", "none", "map", 0);
			task.OnCompleted += () => ticks++;
			bus.Register(task);

			clock.Speed = TimeSpeedTier.Paused;
			clock.Advance(300.0);
			Check.AssertEqual(0, ticks, "暂停期间不应派发 tick");

			clock.Speed = TimeSpeedTier.Standard;
			clock.Advance(1.0);
			Check.AssertEqual(1, ticks, "恢复后应继续派发");
		}

		// ────────────────────────── 单位节拍 ──────────────────────────

		private static void UnitBeatsFollowConstants()
		{
			Check.AssertEqual(108, Completions(TimeConstants.UnitMoveDays), "移动节拍：1080 / 10 日");
			Check.AssertEqual(1080, Completions(TimeConstants.UnitAttackDays), "攻击节拍：1080 / 1 日");
		}

		private static int Completions(float intervalDays)
		{
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue, Speed = TimeSpeedTier.Fastest };
			var bus = new GameTimeService(clock);
			int completions = 0;

			var task = new IntervalTask(0, intervalDays, "probe", "Probe", "none", "map", 0);
			task.OnCompleted += () => completions++;
			bus.Register(task);

			for (int second = 0; second < 180; second++)
				clock.Advance(1.0); // 1080 日

			return completions;
		}

		// ────────────────────────── 配置单位自检 ──────────────────────────

		private static void RealConfigDurationsAreDayBased()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			ConfigTables tables = core.Tables;

			foreach (IBuildingConfig building in tables.AllBuildings())
			{
				Check.Assert(building.Duration > 0f && building.Duration <= TimeConstants.MaxPlausibleDays,
					$"Buildings/{building.BuildingId}.Duration={building.Duration} 应落在 (0,360] 日");
				Check.Assert(building.PopulationGrowthInterval == 0 || building.PopulationGrowthInterval <= TimeConstants.MaxPlausibleDays,
					$"Buildings/{building.BuildingId}.PopulationGrowthInterval={building.PopulationGrowthInterval} 应落在 (0,360] 日或 0");
			}

			// 敌方单位不参与训练系统（`WP-3.8`：生成即就绪，Duration 无意义）→ 训练时长基线只约束玩家单位
			foreach (IUnitConfig unit in tables.AllUnits())
			{
				if (unit.IsHostile) continue;

				Check.Assert(unit.Duration > 0f && unit.Duration <= TimeConstants.MaxPlausibleDays,
					$"Units/{unit.UnitId}.Duration={unit.Duration} 应落在 (0,360] 日");
			}

			foreach (ITechNodeConfig node in AllTechNodes(tables))
				Check.Assert(node.Duration >= 0f && node.Duration <= TimeConstants.MaxPlausibleDays,
					$"TechTrees/{node.Id}.Duration={node.Duration} 应落在 [0,360] 日");

			foreach (IEventConfig gameEvent in tables.AllEvents())
				Check.Assert(gameEvent.Duration >= 0 && gameEvent.Duration <= TimeConstants.MaxPlausibleDays,
					$"Events/{gameEvent.EventId}.Duration={gameEvent.Duration} 应落在 [0,360] 日（0=永久）");

			// 反向验证：真实配置里不应再出现"疑似秒口径"的警告
			List<ConfigIssue> suspects = core.ConfigReport.Issues
				.Where(issue => issue.Message.Contains("疑似仍是秒口径"))
				.ToList();
			Check.Assert(suspects.Count == 0,
				"真实配置不应有秒值残留警告：" + string.Join(" | ", suspects.Select(i => i.ToString())));
		}

		private static IEnumerable<ITechNodeConfig> AllTechNodes(ConfigTables tables)
		{
			foreach (string treeId in tables.TreeIds())
			{
				ITechTreeConfig tree = tables.TechTrees.GetTechTreeConfig(treeId);
				if (tree?.Techs == null) continue;
				foreach (ITechNodeConfig node in tree.Techs.Values) yield return node;
			}
		}

		private static void SecondUnitResidueWarnsOnly()
		{
			// 600 秒是典型的"漏改秒值"：校验器应提示，但不能阻断原型启动
			const string secondsLikeBuildings = """
			{
			  "Buildings": {
			    "slow": {
			      "BuildingId": "slow",
			      "Name": "秒值房",
			      "ResourceCost": { "BasicMinerals": 1 },
			      "TerrainRequirements": ["plain"],
			      "TechRequirements": {},
			      "Modifiers": [],
			      "Duration": 600,
			      "Actions": [],
			      "VisionRadius": 1,
			      "IsHousing": false,
			      "PopulationRadius": 0,
			      "PopulationCap": 0,
			      "PopulationGrowthInterval": 0
			    }
			  }
			}
			""";

			// FailOnConfigErrors=true：只有 warning 时不应抛异常
			CoreServices core = ConfigFixtures.BuildCore(
				ConfigFixtures.RealConfigSourceWith("Buildings", secondsLikeBuildings), failOnConfigErrors: true);

			Check.Assert(core != null, "只有 warning 时应能正常装配");
			Check.AssertEqual(0, core.ConfigReport.ErrorCount, "秒值残留不应判 error");
			Check.Assert(core.ConfigReport.Issues.Any(i => i.Level == ConfigIssueLevel.Warning && i.Message.Contains("疑似仍是秒口径")),
				"Duration=600 应给出「疑似仍是秒口径」warning");
		}

		// ────────────────────────── 适配器 ──────────────────────────

		private static void DriverAdvancesOneMonth()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var driver = new ManualTimeDriver(core.Session.Clock);
			int settlements = 0;

			var task = new IntervalTask(0, TimeConstants.ResourceSettlementDays, "probe", "ResourceGrowth", "none", "map", 1);
			task.OnCompleted += () => settlements++;
			core.Time.Register(task);

			driver.Advance(30.0); // 标准档：30 真实秒 = 30 游戏日 = 1 个月

			Check.AssertEqual(30, core.Session.CurrentDay, "推进 1 个月后的 CurrentDay");
			Check.AssertEqual(1, settlements, "月结触发次数");
			Check.AssertEqual(1, core.Time.SubscriberCount, "总线上的任务数");
			Check.AssertEqual("0年2月1日", core.Session.Clock.Format(), "第 30 日的日期显示");

			core.Time.Unregister(task);
			Check.AssertEqual(0, core.Time.SubscriberCount, "注销后应无任务");
		}
	}
}
