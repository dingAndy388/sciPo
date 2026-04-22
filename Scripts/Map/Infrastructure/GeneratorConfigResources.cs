using Godot;
using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	public partial class GeneratorConfigResources : Resource, IMapGeneratorConfig
	{
		[Export] public float Density { get; set; }
	}
}
