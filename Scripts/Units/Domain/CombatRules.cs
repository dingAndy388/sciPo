using SciencePotato.Scripts.Common.Domain;
using System;

namespace SciencePotato.Scripts.Units.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.6 / `E9`~`E12`、`UNIT-12`）**战斗规则的唯一出处**。
	/// <para>把设计稿里三条容易被各写一份的口径收在这里，并做成**纯函数** —— 无头用例可以直接断言公式，
	/// 不必先把单位摆到地图上：</para>
	/// <list type="number">
	/// <item>**射程**（`E11`）：`AttackRadius == 0` = 近战 → **必须同格**（`E9`：一格一对交战单位）；
	/// `>= 1` = 远程 → 隔格攻击，最远 `AttackRadius` 格。</item>
	/// <item>**目标类型衰减**（`E12`）：对建筑伤害 ×0.5（design/unit.md「所有单位在攻击建筑时都有伤害衰减」）。</item>
	/// <item>**加伤乘其后**（`E12`）：`0.5 × 1.5 = 0.75` —— 先算衰减、再乘攻击方加伤。</item>
	/// </list>
	/// <para>**为什么是常量而不是配置项**：这三条是全局规则、设计稿给的是固定值（与 `TimeConstants` 里的
	/// 节拍同一性质）；平衡期若要调，按 `Settlement` 段的做法整体配置化（`WP-3.10` 的 `D64` 口径），
	/// 而不是把 0.5 抄进每张单位表 —— 抄进去就会出现"两个单位对建筑衰减不同"的隐性不一致。</para>
	/// </summary>
	public static class CombatRules
	{
		/// <summary>对建筑伤害衰减系数（`E12`：设计稿 50%）。</summary>
		public const float BuildingDamageFactor = 0.5f;

		/// <summary>玩家单位的攻击加伤 target（design/unit.md「修饰器影响」的 `UnitAttack`）。</summary>
		public const string PlayerAttackBonusTarget = "UnitAttack";

		/// <summary>敌方单位的攻击加伤 target（design/unit.md「修饰器影响」的 `EnemyAttack`）。</summary>
		public const string EnemyAttackBonusTarget = "EnemyAttack";

		/// <summary>对建筑加伤 target（弩炮「对建筑伤害 +50%」等，`E13` 的条件修正落地前由它承载）。</summary>
		public const string BuildingBonusTarget = "BuildingDamage";

		/// <summary>目标类型系数：建筑 = <see cref="BuildingDamageFactor"/>（0.5），单位 = 1（不衰减）。</summary>
		public static float TargetFactor(OccupantType targetType)
			=> targetType == OccupantType.Building ? BuildingDamageFactor : 1f;

		/// <summary>是否远程单位（`E11`：`AttackRadius >= 1` 即可隔格，"近战/远程"由该字段派生）。</summary>
		public static bool IsRanged(Unit unit) => unit != null && unit.AttackRadius >= 1;

		/// <summary>
		/// 攻击者能否打到该格（`E9` + `E11` 的合取）：
		/// **同格恒可**（不论射程 —— 交战对就站在同一格），否则远程看射程、近战（0）打不到隔壁。
		/// </summary>
		public static bool CanReach(Unit attacker, HexCubePosition targetPosition)
		{
			if (attacker == null) return false;
			if (attacker.Position == targetPosition) return true;

			return attacker.AttackRadius >= 1 && attacker.Position.DistenceTo(targetPosition) <= attacker.AttackRadius;
		}

		/// <summary>能否打到某个占据物（按其所在格判定）。</summary>
		public static bool CanReach(Unit attacker, IMapOccupant target)
			=> target != null && CanReach(attacker, target.GetInfo().Position);

		/// <summary>
		/// **敌对口径**：不同 `OwnerId` 即敌对（与 `EnemySpawner.HostileOwnerId = -1` 同一套，
		/// 项目里没有独立阵营表 —— 再引一张表会让"谁能打谁"出现两处真相）。
		/// </summary>
		public static bool IsEnemy(IMapOccupant a, IMapOccupant b)
		{
			if (a == null || b == null) return false;
			if (ReferenceEquals(a, b)) return false;

			return a.GetInfo().OwnerId != b.GetInfo().OwnerId;
		}

		/// <summary>有伤害来源才会开战：0 伤害的单位（如工人）不该白挂一条按日循环。</summary>
		public static bool HasDamage(IMapOccupant occupant) => occupant is Unit unit && unit.AttackDamage > 0f;

		/// <summary>攻击方加伤 target：玩家单位 → <c>UnitAttack</c>；敌方单位 → <c>EnemyAttack</c>。</summary>
		public static string BonusTargetOf(Unit attacker)
			=> attacker != null && attacker.IsHostile ? EnemyAttackBonusTarget : PlayerAttackBonusTarget;

		/// <summary>
		/// （`E12`）**伤害公式（纯函数）**：`(基础伤害 + 绝对值加伤) × 目标系数 × (1 + 攻击方加伤) × (1 + 对建筑加伤)`。
		/// <para>调用方（`UnitCombatService.ComputeDamage`）把绝对值加伤放在"衰减之后"施加，
		/// 因此本重载只收百分比：`0.5 × 1.5 = 0.75` 与设计稿一致；`Absolute` 型修正代表"装备加伤"，不参与衰减。</para>
		/// </summary>
		/// <param name="baseDamage">配置表的 `AttackDamage`（= 每日伤害，`E10`）。</param>
		/// <param name="targetType">目标占据物类型（单位 / 建筑）。</param>
		/// <param name="attackPercentBonus">攻击方加伤（小数：0.5 = +50%）。</param>
		/// <param name="buildingPercentBonus">对建筑加伤（小数；仅建筑目标会被消费）。</param>
		public static float Damage(float baseDamage, OccupantType targetType,
			float attackPercentBonus = 0f, float buildingPercentBonus = 0f)
		{
			float damage = baseDamage * TargetFactor(targetType) * (1f + attackPercentBonus);
			if (targetType == OccupantType.Building) damage *= 1f + buildingPercentBonus;

			return damage;
		}
	}
}
