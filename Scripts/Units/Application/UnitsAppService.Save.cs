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
		/// 读档重建**单位的周期任务**：移动/回蓝循环（每单位一条）+ 交战循环（`AttackTargetUid` 非空时）。
		/// <para>（v0.3 / WP-3.6）覆盖三处此前会漏掉的对象：</para>
		/// <list type="number">
		/// <item>**交战中的进攻方**（在 `cell.Invader` 槽位，不是 `Occupant`）—— 漏掉它会让"打了一半的仗"
		/// 读档后只剩被挑战方在打空气；</item>
		/// <item>**敌方单位的反击循环**（`E19`）—— 敌方不移动（不重建移动循环），但"在打某个单位"必须恢复；
		/// </item>
		/// <item>同一格的一对**各自**的一条循环（双向），进度都从快照回填。</item>
		/// </list>
		/// </summary>
		/// <param name="progressByTaskKey">按任务键取回存档进度（键见 `TaskSnapshot.Key`）。</param>
		/// <returns>重建的任务数。</returns>
		public int RestoreUnitTasks(string mapId, IDictionary<string, float> progressByTaskKey)
		{
			int restored = 0;

			foreach (MapCell cell in _map.GetAllCells(mapId).ToList())
			{
				restored += RestoreUnit(mapId, cell.Occupant as Unit, progressByTaskKey);
				restored += RestoreUnit(mapId, cell.Invader as Unit, progressByTaskKey);
			}

			return restored;
		}

		/// <summary>单个单位的周期任务重建（`Occupant` 与 `Invader` 两个槽位共用这一段）。</summary>
		private int RestoreUnit(string mapId, Unit unit, IDictionary<string, float> progressByTaskKey)
		{
			if (unit == null || !unit.IsReady) return 0;

			int restored = 0;
			string uid = unit.GetInfo().UId;

			// v0.3 / WP-3.8：敌方单位不移动、不巡逻（design/unit.md「行为模式」）→ 不重建移动循环
			// （旧写法会给每个敌方单位挂一条永远无事可做的日循环）
			if (!unit.IsHostile)
			{
				RegisterMoveTask(mapId, uid, ProgressOf(progressByTaskKey, "UnitMove", "none", uid));
				restored++;
			}

			// v0.3 / WP-3.6（`E19`）：敌方也会反击 → 攻击循环对两个阵营一视同仁地恢复
			if (!string.IsNullOrWhiteSpace(unit.AttackTargetUid))
			{
				RegisterAttackTask(mapId, uid, unit.AttackTargetUid,
					ProgressOf(progressByTaskKey, "UnitAttack", "none", $"atk_{uid}_{unit.AttackTargetUid}"));
				restored++;
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