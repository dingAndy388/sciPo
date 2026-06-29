using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Units.Domain
{
	public class Unit : IMapOccupant
	{
		private readonly string _id;
		private readonly string _uid;
		private readonly int _ownerId;
		private readonly string _name;

		public HexCubePosition Position { get; set; }
		public bool IsReady { get; set; }
		public float HP { get; set; }
		public float MovementPoint { get; set; }
		public int Attack { get; set; }
		public int Movement { get; set; }
		public bool IsIdle { get; set; }

		// Move/Attack engine fields
		public float CurrentMP { get; set; }
		public float MoveRechargePerTick { get; set; }
		public int AttackRadius { get; set; }
		public float AttackDamage { get; set; }
		public HexCubePosition? MoveTarget { get; set; }
		public List<HexCubePosition> MovePath { get; set; } = new();
		public string AttackTargetUid { get; set; }

		public Unit(HexCubePosition coord, string id, string uid, int ownerId, string name,
			float hp, float mp, int attack, int movement, bool isIdle,
			float moveRechargePerTick, int attackRadius, float attackDamage)
		{
			Position = coord;
			_id = id;
			_uid = uid;
			_ownerId = ownerId;
			_name = name;
			HP = hp;
			MovementPoint = mp;
			Attack = attack;
			Movement = movement;
			IsIdle = isIdle;

			MoveRechargePerTick = moveRechargePerTick;
			AttackRadius = attackRadius;
			AttackDamage = attackDamage;
			CurrentMP = 0;
		}

		public MapOccupantInfo GetInfo()
		{
			return new MapOccupantInfo(Position, _id, _uid, _ownerId, _name, IsReady, HP, OccupantType.Unit);
		}
	}
}