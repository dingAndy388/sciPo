namespace SciencePotato.Scripts.Resources.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.9）`Config/Resources.json` 根对象的 `Settlement` 段（见 <see cref="ISettlementConfig"/>）。
	/// <para>与 <see cref="ResourcesPoolConfigDto"/> 同处一个 JSON 根：经济参数放一起，填表时不会漏。</para>
	/// </summary>
	public class SettlementConfigDto : ISettlementConfig
	{
		public string DemandResource { get; set; }

		public float PopulationUpkeepPerMonth { get; set; }
	}
}
