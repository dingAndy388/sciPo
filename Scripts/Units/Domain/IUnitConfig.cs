using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SciencePotato.Scripts.Units.Domain
{
	public interface IUnitConfig
	{
		string UnitId { get; set; }

		Dictionary<string, float> ResourceCost { get; set; }
		List<string> TerrainRequirements { get; set; }
		Dictionary<string, List<string>> TechRequirements { get; set; }

		float Duration { get; }
		float HP { get; set; }
		int Attack { get; set; }
		int Movement { get; set; }	
	}
}
