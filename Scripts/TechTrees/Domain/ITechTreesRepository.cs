namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechTreesRepository
	{
		TechTree GetTreeById(string mapId, int ownerId, string id);
		void SaveTree(string mapId, int ownerId, string id, TechTree tree);
	}
}