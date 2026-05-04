using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public interface IBuildingConfig
	{
		string BuildingID { get; set; }
		Dictionary<string, float> ResourceCost { get; set; }
		string TerrainRequirement { get; set; }
		List<string> TechRequirement { get; set; }
	}
}
