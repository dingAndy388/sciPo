using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Domain;
using SciencePotato.Scripts.TechTree.Infrastructure;
using System;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-2.1）**跨树前置**（`TECH-07`）的验收检查。
	/// <para>正向：物理树根节点（`simple_machine_intuition`）在数学树的「计数」研究完成后**变为可研究**，
	/// 并在 30 日后真正研究完成 —— 这正是 **M0-2 ①** 的门槛。</para>
	/// <para>反向：跨树前置缺树 / 缺节点 / 成环都必须判 **error**（否则又会造出"永久不可解锁"的科技）。</para>
	/// </summary>
	internal static class TechPrerequisiteChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-2.1 兼容层：本树 / tree:node / 结构体三种前置写法都能解析", PrerequisiteFormsAreAccepted);
			Check.Run("WP-2.1 跨树门控：fail closed，且同树前置不走跨树查询", CrossTreeGateIsFailClosed);
			Check.Run("M0-2 ① 物理树根节点可研究（TECH-07 死锁解除）+ 跨存档仍成立", PhysicsRootBecomesResearchable);
			Check.Run("WP-2.1 配置增量：存档后新增的节点也能被 hydrate 出来", NewNodeAfterSaveIsHydrated);
			Check.Run("WP-2.1 校验：跨树前置缺树/缺节点/成环判 error，合法跨树不误报", CrossTreeValidation);
			Check.Run("WP-2.1 真实配置：warning 全部落在白名单内（0 error 仍成立）", RealConfigWarningsAreKnown);
		}

		// ────────────────────────── 兼容层 ──────────────────────────

		private static void PrerequisiteFormsAreAccepted()
		{
			// 旧表不破（§13.z WP-2.1 兼容层）：字符串 = 本树节点；"tree:node" = 跨树；结构体 = 跨树（§15 字段规格）
			var repo = new TechTreesConfigRepository("""
			{
			  "TechTrees": {
			    "t": { "Techs": {
			      "a": { "Id": "a", "Prerequisites": [],                     "Cost": 0, "Duration": 0, "Modifiers": [] },
			      "b": { "Id": "b", "Prerequisites": ["a"],                   "Cost": 1, "Duration": 1, "Modifiers": [] },
			      "c": { "Id": "c", "Prerequisites": ["other:root"],          "Cost": 1, "Duration": 1, "Modifiers": [] },
			      "d": { "Id": "d", "Prerequisites": [ { "TreeId": "other", "NodeId": "root" } ], "Cost": 1, "Duration": 1, "Modifiers": [] }
			    } }
			  }
			}
			""");

			TechPrerequisite inTree = repo.GetTechNodeConfig("t", "b").Prerequisites.Single();
			Check.Assert(!inTree.IsCrossTree, "字符串 \"a\" 应解释为本树前置");
			Check.AssertEqual("a", inTree.NodeId, "本树前置的节点 Id");
			Check.AssertEqual("a", inTree.ToString(), "本树前置的规范写法");

			TechPrerequisite compact = repo.GetTechNodeConfig("t", "c").Prerequisites.Single();
			Check.Assert(compact.IsCrossTree, "\"other:root\" 应解释为跨树前置");
			Check.AssertEqual("other", compact.TreeId, "紧凑写法的树 Id");
			Check.AssertEqual("root", compact.NodeId, "紧凑写法的节点 Id");
			Check.AssertEqual("other:root", compact.ToString(), "跨树前置的规范写法");

			TechPrerequisite structured = repo.GetTechNodeConfig("t", "d").Prerequisites.Single();
			Check.Assert(structured.IsCrossTree, "结构体写法应解释为跨树前置");
			Check.AssertEqual("other:root", structured.ToString(), "结构体写法与紧凑写法应等价");

			// 写出口径：跨树前置序列化为 "treeId:nodeId"（表现层/调试输出读得懂）
			Check.AssertEqual("\"other:root\"", JsonConvert.SerializeObject(compact), "跨树前置的 JSON 写出口径");
		}

		// ────────────────────────── 领域层门控 ──────────────────────────

		private static void CrossTreeGateIsFailClosed()
		{
			ITechTreesConfigRepository config = ConfigFixtures.BuildRealCore().Tables.TechTrees;
			var science = new TechTree("science", 1, config.GetTechTreeConfig("science"));
			var physics = new TechTree("physics", 1, config.GetTechTreeConfig("physics"));

			TechPrerequisite prerequisite = physics.GetPrerequisites("simple_machine_intuition").Single();
			Check.Assert(prerequisite.IsCrossTree && prerequisite.TreeId == "science",
				"物理树根节点的前置应是跨树的 science 节点");

			// ① fail closed：没挂跨树解析器时跨树前置一律"未满足"（宁可暂时不可研究，也不能错误放行）
			Check.Assert(!physics.IsPrerequisiteMet(prerequisite), "未挂解析器时跨树前置应判未满足");
			Check.Assert(!physics.CanResearch("simple_machine_intuition"), "未挂解析器时物理树根节点不可研究");

			// ② 同树前置**不**走跨树查询：解析器一旦被调用就抛，用来证明它没被用上
			science.AttachResearchLookup((treeId, nodeId) => throw new Exception("同树前置不应调用跨树解析器"));
			Check.Assert(science.CanResearch("writing"), "writing 是根节点（无前置）→ 应可研究");
			Check.Assert(!science.CanResearch("mathematics"), "writing 未研究时 mathematics 不可研究（本树前置生效）");
			science.Research("writing");
			Check.Assert(science.CanResearch("mathematics"), "writing 研究后 mathematics 应可研究");

			// ③ 跨树查询：兄弟树未研究 → 不可研究
			physics.AttachResearchLookup((treeId, nodeId) => treeId == "science" && science.IsResearched(nodeId));
			Check.Assert(!physics.CanResearch("simple_machine_intuition"), "计数未研究时物理树根节点不可研究");

			// ④ 兄弟树研究完成 → 可研究、可研究完成（`TECH-07` 的核心断言）
			science.Research("counting");
			Check.Assert(science.IsResearched("counting"), "计数应研究完成（无前置、0 成本）");
			Check.Assert(physics.IsPrerequisiteMet(prerequisite), "跨树前置应变为已满足");
			Check.Assert(physics.CanResearch("simple_machine_intuition"), "跨树前置满足后物理树根节点应可研究");
			physics.Research("simple_machine_intuition");
			Check.Assert(physics.IsResearched("simple_machine_intuition"), "物理树根节点应能真正研究完成");
			Check.Assert(!physics.CanResearch("simple_machine_intuition"), "已研究的节点不应再次可研究");

			// ⑤ 跨树查询只认"目标树 + 目标节点"，不能因为树对了就放行任意节点
			physics.AttachResearchLookup((treeId, nodeId) => treeId == "science" && nodeId == "writing");
			Check.Assert(physics.IsPrerequisiteMet(TechPrerequisite.Cross("science", "writing")), "writing 已研究 → 应判满足");
			Check.Assert(!physics.IsPrerequisiteMet(TechPrerequisite.Cross("science", "no_such_node")),
				"目标树的未知节点应判未满足（不能因树名对就放行）");
			Check.Assert(physics.IsPrerequisiteMet(TechPrerequisite.InTree("simple_machine_intuition")), "本树前置仍查本树集合");
		}

		// ────────────────────────── M0-2 ①：应用服务端到端 ──────────────────────────

		/// <summary>
		/// **M0-2 ① 的门槛**：`Config/TechTrees.json` 的真实配置 + 真实仓储（临时目录）+ 真实时间总线，
		/// 走"能不能研究 → 研究计数 → 物理树根节点可研究 → 30 日后完成 → 重开存档仍成立"的完整链路。
		/// </summary>
		private static void PhysicsRootBecomesResearchable()
		{
			string directory = Path.Combine(Path.GetTempPath(), "sp-wp21-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);

			try
			{
				const string mapId = "test";
				CoreServices core = ConfigFixtures.BuildRealCore();
				var clock = new GameClock();
				var time = new GameTimeService(clock);
				var resources = new ResourcesAppService(
					new ResourcesRepository(Path.Combine(directory, "res_")),
					core.Tables.Resources,
					time,
					new ModifierRepository(Path.Combine(directory, "mod_")));
				var app = new TechTreesAppService(
					new TechTreesRepository(Path.Combine(directory, "tech_"), core.Tables.TechTrees),
					core.Tables.TechTrees,
					resources,
					new ModifierAppService(new ModifierRepository(Path.Combine(directory, "mod_"))),
					time);

				// ① 数学树未研究 → 物理树根节点不可研究（`TECH-07` 的原始症状）
				Check.Assert(!app.CanResearch(mapId, 1, "physics", "simple_machine_intuition"),
					"计数未研究时，物理树根节点不应可研究");

				// ② 研究「计数」（0 idea / 0 日）→ 1 个日节拍即完成
				app.Research(mapId, 1, "science", "counting");
				clock.AdvanceDays(1);
				Check.Assert(app.GetOrCreateTechTree(mapId, 1, "science").IsResearched("counting"), "计数应在 1 日内完成");

				// ③ 跨树前置满足 → 物理树根节点变为可研究（M0-2 ①）
				Check.Assert(app.CanResearch(mapId, 1, "physics", "simple_machine_intuition"),
					"计数研究后，物理树根节点应可研究（TECH-07 死锁解除）");

				// ④ 真正研究它：2000 idea / 30 日（设计值）
				resources.AddResource("Idea", 5000f, mapId, 1);
				app.Research(mapId, 1, "physics", "simple_machine_intuition");
				clock.AdvanceDays(30);
				Check.Assert(app.GetOrCreateTechTree(mapId, 1, "physics").IsResearched("simple_machine_intuition"),
					"30 日后物理树根节点应研究完成");

				// ⑤ 跨存档：新仓储实例（等价于"重开游戏"）从盘上读回，跨树前置链仍成立
				var reopened = new TechTreesAppService(
					new TechTreesRepository(Path.Combine(directory, "tech_"), core.Tables.TechTrees),
					core.Tables.TechTrees,
					resources,
					new ModifierAppService(new ModifierRepository(Path.Combine(directory, "mod_"))),
					time);
				Check.Assert(reopened.GetOrCreateTechTree(mapId, 1, "physics").IsResearched("simple_machine_intuition"),
					"存档重开后物理树根节点仍应是已研究");
				Check.Assert(!reopened.CanResearch(mapId, 1, "physics", "simple_machine_intuition"),
					"已研究的节点不应再次判定为可研究");

				// ⑥ 前置未满足时不得"先扣资源、再在完成回调里静默丢弃"：不注册研发任务
				int subscribersBefore = time.SubscriberCount;
				reopened.Research(mapId, 1, "science", "mathematics"); // 前置 writing 未研究
				Check.AssertEqual(subscribersBefore, time.SubscriberCount, "前置未满足时不应注册研发任务");
			}
			finally
			{
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		// ────────────────────────── 配置增量（HydrateConfigs） ──────────────────────────

		/// <summary>
		/// 先有存档、后往表里加节点：新节点必须能被 <c>HydrateConfigs</c> 带出来。
		/// <para>旧实现只 hydrate **已存在**的节点 → 新科技在旧存档里永远看不见（跨树前置正是"按表增量开放"的用法）。</para>
		/// </summary>
		private static void NewNodeAfterSaveIsHydrated()
		{
			string directory = Path.Combine(Path.GetTempPath(), "sp-wp21grow-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);

			try
			{
				const string mapId = "test";
				string filePrefix = Path.Combine(directory, "tech_");

				// 第一代配置：physics 只有根节点 → 建档落盘（模拟旧存档）
				ITechTreesConfigRepository firstConfig = ConfigFixtures.BuildRealCore().Tables.TechTrees;
				var first = new TechTreesRepository(filePrefix, firstConfig);
				first.SaveTree(mapId, 1, "physics", new TechTree("physics", 1, firstConfig.GetTechTreeConfig("physics")));

				// 第二代配置：表里新增 "wheel" ← "physics:simple_machine_intuition"（紧凑跨树写法）
				CoreServices grown = ConfigFixtures.BuildCore(
					RealConfigWithExtraPhysicsNode("wheel", "physics:simple_machine_intuition"));

				TechTree physics = new TechTreesRepository(filePrefix, grown.Tables.TechTrees).GetTreeById(mapId, 1, "physics");

				Check.Assert(physics != null, "旧存档应能读回");
				Check.Assert(physics.Nodes.ContainsKey("wheel"), "存档后新增的节点应被 HydrateConfigs 带出来");
				Check.Assert(physics.GetPrerequisites("wheel").Single().IsCrossTree, "新节点的前置应解析为跨树前置");
				Check.Assert(!physics.CanResearch("wheel"),
					"未挂跨树解析器时（fail closed）新节点不可研究 —— 应用层必须挂解析器");
			}
			finally
			{
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		/// <summary>把真实 TechTrees 表读成 JSON 对象，往 physics 树里塞一个节点，再作为配置源注入。</summary>
		private static InMemoryConfigSource RealConfigWithExtraPhysicsNode(string nodeId, string prerequisite)
		{
			var root = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("TechTrees")));
			var techs = (JObject)root["TechTrees"]["physics"]["Techs"];
			techs[nodeId] = new JObject(
				new JProperty("Id", nodeId),
				new JProperty("Prerequisites", new JArray(prerequisite)),
				new JProperty("Cost", 10),
				new JProperty("Duration", 5),
				new JProperty("Modifiers", new JArray()));

			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			source.Inject("TechTrees", root.ToString());
			return source;
		}

		// ────────────────────────── 校验器（反向） ──────────────────────────

		private static void CrossTreeValidation()
		{
			// 逐条注入"跨树前置最容易填错的样子"：缺树 / 缺节点 / 空 Id / 跨树成环
			ConfigReport report = ReportFor("TechTrees", """
			{
			  "TechTrees": {
			    "a": { "Techs": {
			      "root": { "Id": "root", "Prerequisites": [],         "Cost": 1, "Duration": 1, "Modifiers": [] },
			      "loop": { "Id": "loop", "Prerequisites": ["b:root"], "Cost": 1, "Duration": 1, "Modifiers": [] }
			    } },
			    "b": { "Techs": {
			      "root":     { "Id": "root",     "Prerequisites": ["a:loop"], "Cost": 1, "Duration": 1, "Modifiers": [] },
			      "ok_cross": { "Id": "ok_cross", "Prerequisites": ["a:root"], "Cost": 1, "Duration": 1, "Modifiers": [] }
			    } },
			    "c": { "Techs": {
			      "root": { "Id": "root", "Prerequisites": [ { "TreeId": "b", "NodeId": "missing" } ], "Cost": 1, "Duration": 1, "Modifiers": [] }
			    } },
			    "d": { "Techs": {
			      "root": { "Id": "root", "Prerequisites": ["nope:root"], "Cost": 1, "Duration": 1, "Modifiers": [] }
			    } },
			    "e": { "Techs": {
			      "root": { "Id": "root", "Prerequisites": [""], "Cost": 1, "Duration": 1, "Modifiers": [] }
			    } }
			  }
			}
			""");

			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("不存在的科技树")),
				"跨树前置引用不存在的科技树应判 error");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("b:missing")),
				"跨树前置引用目标树中不存在的节点应判 error（否则该科技永久不可解锁）");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("空节点 Id")),
				"空前置 Id 应判 error");
			Check.Assert(report.Issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.Contains("成环")),
				"跨树前置成环应判 error（逐树 DFS 抓不到这种环）");

			// 合法跨树前置（b/ok_cross ← a:root）不得报任何问题
			Check.Assert(!report.Issues.Any(i => i.Id == "b/ok_cross"),
				"合法跨树前置不应产生任何问题：" + string.Join(" | ", report.Issues.Select(i => i.ToString())));
		}

		/// <summary>
		/// 真实配置的 warning **白名单**：`WP-2.1` 起 <c>science/counting</c> 按设计就是 0 日（瞬间完成），
		/// 因此真实配置第一次出现 1 条预期 warning。用"允许清单 + 非空转断言"保住原来的 canary
		/// （任何**其它** warning 都算回归）。
		/// </summary>
		private static void RealConfigWarningsAreKnown()
		{
			ConfigReport report = ConfigFixtures.BuildRealCore().ConfigReport;
			var warnings = report.Issues.Where(i => i.Level == ConfigIssueLevel.Warning).ToList();

			Check.Assert(!report.HasErrors, $"真实配置不应有 error，实际：{report.Summary()}");
			Check.Assert(warnings.All(w => w.Message.Contains("Duration=0")),
				"真实配置只允许\"设计即 0 日\"的预期 warning：" +
				string.Join(" | ", warnings.Select(w => w.ToString())));
			Check.Assert(warnings.Any(w => w.Id == "science/counting"),
				"science/counting 的 Duration=0 预期 warning 应存在（白名单不能空转）");
		}

		// ────────────────────────── 工具 ──────────────────────────

		/// <summary>用给定文本覆写真实配置中的某一张表，装配（不因 error 抛出）并取回校验报告。</summary>
		private static ConfigReport ReportFor(string tableName, string json)
			=> ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith(tableName, json), failOnConfigErrors: false).ConfigReport;
	}
}
