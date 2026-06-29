using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Construction.Application
{ 
	public class ConstructionAppService
	{
		private readonly MapAppService _map;
		private readonly ResourcesAppService _resource;
		private readonly TechTreesAppService _tech;
		private readonly BuildingFactory _factory;
		private readonly IBuildingConfigRepository _buildingRepo;
		private readonly ITimeService _time;
		private readonly ModifierAppService _modifier;
		private readonly FogAppService _fog;

		public ConstructionAppService(
			MapAppService mapAppService,
			ResourcesAppService resourceAppService,
			TechTreesAppService techTreeService,
			BuildingFactory factory,
			IBuildingConfigRepository buildingRepo,
			ITimeService time,
			ModifierAppService modifierAppService,
			FogAppService fogAppService)
		{
			_map = mapAppService;
			_resource = resourceAppService;
			_tech = techTreeService;
			_factory = factory;
			_buildingRepo = buildingRepo;
			_time = time;
			_modifier = modifierAppService;
			_fog = fogAppService;
		}

		public void StartConstruction(string mapId, string buildingId, HexCubePosition position, int ownerId)
		{
			var config = _buildingRepo.GetBuildingConfig(buildingId);

			var consumptions = (from item in config.ResourceCost
								select new Consumption(item.Key, item.Value)).ToList();

			List<IConsumable> contracts =
			[
				.. from item in consumptions
				   select _resource.CreateResourceConsumption(item, mapId, ownerId),
			];

			var terrainRequirements = (from item in config.TerrainRequirements
									   select _map.GetTerrainRequirement(mapId, position, item));

			var techRequirements = (from item in config.TechRequirements
									select _tech.GetTechTreeRequirement(mapId, ownerId, item.Key, item.Value.ToList()));

			var modifiers = config.Modifiers;

			if (contracts.All(c => c.IsConsumable())
				&& _map.IsClear(mapId, position)
				&& terrainRequirements.All(c => c.IsMet())
				&& techRequirements.All(c => c.IsMet()))
			{
				contracts.ForEach(c => c.Consume());

				Building building = _factory.CreateBuilding(buildingId, position, ownerId);

				string uid = building.GetInfo().UId;

				LinearTask buildTask = new(0, config.Duration, config.BuildingId, "Construction", false, uid, mapId, ownerId);

				_map.SetOccupant(mapId, position, building);

				buildTask.OnCompleted += () =>
				{
					_map.GetOccupantByUId(mapId, uid).IsReady = true;
					_modifier.AddModifiers(mapId, ownerId, uid, modifiers);
					_fog.RevealArea(position, config.VisionRadius);

					if (config.IsHousing && config.PopulationCap > 0 && config.PopulationGrowthInterval > 0)
						RegisterHousingTask(mapId, uid, position, config.PopulationRadius, config.PopulationCap, config.PopulationGrowthInterval);

					_time.Unregister(buildTask);
				};

				_time.Register(buildTask);
			}
		}

		public LinearTask ResumeConstruction(string mapId, TaskSnapshot snapshot)
		{
			LinearTask buildTask = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type, snapshot.IsCompleted, snapshot.UId, mapId, snapshot.OwnerId);
			buildTask.OnCompleted += () =>
			{
				var building = _map.GetOccupantByUId(mapId, snapshot.UId);
				building.IsReady = true;
				_modifier.AddModifiers(mapId, snapshot.OwnerId, snapshot.UId, _buildingRepo.GetBuildingConfig(building.GetInfo().Id).Modifiers);
				_time.Unregister(buildTask);
			};
			return buildTask;
		}

		public void RemoveBuildingByPosition(string mapId, HexCubePosition position)
		{
			var info = _map.GetBuildingInfo(mapId, position);
			if (info.HasValue)
			{
				var uid = info.Value.UId;
				var buildingConfig = _buildingRepo.GetBuildingConfig(info.Value.Id);
				_map.RemoveBuilding(mapId, position);
				_fog.ResetArea(position, buildingConfig?.VisionRadius ?? 0);
				_modifier.RemoveModifiersBySourceId(mapId, info.Value.OwnerId, uid);
			}
		}

		public void ExcuteAction(string mapId, string uid, string targetParam, string action)
		{
			var occupant = _map.GetOccupantByUId(mapId, uid);
			if (occupant is not Building building || !building.IsReady) return;

			var config = _buildingRepo.GetBuildingConfig(building.GetInfo().Id);
			if (config == null || config.Actions == null || !config.Actions.Contains(action)) return;

			switch (action)
			{
				case "CanResearch":
					Research(mapId, building, targetParam);
					break;
			}
		}

		private void RegisterHousingTask(string mapId, string buildingUid, HexCubePosition center, int radius, int cap, int interval)
		{
			var task = new IntervalTask(0, interval, buildingUid, "PopulationGrowth", "none", mapId, 0);
			task.OnCompleted += () =>
			{
				foreach (var pos in GetHexPositionsInRadius(center, radius))
				{
					var cell = _map.GetMapCell(mapId, pos);
					if (cell != null && cell.Population < cap)
						cell.AddPopulation(1);
				}
			};
			_time.Register(task);
		}

		private IEnumerable<HexCubePosition> GetHexPositionsInRadius(HexCubePosition center, int radius)
		{
			for (int dq = -radius; dq <= radius; dq++)
			{
				int minDr = Math.Max(-radius, -dq - radius);
				int maxDr = Math.Min(radius, -dq + radius);
				for (int dr = minDr; dr <= maxDr; dr++)
					yield return new HexCubePosition(center.q + dq, center.r + dr);
			}
		}

		private void Research(string mapId, Building building, string targetParam)
		{
			// targetParam format: "treeId:nodeId"
			var parts = targetParam.Split(':');
			if (parts.Length != 2) return;

			string treeId = parts[0];
			string nodeId = parts[1];
			int ownerId = building.GetInfo().OwnerId;

			_tech.Research(mapId, ownerId, treeId, nodeId);
		}
	}
}