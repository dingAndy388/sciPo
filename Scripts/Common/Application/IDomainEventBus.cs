using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Application
{
	/// <summary>
	/// （v0.3 / WP-2.10）领域事件总线：**推送侧**（`ROOT-4` / `UNIT-08` / `TECH-05` / `EVT-04`）的最小落地 ——
	/// 让"完成后联动/提示/统计"有落点，而不是只能靠轮询查询（`IsReady`/`IsResearched`/`GetActiveEvents`）。
	/// <para>设计口径：</para>
	/// <list type="bullet">
	/// <item>**类型即频道**：<c>Subscribe&lt;TEvent&gt;</c> / <c>Publish&lt;TEvent&gt;</c>，不做主题字符串（避免拼错就静默丢消息）。</item>
	/// <item>**发布是同步的**：订阅者立刻在调用栈里执行（原型期没有线程/帧边界问题；真需要异步/延迟时再加队列）。</item>
	/// <item>**订阅者异常隔离**：一个订阅者抛异常不影响其余订阅者，也不打断发布方的主流程（见 <see cref="Failures"/>）。</item>
	/// </list>
	/// <para>事件类型定义见 <c>Common/Domain/DomainEvents.cs</c>。</para>
	/// </summary>
	public interface IDomainEventBus
	{
		/// <summary>订阅某类事件。同一处理函数重复订阅只生效一次。</summary>
		void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

		/// <summary>退订（发布中退订不会影响本次派发 —— 内部按快照遍历）。</summary>
		void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

		/// <summary>发布事件（同步派发给当时的所有订阅者）。</summary>
		void Publish<TEvent>(TEvent domainEvent) where TEvent : class;

		/// <summary>某类事件的订阅者数量（测试/调试用）。</summary>
		int SubscriberCount<TEvent>() where TEvent : class;

		/// <summary>订阅者抛出的异常记录（按发生顺序）；发布方不会因此中断，但排查时看得到。</summary>
		IReadOnlyList<Exception> Failures { get; }
	}
}