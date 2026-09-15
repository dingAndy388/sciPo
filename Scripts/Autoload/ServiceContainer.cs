using Godot;
using SciencePotato.Scripts.Autoload;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Save;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using System.Collections.Generic;

public partial class ServiceContainer : Node
{
	//Static Instance
	public static ServiceContainer Instance { get; private set; }

	//Services
	public MapAppService MapService => Core?.Map;
	public GameSession Session => Core?.Session;

	/// <summary>
	/// （v0.6.0 / WP-5.1）**组合根产物**：全部应用服务都在这里（资源/科技/建造/单位/事件/月结/存档/编排）。
	/// <para>表现层只读它，绝不自己 <c>new</c> 服务 —— 这是 `WIRE-01`/`DEP-02` 的落地口径。</para>
	/// </summary>
	public CoreServices Services => Core;

	/// <summary>（v0.6.0 / WP-4.18）玩家表：本局的势力（人类 + AI）。</summary>
	public IReadOnlyList<PlayerContext> Players => Core?.Session?.Players;

	/// <summary>（v0.6.0 / WP-4.18）会话级子系统编排器（开局/读档后"把一局跑起来"的入口）。</summary>
	public SessionOrchestrator Orchestrator => Core?.Orchestrator;

	/// <summary>（v0.6.0 / WP-5.1）世界存档协调者（存档点/读档点）。</summary>
	public WorldSaveService WorldSave => Core?.WorldSave;

	/// <summary>（v0.3 / WP-1.5）游戏日节拍总线：表现层/M1 调试面板可查看注册中的任务数。</summary>
	public GameTimeService Time => Core?.Time;

	/// <summary>
	/// （v0.3 / WP-1.2）Godot 时间适配器：每帧把真实秒交给 <see cref="GameClock"/>，玩法侧只见"游戏日"。
	/// <para>由本节点在 <c>_Ready</c> 中创建并挂为子节点，因此无需改场景与 autoload 列表。</para>
	/// </summary>
	public GodotTimeDriver TimeDriver { get; private set; }

	/// <summary>（v0.3 / WP-1.4）7 张配置表，供表现层与后续应用服务读取。</summary>
	public ConfigTables Tables => Core?.Tables;

	/// <summary>（v0.3 / WP-1.4）启动期配置校验报告（error 会直接让启动失败，因此这里看到的通常只有 warning）。</summary>
	public ConfigReport ConfigIssues => Core?.ConfigReport;

	public IConfigLoader ConfigLoader { get; private set; }

	//Infrastructure (v0.3 / WP-0.3)
	public IFileSystem FileSystem { get; private set; }
	public IConfigSource ConfigSource { get; private set; }

	/// <summary>组合根产物（v0.3 / WP-1.3）：核心服务与状态都由它提供。</summary>
	public CoreServices Core { get; private set; }

	/// <summary>
	/// （v0.3 / WP-3.3）**统一存档单元**：`user://save/local.json`（单一文件 + 版本迁移 + 原子写）。
	/// <para>任务/迷雾/资源/科技/修正器/事件/时钟都以分区形式写进它；逐仓储接线随 `WP-5.1` 完成（见 `CoreServices.SaveStore`）。</para>
	/// </summary>
	public ISaveStore SaveStore { get; private set; }

	private IConfigLoader _configLoader;

	public override void _Ready()
	{
		Instance = this;

		// Godot 适配层：只创建"与引擎相关"的实现（v0.3 / WP-0.3）
		FileSystem = new GodotFileSystem();
		ConfigSource = new GodotJsonConfigSource(FileSystem);
		_configLoader = new GodotConfigService();
		ConfigLoader = _configLoader;

		// 存档单元（v0.3 / WP-3.3；v0.6.0 / WP-5.1 起逐仓储接线）：**全进程一份**，
		// 由文件系统抽象落盘（因此原子替换与"写到一半崩溃"的语义和测试环境完全一致）。
		// 冒烟/无头自查用 `--smoke` 切到独立存档文件，避免覆盖开发中的正式档。
		SaveStore = new JsonSaveStore(FileSystem, IsSmokeRun ? "user://save/smoke.json" : "user://save/local.json");

		// 组合根：核心装配统一走 CoreBootstrap（v0.3 / WP-1.3；v0.6.0 / WP-5.1 起含全部应用服务）
		// 配置表有 error 时 CoreBootstrap 会抛出（含逐条问题清单）；warning 只打印、不阻断启动（v0.3 / WP-1.4）
		Core = CoreBootstrap.Build(new CoreDependencies
		{
			FileSystem = FileSystem,
			ConfigSource = ConfigSource,
			Random = new SystemRandom(20260914),
			ResourceConfigLoader = _configLoader,
			GeneratorConfigPath = "res://Config/Generator",
			MapRepositoryFactory = tables => new GodotMapRepository(tables.Terrains), // v0.3 / WP-3.2：实体重建器由 CoreBootstrap 在装配后补挂
			SessionId = "local",
			SaveStore = SaveStore,
			SaveRoot = "user://save/",
			Players = BuildPlayers(),
		});

		ReportConfigIssues();
		ReportWiring();
		StartTimeDriver();
	}

	/// <summary>
	/// （v0.6.0 / WP-4.18）本局的**玩家表**：缺省 1 个人类玩家；`--ai=N`（命令行用户参数）追加 N 个 AI 势力，
	/// 用于在真实宿主里验证"多玩家各自一套子系统"。
	/// <para>⚠️ 此刻 AI **只是"有资源的势力"，还没有决策**：AI 行为属批次 6（`WP-6.1`~`WP-6.6`）。
	/// 在那之前 `--ai` 是开发开关，正式开局参数归 `WP-5.9`（出生点与势力配置）。</para>
	/// </summary>
	private static List<PlayerContext> BuildPlayers()
	{
		var players = new List<PlayerContext> { PlayerContext.Human(1) };

		int aiCount = UserArgInt("--ai=", 0);
		for (int i = 0; i < aiCount; i++) players.Add(PlayerContext.Ai(2 + i));

		return players;
	}

	/// <summary>本次是否为冒烟运行（`--smoke` 用户参数，见 <c>Scene/Dev/smoke_report.tscn</c>）。</summary>
	private static bool IsSmokeRun => System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0;

	/// <summary>读取形如 <c>--key=123</c> 的命令行用户参数（无则返回缺省）。</summary>
	private static int UserArgInt(string prefix, int fallback)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg != null && arg.StartsWith(prefix, System.StringComparison.Ordinal)
				&& int.TryParse(arg.Substring(prefix.Length), out int value))
				return value;
		}
		return fallback;
	}

	/// <summary>
	/// （v0.3 / WP-1.2）挂载 Godot 时间适配器：每帧把真实秒推进为游戏日，日边界由
	/// <see cref="GameTimeService"/> 逐日派发（玩法侧不再接触秒）。
	/// </summary>
	private void StartTimeDriver()
	{
		TimeDriver = new GodotTimeDriver { Name = "GodotTimeDriver" };
		AddChild(TimeDriver);
		TimeDriver.Configure(Core.Session.Clock, Core.Time);

		GameClock clock = Core.Session.Clock;
		GD.Print($"[ServiceContainer] 时间适配器就绪：{GameClock.DaysPerYear} 日/年、{TimeConstants.DaysPerMonth} 日/月；" +
				 $"当前档位 {clock.DaysPerSecond} 日/真实秒，暂停={clock.IsPaused}；已注册周期任务 {Core.Time?.SubscriberCount ?? 0} 个");
	}

	/// <summary>
	/// （v0.6.0 / WP-5.1）**装配自检报告**：把"哪些服务真的装上了"打到控制台。
	/// <para>为什么值得每次启动都打：改造前生产路径只有 Map + Time，其余服务**悄悄不存在** ——
	/// 表现层要到第一次调用时才发现（那时离根因已经很远）。一行清单让"缺服务"在启动那一刻就可见。</para>
	/// </summary>
	private void ReportWiring()
	{
		CoreServices core = Core;
		if (core == null)
		{
			GD.PushError("[ServiceContainer] 组合根为空：装配失败");
			return;
		}

		GD.Print("[ServiceContainer] 装配自检：" +
			$"Map={(core.Map != null)} Resources={(core.Resources != null)} Tech={(core.Tech != null)} " +
			$"Construction={(core.Construction != null)} Units={(core.Units != null)} " +
			$"Events={(core.Events != null)} Fog={(core.Fog != null)} Settlement={(core.Settlement != null)} " +
			$"WorldSave={(core.WorldSave != null)} Orchestrator={(core.Orchestrator != null)} Tasks={(core.Tasks != null)}");

		var names = new List<string>();
		foreach (PlayerContext player in core.Session.Players) names.Add(player.ToString());
		string playerList = string.Join("、", names);
		GD.Print($"[ServiceContainer] 玩家表（{core.Session.Players.Count} 个势力）：{playerList}；会话 Id={core.Session.SessionId}");

		if (core.ConfigReport != null && core.ConfigReport.HasErrors)
			GD.PushError("[ServiceContainer] 配置存在 error（见上方清单）：玩法数值可能不可用");
	}

	/// <summary>把配置校验结论打到 Godot 控制台：填表错误在编辑器里第一时间可见（`WIRE-04`）。</summary>
	private void ReportConfigIssues()
	{
		ConfigReport report = Core.ConfigReport;
		if (report == null) return;

		GD.Print($"[ServiceContainer] {report.Summary()}；已装载 {Core.Tables.LoadedCount}/{ConfigTables.Names.Count} 张配置表");
		foreach (ConfigIssue issue in report.Issues)
		{
			if (issue.Level == ConfigIssueLevel.Error) GD.PushError(issue.ToString());
			else GD.PushWarning(issue.ToString());
		}
	}
}

