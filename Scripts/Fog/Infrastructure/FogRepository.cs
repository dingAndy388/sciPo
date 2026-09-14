using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Fog.Domain;

namespace SciencePotato.Scripts.Fog.Infrastructure
{
	public class FogRepository : GenericJsonRepository<FogSaveData>, IFogRepository
	{
		private readonly string _filePath;

		/// <param name="filePath">旧口径：文件前缀（`fog_` + mapId + `_` + ownerId）。</param>
		/// <param name="store">（v0.3 / WP-3.3）统一存档单元；分区键 = `fog:{mapId}_{ownerId}`。</param>
		public FogRepository(string filePath, ISaveStore store = null) : base(store)
		{
			_filePath = filePath;
		}

		private string BuildKey(int ownerId)
		{
			return $"{ownerId}_fog";
		}

		private string BuildFilePath(string mapId, int ownerId)
		{
			return UsesSaveStore ? $"fog:{mapId}_{ownerId}" : _filePath + mapId + "_" + ownerId;
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