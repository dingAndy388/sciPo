using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Units.Domain
{
	public class UnitConfigDto : IUnitConfig
	{
		public string UnitId { get; set; }
		public Dictionary<string, float> ResourceCost { get; set; }
		public List<string> TerrainRequirements { get; set; }
		public Dictionary<string, List<string>> TechRequirements { get; set; }
		public float Duration { get; set; }
		public float HP { get; set; }
		public int Attack { get; set; }
		public int Movement { get; set; }
		public List<string> Actions { get; set; }
		public int VisionRadius { get; set; }


		public int AttackRadius { get; set; }
		public float AttackDamage { get; set; }
		public int PopulationCost { get; set; }

		/// <summary>（v0.3 / WP-3.9 / `UNIT-19`）月度维护费：资源名 → 每月数量（空表 = 不维护）。</summary>
		public Dictionary<string, float> Maintenance { get; set; }

		/// <summary>（v0.3 / WP-3.8 / `UNIT-14`）敌方单位标记（分类 = 敌方单位）。</summary>
		public bool IsHostile { get; set; }

		/// <summary>（v0.3 / WP-3.8）生成地形 Id（仅敌方单位；空 = 永不出现在地图上）。</summary>
		public string SpawnTerrain { get; set; }

		/// <summary>（v0.3 / WP-3.8）生成概率（仅敌方单位，取值 (0,1]；0 = 不刷新）。</summary>
		public float SpawnChance { get; set; }

		/// <summary>（v0.3 / WP-3.8）击败掉落（仅敌方单位；消费方 = `WP-3.7`）。</summary>
		public Dictionary<string, float> DropReward { get; set; }
	}
}