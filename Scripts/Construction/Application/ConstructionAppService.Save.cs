using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Construction.Application
{
	/// <summary>
	/// （v0.3 / WP-3.2）**读档恢复的收口**：把磁盘上的建筑状态（训练队列、建造者绑定、进行中的施工/升级）
	/// 重新接回运行时。放在应用层而不是仓储层，因为"恢复"必须与新建走同一套语义
	/// （队列推进、建造者占用、任务注册），否则读档后的行为会与存档前不一致（`CON-03` 的同类教训）。
	/// </summary>
	public partial class ConstructionAppService
	{
	/// <summary>
	/// （v0.3 / WP-3.2）读档恢复建筑的**附加状态**：训练队列与建造者绑定的**数据**由 `SaveRebuilder` 恢复，
	/// 这里只补挂**运行时回调** —— 建造者绑定的【释放动作】（把单位从忙恢复为空闲）。
	/// <para>为什么必须补挂：委托对象不能落盘；不补挂的话，读档后施工完成的建筑不会释放工人，
	/// 那个工人会永久卡在忙状态（`WP-2.4` 修过一次，读档路径是同一个坑）。</para>
	/// </summary>
	public int RestoreBuildingExtras(string mapId)
	{
		int wired = 0;

		foreach (MapCell cell in _map.GetAllCells(mapId).ToList())
		{
			if (cell.Occupant is not Building building || building.BuilderBinding == null) continue;

			var builder = _map.FindOccupantByUId(mapId, building.BuilderBinding.BuilderUId);
			if (builder is Units.Domain.Unit unit)
			{
				building.BuilderBinding.OnRelease = () => unit.IsIdle = true;
				wired++;
			}
			else
			{
				// 建造者已不存在（阵亡/被删）：清掉悬空绑定，避免完工时去释放一个不存在的单位
				building.ReleaseBuilder();
			}
		}

		return wired;
	}

		/// <summary>
		/// （v0.3 / WP-3.2 / `CON-03`）读档续跑**升级**：与首次升级共用同一个完成落地方法
		/// <see cref="CompleteConstruction"/>（含换配置、回收旧修正器/人口任务、释放建造者、推送领域事件）。
		/// </summary>
		public LinearTask ResumeUpgrade(string mapId, TaskSnapshot snapshot)
		{
			if (snapshot == null) return null;

			var occupant = _map.FindOccupantByUId(mapId, snapshot.UId);
			if (occupant is not Building building) return null;

			IBuildingConfig current = _buildingRepo.GetBuildingConfig(building.GetInfo().Id);
			IBuildingConfig target = _buildingRepo.GetBuildingConfig(snapshot.Id); // 升级任务的业务键 = 目标建筑 Id
			if (current == null || target == null) return null;

			LinearTask task = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type,
				snapshot.IsCompleted, snapshot.UId, mapId, snapshot.OwnerId);
			HexCubePosition position = building.GetInfo().Position;

			task.OnCompleted += () =>
			{
				building.ApplyUpgrade(target.BuildingId, target.Name);
				CompleteConstruction(mapId, snapshot.UId, snapshot.OwnerId, position, target, current, task);
			};

			return task;
		}

		/// <summary>
		/// （v0.3 / WP-3.2）读档重建**人口增长任务**：扫描地图上的住房建筑，按配置注册任务，
		/// 并用存档里的进度回填（`progressByTaskKey` 的键 = `TaskSnapshot.Key`，即 `PopulationGrowth:none:{建筑 uid}`）。
		/// <para>为什么按建筑重建而不是恢复旧任务对象：任务的闭包（改哪一格人口、用什么上限）都来自建筑配置，
		/// 重建是唯一能保证"读档后行为与存档前一致"的方式。</para>
		/// </summary>
		public int RestoreHousingTasks(string mapId, int ownerId, IDictionary<string, float> progressByTaskKey)
		{
			int restored = 0;

			foreach (MapCell cell in _map.GetAllCells(mapId).ToList())
			{
				if (cell.Occupant is not Building building || !building.IsReady) continue;

				IBuildingConfig config = _buildingRepo.GetBuildingConfig(building.GetInfo().Id);
				if (config == null || !config.IsHousing) continue;
				if (config.PopulationCap <= 0 || config.PopulationGrowthInterval <= 0) continue;

				string uid = building.GetInfo().UId;
				string key = TaskSnapshot.BuildKey("PopulationGrowth", "none", uid);
				float progress = progressByTaskKey != null && progressByTaskKey.TryGetValue(key, out float saved) ? saved : 0f;

				RegisterHousingTask(mapId, building.GetInfo().OwnerId, uid, building.GetInfo().Position, config, progress);
				restored++;
			}

			return restored;
		}
	}
}
