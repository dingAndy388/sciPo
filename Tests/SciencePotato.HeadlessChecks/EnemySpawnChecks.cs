using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using SciencePotato.Scripts.Units.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-3.8 / `UNIT-14`、`B7`/`B8`/`E19`）**敌方单位：按地形概率放置 + 地块封锁**的验收检查 ——
	/// M0-3 ④ 的门槛：「敌方单位封锁格不可建造」，同时锁住生成概率口径、生成确定性、封锁三件套
	/// （不可进入 / 不可建造 / 解除后恢复）与"敌方单位随存档保留"。
	/// <para>两类夹具：**spawned harness**（真实组合根 + 敌方刷新开启，验生成）与 **sandbox harness**
	/// （刷新关闭 + 用例自己放敌人，验封锁语义，避免随机敌人抢占用例布置的格位）。</para>
	/// </summary>
	internal static class EnemySpawnChecks
	{
		private const string MapId = "enemy-map";

		/// <summary>概率断言的样本量：40+ 的边长让每种地形都有上百格。</summary>
		private const int BigMap = 48;

		private const int SmallMap = 8;

		public static void RunAll()
		{
			Check.Run("WP-3.8 刷新：地图生成后按地形概率放置敌方单位（`B7`/`UNIT-14`）", SpawnsByTerrainProbability);
			Check.Run("WP-3.8 生成概率：野狼 ≈ 平原格 × 15%、野猪 ≈ 10% × (1−15%)（同格冲突让位，`D55`）", SpawnChanceMatchesTable);
			Check.Run("WP-3.8 概率边界：必中则刷满、必不中则一个不刷（受控随机源）", SpawnChanceBoundaries);
			Check.Run("WP-3.8 刷新确定性：同一 seed → 同一布局；不同 seed → 不同布局", SpawnIsDeterministic);
			Check.Run("M0-3 ④ 敌方封锁格不可建造（`B8`）", HostileBlocksConstruction);
			Check.Run("WP-3.8 封锁：敌方单位所在格不可进入（`CanEnter` + 寻路 + 位移三重）", HostileBlocksMovement);
			Check.Run("WP-3.8 解除封锁：敌方单位移除后该格恢复可通行与可建造（`B8`）", RemovingHostileReleasesCell);
			Check.Run("WP-3.8 敌方单位不进入周期循环：无移动任务、不揭雾、MP 恒 0", HostilesHaveNoMoveLoop);
			Check.Run("WP-3.8 敌方单位随地图存档/读档保留（`IsHostile` 由配置派生、封锁语义仍生效）", HostileSurvivesSaveLoad);
			Check.Run("WP-3.8 位移更新占用索引：走过后旧格不再留僵尸占据物", MovementUpdatesOccupancy);
			Check.Run("WP-3.8 配置校验：敌方字段规则（概率越界 / 缺生成地形 / 玩家单位误填生成字段）", ConfigRulesForHostiles);
			Check.Run("WP-3.8 装配：`CoreDependencies` 默认开启敌方刷新，组合根据此挂上生成后处理器", CompositionWiresSpawner);
			Check.Run("WP-3.8 越界目标：寻路不再把地图外当可通行，位移不再抛 `KeyNotFoundException`", OutOfBoundsTargetIsSafe);
		}

		// ────────────────────────── 用例：生成 ──────────────────────────

		private static void SpawnsByTerrainProbability()
		{
			Harness h = NewHarness(enableEnemySpawn: true);
			try
			{
				h.Map.GenerateMap(20260916, BigMap, BigMap, MapId);
				Map map = h.MapOf(MapId);

				List<(HexCubePosition Position, Unit Enemy)> hostiles = HostilesOf(map);
				int cells = map.GetAllCells().Count();

				Check.Assert(hostiles.Count > 0, "按地形概率刷新后地图上应有敌方单位（`B7`）");
				Check.Assert(hostiles.Count < cells, "不应每个格子都是敌人（各敌种概率 < 1）");
				Check.AssertEqual(hostiles.Count, hostiles.Select(x => x.Position).Distinct().Count(), "一格至多一个敌方单位");

				foreach ((HexCubePosition position, Unit enemy) in hostiles)
				{
					IUnitConfig config = h.Tables.Units.GetUnitConfig(enemy.GetInfo().Id);

					Check.Assert(config != null && config.IsHostile, $"{enemy.GetInfo().Id} 应是配置表里的敌方单位");
					Check.AssertEqual(config.SpawnTerrain, map.GetCell(position).Terrain.Id, $"{config.UnitId} 只应出现在 {config.SpawnTerrain} 上");
					Check.AssertEqual(EnemySpawner.HostileOwnerId, enemy.GetInfo().OwnerId, "敌方单位属于敌方阵营（OwnerId = -1）");
					Check.AssertEqual(config.HP, enemy.HP, $"{config.UnitId} 的 HP 取配置表");
					Check.AssertEqual(config.AttackDamage, enemy.AttackDamage, $"{config.UnitId} 的攻击取配置表");
					Check.Assert(enemy.IsReady, "生成即就绪（敌方单位没有训练过程）");
					Check.AssertEqual(0, enemy.Movement, "敌方单位没有移动力");
					Check.AssertEqual(0f, enemy.CurrentMP, "敌方单位不移动 → MP 恒 0");
				}

				// 概率口径（design/unit.md 的「生成地点/生成概率」列）：每个敌种的数量应接近「适配格数 × 概率」。
				// 固定 seed ⇒ 结果确定；这里给 35% 容差只为容忍算法细节调整，而不是随机波动。
				foreach (string speciesId in new[] { "wolf", "boar", "eagle", "ibex", "crocodile" })
				{
					IUnitConfig config = h.Tables.Units.GetUnitConfig(speciesId);
					int terrainCells = map.GetAllCells().Count(c => c.Terrain != null && c.Terrain.Id == config.SpawnTerrain);
					int spawned = hostiles.Count(x => x.Enemy.GetInfo().Id == speciesId);
					double expected = terrainCells * config.SpawnChance;

					// 真实 Voronoi 地形下每种地形的格数差别很大（山地只有几十格），因此这里只做**宽松**检查：
					// 数量级不偏、且至少出现一次；精确的概率口径在 `SpawnChanceMatchesTable`（受控平原图）里验。
					Check.Assert(terrainCells >= 10, $"{config.SpawnTerrain} 的格数应足够做抽样断言（实际 {terrainCells}）");
					Check.Assert(spawned > 0, $"{speciesId} 应至少刷出 1 个（期望 ≈ {expected:F0}）");
					Check.Assert(Math.Abs(spawned - expected) <= expected * 0.5 + 3,
						$"{speciesId} 的刷新量应与配置概率同量级（{config.SpawnTerrain} {terrainCells} 格 × {config.SpawnChance:P0} → 期望 ≈ {expected:F0}，实际 {spawned}）");
				}
			}
			finally { Cleanup(h.Dir); }
		}

		private static void SpawnChanceMatchesTable()
		{
			Harness h = NewHarness(enableEnemySpawn: false);
			try
			{
				const int Side = 32; // 1024 格受控平原：样本量足够把"15% / 10%"验到几十个的数量级
				h.GeneratePlainMap(20260916, Side);

				Map map = h.MapOf(MapId);
				Check.AssertEqual(Side * Side, map.GetAllCells().Count(), "准备：受控平原图的格数");

				// 直接用**生产刷新器**（随机源 = SystemRandom(map.seed)）跑一遍
				h.Spawner.Process(map);

				List<(HexCubePosition Position, Unit Enemy)> hostiles = HostilesOf(map);
				int cells = Side * Side;
				int wolves = hostiles.Count(x => x.Enemy.GetInfo().Id == "wolf");
				int boars = hostiles.Count(x => x.Enemy.GetInfo().Id == "boar");

				Check.AssertEqual(hostiles.Count, wolves + boars, "受控平原图上只应刷出平原敌种（野狼/野猪）");

				// 野狼 15%：同格冲突时概率高者优先 → 它的出现率**保真**
				double wolfRate = (double)wolves / cells;
				Check.Assert(Math.Abs(wolfRate - 0.15) <= 0.03, $"野狼出现率应 ≈ 15%（实际 {wolfRate:P1}，{wolves}/{cells}）");

				// 野猪 10%，但同格被野狼抢走时让位（独立掷骰 × 野狼未通过）→ 实际 ≈ 8.5%（`D55`）
				double boarRate = (double)boars / cells;
				Check.Assert(boarRate >= 0.06 && boarRate <= 0.10, $"野猪出现率应落在 [6%, 10%]（实际 {boarRate:P1}，{boars}/{cells}）");
				Check.Assert(Math.Abs(boarRate - 0.10 * 0.85) <= 0.03, $"野猪 ≈ 10% × (1 − 15%) ≈ 8.5%（实际 {boarRate:P1}）");

				// 一格一敌：两种平原敌种不会叠在同一格
				Check.Assert(wolves + boars <= cells, "一格至多一个敌方单位");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void SpawnChanceBoundaries()
		{
			Harness full = NewHarness(enableEnemySpawn: false);
			try
			{
				full.GeneratePlainMap(20260916, SmallMap);

				// 必中（受控随机源 `FixedRandom(true)`）：每个适配地块都刷出敌人
				var always = new EnemySpawner(full.Tables.Units, new UnitFactory(full.Tables.Units), _ => new FixedRandom(true));
				always.Process(full.MapOf(MapId));

				Check.AssertEqual(SmallMap * SmallMap, always.LastSpawnedCount, "概率必中 → 每个平原格都应刷出敌人");
				Check.Assert(!full.Movement.CanEnter(MapId, new HexCubePosition(2, 2)), "全图刷满后该格不可进入（封锁）");
				Check.AssertEqual(always.LastSpawnedCount, HostilesOf(full.MapOf(MapId)).Count, "刷新计数与地图上的敌方单位数一致");
			}
			finally { Cleanup(full.Dir); }

			Harness empty = NewHarness(enableEnemySpawn: false);
			try
			{
				empty.GeneratePlainMap(20260916, SmallMap);

				// 必不中：一个都不刷（同一张受控平原图的另一份副本）
				var never = new EnemySpawner(empty.Tables.Units, new UnitFactory(empty.Tables.Units), _ => new FixedRandom(false));
				never.Process(empty.MapOf(MapId));

				Check.AssertEqual(0, never.LastSpawnedCount, "概率必不中 → 一个都不刷");
				Check.AssertEqual(0, HostilesOf(empty.MapOf(MapId)).Count, "地图上不应有敌方单位");
			}
			finally { Cleanup(empty.Dir); }
		}

		private static void SpawnIsDeterministic()
		{
			Harness h = NewHarness(enableEnemySpawn: true);
			try
			{
				string first = h.Layout(20260916, "det-a");
				string second = h.Layout(20260916, "det-b");
				string other = h.Layout(20260917, "det-c");

				Check.Assert(first.Length > 0, "固定 seed 应刷出敌方单位");
				Check.AssertEqual(first, second, "同一 seed 的地图应得到完全相同的敌方布局（随机源由 map.seed 派生）");
				Check.Assert(first != other, "不同 seed 的敌方布局应不同（否则随机源没生效）");
			}
			finally { Cleanup(h.Dir); }
		}

		// ────────────────────────── 用例：边界与占用一致性 ──────────────────────────

		private static void OutOfBoundsTargetIsSafe()
		{
			Harness h = NewHarness(enableEnemySpawn: false);
			try
			{
				h.GeneratePlainMap(20260916, SmallMap);

				var start = new HexCubePosition(1, 1);
				Unit worker = h.SpawnWorker(start);

				// 越界格不该被当成"可通行"：旧实现 `IsPassable` 对地图外返回 true，于是路径可能走出地图，
				// 随后 `CanEnter`/`TerrainCost` 因 `GetCell` 抛 KeyNotFoundException 把整局打断
				Check.Assert(!h.Movement.CanEnter(MapId, new HexCubePosition(-1, -1)), "地图外的格子不可进入");
				Check.AssertEqual(0, h.Movement.SetDestination(MapId, worker.GetInfo().UId, new HexCubePosition(99, 99)),
					"越界目的地应判不可达（不抛异常）");

				h.Clock.AdvanceDays(20);
				Check.AssertEqual(start, worker.Position, "20 日后单位应原地不动（不追越界目标）");
			}
			finally { Cleanup(h.Dir); }
		}

		// ────────────────────────── 用例：封锁（不可建造 / 不可进入 / 解除） ──────────────────────────

		private static void HostileBlocksConstruction()
		{
			Harness h = NewHarness(enableEnemySpawn: false);
			try
			{
				h.GeneratePlainMap(20260916, SmallMap);

				var blocked = new HexCubePosition(3, 4);
				var buildable = new HexCubePosition(5, 4);
				Unit enemy = h.PlaceEnemy("wolf", blocked);

				Check.Assert(!h.Map.IsClear(MapId, blocked), "敌方占据的格子不是空格（敌人就是该格的占据物）");
				Check.Assert(!h.Map.GetNoHostileRequirement(MapId, blocked).IsMet(), "封锁判定：敌人存活 → 不可建造");
				Check.Assert(h.Map.GetNoHostileRequirement(MapId, buildable).IsMet(), "对照：空地没有被封锁");

				// ① 建造 API 必须被拒，且**无副作用**（不扣资源、不留建筑）
				h.Resources.AddResource("BasicMinerals", 500f, MapId, h.OwnerId);
				h.Resources.AddResource("Food", 500f, MapId, h.OwnerId);
				float woodBefore = h.Resources.GetOrCreatePool(MapId, h.OwnerId).GetValue("BasicMinerals");

				Check.Assert(!h.Construction.StartConstruction(MapId, "camp", blocked, h.OwnerId),
					"M0-3 ④：敌方封锁格不可建造");
				Check.Assert(h.Map.GetBuildingInfo(MapId, blocked) == null, "被拒后不应留下建筑");
				Check.AssertEqual(woodBefore, h.Resources.GetOrCreatePool(MapId, h.OwnerId).GetValue("BasicMinerals"), "被拒后不应扣资源");
				Check.Assert(!h.Map.IsHostileAt(MapId, buildable), "对照：空地没有被敌方封锁");

				// ② 工人经由 `CanBuild` 动作同样被拒（能力/忙闲/距离都满足，唯一原因是封锁）
				Unit worker = h.SpawnWorker(new HexCubePosition(4, 4)); // 与 blocked 相邻（距离 1）
				Check.Assert(!h.Units.ExcuteAction(MapId, worker.GetInfo().UId, blocked, "camp", "CanBuild"),
					"工人不得在敌方封锁格开工");
				Check.Assert(worker.IsIdle, "被拒后工人应仍然空闲（不能因为失败就把建造者卡住）");

				// ③ 对照：相邻空格可以开工 —— 证明上面的拒绝来自"封锁"，而不是地形/资源/距离
				Check.Assert(h.Units.ExcuteAction(MapId, worker.GetInfo().UId, buildable, "camp", "CanBuild"),
					"对照：相邻空地可以开工");
				Check.Assert(!worker.IsIdle, "对照：开工后建造者被占用");

				Check.Assert(h.Map.GetBuildingInfo(MapId, buildable) != null, "对照：开工后 buildable 上是建筑（不是敌人）");
				Check.Assert(h.Map.FindOccupantByUId(MapId, enemy.GetInfo().UId) != null, "敌人仍在图上");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void HostileBlocksMovement()
		{
			Harness h = NewHarness(enableEnemySpawn: false);
			try
			{
				h.GeneratePlainMap(20260916, SmallMap);

				var blocked = new HexCubePosition(3, 4);
				h.PlaceEnemy("wolf", blocked);
				Unit worker = h.SpawnWorker(new HexCubePosition(4, 4));

				h.Fog.RevealArea(blocked, 2); // 先让该格"已探索"，把判据隔离到封锁本身

				Check.Assert(!h.Movement.CanEnter(MapId, blocked), "敌方单位所在格不可进入（`B8`：必须先击败敌人）");
				Check.Assert(h.Movement.CanEnter(MapId, new HexCubePosition(5, 5)), "对照：空地仍可进入");

				Check.AssertEqual(0, h.Movement.SetDestination(MapId, worker.GetInfo().UId, blocked),
					"直接以敌方格为目的地：应不可达（寻路与位移用同一套封锁判据）");
				Check.Assert(worker.MoveTarget == null, "不可达时不应留下移动指令");

				h.Clock.AdvanceDays(10);
				Check.AssertEqual(new HexCubePosition(4, 4), worker.Position, "10 日后单位仍不应站进敌人的格子");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void RemovingHostileReleasesCell()
		{
			Harness h = NewHarness(enableEnemySpawn: false);
			try
			{
				h.GeneratePlainMap(20260916, SmallMap);

				var walkCell = new HexCubePosition(3, 4);
				var buildCell = new HexCubePosition(5, 4);
				Unit walkEnemy = h.PlaceEnemy("wolf", walkCell);
				Unit buildEnemy = h.PlaceEnemy("boar", buildCell);
				Unit worker = h.SpawnWorker(new HexCubePosition(4, 4));
				h.Fog.RevealArea(walkCell, 2);

				Check.Assert(!h.Movement.CanEnter(MapId, walkCell), "准备：封锁中（不可进入）");
				Check.Assert(!h.Map.GetNoHostileRequirement(MapId, buildCell).IsMet(), "准备：封锁中（不可建造）");

				// 敌人被移除（阵亡/清场）——封锁就是"那格有没有敌方占据物"，因此自动解除（`B8`）
				h.Map.RemoveOccupantByPosition(MapId, walkCell, walkEnemy);
				h.Map.RemoveOccupantByPosition(MapId, buildCell, buildEnemy);

				Check.Assert(h.Map.IsClear(MapId, walkCell), "敌人移除后该格恢复为空");
				Check.Assert(h.Map.GetNoHostileRequirement(MapId, walkCell).IsMet(), "封锁解除（`NoHostileRequirement` 通过）");
				Check.Assert(h.Movement.CanEnter(MapId, walkCell), "恢复可通行");
				Check.Assert(h.Map.FindOccupantByUId(MapId, walkEnemy.GetInfo().UId) == null, "移除后按 uid 查不到敌人（无僵尸索引）");

				// ① 真的能走进去（不只是"判定通过"）
				Check.AssertEqual(1, h.Movement.SetDestination(MapId, worker.GetInfo().UId, walkCell), "解除封锁后应能下达移动指令");
				h.Clock.AdvanceDays(10);
				Check.AssertEqual(walkCell, worker.Position, "10 日后单位应站进原敌方格（平原 5 MP ≤ 10）");
				Check.AssertEqual(0f, worker.CurrentMP, "到达目的地后 MP 清零（R6）");

				// ② 真的能建造
				h.Resources.AddResource("BasicMinerals", 500f, MapId, h.OwnerId);
				Check.Assert(h.Construction.StartConstruction(MapId, "camp", buildCell, h.OwnerId), "解除封锁后恢复可建造");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void HostilesHaveNoMoveLoop()
		{
			Harness h = NewHarness(enableEnemySpawn: false);
			try
			{
				h.GeneratePlainMap(20260916, SmallMap);

				var blocked = new HexCubePosition(3, 4);
				Unit enemy = h.PlaceEnemy("wolf", blocked);

				Check.AssertEqual(0, h.Fog.GetVisibility(blocked), "放置敌方单位不应揭开迷雾（迷雾只由玩家单位/建筑产生）");

				// 读档恢复同理：`RestoreUnitTasks` 会给每个玩家单位重建移动循环，但敌方单位**不移动**（design「行为模式」）
				Check.AssertEqual(0, h.Units.RestoreUnitTasks(MapId, new Dictionary<string, float>()),
					"敌方单位不应有任何周期任务（不移动、不巡逻、不追击）");
				Check.AssertEqual(0, h.Time.SubscriberCount, "时间总线上不应出现敌方单位的任务");

				// 对照：玩家单位确实注册了移动循环
				Unit worker = h.SpawnWorker(new HexCubePosition(6, 6));
				Check.Assert(h.Time.SubscriberCount > 0, "对照：玩家单位应有周期任务（移动循环）");
				Check.Assert(h.Map.FindOccupantByUId(MapId, enemy.GetInfo().UId) != null, "敌方单位在图上（按 uid 可查）");
				Check.Assert(h.Map.FindOccupantByUId(MapId, worker.GetInfo().UId) != null, "对照：玩家单位也在图上");
			}
			finally { Cleanup(h.Dir); }
		}
		// ────────────────────────── 用例：存档 / 占用一致性 / 配置 / 装配 ──────────────────────────

		private static void HostileSurvivesSaveLoad()
		{
			Harness h = NewHarness(enableEnemySpawn: true);
			try
			{
				h.Map.GenerateMap(20260916, 16, 16, MapId);

				(HexCubePosition position, Unit enemy) = HostilesOf(h.MapOf(MapId)).First();
				string uid = enemy.GetInfo().UId;
				float hp = enemy.HP;

				// 存档（`SaveMapper` → `MapSave`）→ 逐出内存缓存 → 重新读档（`SaveRebuilder` 按 uid 重建）
				h.SessionMaps.Flush(MapId);
				h.SessionMaps.Evict(MapId);
				Map restored = h.MapOf(MapId);

				var reloaded = (Unit)restored.GetOccupantByUId(uid);
				Check.Assert(reloaded != null, "敌方单位应按 uid 读回（占据物随地图落盘）");
				Check.AssertEqual(position, reloaded.Position, "读档后仍在原格");
				Check.AssertEqual(hp, reloaded.HP, "读档后 HP 保留");

				// `IsHostile` 是**类型**属性（从配置派生），因此不写进存档也照样还原
				Check.Assert(reloaded.IsHostile, "读档后仍是敌方单位（`IsHostile` 由配置派生）");
				Check.AssertEqual(EnemySpawner.HostileOwnerId, reloaded.GetInfo().OwnerId, "读档后仍属敌方阵营");
				Check.AssertEqual(0, reloaded.Movement, "读档后仍无移动力");
				Check.Assert(restored.IsHostileAt(position), "读档后该格仍被封锁");
				Check.Assert(!h.Map.IsClear(MapId, position), "读档后该格仍不是空格");
				Check.Assert(!h.Movement.CanEnter(MapId, position), "读档后仍不可进入");
				Check.Assert(!h.Construction.StartConstruction(MapId, "camp", position, h.OwnerId), "读档后仍不可建造");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void MovementUpdatesOccupancy()
		{
			Harness h = NewHarness(enableEnemySpawn: false);
			try
			{
				h.GeneratePlainMap(20260916, SmallMap);

				var start = new HexCubePosition(1, 4);
				Unit worker = h.SpawnWorker(start);
				Check.AssertEqual(worker.GetInfo().UId, h.Map.GetOccupantInfo(MapId, start).Value.UId, "准备：出发格上是该单位");

				h.Movement.SetDestination(MapId, worker.GetInfo().UId, new HexCubePosition(3, 4));
				h.Clock.AdvanceDays(10); // 平原 5 MP → 一个周期走 2 格

				Check.AssertEqual(new HexCubePosition(3, 4), worker.Position, "准备：已走 2 格");
				Check.AssertEqual(worker.GetInfo().UId, h.Map.GetOccupantInfo(MapId, new HexCubePosition(3, 4)).Value.UId,
					"新格上记录着该单位（占用权威随位移更新）");
				Check.Assert(h.Map.GetOccupantInfo(MapId, start) == null, "旧格不再留着僵尸占据物（位移走 `Map.MoveOccupant`）");
				Check.Assert(h.Map.GetOccupantInfo(MapId, new HexCubePosition(2, 4)) == null, "途经格也不留占据物");
				Check.Assert(h.Map.FindOccupantByUId(MapId, worker.GetInfo().UId) != null, "索引仍能按 uid 查到该单位");
			}
			finally { Cleanup(h.Dir); }
		}


		private static void ConfigRulesForHostiles()
		{
			ConfigTables tables = ConfigFixtures.BuildRealCore().Tables;

			// 真实表：5 个敌种 + 3 个玩家单位；敌方行必须落在设计稿的地形/概率上
			Check.AssertEqual(5, tables.AllUnits().Count(u => u.IsHostile), "敌方单位条目数（design/unit.md：野狼/野猪/山鹰/巨角山羊/巨鳄）");
			Check.AssertEqual(3, tables.AllUnits().Count(u => !u.IsHostile), "玩家单位条目数");
			Check.AssertEqual(0, tables.AllUnits().Count(u => !u.IsHostile && (u.SpawnChance > 0f || !string.IsNullOrWhiteSpace(u.SpawnTerrain))),
				"玩家单位不参与刷新（不得填生成字段）");

			Check.AssertEqual("plain", tables.Units.GetUnitConfig("wolf").SpawnTerrain, "野狼生成地点 = 平原");
			Check.AssertEqual(0.15f, tables.Units.GetUnitConfig("wolf").SpawnChance, "野狼生成概率 = 15%");
			Check.AssertEqual(0.10f, tables.Units.GetUnitConfig("boar").SpawnChance, "野猪生成概率 = 10%");
			Check.AssertEqual("mountain", tables.Units.GetUnitConfig("eagle").SpawnTerrain, "山鹰生成地点 = 山地");
			Check.AssertEqual(0.08f, tables.Units.GetUnitConfig("eagle").SpawnChance, "山鹰生成概率 = 8%");
			Check.AssertEqual(0.05f, tables.Units.GetUnitConfig("ibex").SpawnChance, "巨角山羊生成概率 = 5%");
			Check.AssertEqual("water", tables.Units.GetUnitConfig("crocodile").SpawnTerrain, "巨鳄生成地点 = 水域");
			Check.AssertEqual(0.06f, tables.Units.GetUnitConfig("crocodile").SpawnChance, "巨鳄生成概率 = 6%");
			Check.Assert(tables.Units.GetUnitConfig("ibex").DropReward.Count > 0, "巨角山羊有掉落表（`E19`：掉落进池）");
			Check.AssertEqual(0, tables.Units.GetUnitConfig("wolf").Movement, "敌方单位无移动力（设计稿）");

			// ① 概率写成百分数（15 而非 0.15）→ error
			Check.Assert(HasUnitsError(SetUnitField("wolf", "SpawnChance", 15), "SpawnChance"),
				"SpawnChance 越界（写成 15）应判 error");

			// ② 敌方单位没有生成地形 → error
			Check.Assert(HasUnitsError(SetUnitField("wolf", "SpawnTerrain", ""), "SpawnTerrain"),
				"敌方单位缺 SpawnTerrain 应判 error（否则永不刷新）");

			// ③ 生成地形不存在 → error
			Check.Assert(HasUnitsError(SetUnitField("wolf", "SpawnTerrain", "lava"), "不存在的地形"),
				"SpawnTerrain 引用不存在的地形应判 error");

			// ④ 玩家单位误填生成字段（漏填 IsHostile）→ error
			Check.Assert(HasUnitsError(SetPlayerSpawnFields(), "IsHostile"),
				"玩家单位填了生成字段应判 error（刷新只取敌方行）");
		}

		private static void CompositionWiresSpawner()
		{
			Check.Assert(new CoreDependencies().EnableEnemySpawn, "组合根默认开启敌方刷新（生产行为，不是测试专属）");

			Harness on = NewHarness(enableEnemySpawn: true);
			try
			{
				Check.AssertEqual(1, on.Map.PostProcessorCount, "启用后 `MapAppService` 挂上 1 个生成后处理器（`EnemySpawner`）");
			}
			finally { Cleanup(on.Dir); }

			Harness off = NewHarness(enableEnemySpawn: false);
			try
			{
				Check.AssertEqual(0, off.Map.PostProcessorCount, "关闭后没有生成后处理器（用例沙盘不被随机敌人污染）");
				off.Map.GenerateMap(20260916, SmallMap, SmallMap, MapId);
				Check.AssertEqual(0, HostilesOf(off.MapOf(MapId)).Count, "关闭刷新时地图上没有敌方单位");
			}
			finally { Cleanup(off.Dir); }
		}


		// ────────────────────────── 配置表注入小工具 ──────────────────────────

		/// <summary>取真实 Units 表 → 给某个单位改一个字段 → 返回新的 JSON 文本。</summary>
		private static string SetUnitField(string unitId, string field, Newtonsoft.Json.Linq.JToken value)
		{
			Newtonsoft.Json.Linq.JObject table = RealUnitsTable();
			table["Units"][unitId][field] = value;
			return table.ToString();
		}

		/// <summary>把玩家单位误填成"有生成字段但没有 IsHostile"（漏填 IsHostile 的典型形态）。</summary>
		private static string SetPlayerSpawnFields()
		{
			Newtonsoft.Json.Linq.JObject table = RealUnitsTable();
			table["Units"]["worker"]["SpawnTerrain"] = "plain";
			table["Units"]["worker"]["SpawnChance"] = 0.2;
			return table.ToString();
		}

		private static Newtonsoft.Json.Linq.JObject RealUnitsTable()
			=> Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));

		/// <summary>用改过的 Units 表装配一次（不快速失败），看报告里有没有指定片段的 error。</summary>
		private static bool HasUnitsError(string unitsJson, string fragment)
			=> ConfigFixtures
				.BuildCore(ConfigFixtures.RealConfigSourceWith("Units", unitsJson), failOnConfigErrors: false)
				.ConfigReport.ForTable("Units")
				.Any(issue => issue.Level == SciencePotato.Scripts.Core.Config.ConfigIssueLevel.Error
					&& issue.Message.Contains(fragment));


		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public string MapId;
			public GameClock Clock;
			public GameTimeService Time;
			public MapAppService Map;
			public MapSession SessionMaps;
			public FogAppService Fog;
			public ResourcesAppService Resources;
			public ConstructionAppService Construction;
			public UnitsAppService Units;
			public UnitMovementService Movement;
			public EnemySpawner Spawner;
			public ConfigTables Tables;

			public Map MapOf(string mapId) => SessionMaps.Get(mapId);

			/// <summary>生成指定尺寸的地图（真实地形分布；敌方刷新按夹具开关）。</summary>
			public void GenerateMap(int seed, int width, int height)
				=> Map.GenerateMap(seed, width, height, MapId);

			/// <summary>生成沙盘：全图铺平原（用例的裸坐标不应被 Voronoi 水地形挡住）。</summary>
			public void GeneratePlainMap(int seed, int size)
			{
				Map.GenerateMap(seed, size, size, MapId);

				ITerrainData plain = Tables.Terrains.GetById("plain");
				foreach (MapCell cell in Map.GetAllCells(MapId).ToList())
					Map.SetTerrain(MapId, cell.Position, plain);
			}

			/// <summary>放置一个敌方单位：走**生产路径**（`EnemySpawner.Spawn` → `Map.PlaceOccupant`）。</summary>
			public Unit PlaceEnemy(string unitId, HexCubePosition position)
			{
				IUnitConfig config = Tables.Units.GetUnitConfig(unitId);
				Check.Assert(config != null && config.IsHostile, $"{unitId} 应是敌方单位配置");

				Check.Assert(Spawner.Spawn(MapOf(MapId), config, position), $"{unitId} 应能落在 {position}");

				return (Unit)Map.FindOccupantByUId(MapId, Map.GetOccupantInfo(MapId, position).Value.UId);
			}

			/// <summary>生成一个已就绪的工人（平原 MP 10）。</summary>
			public Unit SpawnWorker(HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Food", 300f, MapId, OwnerId);
				Units.CreateUnit(MapId, "worker", position, OwnerId);
				Clock.AdvanceDays(3); // 快配置：训练 3 日

				string uid = Map.GetOccupantInfo(MapId, position).Value.UId;
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}

			/// <summary>把某地图上的敌方单位按 (q,r) 拼成稳定字符串（确定性断言的比较键）。</summary>
			public string Layout(int seed, string mapId)
			{
				Map.GenerateMap(seed, 16, 16, mapId);

				return string.Join("|", HostilesOf(SessionMaps.Get(mapId))
					.Select(x => $"{x.Position.q},{x.Position.r}:{x.Enemy.GetInfo().Id}")
					.OrderBy(text => text, StringComparer.Ordinal));
			}
		}

		/// <summary>地图上的全部敌方单位（按 (q,r) 排序，保证枚举稳定）。</summary>
		private static List<(HexCubePosition Position, Unit Enemy)> HostilesOf(Map map)
		{
			var result = new List<(HexCubePosition, Unit)>();
			foreach (MapCell cell in map.GetAllCells().OrderBy(c => c.Position.q).ThenBy(c => c.Position.r))
				if (cell.Occupant is Unit unit && unit.IsHostile)
					result.Add((cell.Position, unit));

			return result;
		}


		/// <summary>装配一套可跑的应用服务（与其余检查同一套路；地图仓库用内存替身）。</summary>
		private static Harness NewHarness(bool enableEnemySpawn, int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp38-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			Shorten(source);

			var mapRepository = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildCore(source, mapRepository: mapRepository, enableEnemySpawn: enableEnemySpawn);
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue; // `D28`

			var time = new GameTimeService(core.Session.Clock, new TaskRepository(Path.Combine(dir, "tasks_")));
			var bus = new DomainEventBus();
			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_")),
				core.Tables.Resources,
				time,
				new ModifierRepository(Path.Combine(dir, "mod_")));
			var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(dir, "mod_")));
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees),
				core.Tables.TechTrees,
				resources,
				modifier,
				time,
				bus);
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_")));
			var construction = new ConstructionAppService(
				core.Map, resources, tech, new BuildingFactory(core.Tables.Buildings),
				core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(
				core.Map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus);
			var movement = new UnitMovementService(core.Map, fog, core.Tables.Units, time);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				MapId = MapId,
				Clock = core.Session.Clock,
				Time = time,
				Map = core.Map,
				SessionMaps = core.Session.Maps,
				Fog = fog,
				Resources = resources,
				Construction = construction,
				Units = units,
				Movement = movement,
				Spawner = new EnemySpawner(core.Tables.Units, new UnitFactory(core.Tables.Units)),
				Tables = core.Tables,
			};
		}

		/// <summary>建筑/升级 2 日、玩家单位 3 日（其余字段与真实表一致 —— 敌方行的地形/概率/掉落保持原样）。</summary>
		private static void Shorten(InMemoryConfigSource source)
		{
			var buildings = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)buildings["Buildings"]).Properties())
			{
				entry.Value["Duration"] = 2;
				entry.Value["UpgradeDuration"] = 2;
			}

			var units = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)units["Units"]).Properties())
			{
				// 敌方单位不参与训练（Duration 无意义）：保持 0，避免"训练 3 日才能落位"的噪声
				if (entry.Value["IsHostile"]?.ToObject<bool>() == true) continue;

				entry.Value["Duration"] = 3;
			}

			source.Inject("Buildings", buildings.ToString());
			source.Inject("Units", units.ToString());
		}

		private static void Cleanup(string dir)
		{
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch (IOException)
			{
				// 临时目录清理失败不影响验收结果
			}
		}


	}
}
