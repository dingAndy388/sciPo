using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.9 / `WP-7.5`+`WP-7.6` 起步）**内容覆盖**判据：不只数"表里有几条"，更要问"内容自洽吗"。
	/// <list type="number">
	/// <item>单位全表 = 设计稿的 **10 玩家 + 5 敌方**；</item>
	/// <item>**每个玩家单位都有训练来源**（某座建筑的 `TrainableUnits` 里能查到它）——
	/// 漏一个就等于"设计稿里有、游戏里造不出来"这类最难发现的缺口；</item>
	/// <item>敌方单位必须齐备生成三件套（地形 / 概率 / 掉落），否则刷不出来或打死了没奖励；</item>
	/// <item>玩家单位不得带生成字段（会变成"随机刷出来的友军"）。</item>
	/// </list>
	/// </summary>
	internal static class ContentCoverageChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-7.5/7.6 单位全表：10 玩家 + 5 敌方（设计稿）", UnitCountsMatchDesign);
			Check.Run("WP-7.5/7.6 训练来源闭合：每个玩家单位都能在某座建筑的 TrainableUnits 里查到", EveryPlayerUnitHasATrainer);
			Check.Run("WP-7.5/7.6 敌方三件套：生成地形 / 概率 / 掉落齐备，且玩家单位不带生成字段", HostilesAreSpawnable);
		}

		private static (ConfigTables Tables, IBuildingConfigRepository Buildings) RealTables()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			return (core.Tables, core.Tables.Buildings);
		}

		private static void UnitCountsMatchDesign()
		{
			(ConfigTables tables, _) = RealTables();
			List<IUnitConfig> all = tables.AllUnits().ToList();

			Check.AssertEqual(15, all.Count, "单位全表应为 15 条（设计稿 unit.md）");
			Check.AssertEqual(10, all.Count(u => !u.IsHostile), "玩家单位 = 10");
			Check.AssertEqual(5, all.Count(u => u.IsHostile), "敌方单位 = 5");
			Check.AssertEqual(15, all.Select(u => u.UnitId).Distinct().Count(), "单位 Id 不应重复");

			foreach (string id in new[] { "worker", "swordsman", "archer", "explorer", "spearman", "heavy_guard", "engineer", "ballista", "scholar", "chemist" })
				Check.Assert(all.Any(u => u.UnitId == id), $"玩家单位 {id} 应在表里（设计稿）");

			foreach (string id in new[] { "wolf", "boar", "eagle", "ibex", "crocodile" })
				Check.Assert(all.Any(u => u.UnitId == id), $"敌方单位 {id} 应在表里（设计稿）");
		}

		private static void EveryPlayerUnitHasATrainer()
		{
			(ConfigTables tables, IBuildingConfigRepository buildings) = RealTables();

			var trainable = new HashSet<string>();
			foreach (IBuildingConfig building in buildings.GetAll())
				foreach (string unitId in building.TrainableUnits ?? new List<string>())
					trainable.Add(unitId);

			foreach (IUnitConfig unit in tables.AllUnits().Where(u => !u.IsHostile))
			{
				Check.Assert(trainable.Contains(unit.UnitId),
					$"玩家单位 {unit.UnitId} 没有任何训练来源（设计稿说它由某座建筑训练）");
				Check.Assert(unit.Duration > 0, $"{unit.UnitId} 的训练时间必须 > 0（0 日 = 瞬间出生）");
				Check.Assert(unit.ResourceCost != null && unit.ResourceCost.Count > 0, $"{unit.UnitId} 应有训练消耗");
				Check.Assert(unit.PopulationCost > 0, $"{unit.UnitId} 应消耗人口（否则人口模型形同虚设）");
			}
		}

		private static void HostilesAreSpawnable()
		{
			(ConfigTables tables, _) = RealTables();

			foreach (IUnitConfig hostile in tables.AllUnits().Where(u => u.IsHostile))
			{
				Check.Assert(!string.IsNullOrWhiteSpace(hostile.SpawnTerrain), $"{hostile.UnitId} 缺生成地形（永远刷不出来）");
				Check.Assert(hostile.SpawnChance > 0f && hostile.SpawnChance <= 1f, $"{hostile.UnitId} 生成概率应在 (0,1]（实际 {hostile.SpawnChance}）");
				Check.Assert(hostile.DropReward != null && hostile.DropReward.Count > 0, $"{hostile.UnitId} 缺掉落（击杀没有奖励）");
				Check.AssertEqual(0, hostile.Movement, $"{hostile.UnitId} 不应移动（设计稿：原地封锁）");
			}

			foreach (IUnitConfig player in tables.AllUnits().Where(u => !u.IsHostile))
			{
				Check.Assert(string.IsNullOrWhiteSpace(player.SpawnTerrain), $"{player.UnitId} 是玩家单位，不该填生成地形");
				Check.AssertEqual(0f, player.SpawnChance, $"{player.UnitId} 是玩家单位，不该填生成概率");
			}
		}
	}
}
