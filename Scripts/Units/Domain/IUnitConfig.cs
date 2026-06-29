using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Units.Domain
{
	public interface IUnitConfig
	{
		string UnitId { get; }
		Dictionary<string, float> ResourceCost { get; }
		List<string> TerrainRequirements { get; }
		Dictionary<string, List<string>> TechRequirements { get; }
		float Duration { get; }
		float HP { get; }
		int Attack { get; }
		int Movement { get; }
		List<string> Actions { get; }
		int VisionRadius { get; }
		float MoveRechargePerTick { get; }
		int AttackRadius { get; }
		float AttackDamage { get; }
		int PopulationCost { get; }
	}
}