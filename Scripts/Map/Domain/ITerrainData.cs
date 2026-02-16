using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface ITerrainData
	{
		string Id { get; set; }
		string Name { get; set; }
		double Weight { get; set; }
	}
}
