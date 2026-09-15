using Newtonsoft.Json.Linq;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
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
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-2.9）**科技树：单树串行 + 三树并行（并发可配）**（`F1`、`TECH-01/04`）的验收检查。
	/// <para>主张：① 每棵树同时只能进行 1 项研发（设计稿 research_tree.md）；② 三棵树互相独立 → 最多 3 项并行；
	/// ③ `Concurrency` 可配（为"一树多研发"预留）；④ 研究任务快照带上**所属树**（`UId` = treeId），
	/// 续跑不再把 `nodeId` 当 `treeId` 用。</para>
	/// </summary>
	internal static class TechTreeConcurrencyChecks
	{
		private const string MapId = "tech-map";

		public static void RunAll()
		{
			Check.Run("WP-2.9 配置：三棵树 Concurrency=1（设计稿口径：树内串行）+ 真实配置 0 error", ConcurrencyDefaultsToSerial);
			Check.Run("WP-2.9 树内串行：同树第二个研究请求被拒，完成后才轮到下一个（`F1`）", ResearchIsSerialWithinTree);
			Check.Run("WP-2.9 三树并行：不同树的研究互不阻塞（2 条任务并存并各自完成）", TreesAreIndependent);
			Check.Run("WP-2.9 并发可配：Concurrency=2 时同树可并行两项、第三项仍被拒（为「一树多研发」预留）", ConcurrencyIsConfigurable);
			Check.Run("WP-2.9 研究任务键：`UId`=树 Id、`Id`=节点 Id，续跑按树恢复（`TECH-01`/`TECH-04`）", ResearchTaskCarriesTree);
			Check.Run("WP-2.9 校验器：Concurrency<1 判 error、>1 判 warning", ValidatorGuardsConcurrency);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void ConcurrencyDefaultsToSerial()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			Check.Assert(!core.ConfigReport.HasErrors, $"真实配置不应有 error：{core.ConfigReport.Summary()}");
			Check.AssertEqual(0, core.ConfigReport.Issues.Count(i => i.ToString().Contains("Concurrency")),
				$"Concurrency 不应产生任何问题行：{core.ConfigReport.ToLines()}");

			foreach (string treeId in new[] { "math", "physics", "chemistry" })
				Check.AssertEqual(1, core.Tables.TechTrees.GetTechTreeConfig(treeId).Concurrency,
					$"{treeId} 的并发上限（设计稿：每棵树同时只能进行一项研发）");

			Harness h = NewHarness();
			try
			{
				Check.AssertEqual(1, h.Tech.GetConcurrency(MapId, h.OwnerId, "chemistry"), "域内并发上限随配置");
				Check.AssertEqual(0, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "初始没有进行中的研究");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void ResearchIsSerialWithinTree()
		{
			Harness h = NewHarness();
			try
			{
				// 化学树的根节点：taming_of_fire（10 日）→ 其下 pottery（15 日）与 potash_alkali（12 日）
				Check.Assert(h.Tech.CanResearch(MapId, h.OwnerId, "chemistry", "taming_of_fire"), "taming_of_fire 是化学树根节点");
				Check.Assert(!h.Tech.CanResearch(MapId, h.OwnerId, "chemistry", "pottery"), "pottery 前置（火的驯服）未满足 → 不可研究");
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "taming_of_fire");
				h.Clock.AdvanceDays(10);   // 火的驯服 10 日 ⇒ 之后 potash_alkali / pottery 前置已满足
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "pottery");
				Check.AssertEqual(1, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "开工后占用 1 个槽位");
				Check.Assert(h.Tasks.GetCurrentTasks(MapId).Any(t => t.Type == "Research"), "应产生研究任务");

				// 同树第二个请求：counting 的前置本就满足，但槽位已满 → 被拒（树内串行）
				Check.Assert(h.Tech.CanResearch(MapId, h.OwnerId, "chemistry", "potash_alkali"), "potash_alkali 本身够格研究");
				Check.Assert(!h.Tech.CanStartResearch(MapId, h.OwnerId, "chemistry", "potash_alkali"), "但同树已有研究在进行 → 不能开工");

				h.Tech.Research(MapId, h.OwnerId, "chemistry", "potash_alkali");
				Check.AssertEqual(1, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Research"), "被拒的请求不得产生第二条任务");
				Check.AssertEqual(1, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "槽位仍只有 1 个");

				// 15 日 → writing 完成 → 槽位释放，counting 可以开工
				h.Clock.AdvanceDays(15);
				Check.Assert(h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "chemistry").IsResearched("pottery"), "pottery 应研究完成");
				Check.AssertEqual(0, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "完成后应释放槽位");
				Check.AssertEqual(0, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Research"), "研究任务应被回收");

				h.Tech.Research(MapId, h.OwnerId, "chemistry", "potash_alkali");
				Check.AssertEqual(1, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "串行结束后下一个才开工");
				h.Clock.AdvanceDays(15);
				Check.Assert(h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "chemistry").IsResearched("potash_alkali"), "potash_alkali（12 日）应在此完成");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void TreesAreIndependent()
		{
			Harness h = NewHarness();
			try
			{
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "taming_of_fire");
				h.Clock.AdvanceDays(10);   // 火的驯服 10 日 ⇒ 之后 potash_alkali / pottery 前置已满足
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "pottery");
				h.Tech.Research(MapId, h.OwnerId, "math", "counting");
				h.Clock.AdvanceDays(1);    // 计数 0 日
				h.Tech.Research(MapId, h.OwnerId, "physics", "simple_machine_intuition");

				Check.AssertEqual(2, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Research"), "两棵树的任务应并存（三树并行）");
				Check.AssertEqual(1, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "化学树 1 项");
				Check.AssertEqual(1, h.Tech.GetInProgress(MapId, h.OwnerId, "physics").Count, "物理树 1 项");

				h.Clock.AdvanceDays(15);
				Check.Assert(h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "chemistry").IsResearched("pottery"), "化学树先完成");
				Check.AssertEqual(1, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Research"), "物理树仍在研究（互不阻塞）");

				h.Clock.AdvanceDays(30);
				Check.Assert(h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "physics").IsResearched("simple_machine_intuition"), "物理树随后完成");
				Check.AssertEqual(0, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Research"), "两棵树的任务都已回收");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void ConcurrencyIsConfigurable()
		{
			Harness h = NewHarness(scienceConcurrency: 2); // 测试夹具：把化学树的并发改成 2
			try
			{
				Check.AssertEqual(2, h.Tech.GetConcurrency(MapId, h.OwnerId, "chemistry"), "并发上限应来自配置");

				h.Tech.Research(MapId, h.OwnerId, "chemistry", "taming_of_fire");
				h.Clock.AdvanceDays(10);   // 火的驯服 10 日 ⇒ 之后 potash_alkali / pottery 前置已满足
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "pottery");
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "potash_alkali");

				Check.AssertEqual(2, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "并发 2：同树可并行两项（为「一树多研发」预留）");
				Check.AssertEqual(2, h.Tasks.GetCurrentTasks(MapId).Count(t => t.Type == "Research"), "两条研究任务并存");

				h.Clock.AdvanceDays(15);
				Check.Assert(h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "chemistry").IsResearched("pottery"), "并行两项都应各自完成");
				Check.Assert(h.Tech.GetOrCreateTechTree(MapId, h.OwnerId, "chemistry").IsResearched("potash_alkali"), "counting 也完成（0 日）");
				Check.AssertEqual(0, h.Tech.GetInProgress(MapId, h.OwnerId, "chemistry").Count, "完成后槽位归零");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void ResearchTaskCarriesTree()
		{
			Harness h = NewHarness();
			try
			{
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "taming_of_fire");
				h.Clock.AdvanceDays(10);   // 火的驯服 10 日 ⇒ 之后 potash_alkali / pottery 前置已满足
				h.Tech.Research(MapId, h.OwnerId, "chemistry", "pottery");

				TaskSnapshot snapshot = h.Tasks.GetCurrentTasks(MapId).Single(t => t.Type == "Research");
				Check.AssertEqual("chemistry", snapshot.UId, "研究任务的 `UId` 应是**所属树**（旧实现为 none）");
				Check.AssertEqual("pottery", snapshot.Id, "业务键 = 节点 Id");
				Check.AssertEqual("Research:chemistry:pottery", snapshot.Key, "存储键 = `Research:{treeId}:{nodeId}`");
			}
			finally { Cleanup(h.Dir); }

			// 续跑：树 Id 从 `UId` 取 → 研究真正落到化学树（旧实现把 nodeId 当 treeId，读档后数据错乱）
			Harness resumed = NewHarness();
			try
			{
				resumed.Resources.AddResource("Idea", 20000f, MapId, resumed.OwnerId);
				resumed.Tech.Research(MapId, resumed.OwnerId, "chemistry", "taming_of_fire");
				resumed.Clock.AdvanceDays(10); // 先满足 pottery 的前置（火的驯服 10 日）
				var resumeSnapshot = new TaskSnapshot
				{
					MapId = MapId, OwnerId = resumed.OwnerId, Progress = 14f, Target = 15f,
					Id = "pottery", Type = "Research", UId = "chemistry", IsCompleted = false,
				};

				LinearTask task = resumed.Tech.CreateResearchTask(MapId, resumed.OwnerId, resumeSnapshot);
				Check.Assert(task != null, "带树 Id 的快照应能续跑");
				resumed.Time.Register(task);
				resumed.Clock.AdvanceDays(1);

				Check.Assert(resumed.Tech.GetOrCreateTechTree(MapId, resumed.OwnerId, "chemistry").IsResearched("pottery"),
					"续跑完成应研究**化学树**的节点");
				Check.Assert(!resumed.Tech.GetOrCreateTechTree(MapId, resumed.OwnerId, "pottery").IsResearched("pottery"),
					"不应把 nodeId 当成 treeId 建出一棵叫 writing 的树（`TECH-01`）");

				// 旧口径快照（UId=none）：无法判断所属树 → 拒绝续跑（不静默猜）
				var legacy = new TaskSnapshot
				{
					MapId = MapId, OwnerId = resumed.OwnerId, Progress = 1f, Target = 15f,
					Id = "pottery", Type = "Research", UId = "none", IsCompleted = false,
				};
				Check.Assert(resumed.Tech.CreateResearchTask(MapId, resumed.OwnerId, legacy) == null,
					"旧口径快照（UId=none）应被拒绝续跑，而不是猜一棵树出来");
			}
			finally { Cleanup(resumed.Dir); }
		}

		private static void ValidatorGuardsConcurrency()
		{
			ConfigReport zero = BuildReport(0);
			Check.Assert(zero.HasErrors, "Concurrency=0 应判 error（该树永远无法开工研究）");

			ConfigReport two = BuildReport(2);
			Check.Assert(!two.HasErrors, "Concurrency=2 是合法配置");
			Check.Assert(two.Issues.Any(i => i.Level == ConfigIssueLevel.Warning && i.ToString().Contains("Concurrency")),
				"Concurrency=2 应给出 warning（超出设计稿当前口径）");
		}

		private static ConfigReport BuildReport(int concurrency)
		{
			var root = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("TechTrees")));
			((JObject)((JObject)root["TechTrees"])["chemistry"])["Concurrency"] = concurrency;

			return ConfigFixtures.BuildCore(
				ConfigFixtures.RealConfigSourceWith("TechTrees", root.ToString()),
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
			public ResourcesAppService Resources;
			public ModifierAppService Modifier;
			public TechTreesAppService Tech;

			public void Dispose() => Cleanup(Dir);
		}

		/// <summary>真实配置（可选覆盖化学树的并发上限），任务仓落在独立临时目录。</summary>
		private static Harness NewHarness(int ownerId = 1, int? scienceConcurrency = null)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp29-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			CoreServices core;
			if (scienceConcurrency.HasValue)
			{
				var root = JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("TechTrees")));
				((JObject)((JObject)root["TechTrees"])["chemistry"])["Concurrency"] = scienceConcurrency.Value;
				core = ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith("TechTrees", root.ToString()));
			}
			else
			{
				core = ConfigFixtures.BuildRealCore();
			}

			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue }; // `D28`
			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"));
			var time = new GameTimeService(clock, tasks);
			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_")),
				core.Tables.Resources,
				time,
				new ModifierRepository(Path.Combine(dir, "mod_")));
			var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(dir, "mod_")));
			resources.AddResource("Idea", 50000f, MapId, ownerId); // 夹具：给足 Idea，避免"资源不足"掩盖并发语义
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees),
				core.Tables.TechTrees,
				resources,
				modifier,
				time);

			// 研究要花 Idea（`writing` 30 / `mathematics` 80 …）
			resources.AddResource("Idea", 1000f, MapId, ownerId);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = clock,
				Time = time,
				Tasks = tasks,
				Resources = resources,
				Modifier = modifier,
				Tech = tech,
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
