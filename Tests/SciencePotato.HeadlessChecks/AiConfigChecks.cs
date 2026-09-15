using Newtonsoft.Json.Linq;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.AI.Infrastructure;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Map.Infrastructure;
using System;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.1 / WP-6.1）**AI 配置**的验收检查：装载、缺省、校验分级（比例/阈值/流派偏好）。
	/// <para>本 WP 只到"参数就位且可校验"；AI 的行为（决策/经济/军事）归 `WP-6.2`~`WP-6.4` ——
	/// 因此这里断言的是**配置口径**，不是"AI 会不会打仗"。</para>
	/// </summary>
	internal static class AiConfigChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-6.1 AI 配置：`Config/AI.json` 装载成功且数值 = 用户裁定（1 个 AI / 360 日不造兵）", LoadsRealConfig);
			Check.Run("WP-6.1 AI 配置：分配比例之和 = 1、威胁阈值严格递增", SplitAndThresholdsAreSane);
			Check.Run("WP-6.1 AI 配置：缺文件 → warning + 内置缺省（AI 仍能跑）", MissingFileFallsBackWithWarning);
			Check.Run("WP-6.1 AI 配置：比例之和≠1 / 阈值不递增 / 流派偏好不存在 → error", BadValuesAreErrors);
			Check.Run("WP-6.1 玩家表：宿主不给表时按 `AI.Count` 自动补 AI（生产路径）", PlayersBuiltFromAiConfig);
		}

		private static void LoadsRealConfig()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			Check.Assert(core.Ai != null, "组合根应暴露 AI 配置");

			Check.AssertEqual(1, core.Ai.Count, "AI 数量（用户定：先 1 个）");
			Check.AssertEqual(360, core.Ai.NoMilitaryDays, "前期不造兵窗口（`D93`：1 游戏年 = 360 日）");
			Check.AssertEqual(30, core.Ai.DecisionIntervalDays, "决策节拍（游戏日）");
			Check.AssertEqual("math", core.Ai.SciencePreference, "科技流派偏好");
			Check.AssertEqual(0, core.ConfigReport.ForTable("AI").Count(i => i.Level == ConfigIssueLevel.Error), "真实 AI 配置的 error 数");
		}

		private static void SplitAndThresholdsAreSane()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			IAiResourceSplit split = core.Ai.ResourceSplit;
			Check.AssertEqual(1f, split.Build + split.Research + split.Military, "三类分配比例之和");
			Check.Assert(split.Build >= split.Research && split.Research >= split.Military,
				"缺省应该是\"发展优先\"（建造 ≥ 科研 ≥ 军事）");

			IAiThreatThresholds threat = core.Ai.ThreatThresholds;
			Check.Assert(threat.Low < threat.Medium && threat.Medium < threat.High && threat.High < threat.Lethal,
				"威胁阈值应严格递增");
		}

		private static void MissingFileFallsBackWithWarning()
		{
			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			source.Inject("AI", null); // 显式清空 = 模拟"没有这张表"

			CoreServices core = ConfigFixtures.BuildCore(source, failOnConfigErrors: false);
			Check.AssertEqual(360, core.Ai.NoMilitaryDays, "缺表时用内置缺省（AI 仍能跑）");
			Check.Assert(core.ConfigReport.ForTable("AI").Any(i => i.Level == ConfigIssueLevel.Warning),
				"缺表应给 warning（而不是静默）");
			Check.Assert(!core.ConfigReport.HasErrors, "缺表不应判 error（AI 是可选能力）");
		}

		private static void BadValuesAreErrors()
		{
			// ① 分配比例之和 ≠ 1
			CoreServices badSplit = ConfigFixtures.BuildCore(
				BuildAiSource(json => json["Defaults"]["ResourceSplit"]["Build"] = 0.9),
				failOnConfigErrors: false);
			Check.Assert(badSplit.ConfigReport.ForTable("AI").Any(i => i.Level == ConfigIssueLevel.Error && i.ToString().Contains("ResourceSplit")),
				"比例之和≠1 应判 error");

			// ② 威胁阈值不递增
			CoreServices badThreshold = ConfigFixtures.BuildCore(
				BuildAiSource(json => json["Defaults"]["ThreatThresholds"]["High"] = 2),
				failOnConfigErrors: false);
			Check.Assert(badThreshold.ConfigReport.ForTable("AI").Any(i => i.Level == ConfigIssueLevel.Error && i.ToString().Contains("ThreatThresholds")),
				"阈值不递增应判 error");

			// ③ 流派偏好不是已装载的科技树
			CoreServices badTree = ConfigFixtures.BuildCore(
				BuildAiSource(json => json["Defaults"]["SciencePreference"] = "alchemy"),
				failOnConfigErrors: false);
			Check.Assert(badTree.ConfigReport.ForTable("AI").Any(i => i.Level == ConfigIssueLevel.Error && i.ToString().Contains("SciencePreference")),
				"流派偏好引用不存在的科技树应判 error");
		}

		/// <summary>
		/// **生产路径的玩家表**：宿主（`ServiceContainer`）不显式给表时，装配层按 `Config/AI.json` 的 `Count`
		/// 自建"1 个人类 + N 个 AI"—— 这样"一局有几个 AI"只有一个出处，不需要在两处同步。
		/// <para>AI 与人类的差别只有"谁下指令"：出生点规则、开局单位、月结/成长/视野都走同一套（`D88`）。</para>
		/// </summary>
		private static void PlayersBuiltFromAiConfig()
		{
			// 直接装配（不经过夹具的显式玩家表）
			CoreServices core = CoreBootstrap.Build(new CoreDependencies
			{
				FileSystem = new InMemoryFileSystem(),
				ConfigSource = ConfigFixtures.RealConfigSource(),
				Random = new SystemRandom(20260914),
				MapRepositoryFactory = tables => new InMemoryMapRepository(),
				SessionId = "ai-players",
				// 注意：**不传 Players** ⇒ 走"按 AI.Count 自建"分支
			});

			Check.AssertEqual(1, core.Ai.Count, "真实配置的 AI 数量");
			Check.AssertEqual(2, core.Session.Players.Count, "玩家表 = 1 人类 + 1 AI");
			Check.AssertEqual(1, core.Session.HumanOwnerId, "人类仍是 owner=1");
			Check.Assert(core.Session.OwnerIds.SequenceEqual(new[] { 1, 2 }), "AI 的 ownerId 从 2 开始且升序");
			Check.Assert(!core.Session.IsHuman(2), "owner=2 是 AI");

			// `Count = 3` ⇒ 1 人类 + 3 AI
			CoreServices three = CoreBootstrap.Build(new CoreDependencies
			{
				FileSystem = new InMemoryFileSystem(),
				ConfigSource = BuildAiSource(json => json["Defaults"]["Count"] = 3),
				Random = new SystemRandom(20260914),
				MapRepositoryFactory = tables => new InMemoryMapRepository(),
				SessionId = "ai-players-3",
			});
			Check.AssertEqual(4, three.Session.Players.Count, "Count=3 ⇒ 1 人类 + 3 AI");
		}

		/// <summary>以真实 `Config/AI.json` 为底改一个字段（用于造"填错了"的场景）。</summary>
		private static InMemoryConfigSource BuildAiSource(System.Action<JObject> mutate)
		{
			string path = Path.Combine(Check.FindRepoRoot(), "Config", "AI.json");
			var json = JObject.Parse(File.ReadAllText(path));
			mutate(json);

			InMemoryConfigSource source = ConfigFixtures.RealConfigSource();
			source.Inject("AI", json.ToString());
			return source;
		}
	}
}
