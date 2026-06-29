using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
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

		public UnitsAppService(
			MapAppService mapApp,
			TechTreesAppService techTreeApp,
			ResourcesAppService resourcesApp,
			ConstructionAppService constructionApp,
			ITimeService timeService,
			IUnitsRepository repo,
			UnitFactory factory,
			FogAppService fogAppService)
		{
			_map = mapApp;
			_tech = techTreeApp;
			_resources = resourcesApp;
			_construction = constructionApp;
			_time = timeService;
			_repo = repo;
			_factory = factory;
			_fog = fogAppService;
		}

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

		public void ExcuteAction(string mapId, string uid, HexCubePosition targetPosition, string targetParam, string action)
		{
			var occupant = _map.GetOccupantByUId(mapId, uid);
			if (occupant is not Unit unit) return;

			var config = _repo.GetUnitConfig(unit.GetInfo().Id);
			if (!config.Actions.Contains(action)) return;

			switch (action)
			{
				case "CanBuild":
					Build(mapId, unit, targetParam, targetPosition);
					break;
				case "CanAttack":
					Attack(mapId, unit, targetParam);
					break;
				case "CanMove":
					Move(mapId, unit, targetPosition);
					break;
			}
		}

		private void Build(string mapId, Unit unit, string building, HexCubePosition position)
		{
			if (unit.Position != position || !unit.IsIdle) return;
			_construction.StartConstruction(mapId, building, position, unit.GetInfo().OwnerId);
			unit.IsIdle = false;
		}

		// ==================== MOVE ENGINE ====================

		private void Move(string mapId, Unit unit, HexCubePosition dest)
		{
			unit.MoveTarget = dest;
			unit.IsIdle = false;
		}

		private void RegisterMoveTask(string mapId, string uid)
		{
			var task = new IntervalTask(0, 10f, uid, "UnitMove", "none", mapId, 0);
			task.OnCompleted += () => MoveTick(mapId, uid);
			_time.Register(task);
		}

		private void MoveTick(string mapId, string uid)
		{
			var occupant = _map.GetOccupantByUId(mapId, uid);
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
			return mapCell.Terrain != null && mapCell.Terrain.MoveCost > 0f;
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
			var task = new IntervalTask(0, 1f, $"atk_{attackerUid}_{targetUid}", "UnitAttack", "none", mapId, 0);
			task.OnCompleted += () => AttackTick(mapId, attackerUid, targetUid);
			_time.Register(task);
		}

		private void AttackTick(string mapId, string attackerUid, string targetUid)
		{
			var attacker = _map.GetOccupantByUId(mapId, attackerUid);
			var target = _map.GetOccupantByUId(mapId, targetUid);

			if (attacker is not Unit atkUnit || target is not Unit defUnit) return;
			if (defUnit.HP <= 0) return;

			// Target moved out of range → stop attacking, no pursuit
			if (atkUnit.AttackRadius > 0 && atkUnit.Position.DistenceTo(defUnit.Position) > atkUnit.AttackRadius)
			{
				atkUnit.IsIdle = true;
				atkUnit.AttackTargetUid = null;
				return;
			}
			if (atkUnit.AttackRadius == 0 && atkUnit.Position != defUnit.Position)
			{
				atkUnit.IsIdle = true;
				atkUnit.AttackTargetUid = null;
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
			}
		}
	}
}