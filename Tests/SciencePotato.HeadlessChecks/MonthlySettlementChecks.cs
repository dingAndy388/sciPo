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
	/// <item>**存档**：赤字月数与月结任务跨读档保留（不翻倍、不清零）。</item>
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

				// 装配自检：两个需求来源都挂上了（否则"结算器在跑但没人上报需求"极难发现）
				Check.AssertEqual(2, h.Settlement.DemandSourceIds.Count, "应挂上两个需求来源（人口 + 单位维护）");
				Check.Assert(h.Settlement.DemandSourceIds.Contains("population"), "应包含人口维护来源");
				Check.Assert(h.Settlement.DemandSourceIds.Contains("unit"), "应包含单位维护来源");
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
		}

		// ────────────────────────── 夹具 ──────────────────────────

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

		private static Harness NewHarness(bool zeroGrowth = false, int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp39-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			Shorten(source);
			if (zeroGrowth) source.Inject("Resources", ZeroGrowthResources());

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
			// + 单位维护（Units 侧读自己的模板字段）—— 结算器只消费需求，不反向依赖这两个模块
			var settlement = new MonthlySettlementService(
				resources,
				core.Tables.Resources,
				modifier,
				time,
				new IUpkeepDemandSource[]
				{
					new PopulationUpkeepDemandSource(map, core.Tables.Resources.GetResourcesPoolConfig()),
					new UnitMaintenanceUpkeepDemandSource(map, core.Tables.Units),
				},
				store);

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

		/// <summary>资源表：把所有 `BaseGrowth` 清零（产出只来自修正器），其余字段（含 `Settlement` 段）保持真实值。</summary>
		private static string ZeroGrowthResources()
		{
			var resources = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Resources")));
			foreach (JObject entry in resources["Resources"].Cast<JObject>())
				entry["BaseGrowth"] = 0;

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
