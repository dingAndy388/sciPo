using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>（v0.3 / WP-0.4）地形配置表根对象：{ "Terrains": [ ... ] }。</summary>
	public class TerrainsConfigDto : ITerrainsConfig
	{
		[JsonProperty("Terrains")]
		public List<TerrainConfigDto> TerrainsData { get; set; }

		List<ITerrainData> ITerrainsConfig.Terrains
			=> TerrainsData?.Select(t => (ITerrainData)t).ToList() ?? new List<ITerrainData>();
	}
}
