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
	}
}