namespace SciencePotato.Scripts.Resources.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `TIME-14`、`RES-01` 部分）**月度经济结算参数**（`Config/Resources.json` 的 `Settlement` 段）。
	/// <para>设计口径（design/resources.md「Food → 基础需求：人口总和 × 3/月」）落在两个字段上：
	/// 需求资源名 + 每人每月需求。原型资源表还没有 Food，故按既有别名口径把设计要求映射到 `Gold`
	/// （与建筑/单位成本、敌方掉落的别名口径一致，见 §18.4「资源词汇别名」）。</para>
	/// </summary>
	public interface ISettlementConfig
	{
		/// <summary>
		/// 需求结算的资源名（与 <c>Resources</c> 段的资源名一致）。
		/// <para>空 = 没有可扣的资源：结算器跳过人口维护（校验器会给 warning，避免"填了不生效"）。</para>
		/// </summary>
		string DemandResource { get; }

		/// <summary>每人每月的需求（设计稿 3 Food/月）；0 = 不向人口收维护费。</summary>
		float PopulationUpkeepPerMonth { get; }
	}
}
