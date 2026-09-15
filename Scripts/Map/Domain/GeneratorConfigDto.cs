using Newtonsoft.Json;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.6.4 / WP-5.9）**开局布置参数**（`Config/Generator.json` 的 `Start` 段）：
	/// 出生点间距、开局单位、开局额外资源、开局揭示半径。
	/// <para>为什么放在"生成器"这张表里：开局布置是"世界创建"的一部分，与地图生成同一节拍发生；
	/// 单独开第 8 张表会让装载/校验/指南三处都多一份样板，而这里信息量只有 4 个字段。</para>
	/// </summary>
	public interface IStartSetupConfig
	{
		/// <summary>两个势力出生点之间的**最小间距**（六边形立方距离）；0 = 不限制。</summary>
		int MinSpawnDistance { get; }

		/// <summary>开局在每个势力出生点周围揭示的半径（迷雾）；0 = 不揭示。</summary>
		int RevealRadius { get; }

		/// <summary>每个势力的开局单位（**人类与 AI 相同**，`D88`）；空 = 不给单位。</summary>
		IReadOnlyList<string> InitialUnits { get; }

		/// <summary>开局额外发放的资源（在资源表初始储备之外；空 = 不额外发）。</summary>
		IReadOnlyDictionary<string, float> InitialResources { get; }
	}

	/// <summary>
	/// （v0.3 / WP-1.3；v0.6.4 / WP-5.9 补 `Start` 段）地图生成器配置的纯 C# 默认实现：
	/// 无头环境（测试 / 离线结算）没有 Godot 的 <c>.tres</c> 资源，用本类作为回退值，
	/// 使 <c>VoronoiMapGenerator</c> 不再硬依赖 <c>ResourceLoader</c>。
	/// </summary>
	public class GeneratorConfigDto : IMapGeneratorConfig, IStartSetupConfig
	{
		/// <summary>锚点密度（与 <c>Config/Generator/Generator.tres</c> 的默认值一致）。</summary>
		public float Density { get; set; } = 4f;

		/// <summary>`Start` 段（JSON 键名 `Start`）。</summary>
		[JsonProperty("Start")]
		public StartSetupDto StartData { get; set; }

		IStartSetupConfig StartConfig => StartData;

		int IStartSetupConfig.MinSpawnDistance => StartConfig?.MinSpawnDistance ?? 20;

		int IStartSetupConfig.RevealRadius => StartConfig?.RevealRadius ?? 3;

		IReadOnlyList<string> IStartSetupConfig.InitialUnits
			=> StartConfig?.InitialUnits ?? new List<string> { "worker" };

		IReadOnlyDictionary<string, float> IStartSetupConfig.InitialResources
			=> StartConfig?.InitialResources ?? new Dictionary<string, float>();
	}

	/// <summary>（v0.6.4 / WP-5.9）`Start` 段的 JSON DTO（缺省值 = 与设计意图一致的保守值）。</summary>
	public class StartSetupDto : IStartSetupConfig
	{
		public int MinSpawnDistance { get; set; } = 20;

		public int RevealRadius { get; set; } = 3;

		/// <summary>
		/// 开局单位 Id 列表（空 = 用缺省的 1 个工人）。
		/// <para>⚠️ 这里**不能**预置默认值：Newtonsoft 对"已存在的集合"默认做 **append**（`ObjectCreationHandling.Auto`），
		/// 预置 <c>{"worker"}</c> 再读 `["worker"]` 会得到**两条** —— 开局就多送一个单位（本 WP 的用例抓到了这一条）。
		/// 缺省一律在读取时补。</para>
		/// </summary>
		public List<string> InitialUnits { get; set; }

		/// <summary>开局额外资源（空 = 不额外发；同样不预置，避免 append 语义把表读成两份）。</summary>
		public Dictionary<string, float> InitialResources { get; set; }

		IReadOnlyList<string> IStartSetupConfig.InitialUnits
			=> InitialUnits != null && InitialUnits.Count > 0 ? InitialUnits : new List<string> { "worker" };

		IReadOnlyDictionary<string, float> IStartSetupConfig.InitialResources
			=> InitialResources ?? new Dictionary<string, float>();
	}
}

