using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Units.Domain
{
	public interface IUnitConfig
	{
		string UnitId { get; }
		Dictionary<string, float> ResourceCost { get; }
		List<string> TerrainRequirements { get; }
		Dictionary<string, List<string>> TechRequirements { get; }
		float Duration { get; }
		float HP { get; }
		int Attack { get; }
		int Movement { get; }
		List<string> Actions { get; }
		int VisionRadius { get; }


		int AttackRadius { get; }
		float AttackDamage { get; }
		int PopulationCost { get; }

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）**是否敌方单位**（design/unit.md「敌方单位」表的「分类」列）。
		/// <para>`true` 的行由地图生成后处理（`EnemySpawner`）按地形概率放置：不参与训练系统、不消耗资源、
		/// 无移动力、不揭雾；`false` 的行是玩家单位，只走训练路径（`TrainUnit`）。</para>
		/// </summary>
		bool IsHostile { get; }

		/// <summary>
		/// （v0.3 / WP-3.8）**生成地形 Id**（仅敌方单位）：只有地块地形与它一致时才可能被刷新出来；
		/// 空字符串 = 该行永远不会出现在地图上（校验器判 error）。
		/// </summary>
		string SpawnTerrain { get; }

		/// <summary>
		/// （v0.3 / WP-3.8）**生成概率**（仅敌方单位，取值 (0,1]）：口径是"每个适配地块出现该敌种的边际概率"，
		/// 与 design/unit.md 的「平原 15% 出野狼」一致。
		/// <para>同一地形有多个敌种时**各自独立掷骰**；同格多敌种都通过时按概率**降序**取第一个（概率高者优先占格）
		/// ⇒ 实际出现率 ≤ 表内值，差值就是被更高概率敌种抢占的部分（平原：野狼 15%、野猪 ≈8.5%，见 `D55`）。</para>
		/// </summary>
		float SpawnChance { get; }

		/// <summary>（v0.3 / WP-3.8）**击败掉落**（仅敌方单位）：资源名 → 数量，直接进资源池（消费方 = `WP-3.7`）。</summary>
		Dictionary<string, float> DropReward { get; }
	}
}