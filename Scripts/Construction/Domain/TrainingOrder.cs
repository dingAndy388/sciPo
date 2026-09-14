namespace SciencePotato.Scripts.Construction.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.5）训练队列里的一个订单：**排队即锁定单位实例 uid**（<see cref="UId"/> 在入队时生成）。
	/// <para>为什么先占 uid：任务的存储键是 <c>Type:UId:Id</c>（`WP-2.2`），入队时就定下 uid
	/// 才能让"同一个建筑连续训练两个工人"各自拥有独立的任务快照（旧实现以 `UnitId` 作键 → 互相覆盖，`TIME-03`）；
	/// 同时 uid 也是"单位落位后可按 uid 取回"的凭据。</para>
	/// <para>本类型**只放数据**（不引用 <c>Units.Domain</c>）：单位实例在完成训练、确定落位格之后才创建，
	/// 传入这里预生成的 uid —— 建筑不持有单位对象，拆除/换主时不必清理引用。</para>
	/// </summary>
	public sealed class TrainingOrder
	{
		/// <summary>要训练的单位模板 Id（`UnitId`）。</summary>
		public string UnitId;

		/// <summary>单位实例 uid（入队时生成；完成时用它创建单位对象）。</summary>
		public string UId;

		/// <summary>训练时长（游戏日，取自单位配置）。</summary>
		public float Duration;

		/// <summary>该订单占用的人口（完成时扣除，见 `E2` / `WP-2.5`）。</summary>
		public int PopulationCost;

		/// <summary>是否已在训练中（每个建筑同时只允许 1 个 —— 即队列头）。</summary>
		public bool IsActive;
	}
}