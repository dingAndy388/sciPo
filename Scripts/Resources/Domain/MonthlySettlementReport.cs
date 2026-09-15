using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Resources.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `TIME-14`）**一次月度结算的结果**：产出汇总 → 需求 → 扣减 → 赤字，
	/// 四个环节各留一份可读账，而不是只回一个 bool。
	/// <para>为什么报告本身要能按来源/资源拆开：`RES-01` 的问题是"玩家的资源为什么在掉"当年无从回答；
	/// 需求按来源归因（人口 / 每个单位模板）+ 赤字按资源拆分后，UI（M1 调试层）与用例都能直接读账。</para>
	/// </summary>
	public sealed class MonthlySettlementReport
	{
		public string MapId { get; }
		public int OwnerId { get; }
		public float Day { get; }

		/// <summary>产出汇总：资源名 → 本月产出（与 `ResourceGrowth` 任务同一公式；观测口径，不参与扣减）。</summary>
		public IReadOnlyDictionary<string, float> Production { get; }

		/// <summary>需求明细（按来源归因；只含 &gt; 0 的条目）。</summary>
		public IReadOnlyList<UpkeepDemand> Demands { get; }

		public IReadOnlyDictionary<string, float> DemandByResource { get; }
		public IReadOnlyDictionary<string, float> PaidByResource { get; }
		public IReadOnlyDictionary<string, float> DeficitByResource { get; }

		public float TotalDemand { get; }
		public float TotalPaid { get; }
		public float TotalDeficit { get; }

		/// <summary>本次结算后的**连续赤字月数**（0 = 本月收支平衡；`WP-3.10` 据此触发减员评估）。</summary>
		public int ConsecutiveDeficitMonths { get; }

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）本次结算是否**到期做了减员评估**（年边界 + 连续赤字达阈值 + 挂了人口出口）。
		/// <para>为什么要有这个布尔而不是"看 <see cref="PopulationLost"/> 是否为 0"：概率为 0、或期望减员四舍五入后为 0、
		/// 或地图上已经没人都可能让减员人数是 0，但"评估发生过"这件事本身（并因此单方面推进了年度窗口）必须可观测。</para>
		/// </summary>
		public bool DeclineEvaluated { get; }

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）评估窗口的**缺口率 r** = 窗口内累计赤字 / 累计需求（clamp 到 `[0,1]`；未评估 = 0）。
		/// <para>口径：窗口 = 上一次评估以来的 `DeclineIntervalDays` 里的全部月度结算之和 —— 用累计比值而不是"本月缺口"，
		/// 才能让"饿了三年、其中几个月勉强吃饱"仍然算作这次饥荒的缺口。</para>
		/// </summary>
		public float DeficitRatio { get; }

		/// <summary>（v0.3 / WP-3.10 / `C9`）减员概率 `p = 1/(1+e^(−k(r−0.5)))`（未评估 = 0）。</summary>
		public float DeclineProbability { get; }

		/// <summary>（v0.3 / WP-3.10 / `C9`）**本次实际减少的人口**（未评估 = 0；可能因人口不足 / 抖动取整为 0）。</summary>
		public int PopulationLost { get; }

		/// <summary>本月是否入不敷出。</summary>
		public bool IsDeficit => TotalDeficit > 0f;

		public MonthlySettlementReport(
			string mapId,
			int ownerId,
			float day,
			IReadOnlyDictionary<string, float> production,
			IReadOnlyList<UpkeepDemand> demands,
			IReadOnlyDictionary<string, float> demandByResource,
			IReadOnlyDictionary<string, float> paidByResource,
			IReadOnlyDictionary<string, float> deficitByResource,
			int consecutiveDeficitMonths,
			bool declineEvaluated = false,
			float deficitRatio = 0f,
			float declineProbability = 0f,
			int populationLost = 0)
		{
			MapId = mapId;
			OwnerId = ownerId;
			Day = day;
			Production = production ?? new Dictionary<string, float>();
			Demands = demands ?? new List<UpkeepDemand>();
			DemandByResource = demandByResource ?? new Dictionary<string, float>();
			PaidByResource = paidByResource ?? new Dictionary<string, float>();
			DeficitByResource = deficitByResource ?? new Dictionary<string, float>();

			TotalDemand = Demands.Sum(d => d.Amount);
			TotalPaid = PaidByResource.Values.Sum();
			TotalDeficit = DeficitByResource.Values.Sum();
			ConsecutiveDeficitMonths = consecutiveDeficitMonths;

			DeclineEvaluated = declineEvaluated;
			DeficitRatio = deficitRatio;
			DeclineProbability = declineProbability;
			PopulationLost = populationLost;
		}

		/// <summary>某来源本月上报的需求总量（0 = 该来源没有需求）。</summary>
		public float DemandOfSource(string sourceId)
			=> Demands.Where(d => string.Equals(d.Source, sourceId, StringComparison.Ordinal)).Sum(d => d.Amount);

		/// <summary>某来源本月上报的计量单位数（人口数 / 单位个数）。</summary>
		public float UnitsOfSource(string sourceId)
			=> Demands.Where(d => string.Equals(d.Source, sourceId, StringComparison.Ordinal)).Sum(d => d.Units);

		/// <summary>某资源的赤字（0 = 该资源够付）。</summary>
		public float DeficitOf(string resource)
			=> DeficitByResource.TryGetValue(resource, out float value) ? value : 0f;

		/// <summary>本月某资源的产出（0 = 无产出）。</summary>
		public float ProductionOf(string resource)
			=> Production.TryGetValue(resource, out float value) ? value : 0f;

		/// <summary>一行摘要（日志与失败信息里直接可用）。</summary>
		public override string ToString()
			=> $"第 {Day:0} 日 需求 {TotalDemand:0.###} / 已付 {TotalPaid:0.###} / 赤字 {TotalDeficit:0.###}" +
			   $"（连续 {ConsecutiveDeficitMonths} 月）" +
			   (DeclineEvaluated
				   ? $"[减员评估：缺口率 {DeficitRatio:0.###} → p {DeclineProbability:0.###}，减员 {PopulationLost} 人]"
				   : string.Empty) +
			   "：" + string.Join(" | ", Demands.Select(d => d.ToString()));
	}
}
