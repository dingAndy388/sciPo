namespace SciencePotato.Scripts.Fog.Domain
{
	public interface IFogRepository
	{
		FogSaveData LoadFog(string mapId, int ownerId);
		void SaveFog(string mapId, int ownerId, FogSaveData data);
	}
}