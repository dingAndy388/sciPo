using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Application
{ 
	public class ConstructionAppService(MapAppService mapAppService, ResourceAppService resourceAppService, TechTreeAppService techTreeService,BuildingFactory factory, IBuildingRepository buildingRepo)
	{

		//SERVICES
		private MapAppService _map = mapAppService;
		private ResourceAppService _resource = resourceAppService;
		private TechTreeAppService _tech = techTreeService;
		private BuildingFactory _factory = factory;
		private IBuildingRepository _buildingRepo = buildingRepo;

		public void StartConstruction(string MapId,string buildingId, HexCubePosition position)
		{
			var config = _buildingRepo.GetBuildingConfig(buildingId);

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
			var terrainRequirement = _map.GetTerrainRequirement(MapId, position, config.TerrainRequirement);

			//CREATE TECH REQUIREMENT
			var techRequirements = (from item in config.TechRequirements select _tech.GetTechTreeRequirement(item.Key,item.Value.ToList()));


			//CREATE BUILDING AS IMAPOCCUPANT
			if (contracts.All(c => c.IsConsumable()) && _map.IsClear(MapId, position) && terrainRequirement.IsMet() && techRequirements.All(c => c.IsMet()))
			{
				Building building = _factory.CreateBuilding(buildingId);

				//CONSUME RESOURCE
				contracts.ForEach(c => c.Consume());

				//ADD TO MAP
				_map.SetOccupant(MapId,position,building);
			}	
		}
    }
}
