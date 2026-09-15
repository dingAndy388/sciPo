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
	/// （v0.3 / WP-3.2 建，WP-3.3 收敛为存档单元的使用者）**世界存档/读档的协调者**：
	/// 把"六类状态"（地图实体 / 人口 / 任务 / 迷雾 / 资源与科技 / 时间）+ 事件状态的
	/// 写入点与恢复点串起来，供 M0-3 ①「存档 → 读档 → 推进 360 日 → 逐项等价」验收使用。
	/// <para><b>本类只做编排，不做序列化</b>：格式与映射属于各仓储（`SaveMapper` + 各自的 Repository）。</para>
	/// <para>（v0.3 / WP-3.3）注入 <see cref="ISaveStore"/> 后：各仓储的写入只是**在统一存档里标脏**，
	/// 真正的落盘由本类在 <see cref="SaveWorld"/> 末尾**一次原子写**完成（`I3`/`DEP-06`/`TIME-08`）；
	/// 读档先让存档单元跑**版本迁移**，再按依赖顺序恢复（地图实体 → 附加状态 → 迷雾 → 任务 → 事件引擎）。</para>
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
		private readonly ISaveStore _store;

		/// <summary>（v0.3 / WP-3.9）月度经济结算器（可空 = 本宿主不使用月度结算）。</summary>
		private readonly MonthlySettlementService _settlement;

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
			FogAppService fog = null,
			ISaveStore store = null,
			MonthlySettlementService settlement = null)
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
			_store = store;
			_settlement = settlement;
		}

		/// <summary>最近一次读档恢复的任务数（验收/调试用）。</summary>
		public int LastRestoredTaskCount { get; private set; }

		/// <summary>最近一次读档是否成功（更高版本的存档 → <c>false</c> + <see cref="LastLoadError"/>）。</summary>
		public bool LastLoadSucceeded { get; private set; } = true;

		/// <summary>最近一次读档的失败原因（无失败为 <c>null</c>）。</summary>
		public string LastLoadError { get; private set; }

		/// <summary>最近一次加载应用的存档格式迁移名（升序；空 = 档是当前版本）。</summary>
		public IReadOnlyList<string> LastAppliedMigrations => _store?.AppliedMigrations ?? new List<string>();

		/// <summary>
		/// **存档点**：写时钟（统一存档时进文件头）+ 把所有脏地图（含实体与人口）落盘 + 迷雾 + 事件状态，
		/// 最后**一次原子写**统一存档。
		/// <para>为什么必须由这里统一写：改造前每个子系统各写各的文件（五个版本号、无原子性、写放大），
		/// 「谁负责落盘」没有唯一答案 —— 存档点写完一半崩溃就会得到\"地图是新的、任务是旧的\"。</para>
		/// </summary>
		public void SaveWorld(string mapId, int ownerId = 1)
		{
			_clockRepo?.SaveDay(_session.SessionId, _session.Clock.CurrentDay);

			if (mapId != null) _session.Maps.MarkDirty(mapId);
			_session.Maps.FlushAll();

			// 迷雾：`FogAppService` 只在内存里维护矩阵，必须显式写（此前没有任何存档点写它 —— `WP-3.2` 补齐）
			if (mapId != null) _fog?.Save(mapId);

			// 事件状态（v0.3 / WP-3.3）：生效中事件 + 触发计数 + 掷骰次数
			if (mapId != null) _events?.SaveEvents(mapId);

			// 月度结算状态（v0.3 / WP-3.9）：连续赤字月数（`WP-3.10` 的减员输入）—— 与事件状态同一套路
			if (mapId != null) _settlement?.SaveSettlement(mapId);

			// 统一落盘（原子写）：任务/资源/修正器/科技/迷雾/事件此前已经在各自写入点标脏
			_store?.Commit();
		}

		/// <summary>
		/// **读档**：① 让存档单元读盘并跑版本迁移（更高版本 → 拒绝并返回 false）；② 恢复时钟；
		/// ③ 丢弃内存地图并重新读盘（拿到含占据物/人口的新实例）；④ 恢复建筑附加状态（训练队列、建造者绑定）；
		/// ⑤ 按类型重建全部周期任务；⑥ 恢复事件状态并重启事件引擎（幂等 + 重挂日节拍）。
		/// </summary>
		/// <summary>
		/// **读档（多势力）**：与 <see cref="LoadWorld(string, int)"/> 同一流程，但**按玩家表逐 owner 恢复**
		/// 月度结算状态 / 周期任务 / 事件引擎。
		/// <para>（v0.9.6 / `WP-5.11` / `U7`）旧口径只恢复传入的那一个 owner ⇒ 多个 AI 势力时，
		/// 读档后 AI 的月结、成长、训练任务全部丢失（`--ai=1` 冒烟实测：9 条周期任务只回来 4 条）。</para>
		/// </summary>
		public bool LoadWorld(string mapId, IReadOnlyList<int> ownerIds)
		{
			if (ownerIds == null || ownerIds.Count == 0) ownerIds = new[] { 1 };

			if (string.IsNullOrWhiteSpace(mapId)) return false;

			LastLoadSucceeded = true;
			LastLoadError = null;

			// ① 存档单元：读盘 + 迁移（版本过新直接拒绝，不猜字段）
			try
			{
				_store?.Load();
			}
			catch (SaveVersionTooNewException ex)
			{
				LastLoadSucceeded = false;
				LastLoadError = ex.Message;
				return false;
			}

			// ② 时间（统一存档：日期来自文件头）
			_session.Clock.RestoreDay(_clockRepo?.LoadDay(_session.SessionId) ?? 0d);

			// ③ 地图（含实体/人口）：先驱逐缓存再取回，确保拿到磁盘上的最新状态
			_session.Maps.Evict(mapId);

			// ③.5 时间总线作废（`WP-3.2`）：旧订阅者持有的是**读档前**的实体副本，
			// 不清理会让"恢复的新任务"与"旧任务"同时跑（月结翻倍、人口翻倍）
			_time.Reset();

			// ③.6 交战循环登记同样作废（v0.3 / WP-3.6）：上一步已把旧订阅者全摘掉，
			// 若战斗侧的"已挂上"登记表还留着键，随后的 `RestoreAttackLoop` 会被自己挡掉
			_units?.Combat?.ResetLoops();

			if (_session.Maps.Get(mapId) == null) return LastLoadSucceeded; // 无存档：无从恢复

			// ④ 建筑附加状态（队列/建造者绑定都在建筑对象上，但回调需要重新挂）
			_construction?.RestoreBuildingExtras(mapId);

			// ④.5 迷雾（`FogAppService` 是内存矩阵，读档必须显式加载，否则地图全黑 —— `WP-3.2` 补齐）
			_fog?.Load(mapId);

			// ④.6 / ⑤ / ⑥：**按玩家表逐 owner** 恢复月度结算状态 → 周期任务 → 事件状态与引擎
			// （顺序不能反：先恢复结算并作废幂等登记，再由任务快照重建，否则"刚恢复的任务"会被当成重复登记）
			foreach (int owner in ownerIds)
				_settlement?.RestoreSettlement(mapId, owner);

			LastRestoredTaskCount = RestoreTasks(mapId, ownerIds);

			foreach (int owner in ownerIds)
			{
				// （v0.9.9 / `WP-5.11` 收口）**事件引擎只给人类玩家**（`G8` / `WP-4.12`：随机事件要打断的是玩家的决策）；
				// `SessionOrchestrator.StartMap` 就是这个口径。旧实现给每个 owner 都挂 ⇒ 读档后 AI 多出一条
				// `EventTick` 任务（用例实测 owner=2 的任务数 7 → 8），且 AI 会在每次日节拍上白跑事件掷骰。
				if (!_session.IsHuman(owner)) continue;

				_events?.RestoreEvents(mapId, owner);
				_events?.StartEventsEngine(mapId, owner);
			}

			return LastLoadSucceeded;
		}

		/// <summary>
		/// **读档（单势力）**：`WP-5.11` 之前的口径（只恢复一个 owner）。保留它以便既有调用方/用例平滑，
		/// 多势力场景请用 <see cref="LoadWorld(string, IReadOnlyList{int})"/>。
		/// </summary>
		public bool LoadWorld(string mapId, int ownerId) => LoadWorld(mapId, new[] { ownerId });

		/// <summary>
		/// 按任务类型分发恢复：
		/// <list type="bullet">
		/// <item>**一次性任务**（施工/升级/训练/研究）：每个快照对应一个任务，按其实体重建并回填进度；</item>
		/// <item>**周期任务**（资源月结/人口增长/单位移动与攻击）：按**实体**整体重建（每个实体一条），
		/// 进度从快照表回填 —— 否则"每格一行快照"会变成重复注册。</item>
		/// </list>
		/// </summary>
		private int RestoreTasks(string mapId, IReadOnlyList<int> ownerIds)
		{
			List<TaskSnapshot> snapshots = _tasks?.GetCurrentTasks(mapId) ?? new List<TaskSnapshot>();
			if (snapshots.Count == 0) return 0;

			var progress = new Dictionary<string, float>(StringComparer.Ordinal);
			foreach (TaskSnapshot snapshot in snapshots) progress[snapshot.Key] = snapshot.Progress;

			// ① **与 owner 无关**的那部分：快照里本来就带 `OwnerId`，逐个重建即可（每类只重建一次，不能按 owner 重复调）
			int restored = RestoreEntityTasks(mapId, snapshots);
			restored += _units?.RestoreUnitTasks(mapId, progress) ?? 0;

			// ② **按 owner 注册**的那部分（周期任务）：每方一条，`U7` 的缺口就在这里
			foreach (int ownerId in ownerIds)
			{
				restored += _resources?.RestoreGrowthTasks(mapId, ownerId, progress) ?? 0;
				restored += _construction?.RestoreHousingTasks(mapId, ownerId, progress) ?? 0;
				restored += _settlement?.RestoreSettlementTask(mapId, ownerId, progress) ?? 0;
			}

			return restored;
		}

		/// <summary>按任务类型重建**实体类**任务（建造 / 升级 / 训练 / 研究）：快照自带 owner ⇒ 与调用者无关。</summary>
		private int RestoreEntityTasks(string mapId, List<TaskSnapshot> snapshots)
		{
			int restored = 0;
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
						break; // 周期任务见 `RestoreTasks` 的第 ② 步
				}
			}
			return restored;
		}

		/// <summary>
		/// **读档（旧签名，保留）**：只恢复一个 owner 的周期任务。
		/// </summary>
		private int RestoreTasks(string mapId, int ownerId) => RestoreTasks(mapId, new[] { ownerId });

		private int Register(IProgressTask task)
		{
			if (task == null) return 0;

			_time.Register(task);
			return 1;
		}
	}
}
