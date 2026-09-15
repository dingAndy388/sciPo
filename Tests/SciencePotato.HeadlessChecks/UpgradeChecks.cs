using Newtonsoft.Json.Linq;
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
	/// （v0.3 / WP-2.6）**建筑升级（lv.I→II→III）+ 完成逻辑收敛**（`CON-09`、`D4/D5/D14`、`CON-03`）的验收检查。
	/// <para>两条主张：① 升级是"同一栋建筑换配置"（uid 不变、参数按等级生效、旧等级的修正器/人口任务被替换）；
	/// ② 建造完成 / 升级完成 / 读档续跑完成走**同一个** `CompleteConstruction`（旧实现手抄三份，续跑那份漏了视野与人口任务）。</para>
	/// </summary>
	internal static class UpgradeChecks
	{
		private const string MapId = "upgrade-map";

		public static void RunAll()
		{
			Check.Run("WP-2.6 升级链：4 条链齐全且参数取自设计稿（营地 9/1/300 → 18/1/240 → 36/2/180）", UpgradeChainMatchesDesign);
			Check.Run("WP-2.6 门控：科技未解锁 / 资源不足 / 已最高级 / 未完工 都不得升级", UpgradeGatesRejectInvalid);
			Check.Run("WP-2.6 升级成功：30 日后 Id 换级、uid 不变、参数生效、建造者释放（`CON-09`）", UpgradeSwapsConfigKeepingUId);
			Check.Run("WP-2.6 修正器换级不叠加：学院 lv.II 的月结产出按 1000 计（不是 250+1000）", UpgradeReplacesModifiers);
			Check.Run("WP-2.6 人口任务换级不重复：一条任务、Target 从 300 → 240（`WP-2.2` 范围注销）", UpgradeReplacesHousingTask);
			Check.Run("WP-2.6 `CompleteConstruction` 收敛：续跑完成也开视野 + 注册人口任务（`CON-03`）", ResumePathSharesCompletion);
			Check.Run("WP-2.6 校验器：悬空 UpgradeTo / 自环判 error，缺 UpgradeDuration 判 warning", ValidatorGuardsUpgradeChain);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void UpgradeChainMatchesDesign()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			ConfigTables tables = core.Tables;

			Check.Assert(!core.ConfigReport.HasErrors, $"真实配置不应有 error，实际：{core.ConfigReport.Summary()}");
			int upgradeWarnings = core.ConfigReport.Issues.Count(issue => issue.Level == ConfigIssueLevel.Warning
				&& (issue.ToString().Contains("UpgradeTo") || issue.ToString().Contains("UpgradeDuration") || issue.ToString().Contains("UpgradeCost")));
			Check.AssertEqual(0, upgradeWarnings, $"升级字段不应有 warning，实际：{core.ConfigReport.ToLines()}");

			// 四级链：每级 UpgradeTo 都能解析到下一级
			foreach (string chain in new[] { "camp", "workshop", "school", "military_camp" })
			{
				IBuildingConfig level1 = tables.Buildings.GetBuildingConfig(chain);
				IBuildingConfig level2 = tables.Buildings.GetBuildingConfig(level1.UpgradeTo);
				IBuildingConfig level3 = tables.Buildings.GetBuildingConfig(level2.UpgradeTo);

				Check.Assert(level2 != null, $"{chain} 的 lv.II 应存在");
				Check.Assert(level3 != null, $"{chain} 的 lv.III 应存在");
				Check.AssertEqual("", level3.UpgradeTo, $"{chain} lv.III 应已是最高级（UpgradeTo 为空）");
				Check.Assert(level1.UpgradeDuration > 0f && level2.UpgradeDuration > 0f, $"{chain} 的升级耗时应 > 0");
				Check.Assert(level1.UpgradeTechRequirements.Count > 0, $"{chain} 的升级应有科技前置（设计稿「升级条件」列）");
				Check.Assert(level1.Actions.Contains("CanUpgrade"), $"{chain} lv.I 应声明 CanUpgrade");
			}

			// 数值取自设计稿（buildings.md）：营地人口 9（半径1/300日）→ 18（1/240）→ 36（2/180）
			IBuildingConfig camp1 = tables.Buildings.GetBuildingConfig("camp");
			IBuildingConfig camp2 = tables.Buildings.GetBuildingConfig("camp_ii");
			IBuildingConfig camp3 = tables.Buildings.GetBuildingConfig("camp_iii");
			Check.AssertEqual(9, camp1.PopulationCap, "营地 lv.I 上限");
			Check.AssertEqual(18, camp2.PopulationCap, "营地 lv.II 上限（设计稿 18）");
			Check.AssertEqual(36, camp3.PopulationCap, "营地 lv.III 上限（设计稿 36）");
			Check.AssertEqual(240, camp2.PopulationGrowthInterval, "营地 lv.II 人口间隔（设计 240 日）");
			Check.AssertEqual(180, camp3.PopulationGrowthInterval, "营地 lv.III 人口间隔（设计 180 日）");
			Check.AssertEqual(2, camp3.PopulationRadius, "营地 lv.III 人口半径（设计 2 格）");

			// 学院产出链：250 → 1000 → 3000 idea/月（设计稿）
			Check.AssertEqual(250f, tables.Buildings.GetBuildingConfig("school").Modifiers.Single().Value, "学院 lv.I 产出");
			Check.AssertEqual(1000f, tables.Buildings.GetBuildingConfig("school_ii").Modifiers.Single().Value, "学院 lv.II 产出（设计 1000）");
			Check.AssertEqual(3000f, tables.Buildings.GetBuildingConfig("school_iii").Modifiers.Single().Value, "学院 lv.III 产出（设计 3000）");

			// 训练速度链（设计：lv.II +20% / lv.III +50%）
			Check.AssertEqual(0.2f, tables.Buildings.GetBuildingConfig("workshop_ii").Modifiers.Single().Value, "工坊 lv.II 训练速度 +20%");
			Check.AssertEqual(0.5f, tables.Buildings.GetBuildingConfig("military_camp_iii").Modifiers.Single().Value, "军营 lv.III 训练速度 +50%");
		}

		private static void UpgradeGatesRejectInvalid()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker();
				h.BuildAndFinish("camp");

				// ① 科技未解锁（camp lv.I 的升级前置 = math/basic_geometry）
				Check.Assert(!h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, h.CampUid, "CanUpgrade"),
					"科技未解锁时不得升级");
				Check.Assert(h.Map.GetOccupantInfo(MapId, h.Site).Value.Id == "camp", "被拒后建筑仍是 lv.I");
				Check.Assert(worker.IsIdle, "被拒后工人仍空闲");

				// 只解锁 lv.I → lv.II 所需的那一条（基础几何 ← 测量 ← 计数；初步测量仍锁着，用于验证 lv.II→III 的门控）
				h.Research("math", "counting");
				h.Research("math", "arithmetic");
				h.Research("math", "measurement");
				h.Research("math", "basic_geometry");
			
				// ② 资源不足
				h.Drain("BasicMinerals");
				Check.Assert(!h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, h.CampUid, "CanUpgrade"), "资源不足时不得升级");

				// ③ 成功一次 → 到 lv.II（lv.II 的升级前置是 physics/simple_machine_intuition，未解锁）
				h.Resources.AddResource("BasicMinerals", 500f, MapId, h.OwnerId);
				h.Resources.AddResource("Food", 200f, MapId, h.OwnerId);
				Check.Assert(h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, h.CampUid, "CanUpgrade"), "前置与资源齐备后应能升级");
				h.Clock.AdvanceDays(2); // 快配置：升级 2 日

				Check.AssertEqual("camp_ii", h.Map.GetOccupantInfo(MapId, h.Site).Value.Id, "应升到 lv.II");
				Check.Assert(!h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, h.CampUid, "CanUpgrade"),
					"lv.II → lv.III 的科技前置未解锁时不得升级");

				// ④ 最高级不得升级：直接造 lv.III 的建筑（camp_iii 无 UpgradeTo）
				HexCubePosition top = h.FreeSite();
				h.BuildAndFinish("camp_iii", top);
				string topUid = h.Map.GetOccupantInfo(MapId, top).Value.UId;
				Check.Assert(!h.Construction.StartUpgrade(MapId, topUid, h.OwnerId), "最高等级建筑不得再升级");

				// ⑤ 未完工不得升级
				HexCubePosition pending = h.FreeSite();
				h.Resources.AddResource("BasicMinerals", 500f, MapId, h.OwnerId);
				h.Construction.StartConstruction(MapId, "camp", pending, h.OwnerId);
				string pendingUid = h.Map.GetOccupantInfo(MapId, pending).Value.UId;
				Check.Assert(!h.Construction.StartUpgrade(MapId, pendingUid, h.OwnerId), "施工中的建筑不得升级");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void UpgradeSwapsConfigKeepingUId()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker();
				h.BuildAndFinish("camp");
				string uidBefore = h.CampUid;

				h.UnlockUpgradeTech();
				Check.Assert(h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, h.CampUid, "CanUpgrade"), "应能开工升级");
				Check.Assert(!worker.IsIdle, "升级中工人应忙碌");

				// 升级期间建筑不可用（IsReady=false）→ 训练/研究门控自动失效
				Check.Assert(!h.Map.GetOccupantInfo(MapId, h.Site).Value.IsReady, "升级中建筑应未就绪（`CON-09` 的最小「升级中」语义）");
				Check.Assert(h.Tasks.GetCurrentTasks(MapId).Any(t => t.Type == "Upgrade"), "应产生升级任务");

				h.Clock.AdvanceDays(2); // 快配置：升级 2 日

				MapOccupantInfo info = h.Map.GetOccupantInfo(MapId, h.Site).Value;
				Check.AssertEqual("camp_ii", info.Id, "升级后建筑 Id 应换成 lv.II 配置");
				Check.AssertEqual("营地 lv.II", info.Name, "升级后名称应换成 lv.II");
				Check.AssertEqual(uidBefore, info.UId, "**uid 不变**（修正器/迷雾/任务都以 uid 为锚）");
				Check.Assert(info.IsReady, "升级完成后建筑就绪");
				Check.Assert(worker.IsIdle, "升级完成后建造者释放");
				Check.AssertEqual(0, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Upgrade"), "升级任务应被回收");
				Check.AssertEqual(18, h.Tables.Buildings.GetBuildingConfig("camp_ii").PopulationCap, "lv.II 的人口上限应生效（18）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void UpgradeReplacesModifiers()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker();
				h.Resources.AddResource("Food", 500f, MapId, h.OwnerId);
				h.Resources.AddResource("Idea", 0f, MapId, h.OwnerId);
				h.BuildAndFinish("school");

				h.UnlockUpgradeTech();
				// (Idea 基线在升级前不取：月结断言与相位无关，见下)

				Check.Assert(h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, h.SchoolUid, "CanUpgrade"), "学院应能升级到 lv.II");
				h.Clock.AdvanceDays(2);

				// 完工后归零 Idea 再跑两个月：断言只与**月结**有关（与月相位无关）。
				// 若旧等级的 250 没被回收，月结会变成 1250/1625 —— 该断言同时锁住"换级不叠加"与"科技修正器仍在"。
				h.Drain("Idea");
				h.Clock.AdvanceDays(60);
				float gained = h.Idea();
				float months = Math.Max(1f, (float)Math.Round(gained / 1300f));
				float perMonth = gained / months;
				Check.Assert(perMonth >= 995f && perMonth <= 1305f,
					$"升级后月结应在 1000~1300（lv.II 的 1000，含 0~30% 科技加成）—— 250+1000 叠加会变成 1250/1625，实际每月 {perMonth}（gained={gained}）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void UpgradeReplacesHousingTask()
		{
			Harness h = NewHarness();
			try
			{
				Unit worker = h.SpawnWorker();
				h.BuildAndFinish("camp");

				List<TaskSnapshot> housing = h.Tasks.GetCurrentTasks(MapId).Where(t => t.Type == "PopulationGrowth").ToList();
				Check.AssertEqual(1, housing.Count, "升级前应恰好一条人口任务");
				Check.AssertEqual(300f, housing[0].Target, "升级前的间隔（300 日）");

				h.UnlockUpgradeTech();
				Check.Assert(h.Units.ExcuteAction(MapId, worker.GetInfo().UId, h.Site, h.CampUid, "CanUpgrade"), "营地应能升级");
				h.Clock.AdvanceDays(2);

				housing = h.Tasks.GetCurrentTasks(MapId).Where(t => t.Type == "PopulationGrowth").ToList();
				Check.AssertEqual(1, housing.Count, "升级后仍应**只有一条**人口任务（旧任务按 uid 范围注销）");
				Check.AssertEqual(240f, housing[0].Target, "升级后间隔应换成 lv.II 的 240 日");
				Check.AssertEqual(h.CampUid, housing[0].Id, "人口任务仍挂在同一个建筑 uid 上");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void ResumePathSharesCompletion()
		{
			Harness h = NewHarness();
			try
			{
				// 模拟读档：先有一栋"施工中"的建筑（`WP-3.2` 才做实体持久化，这里手工放置等价现场）
				HexCubePosition site = h.FreeSite();
				Building building = h.Factory.CreateBuilding("camp", site, h.OwnerId);
				h.Map.SetOccupant(MapId, site, building);

				var snapshot = new TaskSnapshot
				{
					MapId = MapId,
					OwnerId = h.OwnerId,
					Progress = 1f,
					Target = 2f,
					Id = "camp",
					Type = "Construction",
					UId = building.GetInfo().UId,
					IsCompleted = false,
				};

				LinearTask resumed = h.Construction.ResumeConstruction(MapId, snapshot);
				h.Time.Register(resumed);

				Check.Assert(!h.Map.GetOccupantInfo(MapId, site).Value.IsReady, "续跑开始时不应就绪");
				Check.Assert(h.Fog.GetVisibility(site) != FogAppService.Visible, "续跑开始时不应有视野（旧实现续跑完成后也永远没有）");

				h.Clock.AdvanceDays(1); // 第 2 日完成

				Check.Assert(h.Map.GetOccupantInfo(MapId, site).Value.IsReady, "续跑完成后应就绪");
				Check.Assert(h.Fog.GetVisibility(site) == FogAppService.Visible, "续跑完成也应开视野（`CON-03` 的修复点）");
				foreach (HexCubePosition pos in site.InRadius(1))
					Check.Assert(h.Fog.GetVisibility(pos) == FogAppService.Visible, $"续跑完成应开半径 1 的视野：{pos.q},{pos.r}");
				Check.Assert(h.Tasks.GetCurrentTasks(MapId).Any(t => t.Type == "PopulationGrowth" && t.Id == building.GetInfo().UId),
					"续跑完成也应注册人口任务（`CON-03` 的另一个修复点）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void ValidatorGuardsUpgradeChain()
		{
			// ① 悬空 UpgradeTo → error
			ConfigReport report = BuildReport(b => b["camp"]["UpgradeTo"] = "no_such_building");
			Check.Assert(report.HasErrors, "悬空 UpgradeTo 应判 error");

			// ② 自环 → error
			report = BuildReport(b => b["camp"]["UpgradeTo"] = "camp");
			Check.Assert(report.HasErrors, "UpgradeTo 自环应判 error");

			// ③ 有 UpgradeTo 但 UpgradeDuration=0 → warning（不阻断启动）
			report = BuildReport(b => b["camp"]["UpgradeDuration"] = 0);
			Check.Assert(!report.HasErrors, "缺 UpgradeDuration 只应 warning，不阻断启动");
			Check.Assert(report.Issues.Any(i => i.ToString().Contains("UpgradeDuration")), "应给出 UpgradeDuration 的 warning");

			// ④ 升级消耗引用未填写的资源 → warning（与 ResourceCost 同口径：原型表可能引用尚未填写的资源）
			report = BuildReport(b => b["camp"]["UpgradeCost"] = new JObject { ["Unobtainium"] = 10 });
			Check.Assert(!report.HasErrors, "未知资源的升级消耗只应 warning（与 ResourceCost 同口径）");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Warning && i.ToString().Contains("Unobtainium")),
				"应给出未知资源的 warning");

			// ⑤ 负值消耗 → error
			report = BuildReport(b => b["camp"]["UpgradeCost"] = new JObject { ["BasicMinerals"] = -5 });
			Check.Assert(report.HasErrors, "负的升级消耗应判 error");
		}

		/// <summary>读真实建筑表 → 改一处 → 装配一遍，返回校验报告（容错模式，便于断言 error/warning 分级）。</summary>
		private static ConfigReport BuildReport(Action<JObject> mutate)
		{
			var root = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			mutate((JObject)root["Buildings"]);
			return ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith("Buildings", root.ToString()),
				failOnConfigErrors: false).ConfigReport;
		}


		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public GameClock Clock;
			public GameTimeService Time;
			public TaskRepository Tasks;
			public MapSession MapSession;
			public MapAppService Map;
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public ConstructionAppService Construction;
			public UnitsAppService Units;
			public TechTreesAppService Tech;
			public FogAppService Fog;
			public BuildingFactory Factory;
			public ConfigTables Tables;
			public HexCubePosition Spawn;
			public HexCubePosition Site;
			public string CampUid;
			public string SchoolUid;

			/// <summary>在指定格（默认 <see cref="Site"/>）直接建好一栋建筑并推进到完工。</summary>
			public string BuildAndFinish(string buildingId, HexCubePosition? position = null)
			{
				HexCubePosition site = position ?? Site;
				Resources.AddResource("BasicMinerals", 500f, MapId, OwnerId);
				Resources.AddResource("Food", 500f, MapId, OwnerId);
				Construction.StartConstruction(MapId, buildingId, site, OwnerId);
				Clock.AdvanceDays(2); // 快配置：建造 2 日

				string uid = Map.GetOccupantInfo(MapId, site).Value.UId;
				if (buildingId == "camp") CampUid = uid;
				if (buildingId == "school") SchoolUid = uid;
				return uid;
			}

			/// <summary>生成一个已就绪的工人（建造者）：落点取**工地（<see cref="Site"/>）相邻的空格**，便于建造/升级。</summary>
			public Unit SpawnWorker()
			{
				MapCell cell = Map.GetAllCells(MapId).First(c => Map.IsClear(MapId, c.Position)
					&& c.Position.DistenceTo(Site) <= 1);

				Map.AddPopulation(MapId, cell.Position, 0, 9, 1);
				Resources.AddResource("Food", 100f, MapId, OwnerId);
				Units.CreateUnit(MapId, "worker", cell.Position, OwnerId);
				Clock.AdvanceDays(3); // 快配置：训练 3 日

				string uid = Map.GetOccupantInfo(MapId, cell.Position).Value.UId;
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}

			/// <summary>
			/// 解锁升级链所需的科技（设计稿映射见 `Config/Buildings.json` 的 `UpgradeTechRequirements`）：
			/// 数学树：计数 → 算术/测量 → 基础几何 → 记数系统 → 初步测量 → 毕达哥拉斯学派 → 几何原本；物理树：简单机械直觉 → 杠杆平衡。
			/// </summary>
			public void UnlockUpgradeTech()
			{
				Resources.AddResource("Idea", 20000f, MapId, OwnerId);
				// 前置链必须自身成立：counting → arithmetic/measurement → basic_geometry → numeral_system → preliminary_survey → pythagorean_school → elements
				Research("math", "counting");
				Research("math", "arithmetic");
				Research("math", "measurement");
				Research("math", "basic_geometry");
				Research("math", "numeral_system");
				Research("math", "preliminary_survey");
				Research("math", "pythagorean_school");
				Research("math", "elements");
				Research("physics", "simple_machine_intuition");
				Research("physics", "lever_balance");
			}

			/// <summary>研究一个科技节点（升级前置用；直接推进足够天数）。前置未满足时本方法无效。</summary>
			public void Research(string treeId, string nodeId)
			{
				Resources.AddResource("Idea", 500f, MapId, OwnerId);
				Tech.Research(MapId, OwnerId, treeId, nodeId);
				Clock.AdvanceDays(400); // 覆盖最长节点（几何原本 180 日），保证前置链真的研究完
			}

			/// <summary>
			/// 把某种资源清空（制造"资源不足"）。**必须走 <c>AddResource</c> 写回仓储** ——
			/// 直接改 <c>ResourcesPool</c> 不落盘（`D25`），下次读盘就"回滚"，造不出资源不足的场景。
			/// </summary>
			public void Drain(string resource)
			{
				float current = Resources.GetOrCreatePool(MapId, OwnerId).GetValue(resource);
				Resources.AddResource(resource, -current, MapId, OwnerId);
			}

			/// <summary>内部区域里任取一个空格（用于多建筑场景）。</summary>
			public HexCubePosition FreeSite()
				=> Map.GetAllCells(MapId).First(c => Map.IsClear(MapId, c.Position)
					&& c.Position.q is >= 2 and <= 5 && c.Position.r is >= 2 and <= 5).Position;

			public float Idea() => Resources.GetOrCreatePool(MapId, OwnerId).GetValue("Idea");
		}

		/// <summary>快配置：建筑/单位/升级时长压到 2~3 日（其余字段与真实表一致，含升级链与数值）。</summary>
		private static InMemoryConfigSource FastConfigSource()
		{
			var buildings = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (JProperty entry in ((JObject)buildings["Buildings"]).Properties())
			{
				entry.Value["Duration"] = 2;
				entry.Value["UpgradeDuration"] = 2;
			}

			var units = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));
			foreach (JProperty entry in ((JObject)units["Units"]).Properties())
				entry.Value["Duration"] = 3;

			InMemoryConfigSource source = ConfigFixtures.RealConfigSourceWith("Buildings", buildings.ToString());
			source.Inject("Units", units.ToString());
			return source;
		}

		private static Harness NewHarness(int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp26-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			CoreServices core = ConfigFixtures.BuildCore(FastConfigSource());
			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue }; // `D28`
			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"));
			var time = new GameTimeService(clock, tasks);

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
				time);
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_")));

			var session = new MapSession(new InMemoryMapRepository());
			var map = new MapAppService(core.MapGenerator, session);
			var factory = new BuildingFactory(core.Tables.Buildings);

			var construction = new ConstructionAppService(
				map, resources, tech, factory, core.Tables.Buildings, time, modifier, fog);
			var units = new UnitsAppService(
				map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings);

			map.GenerateMap(20260914, 8, 8, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				if (cell.Position.q is >= 2 and <= 6 && cell.Position.r is >= 2 and <= 6)
					map.SetTerrain(MapId, cell.Position, plain);

			MapCell anchor = map.GetAllCells(MapId).First(c => map.IsClear(MapId, c.Position)
				&& c.Position.q == 3 && c.Position.r == 3);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = clock,
				Time = time,
				Tasks = tasks,
				MapSession = session,
				Map = map,
				Resources = resources,
				Modifier = modifier,
				Construction = construction,
				Units = units,
				Tech = tech,
				Fog = fog,
				Factory = factory,
				Tables = core.Tables,
				Spawn = anchor.Position,
				Site = new HexCubePosition(4, 4),
			};
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
