using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Resources.Domain;

namespace SciencePotato.Scripts.Resources.Infrastructure
{
	public class ResourcesRepository : GenericJsonRepository<ResourcesPool>, IResourcesRepository
	{
		private readonly string _filePath;

		public ResourcesRepository(string filePath)
		{
			_filePath = filePath;
		}

		private string BuildKey(int ownerId)
		{
			return $"{ownerId}_pool";
		}

		private string BuildFilePath(string mapId, int ownerId)
		{
			return _filePath + mapId + "_" + ownerId;
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