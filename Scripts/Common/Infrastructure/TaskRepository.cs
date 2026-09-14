using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-2.2）任务仓储实现：按地图分文件，键 = <see cref="TaskSnapshot.Key"/>。
	/// <para>读写口径：**首次触碰某地图时读一次盘**，此后所有读写都在内存字典上完成，每次改动整文件写回。
	/// 这样既不丢数据（旧实现 <c>AddTask</c> 不读盘 → 进程重启后第一次写入会用「只含一个新任务」的字典
	/// 覆盖整个任务文件），也不再像 `WP-2.2` 首版那样"每次改动读一次盘"（日边界上每个任务一次 Read+Write
	/// 会让长跑用例的 IO 翻倍）。</para>
	/// <para>（v0.3 / WP-3.3）注入 <see cref="ISaveStore"/> 后：分区键 = <c>tasks:{mapId}</c>，
	/// 改动只在内存标脏、由**存档点**统一原子落盘（此前是"每个任务每日一次整文件覆盖"）。</para>
	/// </summary>
	public class TaskRepository :  GenericJsonRepository<TaskSnapshot>, ITaskRepository
	{
		private readonly string _filePath;

		/// <param name="filePath">旧口径：文件前缀（`tasks_` + mapId）；接入存档单元后不再使用。</param>
		/// <param name="store">（v0.3 / WP-3.3）统一存档单元；为 null 时按文件落盘。</param>
		public TaskRepository(string filePath, ISaveStore store = null) : base(store)
		{
			this._filePath = filePath;
		}

		/// <summary>\"已读盘\"的地图（同一地图只读一次）。</summary>
		private readonly HashSet<string> _loadedMaps = new(StringComparer.OrdinalIgnoreCase);

		public void AddTask(string mapId, TaskSnapshot task)
		{
			if (task == null) return; // 防御：null 不再被写进文件（`TIME-04`）

			EnsureLoaded(mapId); // 必须先读盘，否则会用一个只含新任务的字典覆盖旧任务（旧实现的真实缺陷）
			base.AddOrUpdate(task.Key, task, PathFor(mapId)); // 键 = `Type:UId:Id`（`TIME-03`）
		}

		public List<TaskSnapshot> GetCurrentTasks(string mapId)
		{
			EnsureLoaded(mapId);
			return base.GetAll().Where(snapshot => snapshot != null).ToList();
		}

		public void RemoveTask(string mapId, TaskSnapshot task)
		{
			if (task == null) return;

			EnsureLoaded(mapId);
			base.Remove(task.Key, PathFor(mapId)); // 真删除（`TIME-04`），而不是写 null 占位
		}

		public int RemoveTasks(string mapId, Func<TaskSnapshot, bool> match)
		{
			if (match == null) return 0;

			EnsureLoaded(mapId);
			return base.RemoveWhere(match, PathFor(mapId));
		}

		/// <summary>分区键（存档单元模式）= `tasks:{mapId}`；文件模式 = `{前缀}{mapId}`。</summary>
		private string PathFor(string mapId) => UsesSaveStore ? $"tasks:{mapId}" : _filePath + mapId;

		/// <summary>首次触碰该地图的任务文件时读盘，并顺手把历史「以 Id 为键」的条目重新编键（`TIME-03`）。</summary>
		private void EnsureLoaded(string mapId)
		{
			if (!_loadedMaps.Add(mapId)) return;

			string path = PathFor(mapId);
			base.Load(path);
			base.Reindex(snapshot => snapshot.Key, path);
		}
	}
}
