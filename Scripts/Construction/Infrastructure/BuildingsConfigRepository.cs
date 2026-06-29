using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Domain;

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
	}
}