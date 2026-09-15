namespace SciencePotato.Scripts.Resources.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.9）`Config/Resources.json` 根对象的 `Settlement` 段（见 <see cref="ISettlementConfig"/>）。
	/// <para>与 <see cref="ResourcesPoolConfigDto"/> 同处一个 JSON 根：经济参数放一起，填表时不会漏。</para>
	/// </summary>
	public class SettlementConfigDto : ISettlementConfig
	{
		/// <summary>（v0.3 / WP-3.10）减员参数的**设计缺省值**（表里省略字段时用，避免"漏填一项 → 减员静默关掉"）。</summary>
		public const int DefaultDeclineThresholdMonths = 36;

		/// <inheritdoc cref="DefaultDeclineThresholdMonths"/>
		public const int DefaultDeclineIntervalDays = 360;

		/// <inheritdoc cref="DefaultDeclineThresholdMonths"/>
		public const float DefaultDeclineLogisticK = 8f;

		/// <inheritdoc cref="DefaultDeclineThresholdMonths"/>
		public const float DefaultDeclineExpectedFactor = 0.05f;

		/// <inheritdoc cref="DefaultDeclineThresholdMonths"/>
		public const float DefaultDeclineJitterRatio = 0.25f;

		public string DemandResource { get; set; }

		public float PopulationUpkeepPerMonth { get; set; }

		/// <summary>（v0.3 / WP-3.10 / `C9`）连续赤字月数阈值；省略 = 36（设计稿）。</summary>
		public int DeclineThresholdMonths { get; set; } = DefaultDeclineThresholdMonths;

		/// <summary>（v0.3 / WP-3.10 / `C9`）评估间隔（游戏日）；省略 = 360（年评估）。</summary>
		public int DeclineIntervalDays { get; set; } = DefaultDeclineIntervalDays;

		/// <summary>（v0.3 / WP-3.10 / `C9`）logistic 陡度 k；省略 = 8。</summary>
		public float DeclineLogisticK { get; set; } = DefaultDeclineLogisticK;

		/// <summary>（v0.3 / WP-3.10 / `C9`）期望减员系数；省略 = 0.05。</summary>
		public float DeclineExpectedFactor { get; set; } = DefaultDeclineExpectedFactor;

		/// <summary>（v0.3 / WP-3.10 / `C9`）随机抖动幅度；省略 = 0.25。</summary>
		public float DeclineJitterRatio { get; set; } = DefaultDeclineJitterRatio;
	}
}
