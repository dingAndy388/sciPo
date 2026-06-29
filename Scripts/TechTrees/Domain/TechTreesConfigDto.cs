using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechTreesConfigDto : ITechTreesConfig
	{
		[JsonProperty("TechTrees")]
		public Dictionary<string, TechTreeConfigDto> TechTreesData { get; set; }

		Dictionary<string, ITechTreeConfig> ITechTreesConfig.TechTrees
			=> TechTreesData?.ToDictionary(kvp => kvp.Key, kvp => (ITechTreeConfig)kvp.Value)
			   ?? new Dictionary<string, ITechTreeConfig>();
	}
}