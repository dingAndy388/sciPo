using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Resources.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-1.4）7 张配置表通电与启动期校验的验收检查。
	/// <para>正向：真实 <c>Config/*.json</c> 全部装载成功、装配出 7 张表、**0 error**（M0-1 ② 的门槛）。</para>
	/// <para>反向：逐条注入"填表最容易犯的错"，确认校验器真的能抓住（error 阻断 / warning 放行）。</para>
	/// </summary>
	internal static class ConfigTableChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-1.4 配置表：7 张 JSON 表齐全且非空", AllTablesPresent);
			Check.Run("WP-1.4 装配：7 张表全部通电（LoadedCount=7）", AllTablesWired);
			Check.Run("WP-1.4 校验：真实配置 0 error（M0-1 ② 门槛）", RealConfigHasNoError);
			Check.Run("WP-1.4 校验：真实配置的 Modifier Target 全在登记表内（MOD-05 卫生）", RealTargetsRegistered);
			Check.Run("WP-1.4 校验：非法 Modifier.Type 判 error（静默降级保护）", IllegalModifierTypeIsError);
			Check.Run("WP-1.4 校验：科技前置同表闭合（缺失/自环/成环均判 error）", PrerequisiteClosureAndCycles);
			Check.Run("WP-1.4 校验：字典 key 与条目 Id 不一致判 error", KeyIdMismatchIsError);
			Check.Run("WP-1.4 校验：空表 / 缺表判 error", EmptyAndMissingTableAreErrors);
			Check.Run("WP-1.4 校验：跨表引用——未知科技树 error / 未知节点 warning", CrossTableTechReferences);
			Check.Run("WP-1.4 校验：事件表根对象必须是 Events 数组（裸数组被拒绝）", EventsRootObjectRequired);
			Check.Run("WP-1.4 放行：FailOnConfigErrors=false 时带 error 仍可装配并取到报告", ReportWithoutThrowing);
			Check.Run("WP-1.4 全表视图：GetAll/GetTreeIds 与 JSON 条目数一致", EnumerationViewsMatchJson);
			Check.Run("WP-1.4 报告：问题行包含表名与等级（可直接贴给填表者）", ReportIsReadable);
			Check.Run("WP-7.1 资源口径：设计稿的 3 种资源（名/初始储备/上限/修正器目标）", DesignResourceIds);
			Check.Run("WP-7.1 资源口径：全表已无旧别名（Gold/Wood 残留 = 改了一半）", NoLegacyResourceAliases);
		}

		// ────────────────────────── 正向 ──────────────────────────

		/// <summary>
		/// （v0.6.3 / WP-7.1）**资源口径 = 设计稿**：名 / 初始储备 / 上限 / 修正器目标名。
		/// <para>数值取自 `design/resources.md` 的资源总表（Idea 500/10000 · Food 300/2000 · BasicMinerals 200/1500）。
		/// 这条断言的价值是"把设计稿的表格钉在代码里"：数值被悄悄改掉时立刻红。</para>
		/// </summary>
		private static void DesignResourceIds()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			var expected = new (string Name, float Initial, float Limit, string Growth)[]
			{
				("Idea", 500f, 10000f, "IdeaGrowth"),
				("Food", 300f, 2000f, "FoodGrowth"),
				("BasicMinerals", 800f, 1500f, "MineralGrowth"),
			};

			List<IResourceConfig> resources = core.Tables.AllResources().ToList();
			Check.AssertEqual(expected.Length, resources.Count, "资源种类数（设计稿 3 种）");

			foreach ((string name, float initial, float limit, string growth) in expected)
			{
				IResourceConfig resource = resources.FirstOrDefault(r => r.Name == name);
				Check.Assert(resource != null, $"设计稿的资源「{name}」应在表里");
				Check.AssertEqual(initial, resource.BaseValue, $"{name} 初始储备（设计稿 200 → 用户裁定 800，`D91`）");
				Check.AssertEqual(limit, resource.BaseLimit, $"{name} 存储上限（`resources.md`）");
				Check.Assert(resource.DependentModifiers != null && resource.DependentModifiers.Contains(growth),
					$"{name} 的产出修正器目标应为 {growth}");
			}

			// 人口口粮走 Food（设计稿：人口 × 3/月）
			IResourcesPoolConfig poolConfig = core.Tables.Resources.GetResourcesPoolConfig();
			ISettlementConfig settlement = poolConfig?.Settlement;
			Check.Assert(settlement != null, "Resources 表应含 Settlement 段");
			Check.AssertEqual("Food", settlement.DemandResource, "Settlement.DemandResource");
			Check.AssertEqual(3f, settlement.PopulationUpkeepPerMonth, "人口维护率（设计稿 3/月）");
		}

		/// <summary>
		/// （v0.6.3 / WP-7.1）**旧别名必须绝迹**：全表（资源/建筑/单位/事件）里不允许再出现 `Gold` / `Wood`。
		/// <para>为什么单独查一条：改名改一半的表（比如建筑造价还是 Wood、资源表已经没有 Wood）不会报 error ——
		/// 它只会让"某个建筑永远造不了"（费用校验失败），这类缺陷找起来很贵。</para>
		/// </summary>
		private static void NoLegacyResourceAliases()
		{
			string[] legacy = { "Gold", "Wood", "GoldGrowth", "WoodGrowth" };
			CoreServices core = ConfigFixtures.BuildRealCore();

			foreach (string alias in legacy)
			{
				Check.Assert(!core.Tables.AllResources().Any(r => r.Name == alias), $"资源表不应再有「{alias}」");
				Check.Assert(!core.Tables.AllBuildings().Any(b => UsesAlias(b.ResourceCost, b.Maintenance, alias)),
					$"建筑表不应再引用「{alias}」");
				Check.Assert(!core.Tables.AllUnits().Any(u => UsesAlias(u.ResourceCost, u.Maintenance, alias)),
					$"单位表不应再引用「{alias}」");
			}
		}

		/// <summary>某条配置（建筑/单位）的费用或维护费里是否还引用旧资源别名。</summary>
		private static bool UsesAlias(IDictionary<string, float> cost, IDictionary<string, float> maintenance, string alias)
			=> (cost != null && cost.ContainsKey(alias)) || (maintenance != null && maintenance.ContainsKey(alias));

		private static void AllTablesPresent()
		{
			foreach (string name in ConfigFixtures.TableNames)
			{
				string path = ConfigFixtures.TablePath(name);
				Check.Assert(File.Exists(path), $"缺少配置表文件：Config/{name}.json");
				Check.Assert(File.ReadAllText(path).Trim().Length > 0, $"配置表为空文件：Config/{name}.json");
			}
		}

		private static void AllTablesWired()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			ConfigTables tables = core.Tables;

			Check.Assert(tables != null, "应装配出 ConfigTables");
			Check.AssertEqual(ConfigFixtures.TableNames.Length, tables.LoadedCount, "已装载的表数量");
			Check.AssertEqual(ConfigTables.Names.Count, ConfigFixtures.TableNames.Length, "登记的表名数量");

			foreach (string name in ConfigTables.Names)
				Check.Assert(tables.IsLoaded(name), $"{name} 表未通电（IsLoaded=false）");
		}

		private static void RealConfigHasNoError()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			ConfigReport report = core.ConfigReport;

			if (report.Issues.Count > 0) Console.WriteLine($"       {report.Summary()}\n{report.ToLines()}");

			Check.Assert(!report.HasErrors, $"真实配置不应有 error（M0-1 ② 要求），实际：{report.Summary()}");
			Check.AssertEqual(0, report.ErrorCount, "error 数量");
		}

		private static void RealTargetsRegistered()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			List<ConfigIssue> unregistered = core.ConfigReport.Issues
				.Where(i => i.Message.Contains("不在 Target 登记表中"))
				.ToList();

			Check.Assert(unregistered.Count == 0,
				"真实配置里所有 Modifier.Target 都应能被登记表解析（含资源派生 FoodGrowth/MineralGrowth 与单位派生 swordsmanAttack/swordsmanHP）：" +
				string.Join(" | ", unregistered.Select(i => i.ToString())));
		}

		// ────────────────────────── 反向 ──────────────────────────

		private static void IllegalModifierTypeIsError()
		{
			// ModifierAppService 只把 "Percent" 认成 Percentage，其余拼写都会被静默当成 Absolute（MOD-05）
			ConfigReport report = ReportFor("Events", """
			{
			  "Events": [
			    {
			      "EventId": "typo_event",
			      "Name": "拼错的类型",
			      "TriggerChancePerDay": 0.01,
			      "Duration": 5,
			      "Modifiers": [ { "Target": "FoodGrowth", "Type": "Percentt", "Value": 0.5 } ],
			      "ResourcePrerequisites": {},
			      "TechPrerequisites": {}
			    }
			  ]
			}
			""");

			Check.Assert(report.HasErrors, "非法 Modifier.Type 应判 error");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("Percentt")),
				"错误信息应指出具体的非法字面量");
		}

		private static void PrerequisiteClosureAndCycles()
		{
			ConfigReport report = ReportFor("TechTrees", """
			{
			  "TechTrees": {
			    "t": {
			      "Techs": {
			        "root":  { "Id": "root",  "Prerequisites": [],          "Cost": 1, "Duration": 1, "Modifiers": [] },
			        "ghost": { "Id": "ghost", "Prerequisites": ["missing"], "Cost": 1, "Duration": 1, "Modifiers": [] },
			        "self":  { "Id": "self",  "Prerequisites": ["self"],    "Cost": 1, "Duration": 1, "Modifiers": [] },
			        "b":     { "Id": "b",     "Prerequisites": ["c"],       "Cost": 1, "Duration": 1, "Modifiers": [] },
			        "c":     { "Id": "c",     "Prerequisites": ["b"],       "Cost": 1, "Duration": 1, "Modifiers": [] }
			      }
			    }
			  }
			}
			""");

			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("不在同一棵树内")),
				"同表内缺失前置应判 error");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("自环")),
				"自环前置应判 error");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("成环")),
				"前置成环应判 error");
		}

		private static void KeyIdMismatchIsError()
		{
			ConfigReport report = ReportFor("Buildings", """
			{
			  "Buildings": {
			    "house": {
			      "BuildingId": "hovel",
			      "Name": "民居",
			      "ResourceCost": { "BasicMinerals": 20 },
			      "TerrainRequirements": ["plain"],
			      "TechRequirements": {},
			      "Modifiers": [],
			      "Duration": 10,
			      "Actions": [],
			      "VisionRadius": 1,
			      "IsHousing": true,
			      "PopulationRadius": 3,
			      "PopulationCap": 5,
			      "PopulationGrowthInterval": 15
			    }
			  }
			}
			""");

			Check.Assert(report.HasErrors, "字典 key 与 BuildingId 不一致应判 error");
			Check.Assert(report.Issues.Any(i => i.Message.Contains("不一致")), "错误信息应指出 key/Id 不一致");
		}

		private static void EmptyAndMissingTableAreErrors()
		{
			// 空表：根对象存在但没有任何条目
			ConfigReport empty = ReportFor("Buildings", @"{ ""Buildings"": {} }");
			Check.Assert(empty.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Table == "Buildings" && i.Message.Contains("表为空")),
				"空表应判 error");

			// 缺表：IConfigSource.LoadText 返回 null
			ConfigReport missing = ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith("Units", null), failOnConfigErrors: false).ConfigReport;
			Check.Assert(missing.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Table == "Units" && i.Message.Contains("缺少配置表")),
				"缺表应判 error");
		}

		private static void CrossTableTechReferences()
		{
			ConfigReport report = ReportFor("Buildings", """
			{
			  "Buildings": {
			    "lab": {
			      "BuildingId": "lab",
			      "Name": "实验室",
			      "ResourceCost": { "Food": 10 },
			      "TerrainRequirements": ["plain"],
			      "TechRequirements": { "no_such_tree": ["x"], "science": ["no_such_node"] },
			      "Modifiers": [],
			      "Duration": 10,
			      "Actions": [],
			      "VisionRadius": 0,
			      "IsHousing": false
			    }
			  }
			}
			""");

			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("不存在的科技树")),
				"引用不存在的科技树应判 error");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Warning && i.Message.Contains("no_such_node")),
				"引用树内不存在的节点应判 warning（跨表引用先降级）");
		}

		private static void EventsRootObjectRequired()
		{
			// 裸数组（旧 Document/EventsConfig.json 的形状）无法映射到 EventsConfigDto：
			// 必须报"解析失败"，而不是静默得到 0 条事件
			ConfigReport report = ReportFor("Events", """[ { "EventId": "bare", "TriggerChancePerDay": 0.1, "Duration": 1 } ]""");

			Check.Assert(report.HasErrors, "事件表写成裸数组应判 error");
			Check.Assert(report.Issues.Any(i => i.Table == "Events" && i.Message.Contains("解析失败")), "应报告 JSON 解析失败");
			Check.AssertEqual(3, ConfigFixtures.BuildRealCore().Tables.AllEvents().Count(), "真实事件表条目数");
		}

		private static void ReportWithoutThrowing()
		{
			CoreServices core = ConfigFixtures.BuildCore(new InMemoryConfigSource(), failOnConfigErrors: false);

			Check.Assert(core != null, "关闭快速失败后应仍能装配");
			Check.Assert(core.ConfigReport.HasErrors, "7 张表全缺时应积累 error");
			Check.AssertEqual(1, core.ConfigReport.ForTable("Terrains").Count(i => i.Level == ConfigIssueLevel.Error), "Terrains 的 error 数量");
		}

		private static void EnumerationViewsMatchJson()
		{
			ConfigTables tables = ConfigFixtures.BuildRealCore().Tables;

			Check.AssertEqual(5, tables.AllTerrains().Count(), "Terrains 条目数");
			Check.AssertEqual(3, tables.AllResources().Count(), "Resources 条目数");
			Check.AssertEqual(21, tables.AllBuildings().Count(), "Buildings 条目数（12 旧 + 农田/矿场/仓库各 3 级 = 21；24 条全表归 WP-7.2b）");
			Check.AssertEqual(8, tables.AllUnits().Count(), "Units 条目数（WP-3.8：3 条玩家单位 + 5 条敌方单位）");
			Check.AssertEqual(3, tables.TreeIds().Count(), "TechTrees 树数量（WP-2.1 起含最小 physics 样例）");
			Check.AssertEqual(3, tables.AllEvents().Count(), "Events 条目数");
			Check.AssertEqual(4f, tables.Generator.Density, "Generator.Density（来自 Config/Generator.json）");

			// 按 Id 检索仍然可用：新增的全表视图没有破坏原有单条查询
			Check.AssertEqual("营地", tables.Buildings.GetBuildingConfig("camp").Name, "camp 名称");
			Check.AssertEqual(60f, tables.Units.GetUnitConfig("archer").HP, "archer HP");
			Check.AssertEqual("buoyancy", tables.Terrains.GetById("water").UnlockTech, "water 的解锁科技");
			Check.Assert(tables.TechTrees.GetTechNodeConfig("science", "mathematics") != null, "science/mathematics 节点应可检索");
			Check.Assert(tables.TechTrees.GetTechNodeConfig("science", "counting") != null, "science/counting 节点应可检索（WP-2.1 新增）");
			Check.Assert(tables.TechTrees.GetTechNodeConfig("physics", "simple_machine_intuition") != null, "physics 树根节点应可检索（WP-2.1 新增）");
			Check.AssertEqual(20, tables.Events.GetAllEvents().First(e => e.EventId == "plague").Duration, "plague 持续天数");
		}

		private static void ReportIsReadable()
		{
			ConfigReport report = ReportFor("Terrains",
				@"{ ""Terrains"": [ { ""Id"": ""bad"", ""Name"": ""劣地"", ""Weight"": 0, ""MoveCost"": -1, ""Passable"": true } ] }");

			string line = report.Issues.First(i => i.Level == ConfigIssueLevel.Error).ToString();
			Check.Assert(line.StartsWith("[ERROR] Terrains/bad:"), $"报告行应形如 [ERROR] 表/Id: 说明，实际：{line}");
			Check.Assert(!string.IsNullOrWhiteSpace(report.Summary()), "摘要不应为空");
			Check.Assert(report.ToLines().Contains("Terrains"), "逐行输出应包含表名");
		}

		// ────────────────────────── 工具 ──────────────────────────

		/// <summary>用给定文本覆写真实配置中的某一张表，装配（不因 error 抛出）并取回校验报告。</summary>
		private static ConfigReport ReportFor(string tableName, string json)
			=> ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith(tableName, json), failOnConfigErrors: false).ConfigReport;
	}
}
