using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class BuildingFactory(IBuildingConfigRepository repo)
	{
		private IBuildingConfigRepository _repo = repo;

		public Building CreateBuilding(string buildingId, HexCubePosition position, int ownerId)
		{
			var config = _repo.GetBuildingConfig(buildingId);

			// v0.3 / WP-2.5：训练队列上限随配置进建筑实例（默认 5，见 BuildingConfigDto）
			return new Building(position,config.BuildingId, Guid.NewGuid().ToString(), ownerId, config.Name, config.TrainingQueueLimit);
		}
	}
}
