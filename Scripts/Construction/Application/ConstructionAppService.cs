using SciencePotato.Scripts.Common.Application;
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
	public class ConstructionAppService(MapAppService mapAppService, ResourcesAppService resourceAppService, TechTreeAppService techTreeService,BuildingFactory factory, IBuildingRepository buildingRepo, ITimeService time, ModifierAppService modifierAppService)
	{
		//SERVICES
		private MapAppService _map = mapAppService;
		private ResourcesAppService _resource = resourceAppService;
		private TechTreeAppService _tech = techTreeService;
		private BuildingFactory _factory = factory;
		private IBuildingRepository _buildingRepo = buildingRepo;
		private ITimeService _time = time;
		private ModifierAppService _modifier = modifierAppService;

		public void StartConstruction(string mapId,string buildingId, HexCubePosition position)
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
			var terrainRequirements = (from item in config.TerrainRequirements select _map.GetTerrainRequirement(mapId, position, item));

			//CREATE TECH REQUIREMENT
			var techRequirements = (from item in config.TechRequirements select _tech.GetTechTreeRequirement(mapId,item.Key,item.Value.ToList()));

			var modifiers = config.Modifiers;

			//START BUILDING TASK IF MET
			if (contracts.All(c => c.IsConsumable()) && _map.IsClear(mapId, position) && terrainRequirements.All(c=>c.IsMet()) && techRequirements.All(c => c.IsMet()))
			{
				//CONSUME RESOURCE
				contracts.ForEach(c => c.Consume());

				//CREATE BUILDING UNDER CONSTRUCTION
				Building building = _factory.CreateBuilding(buildingId);

				long uid = building.GetInfo().UId;

				//START BUILDING TASK
				LinearTask buildTask = new(0,config.Duration,config.BuildingId,"Construction",false, uid);

				//ADD TO MAP
				_map.SetOccupant(mapId, position, building);

				buildTask.OnCompleted += () => 
				{
					//COMPLETE BUILDING
					_map.GetOccupantByUId(mapId,uid).IsReady = true;

					//APPLY MODIFIERS
					_modifier.AddModifiers(mapId, uid.ToString(), modifiers);

					//DELETE TASK
					_time.Unregister(buildTask);
				};
				//SUBSCRIBE TASK TO TIME SERVICE
				_time.Register(buildTask);
			}
		}

		public LinearTask ResumeConstruction(string mapId,TaskSnapshot snapshot)	
		{
			LinearTask buildTask = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type, snapshot.IsCompleted,snapshot.UId);
			buildTask.OnCompleted += () =>
			{
				//COMPLETE BUILDING
				var building = _map.GetOccupantByUId(mapId, snapshot.UId);
				building.IsReady = true;

				//APPLY MODIFIERS
				_modifier.AddModifiers(mapId, snapshot.UId.ToString(), _buildingRepo.GetBuildingConfig(building.GetInfo().Id).Modifiers);

				//DELETE TASK
				_time.Unregister(buildTask);
			};
			return buildTask;
		}

	}
}	
