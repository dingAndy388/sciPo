using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SciencePotato.Scripts.Units.Domain
{
	public class Unit(HexCubePosition coord, string id, long uid, string name, float hp, int attack, int movement, bool isIdle) : IMapOccupant
	{
		//id: same as unit config id
		private readonly string _id = id;
		//uid: unique identifier for this unit instance
		private readonly long _uid = uid;
		//name: display name for this unit
		private readonly string _name = name;

		public HexCubePosition Position { get; set; } = coord;
		public bool IsReady { get; set; } = false;
		public float HP { get; set; } = hp;
		public int Attack { get; set; } = attack;
		public int Movement { get; set; } = movement;
		public bool IsIdle { get; set; } = isIdle;

		public MapOccupantInfo GetInfo()
		{
			return new MapOccupantInfo(Position,_id,_uid,_name,IsReady, HP,OccupantType.Unit);
		}
	}
}
