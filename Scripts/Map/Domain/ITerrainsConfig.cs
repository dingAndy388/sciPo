using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>（v0.3 / WP-0.4）地形配置表（JSON）的容器契约。</summary>
	public interface ITerrainsConfig
	{
		List<ITerrainData> Terrains { get; }
	}
}
