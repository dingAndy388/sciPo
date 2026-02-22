using Godot;
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
		public readonly string ID;

		// events
		public record CellTerrainChangedEvent (IPosition IPosition, ITerrainData OldTerr, ITerrainData NewTerr);
		public event Action<CellTerrainChangedEvent> CellTerrainChanged;

		public Map(int seed, int width, int height, string Id)
		{
			this.seed = seed;
			this.width = width;
			this.height = height;
			this.ID = Id;

			this._cells = [];
		}

		public void SetCell(IPosition pos, MapCell cell)
		{
			_cells[pos]=cell;
		}

		public void SetTerrain(IPosition position, ITerrainData terrain)
		{
			ITerrainData old = _cells[position].terrain;
			_cells[position].SetTerrain(terrain);
			CellTerrainChanged?.Invoke(new CellTerrainChangedEvent(position,old,terrain));
		}

		public MapCell GetCell(IPosition position)
		{ 
			return _cells[position];
		}

		public IEnumerable<MapCell> GetAllCells()
		{
			GD.Print("value:"+_cells.Values.Count);
			return _cells.Values;
		}

		public ITerrainData GetTerrain(IPosition position)
		{
			return _cells[position].terrain;
		}
	}
}
