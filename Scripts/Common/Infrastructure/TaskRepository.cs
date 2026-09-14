using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-2.2）任务仓储实现：**每次改动前先读盘**，再改键、再整文件写回。
	/// <para>为什么要"读 — 改 — 写"（而不是依赖内存态累积）：旧实现只在 <c>GetCurrentTasks</c> 里 <c>Load</c>，
	/// 但 <c>AddTask</c> 直接写 <c>_data</c> —— 进程重启后第一次 <c>AddTask</c> 会用**只含一个新任务**的字典
	/// 覆盖整个任务文件（旧任务全部丢失）。任务写入现在只在**日边界**发生（`WP-1.5` / `A8`），
	/// 因此"每次改动读一次盘"的代价可以接受；真正的"内存态 + 存档点"由 `WP-3.3` 统一收口。</para>
	/// </summary>
	public class TaskRepository :  GenericJsonRepository<TaskSnapshot>, ITaskRepository
	{
		private string _filePath;

		public TaskRepository(string filePath)
		{
			this._filePath = filePath;
		}

		public void AddTask(string mapId, TaskSnapshot task)
		{
			if (task == null) return; // 防御：null 不再被写进文件（`TIME-04`）

			string path = PathFor(mapId);
			base.Load(path);
			base.AddOrUpdate(task.Key, task, path); // 键 = `Type:UId:Id`（`TIME-03`）
		}

		public List<TaskSnapshot> GetCurrentTasks(string mapId)
		{
			string path = PathFor(mapId);
			base.Load(path);
			base.Reindex(snapshot => snapshot.Key, path); // 迁移历史"以 Id 为键"的文件（`TIME-03`）

			return base.GetAll().Where(snapshot => snapshot != null).ToList();
		}

		public void RemoveTask(string mapId, TaskSnapshot task)
		{
			if (task == null) return;

			string path = PathFor(mapId);
			base.Load(path);
			base.Remove(task.Key, path); // 真删除（`TIME-04`），而不是写 null 占位
		}

		public int RemoveTasks(string mapId, Func<TaskSnapshot, bool> match)
		{
			if (match == null) return 0;

			string path = PathFor(mapId);
			base.Load(path);
			return base.RemoveWhere(match, path);
		}

		private string PathFor(string mapId) => _filePath + mapId;
	}
}
