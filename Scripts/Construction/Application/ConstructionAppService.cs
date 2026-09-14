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
						RegisterHousingTask(mapId, ownerId, uid, position, config);

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

				// 范围注销（v0.3 / WP-2.2）：建筑消失后，它名下的建造任务与人口增长任务都必须停止，
				// 否则会永久留在时间总线里空转，并把"已拆建筑"的快照一直写回任务文件（`TIME-02` / `CON-06`）。
				_time.UnregisterByUId(uid);
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

		/// <summary>
		/// （v0.3 / WP-2.3）注册住房的人口增长循环任务。修掉的三件事（`CON-04/05/06`）：
		/// <list type="number">
		/// <item>**`OwnerId`** 传建筑所有者（旧实现硬编码 <c>0</c> → 任务快照的归属永远是玩家 0，读档会把人口增长挂到别人名下）；</item>
		/// <item>**上限**按「半径内**总计**」判定（设计稿：营地「半径 1 格内总计 9 人」；旧实现是每格各自到 9 → 半径 1 的 7 格可达 63 人），
		/// 且写入走 <see cref="MapAppService.AddPopulation"/>（唯一写入点 + 脏标记 → 改动进入存档点）；</item>
		/// <item>每次增长经过 **`PopulationGrowth` 修正器**（旧实现直接 <c>+1</c>，填了 Modifier 也不生效），
		/// 用小数累加器保留不足 1 人的余量（例：+50% → 每两轮多出 1 人）。</item>
		/// </list>
		/// <para>任务的回收不在本方法里：住房被拆时由 <see cref="RemoveBuildingByPosition"/> →
		/// <c>ITimeService.UnregisterByUId</c> 按 uid 范围注销（`CON-06`，`WP-2.2` 提供能力）。</para>
		/// </summary>
		private void RegisterHousingTask(string mapId, int ownerId, string buildingUid, HexCubePosition center, IBuildingConfig config)
		{
			// 口径（v0.3 / WP-1.5）：`PopulationGrowthInterval` 的单位是**游戏日**（营地 300 日）
			var task = new IntervalTask(0, config.PopulationGrowthInterval, buildingUid, "PopulationGrowth", "none", mapId, ownerId);

			float pending = 0f; // 小数余量：修正器可能给出非整数增长（如 +50%）

			task.OnCompleted += () =>
			{
				pending += _modifier.GetValue(mapId, ownerId, "PopulationGrowth", 1f);

				int whole = (int)Math.Floor(pending);
				if (whole <= 0) return;

				// 余量先扣掉：人口受上限约束，超出的部分不会排队等待（与设计稿"总量封顶"一致）
				pending -= whole;
				_map.AddPopulation(mapId, center, config.PopulationRadius, config.PopulationCap, whole);
			};

			_time.Register(task);
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