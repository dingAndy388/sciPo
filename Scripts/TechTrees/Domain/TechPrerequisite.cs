using Newtonsoft.Json;
using System;

namespace SciencePotato.Scripts.TechTree.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.1）**带树维度的科技前置**：修复 `TECH-07`（跨树前置失效 → 物理树 35 节点永久不可解锁）。
	/// <para>`TreeId` 为空 = 「引用它的节点所在的那棵树」，因此**旧表的 <c>"mathematics"</c> 写法不需要改写**；
	/// 非空 = 跨树前置（推荐写法 <c>"math:counting"</c> 或结构体 <c>{ "TreeId": "science", "NodeId": "counting" }</c>）。</para>
	/// <para>解析兼容层见 <see cref="TechPrerequisiteJsonConverter"/>；空/错填的 Id 由
	/// <c>ConfigValidator</c> 判 error，而不是让整张表"解析失败"。</para>
	/// </summary>
	[JsonConverter(typeof(TechPrerequisiteJsonConverter))]
	public sealed class TechPrerequisite
	{
		/// <summary>所属科技树 Id；空 = 与本节点同一棵树（向后兼容旧表）。</summary>
		public string TreeId { get; }

		/// <summary>节点 Id（在所属树内唯一）。</summary>
		public string NodeId { get; }

		public TechPrerequisite(string treeId, string nodeId)
		{
			TreeId = treeId;
			NodeId = nodeId;
		}

		/// <summary>本树前置（无树维度，等价于旧表写法）。</summary>
		public static TechPrerequisite InTree(string nodeId) => new(null, nodeId);

		/// <summary>跨树前置。</summary>
		public static TechPrerequisite Cross(string treeId, string nodeId) => new(treeId, nodeId);

		/// <summary>是否带树维度（即是否跨树前置）。</summary>
		public bool IsCrossTree => !string.IsNullOrWhiteSpace(TreeId);

		/// <summary>规范写法：跨树为 <c>treeId:nodeId</c>，本树为 <c>nodeId</c>（也是 JSON 写出口径）。</summary>
		public override string ToString() => IsCrossTree ? TreeId + ":" + NodeId : NodeId;
	}
}
