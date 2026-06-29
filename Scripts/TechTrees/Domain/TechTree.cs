using Newtonsoft.Json;
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

		public void HydrateConfigs(ITechTreesConfigRepository configRepo)
		{
			var treeConfig = configRepo.GetTechTreeConfig(TreeId);
			if (treeConfig?.Techs == null) return;

			foreach (var kvp in treeConfig.Techs)
			{
				if (_nodes.TryGetValue(kvp.Key, out var node))
				{
					node.HydrateConfig(kvp.Value);
				}
			}
		}

		public bool IsResearched(string nodeId)
		{
			return _researchedIds.Contains(nodeId);
		}

		public bool CanResearch(string nodeId)
		{
			if (!_nodes.TryGetValue(nodeId, out var node)) return false;
			if (_researchedIds.Contains(nodeId)) return false;

			var config = node.Config;
			if (config?.Prerequisites == null || config.Prerequisites.Count == 0)
				return true;

			return config.Prerequisites.All(p => _researchedIds.Contains(p));
		}

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