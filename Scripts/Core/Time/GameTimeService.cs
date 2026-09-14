using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Core.Time
{
	/// <summary>
	/// （v0.3 / WP-1.5）**游戏日节拍总线**：把 <see cref="GameClock"/> 的"日边界"转成 <see cref="ITickable"/> 的 tick。
	/// <para>为什么必须逐日派发（`A3`）：一帧可能跨多日（第三档 6 日/真实秒 × 60FPS），
	/// 若按"一帧一次 OnTick(跨过的日数)"派发，"每日掷骰"（事件 `TriggerChancePerDay` 是 %/日）、
	/// "月结"等周期就会被吞掉；<see cref="GameClock"/> 已保证逐日触发 <c>DayElapsed</c>，
	/// 本类据此每日调用一次 <c>OnTick(1 日)</c>。</para>
	/// <para>任务落盘口径（`A8` 的部分改善）：旧实现每帧为每个任务写一次 JSON（`GodotTimeService.cs:51-55`）；
	/// 现在只在**日边界**同步快照（标准档下写盘频率降为 1/60），完整"统一存档点"仍由 `WP-3.3` 收口。</para>
	/// </summary>
	public sealed class GameTimeService : ITimeService
	{
		private readonly GameClock _clock;
		private readonly ITaskRepository _tasks;
		private readonly List<ITickable> _subscribers = new();

		/// <param name="clock">权威时钟；本类订阅它的 <see cref="GameClock.DayElapsed"/>。</param>
		/// <param name="tasks">可选任务仓储：非空时在日边界同步 <see cref="IProgressTask"/> 快照（`WP-3.3` 前的最小口径）。</param>
		public GameTimeService(GameClock clock, ITaskRepository tasks = null)
		{
			_clock = clock ?? throw new ArgumentNullException(nameof(clock));
			_tasks = tasks;
			_clock.DayElapsed += OnDayElapsed;
		}

		public GameClock Clock => _clock;

		/// <summary>当前游戏日（向后兼容 `ITimeService`）。</summary>
		public int CurrentDay => (int)_clock.CurrentDay;

		/// <summary>已注册的周期任务数（测试与泄漏排查用）。</summary>
		public int SubscriberCount => _subscribers.Count;

		public void Register(ITickable tickable)
		{
			if (tickable == null || _subscribers.Contains(tickable)) return;
			_subscribers.Add(tickable);
			SyncSnapshot(tickable);
		}

		public void Unregister(ITickable tickable)
		{
			if (tickable == null) return;
			_subscribers.Remove(tickable);
			RemoveSnapshot(tickable);
		}

		/// <summary>
		/// （v0.3 / WP-2.2）范围注销：把某实体名下**所有**周期任务一次性摘掉（`TIME-02` / `CON-06`）。
		/// <para>为什么需要：`IntervalTask` 没有"完成"语义（它永远循环），所以循环任务只能由"宿主消失"来终结。
		/// 建筑被拆、单位阵亡时若不注销，任务会永久留在订阅列表里空转，并把"已死对象"的快照一直写回任务文件。</para>
		/// </summary>
		public int UnregisterByUId(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return 0;

			int removed = 0;
			for (int i = _subscribers.Count - 1; i >= 0; i--)
			{
				ITickable subscriber = _subscribers[i];
				if (!BelongsTo(subscriber, uid)) continue;

				RemoveSnapshot(subscriber);
				_subscribers.RemoveAt(i);
				removed++;
			}
			return removed;
		}

		private static bool BelongsTo(ITickable tickable, string uid)
		{
			if (tickable is not IProgressTask task) return false;
			return task.UId == uid || task.Id == uid;
		}

		/// <summary>
		/// 日边界回调。两个与生命周期有关的细节：
		/// <list type="number">
		/// <item>**迭代快照**（<c>ToArray</c>）：回调里可能注册/注销任务（例如单位阵亡时 `UnregisterByUId`），
		/// 直接按索引遍历当场变化的列表会漏派发或重复派发；快照后再用 `Contains` 复核"是否仍活跃"。</item>
		/// <item>**完成即回收**（`TIME-09`）：一次性任务完成后由总线兜底移除（连同快照），调用方不再承担注销职责；
		/// 已在回调里自行注销的任务不再写回快照（旧实现会"先注销再写回"，留下已完成任务的残影 —— `TIME-04` 的孪生缺陷）。</item>
		/// </list>
		/// </summary>
		private void OnDayElapsed(int day)
		{
			foreach (ITickable subscriber in _subscribers.ToArray())
			{
				if (!_subscribers.Contains(subscriber)) continue; // 本次派发前已被移除（例如同批回调注销了它）

				subscriber.OnTick(TimeConstants.DaysPerTick);

				if (!_subscribers.Contains(subscriber)) continue; // 回调里自行注销 → 快照已随之移除，不要再写回

				if (subscriber is IProgressTask finished && finished.IsCompleted)
				{
					_subscribers.Remove(subscriber);
					RemoveSnapshot(subscriber);
					continue;
				}

				SyncSnapshot(subscriber);
			}
		}

		private void SyncSnapshot(ITickable tickable)
		{
			if (_tasks == null || tickable is not IProgressTask progressTask) return;
			TaskSnapshot snapshot = progressTask.GetSnapshot();
			_tasks.AddTask(snapshot.MapId, snapshot);
		}

		private void RemoveSnapshot(ITickable tickable)
		{
			if (_tasks == null || tickable is not IProgressTask progressTask) return;
			TaskSnapshot snapshot = progressTask.GetSnapshot();
			_tasks.RemoveTask(snapshot.MapId, snapshot);
		}
	}
}
