using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechNodeConfig
	{
		string Id { get; }

		/// <summary>
		/// （v0.3 / WP-2.1）带**树维度**的前置列表：`TreeId` 为空 = 本树节点（旧表写法兼容），
		/// 非空 = 跨树前置。原为 `List&lt;string&gt;`，是 `TECH-07`（物理树永久不可解锁）的根因。
		/// </summary>
		List<TechPrerequisite> Prerequisites { get; }

		float Cost { get; }
		float Duration { get; }
		List<Modifier> Modifiers { get; }
	}
}
