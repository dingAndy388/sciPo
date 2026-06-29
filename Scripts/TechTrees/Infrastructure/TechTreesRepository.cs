using SciencePotato.Scripts.Common.Infrastructure;
using TechTreeDomain = SciencePotato.Scripts.TechTree.Domain;

namespace SciencePotato.Scripts.TechTree.Infrastructure
{
	public class TechTreesRepository : GenericJsonRepository<TechTreeDomain.TechTree>, TechTreeDomain.ITechTreesRepository
	{
		private readonly string _filePath;
		private readonly TechTreeDomain.ITechTreesConfigRepository _configRepo;

		public TechTreesRepository(string filePath, TechTreeDomain.ITechTreesConfigRepository configRepo)
		{
			_filePath = filePath;
			_configRepo = configRepo;
		}

		private string BuildKey(int ownerId, string treeId)
		{
			return $"{ownerId}_{treeId}";
		}

		private string BuildFilePath(string mapId, int ownerId)
		{
			return _filePath + mapId + "_" + ownerId;
		}

		public TechTreeDomain.TechTree GetTreeById(string mapId, int ownerId, string id)
		{
			var filePath = BuildFilePath(mapId, ownerId);
			base.Load(filePath);

			var key = BuildKey(ownerId, id);
			var tree = base.GetById(key);

			if (tree != null)
			{
				tree.HydrateConfigs(_configRepo);
			}

			return tree;
		}

		public void SaveTree(string mapId, int ownerId, string id, TechTreeDomain.TechTree tree)
		{
			var filePath = BuildFilePath(mapId, ownerId);
			var key = BuildKey(ownerId, id);
			base.AddOrUpdate(key, tree, filePath);
		}
	}
}