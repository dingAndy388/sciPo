using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
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
		}

		// ────────────────────────── 正向 ──────────────────────────

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
				"真实配置里所有 Modifier.Target 都应能被登记表解析（含资源派生 GoldGrowth/WoodGrowth 与单位派生 swordsmanAttack/swordsmanHP）：" +
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
			      "TriggerChance": 0.01,
			      "Duration": 5,
			      "Modifiers": [ { "Target": "GoldGrowth", "Type": "Percentt", "Value": 0.5 } ],
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
			      "ResourceCost": { "Wood": 20 },
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
			      "ResourceCost": { "Gold": 10 },
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
			ConfigReport report = ReportFor("Events", """[ { "EventId": "bare", "TriggerChance": 0.1, "Duration": 1 } ]""");

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
			Check.AssertEqual(3, tables.AllBuildings().Count(), "Buildings 条目数");
			Check.AssertEqual(3, tables.AllUnits().Count(), "Units 条目数");
			Check.AssertEqual(3, tables.TreeIds().Count(), "TechTrees 树数量（WP-2.1 起含最小 physics 样例）");
			Check.AssertEqual(3, tables.AllEvents().Count(), "Events 条目数");
			Check.AssertEqual(4f, tables.Generator.Density, "Generator.Density（来自 Config/Generator.json）");

			// 按 Id 检索仍然可用：新增的全表视图没有破坏原有单条查询
			Check.AssertEqual("民居", tables.Buildings.GetBuildingConfig("house").Name, "house 名称");
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
