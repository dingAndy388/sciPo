using System.Collections.Generic;

namespace SciencePotato.Scripts.Audio.Domain
{
	/// <summary>
	/// （v0.9.11 / `WP-8.4` 前置）**音效契约**：音频文件与"什么时候响"之间的唯一口径（纯 C#、无 Godot 依赖 ⇒ 无头可验）。
	/// <list type="bullet">
	/// <item><b>路径口径</b>：音效 `res://Audio/SFX/{id}.wav`、音乐 `res://Audio/BGM/{id}.ogg`
	/// （与 `Document/SoundManifest.csv` / `SoundList.md` 完全一致）；</item>
	/// <item><b>触发口径</b>：领域事件名 / 意图结果 → 音效 id 的映射写死在 <see cref="SfxForDomainEvent"/> 与
	/// <see cref="SfxForIntentFailure"/> 两张表里 —— 音频作者只需照 id 出文件，播放点由代码决定；</item>
	/// <item><b>缺失口径</b>：文件不存在时**什么都不做**（不抛异常、不刷屏日志）⇒ 音频可以边做边交。</item>
	/// </list>
	/// <para>本类**不播放**任何东西：真正的播放器由 `WP-8.4` 的 `AudioAppService`（Godot 侧）订阅事件后调用，
	/// 它只负责"查表"（哪条事件该响哪个 id、文件路径是什么）。</para>
	/// </summary>
	public static class SoundCatalog
	{
		public const string SfxDir = "res://Audio/SFX/";
		public const string BgmDir = "res://Audio/BGM/";

		/// <summary>音效扩展名：短促一次性音效用 **wav**（无解码延迟、体量小）。</summary>
		public const string SfxExtension = ".wav";

		/// <summary>音乐扩展名：长循环用 **ogg**（体积小、可无缝循环）。</summary>
		public const string BgmExtension = ".ogg";

		// ── UI / 系统 ──
		public const string UiClick = "ui_click";
		public const string UiHover = "ui_hover";
		public const string UiCancel = "ui_cancel";
		public const string UiReject = "ui_reject";
		public const string UiPanelOpen = "ui_panel_open";
		public const string UiPanelClose = "ui_panel_close";
		public const string UiTab = "ui_tab";
		public const string GameStart = "game_start";
		public const string GameSave = "game_save";
		public const string GameLoad = "game_load";
		public const string Victory = "victory";
		public const string Defeat = "defeat";

		// ── 建造 / 生产 ──
		public const string BuildStart = "build_start";
		public const string BuildComplete = "build_complete";
		public const string UpgradeStart = "upgrade_start";
		public const string UpgradeComplete = "upgrade_complete";
		public const string BuildingCaptured = "building_captured";
		public const string BuildingDestroyed = "building_destroyed";
		public const string TrainStart = "train_start";
		public const string UnitTrained = "unit_trained";

		// ── 单位 / 战斗 ──
		public const string UnitMoveOrder = "unit_move_order";
		public const string UnitAttack = "unit_attack";
		public const string UnitHit = "unit_hit";
		public const string UnitDied = "unit_died";
		public const string UnitLoot = "unit_loot";

		// ── 科技 ──
		public const string ResearchStart = "research_start";
		public const string ResearchComplete = "research_complete";
		public const string TechUiUnlocked = "tech_ui_unlocked";

		// ── 事件 ──
		public const string EventTrigger = "event_trigger";
		public const string EventAccept = "event_accept";
		public const string EventDismiss = "event_dismiss";

		// ── 资源 / 结算 ──
		public const string MonthlySettle = "monthly_settle";
		public const string ResourceLow = "resource_low";
		public const string PopulationGrowth = "population_growth";
		public const string DeficitWarning = "deficit_warning";

		// ── 环境（可选） ──
		public const string FogReveal = "fog_reveal";

		/// <summary>全部音效 id（顺序 = `SoundList.md` 的展示顺序；用例拿它和清单做闭合校验）。</summary>
		public static readonly IReadOnlyList<string> SfxIds = new[]
		{
			UiClick, UiHover, UiCancel, UiReject, UiPanelOpen, UiPanelClose, UiTab,
			GameStart, GameSave, GameLoad, Victory, Defeat,
			BuildStart, BuildComplete, UpgradeStart, UpgradeComplete, BuildingCaptured, BuildingDestroyed, TrainStart, UnitTrained,
			UnitMoveOrder, UnitAttack, UnitHit, UnitDied, UnitLoot,
			ResearchStart, ResearchComplete, TechUiUnlocked,
			EventTrigger, EventAccept, EventDismiss,
			MonthlySettle, ResourceLow, PopulationGrowth, DeficitWarning,
			FogReveal,
		};

		/// <summary>音乐 id（当前只有一首主曲；将来按情境加曲时在这里加）。</summary>
		public static readonly IReadOnlyList<string> BgmIds = new[] { "main" };

		/// <summary>音效文件路径（`res://Audio/SFX/{id}.wav`）；空 id 返回 <c>null</c>。</summary>
		public static string SfxPath(string id) => string.IsNullOrWhiteSpace(id) ? null : SfxDir + id + SfxExtension;

		/// <summary>音乐文件路径（`res://Audio/BGM/{id}.ogg`）；空 id 返回 <c>null</c>。</summary>
		public static string BgmPath(string id) => string.IsNullOrWhiteSpace(id) ? null : BgmDir + id + BgmExtension;

		/// <summary>
		/// **领域事件名 → 音效 id**（<c>null</c> = 该事件不出声）。事件名取自 `Common/Domain/DomainEvents.cs` 的类名；
		/// 播放器按名字订阅，因此本表是"播放点"的唯一出处。
		/// </summary>
		public static string SfxForDomainEvent(string eventTypeName) => eventTypeName switch
		{
			"BuildingCompletedEvent" => BuildComplete,
			"BuildingUpgradedEvent" => UpgradeComplete,
			"UnitTrainedEvent" => UnitTrained,
			"UnitDiedEvent" => UnitDied,
			"ResearchCompletedEvent" => ResearchComplete,
			"GameEventTriggeredEvent" => EventTrigger,
			"BuildingCapturedEvent" => BuildingCaptured,
			"BuildingRemovedEvent" => BuildingDestroyed,
			_ => null,
		};

		/// <summary>
		/// **意图结果 → 音效 id**：被接受响 <c>ui_click</c>、被拒响 <c>ui_reject</c>。
		/// 拒绝原因不止一条，但提示音只一条（原因是文案，由 i18n 键给）。
		/// </summary>
		public static string SfxForIntent(bool accepted) => accepted ? UiClick : UiReject;
	}
}