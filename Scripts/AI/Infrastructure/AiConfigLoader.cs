using Newtonsoft.Json;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using System;
using System.Linq;

namespace SciencePotato.Scripts.AI.Infrastructure
{
	/// <summary>
	/// （v0.7.1 / WP-6.1）**AI 配置装载 + 校验**：`Config/AI.json` → <see cref="IAiConfig"/>。
	/// <para>为什么不并进 7 张配置表：AI 表是**策略参数**（调手感用的），不参与造物/科技的引用完整性；
	/// 与多语言文案同一处理方式（由 `IConfigSource` 直接读、单独校验），从而不动 `ConfigTables` 的 7 表契约。</para>
	/// <para>分级：缺文件 = warning（用缺省值，AI 仍能跑）；比例不合法 / 阈值不递增 / 科技树 Id 不存在 = **error**
	/// （这些会让 AI 行为"看起来在跑但全是错的"，属于必须看见的配置错误）。</para>
	/// </summary>
	public static class AiConfigLoader
	{
		/// <summary>配置文件在 `IConfigSource` 里的名字（对应 `Config/AI.json`）。</summary>
		public const string ConfigName = "AI";

		public static IAiConfig Load(IConfigSource source, ConfigTables tables, ConfigReport report)
		{
			string json = source?.LoadText(ConfigName);
			if (string.IsNullOrWhiteSpace(json))
			{
				report?.Warn("AI", null, "缺少 Config/AI.json：AI 将使用内置缺省（1 个 AI、360 日不造兵、建造/科研/军事 = 50/30/20）");
				return AiConfigDto.Fallback;
			}

			IAiConfig config;
			try
			{
				config = JsonConvert.DeserializeObject<AiConfigDto>(json) ?? AiConfigDto.Fallback;
			}
			catch (Exception ex)
			{
				report?.Error("AI", null, $"解析失败：{ex.Message}");
				return AiConfigDto.Fallback;
			}

			Validate(config, tables, report);
			return config;
		}

		private static void Validate(IAiConfig config, ConfigTables tables, ConfigReport report)
		{
			if (config.Count < 0) report?.Error("AI", "Count", $"Count={config.Count} 不能为负");
			if (config.NoMilitaryDays < 0) report?.Error("AI", "NoMilitaryDays", $"NoMilitaryDays={config.NoMilitaryDays} 不能为负");
			if (config.DecisionIntervalDays <= 0) report?.Error("AI", "DecisionIntervalDays", "决策节拍必须 > 0（AI 不能每帧决策）");
			if (config.ExploreRadius < 0) report?.Error("AI", "ExploreRadius", "探图半径不能为负");
			if (config.ReserveMonths < 0f) report?.Error("AI", "ReserveMonths", "最低保留（月数）不能为负");

			// 分配比例：和必须为 1（否则"分配"这个模型没有意义）
			IAiResourceSplit split = config.ResourceSplit;
			float sum = split.Build + split.Research + split.Military;
			if (split.Build < 0f || split.Research < 0f || split.Military < 0f)
				report?.Error("AI", "ResourceSplit", "分配比例不能为负");
			else if (Math.Abs(sum - 1f) > 0.01f)
				report?.Error("AI", "ResourceSplit", $"三类比例之和={sum:0.###}，应等于 1（建造/科研/军事）");

			// 威胁阈值必须递增（否则等级判定会出现"不可达档位"）
			IAiThreatThresholds threat = config.ThreatThresholds;
			if (!(0 < threat.Low && threat.Low < threat.Medium && threat.Medium < threat.High && threat.High < threat.Lethal))
				report?.Error("AI", "ThreatThresholds",
					$"威胁阈值必须严格递增且 > 0（当前 Low={threat.Low} Medium={threat.Medium} High={threat.High} Lethal={threat.Lethal}）");

			// 科技流派偏好必须是已装载的科技树 Id（否则 AI 的"研究"永远选不到东西）
			if (!string.IsNullOrWhiteSpace(config.SciencePreference)
				&& tables?.TreeIds() != null
				&& !tables.TreeIds().Contains(config.SciencePreference, StringComparer.Ordinal))
				report?.Error("AI", "SciencePreference",
					$"流派偏好 \"{config.SciencePreference}\" 不是已装载的科技树（当前：{string.Join(", ", tables.TreeIds())}）");
		}
	}
}
