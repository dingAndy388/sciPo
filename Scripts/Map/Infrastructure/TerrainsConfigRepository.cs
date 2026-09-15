using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>（v0.3 / WP-0.4；v0.6.0 / WP-5.3 起同时提供外观参数）地形配置仓库实现（JSON → DTO → 契约）。</summary>
	public class TerrainsConfigRepository : GenericConfigRepository<ITerrainsConfig, TerrainsConfigDto>, ITerrainConfigRepository, IMapAppearanceConfig
	{
		private readonly ITerrainsConfig _config;

		public TerrainsConfigRepository(string json) : base(json)
		{
			base.Load();
			_config = base.Data;
		}

		public IEnumerable<ITerrainData> GetAll()
		{
			return _config?.Terrains ?? new List<ITerrainData>();
		}

		public ITerrainData GetById(string terrainId)
		{
			if (string.IsNullOrWhiteSpace(terrainId)) return null;
			return (_config?.Terrains ?? new List<ITerrainData>()).FirstOrDefault(t => t.Id == terrainId);
		}

		// ────────────── 外观参数（v0.6.0 / WP-5.3）──────────────
		// 表里没写就用内置兜底值：表现层任何情况下都要能算出格位（"配置缺一半 → 地图不显示"是最糟的失败模式）。

		float IMapAppearanceConfig.CellXStep
			=> (_config as IMapAppearanceConfig)?.CellXStep ?? TerrainAppearance.DefaultCellXStep;

		float IMapAppearanceConfig.CellYStep
			=> (_config as IMapAppearanceConfig)?.CellYStep ?? TerrainAppearance.DefaultCellYStep;

		string IMapAppearanceConfig.TerrainSpriteDir
			=> (_config as IMapAppearanceConfig)?.TerrainSpriteDir ?? TerrainAppearance.DefaultSpriteDir;
	}
}
