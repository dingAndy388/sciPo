using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Events.Infrastructure;
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-2.10）**最小领域事件总线**（`F10`、`TECH-05`、`UNIT-08`、`EVT-04`、`ROOT-4`）的验收检查。
	/// <para>主张：建造完成 / 升级完成 / 训练完成 / 单位阵亡 / 研究完成 / 事件触发这六类"点子"都有推送，
	/// 且总线本身健壮（快照派发、异常隔离、重复订阅幂等、可退订）。</para>
	/// </summary>
	internal static class DomainEventChecks
	{
		private const string MapId = "event-map";

		public static void RunAll()
		{
			Check.Run("WP-2.10 总线：快照派发 / 异常隔离 / 重复订阅幂等 / 可退订", BusIsRobust);
			Check.Run("WP-2.10 建造完成事件：`BuildingCompletedEvent`（`CON-01`/`CON-02` 的推送面）", BuildingCompletedIsPublished);
			Check.Run("WP-2.10 升级完成事件：`BuildingUpgradedEvent`（含 from/to 等级）", BuildingUpgradedIsPublished);
			Check.Run("WP-2.10 训练完成事件：`UnitTrainedEvent`（单位诞生的推送面）", UnitTrainedIsPublished);
			Check.Run("WP-2.10 单位阵亡事件：`UnitDiedEvent`（`UNIT-08`：死亡原本没有任何推送）", UnitDiedIsPublished);
			Check.Run("WP-2.10 研究完成事件：`ResearchCompletedEvent`（`TECH-05`）", ResearchCompletedIsPublished);
			Check.Run("WP-2.10 事件触发推送：`GameEventTriggeredEvent`（`EVT-04`：不再只能轮询）", GameEventTriggeredIsPublished);
		}

		// ────────────────────────── 总线本身 ──────────────────────────

		private static void BusIsRobust()
		{
			var bus = new DomainEventBus();
			var received = new List<string>();

			Action<BuildingCompletedEvent> first = e => received.Add("first:" + e.BuildingId);
			Action<BuildingCompletedEvent> second = e => received.Add("second:" + e.BuildingId);
			Action<BuildingCompletedEvent> broken = e => throw new InvalidOperationException("订阅者自己炸了");
			Action<BuildingCompletedEvent> late = e => received.Add("late:" + e.BuildingId);

			bus.Subscribe(first);
			bus.Subscribe(first);        // 重复订阅应幂等
			bus.Subscribe(broken);
			bus.Subscribe(second);

			Check.AssertEqual(3, bus.SubscriberCount<BuildingCompletedEvent>(), "重复订阅不应重复注册");

			// 第二个订阅者在收到事件时退订第三个（自己）：本次派发仍应走完快照
			bus.Subscribe(late);
			bus.Subscribe<BuildingCompletedEvent>(e => bus.Unsubscribe(late));

			bus.Publish(new BuildingCompletedEvent(MapId, 1, "uid-1", "camp", new HexCubePosition(0, 0)));

			Check.AssertEqual(3, received.Count, $"异常订阅者不应影响其余订阅者（实际收到 {string.Join(",", received)}）");
			Check.Assert(received.Contains("first:camp") && received.Contains("second:camp") && received.Contains("late:camp"),
				"三个正常订阅者都应收到事件");
			Check.AssertEqual(1, bus.Failures.Count, "订阅者异常应被记录");
			Check.Assert(bus.Failures[0] is InvalidOperationException, "记录的应是订阅者抛出的异常");

			// 退订后不再收到
			bus.Unsubscribe(first);
			bus.Unsubscribe(late);
			received.Clear();
			bus.Publish(new BuildingCompletedEvent(MapId, 1, "uid-2", "camp", new HexCubePosition(0, 0)));
			Check.Assert(!received.Contains("first:camp") && !received.Contains("late:camp"), "退订后不应再收到事件");

			// 无订阅者时发布不应抛异常
			bus.Publish(new UnitDiedEvent(MapId, 1, "uid", "worker", new HexCubePosition(0, 0), null));
		}

		// ────────────────────────── 六类事件 ──────────────────────────

		private static void BuildingCompletedIsPublished()
		{
			Harness h = NewHarness();
			try
			{
				var events = new List<BuildingCompletedEvent>();
				h.Bus.Subscribe<BuildingCompletedEvent>(events.Add);

				h.Build("camp");
				h.Clock.AdvanceDays(2);

				Check.AssertEqual(1, events.Count, "建造完成应推送一次");
				Check.AssertEqual("camp", events[0].BuildingId, "事件里的建筑 Id");
				Check.AssertEqual(h.CampUid, events[0].BuildingUId, "事件里的建筑 uid");
				Check.AssertEqual(1, events[0].OwnerId, "所有者");
				Check.AssertEqual(h.Site, events[0].Position, "位置");
				Check.AssertEqual(MapId, events[0].MapId, "地图");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void BuildingUpgradedIsPublished()
		{
			Harness h = NewHarness();
			try
			{
				var upgraded = new List<BuildingUpgradedEvent>();
				var completed = new List<BuildingCompletedEvent>();
				h.Bus.Subscribe<BuildingUpgradedEvent>(upgraded.Add);
				h.Bus.Subscribe<BuildingCompletedEvent>(completed.Add);

				h.Build("camp");
				h.Clock.AdvanceDays(2);
				h.UnlockScience();
				Check.Assert(h.Construction.StartUpgrade(MapId, h.CampUid, h.OwnerId), "应能开工升级");
				h.Clock.AdvanceDays(2);

				Check.AssertEqual(1, upgraded.Count, "升级完成应推送一次");
				Check.AssertEqual("camp", upgraded[0].FromBuildingId, "升级前等级");
				Check.AssertEqual("camp_ii", upgraded[0].ToBuildingId, "升级后等级");
				Check.AssertEqual(h.CampUid, upgraded[0].BuildingUId, "uid 不变");
				Check.AssertEqual(1, completed.Count, "升级不应重复推送「建成」事件（两类事件分工明确）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void UnitTrainedIsPublished()
		{
			Harness h = NewHarness();
			try
			{
				var trained = new List<UnitTrainedEvent>();
				h.Bus.Subscribe<UnitTrainedEvent>(trained.Add);

				h.Build("workshop");
				h.Clock.AdvanceDays(2);
				h.SeedPopulation(2);
				Check.Assert(h.Units.TrainUnit(MapId, h.WorkshopUid, "worker"), "应能训练工人");
				h.Clock.AdvanceDays(3);

				Check.AssertEqual(1, trained.Count, "训练完成应推送一次");
				Check.AssertEqual("worker", trained[0].UnitId, "单位模板");
				Check.AssertEqual(1, trained[0].OwnerId, "所有者");
				Check.Assert(trained[0].UnitUId == h.Map.GetOccupantInfo(MapId, trained[0].Position).Value.UId,
					"事件里的 uid 应与落位单位一致");
				Check.Assert(trained[0].Position.DistenceTo(h.Site) <= 1, "落位在建筑格或相邻格");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void UnitDiedIsPublished()
		{
			Harness h = NewHarness();
			try
			{
				var died = new List<UnitDiedEvent>();
				h.Bus.Subscribe<UnitDiedEvent>(died.Add);

				// 敌方工人（owner 2）站在射手（owner 1）的射程内
				HexCubePosition victimPos = h.CellAtDistance(2, h.Site);
				Unit victim = h.SpawnFor(2, "worker", victimPos);
				Unit archer = h.SpawnFor(1, "archer", h.Site);
				Check.AssertEqual(2, archer.Position.DistenceTo(victim.Position), "射手与目标距离（射程 3 内）");

				Check.Assert(h.Units.ExcuteAction(MapId, archer.GetInfo().UId, victim.Position, victim.GetInfo().UId, "CanAttack"),
					"应能下达攻击指令");
				h.Clock.AdvanceDays(12); // 工人 50 HP / 弓箭手 6 伤害 → 9 日阵亡

				Check.AssertEqual(1, died.Count, "阵亡应推送一次");
				Check.AssertEqual(victim.GetInfo().UId, died[0].UnitUId, "阵亡单位 uid");
				Check.AssertEqual("worker", died[0].UnitId, "阵亡单位模板");
				Check.AssertEqual(2, died[0].OwnerId, "阵亡方所有者（不是凶手）");
				Check.AssertEqual(archer.GetInfo().UId, died[0].KillerUId, "凶手 uid");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void ResearchCompletedIsPublished()
		{
			Harness h = NewHarness();
			try
			{
				var completed = new List<ResearchCompletedEvent>();
				h.Bus.Subscribe<ResearchCompletedEvent>(completed.Add);

				h.Resources.AddResource("Idea", 500f, MapId, h.OwnerId);
				h.Tech.Research(MapId, h.OwnerId, "science", "writing");
				h.Clock.AdvanceDays(15);

				Check.AssertEqual(1, completed.Count, "研究完成应推送一次");
				Check.AssertEqual("science", completed[0].TreeId, "树 Id");
				Check.AssertEqual("writing", completed[0].NodeId, "节点 Id");
				Check.AssertEqual(h.OwnerId, completed[0].OwnerId, "所有者");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void GameEventTriggeredIsPublished()
		{
			Harness h = NewHarness(triggerChancePerDay: 1f);
			try
			{
				var triggered = new List<GameEventTriggeredEvent>();
				h.Bus.Subscribe<GameEventTriggeredEvent>(triggered.Add);

				h.Events.StartEventsEngine(MapId, h.OwnerId);
				h.Clock.AdvanceDays(1);

				Check.AssertEqual(1, triggered.Count, "事件触发应推送一次（100%/日）");
				Check.AssertEqual("wildfire", triggered[0].EventId, "事件 Id");
				Check.AssertEqual(3, triggered[0].Duration, "持续期（日）");
				Check.AssertEqual(MapId, triggered[0].MapId, "地图");
			}
			finally { Cleanup(h.Dir); }
		}


		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public GameClock Clock;
			public GameTimeService Time;
			public TaskRepository Tasks;
			public DomainEventBus Bus;
			public MapSession MapSession;
			public MapAppService Map;
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public ConstructionAppService Construction;
			public UnitsAppService Units;
			public TechTreesAppService Tech;
			public EventAppService Events;
			public FogAppService Fog;
			public ConfigTables Tables;
			public HexCubePosition Site;
			public string CampUid;
			public string WorkshopUid;

			/// <summary>地图上位于指定格 <paramref name="distance"/> 距离的**真实存在的**格子（找不到则抛）。</summary>
			public HexCubePosition CellAtDistance(int distance, HexCubePosition from)
				=> Map.GetAllCells(MapId)
					.Select(c => c.Position)
					.First(pos => pos.DistenceTo(from) == distance);

			/// <summary>在 <see cref="Site"/> 开工建造（快配置：2 日完工）。</summary>
			public void Build(string buildingId)
			{
				Resources.AddResource("Wood", 500f, MapId, OwnerId);
				Resources.AddResource("Gold", 500f, MapId, OwnerId);
				Construction.StartConstruction(MapId, buildingId, Site, OwnerId);
				Clock.AdvanceDays(2);

				string uid = Map.GetOccupantInfo(MapId, Site).Value.UId;
				if (buildingId == "camp") CampUid = uid;
				if (buildingId == "workshop") WorkshopUid = uid;
			}

			/// <summary>给 <see cref="Site"/> 半径 1 内注入人口（训练/生成的消耗来源）。</summary>
			public void SeedPopulation(int amount)
				=> Map.AddPopulation(MapId, Site, 1, 20, amount);

			/// <summary>直接生成一个单位（`CreateUnit` 入口；不需要建筑）。</summary>
			public Unit SpawnFor(int ownerId, string unitId, HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Gold", 200f, MapId, ownerId);
				Resources.AddResource("Wood", 200f, MapId, ownerId); // 弓箭手等需要木材
				Units.CreateUnit(MapId, unitId, position, ownerId);
				Clock.AdvanceDays(3); // 快配置：训练 3 日 → 就绪

				string uid = Map.GetOccupantInfo(MapId, position).Value.UId;
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}

			/// <summary>解锁科学树升级/事件前置：研究 writing → mathematics 等（顺序满足前置）。</summary>
			public void UnlockScience()
			{
				Resources.AddResource("Idea", 500f, MapId, OwnerId);
				Tech.Research(MapId, OwnerId, "science", "writing");
				Clock.AdvanceDays(15);
				Tech.Research(MapId, OwnerId, "science", "mathematics");
				Clock.AdvanceDays(30);
			}
		}

		/// <summary>
		/// 装配：真实配置（可覆写 Events 表以固定触发概率）+ 临时目录仓储 + 真实总线 +
		/// **一个共享的 <see cref="DomainEventBus"/>** 注入到四个应用服务。
		/// </summary>
		private static Harness NewHarness(float? triggerChancePerDay = null)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp210-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			// 快配置：建筑/升级 2 日、单位 3 日（其余字段与真实表一致），事件表可按需覆写
			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			ShortenDurations(source);
			if (triggerChancePerDay.HasValue) source.Inject("Events", EventsJson(triggerChancePerDay.Value));

			CoreServices core = ConfigFixtures.BuildCore(source);

			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue }; // `D28`
			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"));
			var time = new GameTimeService(clock, tasks);
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
			var fog = new FogAppService(1, new FogRepository(Path.Combine(dir, "fog_")));

			var session = new MapSession(new InMemoryMapRepository());
			var map = new MapAppService(core.MapGenerator, session);
			var factory = new BuildingFactory(core.Tables.Buildings);

			var construction = new ConstructionAppService(
				map, resources, tech, factory, core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(
				map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus);
			var events = new EventAppService(
				core.Tables.Events, resources, tech, modifier, time,
				new SystemRandom(20260914), bus);

			map.GenerateMap(20260914, 8, 8, MapId);

			// 全图铺平原：用例里的裸坐标（如"距离 2 的格子"）不应被 Voronoi 水地形挡住
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				map.SetTerrain(MapId, cell.Position, plain);

			return new Harness
			{
				Dir = dir,
				Clock = clock,
				Time = time,
				Tasks = tasks,
				Bus = bus,
				MapSession = session,
				Map = map,
				Resources = resources,
				Modifier = modifier,
				Construction = construction,
				Units = units,
				Tech = tech,
				Events = events,
				Fog = fog,
				Tables = core.Tables,
				// 工地取地图上**真实存在**的内部格（生成器是 Voronoi，硬编码坐标可能落在图外）
				Site = map.GetAllCells(MapId)
					.Select(c => c.Position)
					.First(pos => pos.DistenceTo(new HexCubePosition(4, 4)) == 0 || pos.DistenceTo(new HexCubePosition(4, 4)) == 1),
			};
		}

		/// <summary>把建筑/升级/单位时长压到 2~3 日（其余字段保持不变），并写回配置源。</summary>
		private static void ShortenDurations(InMemoryConfigSource source)
		{
			var buildings = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)buildings["Buildings"]).Properties())
			{
				entry.Value["Duration"] = 2;
				entry.Value["UpgradeDuration"] = 2;
			}

			var units = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)units["Units"]).Properties())
				entry.Value["Duration"] = 3;

			source.Inject("Buildings", buildings.ToString());
			source.Inject("Units", units.ToString());
		}

		/// <summary>单条 100%/日触发的事件表（`wildfire`，持续 3 日，无前置）。</summary>
		private static string EventsJson(float perDay)
			=> $$"""
			{ "Events": [ { "EventId": "wildfire", "Name": "野火", "Description": "测试事件",
			  "TriggerChancePerDay": {{perDay}}, "Duration": 3, "Modifiers": [],
			  "ResourcePrerequisites": {}, "TechPrerequisites": {} } ] }
			""";

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
