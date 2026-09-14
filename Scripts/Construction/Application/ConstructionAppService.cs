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
	public partial class ConstructionAppService
	{
		private readonly MapAppService _map;
		private readonly ResourcesAppService _resource;
		private readonly TechTreesAppService _tech;
		private readonly BuildingFactory _factory;
		private readonly IBuildingConfigRepository _buildingRepo;
		private readonly ITimeService _time;
		private readonly ModifierAppService _modifier;
		private readonly FogAppService _fog;

		/// <summary>（v0.3 / WP-2.10）领域事件总线（可空 = 无人订阅，发布变成空操作）。</summary>
		private readonly IDomainEventBus _events;

		public ConstructionAppService(
			MapAppService mapAppService,
			ResourcesAppService resourceAppService,
			TechTreesAppService techTreeService,
			BuildingFactory factory,
			IBuildingConfigRepository buildingRepo,
			ITimeService time,
			ModifierAppService modifierAppService,
			FogAppService fogAppService,
			IDomainEventBus eventBus = null)
		{
			_map = mapAppService;
			_resource = resourceAppService;
			_tech = techTreeService;
			_factory = factory;
			_buildingRepo = buildingRepo;
			_time = time;
			_modifier = modifierAppService;
			_fog = fogAppService;
			_events = eventBus;
		}

		public bool StartConstruction(string mapId, string buildingId, HexCubePosition position, int ownerId, BuilderBinding builder = null)
		{
			var config = _buildingRepo.GetBuildingConfig(buildingId);
			if (config == null) return false;

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

			var noHostileRequirement = _map.GetNoHostileRequirement(mapId, position);

			if (contracts.All(c => c.IsConsumable())
				&& _map.IsClear(mapId, position)
				&& noHostileRequirement.IsMet() // v0.3 / WP-3.8（`B8`/`UNIT-14`）：敌方封锁格不可建造（M0-3 ④）
				&& terrainRequirements.All(c => c.IsMet())
				&& techRequirements.All(c => c.IsMet()))
			{
				Building building = _factory.CreateBuilding(buildingId, position, ownerId);

				// 建造者绑定（v0.3 / WP-2.4 / `D6`）：每建筑同时仅 1 个；绑定失败（已被占用）则不消耗任何资源
				if (!building.TryBindBuilder(builder)) return false;

				contracts.ForEach(c => c.Consume());

				string uid = building.GetInfo().UId;

				LinearTask buildTask = new(0, config.Duration, config.BuildingId, "Construction", false, uid, mapId, ownerId);

				// v0.3 / WP-3.4：建筑落位走统一入口（同时写 cell.Building 与占据物槽位 → 修 MAP-04）
				_map.PlaceBuilding(mapId, position, building);

				buildTask.OnCompleted += () => CompleteConstruction(mapId, uid, ownerId, position, config, null, buildTask);

				_time.Register(buildTask);
				return true;
			}

			return false;
		}

		/// <summary>
		/// （v0.3 / WP-2.6 / `CON-03`）**建造/升级完成的唯一落地方法**：首次建造完成、升级完成、读档续跑完成
		/// 三条路径都调用它 —— 旧实现是"照抄一遍"，续跑那份漏掉了开视野与人口任务（读档后玩法静默降级）。
		/// <list type="number">
		/// <item>建筑就绪；</item>
		/// <item>修正器：升级时先按 uid 回收旧等级的修正器，再挂新等级的（否则两层产出叠加）；</item>
		/// <item>视野：升级时先 <c>ResetArea</c> 旧半径，再按新半径 <c>RevealArea</c>；</item>
		/// <item>人口任务：先按 uid 范围注销旧任务再按新配置注册（同一建筑只允许一条人口增长任务，`WP-2.2`/`WP-2.3`）；</item>
		/// <item>释放建造者（`WP-2.4`）；</item>
		/// <item>注销本任务（`WP-2.2` 的完成即回收也会兜底）。</item>
		/// </list>
		/// </summary>
		/// <param name="previousConfig">升级前的配置（首次建造传 null）：用来回收旧修正器与旧视野半径。</param>
		private void CompleteConstruction(string mapId, string uid, int ownerId, HexCubePosition position,
			IBuildingConfig config, IBuildingConfig previousConfig, IProgressTask task)
		{
			var occupant = _map.FindOccupantByUId(mapId, uid);
			if (occupant == null) return; // 建筑已被拆（`CON-06` 的竞态）：什么都不做

			occupant.IsReady = true;

			if (previousConfig != null)
			{
				_modifier.RemoveModifiersBySourceId(mapId, ownerId, uid);
				_fog.ResetArea(position, previousConfig.VisionRadius);
			}

			_modifier.AddModifiers(mapId, ownerId, uid, config.Modifiers);
			_fog.RevealArea(position, config.VisionRadius);

			// 人口任务：同一建筑同时只允许一条（升级会换间隔/上限，必须先把旧任务摘掉）
			_time.UnregisterByUId(uid);
			if (config.IsHousing && config.PopulationCap > 0 && config.PopulationGrowthInterval > 0)
				RegisterHousingTask(mapId, ownerId, uid, position, config);

			if (occupant is Building building) building.ReleaseBuilder();

			// 推送（v0.3 / WP-2.10）：首次建成与升级完成是两类事件 —— 订阅方（UI/成就/联动）无需再轮询 `IsReady`
			if (previousConfig == null)
				_events?.Publish(new BuildingCompletedEvent(mapId, ownerId, uid, config.BuildingId, position));
			else
				_events?.Publish(new BuildingUpgradedEvent(mapId, ownerId, uid, previousConfig.BuildingId, config.BuildingId));

			_time.Unregister(task);
		}

		/// <summary>
		/// （v0.3 / WP-2.6 / `CON-09` / `D4`）**升级建筑**：校验等级链 / 科技前置 / 消耗 / 建造者绑定，然后注册升级任务。
		/// <para>升级期间建筑置为**未就绪**（`IsReady=false`）：训练/研究等门控自动失效，直到升级完成 ——
		/// 这是"升级中"语义的最小实现（不需要额外的状态字段）。</para>
		/// </summary>
		/// <returns>是否成功开工。</returns>
		public bool StartUpgrade(string mapId, string buildingUid, int ownerId, BuilderBinding builder = null)
		{
			var occupant = _map.FindOccupantByUId(mapId, buildingUid);
			if (occupant is not Building building || !building.IsReady) return false;

			var config = _buildingRepo.GetBuildingConfig(building.GetInfo().Id);
			if (config == null || string.IsNullOrWhiteSpace(config.UpgradeTo)) return false;

			var target = _buildingRepo.GetBuildingConfig(config.UpgradeTo);
			if (target == null) return false;

			var consumptions = (from item in (config.UpgradeCost ?? new Dictionary<string, float>())
								select new Consumption(item.Key, item.Value)).ToList();
			List<IConsumable> contracts =
			[
				.. from item in consumptions select _resource.CreateResourceConsumption(item, mapId, ownerId),
			];

			var techRequirements = (from item in (config.UpgradeTechRequirements ?? new Dictionary<string, List<string>>())
									select _tech.GetTechTreeRequirement(mapId, ownerId, item.Key, item.Value.ToList()));

			if (!contracts.All(c => c.IsConsumable()) || !techRequirements.All(c => c.IsMet())) return false;
			if (!building.TryBindBuilder(builder)) return false;

			contracts.ForEach(c => c.Consume());

			building.IsReady = false; // 升级中
			HexCubePosition position = building.GetInfo().Position;

			// 任务类型 `Upgrade`、业务键 = 目标建筑 Id：键 = `Upgrade:{buildingUid}:{targetId}`（`WP-2.2`）
			LinearTask upgradeTask = new(0, config.UpgradeDuration, config.UpgradeTo, "Upgrade", false, buildingUid, mapId, ownerId);

			upgradeTask.OnCompleted += () =>
			{
				building.ApplyUpgrade(target.BuildingId, target.Name);
				CompleteConstruction(mapId, buildingUid, ownerId, position, target, config, upgradeTask);
			};

			_time.Register(upgradeTask);
			return true;
		}

		/// <summary>
		/// （v0.3 / WP-2.6 / `CON-03`）读档续跑：用快照重建建造任务。完成时走**同一个**
		/// <see cref="CompleteConstruction"/>（旧实现手抄了一份、漏了开视野与人口任务）。
		/// </summary>
		public LinearTask ResumeConstruction(string mapId, TaskSnapshot snapshot)
		{
			LinearTask buildTask = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type, snapshot.IsCompleted, snapshot.UId, mapId, snapshot.OwnerId);

			buildTask.OnCompleted += () =>
			{
				var occupant = _map.FindOccupantByUId(mapId, snapshot.UId);
				if (occupant == null) return;

				var config = _buildingRepo.GetBuildingConfig(occupant.GetInfo().Id);
				if (config == null) return;

				CompleteConstruction(mapId, snapshot.UId, snapshot.OwnerId, occupant.GetInfo().Position, config, null, buildTask);
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

				// 拆除时释放建造者（v0.3 / WP-2.4）：施工中的建筑被拆 → 工人必须回到空闲，否则永远卡死
				var building = _map.FindOccupantByUId(mapId, uid);
				if (building is Building bound) bound.ReleaseBuilder();

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
				case "CanUpgrade":
					// 升级请求：targetParam = 建造者单位 uid（空 = 无建造者，脚本/测试路径）
					StartUpgrade(mapId, uid, building.GetInfo().OwnerId,
						string.IsNullOrWhiteSpace(targetParam) ? null : new BuilderBinding
						{
							BuilderUId = targetParam,
							BuilderPosition = building.GetInfo().Position,
							TargetPosition = building.GetInfo().Position,
						});
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
		/// <param name="initialProgress">
		/// （v0.3 / WP-3.2）读档恢复时回填的进度（游戏日）；新建时为 0。
		/// </param>
		private void RegisterHousingTask(string mapId, int ownerId, string buildingUid, HexCubePosition center, IBuildingConfig config, float initialProgress = 0f)
		{
			// 口径（v0.3 / WP-1.5）：`PopulationGrowthInterval` 的单位是**游戏日**（营地 300 日）
			var task = new IntervalTask(initialProgress, config.PopulationGrowthInterval, buildingUid, "PopulationGrowth", "none", mapId, ownerId);

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