using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechTreeConfigDto : ITechTreeConfig
	{
		public Dictionary<string, TechNodeConfigDto> Techs { get; set; }

		Dictionary<string, ITechNodeConfig> ITechTreeConfig.Techs
			=> Techs?.ToDictionary(kvp => kvp.Key, kvp => (ITechNodeConfig)kvp.Value)
			   ?? new Dictionary<string, ITechNodeConfig>();
	}
}