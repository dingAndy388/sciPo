using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.TechTree.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.TechTree.Infrastructure
{
	public class TechTreesConfigRepository : GenericConfigRepository<ITechTreesConfig, TechTreesConfigDto>, ITechTreesConfigRepository
	{
		private ITechTreesConfig _techTreesConfig;

		public TechTreesConfigRepository(string json) : base(json)
		{
			base.Load();
			_techTreesConfig = base.Data;
		}

		public ITechNodeConfig GetTechNodeConfig(string treeId, string nodeId)
		{
			var tree = GetTechTreeConfig(treeId);
			if (tree == null) return null;
			tree.Techs.TryGetValue(nodeId, out var node);
			return node;
		}

		public ITechTreeConfig GetTechTreeConfig(string treeId)
		{
			if (_techTreesConfig?.TechTrees == null || string.IsNullOrWhiteSpace(treeId)) return null;
			_techTreesConfig.TechTrees.TryGetValue(treeId, out var tree);
			return tree;
		}

		/// <summary>（v0.3 / WP-1.4）全表视图：供启动期校验（表非空 / 前置闭合）使用。</summary>
		public IEnumerable<string> GetTreeIds()
			=> _techTreesConfig?.TechTrees?.Keys ?? Enumerable.Empty<string>();
	}
}