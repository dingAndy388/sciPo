using Newtonsoft.Json;
using System.Collections.Generic;

namespace SciencePotato.Scripts.AI.Domain
{
	/// <summary>
	/// （v0.7.1 / WP-6.1）**AI 对手的配置口径**（`Config/AI.json`）。
	/// <para>来源：`design/AI.md` + 用户裁定（`D93`：前期不造兵窗口 = **360 日 = 1 游戏年**；
	/// "先 1 个 AI、出生点规则同玩家"）。本 WP 只把**数值与策略参数**落进表并校验，
	/// 行为（决策循环 / 经济 / 军事）归 `WP-6.2`~`WP-6.4`。</para>
	/// <para>为什么单开一张表：AI 是所有"势力"里唯一需要**策略参数**的一类，混进其他表会让
	/// "调 AI 手感"变成跨表改（而 AI 手感是要反复调的，`N3`）。</para>
	/// </summary>
	public interface IAiConfig
	{
		/// <summary>AI 势力数量（用户定：先 1 个）。</summary>
		int Count { get; }

		/// <summary>
		/// **前期不造兵窗口**（游戏日）：从开局起这么多天内，AI 不主动生产军事单位（"AI 侧重发展"）。
		/// <para>`D93`：360 日 = 1 游戏年（`design/AI.md` 的"60 年"按 1 年 = 360 日重读，
		/// 否则一局常见规模约 3600 日会变成"整局不造兵"）。</para>
		/// </summary>
		int NoMilitaryDays { get; }

		/// <summary>决策节拍（游戏日）：AI 每隔这么多天做一次优先级判断（生存 → 威胁 → 发展）。</summary>
		int DecisionIntervalDays { get; }

		/// <summary>科技流派偏好（树 Id：`science` / `physics` / `military`）；影响科技选择权重、不锁死。</summary>
		string SciencePreference { get; }

		/// <summary>探图半径（格）："主动探图"时的目标距离上限。</summary>
		int ExploreRadius { get; }

		/// <summary>**最低保留**（月数）：资源存量低于"这么多个月的维护费"时停止扩张/造兵。</summary>
		float ReserveMonths { get; }

		/// <summary>威胁等级阈值（可见敌方单位数）：达到该数量即进入对应等级。</summary>
		IAiThreatThresholds ThreatThresholds { get; }

		/// <summary>资源分配意向（三类比例之和应为 1）。</summary>
		IAiResourceSplit ResourceSplit { get; }
	}

	/// <summary>（v0.7.1 / WP-6.1）威胁等级阈值（`design/AI.md` 的五档里去掉"无威胁"= 0）。</summary>
	public interface IAiThreatThresholds
	{
		int Low { get; }
		int Medium { get; }
		int High { get; }
		int Lethal { get; }
	}

	/// <summary>（v0.7.1 / WP-6.1）资源分配意向（建造 / 科研 / 军事，和为 1）。</summary>
	public interface IAiResourceSplit
	{
		float Build { get; }
		float Research { get; }
		float Military { get; }
	}

	/// <summary>（v0.7.1 / WP-6.1）`Config/AI.json` 的 JSON DTO（缺省值 = 设计意图的保守值）。</summary>
	public class AiConfigDto : IAiConfig
	{
		[JsonProperty("Defaults")]
		public AiDefaultsDto Defaults { get; set; }

		private IAiConfig DefaultsOrEmpty => Defaults ?? new AiDefaultsDto();

		public int Count => DefaultsOrEmpty.Count;

		public int NoMilitaryDays => DefaultsOrEmpty.NoMilitaryDays;

		public int DecisionIntervalDays => DefaultsOrEmpty.DecisionIntervalDays;

		public string SciencePreference => DefaultsOrEmpty.SciencePreference;

		public int ExploreRadius => DefaultsOrEmpty.ExploreRadius;

		public float ReserveMonths => DefaultsOrEmpty.ReserveMonths;

		public IAiThreatThresholds ThreatThresholds => DefaultsOrEmpty.ThreatThresholds;

		public IAiResourceSplit ResourceSplit => DefaultsOrEmpty.ResourceSplit;

		/// <summary>（v0.7.1 / WP-6.1）配置缺失时的兜底实现（全用设计意图的缺省值）。</summary>
		public static IAiConfig Fallback { get; } = new AiConfigDto();
	}

	/// <summary>`Defaults` 段（v0.7.1 / WP-6.1）。</summary>
	public class AiDefaultsDto : IAiConfig
	{
		public int Count { get; set; } = 1;

		public int NoMilitaryDays { get; set; } = 360;

		public int DecisionIntervalDays { get; set; } = 30;

		public string SciencePreference { get; set; } = "science";

		public int ExploreRadius { get; set; } = 12;

		public float ReserveMonths { get; set; } = 1f;

		public AiThreatThresholdsDto ThreatThresholds { get; set; }

		public AiResourceSplitDto ResourceSplit { get; set; }

		IAiThreatThresholds IAiConfig.ThreatThresholds => ThreatThresholds ?? new AiThreatThresholdsDto();

		IAiResourceSplit IAiConfig.ResourceSplit => ResourceSplit ?? new AiResourceSplitDto();
	}

	/// <summary>威胁阈值段。</summary>
	public class AiThreatThresholdsDto : IAiThreatThresholds
	{
		public int Low { get; set; } = 1;
		public int Medium { get; set; } = 3;
		public int High { get; set; } = 6;
		public int Lethal { get; set; } = 10;
	}

	/// <summary>资源分配段。</summary>
	public class AiResourceSplitDto : IAiResourceSplit
	{
		public float Build { get; set; } = 0.5f;
		public float Research { get; set; } = 0.3f;
		public float Military { get; set; } = 0.2f;
	}
}
