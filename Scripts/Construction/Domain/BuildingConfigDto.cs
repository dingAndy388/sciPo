using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class BuildingConfigDto : IBuildingConfig
	{
		public string BuildingId { get; set; }
		public string Name { get; set; }
		public Dictionary<string, float> ResourceCost { get; set; }
		public List<string> TerrainRequirements { get; set; }
		public Dictionary<string, List<string>> TechRequirements { get; set; }
		public List<Modifier> Modifiers { get; set; }
		public float Duration { get; set; }
		public List<string> Actions { get; set; }
		public int VisionRadius { get; set; }
		public bool IsHousing { get; set; }
		public int PopulationRadius { get; set; }
		public int PopulationCap { get; set; }
		public int PopulationGrowthInterval { get; set; }
	}
}