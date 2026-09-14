using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Units.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.8 / `UNIT-14`、`B7`）**敌方单位刷新器**：地图生成后按地形概率放置敌方单位
	/// （design/unit.md「生成方式：地图生成时，按地形概率放置敌方单位」）。
	/// <list type="bullet">
	/// <item>**候选**：配置表里 `IsHostile=true` 且声明了 `SpawnTerrain`/`SpawnChance` 的行（玩家单位不参与）。</item>
	/// <item>**逐格判定**：地块地形匹配 → 对该地形的各敌种**独立掷骰**（概率 = `SpawnChance`）；同格多敌种都通过时
	/// 按**概率降序**取第一个（`break`，不再掷后续敌种）⇒ 高概率敌种保真、低概率者被抢占（见 `D55`）。</item>
	/// <item>**一格一敌**：已被占据的格子直接跳过（走 <see cref="Map.PlaceOccupant"/> 的占用权威，不静默覆盖）。</item>
	/// <item>**确定性**：随机源从 `Map.seed` 派生，格子按 (q,r) 排序遍历 —— 同一 seed 必得同一批敌人。</item>
	/// </list>
	/// <para>敌方单位落地后即"封锁"该格（不可进入/不可建造/不可采集，见 `Map.IsHostileAt`），
	/// 玩家必须击败它才能进入（战斗 = `WP-3.6`，掉落 = `WP-3.7`）。</para>
	/// </summary>
	public sealed class EnemySpawner : IMapPostProcessor
	{
		/// <summary>
		/// 敌方阵营的 **OwnerId**（`-1` = 非玩家）。
		/// <para>沿用既有的"不同 OwnerId 即敌对"口径：移动服务的视野内敌情判定与攻击目标判定（同 owner 不可攻击）
		/// 都直接成立，不需要再加一套阵营表。</para>
		/// </summary>
		public const int HostileOwnerId = -1;

		private readonly IUnitsRepository _configs;
		private readonly UnitFactory _factory;
		private readonly Func<int, IRandom> _randomFactory;

		/// <param name="configs">单位配置表（敌方行提供 HP/ATK/生成地形/生成概率/掉落）。</param>
		/// <param name="factory">单位工厂（复用同一套实例构造，敌方与玩家单位字段口径一致）。</param>
		/// <param name="randomFactory">随机源工厂（种子 → 随机源）。缺省 = <see cref="SystemRandom"/>，
		/// 测试可注入"必中/必不中"以得到受控布局。</param>
		public EnemySpawner(IUnitsRepository configs, UnitFactory factory, Func<int, IRandom> randomFactory = null)
		{
			_configs = configs;
			_factory = factory;
			_randomFactory = randomFactory ?? (seed => new SystemRandom(seed));
		}

		public string Name => "EnemySpawner";

		/// <summary>最近一次刷新的敌方单位数（观测/断言用，不参与玩法）。</summary>
		public int LastSpawnedCount { get; private set; }

		public void Process(SciencePotato.Scripts.Map.Domain.Map map)
		{
			LastSpawnedCount = 0;
			if (map == null || _configs == null || _factory == null) return;

			// 候选按地形分组；组内按「概率降序 → UnitId 升序」排序，使结果与配置表的书写顺序无关
			Dictionary<string, List<IUnitConfig>> byTerrain = _configs.GetAll()
				.Where(c => c != null && c.IsHostile
					&& !string.IsNullOrWhiteSpace(c.SpawnTerrain)
					&& c.SpawnChance > 0f)
				.GroupBy(c => c.SpawnTerrain, StringComparer.Ordinal)
				.ToDictionary(
					group => group.Key,
					group => group.OrderByDescending(c => c.SpawnChance)
						.ThenBy(c => c.UnitId, StringComparer.Ordinal)
						.ToList(),
					StringComparer.Ordinal);

			if (byTerrain.Count == 0) return;

			IRandom random = _randomFactory?.Invoke(map.seed) ?? new SystemRandom(map.seed);

			// 格子顺序显式排序（不与字典枚举顺序挂钩）：生成确定性的另一半
			foreach (MapCell cell in map.GetAllCells().OrderBy(c => c.Position.q).ThenBy(c => c.Position.r))
			{
				if (cell.Occupant != null) continue; // 一格一占据物：已有建筑/单位的地块不刷怪
				if (cell.Terrain == null || string.IsNullOrWhiteSpace(cell.Terrain.Id)) continue;
				if (!byTerrain.TryGetValue(cell.Terrain.Id, out List<IUnitConfig> candidates)) continue;

				foreach (IUnitConfig candidate in candidates)
				{
					if (!random.ProbCodition(candidate.SpawnChance)) continue;

					if (Spawn(map, candidate, cell.Position)) LastSpawnedCount++;
					break; // 概率高者优先占格（`D55`）
				}
			}
		}

		/// <summary>
		/// 在指定格放置一个该敌种（走"占据物进入的唯一入口"，已被占用则失败）。
		/// <para>敌方单位没有训练过程：**生成即就绪**、不移动不巡逻（`IsIdle`）、MP 恒 0（也不会注册移动循环）；
		/// 也不揭雾（迷雾只由玩家单位与建筑产生）。</para>
		/// </summary>
		public bool Spawn(SciencePotato.Scripts.Map.Domain.Map map, IUnitConfig config, HexCubePosition position)
		{
			if (map == null || config == null) return false;

			Unit enemy = _factory?.CreateUnit(config.UnitId, position, HostileOwnerId);
			if (enemy == null) return false;

			enemy.IsReady = true;
			enemy.IsIdle = true;
			enemy.CurrentMP = 0f;

			return map.PlaceOccupant(enemy, position);
		}
	}
}
