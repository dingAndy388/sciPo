using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Application
{
    public class MapGenerationService (IMapGenerator generator, IMapRepository repo)
    {
        private IMapGenerator _mapGenerator = generator;
        private IMapRepository _repository = repo;

        public void MapGenerate(int seed, int height, int width, string Id)
        {
            Domain.Map map = _mapGenerator.Generate(seed, height, width, Id);
            
            _repository.SaveMap(map);
        }

        public void GenerateBlank(int seed, int height, int width, string Id)
        {
            if(_mapGenerator is RandomAnchorMapGenerator gen)
            {
                Domain.Map map = gen.GetBlankMap(height, width, seed, Id);
                _repository.SaveMap(map);
            }
        }
    }
}
