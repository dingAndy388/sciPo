namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.7.0 / WP-4.8）**可受伤的占据物**（目前只有建筑；单位用自己的 `HP` 字段）。
	/// <para>为什么单独抽接口而不是让 `Map` 直接认 `Building`：地图模块不得依赖 Construction 模块
	/// （§16 的引擎/模块耦合收敛），而"打建筑要掉血、掉到 0 就变成可夺取"这件事**必须发生在
	/// Map 的唯一入口里**（否则又会出现"血量在 A 处减、占位在 B 处改"的分裂）。</para>
	/// <para>口径（`D73`，用户确认）：**HP &gt; 0 ⇒ 该格不可进入（先打）**；**HP ≤ 0 ⇒ 转为"可夺取"**
	/// （建筑仍在，但不再占住格子）；**我方单位站上该格即易主**（HP 归零**不**自动易主）。</para>
	/// </summary>
	public interface IDamageable
	{
		/// <summary>是否使用 HP 模型（false = 该占据物免疫伤害，攻击只记账）。</summary>
		bool HasHP { get; }

		float MaxHP { get; }

		float HP { get; }

		/// <summary>HP ≤ 0（对建筑 = 可夺取；对单位 = 已被移除，不会观察到这个状态）。</summary>
		bool IsCapturable { get; }

		/// <summary>扣血（不夹到负值之外的逻辑；返回剩余 HP）。</summary>
		float TakeDamage(float damage);

		/// <summary>
		/// **易主**：把归属改成 <paramref name="newOwnerId"/>，并把 HP 恢复到 <c>MaxHP × 0.5</c>
		/// （占下来的建筑不该是"0 血等着再被抢"的状态）。
		/// </summary>
		void CaptureBy(int newOwnerId);
	}
}
