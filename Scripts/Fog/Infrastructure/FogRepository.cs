using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Fog.Domain;

namespace SciencePotato.Scripts.Fog.Infrastructure
{
	public class FogRepository : GenericJsonRepository<FogSaveData>, IFogRepository
	{
		private readonly string _filePath;

		public FogRepository(string filePath)
		{
			_filePath = filePath;
		}

		private string BuildKey(int ownerId)
		{
			return $"{ownerId}_fog";
		}

		private string BuildFilePath(string mapId, int ownerId)
		{
			return _filePath + mapId + "_" + ownerId;
		}

		public FogSaveData LoadFog(string mapId, int ownerId)
		{
			var filePath = BuildFilePath(mapId, ownerId);
			base.Load(filePath);
			return base.GetById(BuildKey(ownerId)) ?? new FogSaveData();
		}

		public void SaveFog(string mapId, int ownerId, FogSaveData data)
		{
			var filePath = BuildFilePath(mapId, ownerId);
			base.AddOrUpdate(BuildKey(ownerId), data, filePath);
		}
	}
}