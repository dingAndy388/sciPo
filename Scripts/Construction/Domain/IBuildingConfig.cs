using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public interface IBuildingConfig
	{
		string BuildingId { get;}

		Dictionary<string, float> ResourceCost { get; }
		List<string> TerrainRequirements { get;}
		Dictionary<string,List<string>> TechRequirements { get; }

		List<Modifier> Modifiers { get; }

		float Duration { get; }
	}
}
