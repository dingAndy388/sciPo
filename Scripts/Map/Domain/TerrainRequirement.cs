using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public class TerrainRequirement(Map map, HexCubePosition position,string targetTerrain):IRequirement
	{
		private Map _map = map;
		private HexCubePosition _position = position;
		private string _targetTerrain = targetTerrain;

		public bool IsMet()
		{
			if(_targetTerrain=="*")
				return true;
			return _map.VerifyTerrain(_position, _targetTerrain);
		}
	}
}
