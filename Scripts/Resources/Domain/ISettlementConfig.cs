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

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**连续赤字触发减员的月数阈值**（设计稿：36 月 = 3 年 = 1080 日）。
		/// <para>= <c>0</c> 或负数时按"永不减员"处理（见 <c>MonthlySettlementService</c>：阈值 ≤ 0 不做评估）。</para>
		/// </summary>
		int DeclineThresholdMonths { get; }

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**减员评估的间隔（游戏日）**（设计稿：年评估 = 360 日）。
		/// <para>评估只在"日序号 % 本间隔 == 0"的边界发生 —— 连续赤字满 36 月不会立刻减员，
		/// 而是等到下一个年边界（`TIME-14` 的节拍口径：所有周期以"每 N 游戏日"表达）。</para>
		/// </summary>
		int DeclineIntervalDays { get; }

		/// <summary>（v0.3 / WP-3.10 / `C9`）logistic 曲线的陡度 k（设计稿 8）：越大越接近"缺口过半即必减员"的阶跃。</summary>
		float DeclineLogisticK { get; }

		/// <summary>（v0.3 / WP-3.10 / `C9`）logistic 概率到"期望减员比例"的系数（设计稿 0.05 = 最多饿死 5%）。</summary>
		float DeclineExpectedFactor { get; }

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**实际减员的随机抖动幅度**（设计稿"实际值随机抖动"；0.25 = 期望值的 ±25%）。
		/// <para>抖动让"同缺口率不必然减同样多的人"：期望值只决定量级，实际值 = 期望 × (1 ± 幅度)。</para>
		/// </summary>
		float DeclineJitterRatio { get; }
	}
}
