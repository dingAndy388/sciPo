using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapQueryService(IMapRepository mapRepo)
	{
		private IMapRepository _mapRepo = mapRepo;

		public MapCell GetMapCell(string MapID, IPosition position)
		{
			return _mapRepo.LoadMap(MapID).GetCell(position);
		}

		public IEnumerable<MapCell> GetAllCells(string MapID)
		{
			return _mapRepo.LoadMap(MapID).GetAllCells();
		}
	}
}
