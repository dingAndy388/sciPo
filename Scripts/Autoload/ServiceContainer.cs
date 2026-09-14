using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;

public partial class ServiceContainer : Node
{
	//Static Instance
	public static ServiceContainer Instance { get; private set; }

	//Services
	public MapAppService MapService { get; private set; }

	public IConfigLoader ConfigLoader { get; private set; }

	//Infrastructure (v0.3 / WP-0.3)
	public IFileSystem FileSystem { get; private set; }
	public IConfigSource ConfigSource { get; private set; }
	public ITerrainConfigRepository TerrainConfig { get; private set; }

	//Interfaces
	private IMapRepository _mapRepository;
	private IConfigLoader _configLoader;

	//Instance
	private IMapGenerator _mapGenerator;

	public override void _Ready()
	{
		Instance = this;

		// 基础设施（v0.3 / WP-0.3）：文件系统与配置表来源
		FileSystem = new GodotFileSystem();
		ConfigSource = new GodotJsonConfigSource(FileSystem);
		_configLoader = new GodotConfigService();
		ConfigLoader = _configLoader;

		// 配置表（v0.3 / WP-0.4）：地形从 .tres 迁移到 res://Config/Terrains.json
		TerrainConfig = new TerrainsConfigRepository(ConfigSource.LoadText("Terrains"));

		// 地图
		_mapGenerator = new VoronoiMapGenerator(TerrainConfig, "res://Config/Generator", _configLoader);
		_mapRepository = new GodotMapRepository(TerrainConfig);

		MapService = new MapAppService(_mapGenerator, _mapRepository);
	}
}
