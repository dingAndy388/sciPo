using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class Building(HexCubePosition coord, int id, long uid, string name) : IMapOccupant
	{
		private MapOccupantInfo info;

		private readonly HexCubePosition _pos = coord;
		private readonly int _id = id;
		private readonly long _uid = uid;
		private readonly string _name = name;

		public MapOccupantInfo GetInfo()
		{
			info = new MapOccupantInfo(_pos, _id,_uid,_name,OccupantType.Building);
			return info;
		}
	}
}
