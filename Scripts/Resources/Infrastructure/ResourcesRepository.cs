using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Resources.Domain;

namespace SciencePotato.Scripts.Resources.Infrastructure
{
	public class ResourcesRepository : GenericJsonRepository<ResourcesPool>, IResourcesRepository
	{
		private readonly string _filePath;

		/// <param name="filePath">旧口径：文件前缀（`res_` + mapId + `_` + ownerId）。</param>
		/// <param name="store">（v0.3 / WP-3.3）统一存档单元；分区键 = `resources:{mapId}_{ownerId}`。</param>
		public ResourcesRepository(string filePath, ISaveStore store = null) : base(store)
		{
			_filePath = filePath;
		}

		private string BuildKey(int ownerId)
		{
			return $"{ownerId}_pool";
		}

		private string BuildFilePath(string mapId, int ownerId)
		{
			return UsesSaveStore ? $"resources:{mapId}_{ownerId}" : _filePath + mapId + "_" + ownerId;
		}

		public ResourcesPool LoadResourcesPool(string mapId, int ownerId)
		{
			var filePath = BuildFilePath(mapId, ownerId);
			base.Load(filePath);
			return base.GetById(BuildKey(ownerId));
		}

		public void SaveResources(string mapId, int ownerId, ResourcesPool pool)
		{
			var filePath = BuildFilePath(mapId, ownerId);
			base.AddOrUpdate(BuildKey(ownerId), pool, filePath);
		}
	}
}