using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;

public partial class ServiceContainer : Node
{
	//Static Instance
	public static ServiceContainer Instance { get; private set; }

	//Services
	public MapAppService MapService => Core?.Map;
	public GameSession Session => Core?.Session;

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

	private IConfigLoader _configLoader;

	public override void _Ready()
	{
		Instance = this;

		// Godot 适配层：只创建"与引擎相关"的实现（v0.3 / WP-0.3）
		FileSystem = new GodotFileSystem();
		ConfigSource = new GodotJsonConfigSource(FileSystem);
		_configLoader = new GodotConfigService();
		ConfigLoader = _configLoader;

		// 组合根：核心装配统一走 CoreBootstrap（v0.3 / WP-1.3）
		// 配置表有 error 时 CoreBootstrap 会抛出（含逐条问题清单）；warning 只打印、不阻断启动（v0.3 / WP-1.4）
		Core = CoreBootstrap.Build(new CoreDependencies
		{
			FileSystem = FileSystem,
			ConfigSource = ConfigSource,
			Random = new SystemRandom(20260914),
			ResourceConfigLoader = _configLoader,
			GeneratorConfigPath = "res://Config/Generator",
			MapRepositoryFactory = terrain => new GodotMapRepository(terrain),
			SessionId = "local",
		});

		ReportConfigIssues();
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

