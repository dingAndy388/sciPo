using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
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

		/// <summary>连续赤字月数：键 = `{mapId}_{ownerId}`（读档由 `RestoreSettlement` 覆盖）。</summary>
		private readonly Dictionary<string, int> _deficitMonths = new(StringComparer.Ordinal);

		/// <summary>各玩家最近一次结算的报告（UI / 调试用）。</summary>
		private readonly Dictionary<string, MonthlySettlementReport> _lastReports = new(StringComparer.Ordinal);

		/// <summary>已挂上月结任务的 `{mapId}_{ownerId}`（幂等标志；读档时由 `RestoreSettlement` 清掉以重挂）。</summary>
		private readonly HashSet<string> _started = new(StringComparer.Ordinal);

		/// <summary>累计结算次数（验收/调试用：证明"每 30 日恰好一次"而不是每帧一次）。</summary>
		public int SettledCount { get; private set; }

		/// <param name="resources">资源池应用服务（池读取 + 唯一写入点）。</param>
		/// <param name="configRepo">资源表（产出汇总用 `DependentModifiers`/`BaseGrowth`）。</param>
		/// <param name="modifier">修正器读取（产出汇总；可空 = 只算基础产出）。</param>
		/// <param name="time">游戏日节拍总线（挂月结任务）。</param>
		/// <param name="demandSources">需求来源（人口 / 单位维护 …）；缺省 = 无需求（只汇报产出）。</param>
		/// <param name="store">统一存档单元（可空 = 赤字月数只在内存）。</param>
		public MonthlySettlementService(
			ResourcesAppService resources,
			IResourcesConfigRepository configRepo,
			ModifierAppService modifier,
			ITimeService time,
			IEnumerable<IUpkeepDemandSource> demandSources = null,
			ISaveStore store = null)
		{
			_resources = resources;
			_configRepo = configRepo;
			_modifier = modifier;
			_time = time;
			_demandSources = demandSources?.Where(s => s != null).ToList() ?? new List<IUpkeepDemandSource>();
			_store = store;
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
			foreach (UpkeepDemand demand in demands)
				demandByResource[demand.Resource] = demandByResource.GetValueOrDefault(demand.Resource) + demand.Amount;

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

			SettledCount++;
			var report = new MonthlySettlementReport(
				mapId, ownerId, _time?.CurrentDay ?? 0f,
				production, demands, demandByResource, paidByResource, deficitByResource, months);

			_lastReports[key] = report;
			Settled?.Invoke(report);
			return report;
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
				production[resource.Name] = _modifier != null
					? _modifier.GetValue(mapId, ownerId, resource.DependentModifiers, baseGrowth)
					: baseGrowth;
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

		private static string Key(string mapId, int ownerId) => $"{mapId}_{ownerId}";
	}
}
