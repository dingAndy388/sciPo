using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapModificationService()
	{

		// TODO
		// private IMapRepository _mapRepo;
		// Save unimplemented

		public void SetTerrain(Domain.Map map,IPosition position, ITerrainData terrain)
		{
			map.SetTerrain(position, terrain);
		}

		public void SetResources(Domain.Map map,IPosition position, ResourcesType resources, int value)
		{
			map.SetResources(position, resources, value);
		}

		public void SetResourcesList(Domain.Map map,IPosition position, Dictionary<ResourcesType,int> resourcesList)
		{
			foreach (var resource in resourcesList)
			{
				map.SetResources(position,resource.Key, resource.Value);
			}
		}
	}
}
