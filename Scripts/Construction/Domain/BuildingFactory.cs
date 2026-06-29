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

			return new Building(position,config.BuildingId, Guid.NewGuid().ToString(), ownerId, config.Name);
		}
	}
}
