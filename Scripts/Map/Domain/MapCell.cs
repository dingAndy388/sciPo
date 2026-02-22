using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public class MapCell(IPosition position)
	{
		public IPosition position { get; set; } = position;
		public ITerrainData terrain { get; private set; }

		public void SetTerrain(ITerrainData terrain)
		{
			this.terrain = terrain;	
		}
	}	
}
