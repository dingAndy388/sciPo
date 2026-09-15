using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Units.Application
{
	public partial class UnitsAppService
	{
		private readonly MapAppService _map;
		private readonly TechTreesAppService _tech;
		private readonly ResourcesAppService _resources;
		private readonly ConstructionAppService _construction;
		private readonly IUnitsRepository _repo;
		private readonly ITimeService _time;
		private readonly UnitFactory _factory;
		private readonly FogAppService _fog;
		private readonly IBuildingConfigRepository _buildingRepo;

		/// <summary>（v0.3 / WP-2.10）领域事件总线（可空 = 无人订阅）。</summary>
		private readonly IDomainEventBus _events;
		private readonly UnitMovementService _movement;

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`~`E12`、`E18`、`E19`）**战斗引擎**：攻击指令、按日结算、敌方反应都在它里面
		/// （旧实现是散在本类里的两段 tick；`WP-2.2` 起"战报/UI 想查谁在打谁"只能翻 `AttackTargetUid`）。
		/// </summary>
		private readonly UnitCombatService _combat;

		/// <summary>
		/// （v0.3 / WP-3.7 / `E20`）**掉落消费端**：订阅 `UnitDiedEvent`，把"玩家击杀敌方"翻译成入池动作
		/// （`ILootSink`；战斗引擎不认识资源池，见 `D69`）。
		/// </summary>
		private readonly UnitLootService _loot;

		public UnitsAppService(
			MapAppService mapApp,
			TechTreesAppService techTreeApp,
			ResourcesAppService resourcesApp,
			ConstructionAppService constructionApp,
			ITimeService timeService,
			IUnitsRepository repo,
			UnitFactory factory,
			FogAppService fogAppService,
			IBuildingConfigRepository buildingRepo = null,
			IDomainEventBus eventBus = null,
			ModifierAppService modifierApp = null,
			ILootSink lootSink = null)
		{
			_map = mapApp;
			_tech = techTreeApp;
			_resources = resourcesApp;
			_construction = constructionApp;
			_time = timeService;
			_repo = repo;
			_factory = factory;
			_fog = fogAppService;
			_buildingRepo = buildingRepo;
			_events = eventBus;
			// v0.3 / WP-3.5：移动模型（R1~R7）独立成服务，避免继续堆在应用服务里
			_movement = new UnitMovementService(mapApp, fogAppService, repo, timeService);
			// v0.3 / WP-3.6：战斗独立成服务；两者互相需要（战斗要"接近/通行"，移动要"攻进去/受击"），
			// 因此用可空属性回连而不是构造注入（构造期互相注入会成环）
			_combat = new UnitCombatService(mapApp, repo, _movement, timeService, fogAppService, eventBus, modifierApp);
			_movement.Combat = _combat;

			// v0.3 / WP-3.7：掉落 = `UnitDiedEvent` 的消费端（`E20`）；入池口缺省直接取资源服务
			// （`ResourcesAppService : ILootSink`，与 `MapAppService : IPopulationSink` 同一手法 `D62`）。
			// `lootSink` 显式传入时优先（表现层/测试想换实现不必换整个资源服务）。
			_loot = new UnitLootService(mapApp, repo, lootSink ?? resourcesApp as ILootSink, eventBus);
		}

		/// <summary>（v0.3 / WP-3.6）战斗引擎（UI / 用例可直接查询"在打谁""开了几条循环"）。</summary>
		public UnitCombatService Combat => _combat;

		/// <summary>（v0.3 / WP-3.6）移动服务（供表现层做可达性预览；与内部实例同一个）。</summary>
		public UnitMovementService Movement => _movement;

		/// <summary>
		/// （v0.3 / WP-3.7 / `E20`）掉落消费端：`Drops`/`LastGranted` 等观测口径与"为什么没掉落"的分类计数都挂在它上面
		/// （UI 想显示"击杀 +30 Gold"直接读 <see cref="UnitLootService.LastGranted"/>，不必自己算配置表）。
		/// </summary>
		public UnitLootService Loot => _loot;

		/// <summary>
		/// **直接生成**单位（不经过建筑）—— 敌方刷新（`WP-3.8`）/ 调试 / 脚本用。
		/// <para>玩家侧的产出走 <see cref="TrainUnit"/>：设计稿规定所有单位由建筑训练（`UNIT-11`）。</para>
		/// </summary>
		public void CreateUnit(string mapId, string unitId, HexCubePosition position, int ownerId)
		{
			var config = _repo.GetUnitConfig(unitId);
			if (config == null) return;

			var consumptions = (from item in config.ResourceCost
								select new Consumption(item.Key, item.Value)).ToList();

			List<IConsumable> contracts =
			[
				.. from item in consumptions select _resources.CreateResourceConsumption(item, mapId, ownerId),
			];

			// （v0.6.3 / WP-7.2a）"可训练地块"同样是**列表 = 任一匹配**（与建筑侧同一修正）
			IRequirement terrainRequirement = _map.GetTerrainRequirement(mapId, position, config.TerrainRequirements);
			var techRequirements = (from item in config.TechRequirements select _tech.GetTechTreeRequirement(mapId, ownerId, item.Key, item.Value.ToList()));

			IConsumable? popContract = null;
			if (config.PopulationCost > 0)
			{
				popContract = _map.CreatePopulationConsumption(mapId, position, config.PopulationCost);
				if (!popContract.IsConsumable()) return;
			}

			if (contracts.All(c => c.IsConsumable()) && _map.IsClear(mapId, position)
				&& terrainRequirement.IsMet() && techRequirements.All(c => c.IsMet())
				&& (popContract == null || popContract.IsConsumable()))
			{
			contracts.ForEach(c => c.Consume());
			popContract?.Consume();

			Unit unit = _factory.CreateUnit(unitId, position, ownerId);
				string uid = unit.GetInfo().UId;

				LinearTask trainingTask = new(0, config.Duration, config.UnitId, "Training", false, uid, mapId, ownerId);

				_map.SetOccupant(mapId, position, unit); // v0.3 / WP-3.4：唯一入口起效（占位冲突会被拒绝）

				trainingTask.OnCompleted += () =>
				{
					_map.GetOccupantByUId(mapId, uid).IsReady = true;
					_fog.RevealArea(position, config.VisionRadius);

					RegisterMoveTask(mapId, uid);
				};

				_time.Register(trainingTask);
			}
		}

		// ==================== TRAINING (BUILDING-BOUND) ====================

		/// <summary>
		/// （v0.3 / WP-2.5 / `UNIT-11` / `D10`）**由建筑训练单位**：校验 → 扣资源 → 入队 → （空闲则）开工。
		/// <para>校验链（任一不过即返回 false，不产生任何副作用）：</para>
		/// <list type="number">
		/// <item>训练建筑存在且**已完工**（`IsReady`）；</item>
		/// <item>该单位在建筑的 <c>TrainableUnits</c> 里（`UNIT-11`：设计稿规定所有单位由建筑产出）；</item>
		/// <item>队列**未满**（上限来自 `TrainingQueueLimit`，默认 5）；</item>
		/// <item>资源足够（在**入队时**扣除："订单已下"即锁定成本，见 `D36`）；</item>
		/// <item>人口够用：`建筑半径 1 内人口 − 队列已占用人口 ≥ PopulationCost`（扣**完成时**执行 —— `E2`）。</item>
		/// </list>
		/// <para>**建筑等级校验**：设计稿要求"工坊 lv.II 才能训练 X"，但配置表还没有等级字段
		/// （`CON-09` 的升级体系已降级），故本轮按计划**跳过等级校验**并在 §18.4 记为可回归项。</para>
		/// </summary>
		/// <param name="mapId">地图 Id。</param>
		/// <param name="buildingUid">训练建筑（工坊 / 军营 …）的 uid —— 训练与建筑绑定的锚点。</param>
		/// <param name="unitId">要训练的单位模板 Id。</param>
		/// <returns>是否成功入队。</returns>
		public bool TrainUnit(string mapId, string buildingUid, string unitId)
		{
			if (_buildingRepo == null) return false;

			var occupant = _map.FindOccupantByUId(mapId, buildingUid);
			if (occupant is not Building building || !building.IsReady) return false;

			int ownerId = building.GetInfo().OwnerId;
			var buildingConfig = _buildingRepo.GetBuildingConfig(building.GetInfo().Id);
			if (buildingConfig == null || !(buildingConfig.TrainableUnits?.Contains(unitId) ?? false)) return false;

			var unitConfig = _repo.GetUnitConfig(unitId);
			if (unitConfig == null) return false;

			var consumptions = (from item in unitConfig.ResourceCost select new Consumption(item.Key, item.Value)).ToList();
			List<IConsumable> contracts =
			[
				.. from item in consumptions select _resources.CreateResourceConsumption(item, mapId, ownerId),
			];
			if (!contracts.All(c => c.IsConsumable())) return false;

			HexCubePosition center = building.GetInfo().Position;
			int reserved = building.TrainingQueue.Sum(order => order.PopulationCost);
			if (_map.GetPopulationWithin(mapId, center, 1) - reserved < unitConfig.PopulationCost) return false;

			var order = new TrainingOrder
			{
				UnitId = unitId,
				UId = Guid.NewGuid().ToString(), // 入队即锁定实例 uid（任务键 + 落位都用它）
				Duration = unitConfig.Duration,
				PopulationCost = unitConfig.PopulationCost,
			};
			if (!building.TryEnqueueTraining(order)) return false;

			contracts.ForEach(c => c.Consume());
			StartNextTraining(mapId, building);
			return true;
		}

		/// <summary>
		/// 队列推进（**同时只训练 1 个** —— 队列头）：没有在训订单且队列非空时，为队头注册训练任务。
		/// <para>完成回调里做三件事（`E2` / `E4`）：落位 → 人口 −1 → 撤下订单并推进队列。</para>
		/// </summary>
		private void StartNextTraining(string mapId, Building building)
		{
			if (building == null || building.HasActiveTraining) return;

			TrainingOrder order = building.PeekTraining();
			if (order == null) return;

			var unitConfig = _repo.GetUnitConfig(order.UnitId);
			if (unitConfig == null)
			{
				building.DequeueTraining();
				return;
			}

			order.IsActive = true;

			// 任务键 = `Training:{unitUid}:{unitId}`（`WP-2.2`）：同名单位的不同实例各自独立
			LinearTask trainingTask = new(0, order.Duration, unitConfig.UnitId, "Training", false, order.UId, mapId, building.GetInfo().OwnerId);

			trainingTask.OnCompleted += () =>
			{
				CompleteTraining(mapId, building, order, unitConfig);
				building.DequeueTraining();
				StartNextTraining(mapId, building); // 立即推进下一个订单（保持"同时 1 个"）
			};

			_time.Register(trainingTask);
		}

		/// <summary>完成一个训练订单：**落位**（建筑格优先，其次相邻空格）→ 单位就绪 → **人口 −1** → 视野。</summary>
		private void CompleteTraining(string mapId, Building building, TrainingOrder order, IUnitConfig unitConfig)
		{
			HexCubePosition? spawn = ResolveSpawnPosition(mapId, building.GetInfo().Position);
			if (spawn == null)
			{
				// 无处落位（建筑格与相邻 6 格全被占）：订单作废，已扣资源不退 —— 见 §18.4.2（`WP-3.4` 的占用模型修好后重评）
				return;
			}

			// 人口 −1（`E2`：设计稿要求"训练结束后"扣人口；扣的位置与校验口径一致 —— 建筑半径 1 内）
			_map.ConsumePopulation(mapId, building.GetInfo().Position, 1, order.PopulationCost);

			Unit unit = _factory.CreateUnit(order.UnitId, spawn.Value, building.GetInfo().OwnerId, order.UId);
			if (unit == null) return;

			unit.IsReady = true;
			_map.SetOccupant(mapId, spawn.Value, unit); // v0.3 / WP-3.4：唯一入口（占位冲突会被拒绝）

			_fog.RevealArea(spawn.Value, unitConfig.VisionRadius);
			RegisterMoveTask(mapId, order.UId);

			// 推送（v0.3 / WP-2.10）：单位诞生（UI 刷新 / 成就 / 联动）
			_events?.Publish(new UnitTrainedEvent(mapId, unit.GetInfo().OwnerId, order.UId, order.UnitId, spawn.Value));

			// v0.3 / WP-3.6（`E19`）：落位也是"进入敌方射程"的一种 —— 若训练场旁边就蹲着敌人，
			// 新兵应当立刻开始挨打（否则"敌人只在移动时才反应"会漏掉这条最常见的入场方式）
			_combat.OnUnitMoved(mapId, unit);
		}

		/// <summary>
		/// 落位格：建筑所在格优先，其次半径 1 内的空格；都没有则返回 null。
		/// <para>**当前占用模型**下建筑自己就占据着它的格子（一格一个 `Occupant`），所以"建筑格优先"
		/// 实际会先失败、单位落到相邻空格 —— 与设计稿"单位出现在建筑格或相邻格"一致。
		/// 等 `WP-3.4`（占用权威一致）/ `WP-4.9`（建筑嵌套）把"格内多占据物"落地后，这里无需改动即可回到"建筑格优先"。</para>
		/// </summary>
		private HexCubePosition? ResolveSpawnPosition(string mapId, HexCubePosition center)
		{
			if (_map.IsClear(mapId, center)) return center;

			foreach (HexCubePosition pos in center.InRadius(1))
				if (_map.IsClear(mapId, pos)) return pos;

			return null;
		}

		public LinearTask ResumeTrainingTask(string mapId, TaskSnapshot snapshot)
		{
			LinearTask task = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type, snapshot.IsCompleted, snapshot.UId, mapId, snapshot.OwnerId);
			task.OnCompleted += () =>
			{
				_map.GetOccupantByUId(mapId, snapshot.UId).IsReady = true;
				RegisterMoveTask(mapId, snapshot.UId);
			};
			return task;
		}

		/// <summary>
		/// （v0.3 / WP-2.4 / WP-2.6）单位动作入口。返回**是否成功执行**（`CON-01`：调用方需要知道成败，
		/// 而不是只能看结果反推）—— 建造/升级返回"是否开工"，移动/攻击返回"是否接受指令"。
		/// </summary>
		public bool ExcuteAction(string mapId, string uid, HexCubePosition targetPosition, string targetParam, string action)
		{
			var occupant = _map.GetOccupantByUId(mapId, uid);
			if (occupant is not Unit unit) return false;

			var config = _repo.GetUnitConfig(unit.GetInfo().Id);
			if (config == null) return false;

			// 能力门控（v0.3 / WP-2.4 + WP-2.6）：与 config.Actions 对齐；`CanUpgrade` 由"建造者"角色派生
			// （设计稿没有单独的升级单位 —— 谁建得起就谁升得起），因此建造者（CanBuild）自动获得升级能力。
			bool actionAllowed = config.Actions?.Contains(action) ?? false;
			if (!actionAllowed && action == "CanUpgrade")
				actionAllowed = config.Actions?.Contains("CanBuild") ?? false;
			if (!actionAllowed) return false;

			switch (action)
			{
				case "CanBuild":
					return Build(mapId, unit, targetParam, targetPosition);
				case "CanUpgrade":
					return Upgrade(mapId, unit, targetParam, targetPosition);
				case "CanAttack":
					// v0.3 / WP-3.6：攻击委托给战斗引擎（近战攻进敌格 = 同格交战；远程隔格开火），
					// 返回"是否接受了指令"（旧实现无条件返回 true，UI 无法区分"打不着"与"已开火"）
					return _combat.Engage(mapId, unit.GetInfo().UId, targetParam);
				case "CanMove":
					Move(mapId, unit, targetPosition);
					return true;
			}

			return false;
		}

		/// <summary>
		/// （v0.3 / WP-2.6）**由建造者升级建筑**：与 <see cref="Build"/> 同一套门（能力 / 忙闲 / 距离），
		/// 目标格是**建筑所在格**（因此距离恒为 1：自己那格被自己占着）。
		/// </summary>
		private bool Upgrade(string mapId, Unit unit, string buildingUid, HexCubePosition position)
		{
			var config = _repo.GetUnitConfig(unit.GetInfo().Id);
			if (config == null || !(config.Actions?.Contains("CanBuild") ?? false)) return false;
			if (!unit.IsIdle) return false;

			var target = _map.FindOccupantByUId(mapId, buildingUid);
			if (target is not Building) return false;
			if (unit.Position.DistenceTo(target.GetInfo().Position) > 1) return false;

			var binding = new BuilderBinding
			{
				BuilderUId = unit.GetInfo().UId,
				BuilderPosition = unit.Position,
				TargetPosition = target.GetInfo().Position,
				OnRelease = () => unit.IsIdle = true,
			};

			if (!_construction.StartUpgrade(mapId, buildingUid, unit.GetInfo().OwnerId, binding)) return false;

			unit.IsIdle = false;
			unit.MoveTarget = null;
			return true;
		}

		/// <summary>
		/// （v0.3 / WP-2.4 / `D6`）**由建造者单位发起建造**：校验能力 → 忙闲 → 距离 → 目标格，然后交给
		/// <see cref="ConstructionAppService.StartConstruction"/>（带建造者绑定）。任一校验不过即返回且**无副作用**
		/// （不扣资源、不产生建筑、不占用单位）。
		/// <para>旧实现的两处错：① 只校验 <c>unit.Position == position</c>（要求建筑盖在**自己脚下**，
		/// 而该格被自己占着 → 建造永远失败）；② 无论成功与否都把 <c>IsIdle</c> 置为 false（单位永久卡死）。</para>
		/// </summary>
		private bool Build(string mapId, Unit unit, string building, HexCubePosition position)
		{
			var config = _repo.GetUnitConfig(unit.GetInfo().Id);

			// ① 能力：只有 CanBuild 的单位才是建造者（设计稿：工人 CanBuild；民兵/弓箭手不行）
			if (config == null || !(config.Actions?.Contains("CanBuild") ?? false)) return false;

			// ② 忙闲：每个建造者同时只干一件事（开工后 IsIdle=false，完工/被拆时释放）
			if (!unit.IsIdle) return false;

			// ③ 距离：只能在本格或相邻格建造（设计稿"可在相邻格建造"）
			if (unit.Position.DistenceTo(position) > 1) return false;

			// ④ 目标格必须为空（自己的格子被自己占着，所以"同格建造"会在这里被拒）
			if (!_map.IsClear(mapId, position)) return false;

			var binding = new BuilderBinding
			{
				BuilderUId = unit.GetInfo().UId,
				BuilderPosition = unit.Position,
				TargetPosition = position,
				OnRelease = () => unit.IsIdle = true, // 释放动作留在 Units 层（构造模块不认识 Unit 类型）
			};

			if (!_construction.StartConstruction(mapId, building, position, unit.GetInfo().OwnerId, binding)) return false;

			unit.IsIdle = false; // 只在真正开工后占用建造者
			unit.MoveTarget = null;
			return true;
		}

		// ==================== MOVE ENGINE ====================

		private void Move(string mapId, Unit unit, HexCubePosition dest)
		{
			// v0.3 / WP-3.5：改为走 UnitMovementService（R7：可随时改目的地，路径重算）
			_movement.SetDestination(mapId, unit.GetInfo().UId, dest);
		}

		private void RegisterMoveTask(string mapId, string uid, float initialProgress = 0f)
		{
			// v0.3 / WP-3.5：每 10 游戏日一次移动结算（R2）——实现见 UnitMovementService
			_movement.RegisterMoveLoop(mapId, uid, initialProgress);
		}

		private void MoveTick(string mapId, string uid) => _movement.Tick(mapId, uid); // v0.3 / WP-3.5：保留入口给既有调用点


		// ==================== ATTACK ENGINE（v0.3 / WP-3.6 起委托给 UnitCombatService）====================
		// 旧实现（本类内的 `Attack` / `RegisterAttackTask` / `AttackTick`）有三个绕不过去的问题：
		// ① 近战要求同格，而敌方格被 `CanEnter` 封锁 → 近战永远贴不上去（缺陷"敌方战斗未实现"）；
		// ② 只有攻击方有循环，敌方不会反击（design 的"双方按日结算"缺一半）；
		// ③ 攻击目标只接受 `Unit`（建筑不能打，`E18` 缺口），且没有目标类型衰减（`E12` 缺口）。
		// 现在全部落在 `UnitCombatService`：交战状态（一格一对）、按日结算、反击、建筑目标、读档恢复。

		/// <summary>
		/// （v0.3 / WP-3.6）**主动攻击**的统一落点（`ExcuteAction("CanAttack")` 与表现层都用它）：
		/// 近战 → 攻进目标格（同格交战）；远程 → 射程内开火，射程外先接近。
		/// </summary>
		public bool AttackUnit(string mapId, string attackerUid, string targetUid)
			=> _combat.Engage(mapId, attackerUid, targetUid);

		/// <summary>（v0.3 / WP-3.6）停手：注销该单位作为攻击方的全部交战循环。</summary>
		public int StopAttack(string mapId, string unitUid) => _combat.StopAttacksOf(mapId, unitUid);

		/// <summary>
		/// （v0.3 / WP-3.2 建，WP-3.6 改走战斗引擎）**恢复一条交战循环**：读档时按单位的
		/// `AttackTargetUid` 重建（任务键与进度见 `RestoreUnitTasks`）。
		/// </summary>
		private void RegisterAttackTask(string mapId, string attackerUid, string targetUid, float initialProgress = 0f)
			=> _combat.RestoreAttackLoop(mapId, attackerUid, targetUid, initialProgress);
	}
}
