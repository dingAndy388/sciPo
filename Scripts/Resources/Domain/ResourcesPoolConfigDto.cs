using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Resources.Domain
{
	public class ResourcesPoolConfigDto : IResourcesPoolConfig
	{
		[JsonProperty("Resources")]
		public List<ResourceConfigDto> ResourcesData { get; set; }

		List<IResourceConfig> IResourcesPoolConfig.Resources
			=> ResourcesData?.Select(r => (IResourceConfig)r).ToList()
			   ?? new List<IResourceConfig>();

		/// <summary>
		/// （v0.3 / WP-3.9）`Settlement` 段：需求资源名 + 每人每月需求。
		/// <para>缺段时返回 <c>null</c>（而不是"悄悄补默认值"）：结算器据此跳过人口维护，校验器据此提示。</para>
		/// </summary>
		[JsonProperty("Settlement")]
		public SettlementConfigDto SettlementData { get; set; }

		ISettlementConfig IResourcesPoolConfig.Settlement => SettlementData;
	}
}