using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class BuildingsConfigDto : IBuildingsConfig
	{
		[JsonProperty("Buildings")]
		public Dictionary<string, BuildingConfigDto> BuildingsData { get; set; }

		Dictionary<string, IBuildingConfig> IBuildingsConfig.Buildings
			=> BuildingsData?.ToDictionary(kvp => kvp.Key, kvp => (IBuildingConfig)kvp.Value)
			   ?? new Dictionary<string, IBuildingConfig>();
	}
}