using SciencePotato.Scripts.Map.Domain;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	[GlobalClass, Tool]
	public partial class TerrainConfigResources : Resource, ITerrainData 
	{
		[Export] public string Id { get; set; }
		[Export] public string Name { get; set; }
		[Export] public double Weight { get; set; } 
	}
}
