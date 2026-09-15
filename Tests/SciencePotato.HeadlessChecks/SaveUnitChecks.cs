using Newtonsoft.Json;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Save;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Events.Domain;
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
	/// （v0.3 / WP-3.3）**统一存档单元**（`I3`、`DEP-06`、`TIME-08`）的验收检查。
	/// <para>改造前：时钟/任务/迷雾/资源/科技/修正器各写各的文件（五套路径规范、五份序列化代码、五个版本号、
	/// 没有原子性、没有迁移）；本组用例验\"收敛成一个会话一份文件 + 原子写 + `saveVersion` 迁移\"真的成立，
	/// 以及顺带修掉的事件状态不落盘（`EVT-02` 的\"无期限加成\"）。</para>
	/// </summary>
	internal static class SaveUnitChecks
	{
		private const string MapId = "world-map";

		public static void RunAll()
		{
			Check.Run("WP-3.3 统一存档：存档点后只有一份文件 + 分区齐全（任务/资源/修正器/科技/迷雾/事件/时钟）", SingleFileAndSections);
			Check.Run("WP-3.3 原子写：先写 .tmp 再替换（一次存档点一次落盘、不留临时文件、垃圾临时文件不污染正式档）", AtomicWrite);
			Check.Run("WP-3.3 版本门禁：更高 saveVersion 的存档被拒绝加载（世界存档与存档单元都拒绝）", NewerSaveVersionIsRejected);
			Check.Run("WP-3.3 迁移 v1→v3：任务分区键改为 Type:UId:Id（并丢弃历史 null 项）", TaskKeyMigrationApplies);
			Check.Run("WP-3.3 迁移 v2→v3：时钟并入存档文件头（时钟仓储读到的是文件头日期）", ClockMigrationApplies);
			Check.Run("WP-3.3 事件状态落盘：生效中事件/剩余天数/触发计数/掷骰次数跨档保留，且读档后继续按日推进", EventStateSurvives);
			Check.Run("WP-3.3 存档单元不改变玩法：store 化的存档→读档后任务继续（施工完工并释放建造者）", WorldStillRunsThroughStore);
		}

		// ────────────────────────── 用例 ──────────────────────────

		/// <summary>
		/// 存档点之后：**一份文件**（`world.save`）+ 六个子系统的分区 + 文件头里带着游戏日与版本号，
		/// 且\"一次存档点只落盘一次\"（此前每个任务每日各自覆盖整文件，写放大明显）。
		/// </summary>
		private static void SingleFileAndSections()
		{
			Harness h = NewHarness();
			try
			{
				h.Events.StartEventsEngine(MapId, h.OwnerId);
				h.Resources.AddResource("BasicMinerals", 500f, MapId, h.OwnerId);
				h.Resources.AddResource("Idea", 500f, MapId, h.OwnerId);
				h.Modifier.AddModifiers(MapId, h.OwnerId, "wp33-source",
					new List<Modifier> { new Modifier { Target = "Idea", Type = "Add", Value = 5f } });
				h.Fog.RevealArea(h.Site, 1);
				h.Tech.Research(MapId, h.OwnerId, "math", "counting");
				h.Clock.AdvanceDays(5);

				h.Save.SaveWorld(MapId, h.OwnerId);

				string[] files = Directory.GetFiles(h.Dir);
				Check.AssertEqual(1, files.Length,
					$"存档点后目录里应只有一份统一存档（地图由内存替身持有），实际：{string.Join(", ", files.Select(Path.GetFileName))}");
				Check.AssertEqual("world.save", Path.GetFileName(files[0]), "统一存档的文件名由宿主决定");

				IReadOnlyList<string> sections = h.Store.ListSections();
				foreach (string prefix in new[] { "tasks:", "resources:", "modifiers:", "tech:", "fog:", "events:" })
					Check.Assert(sections.Any(s => s.StartsWith(prefix, StringComparison.Ordinal)),
						$"缺少 `{prefix}` 分区（五个仓储与事件都应收敛进统一存档）：{string.Join(", ", sections)}");

				Check.AssertEqual(SaveFile.CurrentVersion, h.Store.SaveVersion, "存档版本应为当前版本");
				Check.AssertEqual((double)h.Clock.CurrentDay, h.Store.Day, "游戏日应并入存档文件头");
				Check.AssertEqual(1, h.Store.CommitCount, "一次存档点只应落盘一次（写放大收敛）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>
		/// 原子写：`*.tmp` 写入 → 替换正式文件。验三件事：落盘后没有残留临时文件、
		/// 第二次落盘走\"替换\"（首次是创建）、**崩溃残留的垃圾临时文件不会污染正式档**。
		/// </summary>
		private static void AtomicWrite()
		{
			Harness h = NewHarness();
			try
			{
				string tempPath = h.SavePath + JsonSaveStore.TempSuffix;

				h.Resources.AddResource("BasicMinerals", 100f, MapId, h.OwnerId);
				h.Clock.AdvanceDays(3);
				h.Save.SaveWorld(MapId, h.OwnerId);

				Check.AssertEqual(1, h.Store.CommitCount, "首次存档点应落盘一次");
				Check.AssertEqual(0, h.Store.ReplaceCount, "首次是创建文件（无需替换）");
				Check.Assert(!File.Exists(tempPath), "落盘后不应留下 .tmp");
				Check.Assert(File.ReadAllText(h.SavePath).Contains("\"SaveVersion\""), "落盘内容应是完整存档（含文件头）");

				// 模拟\"上一次写到一半被杀\"：临时文件里躺着半截内容
				h.FileSystem.WriteAllText(tempPath, "{ 这不是 JSON，也不是存档");
				h.Clock.AdvanceDays(1);
				h.Save.SaveWorld(MapId, h.OwnerId);

				Check.AssertEqual(2, h.Store.CommitCount, "第二次存档点应再落盘一次");
				Check.AssertEqual(1, h.Store.ReplaceCount, "第二次落盘应通过替换完成（原子）");
				Check.Assert(!File.Exists(tempPath), "替换后临时文件应被移走");

				var reopened = new JsonSaveStore(h.FileSystem, h.SavePath);
				reopened.Load();
				Check.AssertEqual(SaveFile.CurrentVersion, reopened.SaveVersion, "正式档仍应可完整读出（垃圾临时文件没污染它）");
				Check.AssertEqual((double)h.Clock.CurrentDay, reopened.Day, "读到的是本次存档点的日期");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>更高版本的存档：**拒绝加载**（猜未知字段只会静默丢数据），且失败原因要可读。</summary>
		private static void NewerSaveVersionIsRejected()
		{
			Harness h = NewHarness();
			try
			{
				// 在任何读访问之前，直接把\"更新版本的游戏\"写出的存档放到盘上
				h.FileSystem.WriteAllText(h.SavePath, JsonConvert.SerializeObject(new
				{
					SaveVersion = SaveFile.CurrentVersion + 1,
					SessionId = "future",
					Day = 10d,
					Sections = new Dictionary<string, string>(),
				}));

				Check.Assert(!h.Save.LoadWorld(MapId, h.OwnerId), "更高版本的存档应被拒绝加载（LoadWorld 返回 false）");
				Check.Assert(!string.IsNullOrWhiteSpace(h.Save.LastLoadError), "拒绝加载应留下可读的原因");
				Check.Assert(h.Save.LastLoadError.Contains("拒绝加载"), $"原因应说明是版本问题：{h.Save.LastLoadError}");

				bool threw = false;
				try { new JsonSaveStore(h.FileSystem, h.SavePath).Load(); }
				catch (SaveVersionTooNewException) { threw = true; }
				Check.Assert(threw, "存档单元读盘时应抛 SaveVersionTooNewException（而不是返回半份内容）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>
		/// v1→v3 迁移：任务分区从\"以 `Id` 为键\"改为 `Type:UId:Id`（`TIME-03`），
		/// 并顺带丢弃历史 <c>null</c> 项（`TIME-04` 写坏的文件里会留 `\"key\": null`）。
		/// </summary>
		private static void TaskKeyMigrationApplies()
		{
			string dir = TempDir();
			try
			{
				var fileSystem = new SystemFileSystem();
				string path = Path.Combine(dir, "world.save");
				fileSystem.WriteAllText(path, LegacyV1Save());

				var store = new JsonSaveStore(fileSystem, path);
				store.Load();

				Check.AssertEqual(SaveFile.CurrentVersion, store.SaveVersion, "v1 档加载后应升到当前版本");
				Check.Assert(store.AppliedMigrations.Count == 2, $"应依次应用两步迁移，实际：{string.Join(" | ", store.AppliedMigrations)}");

				var tasks = new TaskRepository(Path.Combine(dir, "tasks_"), store);
				List<TaskSnapshot> restored = tasks.GetCurrentTasks(MapId);

				Check.AssertEqual(2, restored.Count, "两条任务快照都应保留（历史 null 项应被丢弃）");
				Check.Assert(restored.All(t => t.Key == $"{t.Type}:{t.UId}:{t.Id}"), "仓储键应为 Type:UId:Id（旧键会让同名任务互相覆盖）");
				Check.AssertEqual(2, restored.Select(t => t.UId).Distinct().Count(), "两个实例 uid 都应存在");

				// 迁移结果会被写回：存档点后再用\"新进程\"读一遍，键仍是新口径
				store.Commit();
				var reopened = new JsonSaveStore(fileSystem, path);
				reopened.Load();
				var reopenedTasks = new TaskRepository(Path.Combine(dir, "tasks_"), reopened);
				Check.AssertEqual(SaveFile.CurrentVersion, reopened.SaveVersion, "迁移应被写回存档（下次启动不必再迁）");
				Check.AssertEqual(2, reopenedTasks.GetCurrentTasks(MapId).Count, "写回后任务数不变");
			}
			finally { Cleanup(dir); }
		}

		/// <summary>v2→v3 迁移：时钟从独立分区并入**文件头**（`SaveFile.Day`），分区本身被删除。</summary>
		private static void ClockMigrationApplies()
		{
			string dir = TempDir();
			try
			{
				var fileSystem = new SystemFileSystem();
				string path = Path.Combine(dir, "world.save");
				fileSystem.WriteAllText(path, LegacyV2Save());

				var store = new JsonSaveStore(fileSystem, path);
				store.Load();

				Check.AssertEqual(SaveFile.CurrentVersion, store.SaveVersion, "v2 档加载后应升到当前版本");
				Check.AssertEqual(123d, store.Day, "游戏日应从旧 `clock_*` 分区搬进文件头");
				Check.Assert(!store.HasSection("clock_wp33"), "旧时钟分区应被移除（避免两处真相）");
				Check.Assert(store.AppliedMigrations.Any(m => m.Contains("时钟")), $"应记录 v2→v3 迁移：{string.Join(" | ", store.AppliedMigrations)}");

				Check.AssertEqual(123d, new FileClockRepository(Path.Combine(dir, "clock_"), store).LoadDay("wp33"),
					"时钟仓储接入存档单元后应读到文件头里的日期");
			}
			finally { Cleanup(dir); }
		}

		/// <summary>
		/// 事件状态落盘（v0.3.15 的已知缺陷）：生效中事件 + 剩余天数 + 触发计数 + 掷骰次数跨档保留；
		/// 且读档后**日节拍重新挂上**（否则事件从此不再推进 —— `EVT-03` 幂等标志的陷阱）。
		/// </summary>
		private static void EventStateSurvives()
		{
			Harness h = NewHarness(eventsJson: AlwaysTriggerEvents());
			try
			{
				h.Events.StartEventsEngine(MapId, h.OwnerId);
				h.Clock.AdvanceDays(1);

				Check.Assert(h.Events.IsActive(MapId, h.OwnerId, "boom"), "必然触发的事件应在第 1 日生效");
				ActiveEvent active = h.Events.GetActiveEvents(MapId, h.OwnerId).Single();
				int remainingBefore = active.RemainingDays;
				int countBefore = h.Events.GetTriggerCounts(MapId, h.OwnerId)["boom"];
				int rollsBefore = h.Events.RollCount;

				Check.AssertEqual(4, remainingBefore, "持续 5 日的事件：第 1 日结束时剩余 4 日（触发当日起算第 1 日）");
				Check.AssertEqual(1, rollsBefore, "第 1 个游戏日恰好掷 1 次骰");

				h.Save.SaveWorld(MapId, h.OwnerId);
				Check.Assert(h.Save.LoadWorld(MapId, h.OwnerId), "读档应成功");

				Check.Assert(h.Events.IsActive(MapId, h.OwnerId, "boom"),
					"生效中的事件应跨档保留（否则会留下\"没有到期日的加成\"）");
				Check.AssertEqual(remainingBefore, h.Events.GetActiveEvents(MapId, h.OwnerId).Single().RemainingDays,
					"剩余天数应原样接上（不重新满血）");
				Check.AssertEqual(countBefore, h.Events.GetTriggerCounts(MapId, h.OwnerId)["boom"], "触发计数应保留");
				Check.AssertEqual(rollsBefore, h.Events.RollCount, "掷骰次数应保留");

				// 剩余的 4 个游戏日：仍在生效 → 不重复触发，逐日减到 0 即到期
				h.Clock.AdvanceDays(4);
				Check.Assert(!h.Events.IsActive(MapId, h.OwnerId, "boom"), "读档后事件应按日继续推进并到期");
				Check.AssertEqual(rollsBefore + 4, h.Events.RollCount, "读档后日节拍应重新挂上（否则 RollCount 不再增长）");
				Check.AssertEqual(countBefore, h.Events.GetTriggerCounts(MapId, h.OwnerId)["boom"], "生效期内不应重复触发");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>存档单元不改变玩法：任务/资源/迷雾都走统一存档后，\"存档→读档→继续跑\"仍然等价。</summary>
		private static void WorldStillRunsThroughStore()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnUnit(1, "worker", h.CellAtDistance(1, h.Site));
				h.Resources.AddResource("BasicMinerals", 500f, MapId, h.OwnerId);
				Check.Assert(h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, "camp", "CanBuild"), "工人应能开工建营地");
				h.Clock.AdvanceDays(1); // 施工中

				h.Save.SaveWorld(MapId, h.OwnerId);
				Check.Assert(h.Save.LoadWorld(MapId, h.OwnerId), "读档应成功");
				Check.Assert(h.Save.LastRestoredTaskCount > 0, "读档应恢复施工任务（快照来自统一存档）");
				Check.Assert(h.Save.LastAppliedMigrations.Count == 0, "当前版本的档不应触发迁移");

				MapOccupantInfo? restored = h.Map.GetOccupantInfo(MapId, h.Site);
				Check.Assert(restored.HasValue, "施工中的建筑应读回");
				Check.Assert(!restored.Value.IsReady, "未完工状态应保留");

				Unit restoredWorker = (Unit)h.Map.FindOccupantByUId(MapId, worker.GetInfo().UId);
				Check.Assert(!restoredWorker.IsIdle, "建造者应仍处于忙状态");

				h.Clock.AdvanceDays(2);
				Check.Assert(h.Map.GetOccupantInfo(MapId, h.Site).Value.IsReady, "读档后施工应继续完成");
				Check.Assert(restoredWorker.IsIdle, "完工后应释放读档时重建的建造者");
			}
			finally { Cleanup(h.Dir); }
		}

		// ────────────────────────── 夹具 ──────────────────────────

		/// <summary>存档单元周边的一整套装配（各仓储全部 store 化；地图仍由内存替身持有）。</summary>
		private sealed class Harness
		{
			public string Dir;
			public SystemFileSystem FileSystem;
			public JsonSaveStore Store;
			public string SavePath;
			public int OwnerId;
			public GameSession Session;
			public MapAppService Map;
			public GameClock Clock;
			public GameTimeService Time;
			public TaskRepository Tasks;
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public TechTreesAppService Tech;
			public FogAppService Fog;
			public ConstructionAppService Construction;
			public UnitsAppService Units;
			public EventAppService Events;
			public WorldSaveService Save;
			public ConfigTables Tables;
			public HexCubePosition Site;

			public HexCubePosition CellAtDistance(int distance, HexCubePosition from)
				=> Map.GetAllCells(MapId).Select(c => c.Position).First(pos => pos.DistenceTo(from) == distance);

			public Unit SpawnUnit(int ownerId, string unitId, HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Food", 300f, MapId, ownerId);
				Resources.AddResource("BasicMinerals", 300f, MapId, ownerId);
				Units.CreateUnit(MapId, unitId, position, ownerId);
				Clock.AdvanceDays(3);

				string uid = Map.GetOccupantInfo(MapId, position).Value.UId;
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}
		}

		private static Harness NewHarness(int ownerId = 1, string eventsJson = null)
		{
			string dir = TempDir();

			// 快配置（建筑/升级 2 日、单位 3 日）+ 事件表（缺省零概率，避免干扰"只验存档单元"的用例）
			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			ShortenDurations(source);
			source.Inject("Events", eventsJson ?? ZeroChanceEvents());

			var mapRepository = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildCore(source, mapRepository: mapRepository);

			var session = new GameSession(core.Session.Maps, core.Session.Clock, "wp33-session");
			GameClock clock = session.Clock;
			clock.MaxDaysPerAdvance = int.MaxValue; // `D28`

			// 统一存档单元：**一个会话一份文件**（所有仓储共用同一个实例 —— 两个实例会互相覆盖）
			var fileSystem = new SystemFileSystem();
			string savePath = Path.Combine(dir, "world.save");
			var store = new JsonSaveStore(fileSystem, savePath);

			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"), store);
			var time = new GameTimeService(clock, tasks);
			var bus = new DomainEventBus();

			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_"), store),
				core.Tables.Resources,
				time,
				new ModifierRepository(Path.Combine(dir, "mod_"), store));
			var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(dir, "mod_"), store));
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees, store),
				core.Tables.TechTrees,
				resources,
				modifier,
				time,
				bus);
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_"), store));

			var map = core.Map;
			var factory = new BuildingFactory(core.Tables.Buildings);
			var construction = new ConstructionAppService(
				map, resources, tech, factory, core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(
				map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus);

			var clockRepo = new FileClockRepository(Path.Combine(dir, "clock_"), store);
			var events = new EventAppService(core.Tables.Events, resources, tech, modifier, time, new SystemRandom(20260915), bus, store);
			var save = new WorldSaveService(session, map, time, clockRepo, tasks, construction, units, tech, resources, events, fog, store);

			// 生成地图并全图铺平原（用例里的裸坐标不应被 Voronoi 水地形挡住）
			map.GenerateMap(20260914, 8, 8, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				map.SetTerrain(MapId, cell.Position, plain);

			HexCubePosition site = map.GetAllCells(MapId)
				.Select(c => c.Position).First(pos => pos.q == 4 && pos.r == 4);

			return new Harness
			{
				Dir = dir,
				FileSystem = fileSystem,
				Store = store,
				SavePath = savePath,
				OwnerId = ownerId,
				Session = session,
				Map = map,
				Clock = clock,
				Time = time,
				Tasks = tasks,
				Resources = resources,
				Modifier = modifier,
				Tech = tech,
				Fog = fog,
				Construction = construction,
				Units = units,
				Events = events,
				Save = save,
				Tables = core.Tables,
				Site = site,
			};
		}

		// ────────────────────────── 存档夹具（旧版本文件 + 配置片段） ──────────────────────────

		/// <summary>v1 档：**没有版本头**，任务分区以 `Id` 为键，且含一条历史 <c>null</c> 项（`TIME-04` 的痕迹）。</summary>
		private static string LegacyV1Save()
		{
			var legacy = new Dictionary<string, TaskSnapshot>(StringComparer.Ordinal)
			{
				["camp"] = new TaskSnapshot { MapId = MapId, OwnerId = 1, Progress = 1f, Target = 2f, Id = "camp", Type = "Construction", UId = "uid-a" },
				["workshop"] = new TaskSnapshot { MapId = MapId, OwnerId = 1, Progress = 0f, Target = 2f, Id = "workshop", Type = "Upgrade", UId = "uid-b" },
				["stale"] = null,
			};

			return JsonConvert.SerializeObject(new
			{
				SessionId = "wp33",
				Sections = new Dictionary<string, string> { [$"tasks:{MapId}"] = JsonConvert.SerializeObject(legacy) },
			});
		}

		/// <summary>v2 档：有版本头，但**时钟仍是独立分区**（`clock_{sessionId}` → `{ "Day": … }`）。</summary>
		private static string LegacyV2Save()
			=> JsonConvert.SerializeObject(new
			{
				SaveVersion = 2,
				SessionId = "wp33",
				Sections = new Dictionary<string, string>
				{
					["clock_wp33"] = JsonConvert.SerializeObject(new { Day = 123d }),
				},
			});

		/// <summary>零概率事件表（1 条占位事件：避免"表为空"error，同时保证不会触发）。</summary>
		private static string ZeroChanceEvents()
			=> "{ \"Events\": [ { \"EventId\": \"none\", \"Name\": \"占位事件\", \"Description\": \"零概率：本组用例只验存档单元\", \"TriggerChancePerDay\": 0, \"Duration\": 0, \"Modifiers\": [], \"ResourcePrerequisites\": {}, \"TechPrerequisites\": {} } ] }";

		/// <summary>必然触发（100%/日）、持续 5 日的事件表（验收事件状态落盘用）。</summary>
		private static string AlwaysTriggerEvents()
			=> "{ \"Events\": [ { \"EventId\": \"boom\", \"Name\": \"必触发事件\", \"Description\": \"100%/日、持续 5 日\", \"TriggerChancePerDay\": 1, \"Duration\": 5, \"Modifiers\": [], \"ResourcePrerequisites\": {}, \"TechPrerequisites\": {} } ] }";

		/// <summary>建筑/升级 2 日、单位 3 日（其余字段与真实表一致）。</summary>
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

		private static string TempDir()
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp33-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			return dir;
		}

		private static void Cleanup(string dir)
		{
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch (IOException)
			{
				// 临时目录清理不影响验收结果
			}
		}
	}
}
