using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.6.0 / WP-5.1 + WP-4.18）**装配通电**与**会话/玩家表**的验收检查。
	/// <para>为什么这组用例是关键：改造前 `CoreBootstrap` 只装 Map + Time，Construction / Units / TechTrees /
	/// Events / Resources / Fog / 月结 / 世界存档**只存在于测试夹具里** —— 也就是说"这些能力在生产路径上不存在"，
	/// 而所有既有用例都是自己手搭一套服务，永远发现不了这件事。</para>
	/// <para>本组用例与生产**共用同一个 <see cref="CoreBootstrap"/>**：只要组合根少装一个服务、
	/// 或者某个仓储忘了接存档单元，这里就会红。</para>
	/// </summary>
	internal static class WiringChecks
	{
		private const string MapId = "wire-map";

		public static void RunAll()
		{
			Check.Run("WP-5.1 装配：CoreBootstrap 产出全部应用服务", AllServicesAssembled);
			Check.Run("WP-5.1 装配：无存档单元时不挂任务仓储（无持久化语义不变）", NoStoreKeepsMemoryOnly);
			Check.Run("WP-5.1 存档接线：任务与资源快照落在统一存档的分区里", WritesIntoSaveStoreSections);
			Check.Run("WP-5.1 存档接线：存档点保存后能读回世界与任务", WorldSaveRoundTrip);
			Check.Run("WP-4.18 玩家表：缺省 = 单人类玩家（owner=1）", DefaultPlayerTable);
			Check.Run("WP-4.18 玩家表：多势力按 ownerId 升序、重复加入被拒", PlayerTableOrdering);
			Check.Run("WP-4.18 编排器：为每个势力启动资源池与月结", OrchestratorStartsEveryPlayer);
			Check.Run("WP-4.18 编排器：事件引擎只对人类玩家启动", EventsOnlyForHuman);
			Check.Run("WP-4.18 多势力隔离：AI 的资源不落进玩家的池子", PerPlayerIsolation);
			Check.Run("WP-4.18 编排器：重复启动幂等（不重复登记任务）", StartMapIsIdempotent);
		}

		private static void AllServicesAssembled()
		{
			Harness harness = NewHarness();
			CoreServices core = harness.Core;

			Check.Assert(core.Map != null, "MapAppService");
			Check.Assert(core.Resources != null, "ResourcesAppService");
			Check.Assert(core.Modifiers != null, "ModifierAppService");
			Check.Assert(core.Tech != null, "TechTreesAppService");
			Check.Assert(core.Construction != null, "ConstructionAppService");
			Check.Assert(core.Units != null, "UnitsAppService");
			Check.Assert(core.Events != null, "EventAppService");
			Check.Assert(core.Fog != null, "FogAppService");
			Check.Assert(core.Settlement != null, "MonthlySettlementService");
			Check.Assert(core.WorldSave != null, "WorldSaveService");
			Check.Assert(core.Orchestrator != null, "SessionOrchestrator");
			Check.Assert(core.Tasks != null, "注入存档单元时应挂上 TaskRepository");

			Check.AssertEqual(0, core.ConfigReport.ErrorCount, "真实配置的校验 error 数");
			Check.Assert(core.Map.PostProcessorCount > 0, "生产装配应挂上敌方刷新处理器（EnableEnemySpawn 默认开）");
		}

		/// <summary>
		/// 没有存档单元（无头工具 / 既有夹具）时必须退回 v0.5 的语义：**不做持久化、不写任何文件**。
		/// <para>为什么这是一条要守住的契约：`CoreDependencies.SaveRoot` 的缺省值是 `user://save/`，
		/// 若在无存档单元时仍建仓储，测试就会往 "user://..." 这种相对路径写盘（在 Windows 上会得到
		/// 一个名叫 `user:` 的目录），污染工作区并让"无持久化"变成谎话。</para>
		/// </summary>
		private static void NoStoreKeepsMemoryOnly()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			Check.Assert(core.SaveStore == null, "夹具不应注入存档单元");
			Check.Assert(core.Tasks == null, "无存档单元 → 不应挂任务仓储");
			Check.AssertEqual(0, core.Time.SubscriberCount, "无存档单元 → 启动期不应注册任何周期任务");
			Check.Assert(core.Orchestrator != null, "编排器与存档无关，仍然可用");
		}

		private static void WritesIntoSaveStoreSections()
		{
			Harness harness = NewHarness(enableEnemySpawn: false);
			CoreServices core = harness.Core;

			core.Map.GenerateMap(20260914, 6, 6, MapId);

			IReadOnlyList<PlayerStartReport> started = core.Orchestrator.StartMap(MapId);
			Check.AssertEqual(1, started.Count, "单一玩家应启动 1 份子系统");
			Check.Assert(started[0].ResourcesPoolStarted, "资源池应启动");
			Check.Assert(started[0].SettlementStarted, "月结应启动");
			Check.Assert(started[0].EventsStarted, "人类玩家的事件引擎应启动");

			core.Session.Clock.AdvanceDays(TimeConstants.DaysPerMonth);

			Check.Assert(harness.Store.HasSection("tasks:" + MapId), "任务快照应写进 `tasks:{mapId}` 分区");
			Check.Assert(harness.Store.HasSection("resources:" + MapId + "_1"), "资源池应写进 `resources:{mapId}_{ownerId}` 分区");

			string tasks = harness.Store.ReadSection("tasks:" + MapId) ?? string.Empty;
			Check.Assert(tasks.Contains("MonthlySettlement"), "任务分区里应含月结任务（否则「月结没了」这类缺陷查不出来）");
			Check.Assert(core.Settlement.SettledCount >= 1, "推进 30 日后应完成至少 1 次月结");
		}

		/// <summary>
		/// 存档点 → 读档点往返：证明装配后的 <see cref="WorldSaveService"/> 是**可用的**（而不只是非 null）。
		/// <para>它同时覆盖"任务仓储接线"：没有任务仓储时 <c>RestoreTasks</c> 会恢复 0 条，
		/// 读档后所有周期任务（月结/资源成长）就永远消失 —— 这类静默降级只有往返一次才看得见。</para>
		/// </summary>
		private static void WorldSaveRoundTrip()
		{
			Harness harness = NewHarness(enableEnemySpawn: false);
			CoreServices core = harness.Core;

			core.Map.GenerateMap(20260914, 6, 6, MapId);
			core.Orchestrator.StartMap(MapId);

			int registeredBefore = core.Time.SubscriberCount;
			Check.Assert(registeredBefore > 0, "启动后应有周期任务（月结 + 资源成长 + 事件）");

			core.Session.Clock.AdvanceDays(TimeConstants.DaysPerMonth * 2);
			double dayBeforeSave = core.Session.Clock.CurrentDay;

			core.WorldSave.SaveWorld(MapId);
			Check.Assert(harness.Store.Day > 0d, "存档点应把游戏日写进文件头");

			bool loaded = core.WorldSave.LoadWorld(MapId, 1);
			Check.Assert(loaded, "读档应成功");
			Check.Assert(core.WorldSave.LastRestoredTaskCount > 0, "读档后应恢复周期任务（任务仓储已接线）");
			Check.AssertEqual(dayBeforeSave, core.Session.Clock.CurrentDay, "读档后游戏日应回到存档点");

			// 读档会清空时间总线并按快照重建：重建后应仍有任务（否则结算/成长永久停摆）
			Check.Assert(core.Time.SubscriberCount >= 1, "读档后时间总线应重新挂上月结/资源/事件任务");
		}

		private static void DefaultPlayerTable()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			Check.AssertEqual(1, core.Session.Players.Count, "缺省玩家表长度");
			Check.AssertEqual(1, core.Session.HumanOwnerId, "缺省人类 ownerId");
			Check.Assert(core.Session.IsHuman(1), "owner=1 应为人类玩家");
			Check.AssertEqual(1, core.Session.OwnerIds.Count, "ownerId 数量");
		}

		private static void PlayerTableOrdering()
		{
			Harness harness = NewHarness(players: new[]
			{
				PlayerContext.Ai(3),
				PlayerContext.Human(1),
				PlayerContext.Ai(2),
				PlayerContext.Ai(2), // 重复：应被合并（不覆盖已有身份）
			});

			Check.AssertEqual(3, harness.Core.Session.Players.Count, "重复 ownerId 应被合并");
			Check.AssertEqual("1,2,3", string.Join(",", harness.Core.Session.OwnerIds), "ownerId 升序");

			Check.Assert(harness.Core.Session.IsHuman(1), "owner=1 人类");
			Check.Assert(!harness.Core.Session.IsHuman(2), "owner=2 不应是人类");
			Check.AssertEqual(1, harness.Core.Session.HumanOwnerId, "人类 ownerId 应为 1");
			Check.Assert(!harness.Core.Session.AddPlayer(PlayerContext.Ai(2)), "重复加入应被拒");
			Check.Assert(harness.Core.Session.AddPlayer(PlayerContext.Ai(4)), "新 ownerId 应可加入");
			Check.AssertEqual(4, harness.Core.Session.Players.Count, "加入后势力数");
		}

		private static void OrchestratorStartsEveryPlayer()
		{
			Harness harness = NewHarness(players: new[] { PlayerContext.Human(1), PlayerContext.Ai(2, "AI 甲") }, enableEnemySpawn: false);
			CoreServices core = harness.Core;

			core.Map.GenerateMap(20260917, 6, 6, MapId);
			IReadOnlyList<PlayerStartReport> started = core.Orchestrator.StartMap(MapId);

			Check.AssertEqual(2, started.Count, "两个势力都应被启动");
			Check.AssertEqual(2, core.Orchestrator.LastStartedPlayerCount, "编排器记录的启动势力数");
			Check.Assert(core.Orchestrator.IsStarted(MapId), "该地图应被标记为已启动");

			// 每个势力都要有自己的资源池（各自独立实例，不共享）
			Check.Assert(core.Resources.GetOrCreatePool(MapId, 1) != null, "玩家资源池");
			Check.Assert(core.Resources.GetOrCreatePool(MapId, 2) != null, "AI 资源池");

			// 月结要真的按势力跑起来（AI 一样要付维护费、一样会挨饿）
			core.Session.Clock.AdvanceDays(TimeConstants.DaysPerMonth);
			Check.Assert(core.Settlement.LastReport(MapId, 1) != null, "玩家应有月结报告");
			Check.Assert(core.Settlement.LastReport(MapId, 2) != null, "AI 也应有月结报告（同一套规则）");
			Check.Assert(core.Settlement.SettledCount >= 2, "两个势力各结算至少一次");
		}

		private static void EventsOnlyForHuman()
		{
			Harness harness = NewHarness(players: new[] { PlayerContext.Human(1), PlayerContext.Ai(2) }, enableEnemySpawn: false);
			CoreServices core = harness.Core;

			core.Map.GenerateMap(20260918, 6, 6, MapId);
			core.Orchestrator.StartMap(MapId);

			Check.Assert(core.Events.IsEngineStarted(MapId, 1), "人类玩家的事件引擎应启动");
			Check.Assert(!core.Events.IsEngineStarted(MapId, 2), "AI 不应启动事件引擎（口径：随机事件只为难玩家）");
			Check.AssertEqual(0, core.Events.GetActiveEvents(MapId, 2).Count, "AI 侧不应有生效中事件");
		}

		private static void PerPlayerIsolation()
		{
			Harness harness = NewHarness(players: new[] { PlayerContext.Human(1), PlayerContext.Ai(2) }, enableEnemySpawn: false);
			CoreServices core = harness.Core;

			core.Map.GenerateMap(20260919, 6, 6, MapId);
			core.Orchestrator.StartMap(MapId);

			string resource = core.Tables.AllResources().First().Name;
			float before1 = core.Resources.GetOrCreatePool(MapId, 1).GetValue(resource);
			float before2 = core.Resources.GetOrCreatePool(MapId, 2).GetValue(resource);

			core.Resources.AddResource(resource, 100f, MapId, 2);

			Check.AssertEqual(before1, core.Resources.GetOrCreatePool(MapId, 1).GetValue(resource), "玩家的资源不应被 AI 影响");
			Check.AssertEqual(before2 + 100f, core.Resources.GetOrCreatePool(MapId, 2).GetValue(resource), "AI 自己的资源应到账");
		}

		private static void StartMapIsIdempotent()
		{
			Harness harness = NewHarness(enableEnemySpawn: false);
			CoreServices core = harness.Core;

			core.Map.GenerateMap(20260920, 6, 6, MapId);
			core.Orchestrator.StartMap(MapId);
			int first = core.Time.SubscriberCount;

			IReadOnlyList<PlayerStartReport> again = core.Orchestrator.StartMap(MapId);

			Check.AssertEqual(first, core.Time.SubscriberCount, "重复启动不应重复登记任务");
			Check.Assert(again[0].Repeat, "第二次启动应被标记为重复");
			Check.Assert(!again[0].SettlementStarted, "重复启动不应再登记月结");

			// 地图不存在时不应创建任何东西（否则存档里会留孤儿分区）
			IReadOnlyList<PlayerStartReport> none = core.Orchestrator.StartMap("no-such-map");
			Check.AssertEqual(0, none.Count, "地图不存在时不启动任何势力");
			Check.Assert(!string.IsNullOrWhiteSpace(core.Orchestrator.LastStartSkippedReason), "应记录跳过原因");
		}

		// ────────────── 夹具 ──────────────

		private sealed class Harness
		{
			public CoreServices Core;

			public InMemoryFileSystem FileSystem;

			public InMemoryMapRepository MapRepository;

			public ISaveStore Store;
		}

		/// <summary>
		/// 按**生产同样的入口**（<see cref="CoreBootstrap"/>）装配，只把"与引擎相关"的实现换成内存替身：
		/// 文件系统 = 内存、地图仓库 = 内存、存档单元 = 内存（分区语义与真实实现完全一致）。
		/// <para>所以本组用例验的不是"替身能不能跑"，而是**生产装配本身**。</para>
		/// </summary>
		private static Harness NewHarness(IEnumerable<PlayerContext> players = null, bool enableEnemySpawn = true)
		{
			var fileSystem = new InMemoryFileSystem();
			var store = new JsonSaveStore(fileSystem, "mem://world.save");
			var mapRepository = new InMemoryMapRepository();

			CoreServices core = CoreBootstrap.Build(new CoreDependencies
			{
				FileSystem = fileSystem,
				ConfigSource = ConfigFixtures.RealConfigSource(),
				Random = new SystemRandom(20260914),
				ResourceConfigLoader = null,
				GeneratorConfigPath = null,
				MapRepositoryFactory = tables =>
				{
					mapRepository.Rebuilder = new SaveRebuilder(new BuildingFactory(tables.Buildings), new UnitFactory(tables.Units));
					mapRepository.TerrainResolver = terrainId => tables.Terrains.GetById(terrainId);
					return mapRepository;
				},
				SessionId = "wire-session",
				SaveStore = store,
				SaveRoot = "mem://save/",
				Players = players,
				EnableEnemySpawn = enableEnemySpawn,
			});

			// 用例一次性推进多日：默认单帧上限（30 日）会把 90 日拆成三次调用（`D28`）
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;

			return new Harness { Core = core, FileSystem = fileSystem, MapRepository = mapRepository, Store = store };
		}
	}
}

