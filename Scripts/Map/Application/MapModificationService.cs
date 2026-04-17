using Godot;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapModificationService(IMapRepository mapRepo)
	{
        public record CellTerrainChangedEvent(IPosition Position, ITerrainData OldTerr, ITerrainData NewTerr);
        public event Action<CellTerrainChangedEvent> CellTerrainChanged;

        private IMapRepository _mapRepo = mapRepo;

		public void SetTerrain(string MapID,IPosition position, ITerrainData terrain)
		{
			var map = _mapRepo.LoadMap(MapID);
			ITerrainData oldTer = map.GetTerrain(position);
            map.SetTerrain(position, terrain);

			// Forward Event
			map.CellTerrainChanged += ForwardTerrainEvent;

			_mapRepo.SaveMap(map);
		}

		private void ForwardTerrainEvent(Domain.Map.CellTerrainChangedEvent evt)

		{
			CellTerrainChanged?.Invoke(new CellTerrainChangedEvent(evt.IPosition,evt.OldTerr,evt.NewTerr));
		}


	}
}
