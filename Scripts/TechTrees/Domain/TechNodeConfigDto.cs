using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechNodeConfigDto : ITechNodeConfig
	{
		public string Id { get; set; }

		/// <summary>（v0.3 / WP-2.1）三种写法均可：`"nodeId"`（本树）、`"treeId:nodeId"`、`{ "TreeId": …, "NodeId": … }`。</summary>
		public List<TechPrerequisite> Prerequisites { get; set; }
		public float Cost { get; set; }
		public float Duration { get; set; }
		public List<Modifier> Modifiers { get; set; }
	}
}