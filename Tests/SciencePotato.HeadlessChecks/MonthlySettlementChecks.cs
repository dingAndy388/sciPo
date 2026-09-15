using Newtonsoft.Json.Linq;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Save;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `TIME-14`、`C7`、`C8`、`C12`、`RES-01` 部分、`UNIT-19`）**月度经济结算器**的验收检查。
	/// <para>改造前的经济只有"资源各自定时增长"、没有任何消费方 —— 这次把设计稿 §10.3.4 的四段式
	/// （产出汇总 → 需求 → 扣减 → 赤字记录）落成可断言的链路：</para>
	/// <list type="bullet">
	/// <item>**节拍**：第 30 日首次结算、每 30 日一次（不足月不提前）；</item>
	/// <item>**需求**：人口 × 3/月 + 单位维护表（按来源与模板归因）+ 敌方单位不进经济；</item>
	/// <item>**产出**：汇总值与资源池增量一致（账要对得上，`C12`）；</item>
	/// <item>**扣减 / 赤字**：不足时扣到 0（不为负）、赤字按月累计、恢复盈余即归零（`WP-3.10` 的输入）；</item>
	/// <item>**减员（`WP-3.10`）**：连续赤字 ≥ 36 月的年边界按年度缺口率做 logistic 减员（配置化 + 抖动量级 + 按地块随机落地）；</item>
	/// <item>**存档**：赤字月数、年度窗口与月结任务跨读档保留（不翻倍、不清零）。</item>
	/// </list>
	/// </summary>
	internal static class MonthlySettlementChecks
	{
		private const string MapId = "economy-map";

		public static void RunAll()
		{
			Check.Run("WP-3.9 配置口径：`Settlement`（Gold × 3/月）+ 单位维护表（工人 1 / 剑士 2 / 弓箭手 3，敌方 0）", ConfigDeclaresUpkeep);
			Check.Run("WP-3.9 月结节拍：第 30 日首次结算、每 30 日一次（不足月不提前）", SettlesOnMonthBoundary);
			Check.Run("WP-3.9 需求汇总：人口 × 3/月 + 单位维护（按来源与模板归因）", CollectsPopulationAndUnitDemand);
			Check.Run("WP-3.9 产出汇总：修正器驱动的月产出与资源池增量一致（账对得上）", ProductionSummaryMatchesPool);
			Check.Run("WP-3.9 扣减：恰好扣掉需求，不足则扣到 0（不出现负库存）", DeductsDemandAndNeverGoesNegative);
			Check.Run("WP-3.9 赤字记录：连续赤字逐月累加、恢复盈余即归零", TracksConsecutiveDeficitMonths);
			Check.Run("WP-3.9 敌方单位不进经济：敌人不吃人口维护、不计单位维护", HostilesAreExcluded);
			Check.Run("WP-3.9 幂等：重复挂载只登记一条月结任务（不会一月结两次）", StartIsIdempotent);
			Check.Run("WP-3.9 存档：连续赤字月数与月结任务跨读档保留（不翻倍、不清零）", SettlementStateSurvivesSaveLoad);
			Check.Run("WP-3.9 校验器：`Settlement` 字段与单位维护的分级（未定义资源 / 负值 error、缺段 warning）", ValidatorGuardsSettlementConfig);
			Check.Run("WP-3.10 减员配置：36 月 / 360 日 / k=8 / 0.05 / ±25% 与 logistic 公式", ConfigDeclaresDeclineParameters);
			Check.Run("WP-3.10 减员触发：连续赤字满 3 年后的年边界按缺口率减员（100 人 → 96）", DeclineTriggersOnYearBoundary);
			Check.Run("WP-3.10 减员持续：赤字不停则每个年边界都评估（不是一辈子只罚一次）", DeclineRepeatsWhileDeficitPersists);
			Check.Run("WP-3.10 减员门槛：不满 36 月不减员，第 36 个月的边界才评估", DeclineWaitsForFullThreeYears);
			Check.Run("WP-3.10 减员可关：阈值配 0 = 显式关闭（跑满 4 年不掉人）", DeclineCanBeDisabledByConfig);
			Check.Run("WP-3.10 减员存档：年度窗口（累计赤字 / 需求）跨读档保留", DeclineWindowSurvivesSaveLoad);
			Check.Run("WP-3.10 减员落点：按地块随机扣人、扣空退出候选、绝不扣成负数", MapLossIsRandomAndNeverNegative);
			Check.Run("WP-3.10 建筑维护：Construction 侧新需求来源按模板汇总（填表即生效）", BuildingMaintenanceDemandIsCollected);
		}

		// ────────────────────────── 用例 ──────────────────────────

		/// <summary>真实配置必须写好"维护费从哪来、向谁收"：否则结算器是空转的（填了不生效）。</summary>
		private static void ConfigDeclaresUpkeep()
		{
			Harness h = NewHarness();
			try
			{
				ISettlementConfig settlement = h.Tables.Resources.GetResourcesPoolConfig().Settlement;
				Check.Assert(settlement != null, "真实配置应填写 `Settlement` 段");
				Check.AssertEqual("Gold", settlement.DemandResource, "需求资源应为原型资源别名 Gold（设计稿 Food）");
				Check.AssertEqual(3f, settlement.PopulationUpkeepPerMonth, "每人每月需求应为 3（design/resources.md）");

				Check.AssertEqual(1f, h.Tables.Units.GetUnitConfig("worker").Maintenance["Gold"], "工人维护 1/月（设计稿默认值）");
				Check.AssertEqual(2f, h.Tables.Units.GetUnitConfig("swordsman").Maintenance["Gold"], "剑士（民兵）2/月");
				Check.AssertEqual(3f, h.Tables.Units.GetUnitConfig("archer").Maintenance["Gold"], "弓箭手 3/月");

				foreach (string hostile in new[] { "wolf", "boar", "eagle", "ibex", "crocodile" })
				{
					IUnitConfig config = h.Tables.Units.GetUnitConfig(hostile);
					Check.Assert(config != null && config.IsHostile, $"准备：{hostile} 应是敌方单位");
					Check.AssertEqual(0, config.Maintenance.Count, $"敌方单位 {hostile} 不应有维护费（不进玩家经济）");
				}

				Check.Assert(!h.Core.ConfigReport.HasErrors,
					$"`Settlement` 与维护字段不应引入配置 error：{h.Core.ConfigReport.ToLines()}");

				// 装配自检：三个需求来源都挂上了（否则"结算器在跑但没人上报需求"极难发现）
				Check.AssertEqual(3, h.Settlement.DemandSourceIds.Count, "应挂上三个需求来源（人口 + 单位维护 + 建筑维护）");
				Check.Assert(h.Settlement.DemandSourceIds.Contains("population"), "应包含人口维护来源");
				Check.Assert(h.Settlement.DemandSourceIds.Contains("unit"), "应包含单位维护来源");
				Check.Assert(h.Settlement.DemandSourceIds.Contains("building"), "应包含建筑维护来源（`WP-3.10`）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>月边界 = 每 30 游戏日；第 30 日首次结算（与 `GrowInterval=30` 的资源任务同一口径）。</summary>
		private static void SettlesOnMonthBoundary()
		{
			Harness h = NewHarness();
			try
			{
				Check.Assert(h.Settlement.StartSettlement(MapId, h.OwnerId), "首次挂载月结任务应成功");
				Check.AssertEqual(0, h.Settlement.SettledCount, "挂载后不应立即结算");

				var pushed = new List<MonthlySettlementReport>();
				h.Settlement.Settled += pushed.Add;

				h.Clock.AdvanceDays(29);
				Check.AssertEqual(0, h.Settlement.SettledCount, "不足 30 日不应提前结算");
				Check.AssertEqual(0, pushed.Count, "不足 30 日不应推送结算事件");

				h.Clock.AdvanceDays(1);
				Check.AssertEqual(1, h.Settlement.SettledCount, "第 30 日应恰好结算一次");
				Check.AssertEqual(30f, h.Settlement.LastReport(MapId, h.OwnerId).Day, "报告的日期应为第 30 日");
				Check.AssertEqual(1, pushed.Count, "结算完成应推送一次（UI / `WP-3.10` 据此订阅）");

				h.Clock.AdvanceDays(30);
				Check.AssertEqual(2, h.Settlement.SettledCount, "每 30 日一次（第 60 日第二次）");
				Check.AssertEqual(60f, h.Settlement.LastReport(MapId, h.OwnerId).Day, "报告的日期应为第 60 日");
				Check.AssertEqual(2, pushed.Count, "两次结算推送两次");
				Check.Assert(ReferenceEquals(pushed[1], h.Settlement.LastReport(MapId, h.OwnerId)), "推送的报告应与 `LastReport` 同源");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>需求 = 整图人口 × 3 + 每个玩家单位的模板维护费（按来源与模板归因，敌方单位不进账）。</summary>
		private static void CollectsPopulationAndUnitDemand()
		{
			Harness h = NewHarness();
			try
			{
				h.SeedPopulation(4);
				h.Resources.AddResource("Gold", 1000f, MapId, h.OwnerId);  // 训练单位要有钱（消耗门控读池）
				h.Resources.AddResource("Wood", 1000f, MapId, h.OwnerId);  // 弓箭手还要 30 Wood

				h.SpawnPlayer("worker", h.CellAtDistance(1, h.Site));
				h.SpawnPlayer("archer", h.CellAtDistance(2, h.Site));

				int population = h.Map.GetAllCells(MapId).Sum(cell => cell.Population);
				Check.AssertEqual(2, h.Map.GetOccupants(MapId).Count(occupant => occupant is Unit), "准备：两个玩家单位都已落位");
				Check.AssertEqual(4, population, "准备：聚落 4 人（单位落位消耗的是各自格子上临时补的人）");

				h.Settlement.StartSettlement(MapId, h.OwnerId);
				h.Clock.AdvanceDays(30);

				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(population * 3f, report.DemandOfSource("population"), "人口需求 = 人口 × 3/月");
				Check.AssertEqual((float)population, report.UnitsOfSource("population"), "人口需求要带上人数（报告能解释钱花在哪）");

				Check.AssertEqual(1f, report.DemandOfSource("unit:worker"), "工人维护 1/月（单位表口径）");
				Check.AssertEqual(3f, report.DemandOfSource("unit:archer"), "弓箭手维护 3/月（单位表口径）");
				Check.AssertEqual(population * 3f + 4f, report.TotalDemand, "总需求 = 人口 × 3 + 单位维护之和");

				Check.Assert(!report.IsDeficit, $"1000 Gold 足够付 {report.TotalDemand}：不应判为赤字");
				Check.AssertEqual(report.TotalDemand, report.TotalPaid, "够付时应足额扣款");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>产出汇总必须与资源池增量一致：建筑修正器（School 250 idea/月）驱动的产出要能被结算器读到。</summary>
		private static void ProductionSummaryMatchesPool()
		{
			Harness h = NewHarness(zeroGrowth: true); // 基础产出清零 → 产出只来自修正器，账目干净
			try
			{
				h.Resources.AddResource("Gold", 500f, MapId, h.OwnerId); // 先建池：资源月结任务先于结算器注册
				h.Modifier.AddModifier(MapId, h.OwnerId, "school-uid",
					new Modifier { Target = "IdeaGrowth", Type = "Absolute", Value = 250f });

				h.Settlement.StartSettlement(MapId, h.OwnerId);

				float ideaBefore = h.Pool().GetValue("Idea");
				h.Clock.AdvanceDays(30);
				float ideaAfter = h.Pool().GetValue("Idea");

				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(250f, report.ProductionOf("Idea"), "产出汇总应读到 IdeaGrowth +250（School 250 idea/月）");
				Check.AssertEqual(ideaAfter - ideaBefore, report.ProductionOf("Idea"), "产出汇总应与资源池增量一致（`C12`：账要对得上）");
				Check.AssertEqual(0f, report.ProductionOf("Gold"), "无修正器 + 基础产出 0 → Gold 产出应为 0");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>扣减 = `min(需求, 可用)`：只扣得起多少扣多少，池子扣到 0 也不会变负。</summary>
		private static void DeductsDemandAndNeverGoesNegative()
		{
			Harness h = NewHarness(zeroGrowth: true); // 基础产出清零：池子不会被增长干扰，"账"只看需求侧
			try
			{
				h.SeedPopulation(2);                                      // 需求 = 2 × 3 = 6 Gold/月
				h.Resources.AddResource("Gold", 4f, MapId, h.OwnerId);    // 只付得起 4

				h.Settlement.StartSettlement(MapId, h.OwnerId);
				h.Clock.AdvanceDays(30);

				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(6f, report.TotalDemand, "需求 = 人口 2 × 3");
				Check.AssertEqual(4f, report.TotalPaid, "池里只有 4 → 只扣 4");
				Check.AssertEqual(2f, report.TotalDeficit, "缺口 2 记为赤字");
				Check.AssertEqual(2f, report.DeficitOf("Gold"), "赤字按资源归因（Gold）");
				Check.AssertEqual(0f, h.Pool().GetValue("Gold"), "池子扣到 0（`ResourcesPool` 的截断保证不为负）");
				Check.Assert(h.Settlement.LastReport(MapId, h.OwnerId).IsDeficit, "入不敷出时报告应标记赤字");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>连续赤字月数（`WP-3.10` 减员评估的输入）：逐月累加，恢复盈余即归零。</summary>
		private static void TracksConsecutiveDeficitMonths()
		{
			Harness h = NewHarness(zeroGrowth: true);
			try
			{
				h.SeedPopulation(1); // 需求 = 3 Gold/月，且池里一分钱都没有

				h.Settlement.StartSettlement(MapId, h.OwnerId);
				h.Clock.AdvanceDays(30);
				Check.AssertEqual(1, h.Settlement.GetConsecutiveDeficitMonths(MapId, h.OwnerId), "第 1 个赤字月");

				h.Clock.AdvanceDays(30);
				Check.AssertEqual(2, h.Settlement.GetConsecutiveDeficitMonths(MapId, h.OwnerId), "连续第 2 个赤字月（累加）");

				h.Resources.AddResource("Gold", 100f, MapId, h.OwnerId); // 补足余额
				h.Clock.AdvanceDays(30);

				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.Assert(!report.IsDeficit, "补足余额后本月不应再赤字");
				Check.AssertEqual(0, h.Settlement.GetConsecutiveDeficitMonths(MapId, h.OwnerId), "恢复盈余 → 连续赤字月数归零");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>敌方单位不进玩家经济：不吃人口维护、不计单位维护（`WP-3.8` 的敌人只是地块占据物 + 封锁）。</summary>
		private static void HostilesAreExcluded()
		{
			Harness h = NewHarness();
			try
			{
				h.SeedPopulation(1);
				Unit wolf = h.SpawnHostile("wolf", h.CellAtDistance(2, h.Site));
				Check.Assert(wolf != null && wolf.GetInfo().IsHostile, "准备：地图上应有一个敌方单位");

				h.Settlement.StartSettlement(MapId, h.OwnerId);
				h.Clock.AdvanceDays(30);

				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(3f, report.TotalDemand, "只有 1 人 × 3：敌方单位不产生需求");
				Check.AssertEqual(0f, report.DemandOfSource("unit:wolf"), "敌方单位不应出现在需求明细里");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>幂等：重复挂载不会让同一个月结算两次（任务清单里也只有一条）。</summary>
		private static void StartIsIdempotent()
		{
			Harness h = NewHarness();
			try
			{
				Check.Assert(h.Settlement.StartSettlement(MapId, h.OwnerId), "首次挂载应成功");
				Check.Assert(!h.Settlement.StartSettlement(MapId, h.OwnerId), "重复挂载应被幂等拒绝");

				Check.AssertEqual(1,
					h.Tasks.GetCurrentTasks(MapId).Count(task => task.Type == MonthlySettlementService.TaskType),
					"任务清单里月结任务只有一条");

				h.Clock.AdvanceDays(30);
				Check.AssertEqual(1, h.Settlement.SettledCount, "只结算一次（重复挂载不会翻倍）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>存档：连续赤字月数与月结任务跨读档保留（减员史不清零、任务不翻倍）。</summary>
		private static void SettlementStateSurvivesSaveLoad()
		{
			Harness h = NewHarness(zeroGrowth: true);
			try
			{
				h.SeedPopulation(1); // 需求 3 Gold/月，池里没钱 → 必然赤字
				h.Settlement.StartSettlement(MapId, h.OwnerId);
				h.Clock.AdvanceDays(30);
				Check.AssertEqual(1, h.Settlement.GetConsecutiveDeficitMonths(MapId, h.OwnerId), "准备：已连续赤字 1 个月");

				h.Save.ThenLoad();

				Check.Assert(h.Store.ListSections().Contains(MonthlySettlementService.SectionKey(MapId)),
					"统一存档里应有 `settlement:{mapId}` 分区");
				Check.AssertEqual(1, h.Settlement.GetConsecutiveDeficitMonths(MapId, h.OwnerId),
					"读档后赤字月数应保留（减员史不清零）");
				Check.AssertEqual(1,
					h.Tasks.GetCurrentTasks(MapId).Count(task => task.Type == MonthlySettlementService.TaskType),
					"读档后月结任务仍只有一条（不重复登记）");

				int settledBefore = h.Settlement.SettledCount;
				h.Clock.AdvanceDays(30);
				Check.AssertEqual(settledBefore + 1, h.Settlement.SettledCount, "读档后每 30 日仍结算一次（不翻倍、不漏）");
				Check.AssertEqual(2, h.Settlement.GetConsecutiveDeficitMonths(MapId, h.OwnerId), "赤字月数继续累加（读档不打断）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>配置守卫：`Settlement` 与维护字段必须"错的报错、缺的提示"（静默不生效最难查）。</summary>
		private static void ValidatorGuardsSettlementConfig()
		{
			// ① 缺 `Settlement` 段 → warning（等价于不向人口收维护费，是可解释的配置，不阻断启动）
			CoreServices noSettlement = BuildWith("Resources", json => json.Remove("Settlement"));
			Check.Assert(!noSettlement.ConfigReport.HasErrors, "缺 `Settlement` 段不应判 error（只 warning）");
			Check.Assert(noSettlement.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Warning && issue.Message.Contains("Settlement")),
				$"缺 `Settlement` 段应给出 warning：{noSettlement.ConfigReport.ToLines()}");

			// ② DemandResource 指向未定义的资源 → error（维护费永远扣不到东西）
			CoreServices unknownResource = BuildWith("Resources", json => json["Settlement"]["DemandResource"] = "Food");
			Check.Assert(unknownResource.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.Message.Contains("DemandResource")),
				$"DemandResource 指向未定义的资源应判 error：{unknownResource.ConfigReport.ToLines()}");

			// ③ 人口维护率为负 → error
			CoreServices negativeUpkeep = BuildWith("Resources", json => json["Settlement"]["PopulationUpkeepPerMonth"] = -1);
			Check.Assert(negativeUpkeep.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.Message.Contains("PopulationUpkeepPerMonth")),
				$"人口维护率为负应判 error：{negativeUpkeep.ConfigReport.ToLines()}");

			// ④ 单位维护为负 → error（与 ResourceCost 同一口径）
			CoreServices negativeMaintenance = BuildWith("Units", json => json["Units"]["worker"]["Maintenance"]["Gold"] = -1);
			Check.Assert(negativeMaintenance.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.ToString().Contains("Maintenance")),
				$"单位维护为负应判 error：{negativeMaintenance.ConfigReport.ToLines()}");

			// ⑤ 敌方单位填了维护 → warning（填了不会生效）
			CoreServices hostileUpkeep = BuildWith("Units", json => json["Units"]["wolf"]["Maintenance"]["Gold"] = 2);
			Check.Assert(!hostileUpkeep.ConfigReport.HasErrors, "敌方单位填维护只应 warning、不阻断启动");
			Check.Assert(hostileUpkeep.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Warning && issue.ToString().Contains("Maintenance")),
				$"敌方单位填了维护应给 warning：{hostileUpkeep.ConfigReport.ToLines()}");

			// ⑥ 减员阈值配 0 = 显式关闭 → warning（不是静默：这条配置会关掉 `C9` 的惩罚）
			CoreServices declineOff = BuildWith("Resources", json => json["Settlement"]["DeclineThresholdMonths"] = 0);
			Check.Assert(!declineOff.ConfigReport.HasErrors, "阈值 0（显式关闭减员）只应 warning");
			Check.Assert(declineOff.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Warning && issue.Message.Contains("DeclineThresholdMonths")),
				$"阈值 0 应给 warning：{declineOff.ConfigReport.ToLines()}");

			// ⑦ 减员阈值 / 间隔 / 陡度 / 系数非法 → error（笔误级别：配错就让减员静默失效）
			CoreServices badThreshold = BuildWith("Resources", json => json["Settlement"]["DeclineThresholdMonths"] = -1);
			Check.Assert(badThreshold.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.Message.Contains("DeclineThresholdMonths")),
				$"阈值为负应判 error：{badThreshold.ConfigReport.ToLines()}");

			CoreServices badInterval = BuildWith("Resources", json => json["Settlement"]["DeclineIntervalDays"] = 0);
			Check.Assert(badInterval.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.Message.Contains("DeclineIntervalDays")),
				$"评估间隔为 0 应判 error：{badInterval.ConfigReport.ToLines()}");

			CoreServices badK = BuildWith("Resources", json => json["Settlement"]["DeclineLogisticK"] = 0);
			Check.Assert(badK.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.Message.Contains("DeclineLogisticK")),
				$"陡度 k=0 应判 error：{badK.ConfigReport.ToLines()}");

			CoreServices badJitter = BuildWith("Resources", json => json["Settlement"]["DeclineJitterRatio"] = 2);
			Check.Assert(badJitter.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.Message.Contains("DeclineJitterRatio")),
				$"抖动幅度越界应判 error：{badJitter.ConfigReport.ToLines()}");

			// ⑧ 建筑维护：负值 error、未定义资源 warning（与单位维护同一口径）
			CoreServices badBuildingUpkeep = BuildWith("Buildings", json =>
				SetMaintenance((JObject)json["Buildings"]["camp"], "Gold", -1));
			Check.Assert(badBuildingUpkeep.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Error && issue.ToString().Contains("Maintenance")),
				$"建筑维护为负应判 error：{badBuildingUpkeep.ConfigReport.ToLines()}");

			CoreServices unknownBuildingUpkeep = BuildWith("Buildings", json =>
				SetMaintenance((JObject)json["Buildings"]["camp"], "Food", 1));
			Check.Assert(!unknownBuildingUpkeep.ConfigReport.HasErrors, "建筑维护引用未定义资源只应 warning");
			Check.Assert(unknownBuildingUpkeep.ConfigReport.Issues.Any(issue =>
					issue.Level == ConfigIssueLevel.Warning && issue.ToString().Contains("Maintenance")),
				$"建筑维护引用未定义资源应给 warning：{unknownBuildingUpkeep.ConfigReport.ToLines()}");
		}

		// ────────────────────────── WP-3.10：连续赤字减员（`C9`） ──────────────────────────

		/// <summary>`C9` 的五个减员参数必须全部来自配置（设计稿"均配置化"），公式与设计稿一致。</summary>
		private static void ConfigDeclaresDeclineParameters()
		{
			Harness h = NewHarness();
			try
			{
				ISettlementConfig settlement = h.Tables.Resources.GetResourcesPoolConfig().Settlement;
				Check.Assert(settlement != null, "真实配置应填写 `Settlement` 段");
				Check.AssertEqual(36, settlement.DeclineThresholdMonths, "连续赤字阈值 36 月（3 年，log §9.2 `C9`）");
				Check.AssertEqual(360, settlement.DeclineIntervalDays, "评估间隔 = 年（360 日）");
				Check.AssertEqual(8f, settlement.DeclineLogisticK, "logistic 陡度 k = 8");
				Check.AssertEqual(0.05f, settlement.DeclineExpectedFactor, "期望减员系数 0.05");
				Check.AssertEqual(0.25f, settlement.DeclineJitterRatio, "实际值抖动 ±25%");

				// 公式：`p = 1/(1+e^(−k(r−0.5)))` —— r=0.5 恰好 0.5、越缺越大、缺口全满 ≈ 0.982
				float half = MonthlySettlementService.DeclineProbability(0.5f, 8f);
				float heavy = MonthlySettlementService.DeclineProbability(0.75f, 8f);
				float total = MonthlySettlementService.DeclineProbability(1f, 8f);
				Check.AssertEqual(0.5f, half, "缺口率 0.5 → p 恰好 0.5（logistic 中点）");
				Check.Assert(half < heavy && heavy < total, $"缺口越大减员概率越高：{half:0.###} < {heavy:0.###} < {total:0.###}");
				Check.Assert(Math.Abs(total - 0.982f) < 0.001f, $"缺口全满 → p ≈ 0.982，实际 {total:0.####}");
				Check.AssertEqual(total, MonthlySettlementService.DeclineProbability(2f, 8f), "缺口率超出 [0,1] 先 clamp（>1 按 1 算）");

				// 期望减员 = 总人口 × p × 0.05；实际值按抖动缩放
				Check.AssertEqual(5, MonthlySettlementService.DeclineLoss(100, total, 0.05f, 1f),
					"100 人 × 0.982 × 0.05 ≈ 4.91 → 5 人");
				Check.AssertEqual(4, MonthlySettlementService.DeclineLoss(100, total, 0.05f, 0.75f),
					"抖动 −25% ⇒ 3.68 → 4 人");
				Check.AssertEqual(0, MonthlySettlementService.DeclineLoss(0, total, 0.05f, 1f), "没有人口 ⇒ 减员 0");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`C9`：连续赤字满 36 月的**年边界**按缺口率减员（期望 4.91 人 × 抖动 −25% ⇒ 4 人，落在随机地块上）。</summary>
		private static void DeclineTriggersOnYearBoundary()
		{
			Harness h = NewHarness(zeroGrowth: true, declineRandom: new FixedRandom(true));
			try
			{
				h.Map.AddPopulation(MapId, h.Site, 1, 5000, 100); // 100 人 × 3/月，池里没钱 ⇒ 月月赤字
				h.Settlement.StartSettlement(MapId, h.OwnerId);

				h.Clock.AdvanceDays(1080); // 36 个月，正好落在年边界

				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(36, report.ConsecutiveDeficitMonths, "准备：已连续赤字 36 个月");
				Check.AssertEqual(1, h.Settlement.DeclineEvaluations, "第 360 / 720 日还没满 3 年 ⇒ 只在第 1080 日评估一次");
				Check.Assert(report.DeclineEvaluated, $"年边界 + 连续赤字满 3 年 ⇒ 应做减员评估：{report}");
				Check.Assert(Math.Abs(report.DeficitRatio - 1f) < 0.001f, $"一年一分没付 ⇒ 缺口率 1，实际 {report.DeficitRatio:0.###}");
				Check.Assert(Math.Abs(report.DeclineProbability - 0.982f) < 0.001f, $"p ≈ 0.982，实际 {report.DeclineProbability:0.####}");
				Check.AssertEqual(4, report.PopulationLost, "100 × 0.982 × 0.05 × 0.75 ≈ 3.68 → 4 人");
				Check.AssertEqual(96, h.Map.GetTotalPopulation(MapId), "地图人口真的少了 4 人");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`C9`：只要还在赤字，**下一个年边界照常评估**（评估后只清年度窗口，不清连续赤字月数）。</summary>
		private static void DeclineRepeatsWhileDeficitPersists()
		{
			Harness h = NewHarness(zeroGrowth: true, declineRandom: new FixedRandom(true));
			try
			{
				h.Map.AddPopulation(MapId, h.Site, 1, 5000, 100);
				h.Settlement.StartSettlement(MapId, h.OwnerId);

				h.Clock.AdvanceDays(1080);
				int afterFirst = h.Map.GetTotalPopulation(MapId);
				Check.AssertEqual(96, afterFirst, "准备：第一次评估减 4 人");

				h.Clock.AdvanceDays(360); // 第二个年边界：窗口从 0 重新攒，但赤字没停 ⇒ 再评估
				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(2, h.Settlement.DeclineEvaluations, "每满一年评估一次（不是一辈子只罚一次）");
				Check.Assert(report.DeclineEvaluated && report.PopulationLost > 0, $"第二年应继续减员：{report}");
				Check.Assert(h.Map.GetTotalPopulation(MapId) < afterFirst, "人口继续下降（饿满三年之后不是免疫）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`C9`：连续赤字**不满 36 月不减员**（年边界到了也等着）；第 36 个月一到就评估。</summary>
		private static void DeclineWaitsForFullThreeYears()
		{
			Harness h = NewHarness(zeroGrowth: true, declineRandom: new FixedRandom(true));
			try
			{
				h.Map.AddPopulation(MapId, h.Site, 1, 5000, 100);
				h.Settlement.StartSettlement(MapId, h.OwnerId);

				h.Clock.AdvanceDays(1050); // 35 个月：跨过第 360 / 720 日两个年边界，但都没满 3 年
				MonthlySettlementReport early = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(35, early.ConsecutiveDeficitMonths, "准备：已连续赤字 35 个月");
				Check.AssertEqual(0, h.Settlement.DeclineEvaluations, "不满 36 月不做评估");
				Check.Assert(!early.DeclineEvaluated, $"未评估的月份不应标记减员：{early}");
				Check.AssertEqual(0, early.PopulationLost, "未评估 ⇒ 不减员");
				Check.AssertEqual(100, h.Map.GetTotalPopulation(MapId), "人口不变");

				h.Clock.AdvanceDays(30); // 第 36 个月（第 1080 日）→ 评估
				Check.AssertEqual(1, h.Settlement.DeclineEvaluations, "第 36 个月的边界应评估一次");
				Check.AssertEqual(96, h.Map.GetTotalPopulation(MapId), "评估后减 4 人（与不中断的路径一致）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>阈值配 0 = 显式关闭减员（校验器给 warning 而不是静默）：跑满 4 年也不掉人。</summary>
		private static void DeclineCanBeDisabledByConfig()
		{
			Harness h = NewHarness(zeroGrowth: true, declineRandom: new FixedRandom(true),
				mutateResources: json => json["Settlement"]["DeclineThresholdMonths"] = 0);
			try
			{
				h.Map.AddPopulation(MapId, h.Site, 1, 5000, 100);
				h.Settlement.StartSettlement(MapId, h.OwnerId);

				h.Clock.AdvanceDays(1440); // 4 年

				Check.AssertEqual(0, h.Settlement.DeclineEvaluations, "阈值 0 ⇒ 永不评估");
				Check.AssertEqual(100, h.Map.GetTotalPopulation(MapId), "不减员：人口保持 100");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`C9` + 存档：年度窗口（累计赤字 / 需求）跨读档保留 —— 否则反复读档就能把缺口率压小。</summary>
		private static void DeclineWindowSurvivesSaveLoad()
		{
			Harness h = NewHarness(zeroGrowth: true, declineRandom: new FixedRandom(true));
			try
			{
				h.Map.AddPopulation(MapId, h.Site, 1, 5000, 100);
				h.Settlement.StartSettlement(MapId, h.OwnerId);
				h.Clock.AdvanceDays(300); // 10 个月全赤字（累计缺口 3000 / 需求 3000）

				h.Save.ThenLoad();
				Check.AssertEqual(100, h.Map.GetTotalPopulation(MapId), "准备：人口应跨读档保留");
				Check.AssertEqual(10, h.Settlement.GetConsecutiveDeficitMonths(MapId, h.OwnerId), "准备：赤字月数保留");

				JObject dto = JObject.Parse(h.Store.ReadSection(MonthlySettlementService.SectionKey(MapId)));
				string key = $"{MapId}_{h.OwnerId}";
				Check.AssertEqual(3000f, dto["YearDeficit"]?[key]?.ToObject<float>() ?? -1f,
					"年度窗口的累计赤字应落盘（10 月 × 300）");
				Check.AssertEqual(3000f, dto["YearDemand"]?[key]?.ToObject<float>() ?? -1f,
					"年度窗口的累计需求应落盘（缺口率的分母）");

				h.Clock.AdvanceDays(780); // 读档后继续到第 1080 日
				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.Assert(report.DeclineEvaluated, $"读档不应打断减员评估：{report}");
				Check.AssertEqual(4, report.PopulationLost, "窗口与赤字史跨读档保留 ⇒ 减员人数与不读档一致（4 人）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`C9` 的"按地块随机减员"落在领域层：随机挑格、扣空退出候选、绝不扣成负数。</summary>
		private static void MapLossIsRandomAndNeverNegative()
		{
			List<MapCell> Seed()
			{
				var created = new List<MapCell>();
				for (int q = 0; q < 3; q++)
				{
					var cell = new MapCell(new HexCubePosition(q, 0));
					cell.SetPopulation(2);
					created.Add(cell);
				}
				return created;
			}

			Map Build(List<MapCell> cells)
			{
				var built = new Map(20260917, 3, 1, "loss-map");
				foreach (MapCell cell in cells) built.SetCell(cell.Position, cell);
				return built;
			}

			// ① 恒挑索引 0 ⇒ 顺序确定：先把 (0,0) 扣空，其余两格不受影响
			List<MapCell> ordered = Seed();
			Check.AssertEqual(2, Build(ordered).ApplyPopulationLoss(2, new FixedRandom(true)), "应恰好扣 2 人");
			Check.AssertEqual(0, ordered[0].Population, "恒挑索引 0 ⇒ 先把第一格扣空");
			Check.AssertEqual(2, ordered[1].Population, "没被挑中的格子不受影响");
			Check.AssertEqual(2, ordered[2].Population, "没被挑中的格子不受影响");

			// ② 循环索引（2 → 1 → 0）⇒ 三格各 −1：人口是**散着掉**的，不是永远从第一格扣
			List<MapCell> spread = Seed();
			Map map = Build(spread);
			Check.AssertEqual(3, map.ApplyPopulationLoss(3, new CyclingRandom(2, 1, 0)), "三格各扣 1 人");
			Check.Assert(spread.All(cell => cell.Population == 1), "三格人口都变成 1（随机分散）");

			// ③ 要的比有的多 ⇒ 全扣光、返回实际值、绝不出现负人口
			Check.AssertEqual(3, map.ApplyPopulationLoss(99, new FixedRandom(true)), "人口不足时返回实际扣除数");
			Check.Assert(spread.All(cell => cell.Population == 0), "要的比有的多 ⇒ 清零，不出现负人口");

			// ④ 没人了 ⇒ 0（不是异常）
			Check.AssertEqual(0, map.ApplyPopulationLoss(5, new FixedRandom(true)), "地图上没人时减员返回 0");
		}

		/// <summary>`WP-3.10` 建筑维护：新增第三条需求来源（Construction 侧），**填表即生效**、数值不臆造。</summary>
		private static void BuildingMaintenanceDemandIsCollected()
		{
			// ① 真实表：建筑维护全部留空（设计稿只定义了单位维护数值）
			Harness plain = NewHarness();
			try
			{
				Check.Assert(plain.Tables.Buildings.GetAll().All(config => config.Maintenance.Count == 0),
					"真实建筑表的维护费应全部留空（机制在、数值不臆造）");
				Check.AssertEqual("building", BuildingMaintenanceUpkeepDemandSource.SourceName, "归因前缀");
			}
			finally { Cleanup(plain.Dir); }

			// ② 填上维护费（营地 5 Gold/月）⇒ 两座自己的营地 = 10 Gold/月，归因到 `building:camp`
			Harness h = NewHarness(mutateBuildings: json =>
				SetMaintenance((JObject)json["Buildings"]["camp"], "Gold", 5));
			try
			{
				Check.AssertEqual(5f, h.Tables.Buildings.GetBuildingConfig("camp").Maintenance["Gold"],
					"建筑表应读到维护费（与单位维护同构的字段）");

				h.SeedPopulation(1);
				h.Settlement.StartSettlement(MapId, h.OwnerId);

				var factory = new BuildingFactory(h.Tables.Buildings);
				HexCubePosition second = h.CellAtDistance(1, h.Site);
				HexCubePosition third = h.CellAtDistance(2, h.Site);
				Check.Assert(h.Map.PlaceBuilding(MapId, h.Site, factory.CreateBuilding("camp", h.Site, h.OwnerId, isReady: true)),
					"准备：第一座营地应落位");
				Check.Assert(h.Map.PlaceBuilding(MapId, second, factory.CreateBuilding("camp", second, h.OwnerId, isReady: true)),
					"准备：第二座营地应落位");
				h.Map.PlaceBuilding(MapId, third, factory.CreateBuilding("camp", third, 2, isReady: true)); // 别人的营地

				h.Clock.AdvanceDays(30);

				MonthlySettlementReport report = h.Settlement.LastReport(MapId, h.OwnerId);
				Check.AssertEqual(10f, report.DemandOfSource("building:camp"), "两座自己的营地 × 5 Gold/月（别人的不算）");
				Check.AssertEqual(13f, report.TotalDemand, "人口 3 + 建筑维护 10");
			}
			finally { Cleanup(h.Dir); }
		}

		// ────────────────────────── 夹具 ──────────────────────────

		/// <summary>
		/// 给建筑条目填一条维护费（表里**可能还没有 `Maintenance` 字段** —— 设计稿尚未定义建筑维护数值，
		/// 所以用例要能"从现在开始填"，不能假设字段已存在）。
		/// </summary>
		private static void SetMaintenance(JObject entry, string resource, float amount)
		{
			entry["Maintenance"] ??= new JObject();
			entry["Maintenance"][resource] = amount;
		}

		/// <summary>读真实表 → 改一处 → 装配一遍（容错模式：便于断言 error/warning 分级）。</summary>
		private static CoreServices BuildWith(string tableName, Action<JObject> mutate)
		{
			var json = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath(tableName)));
			mutate(json);

			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			source.Inject(tableName, json.ToString());

			return ConfigFixtures.BuildCore(source, failOnConfigErrors: false);
		}

		/// <summary>结算器周边的一整套装配（存档单元 store 化：`settlement:` 分区与任务重建都能验）。</summary>
		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public GameClock Clock;
			public GameSession Session;
			public MapAppService Map;
			public GameTimeService Time;
			public TaskRepository Tasks;
			public SystemFileSystem FileSystem;
			public JsonSaveStore Store;
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public MonthlySettlementService Settlement;
			public UnitsAppService Units;
			public CoreServices Core;
			public ConfigTables Tables;
			public SaveHelper Save;
			public HexCubePosition Site;

			public HexCubePosition CellAtDistance(int distance, HexCubePosition from)
				=> Map.GetAllCells(MapId).Select(cell => cell.Position).First(pos => pos.DistenceTo(from) == distance);

			/// <summary>在聚落中心放 <paramref name="amount"/> 人（cap 50，避免人口增长任务把数字改乱）。</summary>
			public void SeedPopulation(int amount) => Map.AddPopulation(MapId, Site, 1, 50, amount);

			/// <summary>资源池快照（每次调用都按存档分区重新读 —— 与游戏里的读取路径一致）。</summary>
			public ResourcesPool Pool() => Resources.GetOrCreatePool(MapId, OwnerId);

			/// <summary>走训练路径放一个玩家单位（先在该格补 1 人：训练完成要扣人口）。</summary>
			public Unit SpawnPlayer(string unitId, HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Units.CreateUnit(MapId, unitId, position, OwnerId);
				Clock.AdvanceDays(3); // `Shorten` 后的训练时长

				MapOccupantInfo? info = Map.GetOccupantInfo(MapId, position);
				Check.Assert(info.HasValue, $"准备：{unitId} 应落在 ({position.q},{position.r})");
				return (Unit)Map.FindOccupantByUId(MapId, info.Value.UId);
			}

			/// <summary>直接放置一个敌方单位（与 `EnemySpawner` 同一路径：绕过训练系统与玩家经济）。</summary>
			public Unit SpawnHostile(string unitId, HexCubePosition position)
			{
				Unit unit = new UnitFactory(Tables.Units).CreateUnit(unitId, position, 0);
				Map.SetOccupant(MapId, position, unit);
				return unit;
			}
		}

		/// <summary>存档/读档的一对小助手：让用例写成 `h.Save.ThenLoad()`。</summary>
		private sealed class SaveHelper(WorldSaveService service, string mapId)
		{
			public void ThenSave() => service.SaveWorld(mapId);

			public void ThenLoad()
			{
				service.SaveWorld(mapId);
				service.LoadWorld(mapId, 1);
			}
		}

		private static Harness NewHarness(bool zeroGrowth = false, int ownerId = 1,
			IRandom declineRandom = null, Action<JObject> mutateBuildings = null, Action<JObject> mutateResources = null)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp39-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			Shorten(source);
			if (zeroGrowth || mutateResources != null) source.Inject("Resources", AdjustResources(zeroGrowth, mutateResources));
			if (mutateBuildings != null)
			{
				var buildings = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
				mutateBuildings(buildings);
				source.Inject("Buildings", buildings.ToString());
			}

			var mapRepository = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildCore(source, mapRepository: mapRepository);

			GameClock clock = core.Session.Clock;
			clock.MaxDaysPerAdvance = int.MaxValue; // `D28`
			var session = new GameSession(core.Session.Maps, clock, "wp39-session");

			// 统一存档单元：一个会话一份文件（`settlement:{mapId}` 分区与任务重建都靠它）
			var fileSystem = new SystemFileSystem();
			var store = new JsonSaveStore(fileSystem, Path.Combine(dir, "world.save"));

			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"), store);
			var time = new GameTimeService(clock, tasks);
			var bus = new DomainEventBus();

			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_"), store),
				core.Tables.Resources,
				time,
				new ModifierRepository(Path.Combine(dir, "mod_"), store));
			var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(dir, "mod_"), store));
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees, store),
				core.Tables.TechTrees,
				resources,
				modifier,
				time,
				bus);
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_"), store));

			var map = core.Map;
			var construction = new ConstructionAppService(
				map, resources, tech, new BuildingFactory(core.Tables.Buildings),
				core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(
				map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus);

			// 需求来源：人口维护（经济侧规则，参数读 `Resources.json` 的 `Settlement` 段）
			// + 单位维护（Units 侧读自己的模板字段）+ 建筑维护（Construction 侧读建筑表的 `Maintenance`，`WP-3.10`）
			// —— 结算器只消费需求，不反向依赖这三个模块
			var settlement = new MonthlySettlementService(
				resources,
				core.Tables.Resources,
				modifier,
				time,
				new IUpkeepDemandSource[]
				{
					new PopulationUpkeepDemandSource(map, core.Tables.Resources.GetResourcesPoolConfig()),
					new UnitMaintenanceUpkeepDemandSource(map, core.Tables.Units),
					new BuildingMaintenanceUpkeepDemandSource(map, core.Tables.Buildings),
				},
				store,
				populationSink: map,                     // `WP-3.10`：减员经地图按地块扣人（`MapAppService : IPopulationSink`）
				random: declineRandom ?? new SystemRandom(20260917)); // 夹具给固定种子 ⇒ 减员抖动可复现

			var clockRepo = new FileClockRepository(Path.Combine(dir, "clock_"), store);
			var worldSave = new WorldSaveService(
				session, map, time, clockRepo, tasks, construction, units, tech, resources,
				fog: fog, store: store, settlement: settlement);

			// 全图铺平原：用例里的裸坐标不应被 Voronoi 水地形挡住
			map.GenerateMap(20260917, 8, 8, MapId);
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				map.SetTerrain(MapId, cell.Position, plain);

			HexCubePosition site = map.GetAllCells(MapId)
				.Select(cell => cell.Position).First(pos => pos.q == 4 && pos.r == 4);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = clock,
				Session = session,
				Map = map,
				Time = time,
				Tasks = tasks,
				FileSystem = fileSystem,
				Store = store,
				Resources = resources,
				Modifier = modifier,
				Settlement = settlement,
				Units = units,
				Core = core,
				Tables = core.Tables,
				Save = new SaveHelper(worldSave, MapId),
				Site = site,
			};
		}

		/// <summary>
		/// 资源表：<paramref name="zeroGrowth"/> 时把所有 `BaseGrowth` 清零（产出只来自修正器），
		/// 再应用用例的 <paramref name="mutate"/>（例如改减员阈值）；其余字段（含 `Settlement` 段）保持真实值。
		/// </summary>
		private static string AdjustResources(bool zeroGrowth, Action<JObject> mutate)
		{
			var resources = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Resources")));
			if (zeroGrowth)
				foreach (JObject entry in resources["Resources"].Cast<JObject>())
					entry["BaseGrowth"] = 0;

			mutate?.Invoke(resources);
			return resources.ToString();
		}

		/// <summary>建筑/升级 2 日、玩家单位 3 日（其余字段与真实表一致：维护费与生成字段都不动）。</summary>
		private static void Shorten(InMemoryConfigSource source)
		{
			var buildings = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (JProperty entry in ((JObject)buildings["Buildings"]).Properties())
			{
				entry.Value["Duration"] = 2;
				entry.Value["UpgradeDuration"] = 2;
			}
			source.Inject("Buildings", buildings.ToString());

			var units = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));
			foreach (JProperty entry in ((JObject)units["Units"]).Properties())
			{
				if (entry.Value["IsHostile"]?.ToObject<bool>() == true) continue; // 敌方单位不训练，保持 0
				entry.Value["Duration"] = 3;
			}
			source.Inject("Units", units.ToString());
		}

		private static void Cleanup(string dir)
		{
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch (IOException)
			{
				// 临时目录清理失败不影响验收结果
			}
		}
	}
}
