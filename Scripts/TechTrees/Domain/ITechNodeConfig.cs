using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechNodeConfig
	{
		string Id { get; }

		/// <summary>（v0.8.1 / `WP-7.3`）设计稿中文名（UI 显示用；表里留一份，避免"id → 人话"要靠外部文档）。</summary>
		string Name { get; }

		/// <summary>
		/// （v0.8.1 / `WP-7.3`）**设计稿效果原文**（如"农田产出 +20 Food/年"）。
		/// <para>为什么要留在表里：设计稿的 93 条效果里有一部分**当前还没有对应机制**（范围效果 `WP-4.2`、
		/// 地形通行解锁 `WP-4.11`、UI 门控 `WP-4.14`、"解锁 X 建筑"由建筑表的 `TechRequirements` 承载）——
		/// 原文留档才不会"悄悄丢掉一条设计意图"。</para>
		/// </summary>
		string EffectText { get; }

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
