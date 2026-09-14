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
	/// （v0.3 / WP-3.2）**实体持久化**（`B10`、`MAP-02`）的验收检查 —— M0-3 ① 的门槛：
	/// 「存档 → 读档 → 推进 360 日后，**建筑/单位/人口/任务/迷雾/时间逐项等价**」。
	/// <para>验法：同一固定种子跑两个世界，其中一个在中途"存档 → 读档"，另一个一路跑到底；
	/// 到第 360 日比对六类状态。等价即证明"存读档对玩法透明"。</para>
	/// </summary>
	internal static class WorldPersistenceChecks
	{
		private const string MapId = "world-map";

		public static void RunAll()
		{
			Check.Run("M0-3 ① 存档 → 读档 → 360 日后逐项等价（建筑/单位/人口/任务/迷雾/时间）", SaveLoadIsTransparent);
			Check.Run("WP-3.2 单位细节：HP/MP/忙闲/攻击目标跨存档保留（读档后战斗继续）", UnitRuntimeStateSurvives);
			Check.Run("WP-3.2 建筑细节：等级/完工状态/训练队列/建造者绑定跨存档保留", BuildingExtrasSurvive);
			Check.Run("WP-3.2 存档版本：更高版本的存档被拒绝加载（不猜未知字段）", SaveVersionIsGuarded);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void SaveLoadIsTransparent()
		{
			// 同一个种子、同一套操作：A 一路跑到底；B 在第 30 日存档 → 读档（其他完全相同）
			string snapshotA = RunWorld(saveAtDay: 0);
			string snapshotB = RunWorld(saveAtDay: 30);

			CompareSnapshots(snapshotA, snapshotB);
		}

		/// <summary>
		/// 跑一个世界（0 = 不存读档，否则在第 N 日存档→读档），返回第 360 日的**状态快照**（可读文本，便于失败定位）。
		/// </summary>
		private static string RunWorld(int saveAtDay)
		{
			Harness h = NewHarness();
			try
			{
				// ① 事件引擎常开（两边一致；`StartEventsEngine` 幂等，读档会再调一次但不叠加）
				h.Events.StartEventsEngine(MapId, h.OwnerId);
				// ② 建营地（住房：人口 + 视野）与工坊（训练）
				h.Build("camp");
				h.Build("workshop", h.CellAtDistance(1, h.Site));
				h.SeedPopulation(3);
				h.Resources.AddResource("Idea", 500f, MapId, h.OwnerId);

				// ② 训练 1 个工人（快配置 3 日）→ 单位进入世界
				h.Units.TrainUnit(MapId, h.WorkshopUid, "worker");
				// ③ 研究 1 个节点（15 日）→ 任务在线
				h.Tech.Research(MapId, h.OwnerId, "science", "writing");

				// ④ 推进到第 30 日；按需存档 → 读档
				h.Clock.AdvanceDays(saveAtDay > 0 ? saveAtDay : 30);
				if (saveAtDay > 0) h.Save.ThenLoad();

				// ⑤ 再推进到第 360 日
				h.Clock.AdvanceDays(360 - (int)h.Clock.CurrentDay);

				return h.Snapshot();
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>六类状态逐项比对（先打差异，再断言）。</summary>
		private static void CompareSnapshots(string a, string b)
		{
			string[] linesA = a.Split('\n');
			string[] linesB = b.Split('\n');
			var diffs = new List<string>();

			int count = Math.Max(linesA.Length, linesB.Length);
			for (int i = 0; i < count; i++)
			{
				string left = i < linesA.Length ? linesA[i] : "(缺失)";
				string right = i < linesB.Length ? linesB[i] : "(缺失)";
				if (!string.Equals(left, right, StringComparison.Ordinal)) diffs.Add($"  A: {left}\n  B: {right}");
			}

			Check.Assert(diffs.Count == 0, $"存档读档后应逐项等价，差异 {diffs.Count} 处：\n{string.Join('\n', diffs.Take(12))}");
		}

		private static void UnitRuntimeStateSurvives()
		{
			Harness h = NewHarness();
			try
			{
				// 敌方工人站进射程，我方弓箭手开火 → 存档时战斗任务在跑
				HexCubePosition victimPos = h.CellAtDistance(2, h.Site);
				Unit victim = h.Spawn(2, "worker", victimPos);
				Unit archer = h.Spawn(1, "archer", h.Site);
				h.Units.ExcuteAction(MapId, archer.GetInfo().UId, victim.Position, victim.GetInfo().UId, "CanAttack");
				h.Clock.AdvanceDays(3); // 打掉 18 HP（工人 50）

				float hpBefore = victim.HP;
				float mpBefore = archer.CurrentMP;
				Check.Assert(hpBefore < 50f, "准备：目标应已受伤");

				h.Save.ThenLoad();

				// 读档后：同一 uid 的单位仍在，HP/MP/忙闲/攻击目标都保留 → 战斗继续直到阵亡
				Unit restoredVictim = (Unit)h.Map.FindOccupantByUId(MapId, victim.GetInfo().UId);
				Unit restoredArcher = (Unit)h.Map.FindOccupantByUId(MapId, archer.GetInfo().UId);
				Check.Assert(restoredVictim != null && restoredArcher != null, "两个单位都应按 uid 读回");
				Check.AssertEqual(hpBefore, restoredVictim.HP, "受伤单位的 HP 应精确保留");
				Check.AssertEqual(mpBefore, restoredArcher.CurrentMP, "移动力应保留");
				Check.AssertEqual(victim.GetInfo().UId, restoredArcher.AttackTargetUid, "攻击目标应保留");
				Check.Assert(restoredArcher.IsIdle == archer.IsIdle, "忙闲状态应保留");

				var died = new List<UnitDiedEvent>();
				h.Bus.Subscribe<UnitDiedEvent>(died.Add);
				h.Clock.AdvanceDays(12); // 继续打 → 阵亡

				Check.AssertEqual(1, died.Count, "读档后攻击循环应继续并击杀目标（任务恢复生效）");
				Check.AssertEqual(victim.GetInfo().UId, died[0].UnitUId, "阵亡的是原目标");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void BuildingExtrasSurvive()
		{
			Harness h = NewHarness();
			try
			{
				// 施工中的营地（2 日完工）：存档时建造者绑定 + 施工任务都在
				Unit worker = h.Spawn(1, "worker", h.CellAtDistance(1, h.Site));
				h.Resources.AddResource("Wood", 500f, MapId, h.OwnerId);
				Check.Assert(h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, "camp", "CanBuild"), "工人应能开工建营地");
				h.Clock.AdvanceDays(1); // 施工中

				string campUid = h.Map.GetOccupantInfo(MapId, h.Site).Value.UId;
				Check.Assert(!h.Map.GetOccupantInfo(MapId, h.Site).Value.IsReady, "准备：施工中的营地未就绪");

				h.Save.ThenLoad();

				MapOccupantInfo? restored = h.Map.GetOccupantInfo(MapId, h.Site);
				Check.Assert(restored.HasValue, "施工中的建筑应读回");
				Check.AssertEqual(campUid, restored.Value.UId, "uid 应原样保留");
				Check.Assert(!restored.Value.IsReady, "未完工状态应保留");

				Unit restoredWorker = (Unit)h.Map.FindOccupantByUId(MapId, worker.GetInfo().UId);
				Check.Assert(!restoredWorker.IsIdle, "建造者应仍处于忙状态（绑定的数据部分）");

				// 续跑施工 → 完工时释放建造者（回调在读档后补挂）
				h.Clock.AdvanceDays(2);
				Check.Assert(h.Map.GetOccupantInfo(MapId, h.Site).Value.IsReady, "读档后施工应继续完成");
				Check.Assert(restoredWorker.IsIdle, "完工后应释放读档时重建的建造者（回调补挂生效）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void SaveVersionIsGuarded()
		{
			Harness h = NewHarness();
			try
			{
				h.Map.GenerateMap(20260914, 8, 8, MapId);
				h.Save.ThenSave();

				// 直接篡改仓库里的存档版本（模拟"更新版本的游戏写的档"）
				h.MapRepository.BumpSaveVersion(MapId, SciencePotato.Scripts.Map.Infrastructure.MapSave.CurrentVersion + 1);

				h.Session.Maps.Evict(MapId);
				Check.Assert(h.Session.Maps.Get(MapId) == null, "更高版本的存档应被拒绝加载（返回 null 而不是猜字段）");
			}
			finally { Cleanup(h.Dir); }
		}

		// ────────────────────────── 夹具 ──────────────────────────

		/// <summary>存档/读档的一对小助手：让用例写成 `h.Save.ThenLoad()`。</summary>
		private sealed class SaveHelper(WorldSaveService service, string mapId)
		{
			public void ThenSave() => service.SaveWorld(mapId);

			public void ThenLoad()
			{
				service.SaveWorld(mapId);
				service.LoadWorld(mapId, 1);
			}
		}

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public GameClock Clock;
			public GameSession Session;
			public MapSession SessionMaps;
			public GameTimeService Time;
			public TaskRepository Tasks;
			public InMemoryMapRepository MapRepository;
			public MapAppService Map;
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public ConstructionAppService Construction;
			public UnitsAppService Units;
			public TechTreesAppService Tech;
			public FogAppService Fog;
			public EventAppService Events;
			public ConfigTables Tables;
			public DomainEventBus Bus;
			public SaveHelper Save;
			public HexCubePosition Site;
			public string CampUid;
			public string WorkshopUid;

			/// <summary>在指定格建造并推进到完工（快配置：2 日）。</summary>
			public string Build(string buildingId, HexCubePosition? position = null)
			{
				HexCubePosition site = position ?? Site;
				Resources.AddResource("Wood", 500f, MapId, OwnerId);
				Resources.AddResource("Gold", 500f, MapId, OwnerId);
				Construction.StartConstruction(MapId, buildingId, site, OwnerId);
				Clock.AdvanceDays(2);

				string uid = Map.GetOccupantInfo(MapId, site).Value.UId;
				if (buildingId == "camp") CampUid = uid;
				if (buildingId == "workshop") WorkshopUid = uid;
				return uid;
			}

			public HexCubePosition CellAtDistance(int distance, HexCubePosition from)
				=> Map.GetAllCells(MapId).Select(c => c.Position).First(pos => pos.DistenceTo(from) == distance);

			public void SeedPopulation(int amount) => Map.AddPopulation(MapId, Site, 1, 20, amount);

			public Unit Spawn(int ownerId, string unitId, HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Gold", 300f, MapId, ownerId);
				Resources.AddResource("Wood", 300f, MapId, ownerId);
				Units.CreateUnit(MapId, unitId, position, ownerId);
				Clock.AdvanceDays(3);

				string uid = Map.GetOccupantInfo(MapId, position).Value.UId;
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}

			/// <summary>
			/// 第 360 日的**状态快照**：六类状态逐项列成文本（不含随机 uid，便于跨世界比对）。
			/// 顺序固定（按位置排序）以保证比对稳定。
			/// </summary>
			public string Snapshot()
			{
				var lines = new List<string> { $"day={(int)Clock.CurrentDay}" };

				lines.Add($"population.radius1={Map.GetPopulationWithin(MapId, Site, 1)}");
				lines.Add($"population.total={Map.GetAllCells(MapId).Sum(c => c.Population)}");

				var pool = Resources.GetOrCreatePool(MapId, OwnerId);
				foreach (string resource in new[] { "Gold", "Wood", "Idea" })
					lines.Add($"resource.{resource}={pool.GetValue(resource):0.###}");

				var cells = Map.GetAllCells(MapId).OrderBy(c => c.Position.q).ThenBy(c => c.Position.r).ToList();
				foreach (MapCell cell in cells)
				{
					if (cell.Occupant == null) continue;

					MapOccupantInfo info = cell.Occupant.GetInfo();
					if (cell.Occupant is Building) lines.Add($"building@{cell.Position.q},{cell.Position.r}={info.Id}|ready:{info.IsReady}");
					else if (cell.Occupant is Unit unit) lines.Add($"unit@{cell.Position.q},{cell.Position.r}={info.Id}|hp:{unit.HP}|mp:{unit.CurrentMP}|idle:{unit.IsIdle}");
				}

				// 任务（类型 + 业务键 + 进度 + 目标）：**uid 承载型任务的 Id 不参与比对**
				// （人口增长/单位移动/攻击的 Id 就是随机 uid，跨世界必然不同；等价性看"有条数、有进度"）
				var tasks = Tasks.GetCurrentTasks(MapId)
					.OrderBy(t => t.Type, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal)
					.Select(t => IsUidBearingTask(t.Type)
						? $"task.{t.Type}|{t.Progress:0.##}/{t.Target:0.##}"
						: $"task.{t.Type}={t.Id}|{t.Progress:0.##}/{t.Target:0.##}")
					.ToList();
				lines.AddRange(tasks);

				foreach (HexCubePosition pos in Site.InRadius(1))
					lines.Add($"fog@{pos.q},{pos.r}={Fog.GetVisibility(pos)}");

				return string.Join('\n', lines) + "\n";
			}

			/// <summary>任务的业务键是否是实体 uid（这类键跨世界不可比）。</summary>
			private static bool IsUidBearingTask(string type)
				=> type is "PopulationGrowth" or "UnitMove" or "UnitAttack";
		}




		private static Harness NewHarness(int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp32-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			// 快配置（建筑/升级 2 日、单位 3 日）+ **零概率事件表**（事件状态落盘归 WP-3.3，本用例只看六类状态）
			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			ShortenDurations(source);
			source.Inject("Events", ZeroChanceEvents());

			var mapRepository = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildCore(source, mapRepository: mapRepository);

			var session = new GameSession(core.Session.Maps, core.Session.Clock, "wp32-session");
			var clock = session.Clock;
			clock.MaxDaysPerAdvance = int.MaxValue; // `D28`

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
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_")));

			var map = core.Map;
			var factory = new BuildingFactory(core.Tables.Buildings);
			var construction = new ConstructionAppService(
				map, resources, tech, factory, core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(
				map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus);

			var clockRepo = new FileClockRepository(Path.Combine(dir, "clock_"));
			var events = new EventAppService(core.Tables.Events, resources, tech, modifier, time, new SystemRandom(20260914), bus);
			var save = new WorldSaveService(session, map, time, clockRepo, tasks, construction, units, tech, resources, events, fog);

			// 生成地图并全图铺平原（用例里的裸坐标不应被 Voronoi 水地形挡住）
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			map.GenerateMap(20260914, 8, 8, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				map.SetTerrain(MapId, cell.Position, plain);

			HexCubePosition site = map.GetAllCells(MapId)
				.Select(c => c.Position).First(pos => pos.q == 4 && pos.r == 4);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = clock,
				Session = session,
				SessionMaps = core.Session.Maps,
				Time = time,
				Tasks = tasks,
				MapRepository = mapRepository,
				Map = map,
				Resources = resources,
				Modifier = modifier,
				Construction = construction,
				Units = units,
				Tech = tech,
				Fog = fog,
				Tables = core.Tables,
				Bus = bus,
				Events = events,
				Save = new SaveHelper(save, MapId),
				Site = site,
			};
		}

		/// <summary>零概率事件表（1 条占位事件：避免"表为空"error，同时保证 360 日内不会触发）。</summary>
		private static string ZeroChanceEvents()
			=> "{ \"Events\": [ { \"EventId\": \"none\", \"Name\": \"占位事件\", \"Description\": \"零概率：本用例只验六类状态等价\", "
			   + "\"TriggerChancePerDay\": 0, \"Duration\": 0, \"Modifiers\": [], \"ResourcePrerequisites\": {}, \"TechPrerequisites\": {} } ] }";

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
