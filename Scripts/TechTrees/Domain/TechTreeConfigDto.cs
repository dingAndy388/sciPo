using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechTreeConfigDto : ITechTreeConfig
	{
		/// <summary>（v0.3 / WP-2.9）每棵树默认**同时只能进行 1 项研发**（设计稿 research_tree.md 第 19 行）。</summary>
		public const int DefaultConcurrency = 1;

		public Dictionary<string, TechNodeConfigDto> Techs { get; set; }

		/// <summary>
		/// （v0.3 / WP-2.9）**本树的研究并发上限**：树内串行（1）是三棵树枝互相独立并行（最多 3 项）的基础；
		/// 设计稿说明"后续会推出节点使得一棵树可以同时进行多个研发"，因此这里按树配置、默认 1。
		/// </summary>
		public int Concurrency { get; set; } = DefaultConcurrency;

		Dictionary<string, ITechNodeConfig> ITechTreeConfig.Techs
			=> Techs?.ToDictionary(kvp => kvp.Key, kvp => (ITechNodeConfig)kvp.Value)
			   ?? new Dictionary<string, ITechNodeConfig>();
	}
}