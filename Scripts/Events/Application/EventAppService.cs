using Newtonsoft.Json;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Events.Application
{
	/// <summary>
	/// （v0.3 / WP-1.5 改口径 + WP-2.8 重写）事件引擎：**每个游戏日**对每个事件独立掷一次骰
	/// （`TriggerChancePerDay`，口径 = design/events.md 的「%/日」），掷中后校验前置 → 扣消耗 → 挂修正器 →
	/// 登记为"生效中"。
	/// <para>相对原实现的三处变化（`EVT-02`/`EVT-03`/`EVT-04`/`G7`/`G8`）：</para>
	/// <list type="number">
	/// <item>持续期由"再注册一个 LinearTask"改为**实例内的逐日倒计时**（`ActiveEvent`），
	/// 顺带提供"当前生效事件"查询与触发计数（UI/排查用）；</item>
	/// <item>**生效中的事件不再重复触发**（否则同一事件叠加多次修正器，数值失控）；</item>
	/// <item><see cref="StartEventsEngine"/> 幂等：同一 `(mapId, ownerId)` 只注册一个日节拍任务。</item>
	/// </list>
	/// <para>（v0.3 / WP-3.3）生效状态与触发计数**已落盘**：<see cref="SaveEvents"/> / <see cref="RestoreEvents"/>
	/// 把\"生效中事件（含剩余天数）+ 触发计数 + 掷骰次数\"交给统一存档单元，且读档后会**重挂日节拍**
	/// （时间总线被 `ITimeService.Reset()` 清空过，不重挂就会\"事件从此不再推进\"）。</para>
	/// </summary>
	public class EventAppService
	{
		private readonly IEventConfigRepository _eventRepo;
		private readonly ResourcesAppService _resources;
		private readonly TechTreesAppService _tech;
		private readonly ModifierAppService _modifier;
		private readonly ITimeService _time;
		private readonly IRandom _random;

		/// <summary>（v0.3 / WP-2.10）领域事件总线（可空 = 无人订阅）。</summary>
		private readonly IDomainEventBus _events;

		/// <summary>（v0.3 / WP-3.3）统一存档单元（可空 = 事件状态只在内存）。</summary>
		private readonly ISaveStore _store;

		/// <summary>生效中的事件：键 = `mapId_ownerId_eventId`。</summary>
		private readonly Dictionary<string, ActiveEvent> _active = new(StringComparer.Ordinal);

		/// <summary>累计触发次数：键同 <see cref="_active"/>（供 M0-2 ⑤ 与 M1 调试面板读取）。</summary>
		private readonly Dictionary<string, int> _triggerCounts = new(StringComparer.Ordinal);

		/// <summary>（v0.7.8 / WP-4.12）待玩家确认的事件（键 = `mapId|ownerId`；顺序即触发顺序）。</summary>
		private readonly Dictionary<string, List<PendingEventDecision>> _pending = new(StringComparer.Ordinal);

		/// <summary>（v0.7.8 / WP-4.12）自动暂停前的流速档位：确认后**还原**（不把玩家的档位吃掉）。</summary>
		private TimeSpeedTier _speedBeforePause = TimeSpeedTier.Standard;

		private readonly GameClock _clock;

		/// <summary>已启动事件引擎的 `(mapId, ownerId)`（`EVT-03` 幂等）。</summary>
		private readonly HashSet<string> _startedEngines = new(StringComparer.Ordinal);

		/// <summary>
		/// 累计掷骰次数（= 事件数 × 已过游戏日数；**含**因"事件正在生效"而跳过触发的那些日子 ——
		/// 它们同样是"当日的一次判定"）。用于验收"每日恰好掷一次"，而不是"每帧一次"。
		/// </summary>
		public int RollCount { get; private set; }

		/// <summary>（v0.7.8 / WP-4.12）因事件触发而**自动暂停**的次数（验收/冒烟证据）。</summary>
		public int AutoPauseCount { get; private set; }

		/// <summary>（v0.7.8 / WP-4.12）玩家确认完待决事件后**恢复时间**的次数。</summary>
		public int AutoResumeCount { get; private set; }

		public EventAppService(
			IEventConfigRepository eventRepo,
			ResourcesAppService resources,
			TechTreesAppService tech,
			ModifierAppService modifier,
			ITimeService time,
			IRandom random,
			IDomainEventBus eventBus = null,
			ISaveStore store = null,
			GameClock clock = null)
		{
			_eventRepo = eventRepo;
			_resources = resources;
			_tech = tech;
			_modifier = modifier;
			_time = time;
			_random = random;
			_events = eventBus;
			_store = store;
			_clock = clock;
		}

		/// <summary>
		/// 启动事件引擎（幂等：同一 `(mapId, ownerId)` 只注册一个日节拍任务）。
		/// </summary>
		/// <returns>（v0.6.0 / WP-4.18）本次是否**新**启动了引擎（已启动 → <c>false</c>；供启动明细与断言使用）。</returns>
		public bool StartEventsEngine(string mapId, int ownerId)
		{
			// 口径（v0.3 / WP-1.5）：每日掷一次骰（原为每秒），必须逐日派发 ——
			// 否则第三档（6 日/真实秒）一帧跨多日时会漏掷（`A3`）。
			if (!_startedEngines.Add(EngineKey(mapId, ownerId))) return false; // `EVT-03`：重复调用不再叠加引擎

			var task = new IntervalTask(0, TimeConstants.EventRollDays, $"evt_{mapId}_{ownerId}", "EventTick", "none", mapId, ownerId);
			task.OnCompleted += () => TickEvents(mapId, ownerId);
			_time.Register(task);
			return true;
		}

		/// <summary>
		/// （v0.7.8 / WP-4.12）**该势力当前待玩家确认的事件**（可能是多条：暂停期间不会触发新事件，
		/// 但读档/同一日多个事件都命中时会堆起来 ⇒ 必须**全部**确认才恢复）。
		/// </summary>
		public IReadOnlyList<PendingEventDecision> GetPendingDecisions(string mapId, int ownerId)
			=> _pending.TryGetValue(EntryKey(mapId, ownerId, string.Empty), out List<PendingEventDecision> list)
				? list
				: (IReadOnlyList<PendingEventDecision>)Array.Empty<PendingEventDecision>();

		/// <summary>待确认事件总数（跨势力；冒烟/UI 红点用）。</summary>
		public int PendingCount => _pending.Values.Sum(list => list.Count);

		/// <summary>
		/// （v0.7.8 / WP-4.12）**玩家确认一条事件**：摘掉它；该势力再无待决事件时**恢复时间**
		/// （还原到自动暂停前的流速档位）。
		/// <returns>是否真的摘掉了一条（幂等：重复确认同一条返回 <c>false</c>）。</returns>
		/// </summary>
		public bool Resolve(string mapId, int ownerId, string eventId)
		{
			string ownerKey = EntryKey(mapId, ownerId, string.Empty);
			if (!_pending.TryGetValue(ownerKey, out List<PendingEventDecision> list)) return false;

			int removed = list.RemoveAll(item => item.EventId == eventId);
			if (removed == 0) return false;

			if (list.Count == 0)
			{
				_pending.Remove(ownerKey);
				AutoResumeCount++;
				if (_clock != null)
				_clock.Speed = _speedBeforePause; // 还原玩家原来的档位（不是硬编码 Standard）
			}
			return true;
		}

		/// <summary>**一键确认该势力全部待决事件**（UI 的"知道了"按钮走它）。</summary>
		public int ResolveAll(string mapId, int ownerId)
		{
			IReadOnlyList<PendingEventDecision> pending = GetPendingDecisions(mapId, ownerId);
			int resolved = 0;
			foreach (PendingEventDecision item in pending.ToList())
				if (Resolve(mapId, ownerId, item.EventId)) resolved++;
			return resolved;
		}

		/// <summary>入队 + 自动暂停（时间轴冻结在"事件触发的那一刻"）。</summary>
		private void EnqueuePendingDecision(string mapId, int ownerId, IEventConfig evt)
		{
			string ownerKey = EntryKey(mapId, ownerId, string.Empty);
			if (!_pending.TryGetValue(ownerKey, out List<PendingEventDecision> list))
			{
				list = new List<PendingEventDecision>();
				_pending[ownerKey] = list;
			}

			list.Add(new PendingEventDecision
			{
				MapId = mapId,
				OwnerId = ownerId,
				EventId = evt.EventId,
				Name = evt.Name,
				Day = (int)(_clock?.CurrentDay ?? 0),
				Duration = evt.Duration,
			});

			if (_clock == null || _clock.IsPaused) return; // 玩家自己已经暂停了 ⇒ 不动他的档位

			_speedBeforePause = _clock.Speed;
			_clock.Speed = TimeSpeedTier.Paused;
			AutoPauseCount++;
		}

		/// <summary>(v0.6.0 / WP-4.18) 该 (mapId, ownerId) 的事件引擎是否已启动。</summary>
		/// <para>为什么需要它：事件引擎**只给人类玩家**（AI 侧不启动），"有没有启动"是装配期的可断言事实 ——
		/// 否则只能靠"等 30 天看 RollCount"这类间接证据。</para>
		/// </summary>
		public bool IsEngineStarted(string mapId, int ownerId)
			=> _startedEngines.Contains(EngineKey(mapId, ownerId));

		/// <summary>（v0.3 / WP-2.8）当前生效中的事件（`EVT-04`/`G7`：UI 可据此显示倒计时）。</summary>
		public IReadOnlyList<ActiveEvent> GetActiveEvents(string mapId, int ownerId)
			=> _active.Values
				.Where(e => e.MapId == mapId && e.OwnerId == ownerId)
				.OrderBy(e => e.EventId, StringComparer.Ordinal)
				.ToList();

		/// <summary>（v0.3 / WP-2.8）各事件累计触发次数（未触发过的事件不出现在结果里）。</summary>
		public IReadOnlyDictionary<string, int> GetTriggerCounts(string mapId, int ownerId)
		{
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (IEventConfig evt in _eventRepo.GetAllEvents())
			{
				if (evt == null || string.IsNullOrWhiteSpace(evt.EventId)) continue;
				if (_triggerCounts.TryGetValue(EntryKey(mapId, ownerId, evt.EventId), out int count))
					counts[evt.EventId] = count;
			}
			return counts;
		}

		/// <summary>（v0.3 / WP-2.8）某事件是否正在生效（`G8`：生效中不再重复触发）。</summary>
		public bool IsActive(string mapId, int ownerId, string eventId)
			=> _active.ContainsKey(EntryKey(mapId, ownerId, eventId));

		// ────────────── 存档（v0.3 / WP-3.3） ──────────────
		// 背景：修正器早就落盘了，但\"生效中的事件 + 触发计数\"只在内存 ——
		// 读档后事件没了、修正器还在，等于\"没有到期日的加成\"。这里把状态交给统一存档单元。

		/// <summary>**存档点**：把该地图的事件状态写进存档单元的分区 `events:{mapId}`（无存档单元时只在内存）。</summary>
		public void SaveEvents(string mapId)
		{
			if (_store == null || string.IsNullOrWhiteSpace(mapId)) return;

			string prefix = mapId + "_";
			var dto = new EventSaveDto { RollCount = RollCount };

			foreach (ActiveEvent active in _active.Values)
			{
				if (active.MapId != mapId) continue;
				if (!active.IsPermanent && active.RemainingDays <= 0) continue; // 已到期的不写（读档即等价于已回收）

				dto.Active.Add(new ActiveEventSave
				{
					OwnerId = active.OwnerId,
					EventId = active.EventId,
					Name = active.Name,
					TotalDays = active.TotalDays,
					RemainingDays = active.RemainingDays,
				});
			}

			foreach (var kvp in _triggerCounts)
				if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
					dto.TriggerCounts[kvp.Key] = kvp.Value;

			_store.WriteSection($"events:{mapId}", JsonConvert.SerializeObject(dto, Formatting.Indented));
		}

		/// <summary>
		/// **读档**：恢复生效中事件（含剩余天数）、触发计数与掷骰次数。
		/// <para>⚠️ 同时**注销引擎登记**：读档会清空时间总线（`ITimeService.Reset()`），日节拍任务已不存在，
		/// 因此必须让随后的 <see cref="StartEventsEngine"/> 重新注册 —— 否则\"事件从此不再推进\"（`EVT-03` 幂等的陷阱）。</para>
		/// </summary>
		/// <returns>是否从存档里读到了事件状态。</returns>
		public bool RestoreEvents(string mapId, int ownerId)
		{
			if (string.IsNullOrWhiteSpace(mapId)) return false;

			// 时间总线已作废：本次读档必须重挂日节拍（幂等标志一并清掉）
			_startedEngines.Remove(EngineKey(mapId, ownerId));

			if (_store == null) return false;

			string json = _store.ReadSection($"events:{mapId}");
			if (string.IsNullOrWhiteSpace(json)) return false;

			EventSaveDto dto;
			try
			{
				dto = JsonConvert.DeserializeObject<EventSaveDto>(json);
			}
			catch (JsonException)
			{
				return false; // 认不出的分区：按\"没有生效事件\"处理，不让读档失败
			}

			if (dto == null) return false;

			// 先清掉该地图的旧内存态（读档 = 以盘上状态为准）
			foreach (string key in _active.Keys.Where(k => k.StartsWith(mapId + "_", StringComparison.Ordinal)).ToList())
				_active.Remove(key);
			foreach (string key in _triggerCounts.Keys.Where(k => k.StartsWith(mapId + "_", StringComparison.Ordinal)).ToList())
				_triggerCounts.Remove(key);

			foreach (ActiveEventSave saved in dto.Active ?? new List<ActiveEventSave>())
			{
				if (saved == null || string.IsNullOrWhiteSpace(saved.EventId)) continue;

				_active[EntryKey(mapId, saved.OwnerId, saved.EventId)] =
					new ActiveEvent(mapId, saved.OwnerId, saved.EventId, saved.Name, saved.TotalDays, saved.RemainingDays);
			}

			foreach (var kvp in dto.TriggerCounts ?? new Dictionary<string, int>())
				_triggerCounts[kvp.Key] = kvp.Value;

			RollCount = dto.RollCount;
			return true;
		}

		private void TickEvents(string mapId, int ownerId)
		{
			var events = _eventRepo.GetAllEvents();
			foreach (var evt in events)
			{
				if (evt == null || string.IsNullOrWhiteSpace(evt.EventId)) continue;

				RollCount++; // 每个游戏日对每个事件恰好计一次（生效中被跳过的日子也算"当日的一次判定"）
				if (IsActive(mapId, ownerId, evt.EventId)) continue; // `G8`

				if (!_random.ProbCodition(evt.TriggerChancePerDay)) continue;

				if (!AllPrerequisitesMet(mapId, ownerId, evt)) continue;

				ConsumePrerequisites(mapId, ownerId, evt);

				if (evt.Modifiers != null && evt.Modifiers.Count > 0)
				{
					_modifier.AddModifiers(mapId, ownerId, evt.EventId, evt.Modifiers);
				}

				string key = EntryKey(mapId, ownerId, evt.EventId);
				_active[key] = new ActiveEvent(mapId, ownerId, evt.EventId, evt.Name, evt.Duration);
				_triggerCounts[key] = _triggerCounts.TryGetValue(key, out int count) ? count + 1 : 1;

				// 推送（v0.3 / WP-2.10 / `EVT-04`）：触发瞬间的推送（原本只能轮询 `GetActiveEvents`）
				_events?.Publish(new GameEventTriggeredEvent(mapId, ownerId, evt.EventId, evt.Name, evt.Duration));

			// （v0.7.8 / WP-4.12）**触发即暂停**：把事件挂进待决队列并冻结时间轴，等玩家确认再继续
			EnqueuePendingDecision(mapId, ownerId, evt);
			}

			// 持续期推进放在**当日结算之后**：触发当日不计入，次日开始每天减 1，
			// 减到 0 即到期（回收修正器）—— 因此 `Duration=30` 的效果正好覆盖触发后的 30 个游戏日。
			AdvanceActiveEvents(mapId, ownerId);
		}

		/// <summary>逐日推进生效中事件的倒计时；到期的回收修正器并移出列表。</summary>
		private void AdvanceActiveEvents(string mapId, int ownerId)
		{
			foreach (ActiveEvent active in GetActiveEvents(mapId, ownerId))
			{
				if (!active.Tick()) continue;

				_modifier.RemoveModifiersBySourceId(mapId, ownerId, active.EventId);
				_active.Remove(EntryKey(mapId, ownerId, active.EventId));
			}
		}

		private static string EngineKey(string mapId, int ownerId) => $"{mapId}_{ownerId}";

		private static string EntryKey(string mapId, int ownerId, string eventId) => $"{mapId}_{ownerId}_{eventId}";

		private bool AllPrerequisitesMet(string mapId, int ownerId, IEventConfig evt)
		{
			if (evt.ResourcePrerequisites != null)
			{
				foreach (var kvp in evt.ResourcePrerequisites)
				{
					var contract = _resources.CreateResourceConsumption(new Consumption(kvp.Key, kvp.Value), mapId, ownerId);
					if (!contract.IsConsumable()) return false;
				}
			}

			if (evt.TechPrerequisites != null)
			{
				foreach (var kvp in evt.TechPrerequisites)
				{
					var req = _tech.GetTechTreeRequirement(mapId, ownerId, kvp.Key, kvp.Value);
					if (!req.IsMet()) return false;
				}
			}

			return true;
		}

		private void ConsumePrerequisites(string mapId, int ownerId, IEventConfig evt)
		{
			if (evt.ResourcePrerequisites != null)
			{
				foreach (var kvp in evt.ResourcePrerequisites)
				{
					var contract = _resources.CreateResourceConsumption(new Consumption(kvp.Key, kvp.Value), mapId, ownerId);
					contract.Consume();
				}
			}
		}
	}
}