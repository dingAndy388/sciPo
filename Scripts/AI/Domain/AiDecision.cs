using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.AI.Domain
{
	/// <summary>（v0.7.3 / WP-6.2）威胁等级（`design/AI.md` 的五档；"无威胁"= 没看见任何敌方单位）。</summary>
	public enum AiThreatLevel
	{
		None = 0,
		Low = 1,
		Medium = 2,
		High = 3,
		Lethal = 4,
	}

	/// <summary>
	/// （v0.7.3 / WP-6.2）本轮决策的**侧重点**（`design/AI.md` 的三级优先级，顺序即优先级）：
	/// 生存 → 威胁 → 发展。
	/// </summary>
	public enum AiFocus
	{
		/// <summary>人口/口粮告急：先保命（产食物、补住房）。</summary>
		Survival = 0,

		/// <summary>视野里有敌人：按威胁等级调整军事投入。</summary>
		Threat = 1,

		/// <summary>无威胁或低威胁：优先建造与科研。</summary>
		Development = 2,
	}

	/// <summary>
	/// （v0.7.3 / WP-6.2）**AI 的观测**：它"看得见"的东西 —— 只包含自己的资产与自己视野内的敌方单位。
	/// <para>信息公平（`design/AI.md`：不无视迷雾）在这里落地：视野 = **自己单位的 `VisionRadius` 覆盖圈内**
	/// 的敌方单位；本类不做全图扫描、也不读玩家迷雾。</para>
	/// <para>为什么现在就能做而不用等按 owner 的迷雾（`WP-4.10`）：AI 的视野可以**几何计算**（谁的圈盖到谁），
	/// 这比"借玩家的迷雾矩阵"更准确；`WP-6.5` 会在 `WP-4.10` 落地后改用统一的 owner 迷雾实现。</para>
	/// </summary>
	public sealed class AiObservation
	{
		public string MapId { get; init; }

		public int OwnerId { get; init; }

		public int Day { get; init; }

		public int UnitCount { get; init; }

		public int BuildingCount { get; init; }

		/// <summary>是否有住房（能长人口）。</summary>
		public bool HasHousing { get; init; }

		/// <summary>口粮存量（`Settlement.DemandResource`）。</summary>
		public float FoodStock { get; init; }

		/// <summary>每月口粮需求（人口 × 每人口需求）。</summary>
		public float FoodDemandPerMonth { get; init; }

		/// <summary>视野内可见的**敌方单位**数（不含建筑；野怪也算敌方 —— 同一套 `ownerId` 口径）。</summary>
		public int VisibleEnemies { get; init; }

		/// <summary>自己的最大视野半径（格）。</summary>
		public int MaxVisionRadius { get; init; }

		/// <summary>口粮还能撑几个月（需求为 0 时返回 <see cref="float.PositiveInfinity"/>）。</summary>
		public float FoodMonthsLeft => FoodDemandPerMonth <= 0f ? float.PositiveInfinity : FoodStock / FoodDemandPerMonth;

		public override string ToString()
			=> $"owner={OwnerId} {Day}日 单位×{UnitCount} 建筑×{BuildingCount} 口粮={FoodStock:0.#}({FoodMonthsLeft:0.#}月) 可见敌={VisibleEnemies}";
	}

	/// <summary>
	/// （v0.7.3 / WP-6.2）**一次决策的产物**：侧重点 + 威胁等级 + 本轮的投入意向 + 为什么这么判。
	/// <para>`WP-6.3`（经济）与 `WP-6.4`（军事）消费它去下单；本 WP 只负责"判断 + 记录"，
	/// 这样"AI 为什么这么做"永远可查（`N3` 复看时靠它复盘）。</para>
	/// </summary>
	public sealed class AiDecision
	{
		public string MapId { get; init; }

		public int OwnerId { get; init; }

		public int Day { get; init; }

		public AiFocus Focus { get; init; }

		public AiThreatLevel Threat { get; init; }

		/// <summary>本轮计划的军事投入比例（0 = 不造兵：前期窗口内或口粮告急）。</summary>
		public float PlannedMilitaryShare { get; init; }

		/// <summary>人类可读的判定理由（进日志/复盘）。</summary>
		public string Reason { get; init; }

		/// <summary>触发本轮的观测快照（只读）。</summary>
		public AiObservation Observation { get; init; }

		public override string ToString()
			=> $"owner={OwnerId} {Day}日 focus={Focus} 威胁={Threat} 军费占比={PlannedMilitaryShare:0.##} —— {Reason}";
	}
}
