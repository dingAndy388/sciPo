using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Domain;
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
	/// （v0.3.23 / WP-3.7 / `E20`）**击杀掉落**的验收检查 —— 补上 M0-3 ③ 的最后一段
	/// （"HP 归零 → 单位移除 → **掉落进池**"）：`CombatChecks.WinnerTakesTheCell` 锁"一次击杀会掉"，
	/// 本组锁**口径与边界**：
	/// <list type="number">
	/// <item>**归属** = 击杀者所有者的资源池（不是阵亡方、不是旁观者）；</item>
	/// <item>**只给「玩家击杀敌方」**（`IsHostile` 配置派生）：玩家单位阵亡、敌方互殴、凶手 uid 缺失都不掉；</item>
	/// <item>**不破池上限**且**实际入池量可查**（`ILootSink.GrantLoot` 返回净值 —— 满仓时是 0 而不是静默吞掉）；</item>
	/// <item>**边界**：没接资源池（`ILootSink` = null）不掉不崩、掉落表为空不掉，且每种原因都有独立计数（`D25`）。</item>
	/// </list>
	/// <para>夹具 = 真实配置（单位时长缩短到 3 日、建筑 2 日）+ 平原沙盘 + 手动摆敌我单位；
	/// 需要"改表"的用例（野猪 HP、野狼空表）走 <see cref="ConfigFixtures.RealConfigSourceWith"/>，
	/// 与 `CombatChecks` 同一套路。</para>
	/// </summary>
	internal static class LootChecks
	{
		private const string MapId = "loot-map";

		private const int SmallMap = 10;

		public static void RunAll()
		{
			Check.Run("WP-3.7 掉落归属：进击杀者所有者的资源池（`E20`）", DropGoesToKillerOwner);
			Check.Run("WP-3.7 掉落口径：只给「玩家击杀敌方」（玩家阵亡/敌方互殴/无凶手均不掉）", OnlyPlayerKillsDrop);
			Check.Run("WP-3.7 掉落不破池上限，且实际入池量可查（`ILootSink` / `D25`）", PoolLimitIsRespectedAndVisible);
			Check.Run("WP-3.7 边界：没接资源池不崩、掉落表为空不掉（分类计数可查）", UnwiredSinkAndEmptyTablesAreCounted);
		}

		// ────────────────────────── 用例：归属与口径 ──────────────────────────

		/// <summary>
		/// `E20` 的归属口径：掉落算**击杀者**的战功 —— 玩家 2 的民兵打死野狼，30 Food 进玩家 2 的池子，
		/// 同图的玩家 1 一分不得（防止"谁先建池谁收钱"这类实现漂移）。
		/// </summary>
		private static void DropGoesToKillerOwner()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260918, SmallMap);

				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);
				Unit bystander = h.SpawnFor(1, "swordsman", new HexCubePosition(1, 1)); // 旁观者（玩家 1）
				Unit killer = h.SpawnFor(2, "swordsman", new HexCubePosition(2, 4));   // 击杀者（玩家 2）

				float bystanderBefore = h.PoolFor(1).GetValue("Food");
				float killerBefore = h.PoolFor(2).GetValue("Food");

				Check.Assert(h.Units.ExcuteAction(MapId, killer.GetInfo().UId, enemyCell, wolf.GetInfo().UId, "CanAttack"),
					"相邻的民兵应接受攻击指令");
				h.Clock.AdvanceDays(8); // 野狼 HP 60 / 民兵 8 每日 → 第 8 日击杀

				Check.Assert(h.Map.FindOccupantByUId(MapId, wolf.GetInfo().UId) == null, "野狼应按日掉血直到阵亡并被移除");
				Check.AssertEqual(1, h.Units.Loot.Drops, "一次击杀 = 一次掉落");
				Check.AssertEqual(30f, h.PoolFor(2).GetValue("Food") - killerBefore, "掉落进**击杀者**（玩家 2）的资源池");
				Check.AssertEqual(bystanderBefore, h.PoolFor(1).GetValue("Food"), "旁观者（玩家 1）的池子不该有变化");
				Check.AssertEqual(2, h.Units.Loot.LastLootOwnerId, "受益者 = 击杀者所有者");
				Check.AssertEqual("wolf", h.Units.Loot.LastDroppedUnitId, "掉的是被杀的那个敌种");
				Check.AssertEqual(0, h.Units.Loot.PlayerDeathsIgnored, "击杀敌方不该被算成「玩家阵亡」");
				Check.AssertEqual(0, h.Units.Loot.UnwiredDeathsIgnored, "缺省装配就是接了资源服务的（不必额外接线）");
				Check.Assert(bystander.HP > 0f, "旁观者全程没参战（准备检查：没有被误伤）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>
		/// `E20` 的口径边界：**只有玩家击杀敌方**才掉落 —— 三条负向对照（玩家单位阵亡、敌方互殴、
		/// 无名凶手）都必须"不掉但留痕"，最后用一次真击杀证明前三条负向对照没有把正常掉落也一起关掉。
		/// <para>三条负向事件直接推到总线上：消费端（<see cref="UnitLootService"/>）是纯观察者
		/// （不改地图、不碰战斗），因此可以单独喂事件 —— 这正是"掉落做成事件消费端"的可测性收益。</para>
		/// </summary>
		private static void OnlyPlayerKillsDrop()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260918, SmallMap);

				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);
				Unit eagle = h.PlaceEnemy("eagle", new HexCubePosition(7, 7));
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));

				var died = new List<UnitDiedEvent>();
				h.Bus.Subscribe<UnitDiedEvent>(died.Add);

				// ① 玩家单位阵亡 → 不掉落（design：「不掉落单位或建筑」）
				h.Bus.Publish(new UnitDiedEvent(MapId, 1, swordsman.GetInfo().UId, "swordsman", swordsman.Position, wolf.GetInfo().UId));
				Check.AssertEqual(0, h.Units.Loot.Drops, "玩家单位阵亡不该掉落");
				Check.AssertEqual(1, h.Units.Loot.PlayerDeathsIgnored, "应归入「玩家阵亡」这一类");

				// ② 敌方被判敌方杀手击杀（未来 AI 互殴/环境）：凶手不是玩家单位 → 不掉
				h.Bus.Publish(new UnitDiedEvent(MapId, EnemySpawner.HostileOwnerId, wolf.GetInfo().UId, "wolf", enemyCell, eagle.GetInfo().UId));
				Check.AssertEqual(0, h.Units.Loot.Drops, "敌方互殴不该给玩家掉落");
				Check.AssertEqual(1, h.Units.Loot.UnattributedDeathsIgnored, "应归入「找不出玩家凶手」这一类");

				// ③ 无名凶手（`KillerUId` 为空：环境伤害/未知来源）→ 同样不掉
				h.Bus.Publish(new UnitDiedEvent(MapId, EnemySpawner.HostileOwnerId, "ghost", "wolf", enemyCell, null));
				Check.AssertEqual(2, h.Units.Loot.UnattributedDeathsIgnored, "uid 缺失同样归「找不出玩家凶手」");

				// ④ 真打一次：玩家击杀敌方 → 掉（负向对照没有把正常掉落误伤掉）
				float goldBefore = h.Pool().GetValue("Food");
				Check.Assert(h.Units.ExcuteAction(MapId, swordsman.GetInfo().UId, enemyCell, wolf.GetInfo().UId, "CanAttack"),
					"相邻的民兵应接受攻击指令");
				h.Clock.AdvanceDays(8);

				Check.AssertEqual(1, h.Units.Loot.Drops, "只有这一次真击杀产生了掉落");
				Check.AssertEqual(goldBefore + 30f, h.Pool().GetValue("Food"), "野狼 30 Food 入池");
				Check.AssertEqual(4, h.Units.Loot.HandledDeaths, "四次阵亡事件都进过消费端（1 掉 + 3 免）");
				Check.AssertEqual(4, died.Count, "阵亡事件仍照常推送给所有订阅者（掉落没有吃掉事件）");
				Check.AssertEqual(3, h.Units.Loot.PlayerDeathsIgnored + h.Units.Loot.UnattributedDeathsIgnored
					+ h.Units.Loot.EmptyTablesIgnored + h.Units.Loot.UnwiredDeathsIgnored,
					"「没掉成」的分类计数之和 = 3（1 玩家阵亡 + 2 找不出凶手），与 HandledDeaths 4 = Drops 1 + 3 对账");
				Check.AssertEqual(70f, eagle.HP, "准备检查：猛禽全程没被波及（它离得很远）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>
		/// **上限口径**（`ILootSink` 契约）：掉落不破仓储上限 —— `BasicMinerals` 的 `BaseLimit` 只有 500，
		/// 满仓时那 20 BasicMinerals 不会到账；因此 `GrantLoot` 返回的是**实际入池量**而不是传进去的表
		/// （`D25`：被上限吃掉必须看得见，不能静默丢弃）。
		/// <para>两段：① 直接调契约实现（数值精确）；② 走玩法路径打一只野猪（`DropReward` = 80 Food + 20 BasicMinerals）
		/// 看 `LastGranted` 与池子是否一致。野猪 HP 150 / ATK 10 会反杀民兵，故本用例把它改成
		/// 「HP 10 / 不还手」—— 只为让"掉落数字"成为唯一的变量。</para>
		/// </summary>
		private static void PoolLimitIsRespectedAndVisible()
		{
			// ① 契约本身：把 BasicMinerals 顶到**它的**上限（上限值从配置读，不写死 —— v0.6.3 / WP-7.1 起
			//    `Resources.json` 的 `BaseLimit` 就是设计值：Food 2000 / BasicMinerals 1500 / Idea 10000）
			Harness direct = NewHarness();
			try
			{
				direct.GeneratePlainMap(20260918, SmallMap);
				direct.SpawnFor(1, "swordsman", new HexCubePosition(1, 1));

				string capped = "BasicMinerals";
				float cap = direct.Pool().GetLimit(capped);
				direct.Resources.AddResource(capped, cap, MapId, 1);
				Check.AssertEqual(cap, direct.Pool().GetValue(capped), $"准备：{capped} 已被顶到上限 {cap}（`Resources.json` 的 `BaseLimit`）");

				float foodBefore = direct.Pool().GetValue("Food");
				Dictionary<string, float> granted = direct.Resources.GrantLoot(MapId, 1,
					new Dictionary<string, float> { { "Food", 80f }, { capped, 20f } });

				Check.AssertEqual(80f, granted["Food"], "Food 未满 → 全额入池");
				Check.AssertEqual(0f, granted[capped], $"{capped} 已满 → 实际入池 0（契约把它报出来，而不是静默丢弃）");
				Check.AssertEqual(cap, direct.Pool().GetValue(capped), "上限不被突破");
				Check.AssertEqual(foodBefore + 80f, direct.Pool().GetValue("Food"), "80 Food 全额到账");
			}
			finally { Cleanup(direct.Dir); }

			// ② 玩法路径：野猪（80 Food + 20 BasicMinerals）被击杀，`LastGranted` = 池子前值/后值之差
			//    （把 BasicMinerals 先顶满，才能验"满仓的掉落不到账"）
			Newtonsoft.Json.Linq.JObject table = RealUnitsTable();
			table["Units"]["boar"]["HP"] = 10;          // 150 → 10：两天内击杀
			table["Units"]["boar"]["AttackDamage"] = 0; // 不还手（避开"民兵先死"这个无关变量）
			Harness h = NewHarness(source: ConfigFixtures.RealConfigSourceWith("Units", table.ToString()));
			try
			{
				h.GeneratePlainMap(20260918, SmallMap);

				var enemyCell = new HexCubePosition(3, 4);
				Unit boar = h.PlaceEnemy("boar", enemyCell);
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));

				float mineralCap = h.Pool().GetLimit("BasicMinerals");
				h.Resources.AddResource("BasicMinerals", mineralCap, MapId, 1);

				float foodBefore = h.Pool().GetValue("Food");
				float mineralBefore = h.Pool().GetValue("BasicMinerals");

				Check.Assert(h.Units.ExcuteAction(MapId, swordsman.GetInfo().UId, enemyCell, boar.GetInfo().UId, "CanAttack"),
					"相邻的民兵应接受攻击指令");
				h.Clock.AdvanceDays(3);

				Check.AssertEqual(1, h.Units.Loot.Drops, "野猪被击杀 → 掉落发生了（被上限吃掉的只是数量）");
				Check.AssertEqual(80f, h.Units.Loot.LastGranted["Food"], "击杀者应拿到 80 Food");
				Check.AssertEqual(0f, h.Units.Loot.LastGranted["BasicMinerals"], "满仓的 BasicMinerals 实际入池 0");
				Check.AssertEqual(foodBefore + 80f, h.Pool().GetValue("Food"), "池子和 `LastGranted` 对得上（Food）");
				Check.AssertEqual(mineralBefore, h.Pool().GetValue("BasicMinerals"), "池子和 `LastGranted` 对得上（BasicMinerals）");
				Check.AssertEqual(80f, h.Units.Loot.TotalGranted, "累计实际入池量 = 80（只算真实到账的部分）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>
		/// **边界**：阵亡流程不能被掉落拖垮 —— ① 装配时没接资源池（`ILootSink` = null）时"不掉但不抛"，
		/// 并留下 <see cref="UnitLootService.UnwiredDeathsIgnored"/>；② 该敌种掉落表为空（配置合法）时
		/// 同样不掉，且 <see cref="UnitLootService.EmptyTablesIgnored"/> 证明表**被真的读过**
		/// （否则"没掉落"分不清是"没接上"还是"不该掉"）。
		/// </summary>
		private static void UnwiredSinkAndEmptyTablesAreCounted()
		{
			// ① 没接资源池：直接建一个裸掉的消费端（`loot: null`），喂一条真实的击杀事件
			Harness bare = NewHarness();
			try
			{
				bare.GeneratePlainMap(20260918, SmallMap);
				Unit wolf = bare.PlaceEnemy("wolf", new HexCubePosition(3, 4));
				Unit swordsman = bare.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));
				float goldBefore = bare.Pool().GetValue("Food");

				var unwired = new UnitLootService(bare.Map, bare.Tables.Units, loot: null, events: null);
				unwired.OnUnitDied(new UnitDiedEvent(MapId, EnemySpawner.HostileOwnerId,
					wolf.GetInfo().UId, "wolf", wolf.Position, swordsman.GetInfo().UId));

				Check.AssertEqual(1, unwired.UnwiredDeathsIgnored, "没接池子 → 记入「没接线」这一类（不是静默无操作）");
				Check.AssertEqual(0, unwired.Drops, "没接池子不可能掉落");
				Check.AssertEqual(1, unwired.HandledDeaths, "事件仍被处理过（只是掉不出去）");
				Check.AssertEqual(goldBefore, bare.Pool().GetValue("Food"), "池子不受影响");
			}
			finally { Cleanup(bare.Dir); }

			// ② 掉落表为空：野狼的 `DropReward` 清空（校验器不报错：空表是合法的"不产资源"）
			Newtonsoft.Json.Linq.JObject table = RealUnitsTable();
			table["Units"]["wolf"]["DropReward"] = new Newtonsoft.Json.Linq.JObject();
			Harness h = NewHarness(source: ConfigFixtures.RealConfigSourceWith("Units", table.ToString()));
			try
			{
				h.GeneratePlainMap(20260918, SmallMap);

				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));
				float goldBefore = h.Pool().GetValue("Food");

				Check.Assert(h.Units.ExcuteAction(MapId, swordsman.GetInfo().UId, enemyCell, wolf.GetInfo().UId, "CanAttack"),
					"相邻的民兵应接受攻击指令");
				h.Clock.AdvanceDays(8);

				Check.AssertEqual(1, h.Units.Combat.Kills, "战斗照常打完（掉落与战斗解耦）");
				Check.AssertEqual(0, h.Units.Loot.Drops, "空表 → 不掉落");
				Check.AssertEqual(1, h.Units.Loot.EmptyTablesIgnored, "应记入「表为空」这一类（说明表被真的读过了）");
				Check.AssertEqual(goldBefore, h.Pool().GetValue("Food"), "池子不受影响");
			}
			finally { Cleanup(h.Dir); }
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public string MapId = LootChecks.MapId;
			public GameClock Clock;
			public MapAppService Map;
			public MapSession SessionMaps;
			public ResourcesAppService Resources;
			public UnitsAppService Units;
			public EnemySpawner Spawner;
			public DomainEventBus Bus;
			public ConfigTables Tables;

			public Map MapOf(string mapId) => SessionMaps.Get(mapId);

			/// <summary>玩家 1 的资源池（多数用例的默认观察对象）。</summary>
			public ResourcesPool Pool() => PoolFor(OwnerId);

			/// <summary>指定玩家的资源池（按存档分区读取 —— 与游戏里的读取路径一致）。</summary>
			public ResourcesPool PoolFor(int ownerId) => Resources.GetOrCreatePool(MapId, ownerId);

			/// <summary>生成沙盘：全图铺平原（裸坐标不应被 Voronoi 水地形挡住）。</summary>
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

			/// <summary>走训练路径放一个玩家单位（先在该格补 1 人：训练完成要扣人口；顺手把资源池建起来）。</summary>
			public Unit SpawnFor(int ownerId, string unitId, HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Food", 500f, MapId, ownerId);
				Resources.AddResource("BasicMinerals", 500f, MapId, ownerId);

				Units.CreateUnit(MapId, unitId, position, ownerId);
				Clock.AdvanceDays(3); // 快配置：玩家单位训练 3 日 → 就绪

				return (Unit)Map.FindOccupantByUId(MapId, Map.GetOccupantInfo(MapId, position).Value.UId);
			}
		}

		/// <summary>装配一套可跑的应用服务（与其余检查同一套路；地图仓库用内存替身）。</summary>
		/// <param name="source">可选：自定义配置源（"改表"的用例传，缺省 = 真实表 + 时长缩短）。</param>
		private static Harness NewHarness(int ownerId = 1, InMemoryConfigSource source = null)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp37-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			InMemoryConfigSource configSource = source ?? ConfigFixtures.RealConfigSource();
			Shorten(configSource);

			var mapRepository = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildCore(configSource, mapRepository: mapRepository);
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

			// 注意：**不传** `lootSink` —— 掉落口缺省取 `resources`（`ResourcesAppService : ILootSink`），
			// 这正是"装配处不必额外接线"的验收点（`D69`）。
			var units = new UnitsAppService(
				core.Map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus, modifier);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = core.Session.Clock,
				Map = core.Map,
				SessionMaps = core.Session.Maps,
				Resources = resources,
				Units = units,
				Spawner = new EnemySpawner(core.Tables.Units, new UnitFactory(core.Tables.Units)),
				Bus = bus,
				Tables = core.Tables,
			};
		}

		/// <summary>建筑 2 日、玩家单位 3 日（其余字段与真实表一致 —— 敌方行的 HP/伤害/掉落保持原样）。</summary>
		private static void Shorten(InMemoryConfigSource source)
		{
			// 以**配置源里的当前内容**为基础（用例可能已经覆写过某张表）：否则会把改过的表覆盖回真实表
			var buildings = Newtonsoft.Json.Linq.JObject.Parse(
				source.LoadText("Buildings") ?? File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)buildings["Buildings"]).Properties())
			{
				entry.Value["Duration"] = 2;
				entry.Value["UpgradeDuration"] = 2;
			}

			var units = Newtonsoft.Json.Linq.JObject.Parse(
				source.LoadText("Units") ?? File.ReadAllText(ConfigFixtures.TablePath("Units")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)units["Units"]).Properties())
			{
				// 敌方单位不参与训练（Duration 无意义）：保持 0，避免"训练 3 日才能落位"的噪声
				if (entry.Value["IsHostile"]?.ToObject<bool>() == true) continue;

				entry.Value["Duration"] = 3;
			}

			source.Inject("Buildings", buildings.ToString());
			source.Inject("Units", units.ToString());
		}

		/// <summary>真实 Units 表 → 改字段 → 返回新的 JSON 文本（与 `CombatChecks` 同一工具）。</summary>
		private static Newtonsoft.Json.Linq.JObject RealUnitsTable()
			=> Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));

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
