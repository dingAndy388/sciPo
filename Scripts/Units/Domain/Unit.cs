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
		public int AttackRadius { get; set; }
		public float AttackDamage { get; set; }
		public HexCubePosition? MoveTarget { get; set; }
		public List<HexCubePosition> MovePath { get; set; } = new();
		public string AttackTargetUid { get; set; }

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）**是否为敌方单位**：由配置在生成实例时派生（`IsHostile` 是**类型**属性，
		/// 不是实例状态，因此不写进存档 —— 读档时按 `Id` 查配置即可还原）。
		/// <para>敌方单位存活 = 该格封锁（不可进入/不可建造），见 `Map.IsHostileAt` 与 `NoHostileRequirement`。</para>
		/// </summary>
		public bool IsHostile { get; set; }

		public Unit(HexCubePosition coord, string id, string uid, int ownerId, string name,
			float hp, float mp, int attack, int movement, bool isIdle,
			int attackRadius, float attackDamage, bool isHostile = false)
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

			AttackRadius = attackRadius;
			AttackDamage = attackDamage;
			CurrentMP = 0;
			IsHostile = isHostile;
		}

		public MapOccupantInfo GetInfo()
		{
			return new MapOccupantInfo(Position, _id, _uid, _ownerId, _name, IsReady, HP, OccupantType.Unit, IsHostile);
		}
	}
}