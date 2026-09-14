using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.2）**存档映射器**：活动对象 ↔ 存档 DTO 的唯一翻译层。
	/// <list type="bullet">
	/// <item><see cref="ToSave"/>：地图（含每格人口与占据物）→ <see cref="MapSave"/>。</item>
	/// <item><see cref="RebuildBuilding"/> / <see cref="RebuildUnit"/>：DTO → 活动对象。
	/// <see cref="SaveRebuilder"/> 把这两个委托交给地图仓库，从而**地图模块不需要认识**
	/// 建筑/单位模块的类型（只认识 `IMapOccupant`）。</item>
	/// </list>
	/// <para>为什么单独一层（`B10` / `MAP-02`）：\"实体状态\"与\"磁盘格式\"的耦合是过去几轮 bug 的来源
	/// （改字段即破档、读档靠手抄）；映射集中在这里后，加字段只改 DTO + 两个方向各一行。</para>
	/// </summary>
	public static class SaveMapper
	{
		/// <summary>把活动地图映射成存档对象（含人口与占据物，`SaveVersion = 2`）。</summary>
		public static MapSave ToSave(Domain.Map map)
		{
			if (map == null) return null;

			var save = new MapSave
			{
				SaveVersion = MapSave.CurrentVersion,
				Id = map.Id,
				seed = map.seed,
				width = map.width,
				height = map.height,
			};

			foreach (MapCell cell in map.GetAllCells())
			{
				var cellSave = new HexCubeCellSave
				{
					position = cell.Position,
					terrain = cell.Terrain?.Id ?? string.Empty,
					Population = cell.Population,
				};

				if (cell.Occupant is Building building) cellSave.Building = ToBuildingSave(building);
				else if (cell.Occupant is Unit unit) cellSave.Unit = ToUnitSave(unit);

				save.cells.Add(cellSave);
			}

			return save;
		}

		/// <summary>建筑 → DTO（含建造者绑定与训练队列）。</summary>
		public static BuildingSaveDto ToBuildingSave(Building building)
		{
			MapOccupantInfo info = building.GetInfo();

			return new BuildingSaveDto
			{
				UId = info.UId,
				Id = info.Id,
				Name = info.Name,
				OwnerId = info.OwnerId,
				IsReady = building.IsReady,
				BuilderUId = building.BuilderBinding?.BuilderUId,
				TrainingQueue = building.TrainingQueue
					.Select(order => new TrainingOrderSave
					{
						UnitId = order.UnitId,
						UId = order.UId,
						Duration = order.Duration,
						PopulationCost = order.PopulationCost,
						IsActive = order.IsActive,
					})
					.ToList(),
			};
		}

		/// <summary>单位 → DTO（含 HP / MP / 忙闲 / 攻击目标）。</summary>
		public static UnitSaveDto ToUnitSave(Unit unit)
		{
			MapOccupantInfo info = unit.GetInfo();

			return new UnitSaveDto
			{
				UId = info.UId,
				Id = info.Id,
				Name = info.Name,
				OwnerId = info.OwnerId,
				IsReady = unit.IsReady,
				HP = unit.HP,
				CurrentMP = unit.CurrentMP,
				IsIdle = unit.IsIdle,
				AttackTargetUid = unit.AttackTargetUid,
			};
		}
	}

	/// <summary>
	/// （v0.3 / WP-3.2）**实体重建器**：由组合根用两个工厂（建筑/单位）构造，交给地图仓库在读档时回调。
	/// <para>职责边界：它把「DTO → 领域对象」这一步所需的<b>配置表</b>依赖关在组合根里，
	/// 地图仓库只调用委托（因此 `Map.Infrastructure` 不必引用 `Construction.Domain` / `Units.Domain`）。</para>
	/// </summary>
	public sealed class SaveRebuilder(BuildingFactory buildingFactory, UnitFactory unitFactory)
	{
		private readonly BuildingFactory _buildings = buildingFactory;
		private readonly UnitFactory _units = unitFactory;

		/// <summary>重建建筑：**保留原 uid**（uid 是一切引用的锚点），并恢复训练队列与建造者绑定的**数据部分**。</summary>
		public IMapOccupant RebuildBuilding(BuildingSaveDto save, HexCubePosition position)
		{
			if (save == null || string.IsNullOrWhiteSpace(save.Id)) return null;

			Building building = _buildings?.CreateBuilding(save.Id, position, save.OwnerId, save.UId, save.IsReady);
			if (building == null) return null;

			// 训练队列（数据）：释放回调等"需要单位对象"的部分由应用服务在读档后补挂（`RestoreBuildingExtras`）
			foreach (TrainingOrderSave order in save.TrainingQueue ?? new List<TrainingOrderSave>())
			{
				building.TryEnqueueTraining(new TrainingOrder
				{
					UnitId = order.UnitId,
					UId = order.UId,
					Duration = order.Duration,
					PopulationCost = order.PopulationCost,
					IsActive = order.IsActive,
				});
			}

			if (!string.IsNullOrWhiteSpace(save.BuilderUId))
			{
				building.TryBindBuilder(new BuilderBinding
				{
					BuilderUId = save.BuilderUId,
					BuilderPosition = position,
					TargetPosition = position,
				});
			}

			return building;
		}

		/// <summary>重建单位（含 HP/MP/忙闲/攻击目标）。</summary>
		public IMapOccupant RebuildUnit(UnitSaveDto save, HexCubePosition position)
		{
			if (save == null || string.IsNullOrWhiteSpace(save.Id)) return null;

			Unit unit = _units?.CreateUnit(save.Id, position, save.OwnerId, save.UId);
			if (unit == null) return null;

			unit.IsReady = save.IsReady;
			if (save.HP > 0f) unit.HP = save.HP;
			unit.CurrentMP = save.CurrentMP;
			unit.IsIdle = save.IsIdle;
			unit.AttackTargetUid = save.AttackTargetUid;
			return unit;
		}
	}
}