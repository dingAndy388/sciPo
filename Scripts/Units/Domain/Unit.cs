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

		/// <summary>
		/// （v0.3 / WP-3.6）**生命上限**：生成时取自配置的 `HP`，此后只读（`HP` 才是会变的那一半）。
		/// <para>用途：单位合并（`E15` / `WP-4.7`）的判据是"两个同模板单位的 HP 之和 ≤ 上限"，
		/// 没有上限字段就只能拿配置表当上限 —— 那会让"上限被修正器改过"的单位算错。</para>
		/// <para>与 <see cref="IsHostile"/> 同口径：它是**类型**属性（由配置派生），因此不写进存档；
		/// 读档时按 `Id` 查配置即可还原（`SaveRebuilder.RebuildUnit` 只回填 `HP`）。</para>
		/// </summary>
		public float MaxHP { get; private set; }
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
			MaxHP = hp; // WP-3.6：上限 = 配置的 HP（读档只回填当前值，上限仍由这里派生）
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