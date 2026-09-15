namespace SciencePotato.Scripts.Intent.Domain
{
	/// <summary>
	/// （v0.9.4 / `WP-5.7`）**玩家意图的种类**（`I2` / `CON-02` / `CON-08` 的收口）。
	/// <para>表现层**只表达"想做什么"**（下面这些种类 + 目标），由 `IActionHandler` 去校验并翻译成
	/// 应用服务调用 —— 于是 UI 里不再出现 `Construction.StartConstruction(...)` 这类直连，
	/// 规则（谁能做 / 门控是否解锁 / 资源够不够）只在一个地方判断。</para>
	/// </summary>
	public enum IntentKind
	{
		/// <summary>在某格建造某建筑（`Id` = 建筑 Id，`Target` = 格位）。</summary>
		Build = 0,

		/// <summary>升级某栋建筑（`SourceUid` = 建筑 uid；目标等级由升级链决定）。</summary>
		Upgrade = 1,

		/// <summary>在某栋建筑下单训练某单位（`SourceUid` = 建筑 uid，`Id` = 单位 Id）。</summary>
		Train = 2,

		/// <summary>研究某科技节点（`TreeId` + `Id`；不是"能研究"的查询 ⇒ UI 用 `CanRequest` 先问）。</summary>
		Research = 3,

		/// <summary>把某单位派往某格（`SourceUid` = 单位 uid，`Target` = 目的地）。</summary>
		MoveUnit = 4,

		/// <summary>对某个待决事件做出决策（`Id` = 事件 Id；`WP-4.12` 的 `Resolve`）。</summary>
		ResolveEvent = 5,
	}
}
