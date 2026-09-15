using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Save;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.Units.Application;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）组合根的产物：**已装配好的服务集合**（无状态）。
	/// 与 <see cref="GameSession"/>（只持状态）分离，避免"服务与状态混在同一个对象里"导致测试无法替换实现。
	/// <para>v0.3 / WP-1.4 起 <see cref="Tables"/> 持有 7 张配置表，<see cref="ConfigReport"/> 持有启动期校验结论
	/// （error 才会阻断启动，warning 仅提示）。装配是增量的：`WP-2.x` 会依次把 Construction / Units /
	/// TechTrees / Events 的应用服务接到这些表上。</para>
	/// </summary>
	public sealed class CoreServices
	{
		public GameSession Session { get; init; }

		/// <summary>
		/// （v0.3 / WP-1.5）**游戏日节拍总线**：所有周期任务（建造 / 训练 / 人口 / 事件 / 资源月结）
		/// 都注册到这里，由 <see cref="GameSession.Clock"/> 逐日派发（`OnTick(1 日)`）。
		/// <para>调用方只需推进时钟（`Session.Advance(realSeconds)` 或 `ITimeDriver.Advance`），
		/// 不必关心"秒"。</para>
		/// </summary>
		public GameTimeService Time { get; init; }

		public IConfigSource ConfigSource { get; init; }

		/// <summary>7 张配置表（Terrains / Resources / Buildings / Units / TechTrees / Events / Generator）。</summary>
		public ConfigTables Tables { get; init; }

		/// <summary>启动期配置校验报告（真实配置要求 0 error）。</summary>
		public ConfigReport ConfigReport { get; init; }

		/// <summary>
		/// （v0.6.0 / WP-5.3）**地图外观参数**：表现层算格位/贴图路径的唯一来源
		/// （`CellXStep`/`CellYStep`/`TerrainSpriteDir`，全部来自地形表 → 换美术不改代码，`R5`）。
		/// </summary>
		public IMapAppearanceConfig Appearance { get; init; }

		public IMapGenerator MapGenerator { get; init; }

		public MapAppService Map { get; init; }

		/// <summary>
		/// （v0.3 / WP-2.10）**领域事件总线**：全进程一份（`ROOT-4` / `UNIT-08` / `TECH-05` / `EVT-04`）。
		/// <para>应用服务在构造时接收它并发布事件；表现层/统计/联动模块订阅它，
		/// 从而不必轮询 `IsReady`/`IsResearched`/`GetActiveEvents`。</para>
		/// </summary>
		public IDomainEventBus DomainEvents { get; init; }

		/// <summary>
		/// （v0.3 / WP-3.3）**统一存档单元**：任务/迷雾/资源/科技/修正器/事件/时钟都写进它的分区，
		/// 由存档点（`WorldSaveService.SaveWorld`）一次原子落盘。
		/// <para>⚠️ 生产路径的**逐仓储接线**随 `WP-5.1`（把 Construction/Units/Events/… 应用服务收进组合根）一起完成；
		/// 在此之前本项由宿主透传，供无头验收与后续装配使用（见 §18.5.3）。</para>
		/// </summary>
		public ISaveStore SaveStore { get; init; }

		// ───────────── v0.6.0 / WP-5.1：应用服务到齐 ─────────────

		/// <summary>任务仓储（统一存档模式的周期/一次性任务快照分区；无存档单元时为 null）。</summary>
		public ITaskRepository Tasks { get; init; }

		/// <summary>资源池 + 资源成长（`M0-2` ② 的落点）。</summary>
		public ResourcesAppService Resources { get; init; }

		/// <summary>修正器（建筑/科技/事件对数值的加成；`MOD-*` 的落点）。</summary>
		public ModifierAppService Modifiers { get; init; }

		/// <summary>科技树（93 节点目标的承载服务）。</summary>
		public TechTreesAppService Tech { get; init; }

		/// <summary>
		/// 战争迷雾。
		/// <para>⚠️ 当前是**单 owner 实例**（服务人类阵营）：按 owner 拆分属 `WP-4.10`（`FOG-01` 收口）。</para>
		/// </summary>
		public FogAppService Fog { get; init; }

		/// <summary>建造 / 升级 / 拆除（`CON-*` 的落点）。</summary>
		public ConstructionAppService Construction { get; init; }

		/// <summary>单位训练 / 移动 / 战斗 / 掉落（`UNIT-*` 的落点）。</summary>
		public UnitsAppService Units { get; init; }

		/// <summary>随机事件引擎（**只对人类玩家**启动；`EVT-*` / `G8` 的落点）。</summary>
		public EventAppService Events { get; init; }

		/// <summary>月度经济结算（`WP-3.9`/`WP-3.10`：产出、维护费、赤字减员）。</summary>
		public MonthlySettlementService Settlement { get; init; }

		/// <summary>世界存档/读档协调者（存档点唯一入口）。</summary>
		public WorldSaveService WorldSave { get; init; }

		/// <summary>
		/// （v0.6.0 / WP-5.1）**会话级子系统编排器**：按玩家表逐 owner 启动资源池 / 月结 / 事件引擎。
		/// <para>开局、生成地图、读档后的"把一局跑起来"都走它 —— 表现层与 AI 只需要拿玩家表遍历。</para>
		/// </summary>
		public SessionOrchestrator Orchestrator { get; init; }

		/// <summary>
		/// （v0.6.5 / WP-5.10）**多语言（i18n）**：UI 文案的键 → 文本解析器（缺键可见、可回退到默认语言）。
		/// <para>表现层今后**只允许**写键（如 <c>i18n.T("ui.generate")</c>），不允许写死文案（`R4`）。</para>
		/// </summary>
		public SessionSetupService Setup { get; init; }

		/// <summary>
		/// （v0.7.0 / WP-4.19）**胜负判定**：每月判定 + 全灭（`D72`）；AI 与人类同一套判据。
		/// <para>一局结束的判据 = 只剩一个势力存活（`OutcomeOf(mapId).IsOver`）。</para>
		/// </summary>
		public VictoryService Victory { get; init; }

		/// <summary>
		/// （v0.6.5 / WP-5.10）**多语言（i18n）**：UI 文案的键 → 文本解析器（缺键可见、可回退到默认语言）。
		/// <para>表现层今后**只允许**写键（如 <c>i18n.T("ui.generate")</c>），不允许写死文案（`R4`）。</para>
		/// </summary>
		public II18nService I18n { get; init; }
	}
}

