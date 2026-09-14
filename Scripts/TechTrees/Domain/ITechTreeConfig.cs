using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechTreeConfig
	{
		Dictionary<string,ITechNodeConfig> Techs { get; }

		/// <summary>
		/// （v0.3 / WP-2.9）**本树的研究并发上限**（设计稿默认 1 = 树内串行；三棵树之间互相独立）。
		/// 为"一树多研发"（后续科技节点）预留的可配项。
		/// </summary>
		int Concurrency { get; }
	}
}
