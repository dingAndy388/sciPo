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
		private Dictionary<string, IMapOccupant> _occupants = new();

		public int seed;
		public int width, height;
		public readonly string Id;

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
		}

		public MapCell GetCell(HexCubePosition position)
		{
			return _cells[position];
		}

		public IEnumerable<MapCell> GetAllCells()
		{
			return _cells.Values;
		}

		public bool TryGetCell(HexCubePosition position, out MapCell cell)
		{
			return _cells.TryGetValue(position, out cell);
		}

		public ITerrainData GetTerrain(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out _))
				return null;
			return _cells[position].Terrain;
		}

		public MapOccupantInfo? GetOccupantInfo(HexCubePosition position)
		{
			if(_cells.TryGetValue(position, out MapCell cell))
				if (cell.Occupant!=null)
					return cell.Occupant.GetInfo();
			return null;
		}

		public IMapOccupant GetOccupantByUId(string uid)
		{
			return _occupants[uid];
		}	

		/// <summary>
		/// （v0.3 / WP-2.2）**空安全**的按 uid 查询：给"任务回调里核对实体是否还在"用
		/// （攻击/移动循环在宿主消失后要自行注销，不能因为查不到就抛异常把整个日派发打断）。
		/// <para>注意：这只是**查询**侧的安全网，不改占用权威 —— "僵尸索引"（移除后仍留在 `_occupants` 里）
		/// 由 `WP-3.4` 统一修（`MAP-03` / `UNIT-05`）。</para>
		/// </summary>
		public bool TryGetOccupantByUId(string uid, out IMapOccupant occupant)
		{
			if (string.IsNullOrEmpty(uid))
			{
				occupant = null;
				return false;
			}
			return _occupants.TryGetValue(uid, out occupant);
		}

		public void AddOccupant(IMapOccupant occupant,HexCubePosition position)
		{
			if (_cells.TryGetValue(position, out _))
				_cells[position].SetOccupant(occupant);
			_occupants[occupant.GetInfo().UId] = occupant;
		}

		public void RemoveOccupantByPosition(HexCubePosition position)
		{
			if (_cells.TryGetValue(position, out MapCell cell))
				if (cell.Occupant!=null)
					cell.RemoveOccupant();
		}

		public void SetBuilding(HexCubePosition position, IMapOccupant building)
		{
			if (_cells.TryGetValue(position, out _))
				_cells[position].SetBuilding(building);
		}

		public void RemoveBuilding(HexCubePosition position)
		{
			if (_cells.TryGetValue(position, out MapCell cell)) 
			{
				cell.RemoveBuilding();
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
			// （v0.3 / WP-2.7 验收时发现）与 GetOccupantInfo 对齐：格子上没有建筑时返回 null，
			// 而不是对着 null 调 GetInfo() 抛 NRE —— 任何"查询任意格"的调用方（UI / 建造校验 / 测试）都会踩到。
			if (_cells.TryGetValue(position, out MapCell cell) && cell.Building != null)
				return cell.Building.GetInfo();
			return null;
        }

		public void SetInvader(HexCubePosition position, IMapOccupant invader)
		{
			if (_cells.TryGetValue(position, out MapCell cell))
				cell.SetInvader(invader);
		}

		public void RemoveInvader(HexCubePosition position)
		{
			if (_cells.TryGetValue(position, out MapCell cell))
				cell.RemoveInvader();
		}
    }
}
