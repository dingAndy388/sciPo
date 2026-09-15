using Newtonsoft.Json.Linq;
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
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Domain;
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
	/// （v0.6.3 / WP-7.2a）**生产类建筑的内容验收**：农田 / 矿场 / 仓库。
	/// <para>验的不是"能建造"（那是 `BuildingProductionChecks` 的事），而是**设计稿数值真的在跑**：
	/// 农田 144 food/年、矿场 100 basic minerals/月、仓库把基础石材上限抬高 —— 以及"产出只来自建筑"
	/// 这条口径（`D84`：`BaseGrowth=0`）成立。</para>
	/// </summary>
	internal static class ContentBuildingChecks
	{
		private const string MapId = "content";

		public static void RunAll()
		{
			Check.Run("WP-7.2a 农田：1 座 lv.I = 144 food/年（12/月，设计稿）", FarmProducesPerYear);
			Check.Run("WP-7.2a 矿场：1 座 lv.I = 100 basic minerals/月（设计稿）", MineProducesPerMonth);
			Check.Run("WP-7.2a 仓库：落成即抬高基础石材上限（基值 + 500，幂等）", WarehouseRaisesLimit);
			Check.Run("WP-7.2a 造价口径：农田/矿场/仓库的消耗与耗时 = 设计稿", DesignCostsAndDurations);
			Check.Run("WP-7.2a 已知张力：初始 800 石材 ≥ 最便宜产出建筑 600（开局不再死锁）", StartingStockAllowsAProducer);
			Check.Run("P0 占位六边形：6 顶点 / 宽高比 = 配置 / 与行步长自洽", PlaceholderHexagonGeometry);
			Check.Run("WP-7.2a 多点地块：\"平原或山地\"是**任一匹配**（列表不是 AND，旧口径永远建不了）", MultiTerrainIsAnyNotAll);
		}

		/// <summary>
		/// （v0.6.3 / WP-7.2a 修复）**"可建地块"列表 = 任一匹配**：矿场（平原、山地）应能在**山地**造出来，
		/// 而水域（不在列表里）应被拒。
		/// <para>旧实现把列表逐项 AND（`All()`）⇒ "平原或山地"变成"同时是平原和山地" ⇒ 任何 2+ 地块的建筑都造不出来。
		/// 本用例是那个缺陷的回归锁。</para>
		/// </summary>
		private static void MultiTerrainIsAnyNotAll()
		{
			Harness h = NewHarness();
			try
			{
				ITerrainData mountain = h.Core.Tables.Terrains.GetById("mountain");
				ITerrainData water = h.Core.Tables.Terrains.GetById("water");

				h.Map.SetTerrain(MapId, h.Site, mountain);
				Check.Assert(h.Construction.StartConstruction(MapId, "mine", h.Site, h.OwnerId),
					"矿场应能在**山地**开工（列表含 mountain）");

				var waterCell = new HexCubePosition(1, 1);
				h.Map.SetTerrain(MapId, waterCell, water);
				Check.Assert(!h.Construction.StartConstruction(MapId, "mine", waterCell, h.OwnerId),
					"矿场不应能在水域开工（列表里没有 water）");

				// 单地块建筑不受影响（回归面）
				var plainCell = new HexCubePosition(2, 2);
				Check.Assert(h.Construction.StartConstruction(MapId, "farm", plainCell, h.OwnerId),
					"农田（仅平原）应仍能在平原开工");
			}
			finally { h.Dispose(); }
		}

		/// <summary>农田 lv.I：产出 144 食物/年 → 12/月（`FoodGrowth` Absolute 12），跑满 12 个月刚好 +144。</summary>
		private static void FarmProducesPerYear()
		{
			Harness h = NewHarness();
			try
			{
				h.BuildAndFinish("farm");
				float before = h.Pool().GetValue("Food");

				h.AdvanceMonths(12);

				Check.AssertEqual(before + 144f, h.Pool().GetValue("Food"), "农田 lv.I 12 个月的产出（设计稿 144 food/年）");
			}
			finally { h.Dispose(); }
		}

		/// <summary>矿场 lv.I：产出 100 基础石材/月（`MineralGrowth` Absolute 100）。</summary>
		private static void MineProducesPerMonth()
		{
			Harness h = NewHarness();
			try
			{
				h.BuildAndFinish("mine");
				float before = h.Pool().GetValue("BasicMinerals");

				h.AdvanceMonths(1);
				Check.AssertEqual(before + 100f, h.Pool().GetValue("BasicMinerals"), "矿场 lv.I 一个月的产出（设计稿 100/月）");

				h.AdvanceMonths(2);
				Check.AssertEqual(before + 300f, h.Pool().GetValue("BasicMinerals"), "矿场 lv.I 三个月的产出（线性累加）");
			}
			finally { h.Dispose(); }
		}

		/// <summary>
		/// 仓库：落成即抬高**基础石材**上限（设计稿 +500），且口径是"重算"而不是"累加"。
		/// <para>验法：建一座 → 上限 = 基值 + 500；再手动重算两次 → 上限不变（幂等）；只影响基础石材。</para>
		/// </summary>
		private static void WarehouseRaisesLimit()
		{
			Harness h = NewHarness();
			try
			{
				float baseLimit = h.Pool().GetLimit("BasicMinerals");
				Check.AssertEqual(1500f, baseLimit, "基础石材基值上限（设计稿）");

				h.BuildAndFinish("warehouse");
				Check.AssertEqual(baseLimit + 500f, h.Pool().GetLimit("BasicMinerals"), "仓库 lv.I 落成后 +500（即时生效，不等月结）");
				Check.AssertEqual(2000f, h.Pool().GetLimit("Food"), "仓库只影响基础石材：食物上限不变");

				h.Resources.RefreshLimits(MapId, h.OwnerId);
				h.Resources.RefreshLimits(MapId, h.OwnerId);
				Check.AssertEqual(baseLimit + 500f, h.Pool().GetLimit("BasicMinerals"), "重复重算不应累加（上限是派生值）");
			}
			finally { h.Dispose(); }
		}

		/// <summary>造价与耗时抽查（设计稿「建造消耗 / 建造时间」列）。</summary>
		private static void DesignCostsAndDurations()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var expected = new (string Id, float Minerals, float Duration)[]
			{
				("farm", 600f, 120f),
				("farm_ii", 1200f, 180f),
				("mine", 600f, 180f),
				("mine_ii", 1200f, 240f),
				("warehouse", 800f, 60f),
				("warehouse_ii", 1600f, 120f),
			};

			foreach ((string id, float minerals, float duration) in expected)
			{
				IBuildingConfig config = core.Tables.Buildings.GetBuildingConfig(id);
				Check.Assert(config != null, $"建筑「{id}」应在表里");

				Check.AssertEqual(minerals, config.ResourceCost["BasicMinerals"], $"{id} 建造消耗（设计稿基础石材）");
				Check.AssertEqual(duration, config.Duration, $"{id} 建造耗时（设计稿日数）");
			}

			// 24 条建筑里已落地 21 条（12 旧 + 9 新）；缺口归 WP-7.2b
			Check.AssertEqual(23, core.Tables.AllBuildings().Count(), "当前建筑条目数（含观星台/骨笛工坊；24 条全表归 WP-7.2b）");
		}

		/// <summary>
		/// **开局可建造性**（v0.6.7 / P0，用户裁定方案 a：初始石材提到 800）：
		/// 初始基础石材必须 ≥ 最便宜的**产出**建筑（农田/矿场 600）⇒ 开局能建起第一个产出来源，经济不会死锁。
		/// <para>这条断言把"设计数值"与"能开局"挂钩：谁把初始储备调回去，测试立刻红。</para>
		/// </summary>
		private static void StartingStockAllowsAProducer()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			float initialMinerals = core.Tables.AllResources().First(r => r.Name == "BasicMinerals").BaseValue;
			float cheapestProducer = core.Tables.AllBuildings()
				.Where(b => b.Modifiers != null && b.Modifiers.Any(m => m.Target == "MineralGrowth"))
				.Select(b => b.ResourceCost.TryGetValue("BasicMinerals", out float v) ? v : float.MaxValue)
				.Min();

			Check.AssertEqual(800f, initialMinerals, "初始基础石材（设计稿 200 → 用户裁定 800，`D91`）");
			Check.AssertEqual(600f, cheapestProducer, "最便宜的产出建筑造价（设计稿矿场/农田 lv.I）");
			Check.Assert(initialMinerals >= cheapestProducer,
				$"开局必须造得起第一个产出来源：初始 {initialMinerals} ≥ 最便宜产出建筑 {cheapestProducer}");
		}

		/// <summary>
		/// （v0.6.7 / P0）**占位六边形的几何**：6 个顶点、宽 = 列步长、高 = 行步长×4/3、左右对称，
		/// 且与 `LayoutPosition` 的排布自洽（相邻行走访的重叠量 = 六边形高的 1/4）。
		/// <para>为什么值得断言：占位块是美术到位前唯一的"网格可见性"来源（`N1` 提出来的），
		/// 尺寸算错会让格子之间出现缝隙或重叠 —— 这种错一眼看不出，但会在换真美术时变成对齐问题。</para>
		/// </summary>
		private static void PlaceholderHexagonGeometry()
		{
			IMapAppearanceConfig appearance = ConfigFixtures.BuildRealCore().Appearance;
			(float X, float Y)[] hex = TerrainAppearance.HexOutline(appearance.CellXStep, appearance.CellYStep);

			Check.AssertEqual(6, hex.Length, "六边形应有 6 个顶点");

			float width = hex.Max(p => p.X) - hex.Min(p => p.X);
			float height = hex.Max(p => p.Y) - hex.Min(p => p.Y);

			Check.AssertEqual(appearance.CellXStep, width, "宽 = 列步长（相邻列无缝）");
			Check.AssertEqual(TerrainAppearance.HexHeight(appearance.CellYStep), height, "高 = 行步长 × 4/3");

			// 左右对称（x 的极值互为相反数）：不对称会在错位排布下露出锯齿
			Check.AssertEqual(-hex.Min(p => p.X), hex.Max(p => p.X), "关于格心左右对称");

			// 上下对称（y 的极值互为相反数）
			Check.AssertEqual(-hex.Min(p => p.Y), hex.Max(p => p.Y), "关于格心上下对称");

			// 与排布自洽：六边形高 = 1.3333 × 行步长 ⇒ 相邻行的重叠 = 高的 1/4（六边形堆叠的标准比例）
			float overlap = height - appearance.CellYStep;
			Check.AssertEqual(height / 4f, overlap, "相邻行重叠量 = 六边形高的 1/4");

			// 配置缺失时的兜底也要是合法六边形
			(float X, float Y)[] fallback = TerrainAppearance.HexOutline(0f, 0f);
			Check.AssertEqual(6, fallback.Length, "兜底（步长 0）也应有 6 个顶点");
			Check.AssertEqual(TerrainAppearance.DefaultCellXStep, fallback.Max(p => p.X) - fallback.Min(p => p.X), "兜底宽 = 缺省列步长");
		}

		// ────────────── 夹具 ──────────────

		private sealed class Harness : IDisposable
		{
			public string Dir;
			public int OwnerId = 1;
			public GameClock Clock;
			public CoreServices Core;
			public MapAppService Map;
			public ResourcesAppService Resources;
			public ConstructionAppService Construction;
			public HexCubePosition Site;

			public ResourcesPool Pool() => Resources.GetOrCreatePool(MapId, OwnerId);

			public void AdvanceMonths(int months) => Clock.AdvanceDays(TimeConstants.DaysPerMonth * months);

			/// <summary>建一座建筑并推到完工（走真实链路：开工 → 任务 → 完工回调注册修正器）。</summary>
			public void BuildAndFinish(string buildingId)
			{
				Check.Assert(Construction.StartConstruction(MapId, buildingId, Site, OwnerId), $"{buildingId} 应能开工");
				Clock.AdvanceDays(2); // 夹具把 Duration 压成 1 日
			}

			public void Dispose()
			{
				try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { /* 清理失败不影响结论 */ }
			}
		}

		/// <summary>
		/// 真实配置 + 临时目录 + 内存地图仓库；把新建筑的耗时压到 1 日、造价清空
		/// （只为让"产出数值"成为唯一变量：造价/工时已由 `DesignCostsAndDurations` 单独验）。
		/// </summary>
		private static Harness NewHarness()
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp72a-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			JObject buildings = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (string id in new[] { "farm", "mine", "warehouse" })
			{
				buildings["Buildings"][id]["Duration"] = 1;
				buildings["Buildings"][id]["ResourceCost"] = new JObject();
			}

			InMemoryConfigSource source = ConfigFixtures.RealConfigSourceWith("Buildings", buildings.ToString());
			CoreServices core = ConfigFixtures.BuildCore(source, mapRepository: new InMemoryMapRepository());
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;

			var fileSystem = new SystemFileSystem();
			var store = new JsonSaveStore(fileSystem, Path.Combine(dir, "world.save"));
			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"), store);
			var time = new GameTimeService(core.Session.Clock, tasks);
			var bus = new DomainEventBus();

			// ⚠️ 总线必须传进来：仓库靠 `BuildingCompletedEvent` 抬高上限（生产装配见 `CoreBootstrap`）
			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_"), store),
				core.Tables.Resources, time,
				new ModifierRepository(Path.Combine(dir, "mod_"), store), bus);
			var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(dir, "mod_"), store));
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees, store),
				core.Tables.TechTrees, resources, modifier, time, bus);
			var fog = new FogAppService(1, new FogRepository(Path.Combine(dir, "fog_"), store));

			MapAppService map = core.Map;
			var construction = new ConstructionAppService(
				map, resources, tech, new BuildingFactory(core.Tables.Buildings),
				core.Tables.Buildings, time, modifier, fog, bus);

			map.GenerateMap(20260921, 6, 6, MapId);
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				map.SetTerrain(MapId, cell.Position, plain);

			HexCubePosition site = map.GetAllCells(MapId).Select(c => c.Position).First(p => p.q == 4 && p.r == 4);
			resources.GetOrCreatePool(MapId, 1);

			return new Harness
			{
				Dir = dir,
				Clock = core.Session.Clock,
				Core = core,
				Map = map,
				Resources = resources,
				Construction = construction,
				Site = site,
			};
		}
	}
}
