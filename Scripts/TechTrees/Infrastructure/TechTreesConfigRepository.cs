using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.TechTree.Domain;

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
			_techTreesConfig.TechTrees.TryGetValue(treeId, out var tree);
			return tree;
		}
	}
}