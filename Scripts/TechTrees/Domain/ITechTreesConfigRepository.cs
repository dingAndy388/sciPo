namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechTreesConfigRepository
	{
		ITechNodeConfig GetTechNodeConfig(string treeId, string nodeId);
		ITechTreeConfig GetTechTreeConfig(string treeId);
	}
}