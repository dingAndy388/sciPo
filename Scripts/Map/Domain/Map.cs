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

		/// <summary>
		/// （v0.3 / WP-3.4）**占据物进入的唯一入口**：写入 `cell.Occupant` 与 `_occupants` 索引，
		/// 并在目标格已被占用时**拒绝**（旧实现直接覆盖 → 一格两实体、索引与格子不一致）。
		/// </summary>
		/// <returns>是否放置成功。</returns>
		public bool PlaceOccupant(IMapOccupant occupant, HexCubePosition position)
		{
			if (occupant == null) return false;
			if (!_cells.TryGetValue(position, out MapCell cell)) return false;
			if (cell.Occupant != null) return false; // 一格一占据物（`MAP-03`：不再静默覆盖）

			cell.SetOccupant(occupant);
			_occupants[occupant.GetInfo().UId] = occupant;
			return true;
		}

		/// <summary>
		/// （v0.3 / WP-3.4）**建筑进入的唯一入口**：同时写 `cell.Building` 与占据物槽位。
		/// <para>旧实现只有建造路径写 `cell.Occupant`、`cell.Building` 恒空（`MAP-04`）→ `GetBuildingInfo` 查不到、
		/// 拆除整体失效；统一入口后两者从落位那一刻起就一致。</para>
		/// </summary>
		public bool PlaceBuilding(IMapOccupant building, HexCubePosition position)
		{
			if (building == null) return false;
			if (!_cells.TryGetValue(position, out MapCell cell)) return false;
			if (cell.Occupant != null && !ReferenceEquals(cell.Occupant, building)) return false;

			cell.SetBuilding(building);
			cell.SetOccupant(building);
			_occupants[building.GetInfo().UId] = building;
			return true;
		}

		/// <summary>
		/// （v0.3 / WP-3.4）**占据物离开的唯一入口**：格子、建筑槽位、`_occupants` 索引三处一起清。
		/// <para>旧实现只清 `cell.Occupant` → `_occupants` 里留下**僵尸索引**（`MAP-03`/`UNIT-05`）：
		/// 已阵亡的单位仍能按 uid 查到，任务/战斗回调于是对着尸体干活。</para>
		/// </summary>
		/// <returns>被移除的占据物（无则 null）。</returns>
		public IMapOccupant RemoveOccupant(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out MapCell cell)) return null;

			IMapOccupant occupant = cell.Occupant;
			if (occupant == null) return null;

			string uid = occupant.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants.Remove(uid);
			if (ReferenceEquals(cell.Building, occupant)) cell.RemoveBuilding();
			cell.RemoveOccupant();
			return occupant;
		}

		/// <summary>（v0.3 / WP-3.4）**建筑离开的唯一入口**：清 `cell.Building` + 占据物槽位 + 索引。</summary>
		/// <returns>被移除的建筑（无则 null）。</returns>
		public IMapOccupant RemoveBuildingAt(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out MapCell cell)) return null;

			IMapOccupant building = cell.Building;
			cell.RemoveBuilding();
			if (building == null) return null;

			string uid = building.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants.Remove(uid);
			if (ReferenceEquals(cell.Occupant, building)) cell.RemoveOccupant();
			return building;
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

		/// <summary>
		/// （v0.3 / WP-2.5）按**范围内总计**扣人口（与 <see cref="AddPopulationWithin"/> 同一口径的反向操作）：
		/// 先扣中心格，不足再按半径展开顺序扣其余格；扣到 0 为止（不会出现负人口）。
		/// <para>训练一个单位完成时消耗人口（`E2`）走这里；未来的"赤字减员"（`WP-3.10`）同样复用。</para>
		/// </summary>
		/// <returns>实际扣除的人数。</returns>
		public int ConsumePopulationWithin(HexCubePosition center, int radius, int amount)
		{
			if (amount <= 0) return 0;

			int remaining = amount;
			foreach (HexCubePosition pos in center.InRadius(radius))
			{
				if (remaining <= 0) break;
				if (!_cells.TryGetValue(pos, out MapCell cell) || cell.Population <= 0) continue;

				int taken = Math.Min(cell.Population, remaining);
				cell.SetPopulation(cell.Population - taken);
				remaining -= taken;
			}
			return amount - remaining;
		}

		/// <summary>（v0.3 / WP-2.3）范围内人口**总计**（含中心格）。</summary>
		public int GetPopulationWithin(HexCubePosition center, int radius)
		{
			int total = 0;
			foreach (HexCubePosition pos in center.InRadius(radius))
				if (_cells.TryGetValue(pos, out MapCell cell))
					total += cell.Population;

			return total;
		}

		/// <summary>
		/// （v0.3 / WP-2.3）按**范围内总计上限**增加人口（设计稿：营地「半径 1 格内总计 9 人」）。
		/// <para>旧实现是「每格各自到 cap」：半径 1 的 7 格各自可达 9 → 实际容量 63，与设计稿不符（`CON-05`）。
		/// 增长落在**中心格**（聚落中心 = 住房所在格）：设计稿描述的是聚落级总量，不是逐格配额。</para>
		/// </summary>
		/// <returns>实际增加的人数（0 = 已达上限或地块不存在）。</returns>
		public int AddPopulationWithin(HexCubePosition center, int radius, int cap, int amount)
		{
			if (amount <= 0 || cap <= 0) return 0;
			if (!_cells.TryGetValue(center, out MapCell cell)) return 0;

			int room = cap - GetPopulationWithin(center, radius);
			if (room <= 0) return 0;

			int added = Math.Min(room, amount);
			cell.AddPopulation(added);
			return added;
		}

		public void AddOccupant(IMapOccupant occupant,HexCubePosition position)
		{
			// v0.3 / WP-3.4：统一走"进入的唯一入口"（含占用冲突拒绝与索引一致）
			PlaceOccupant(occupant, position);
		}

		public void RemoveOccupantByPosition(HexCubePosition position)
		{
			// v0.3 / WP-3.4：统一走"离开的唯一入口"（含 `_occupants` 索引清理，修僵尸索引）
			RemoveOccupant(position);
		}

		public void SetBuilding(HexCubePosition position, IMapOccupant building)
		{
			// v0.3 / WP-3.4：建筑落位统一走 PlaceBuilding（同时写 cell.Building 与占据物槽位）
			PlaceBuilding(building, position);
		}

		public void RemoveBuilding(HexCubePosition position)
		{
			RemoveBuildingAt(position);
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
