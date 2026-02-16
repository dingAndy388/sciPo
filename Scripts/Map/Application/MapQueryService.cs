using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapQueryService()
	{
		// TODO
		// private IMapRepository _mapRepo;
		// Load unimplemented

		public MapCell GetMapCell(Domain.Map map, IPosition position)
		{
			return map.GetCell(position);
		}

		public IEnumerable<MapCell> GetAllCells (Domain.Map map)
		{
			return map.GetAllCells();
		}

		public Dictionary<ResourcesType, int> GetResourcesDict(Domain.Map map,IPosition position)
		{
			return map.GetResourcesDict(position);
		}

		public int GetResourcesAmount(Domain.Map map, IPosition position, ResourcesType resource)
		{
			var dict = map.GetResourcesDict(position);
			if (dict.TryGetValue(resource, out int value))
			{
				return value;
			}
			return 0;
		}
	}
}
