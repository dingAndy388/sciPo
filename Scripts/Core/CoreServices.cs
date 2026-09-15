using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Save;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Intent.Application;
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
		/// （v0.7.1 / WP-6.1）**AI 对手的策略参数**（`Config/AI.json`）：几个 AI、前期不造兵窗口、分配比例、威胁阈值。
		/// <para>行为（决策循环/经济/军事）归 `WP-6.2`~`WP-6.4`；本项只提供参数与校验结论。</para>
		/// </summary>
		public IAiConfig Ai { get; init; }

		/// <summary>
		/// （v0.7.3 / WP-6.2）**AI 决策循环**：按游戏日节拍做"生存 → 威胁 → 发展"判断（人类不参与）。
		/// <para>它只产出 <c>AiDecision</c>（可复盘）；下单（建造/科研/训练）归 `WP-6.3`/`WP-6.4`。</para>
		/// </summary>
		public AiService AiService { get; init; }

		/// <summary>（v0.8.8 / WP-4.17）人口模型：聚落级容量（多住房不叠加）+ 拆住房减员。</summary>
		public PopulationModelService Population { get; init; }

		/// <summary>（v0.8.9 / WP-4.13）产出归因：把某资源产出拆成一条条来源（"查看资源加减项"）。</summary>
		public ProductionAttributionService Attribution { get; init; }

		/// <summary>（v0.8.9 / WP-4.14）UI 门控：科技节点 `UnlocksUi` → 界面能力开关。</summary>
		public UiGateService UiGate { get; init; }

		/// <summary>
		/// （v0.7.4 / WP-6.3）**AI 经济分配**：把决策变成建造/科研订单（走玩家同一套应用服务 ⇒ 不作弊）。
		/// </summary>
		public AiEconomyService AiEconomy { get; init; }

		/// <summary>
		/// （v0.7.5 / WP-6.4）**AI 军事**：按威胁分级投军费（训练）+ 优先防御（把人叫回自家聚落）。
		/// </summary>
		public AiMilitaryService AiMilitary { get; init; }

		/// <summary>
		/// （v0.9.4 / `WP-5.7`）**意图契约**：表现层唯一的输入口（`PlayerIntent` + `IActionHandler`）。
		/// <para>为什么要有它：UI 直连 `ConstructionAppService` 这类调用会让"谁能做/门控/资源"的规则散进表现层
		/// （`CON-02`/`CON-08`）。有了本项，UI 只表达意图，规则集中在 `HumanIntentHandler` 一处判断。</para>
		/// </summary>
		public IActionHandler Intent { get; init; }

		/// <summary>
		/// （v0.9.6 / `WP-5.11`）**会话入口**：新开局 / 存档 / 读档 / 退出 + 自动存档点。
		/// <para>宿主只跟它打交道，不必自己拼"生成地图 → 启动编排器 → 存档点"的顺序；读档按**玩家表逐 owner**恢复（`U7`）。</para>
		/// </summary>
		public SessionEntryService Entry { get; init; }

		/// <summary>
		/// （v0.6.5 / WP-5.10）**多语言（i18n）**：UI 文案的键 → 文本解析器（缺键可见、可回退到默认语言）。
		/// <para>表现层今后**只允许**写键（如 <c>i18n.T("ui.generate")</c>），不允许写死文案（`R4`）。</para>
		/// </summary>
		public II18nService I18n { get; init; }
	}
}

