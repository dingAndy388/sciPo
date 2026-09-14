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
		/// 日边界回调：**倒序**遍历，使"在 OnCompleted 里注销自己"的任务（`IntervalTask` 完成即 `Unregister`）
		/// 不会因为列表位移而漏掉同一日的后续订阅者。
		/// </summary>
		private void OnDayElapsed(int day)
		{
			for (int i = _subscribers.Count - 1; i >= 0; i--)
			{
				ITickable subscriber = _subscribers[i];
				subscriber.OnTick(TimeConstants.DaysPerTick);
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
