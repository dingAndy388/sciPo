using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapGenerationService(IMapGenerator generator, IMapRepository repo)
	{
		private IMapGenerator _mapGenerator = generator;
		private IMapRepository _repository = repo;

		public void GenerateMap(int seed, int width, int height, string Id)
		{
			Domain.Map map = _mapGenerator.Generate(width, height, seed, Id);

			_repository.SaveMap(map);
		}
	}
}
