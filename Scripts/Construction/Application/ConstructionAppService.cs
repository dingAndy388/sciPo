using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Application
{
	public class ConstructionAppService(MapAppService mapAppService, ResourceAppService resourceAppService,BuildingFactory factory, IBuildingRepository buildingRepo)
	{
		private MapAppService _map = mapAppService;
		private ResourceAppService _resource = resourceAppService;
		private BuildingFactory _factory = factory;
		private IBuildingRepository _buildingRepo = buildingRepo;

		public void Build(string MapID,string buildingID, HexCubePosition position)
		{
			var config = _buildingRepo.GetBuildingConfig(buildingID);

			//CREATE CONSUMPTION
			var consumptions = (from item in config.ResourceCost
									select new Consumption(item.Key, item.Value)).ToList();
			//CREATE CONSUMPTION CONTRACT
			List<IConsumable> contracts =
			[
				.. from item in consumptions
								   select _resource.CreateResourceConsumption(item),
			];

			//CREATE TERRAIN REQUIREMENT
			var terrainRequirement = _map.GetTerrainRequirement(MapID, position, config.TerrainRequirement);

			//CREATE BUILDING IMAPOCCUPANT
			if (contracts.All(c=>c.IsConsumable()) && _map.IsClear(MapID, position)&&terrainRequirement.IsMet())
			{
				Building building = _factory.CreateBuilding(buildingID);

				//CONSUME RESOURCE
				contracts.ForEach(c => c.Consume());

				//ADD TO MAP
				_map.SetOccupant(MapID,position,building);
			}	
		}
	}
}
