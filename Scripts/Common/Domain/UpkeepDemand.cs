using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `TIME-14`、`UNIT-19`）**一条月度维护需求**：某个来源每月要从资源池取走的量。
	/// <para>为什么用"需求记录"而不是"由来源直接扣费"：需求的计算者必然知道库存之外的事实
	/// （人口住在哪几格 = Map 的事实、某单位每月吃多少 = 单位模板的事实），而**扣减 / 赤字记录**是经济侧的职责。
	/// 把需求表达成一条只读记录后，<c>MonthlySettlementService</c> 只依赖本记录与其来源契约，
	/// 不必反向依赖 Map / Units 模块（模块环是 `D38` 家族一直在避的坑）。</para>
	/// </summary>
	/// <param name="resource">要消耗的资源名（与 <c>Config/Resources.json</c> 的 <c>Name</c> 一致）。</param>
	/// <param name="amount">本月需求总量（&lt;= 0 的条目会被结算器忽略）。</param>
	/// <param name="source">归因标签（`population` / `unit:worker` …）—— 报告与 UI 按它拆分。</param>
	/// <param name="units">计量单位数（人数 / 单位个数），只为报告可读性，不参与扣减。</param>
	public readonly struct UpkeepDemand(string resource, float amount, string source, float units = 0f)
	{
		public readonly string Resource = resource;
		public readonly float Amount = amount;
		public readonly string Source = source;
		public readonly float Units = units;

		public override string ToString() => $"{Source}: {Amount:0.###} {Resource}（{Units:0.##} 单位）";
	}

	/// <summary>
	/// （v0.3 / WP-3.9）**维护需求来源**：由"持有状态"的一方实现（Map 侧报人口、Units 侧报单位维护），
	/// 经济侧只消费结果。返回 <c>null</c> 或空集合都表示"本月没有这项需求"。
	/// </summary>
	public interface IUpkeepDemandSource
	{
		/// <summary>归因名（写进报告的 `Source` 字段，便于按来源排查"钱花在哪了"）。</summary>
		string SourceId { get; }

		/// <summary>汇总该玩家本月的维护需求（同一来源可返回多条，例如按单位模板分组）。</summary>
		IEnumerable<UpkeepDemand> CollectUpkeep(string mapId, int ownerId);
	}
}
