using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.2）**建筑存档 DTO**（搬迁 `MAP-02`：旧实现只存地形，建筑/单位/人口在两次调用间全丢）。
	/// <para>字段口径：`UId` 是**同一性锚点**（修正器 sourceId、迷雾、任务、建造者绑定、附属建筑都以它为准），
	/// 因此读档必须原样恢复；`Id` 是模板/等级（`WP-2.6` 起 lv.II/III 各有自己的 Id）。</para>
	/// </summary>
	public class BuildingSaveDto
	{
		public string UId { get; set; }
		public string Id { get; set; }
		public string Name { get; set; }
		public int OwnerId { get; set; }
		public bool IsReady { get; set; }

		/// <summary>建造者 uid（施工中才有；`WP-2.4` 的绑定）。</summary>
		public string BuilderUId { get; set; }

		/// <summary>训练队列（`WP-2.5`）。</summary>
		public List<TrainingOrderSave> TrainingQueue { get; set; } = new List<TrainingOrderSave>();
	}

	/// <summary>（v0.3 / WP-3.2）训练队列里的一条订单（含已锁定的单位 uid，读档后不会换实例）。</summary>
	public class TrainingOrderSave
	{
		public string UnitId { get; set; }
		public string UId { get; set; }
		public float Duration { get; set; }
		public int PopulationCost { get; set; }
		public bool IsActive { get; set; }
	}

	/// <summary>（v0.3 / WP-3.2）**单位存档 DTO**（含移动/战斗的运行时字段，否则读档后单位会"忘掉"自己在干什么）。</summary>
	public class UnitSaveDto
	{
		public string UId { get; set; }
		public string Id { get; set; }
		public string Name { get; set; }
		public int OwnerId { get; set; }
		public bool IsReady { get; set; }

		public float HP { get; set; }
		public float CurrentMP { get; set; }
		public bool IsIdle { get; set; }

		/// <summary>当前攻击目标的 uid（非空 = 读档后应重建攻击循环，`WP-2.2` 的攻击任务键 `atk_{a}_{t}`）。</summary>
		public string AttackTargetUid { get; set; }
	}
}