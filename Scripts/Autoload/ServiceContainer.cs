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
	public MapQueryService MapQuery { get; private set; }
	public MapModificationService MapMod { get; private set; }
	public MapGenerationService MapGeneration { get; private set; }

	public IConfigLoader ConfigLoader { get; private set; }

	//Interfaces
	private IMapRepository _mapRepository;
	private IRandom _random;
	private IConfigLoader _configLoader;

	//Instance
	private IMapGenerator _mapGenerator;

	public override void _Ready()
	{
		Instance = this;
		_configLoader = new GodotConfigService();
		_mapGenerator = new VoronoiMapGenerator("res://Config/Terrains", "res://Config/Generator", _configLoader);
		_mapRepository = new GodotMapRepository();

		MapQuery = new MapQueryService(_mapRepository);
		MapMod = new MapModificationService(_mapRepository);
		MapGeneration = new MapGenerationService(_mapGenerator, _mapRepository);

		ConfigLoader = new GodotConfigService();
	}
}
