using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Domain
{
	// This is the aggregate root of map domain
	public class Map
	{
		// aggregate of mapcell
		private Dictionary<HexCubePosition, MapCell> _cells;

		//dict of all occupants
		private Dictionary<long, IMapOccupant> _occupants = new();

		public int seed;
		public int width, height;
		public readonly string Id;

		// events
		public record CellTerrainChangedEvent(HexCubePosition HexCubePosition, ITerrainData OldTerr, ITerrainData NewTerr);
		public event Action<CellTerrainChangedEvent> CellTerrainChanged;
		public event Action<MapOccupantInfo> OccupantRemoved;

		public Map(int seed, int width, int height, string Id)
		{
			this.seed = seed;
			this.width = width;
			this.height = height;
			this.Id = Id;

			this._cells = [];
		}

		public void SetCell(HexCubePosition pos, MapCell cell)
		{
			_cells[pos] = cell;
		}

		public void SetTerrain(HexCubePosition position, ITerrainData terrain)
		{
			if (!_cells.TryGetValue(position, out _))
				return;
			ITerrainData old = _cells[position].Terrain;
			_cells[position].SetTerrain(terrain);
			CellTerrainChanged?.Invoke(new CellTerrainChangedEvent(position, old, terrain));
		}

		public MapCell GetCell(HexCubePosition position)
		{
			return _cells[position];
		}

		public IEnumerable<MapCell> GetAllCells()
		{
			return _cells.Values;
		}

		public ITerrainData GetTerrain(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out _))
				return null;
			return _cells[position].Terrain;
		}

		public IEnumerable<MapOccupantInfo> GetOccupantInfo(HexCubePosition position)
		{
			if(_cells.TryGetValue(position, out _))
				if (_cells[position].Occupants!=null)
					return from occ in _cells[position].Occupants select occ.GetInfo();
			return null;
		}

		public IMapOccupant GetOccupantByUId(long uid)
		{
			return _occupants[uid];
		}	

		public void AddOccupant(IMapOccupant occupant,HexCubePosition position)
		{
			if (_cells.TryGetValue(position, out _))
				_cells[position].AddOccupant(occupant);
			_occupants[occupant.GetInfo().UId] = occupant;
		}

		public void RemoveOccupantByPosition(HexCubePosition position, IMapOccupant occupant)
		{
			if (_cells.TryGetValue(position, out _))
				if (_cells[position].Occupants.Contains(occupant))
					_cells[position].RemoveOccupant(occupant);
		}

		public void SetBuilding(HexCubePosition position, IMapOccupant building)
		{
			if (_cells.TryGetValue(position, out _))
				_cells[position].SetBuilding(building);
		}

		public void RemoveBuilding(HexCubePosition position)
		{
			if (_cells.TryGetValue(position, out _)) 
			{
				var info = _cells[position].GetBuildingInfo();
				if(info.HasValue)
				{
                    _cells[position].RemoveBuilding();
                    OccupantRemoved?.Invoke(info.Value);
                }
            }
        }

		public bool VerifyTerrain(HexCubePosition position, string targetTerrain)
		{
			if(_cells.TryGetValue(position,out _))
				if (_cells[position].Terrain.Id==targetTerrain)
					return true;
			return false;
		}

        public MapOccupantInfo? GetBuildingInfo(HexCubePosition position)
        {
			if (_cells.TryGetValue(position, out _))
				return _cells[position].GetBuildingInfo();
			return null;
        }
    }
}
