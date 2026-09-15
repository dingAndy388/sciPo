namespace SciencePotato.Scripts.Intent.Application
{
	/// <summary>
	/// （v0.9.4 / `WP-5.7`）**意图被拒的原因键**（`Config/Strings.{locale}.json` 里必须有对应文案）。
	/// <para>用常量而不是散落字符串：① 用例可以断言"键存在"（`WP-5.10` 的缺键纪律）；
	/// ② 表现层拿到键直接 `I18n.T(key)`，不需要 switch 文案。</para>
	/// </summary>
	public static class IntentKeys
	{
		/// <summary>传入了 <c>null</c> 意图。</summary>
		public const string NoIntent = "intent.no_intent";

		/// <summary>意图没带地图 Id。</summary>
		public const string NoMap = "intent.no_map";

		/// <summary>缺少目标（建筑/单位/节点/事件 Id，或格位）。</summary>
		public const string NoTarget = "intent.no_target";

		/// <summary>找不到该单位。</summary>
		public const string NoUnit = "intent.no_unit";

		/// <summary>发起者不是人类势力（或该势力不存在）⇒ UI 不能指挥 AI。</summary>
		public const string NotHuman = "intent.not_human";

		/// <summary>界面能力尚未解锁（如未研究「计数」就想开研究面板）。</summary>
		public const string UiLocked = "intent.ui_locked";

		/// <summary>科技前置未满足 / 已在研究中 / 已研究（`CanResearch` 为假）。</summary>
		public const string Prerequisite = "intent.prereq";

		/// <summary>规则层拒绝（资源不足 / 地形不允许 / 门控不满足 —— 与"参数错"区分开）。</summary>
		public const string Rejected = "intent.rejected";

		/// <summary>未知意图种类（版本错配 / 手写数据）。</summary>
		public const string UnknownKind = "intent.unknown_kind";
	}
}
