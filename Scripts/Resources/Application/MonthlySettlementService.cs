using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Units.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Resources.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Resources.Application
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `TIME-14`、`C7`、`C8`、`C12`、`RES-01` 部分、`UNIT-19`）**月度经济结算器**。
	/// <para>改造前的经济只有"各资源各自定时增长 + 上限截断"：没有需求、没有赤字、没有账 ——
	/// 食物消耗（`C7`）、单位维护（`C8`/`UNIT-19`）、产出归因（`C12`）都无处落地。</para>
	/// <para><b>四段式（设计口径 §10.3.4）</b>：</para>
	/// <list type="number">
	/// <item>**产出汇总**：按 `ResourceGrowth` 任务同一公式（资源表 `DependentModifiers` + 修正器 + `BaseGrowth`）
	/// 算出本月产出并记进报告 —— 观测口径，用于"账要对得上"（用例断言它等于资源池增量）。</item>
	/// <item>**需求**：向所有 <see cref="IUpkeepDemandSource"/> 收账（人口 × 3/月 + 单位维护表），按资源汇总。</item>
	/// <item>**扣减**：走资源池的**唯一写入点**（`ResourcesAppService.AddResource`，负数即扣；`ResourcesPool.AddValue`
	/// 自带 <c>[0, limit]</c> 截断 ⇒ 不会出现负库存）。</item>
	/// <item>**赤字记录**：付不出的部分记进报告，并按玩家累计**连续赤字月数**（`WP-3.10` 的减员评估据此触发）。</item>
	/// </list>
	/// <para><b>节拍</b>：月边界 = 每 `TimeConstants.DaysPerMonth`（30）游戏日一次，第 30 日首次结算
	/// （`IntervalTask` 从 0 计时，与 `GrowInterval=30` 的资源任务同一口径）。任务类型 = `MonthlySettlement`，
	/// 快照键 = `MonthlySettlement:none:Settlement`；读档由 `WorldSaveService` 按实体重建并回填进度。</para>
	/// <para><b>与产出任务的次序</b>：产出仍由各资源的 `ResourceGrowth` 任务入账（`WP-2.7` 已接线），本结算器只做
	/// **需求 / 扣减 / 赤字**。二者同为 30 日周期、按注册顺序派发，池子先建（资源首次写入时）→ 结算器后注册，
	/// 因此"先产出、后收账"；即便次序反转也只会把某个月的产出记到下个月，不会算错总量。</para>
	/// <para><b>减员惩罚（`WP-3.10` / `C9` / `RES-01`）</b>：连续赤字 ≥ <c>DeclineThresholdMonths</c>（36 月 = 3 年）
	/// 之后，在年边界（`DeclineIntervalDays` = 360 日，即设计稿的"年评估"）按**年度窗口的缺口率**
	/// `r = Σ赤字 / Σ需求` 算 logistic 减员概率 `p = 1/(1+e^(−k(r−0.5)))`，期望减员 = 总人口 × p × 系数（0.05），
	/// 实际值按 `DeclineJitterRatio` 抖动后交给 <see cref="IPopulationSink"/> **按地块随机**扣人。
	/// 五个参数全部来自 `Resources.json` 的 `Settlement` 段（设计稿"均配置化"）。评估后年度窗口清零、
	/// 连续赤字月数保留 ⇒ 只要还在赤字，每一年都会再评估一次（饿满三年之后不是免疫）。</para>

	/// </summary>
	public sealed partial class MonthlySettlementService
	{
		/// <summary>任务类型（`TaskSnapshot.Type`）。</summary>
		public const string TaskType = "MonthlySettlement";

		/// <summary>业务键（`TaskSnapshot.Id`）：本任务没有实体宿主（`UId` 恒为 `"none"`）。</summary>
		public const string TaskId = "Settlement";

		private readonly ResourcesAppService _resources;
		private readonly IResourcesConfigRepository _configRepo;
		private readonly ModifierAppService _modifier;
		private readonly ITimeService _time;
		private readonly List<IUpkeepDemandSource> _demandSources;
		private readonly ISaveStore _store;

		/// <summary>（v0.3 / WP-3.10）人口出口（可空 = 不做减员：减员评估整体跳过，年度窗口继续累计）。</summary>
		private readonly IPopulationSink _populationSink;

		/// <summary>（v0.8.5 / `WP-4.5`）建筑表（读 `OutputVariance`）+ 占据物查询；可空 = 不浮动。</summary>
		private readonly IBuildingConfigRepository _buildingRepo;
		private readonly IOccupantQuery _occupantQuery;

		/// <summary>（v0.8.7 / `WP-4.6`）单位表（读 `GarrisonHosts`/`GarrisonModifiers`）；可空 = 不算驻扎。</summary>
		private readonly IUnitsRepository _unitConfigs;

		/// <summary>（v0.3 / WP-3.10）减员抖动的随机源（可注入以求受控；缺省 = 系统随机）。</summary>
		private readonly IRandom _random;

		/// <summary>连续赤字月数：键 = `{mapId}_{ownerId}`（读档由 `RestoreSettlement` 覆盖）。</summary>
		private readonly Dictionary<string, int> _deficitMonths = new(StringComparer.Ordinal);

		/// <summary>
		/// （v0.3 / WP-3.10）**年度窗口的累计赤字**：键 = `{mapId}_{ownerId}`，值是上次评估以来各月赤字之和。
		/// <para>为什么不用"本月赤字"当缺口：饿了三年的玩家某个月刚好凑够维护费，本月缺口是 0，但它显然还在这场饥荒里。
		/// 用窗口累计的 赤字/需求 比值当 r 才能反映"这段时间总体缺了多少"。</para>
		/// </summary>
		private readonly Dictionary<string, float> _yearDeficit = new(StringComparer.Ordinal);

		/// <summary>（v0.3 / WP-3.10）**年度窗口的累计需求**（与 <see cref="_yearDeficit"/> 同一窗口，分母）。</summary>
		private readonly Dictionary<string, float> _yearDemand = new(StringComparer.Ordinal);

		/// <summary>各玩家最近一次结算的报告（UI / 调试用）。</summary>
		private readonly Dictionary<string, MonthlySettlementReport> _lastReports = new(StringComparer.Ordinal);

		/// <summary>已挂上月结任务的 `{mapId}_{ownerId}`（幂等标志；读档时由 `RestoreSettlement` 清掉以重挂）。</summary>
		private readonly HashSet<string> _started = new(StringComparer.Ordinal);

		/// <summary>累计结算次数（验收/调试用：证明"每 30 日恰好一次"而不是每帧一次）。</summary>
		public int SettledCount { get; private set; }

		/// <summary>（v0.3 / WP-3.10）累计减员评估次数（验收/调试用：证明"每 360 日最多评估一次"）。</summary>
		public int DeclineEvaluations { get; private set; }

		/// <param name="resources">资源池应用服务（池读取 + 唯一写入点）。</param>
		/// <param name="configRepo">资源表（产出汇总用 `DependentModifiers`/`BaseGrowth`，减员参数读 `Settlement` 段）。</param>
		/// <param name="modifier">修正器读取（产出汇总；可空 = 只算基础产出）。</param>
		/// <param name="time">游戏日节拍总线（挂月结任务）。</param>
		/// <param name="demandSources">需求来源（人口 / 单位维护 / 建筑维护 …）；缺省 = 无需求（只汇报产出）。</param>
		/// <param name="store">统一存档单元（可空 = 赤字月数与年度窗口只在内存）。</param>
		/// <param name="populationSink">人口出口（`WP-3.10` 减员用；可空 = 不评估减员）。</param>
		/// <param name="random">减员抖动的随机源（可空 = 系统随机；测试注入受控随机源以复现数值）。</param>
		public MonthlySettlementService(
			ResourcesAppService resources,
			IResourcesConfigRepository configRepo,
			ModifierAppService modifier,
			ITimeService time,
			IEnumerable<IUpkeepDemandSource> demandSources = null,
			ISaveStore store = null,
			IPopulationSink populationSink = null,
			IBuildingConfigRepository buildingRepo = null,
			IOccupantQuery occupantQuery = null,
			IUnitsRepository unitConfigs = null,
			IRandom random = null)
		{
			_resources = resources;
			_configRepo = configRepo;
			_modifier = modifier;
			_time = time;
			_demandSources = demandSources?.Where(s => s != null).ToList() ?? new List<IUpkeepDemandSource>();
			_store = store;
			_populationSink = populationSink;
			_buildingRepo = buildingRepo;
			_occupantQuery = occupantQuery;
			_unitConfigs = unitConfigs;
			_random = random ?? new SystemRandom(Environment.TickCount);
		}

		/// <summary>（`WP-2.10` 家族）结算完成推送：UI 与后续 `WP-3.10` 的减员评估据此订阅。</summary>
		public event Action<MonthlySettlementReport> Settled;

		/// <summary>已登记的需求来源名（装配自检 / 日志用）。</summary>
		public IReadOnlyList<string> DemandSourceIds => _demandSources.Select(s => s.SourceId).ToList();

		/// <summary>
		/// **挂上月结任务**（幂等：同一 `(mapId, ownerId)` 只登记一个 —— 重复登记会让同一月结算两次）。
		/// </summary>
		/// <param name="initialProgress">初始进度（读档恢复时回填存档进度，避免"读档后月结时间点整体后移"）。</param>
		/// <returns>是否新登记（已登记过返回 false）。</returns>
		public bool StartSettlement(string mapId, int ownerId, float initialProgress = 0f)
		{
			if (string.IsNullOrWhiteSpace(mapId) || _time == null) return false;
			if (!_started.Add(Key(mapId, ownerId))) return false;

			var task = new IntervalTask(initialProgress, TimeConstants.DaysPerMonth, TaskId, TaskType, "none", mapId, ownerId);
			task.OnCompleted += () => Settle(mapId, ownerId);
			_time.Register(task);
			return true;
		}

		/// <summary>最近一次结算报告（没有结算过则为 <c>null</c>）。</summary>
		public MonthlySettlementReport LastReport(string mapId, int ownerId)
			=> _lastReports.TryGetValue(Key(mapId, ownerId), out MonthlySettlementReport report) ? report : null;

		/// <summary>连续赤字月数（0 = 最近一次结算收支平衡，或从未结算）。</summary>
		public int GetConsecutiveDeficitMonths(string mapId, int ownerId)
			=> _deficitMonths.TryGetValue(Key(mapId, ownerId), out int months) ? months : 0;

		/// <summary>
		/// **执行一次结算**（月结任务回调调用；测试与 M1 调试面板也可手动触发）。
		/// <para>产出汇总 → 需求 → 扣减 → 赤字记录，最后推一份 <see cref="MonthlySettlementReport"/>。</para>
		/// </summary>
		public MonthlySettlementReport Settle(string mapId, int ownerId)
		{
			IResourcesPoolConfig config = _configRepo?.GetResourcesPoolConfig();

			IReadOnlyDictionary<string, float> production = SummarizeProduction(mapId, ownerId, config);
			List<UpkeepDemand> demands = CollectDemands(mapId, ownerId);

			var demandByResource = new Dictionary<string, float>(StringComparer.Ordinal);
			// （v0.8.4 / WP-4.4）口粮消耗读 `FoodConsumption`（`base × (1 + ΣPercent)`）：食物消耗类科技的效果点
			string demandResourceName = _configRepo?.GetResourcesPoolConfig()?.Settlement?.DemandResource ?? "Food";
			foreach (UpkeepDemand demand in demands)
			{
				float amount = demand.Amount;
				if (_modifier != null && demand.Resource == demandResourceName)
					amount = Math.Max(0f, _modifier.GetValue(mapId, ownerId, "FoodConsumption", amount));
				demandByResource[demand.Resource] = demandByResource.GetValueOrDefault(demand.Resource) + amount;
			}

			// ③ 扣减：按资源名排序保证可复现（同一档读两次的报告与日志稳定）
			var paidByResource = new Dictionary<string, float>(StringComparer.Ordinal);
			var deficitByResource = new Dictionary<string, float>(StringComparer.Ordinal);
			foreach (string resource in demandByResource.Keys.OrderBy(k => k, StringComparer.Ordinal))
			{
				float demand = demandByResource[resource];
				float available = Math.Max(0f, _resources?.GetOrCreatePool(mapId, ownerId)?.GetValue(resource) ?? 0f);
				float paid = Math.Min(demand, available);

				if (paid > 0f) _resources.AddResource(resource, -paid, mapId, ownerId);

				paidByResource[resource] = paid;
				deficitByResource[resource] = demand - paid;
			}

			// ④ 赤字记录：连续赤字月数是 `WP-3.10`（连续 36 月 → logistic 减员）的输入，必须逐月累加 / 清零
			string key = Key(mapId, ownerId);
			float totalDeficit = deficitByResource.Values.Sum();
			int months = totalDeficit > 0f ? _deficitMonths.GetValueOrDefault(key) + 1 : 0;
			_deficitMonths[key] = months;

			// ⑤ 年度窗口累计（`WP-3.10`）：分母 = 本月需求总量，分子 = 本月赤字总量；
			//    被减员评估消费（评估时清零）。需求为 0 的月份两边都不加，不影响比值。
			_yearDeficit[key] = _yearDeficit.GetValueOrDefault(key) + totalDeficit;
			_yearDemand[key] = _yearDemand.GetValueOrDefault(key) + demands.Sum(d => d.Amount);

			// ⑥ 减员评估（`WP-3.10`）：只在年边界 + 连续赤字达阈值时发生；不做时返回全 0 结果
			DeclineOutcome decline = EvaluateDecline(mapId, ownerId, key, months);

			SettledCount++;
			var report = new MonthlySettlementReport(
				mapId, ownerId, _time?.CurrentDay ?? 0f,
				production, demands, demandByResource, paidByResource, deficitByResource, months,
				decline.Evaluated, decline.DeficitRatio, decline.Probability, decline.PopulationLost);

			_lastReports[key] = report;
			Settled?.Invoke(report);
			return report;
		}

		/// <summary>
		/// （v0.8.7 / `WP-4.2`）**范围效果的产出加成**：遍历自家 `ModifierRange > 0` 的已完工源建筑，
		/// 统计它**覆盖到几个生产建筑**（`Modifiers` 里含该目标名的建筑），每个被覆盖者贡献一次源建筑的加成。
		/// <para>口径（`D112`）：`absolute = Σ_源 (ΣAbsolute_源 × 覆盖数)`、`percent = Σ_源 (ΣPercent_源 × 覆盖数)`
		/// —— "骨笛工坊覆盖 2 块农田" = 两块农田各 +15%（合计 +30%）；owner 级近似（按建筑拆分产出归 `WP-4.13`）。</para>
		/// </summary>
		private void RangedProductionBonus(string mapId, int ownerId, IEnumerable<string> targets, ref float absolute, ref float percent)
		{
			if (_buildingRepo == null || _occupantQuery == null || targets == null) return;

			List<IMapOccupant> own = _occupantQuery.GetOccupants(mapId)
				.Where(o => o != null && o.GetInfo().OwnerId == ownerId
							&& o.GetInfo().Type == OccupantType.Building && o.IsReady)
				.ToList();

			foreach (IMapOccupant source in own)
			{
				IBuildingConfig sourceConfig = _buildingRepo.GetBuildingConfig(source.GetInfo().Id);
				if (sourceConfig == null || sourceConfig.ModifierRange <= 0 || sourceConfig.Modifiers == null) continue;

				float abs = 0f;
				float per = 0f;
				foreach (Modifier modifier in sourceConfig.Modifiers)
				{
					if (!targets.Contains(modifier.Target, StringComparer.OrdinalIgnoreCase)) continue;
					if (string.Equals(modifier.Type, "Percent", StringComparison.OrdinalIgnoreCase)) per += modifier.Value;
					else abs += modifier.Value;
				}
				if (abs == 0f && per == 0f) continue;

				int covered = 0;
				foreach (IMapOccupant target in own)
				{
					if (ReferenceEquals(target, source)) continue;   // 只作用于**其他**建筑
					if (source.GetInfo().Position.DistenceTo(target.GetInfo().Position) > sourceConfig.ModifierRange) continue;

					IBuildingConfig targetConfig = _buildingRepo.GetBuildingConfig(target.GetInfo().Id);
					if (targetConfig?.Modifiers == null) continue;
					if (!targetConfig.Modifiers.Any(m => targets.Contains(m.Target, StringComparer.OrdinalIgnoreCase))) continue;
					covered++;
				}
				if (covered == 0) continue;

				absolute += abs * covered;
				percent += per * covered;
			}
		}

		/// <summary>（v0.8.7 / `WP-4.2`）**相邻同类计数**：有几个自家已完工建筑存在"同 Id 的邻居"。</summary>
		private int AdjacentSameTypeCount(string mapId, int ownerId)
		{
			if (_occupantQuery == null) return 0;

			List<IMapOccupant> own = _occupantQuery.GetOccupants(mapId)
				.Where(o => o != null && o.GetInfo().OwnerId == ownerId
							&& o.GetInfo().Type == OccupantType.Building && o.IsReady)
				.ToList();

			int count = 0;
			foreach (IMapOccupant building in own)
			{
				bool hasTwin = own.Any(other => !ReferenceEquals(other, building)
					&& other.GetInfo().Id == building.GetInfo().Id
					&& other.GetInfo().Position.DistenceTo(building.GetInfo().Position) <= 1);
				if (hasTwin) count++;
			}
			return count;
		}

		/// <summary>
		/// （v0.8.7 / `WP-4.6`）**驻扎加成**：单位在宿主建筑（`GarrisonHosts` 之一）一格内时，把它的
		/// `GarrisonModifiers` 并进产出（`(value + ΣAbsolute) × (1 + ΣPercent)`）。
		/// <para>宿主消失 / 单位走开 ⇒ 下一拍自动不加（无状态、无清理）。</para>
		/// </summary>
		private float ApplyGarrison(string mapId, int ownerId, IEnumerable<string> targets, float value)
		{
			if (_unitConfigs == null || _occupantQuery == null || targets == null) return value;

			List<IMapOccupant> occupants = _occupantQuery.GetOccupants(mapId).Where(o => o != null).ToList();

			foreach (IMapOccupant unit in occupants.Where(o => o.GetInfo().OwnerId == ownerId && o.GetInfo().Type == OccupantType.Unit))
			{
				IUnitConfig config = _unitConfigs.GetUnitConfig(unit.GetInfo().Id);
				if (config?.GarrisonHosts == null || config.GarrisonHosts.Count == 0) continue;
				if (config.GarrisonModifiers == null || config.GarrisonModifiers.Count == 0) continue;

				bool hosted = occupants.Any(b =>
					b.GetInfo().OwnerId == ownerId && b.GetInfo().Type == OccupantType.Building && b.IsReady
					&& config.GarrisonHosts.Contains(b.GetInfo().Id)
					&& b.GetInfo().Position.DistenceTo(unit.GetInfo().Position) <= 1);
				if (!hosted) continue;

				float abs = 0f;
				float per = 0f;
				foreach (Modifier modifier in config.GarrisonModifiers)
				{
					if (!targets.Contains(modifier.Target, StringComparer.OrdinalIgnoreCase)) continue;
					if (string.Equals(modifier.Type, "Percent", StringComparison.OrdinalIgnoreCase)) per += modifier.Value;
					else abs += modifier.Value;
				}
				value = (value + abs) * (1f + per);
			}
			return value;
		}

		/// <summary>
		/// （v0.8.5 / `WP-4.5`）**产出浮动**：`value × (1 + roll)`，`roll ∈ [-lower, +upper]`。
		/// <para>幅度 = 自家已完成建筑里**最大**的 `OutputVariance`（聚落级口径）；`OutputVarianceUpper` / `OutputVarianceLower`
		/// 一旦有修正器就**改写**该侧边界（观星台"上限 +5% / 下限 -1%"，范围效果归 `WP-4.2`）。</para>
		/// </summary>
		private float ApplyOutputVariance(string mapId, int ownerId, float value)
		{
			float variance = MaxOutputVariance(mapId, ownerId);
			if (variance <= 0f) return value;

			float upper = BoundOrDefault(mapId, ownerId, "OutputVarianceUpper", variance);
			float lower = BoundOrDefault(mapId, ownerId, "OutputVarianceLower", variance);
			float roll = (_random.NextFloat() * (upper + lower)) - lower;
			return Math.Max(0f, value * (1f + roll));
		}

		/// <summary>上下限：有对应修正器就**改写**（取修正器值），否则用建筑浮动幅度。</summary>
		private float BoundOrDefault(string mapId, int ownerId, string target, float fallback)
			=> _modifier != null && _modifier.HasTarget(mapId, ownerId, target)
				? Math.Max(0f, _modifier.GetValue(mapId, ownerId, target, 0f))
				: fallback;

		/// <summary>自家**已完成**建筑里最大的 `OutputVariance`（没有建筑/没有表 ⇒ 0 = 不浮动）。</summary>
		private float MaxOutputVariance(string mapId, int ownerId)
		{
			if (_buildingRepo == null || _occupantQuery == null) return 0f;

			float max = 0f;
			foreach (IMapOccupant occupant in _occupantQuery.GetOccupants(mapId))
			{
				if (occupant == null || occupant.GetInfo().OwnerId != ownerId) continue;
				if (occupant.GetInfo().Type != OccupantType.Building || !occupant.IsReady) continue;

				float variance = _buildingRepo.GetBuildingConfig(occupant.GetInfo().Id)?.OutputVariance ?? 0f;
				if (variance > max) max = variance;
			}

			return max;
		}

		/// <summary>
		/// **产出汇总**（观测口径）：与资源 `ResourceGrowth` 任务同一公式 ——
		/// <c>(BaseGrowth + ΣAbsolute) × (1 + ΣPercent)</c>，修正器名取资源表的 `DependentModifiers`。
		/// </summary>
		private IReadOnlyDictionary<string, float> SummarizeProduction(string mapId, int ownerId, IResourcesPoolConfig config)
		{
			var production = new Dictionary<string, float>(StringComparer.Ordinal);
			foreach (IResourceConfig resource in config?.Resources ?? Enumerable.Empty<IResourceConfig>())
			{
				if (resource == null || string.IsNullOrWhiteSpace(resource.Name)) continue;

				float baseGrowth = resource.BaseGrowth;
				float summarized = _modifier != null
					? _modifier.GetValue(mapId, ownerId, resource.DependentModifiers, baseGrowth)
					: baseGrowth;
				// （v0.8.7 / WP-4.2）范围效果：生产建筑被范围内源建筑覆盖 ⇒ 每覆盖一次加一份
				float rangedAbsolute = 0f;
				float rangedPercent = 0f;
				RangedProductionBonus(mapId, ownerId, resource.DependentModifiers, ref rangedAbsolute, ref rangedPercent);
				summarized = (summarized + rangedAbsolute) * (1f + rangedPercent);

				// （v0.8.7 / WP-4.2）振动与波类"相邻同类"加成：每个"有同类邻居"的建筑各算一次
				int adjacentSameType = AdjacentSameTypeCount(mapId, ownerId);
				if (adjacentSameType > 0 && _modifier != null && _modifier.HasTarget(mapId, ownerId, "AdjacentSameTypeBonus"))
					// 相邻同类是"倍率"语义：base 传 1（传 0 会被乘成 0 —— 修正器公式是 (base+Σabs)×(1+Σper)）；
					// 有任一"成对"建筑 ⇒ 整体乘一次（owner 级近似，`D112`）。
					summarized *= _modifier.GetValue(mapId, ownerId, "AdjacentSameTypeBonus", 1f);

				// （v0.8.7 / WP-4.6）驻扎：单位在宿主建筑一格内 ⇒ 其驻扎修正并入产出
				summarized = ApplyGarrison(mapId, ownerId, resource.DependentModifiers, summarized);

				// （v0.8.5 / WP-4.5）产出浮动：幅度取自家建筑的最大 OutputVariance，上下限可被修正器改写
				production[resource.Name] = ApplyOutputVariance(mapId, ownerId, summarized);
			}
			return production;
		}

		/// <summary>向所有需求来源收账（数量 ≤ 0 或资源名为空的条目直接丢弃，避免污染报告）。</summary>
		private List<UpkeepDemand> CollectDemands(string mapId, int ownerId)
		{
			var demands = new List<UpkeepDemand>();
			foreach (IUpkeepDemandSource source in _demandSources)
			{
				IEnumerable<UpkeepDemand> collected = source.CollectUpkeep(mapId, ownerId);
				if (collected == null) continue;

				foreach (UpkeepDemand demand in collected)
					if (demand.Amount > 0f && !string.IsNullOrWhiteSpace(demand.Resource)) demands.Add(demand);
			}
			return demands;
		}

		// ────────────────────────── 减员评估（v0.3 / WP-3.10 / `C9`） ──────────────────────────

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**减员概率** `p = 1/(1+e^(−k(r−0.5)))`（设计稿 logistic 口径）。
		/// <para>`r = 0.5` 时恰好 `p = 0.5`：缺口一半以上开始"多数年份会减员"，缺口全满时 `p → 1`；
		/// `k` 越大越接近阶跃（设计稿 8）。公开成静态方法是为了让 UI/用例直接算这条曲线，不必先造一次结算。</para>
		/// </summary>
		/// <param name="deficitRatio">缺口率 r（0 = 足额付清，1 = 完全付不出；超出区间会先 clamp）。</param>
		/// <param name="k">曲线陡度（设计稿 8）。</param>
		public static float DeclineProbability(float deficitRatio, float k)
			=> 1f / (1f + (float)Math.Exp(-k * (Math.Clamp(deficitRatio, 0f, 1f) - 0.5f)));

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**实际减员人数** = 总人口 × p × 系数，再按 <paramref name="jitter"/> 抖动量级
		/// （设计稿：期望值 = 总人口 × p × 0.05，**实际值随机抖动**）。
		/// </summary>
		/// <param name="population">总人口（评估基数）。</param>
		/// <param name="probability">减员概率 p（见 <see cref="DeclineProbability"/>）。</param>
		/// <param name="factor">期望减员系数（设计稿 0.05）。</param>
		/// <param name="jitter">抖动系数（1 = 不抖；调用方按 `1 ± 幅度` 取值）。</param>
		/// <returns>取整后的减员人数（≥ 0；期望不足半人时可以是 0 —— 小聚落不会被四舍五入成"必减 1 人"）。</returns>
		public static int DeclineLoss(int population, float probability, float factor, float jitter)
		{
			if (population <= 0 || probability <= 0f || factor <= 0f) return 0;

			float expected = population * probability * factor;
			int loss = (int)Math.Round(expected * Math.Max(0f, jitter), MidpointRounding.AwayFromZero);
			return loss < 0 ? 0 : loss;
		}

		/// <summary>
		/// **到期就评估一次减员**（`C9`）：连续赤字 ≥ 阈值 + 落在年边界（`日 % 间隔 == 0`）时才发生。
		/// <list type="number">
		/// <item>**r** = 年度窗口累计赤字 / 累计需求（clamp `[0,1]`；窗口没有需求 → 0）；</item>
		/// <item>**p** = logistic(r)；**期望减员** = 总人口 × p × 系数；</item>
		/// <item>**实际减员** = 期望 × 随机抖动 → 经 <see cref="IPopulationSink"/> 按地块随机扣人；</item>
		/// <item>评估后**年度窗口清零**（下一次评估只看新的一年），但**连续赤字月数不清零** ——
		/// 只要还在赤字，下一个年边界照常评估（"连续 3 年不足 → 年评估"，不是"一辈子只罚一次"）。</item>
		/// </list>
		/// <para>**不评估的三种情形**（都返回全 0 结果，且不动年度窗口）：没挂人口出口 / 阈值或间隔 ≤ 0 /
		/// 未到年边界或连续赤字不足阈值。</para>
		/// </summary>
		private DeclineOutcome EvaluateDecline(string mapId, int ownerId, string key, int months)
		{
			ISettlementConfig settlement = _configRepo?.GetResourcesPoolConfig()?.Settlement;
			int threshold = settlement?.DeclineThresholdMonths ?? SettlementConfigDto.DefaultDeclineThresholdMonths;
			int interval = settlement?.DeclineIntervalDays ?? SettlementConfigDto.DefaultDeclineIntervalDays;

			// 没挂人口出口 = 这次运行时不做减员：窗口继续累计（不清零），避免"事后接上出口却少了三年的账"
			if (_populationSink == null || threshold <= 0 || interval <= 0) return default;

			float day = _time?.CurrentDay ?? 0f;
			if (day <= 0f || day % interval != 0f) return default;      // 只在年边界评估（`TIME-14` 节拍口径）
			if (months < threshold) return default;                     // 连续赤字不足 3 年：不评估（窗口继续攒）

			float demand = _yearDemand.GetValueOrDefault(key);
			float deficit = _yearDeficit.GetValueOrDefault(key);
			float ratio = demand > 0f ? Math.Clamp(deficit / demand, 0f, 1f) : 0f;

			float k = settlement?.DeclineLogisticK ?? SettlementConfigDto.DefaultDeclineLogisticK;
			float factor = settlement?.DeclineExpectedFactor ?? SettlementConfigDto.DefaultDeclineExpectedFactor;
			float jitterRatio = Math.Clamp(
				settlement?.DeclineJitterRatio ?? SettlementConfigDto.DefaultDeclineJitterRatio, 0f, 1f);

			float probability = DeclineProbability(ratio, k);
			int population = _populationSink.GetPopulation(mapId);
			float jitter = 1f + jitterRatio * (2f * _random.NextFloat() - 1f);
			int loss = DeclineLoss(population, probability, factor, jitter);
			int lost = loss > 0 ? _populationSink.ApplyPopulationLoss(mapId, loss) : 0;

			// 窗口清零：新的一年从 0 开始攒（连续赤字月数保持累加，下个年边界照常评估）
			_yearDeficit[key] = 0f;
			_yearDemand[key] = 0f;
			DeclineEvaluations++;

			return new DeclineOutcome(true, ratio, probability, lost);
		}

		/// <summary>（v0.3 / WP-3.10）一次减员评估的结果（内部传球用：报告要把它摊成四个字段）。</summary>
		private readonly struct DeclineOutcome(bool evaluated, float deficitRatio, float probability, int populationLost)
		{
			public readonly bool Evaluated = evaluated;
			public readonly float DeficitRatio = deficitRatio;
			public readonly float Probability = probability;
			public readonly int PopulationLost = populationLost;
		}

		private static string Key(string mapId, int ownerId) => $"{mapId}_{ownerId}";
	}
}
