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
			int consecutiveDeficitMonths)
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
			   $"（连续 {ConsecutiveDeficitMonths} 月）：" + string.Join(" | ", Demands.Select(d => d.ToString()));
	}
}
