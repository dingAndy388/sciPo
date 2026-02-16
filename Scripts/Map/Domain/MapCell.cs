using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public class MapCell(IPosition position)
	{
		public IPosition position = position;
		public ITerrainData terrain;
		public Dictionary<ResourcesType, int> resources;
	}
}
