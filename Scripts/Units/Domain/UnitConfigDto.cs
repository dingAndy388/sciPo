using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Units.Domain
{
	public class UnitConfigDto : IUnitConfig
	{
		public string UnitId { get; set; }
		public Dictionary<string, float> ResourceCost { get; set; }
		public List<string> TerrainRequirements { get; set; }
		public Dictionary<string, List<string>> TechRequirements { get; set; }
		public float Duration { get; set; }
		public float HP { get; set; }
		public int Attack { get; set; }
		public int Movement { get; set; }
		public List<string> Actions { get; set; }
		public int VisionRadius { get; set; }
		public float MoveRechargePerTick { get; set; }
		public int AttackRadius { get; set; }
		public float AttackDamage { get; set; }
		public int PopulationCost { get; set; }
	}
}