using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-2.10）领域事件总线的无依赖实现。
	/// <para>三个健壮性口径（都用例锁住）：</para>
	/// <list type="number">
	/// <item>**派发用快照**：订阅者可以在处理函数里订阅/退订，不影响本次派发（与 `GameTimeService` 的日派发同一个教训）；</item>
	/// <item>**异常隔离**：某个订阅者抛异常 → 记入 <see cref="Failures"/> 并继续通知其余订阅者，**不打断发布方的主流程**
	/// （建筑完工/单位阵亡这类流程不能因为一个 UI 回调坏掉而中断）；</item>
	/// <item>**重复订阅幂等**：同一个处理函数重复 `Subscribe` 只注册一次（避免"切场景重订阅"导致同一提示弹两次）。</item>
	/// </list>
	/// </summary>
	public sealed class DomainEventBus : Application.IDomainEventBus
	{
		private readonly Dictionary<Type, List<Delegate>> _handlers = new();
		private readonly List<Exception> _failures = new();

		public IReadOnlyList<Exception> Failures => _failures;

		public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
		{
			if (handler == null) return;

			List<Delegate> list = Channel(typeof(TEvent));
			if (!list.Contains(handler)) list.Add(handler);
		}

		public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
		{
			if (handler == null) return;
			if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate> list)) return;

			list.Remove(handler);
		}

		public void Publish<TEvent>(TEvent domainEvent) where TEvent : class
		{
			if (domainEvent == null) return;
			if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate> list) || list.Count == 0) return;

			// 快照：处理函数里的订阅/退订不改变本次派发的名单
			foreach (Delegate handler in list.ToArray())
			{
				try
				{
					((Action<TEvent>)handler).Invoke(domainEvent);
				}
				catch (Exception ex)
				{
					// 订阅者自己的问题不应该让玩法流程崩掉（但要留痕）
					_failures.Add(ex);
				}
			}
		}

		public int SubscriberCount<TEvent>() where TEvent : class
			=> _handlers.TryGetValue(typeof(TEvent), out List<Delegate> list) ? list.Count : 0;

		private List<Delegate> Channel(Type eventType)
		{
			if (!_handlers.TryGetValue(eventType, out List<Delegate> list))
			{
				list = new List<Delegate>();
				_handlers[eventType] = list;
			}
			return list;
		}
	}
}