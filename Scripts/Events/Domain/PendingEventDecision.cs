namespace SciencePotato.Scripts.Events.Domain
{
	/// <summary>
	/// （v0.7.8 / WP-4.12）**待玩家决策的事件**：事件一旦触发就**暂停时间轴**，等玩家看过、点过确认再继续
	/// （`M2` 判据 ③"事件触发即暂停、玩家决策后继续"）。
	/// <para>为什么暂停要挂在事件上：事件多是"天降横财 / 天灾"这类**需要玩家重新规划**的信息；若时间照跑，
	/// 玩家看完弹窗损失已经发生（设计中 `design/events.md` 的口径）。</para>
	/// <para>本类只描述"待决什么"，不含 UI 文本与选项 —— 事件目前没有分支选项（`Config/Events.json` 只有
	/// 概率/前置/修正器），因此"决策"= 确认已知。将来加选项时在这里扩字段即可。</para>
	/// </summary>
	public sealed class PendingEventDecision
	{
		public string MapId { get; init; }

		public int OwnerId { get; init; }

		public string EventId { get; init; }

		public string Name { get; init; }

		/// <summary>触发当日（复盘用：暂停发生在哪一天）。</summary>
		public int Day { get; init; }

		/// <summary>持续天数（<c>-1</c> = 永久，与 `ActiveEvent` 同口径）。</summary>
		public int Duration { get; init; }

		public override string ToString()
			=> $"{Name}（{EventId}，{Day} 日触发，持续 {(Duration <= 0 ? "永久" : Duration + " 日")}）";
	}
}
