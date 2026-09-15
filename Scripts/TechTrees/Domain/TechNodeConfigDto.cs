using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechNodeConfigDto : ITechNodeConfig
	{
		public string Id { get; set; }

		/// <summary>（v0.8.1 / `WP-7.3`）设计稿中文名。</summary>
		public string Name { get; set; }

		/// <summary>（v0.8.1 / `WP-7.3`）设计稿效果原文（含尚未实现的机制，见 `ITechNodeConfig.EffectText`）。</summary>
		public string EffectText { get; set; }

		/// <summary>（v0.3 / WP-2.1）三种写法均可：`"nodeId"`（本树）、`"treeId:nodeId"`、`{ "TreeId": …, "NodeId": … }`。</summary>
		public List<TechPrerequisite> Prerequisites { get; set; }
		public float Cost { get; set; }
		public float Duration { get; set; }
		public List<Modifier> Modifiers { get; set; }

		/// <summary>（v0.8.9 / WP-4.14）解锁的界面能力（如 resource_panel / research / harvest_panel）。</summary>
		public List<string> UnlocksUi { get; set; } = new List<string>();

		IReadOnlyList<string> ITechNodeConfig.UnlocksUi => UnlocksUi ?? new List<string>();
	}
}