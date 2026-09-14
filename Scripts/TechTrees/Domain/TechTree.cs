using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechTree
	{
		public string TreeId { get; }
		public int OwnerId { get; }

		[JsonProperty]
		private Dictionary<string, TechNode> _nodes = new();
		[JsonProperty]
		private HashSet<string> _researchedIds = new();

		/// <summary>
		/// （v0.3 / WP-2.1）跨树前置的解析入口：<c>(treeId, nodeId) → 是否已研究</c>。
		/// <para>由 `TechTreesAppService` 在取到树之后挂上（<see cref="AttachResearchLookup"/>）；**不落盘**
		/// （<see cref="JsonIgnoreAttribute"/>：委托无法序列化，且它是"环境"而不是树的自身状态）。</para>
		/// <para>未挂时跨树前置一律视为**未满足**（fail closed）：宁可暂时不可研究，也不能错误放行 —— 复现
		/// `TECH-07` 的那种"看着可点、其实前置永远不成立"的静默错误要更糟。</para>
		/// </summary>
		[JsonIgnore]
		private Func<string, string, bool> _isResearchedElsewhere;

		public IReadOnlyDictionary<string, TechNode> Nodes => _nodes;

		[JsonConstructor]
		private TechTree(string treeId, int ownerId)
		{
			TreeId = treeId;
			OwnerId = ownerId;
		}

		public TechTree(string treeId, int ownerId, ITechTreeConfig config) : this(treeId, ownerId)
		{
			InitializeFromConfig(config);
		}

		public void InitializeFromConfig(ITechTreeConfig config)
		{
			_nodes.Clear();
			_researchedIds.Clear();

			if (config?.Techs == null) return;

			foreach (var kvp in config.Techs)
			{
				var node = new TechNode(kvp.Value);
				_nodes[kvp.Key] = node;
			}
		}

		/// <summary>
		/// （v0.3 / WP-2.1）挂上跨树前置的解析器（由应用层提供，仓储/应用服务在装载后调用）。
		/// <para>同树前置不走它（直接查本树的 <c>_researchedIds</c>），因此**不挂也能正常玩单树**。</para>
		/// </summary>
		public void AttachResearchLookup(Func<string, string, bool> isResearchedElsewhere)
		{
			_isResearchedElsewhere = isResearchedElsewhere;
		}

		/// <summary>
		/// （v0.3 / WP-2.1）把配置重新挂到节点上：**含存档之后往表里新增的节点**。
		/// <para>旧实现只 hydrate 已存在的节点，因此"先有存档、后加表"时新节点永远不出现；
		/// 而跨树前置（`TECH-07`）正是这种场景（数学树先研究、物理树按表增量开放）。</para>
		/// </summary>
		public void HydrateConfigs(ITechTreesConfigRepository configRepo)
		{
			var treeConfig = configRepo?.GetTechTreeConfig(TreeId);
			if (treeConfig?.Techs == null) return;

			foreach (var kvp in treeConfig.Techs)
			{
				if (kvp.Value == null) continue;

				if (_nodes.TryGetValue(kvp.Key, out var node))
				{
					node.HydrateConfig(kvp.Value);
				}
				else
				{
					_nodes[kvp.Key] = new TechNode(kvp.Value);
				}
			}
		}

		public bool IsResearched(string nodeId)
		{
			return _researchedIds.Contains(nodeId);
		}

		/// <summary>
		/// 是否可以研究该节点：未研究 + **全部前置已满足**。
		/// <para>（v0.3 / WP-2.1）前置判定改为逐条走 <see cref="IsPrerequisiteMet"/>：本树前置查本树集合，
		/// 跨树前置（`TreeId` 非空）走 <c>_isResearchedElsewhere</c> —— 这正是 `TECH-07` 的修复点。</para>
		/// </summary>
		public bool CanResearch(string nodeId)
		{
			if (!_nodes.TryGetValue(nodeId, out var node)) return false;
			if (_researchedIds.Contains(nodeId)) return false;

			var config = node.Config;
			if (config?.Prerequisites == null || config.Prerequisites.Count == 0)
				return true;

			return config.Prerequisites.All(IsPrerequisiteMet);
		}

		/// <summary>
		/// （v0.3 / WP-2.1）单条前置是否已满足。公开给表现层做"还差哪个前置"的提示（`WP-4.14`），
		/// 也让验收测试能直接断言跨树查询这一环。
		/// </summary>
		public bool IsPrerequisiteMet(TechPrerequisite prerequisite)
		{
			if (prerequisite == null || string.IsNullOrWhiteSpace(prerequisite.NodeId)) return false;

			if (!prerequisite.IsCrossTree || prerequisite.TreeId == TreeId)
				return _researchedIds.Contains(prerequisite.NodeId);

			return _isResearchedElsewhere?.Invoke(prerequisite.TreeId, prerequisite.NodeId) ?? false;
		}

		/// <summary>（v0.3 / WP-2.1）节点前置列表（空 = 根节点；未知节点同样返回空列表）。</summary>
		public IReadOnlyList<TechPrerequisite> GetPrerequisites(string nodeId)
			=> (_nodes.TryGetValue(nodeId, out var node) ? node.Config?.Prerequisites : null)
			   ?? new List<TechPrerequisite>();

		public void Research(string nodeId)
		{
			if (!_nodes.ContainsKey(nodeId)) return;
			if (_researchedIds.Contains(nodeId)) return;

			if (CanResearch(nodeId))
			{
				_researchedIds.Add(nodeId);
				_nodes[nodeId].MarkResearched();
			}
		}

		public float GetCost(string nodeId)
		{
			if (_nodes.TryGetValue(nodeId, out var node) && node.Config != null)
				return node.Config.Cost;
			return 0f;
		}

		public float GetDuration(string nodeId)
		{
			if (_nodes.TryGetValue(nodeId, out var node) && node.Config != null)
				return node.Config.Duration;
			return 0f;
		}

		public List<SciencePotato.Scripts.Common.Domain.Modifier> GetModifiers(string nodeId)
		{
			if (_nodes.TryGetValue(nodeId, out var node) && node.Config != null)
				return node.Config.Modifiers ?? new List<SciencePotato.Scripts.Common.Domain.Modifier>();
			return new List<SciencePotato.Scripts.Common.Domain.Modifier>();
		}
	}
}