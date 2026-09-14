using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>（v0.3 / WP-0.4）地形配置仓库实现（JSON → DTO → 契约）。</summary>
	public class TerrainsConfigRepository : GenericConfigRepository<ITerrainsConfig, TerrainsConfigDto>, ITerrainConfigRepository
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
	}
}
