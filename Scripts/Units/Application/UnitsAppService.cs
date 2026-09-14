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
	public class UnitsAppService
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

		public UnitsAppService(
			MapAppService mapApp,
			TechTreesAppService techTreeApp,
			ResourcesAppService resourcesApp,
			ConstructionAppService constructionApp,
			ITimeService timeService,
			IUnitsRepository repo,
			UnitFactory factory,
			FogAppService fogAppService,
			IBuildingConfigRepository buildingRepo = null)
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
		}

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

			var terrainRequirements = (from item in config.TerrainRequirements select _map.GetTerrainRequirement(mapId, position, item));
			var techRequirements = (from item in config.TechRequirements select _tech.GetTechTreeRequirement(mapId, ownerId, item.Key, item.Value.ToList()));

			IConsumable? popContract = null;
			if (config.PopulationCost > 0)
			{
				popContract = _map.CreatePopulationConsumption(mapId, position, config.PopulationCost);
				if (!popContract.IsConsumable()) return;
			}

			if (contracts.All(c => c.IsConsumable()) && _map.IsClear(mapId, position)
				&& terrainRequirements.All(c => c.IsMet()) && techRequirements.All(c => c.IsMet())
				&& (popContract == null || popContract.IsConsumable()))
			{
			contracts.ForEach(c => c.Consume());
			popContract?.Consume();

			Unit unit = _factory.CreateUnit(unitId, position, ownerId);
				string uid = unit.GetInfo().UId;

				LinearTask trainingTask = new(0, config.Duration, config.UnitId, "Training", false, uid, mapId, ownerId);

				_map.SetOccupant(mapId, position, unit);

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
			_map.SetOccupant(mapId, spawn.Value, unit);

			_fog.RevealArea(spawn.Value, unitConfig.VisionRadius);
			RegisterMoveTask(mapId, order.UId);
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
					Attack(mapId, unit, targetParam);
					return true;
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
			unit.MoveTarget = dest;
			unit.IsIdle = false;
		}

		private void RegisterMoveTask(string mapId, string uid)
		{
			// 口径（v0.3 / WP-1.5）：每 10 游戏日补一次 MP（design/unit.md「每 10 秒恢复」→ M0-3 ② 的 10 日）
			var task = new IntervalTask(0, TimeConstants.UnitMoveDays, uid, "UnitMove", "none", mapId, 0);
			task.OnCompleted += () => MoveTick(mapId, uid);
			_time.Register(task);
		}

		private void MoveTick(string mapId, string uid)
		{
			// v0.3 / WP-2.2：宿主消失时用空安全查询（任务可能比实体多活一帧，见 UnregisterByUId）
			var occupant = _map.FindOccupantByUId(mapId, uid);
			if (occupant is not Unit unit) return;

			// Recharge MP (capped at MoveRechargePerTick when idle)
			unit.CurrentMP = Math.Min(unit.CurrentMP + unit.MoveRechargePerTick, unit.MoveRechargePerTick);

			if (unit.MoveTarget == null) return;

			// Recalculate path if needed
			if (unit.MovePath == null || unit.MovePath.Count == 0 || unit.MovePath[0] != unit.Position)
			{
				unit.MovePath = _map.FindPath(mapId, unit.Position, unit.MoveTarget.Value, _fog);
				if (unit.MovePath.Count <= 1) { unit.MoveTarget = null; unit.IsIdle = true; return; }
				unit.MovePath.RemoveAt(0); // remove current position
			}

			var nextCell = unit.MovePath[0];

			// Stop if enemy in vision radius
			if (HasEnemyInRadius(mapId, unit))
			{
				unit.MoveTarget = null;
				unit.IsIdle = true;
				return;
			}

			// Check if next cell has a friendly unit → skip over
			var friendlySkipCost = 0f;
			int skipIndex = 0;
			while (skipIndex < unit.MovePath.Count)
			{
				var checkCell = unit.MovePath[skipIndex];
				var occInfo = _map.GetOccupantInfo(mapId, checkCell);
				if (occInfo != null && occInfo.Value.OwnerId == unit.GetInfo().OwnerId)
				{
					friendlySkipCost += GetCellMoveCost(mapId, checkCell);
					skipIndex++;
				}
				else break;
			}

			if (skipIndex > 0 && unit.CurrentMP >= friendlySkipCost)
			{
				unit.CurrentMP -= friendlySkipCost;
				for (int i = 0; i < skipIndex; i++)
				{
					HexCubePosition oldPos = unit.Position;
					unit.Position = unit.MovePath[0];
					unit.MovePath.RemoveAt(0);
					_fog.ResetArea(oldPos, _repo.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0);
					_fog.RevealArea(unit.Position, _repo.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0);
				}
			}

			if (unit.MovePath.Count == 0) { unit.MoveTarget = null; unit.IsIdle = true; return; }

			nextCell = unit.MovePath[0];

			// Check if the next cell's terrain can't be passed
			if (!CanEnterCell(mapId, nextCell, _fog))
			{
				unit.MoveTarget = null;
				unit.IsIdle = true;
				return;
			}

			float moveCost = GetCellMoveCost(mapId, nextCell);
			if (unit.CurrentMP >= moveCost)
			{
				unit.CurrentMP -= moveCost;
				HexCubePosition oldPos = unit.Position;
				unit.Position = nextCell;
				unit.MovePath.RemoveAt(0);
				_fog.ResetArea(oldPos, _repo.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0);
				_fog.RevealArea(unit.Position, _repo.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0);

				if (unit.MovePath.Count == 0)
				{
					unit.MoveTarget = null;
					unit.IsIdle = true;
				}
			}
		}

		private bool HasEnemyInRadius(string mapId, Unit unit)
		{
			var map = _map.GetAllCells(mapId);
			int ownerId = unit.GetInfo().OwnerId;
			int visionRadius = _repo.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 3;

			foreach (var cell in map)
			{
				if (cell.Occupant != null && cell.Occupant is Unit otherUnit
					&& otherUnit.GetInfo().OwnerId != ownerId
					&& unit.Position.DistenceTo(cell.Position) <= visionRadius)
					return true;
			}
			return false;
		}

		private bool CanEnterCell(string mapId, HexCubePosition pos, FogAppService fog)
		{
			var mapCell = _map.GetMapCell(mapId, pos);
			if (mapCell == null) return true;
			byte vis = fog.GetVisibility(pos);
			if (vis == FogAppService.Unexplored) return true;
			return mapCell.Terrain != null && mapCell.Terrain.Passable;
		}

		private float GetCellMoveCost(string mapId, HexCubePosition pos)
		{
			var mapCell = _map.GetMapCell(mapId, pos);
			return mapCell?.Terrain?.MoveCost ?? 1f;
		}

		// ==================== ATTACK ENGINE ====================

		private void Attack(string mapId, Unit unit, string targetUid)
		{
			var target = _map.GetOccupantByUId(mapId, targetUid);
			if (target is not Unit targetUnit || targetUnit.GetInfo().OwnerId == unit.GetInfo().OwnerId)
				return;

			int aRadius = unit.AttackRadius;
			unit.IsIdle = false;

			if (aRadius > 0 && unit.Position.DistenceTo(targetUnit.Position) <= aRadius)
			{
				// Ranged — attack immediately
				RegisterAttackTask(mapId, unit.GetInfo().UId, targetUid);
				return;
			}

			// Melee or out of range → move adjacent
			var neighbor = targetUnit.Position.GetNeighbor().FirstOrDefault(n => CanEnterCell(mapId, n, _fog));
			if (neighbor == default) return;

			unit.MoveTarget = neighbor;
			unit.AttackTargetUid = targetUid;
			// When MoveTick reaches target, it will detect AttackTargetUid and start melee
		}

		private void RegisterAttackTask(string mapId, string attackerUid, string targetUid)
		{
			// 口径（v0.3 / WP-1.5）：每 1 游戏日结算一次伤害（原为 1 秒）
			// 生命周期（v0.3 / WP-2.2）：交战循环的终结状态是"目标消失 / 脱离射程 / 已是尸体"，
			// 由 AttackTick 自行注销 —— 否则循环任务会永久留在时间总线里空转（`TIME-02`）。
			IntervalTask task = null;
			task = new IntervalTask(0, TimeConstants.UnitAttackDays, $"atk_{attackerUid}_{targetUid}", "UnitAttack", "none", mapId, 0);
			task.OnCompleted += () => AttackTick(mapId, attackerUid, targetUid, task);
			_time.Register(task);
		}

		private void AttackTick(string mapId, string attackerUid, string targetUid, IntervalTask task)
		{
			var attacker = _map.FindOccupantByUId(mapId, attackerUid);
			var target = _map.FindOccupantByUId(mapId, targetUid);

			// 任一方已从地图上消失 → 交战结束（注销本任务）
			if (attacker is not Unit atkUnit || target is not Unit defUnit)
			{
				_time.Unregister(task);
				return;
			}

			// 目标已是尸体（等 `WP-3.4` 修好"僵尸索引"后不会再走到这里，此处保留兜底）
			if (defUnit.HP <= 0)
			{
				_time.Unregister(task);
				return;
			}

			// Target moved out of range → stop attacking, no pursuit
			if (atkUnit.AttackRadius > 0 && atkUnit.Position.DistenceTo(defUnit.Position) > atkUnit.AttackRadius)
			{
				atkUnit.IsIdle = true;
				atkUnit.AttackTargetUid = null;
				_time.Unregister(task);
				return;
			}
			if (atkUnit.AttackRadius == 0 && atkUnit.Position != defUnit.Position)
			{
				atkUnit.IsIdle = true;
				atkUnit.AttackTargetUid = null;
				_time.Unregister(task);
				return;
			}

			defUnit.HP -= atkUnit.AttackDamage;

			if (defUnit.HP <= 0)
			{
				var pos = defUnit.Position;
				_map.RemoveOccupantByPosition(mapId, pos, defUnit);
				_fog.ResetArea(pos, _repo.GetUnitConfig(defUnit.GetInfo().Id)?.VisionRadius ?? 0);
				atkUnit.IsIdle = true;
				atkUnit.AttackTargetUid = null;

				// 范围注销（v0.3 / WP-2.2）：阵亡单位名下的移动/训练任务一并回收，避免"已死对象"继续空转
				_time.UnregisterByUId(targetUid);
				_time.Unregister(task);
			}
		}
	}
}