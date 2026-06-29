using Godot;
using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	[GlobalClass, Tool]
	public partial class TerrainConfigResources : Resource, ITerrainData
	{
		[Export] public string Id { get; set; }
		[Export] public string Name { get; set; }
		[Export] public float Weight { get; set; }
		[Export] public float MoveCost { get; set; }
	}
}
