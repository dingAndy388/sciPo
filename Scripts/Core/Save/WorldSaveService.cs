using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.Units.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core.Save
{
	/// <summary>
	/// （v0.3 / WP-3.2）**世界存档/读档的协调者**：把"六类状态"（地图实体 / 人口 / 任务 / 迷雾 / 资源与科技 / 时间）
	/// 的写入点与恢复点串起来，供 M0-3 ①「存档 → 读档 → 推进 360 日 → 逐项等价」验收使用。
	/// <para><b>本类只做编排，不做序列化</b>：格式与映射属于各仓储（`SaveMapper` + 各自的 Repository）。
	/// 它的存在是因为"读档"必须**按依赖顺序**执行：先恢复地图实体（否则任务找不到宿主），再恢复时钟，
	/// 最后重建周期任务与事件引擎。</para>
	/// <para>`WP-3.3` 会把本类收敛为统一的存档单元（单一文件 + 原子写 + 版本迁移），当前版本仍然
	/// "各仓各写各的盘"，但**入口已经唯一**。</para>
	/// </summary>
	public sealed class WorldSaveService
	{
		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly IClockRepository _clockRepo;
		private readonly ITaskRepository _tasks;
		private readonly ITimeService _time;
		private readonly ConstructionAppService _construction;
		private readonly UnitsAppService _units;
		private readonly TechTreesAppService _tech;
		private readonly ResourcesAppService _resources;
		private readonly EventAppService _events;
		private readonly FogAppService _fog;

		public WorldSaveService(
			GameSession session,
			MapAppService map,
			ITimeService time,
			IClockRepository clockRepo,
			ITaskRepository tasks,
			ConstructionAppService construction,
			UnitsAppService units,
			TechTreesAppService tech,
			ResourcesAppService resources,
			EventAppService events = null,
			FogAppService fog = null)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_time = time ?? throw new ArgumentNullException(nameof(time));
			_map = map;
			_clockRepo = clockRepo;
			_tasks = tasks;
			_construction = construction;
			_units = units;
			_tech = tech;
			_resources = resources;
			_events = events;
			_fog = fog;
		}

		/// <summary>最近一次读档恢复的任务数（验收/调试用）。</summary>
		public int LastRestoredTaskCount { get; private set; }

		/// <summary>
		/// **存档点**：写时钟 + 把所有脏地图（含实体与人口）落盘。
		/// <para>资源/修正器/科技/迷雾/任务都在各自的写入点落盘（`WP-1.5` 起任务降为日边界同步），
		/// 因此这里不重复写；它们的"统一在一个存档点写"由 `WP-3.3` 完成。</para>
		/// </summary>
		public void SaveWorld(string mapId)
		{
			_clockRepo?.SaveDay(_session.SessionId, _session.Clock.CurrentDay);

			if (mapId != null) _session.Maps.MarkDirty(mapId);
			_session.Maps.FlushAll();

			// 迷雾：`FogAppService` 只在内存里维护矩阵，必须显式写盘（此前没有任何存档点写它 —— `WP-3.2` 补齐）
			if (mapId != null) _fog?.Save(mapId);

			// 任务/资源/修正器/科技：各自在写入点落盘（任务为日边界同步），此处重复写只会放大 IO
		}

		/// <summary>
		/// **读档**：① 恢复时钟；② 丢弃内存地图并重新读盘（拿到含占据物/人口的新实例）；
		/// ③ 恢复建筑附加状态（训练队列、建造者绑定）；④ 按类型重建全部周期任务；⑤ 重启事件引擎（幂等）。
		/// </summary>
		public void LoadWorld(string mapId, int ownerId)
		{
			if (string.IsNullOrWhiteSpace(mapId)) return;

			// ① 时间
			_session.Clock.RestoreDay(_clockRepo?.LoadDay(_session.SessionId) ?? 0d);

			// ② 地图（含实体/人口）：先驱逐缓存再取回，确保拿到磁盘上的最新状态
			_session.Maps.Evict(mapId);

			// ②.5 时间总线作废（`WP-3.2`）：旧订阅者持有的是**读档前**的实体副本，
			// 不清理会让"恢复的新任务"与"旧任务"同时跑（月结翻倍、人口翻倍）
			_time.Reset();

			if (_session.Maps.Get(mapId) == null) return; // 无存档：无从恢复

			// ③ 建筑附加状态（队列/建造者绑定都在建筑对象上，但回调需要重新挂）
			_construction?.RestoreBuildingExtras(mapId);

			// ③.5 迷雾（`FogAppService` 是内存矩阵，读档必须显式加载，否则地图全黑 —— `WP-3.2` 补齐）
			_fog?.Load(mapId);

			// ④ 周期任务
			LastRestoredTaskCount = RestoreTasks(mapId, ownerId);

			// ⑤ 事件引擎（`StartEventsEngine` 幂等）
			_events?.StartEventsEngine(mapId, ownerId);
		}

		/// <summary>
		/// 按任务类型分发恢复：
		/// <list type="bullet">
		/// <item>**一次性任务**（施工/升级/训练/研究）：每个快照对应一个任务，按其实体重建并回填进度；</item>
		/// <item>**周期任务**（资源月结/人口增长/单位移动与攻击）：按**实体**整体重建（每个实体一条），
		/// 进度从快照表回填 —— 否则"每格一行快照"会变成重复注册。</item>
		/// </list>
		/// </summary>
		private int RestoreTasks(string mapId, int ownerId)
		{
			List<TaskSnapshot> snapshots = _tasks?.GetCurrentTasks(mapId) ?? new List<TaskSnapshot>();

			int restored = 0;
			var progress = new Dictionary<string, float>(StringComparer.Ordinal);
			foreach (TaskSnapshot snapshot in snapshots) progress[snapshot.Key] = snapshot.Progress;

			foreach (TaskSnapshot snapshot in snapshots)
			{
				switch (snapshot.Type)
				{
					case "Construction":
						restored += Register(_construction?.ResumeConstruction(mapId, snapshot));
						break;

					case "Upgrade":
						restored += Register(_construction?.ResumeUpgrade(mapId, snapshot));
						break;

					case "Training":
						restored += Register(_units?.ResumeTraining(mapId, snapshot));
						break;

					case "Research":
						restored += Register(_tech?.CreateResearchTask(mapId, snapshot.OwnerId, snapshot));
						break;

					default:
						break; // 周期任务见下方按实体重建
				}
			}

			if (snapshots.Count > 0)
			{
				restored += _resources?.RestoreGrowthTasks(mapId, ownerId, progress) ?? 0;
				restored += _construction?.RestoreHousingTasks(mapId, ownerId, progress) ?? 0;
				restored += _units?.RestoreUnitTasks(mapId, progress) ?? 0;
			}

			return restored;
		}

		private int Register(IProgressTask task)
		{
			if (task == null) return 0;

			_time.Register(task);
			return 1;
		}
	}
}
