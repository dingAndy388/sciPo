namespace SciencePotato.Scripts.Resources.Domain
{
	public interface IResourcesRepository
	{
		void SaveResources(string mapId, int ownerId, ResourcesPool pool);
		ResourcesPool LoadResourcesPool(string mapId, int ownerId);
	}
}