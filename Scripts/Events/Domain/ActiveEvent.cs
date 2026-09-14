namespace SciencePotato.Scripts.Events.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.8）**生效中的事件**：把"事件触发了"从"挂了个修正器就没人知道"变成可查询的状态
	/// （`EVT-02` 的"永久事件无撤销入口"、`EVT-04` 的"无当前生效事件查询"、`G7` 的最小落地）。
	/// <para>持续期口径：`Duration` 的**单位是游戏日**（0 = 永久）。**触发当日起算为第 1 日**，
	/// 每个游戏日结束时 `RemainingDays` 减 1，减到 0 即到期（修正器被回收）——
	/// 因此 `Duration=10` 的效果覆盖触发后的第 1~10 个游戏日。</para>
	/// </summary>
	public sealed class ActiveEvent
	{
		public string MapId { get; }

		public int OwnerId { get; }

		public string EventId { get; }

		/// <summary>事件名（来自配置）：UI 展示"当前生效事件"用（`EVT-04`）。</summary>
		public string Name { get; }

		/// <summary>配置里的持续天数（0 = 永久）。</summary>
		public int TotalDays { get; }

		/// <summary>剩余天数；永久事件恒为 <see cref="Permanent"/>（-1）。</summary>
		public int RemainingDays { get; private set; }

		/// <summary>永久事件的剩余天数哨兵值。</summary>
		public const int Permanent = -1;

		public bool IsPermanent => TotalDays <= 0;

		public ActiveEvent(string mapId, int ownerId, string eventId, string name, int durationDays)
			: this(mapId, ownerId, eventId, name, durationDays, durationDays > 0 ? durationDays : Permanent)
		{
		}

		/// <summary>
		/// （v0.3 / WP-3.3）**读档用**：显式给出剩余天数（存档里的倒计时必须原样接上，
		/// 否则\"第 30 日生效、持续 30 日\"的事件读档后会重新满血）。
		/// </summary>
		public ActiveEvent(string mapId, int ownerId, string eventId, string name, int durationDays, int remainingDays)
		{
			MapId = mapId;
			OwnerId = ownerId;
			EventId = eventId;
			Name = name;
			TotalDays = durationDays > 0 ? durationDays : 0;
			RemainingDays = IsPermanent ? Permanent : remainingDays;
		}

		/// <summary>推进一个游戏日；返回 <c>true</c> 表示本次推进后到期（调用方负责回收修正器）。</summary>
		public bool Tick()
		{
			if (IsPermanent) return false;

			RemainingDays--;
			return RemainingDays <= 0;
		}

		public override string ToString()
			=> IsPermanent ? $"{EventId}(永久)" : $"{EventId}(剩余 {RemainingDays}/{TotalDays} 日)";
	}
}
