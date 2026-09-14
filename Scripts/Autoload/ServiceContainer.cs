using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Infrastructure;

public partial class ServiceContainer : Node
{
	//Static Instance
	public static ServiceContainer Instance { get; private set; }

	//Services
	public MapAppService MapService => Core?.Map;
	public GameSession Session => Core?.Session;

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
	}
}
