namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.8.3 / `WP-4.1`）**修正器的阶段**：`Modifier` 从哪来，决定它在管道里的计算顺序。
	/// <para>为什么需要它（设计稿的"效果"列大量出现 `+x%` 与 `+N` 混用）：如果所有修正一起累加，
	/// `(base + ΣAbsolute) × (1 + ΣPercent)` 的顺序会被"谁先注册"左右 —— 同一份配置换个加载顺序结果就变。
	/// 阶段管道把顺序**写进口径**：`base → 建筑 → 科技 → 事件`，每阶段内 `(累加 Absolute) × (1 + 累加 Percent)`。</para>
	/// <para>**兼容**：存档里没有 `Stage` 字段的历史数据解析为 <see cref="Building"/>（= 0），
	/// 与旧公式（单阶段）逐位一致；旧调用方无需改动。</para>
	/// </summary>
	public enum ModifierStage
	{
		/// <summary>建筑（含升级链）带来的修正；历史数据的缺省阶段。</summary>
		Building = 0,

		/// <summary>科技树节点的修正（在建筑之后结算：科技是对既有建筑的"改进"）。</summary>
		Tech = 1,

		/// <summary>事件（淘金热/瘟疫等）的修正（最后结算：事件是临时外因，作用在既有一切之上）。</summary>
		Event = 2,
	}
}
