using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Units.Application
{
	/// <summary>
	/// （v0.3 / WP-3.2）**单位与训练队列的读档恢复**。
	/// <para>恢复策略：**按实体与配置重建周期任务**，再用存档进度回填 —— 任务的闭包（改哪个单位、瞄准谁）
	/// 都来自实体状态，重建才可能保证"读档后行为与存档前一致"；直接序列化任务对象既做不到（闭包不可序列化），
	/// 也会让"任务与实体"两处真相。</para>
	/// </summary>
	public partial class UnitsAppService
	{
		/// <summary>
		/// 读档续跑**训练**：训练快照的 `UId` 就是队列里那个已锁定单位的 uid，据此找回建筑与订单，
		/// 完成后走与首次训练相同的 <see cref="CompleteTraining"/>（落位 + 人口 −1 + 推进队头）。
		/// </summary>
		public LinearTask ResumeTraining(string mapId, TaskSnapshot snapshot)
		{
			if (snapshot == null) return null;

			Building building = FindActiveOrderBuilding(mapId, snapshot.UId);
			if (building == null) return null;

			TrainingOrder order = building.TrainingQueue.FirstOrDefault(o => o.IsActive && o.UId == snapshot.UId);
			if (order == null) return null;

			var unitConfig = _repo.GetUnitConfig(order.UnitId);
			if (unitConfig == null) return null;

			LinearTask task = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type,
				snapshot.IsCompleted, snapshot.UId, mapId, snapshot.OwnerId);

			task.OnCompleted += () =>
			{
				CompleteTraining(mapId, building, order, unitConfig);
				building.DequeueTraining();
				StartNextTraining(mapId, building);
			};

			return task;
		}

		/// <summary>
		/// 读档重建**单位的周期任务**：移动/回蓝循环（每单位一条）+ 攻击循环（`AttackTargetUid` 非空时）。
		/// </summary>
		/// <param name="progressByTaskKey">按任务键取回存档进度（键见 `TaskSnapshot.Key`）。</param>
		/// <returns>重建的任务数。</returns>
		public int RestoreUnitTasks(string mapId, IDictionary<string, float> progressByTaskKey)
		{
			int restored = 0;

			foreach (MapCell cell in _map.GetAllCells(mapId).ToList())
			{
				if (cell.Occupant is not Unit unit || !unit.IsReady) continue;

				// v0.3 / WP-3.8：敌方单位不移动、不巡逻（design/unit.md「行为模式」）→ 不重建移动循环
				// （旧写法会给每个敌方单位挂一条永远无事可做的日循环；交战循环由 `WP-3.6` 决定）
				if (unit.IsHostile) continue;

				string uid = unit.GetInfo().UId;

				RegisterMoveTask(mapId, uid, ProgressOf(progressByTaskKey, "UnitMove", "none", uid));
				restored++;

				if (!string.IsNullOrWhiteSpace(unit.AttackTargetUid))
				{
					RegisterAttackTask(mapId, uid, unit.AttackTargetUid,
						ProgressOf(progressByTaskKey, "UnitAttack", "none", $"atk_{uid}_{unit.AttackTargetUid}"));
					restored++;
				}
			}

			return restored;
		}

		/// <summary>按（类型, uid, 业务键）从存档进度表取值（缺省 0 = 从头计时）。</summary>
		private static float ProgressOf(IDictionary<string, float> progress, string type, string uid, string id)
		{
			if (progress == null) return 0f;

			return progress.TryGetValue(TaskSnapshot.BuildKey(type, uid, id), out float value) ? value : 0f;
		}

		/// <summary>找出"队列里恰好有在训订单 uid"的建筑（用于训练快照归属判定）。</summary>
		private Building FindActiveOrderBuilding(string mapId, string orderUId)
		{
			if (string.IsNullOrWhiteSpace(orderUId)) return null;

			foreach (MapCell cell in _map.GetAllCells(mapId))
			{
				if (cell.Occupant is Building building
					&& building.TrainingQueue.Any(order => order.IsActive && order.UId == orderUId))
					return building;
			}

			return null;
		}
	}
}