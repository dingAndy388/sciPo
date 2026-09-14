using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Construction.Infrastructure
{
	public class BuildingsConfigRepository : GenericConfigRepository<IBuildingsConfig, BuildingsConfigDto>, IBuildingConfigRepository
	{
		private IBuildingsConfig _buildingConfigs;

		public BuildingsConfigRepository(string json) : base(json)
		{
			base.Load();
			_buildingConfigs = base.Data;
		}

		public IBuildingConfig GetBuildingConfig(string buildingId)
		{
			if (_buildingConfigs?.Buildings == null) return null;
			_buildingConfigs.Buildings.TryGetValue(buildingId, out var building);
			return building;
		}

		/// <summary>（v0.3 / WP-1.4）全表视图：供启动期校验（表非空 / Id 一致性 / 引用完整性）使用。</summary>
		public IEnumerable<IBuildingConfig> GetAll()
			=> _buildingConfigs?.Buildings?.Values ?? Enumerable.Empty<IBuildingConfig>();
	}
}