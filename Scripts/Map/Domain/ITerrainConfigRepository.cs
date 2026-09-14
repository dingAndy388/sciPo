using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.3 / WP-0.4）地形配置仓库：地形数据来源由 `.tres` 改为 JSON 配置表。
	/// 生成器与地图存档都通过它按 Id 解析地形。
	/// </summary>
	public interface ITerrainConfigRepository
	{
		IEnumerable<ITerrainData> GetAll();

		/// <summary>按地形 Id 取配置；找不到返回 null。</summary>
		ITerrainData GetById(string terrainId);
	}
}
