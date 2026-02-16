using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace SciencePotato.Scripts.Map.Domain
{
	// This is the aggregate root of map domain
	public class Map
	{
		// aggregate of mapcell
		private Dictionary<IPosition,MapCell> _cells;

		public int seed;
		public int width, height;

		// events
		public record CellTerrainChangedEvent (IPosition IPosition, ITerrainData OldTerr, ITerrainData NewTerr);
		public event Action<CellTerrainChangedEvent> CellTerrainChanged;
		public record CellResourcesChangedEvent (IPosition IPosition);
		public event Action<CellResourcesChangedEvent> CellResourcesChanged;

		public Map(int seed, int width, int height)
		{
			this.seed = seed;
			this.width = width;
			this.height = height;

			this._cells = [];
		}

		public void SetCell(IPosition pos, MapCell cell)
		{
			_cells[pos]=cell;
		}

		public void SetTerrain(IPosition position, ITerrainData terrain)
		{
			ITerrainData old = _cells[position].terrain;
			_cells[position].terrain = terrain;
			CellTerrainChanged?.Invoke(new CellTerrainChangedEvent(position,old,terrain));
		}

		public void SetResources(IPosition position, ResourcesType resources, int value)
		{
			_cells[position].resources[resources] = value;
			CellResourcesChanged?.Invoke(new CellResourcesChangedEvent(position));
		}

		public MapCell GetCell(IPosition position)
		{ 
			return _cells[position];
		}

		public IEnumerable<MapCell> GetAllCells()
		{
			return _cells.Values;
		}

		public ITerrainData GetTerrain(IPosition position)
		{
			return _cells[position].terrain;
		}

		public Dictionary<ResourcesType, int> GetResourcesDict(IPosition	position)
		{
			return _cells[position].resources;
		}
	}
}
