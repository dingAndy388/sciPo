namespace SciencePotato.Scripts.Map.Domain
{
	public interface IMapGenerator
	{
		Map Generate(int width, int height, int seed, string Id);
	}
}
