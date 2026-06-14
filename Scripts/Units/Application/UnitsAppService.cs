using Godot;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Units.Application
{
	public class UnitsAppService(MapAppService mapApp, TechTreeAppService techTreeApp, ResourcesAppService resourcesApp, ITimeService timeService, IUnitsRepository repo, UnitFactory factory)
	{
		private MapAppService _map = mapApp;
		private TechTreeAppService _tech = techTreeApp;
		private ResourcesAppService _resources = resourcesApp;
		private IUnitsRepository _repo = repo;
		private ITimeService _time = timeService;
		private UnitFactory _factory = factory;

		public void CreateUnit(string mapId, string unitId, HexCubePosition position)
		{
			var config = _repo.GetUnitConfig(unitId);

			//CREATE CONSUMPTION
			var consumptions = (from item in config.ResourceCost
								select new Consumption(item.Key, item.Value)).ToList();
			//CREATE CONSUMPTION CONTRACT
			List<IConsumable> contracts =
			[
				.. from item in consumptions select _resources.CreateResourceConsumption(item),
			];

			//CREATE TERRAIN REQUIREMENT
			var terrainRequirements = (from item in config.TerrainRequirements select _map.GetTerrainRequirement(mapId, position, item));

			//CREATE TECH REQUIREMENT
			var techRequirements = (from item in config.TechRequirements select _tech.GetTechTreeRequirement(mapId, item.Key, item.Value.ToList()));

			//START TRAINING TASK IF MET ALL REQUIREMENTS
			if (contracts.All(c => c.IsConsumable()) && _map.IsClear(mapId, position) && terrainRequirements.All(c => c.IsMet()) && techRequirements.All(c => c.IsMet()))
			{
				//CONSUME RESOURCE
				contracts.ForEach(c => c.Consume());

				//CREATE UNIT UNDER CONSTRUCTION
				Unit unit = _factory.CreateUnit(unitId);

				long uid = unit.GetInfo().UId;

				//START UNIT TASK
				LinearTask trainingTask = new(0, config.Duration, config.UnitId, "Training", false, uid);

				//ADD TO MAP
				_map.SetOccupant(mapId, position, unit);

				trainingTask.OnCompleted += () =>
				{
					//COMPLETE TRAINING
					_map.GetOccupantByUId(mapId, uid).IsReady = true;
				};
				//SUBSCRIBE TASK TO TIME SERVICE
				_time.Register(trainingTask);
			}
		}

		public LinearTask ResumeTrainingTask(string mapId, TaskSnapshot snapshot)
		{
			LinearTask trainingtask = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type, snapshot.IsCompleted, snapshot.UId);

			//GET OCCUPANT REFERENCE
			var occupant = _map.GetOccupantByUId(mapId, snapshot.UId);

			trainingtask.OnCompleted += () =>
			{
				//COMPLETE TRAINING
				occupant.IsReady = true;
			};

			return trainingtask;
		}
	}
}	

