using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.3）**事件引擎的存档形态**：\"生效中的事件 + 累计触发次数 + 累计掷骰次数\"。
	/// <para>为什么必须落盘：修正器在盘上、事件的\"持续期与计数\"只在内存 —— 读档后\"生效中的事件\"消失，
	/// 但它的修正器还在，于是得到\"没有到期日的加成\"（数值解释不通）；触发计数与掷骰次数归零还会让
	/// \"每日恰好掷一次\"的验收在跨档后失真。</para>
	/// </summary>
	public sealed class EventSaveDto
	{
		/// <summary>累计掷骰次数（= 事件数 × 已过游戏日数）。</summary>
		public int RollCount { get; set; }

		/// <summary>生效中的事件（不含倒计时为 0 的条目）。</summary>
		public List<ActiveEventSave> Active { get; set; } = new List<ActiveEventSave>();

		/// <summary>累计触发次数：键与 <c>EventAppService</c> 内存口径一致（`{mapId}_{ownerId}_{eventId}`）。</summary>
		public Dictionary<string, int> TriggerCounts { get; set; } = new Dictionary<string, int>();
	}

	/// <summary>（v0.3 / WP-3.3）生效中事件的一条记录。</summary>
	public sealed class ActiveEventSave
	{
		public int OwnerId { get; set; }

		public string EventId { get; set; }

		/// <summary>事件名（配置快照）：UI 读档后仍能显示\"当前生效事件\"。</summary>
		public string Name { get; set; }

		/// <summary>配置里的持续天数（0 = 永久）。</summary>
		public int TotalDays { get; set; }

		/// <summary>剩余天数（永久 = -1）。</summary>
		public int RemainingDays { get; set; }
	}
}
