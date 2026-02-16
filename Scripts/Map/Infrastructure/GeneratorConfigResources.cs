using Godot;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	public partial class GeneratorConfigResources : Resource, IMapGeneratorConfig
	{
		[Export] public double Density { get; set; }
	}
}
