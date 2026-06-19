using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class Building(HexCubePosition coord, string id, long uid, string name) : IMapOccupant
	{
		private MapOccupantInfo info;

		private readonly HexCubePosition _pos = coord;
		private readonly string _id = id;
		private readonly long _uid = uid;
		private readonly string _name = name;
		public bool IsReady { get; set; } = false;

		public MapOccupantInfo GetInfo()
		{
			info = new MapOccupantInfo(_pos, _id,_uid,_name, IsReady, -1f, OccupantType.Building);
			return info;
		}
	}
}
