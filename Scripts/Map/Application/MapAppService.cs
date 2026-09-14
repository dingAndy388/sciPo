using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapAppService(IMapGenerator generator, MapSession session)
	{
		private readonly IMapGenerator _mapGenerator = generator;
		private readonly MapSession _session = session;

		private const byte FogUnexplored = 0;

		public void GenerateMap(int seed, int width, int height, string Id)
		{
			Domain.Map map = _mapGenerator.Generate(width, height, seed, Id);

			// v0.3 / WP-3.1：新生成的地图放入会话缓存，并在存档点落盘
			_session.Set(Id, map);
			_session.MarkDirty(Id);
			_session.Flush(Id);
		}

		public MapCell GetMapCell(string mapId, HexCubePosition position)
		{
			return _session.Get(mapId).GetCell(position);
		}

		public IEnumerable<MapCell> GetAllCells(string MapId)
		{
			return _session.Get(MapId).GetAllCells();
		}

		public void SetTerrain(string MapId, HexCubePosition position, ITerrainData terrain)
		{
			var map = _session.Get(MapId);
			map.SetTerrain(position, terrain);
			_session.MarkDirty(map.Id);
		}

		public bool IsClear(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map.GetOccupantInfo(position) == null;
		}

		public MapOccupantInfo? GetOccupantInfo(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map.GetOccupantInfo(position);
		}

		public IMapOccupant GetOccupantByUId(string mapId, string uid)
		{
			var map = _session.Get(mapId);
			return map.GetOccupantByUId(uid);
		}

		/// <summary>
		/// （v0.3 / WP-2.2）**空安全**查询：任务回调里核对"实体是否还在"时必须用它
		/// （查不到的 uid 返回 <c>null</c>，而不是抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>）。
		/// </summary>
		public IMapOccupant FindOccupantByUId(string mapId, string uid)
		{
			var map = _session.Get(mapId);
			return map != null && map.TryGetOccupantByUId(uid, out IMapOccupant occupant) ? occupant : null;
		}

		/// <summary>
		/// （v0.3 / WP-2.3）**唯一的人口写入点**（`CON-05`）：走 <see cref="Map.AddPopulationWithin"/> 的
		/// 「范围内总计上限」口径，并在真正变化时把地图标记为脏 —— 否则人口改动不会被下一个存档点写盘
		/// （这是"人口不落盘"的一半；另一半是实体序列化，归 `WP-3.2`）。
		/// </summary>
		/// <returns>实际增加的人数。</returns>
		public int AddPopulation(string mapId, HexCubePosition center, int radius, int cap, int amount)
		{
			var map = _session.Get(mapId);
			if (map == null) return 0;

			int added = map.AddPopulationWithin(center, radius, cap, amount);
			if (added > 0) _session.MarkDirty(mapId);
			return added;
		}

		/// <summary>（v0.3 / WP-2.3）范围内人口总计（只读；供断言与未来的 UI/结算器使用）。</summary>
		public int GetPopulationWithin(string mapId, HexCubePosition center, int radius)
		{
			var map = _session.Get(mapId);
			return map?.GetPopulationWithin(center, radius) ?? 0;
		}

		/// <summary>
		/// （v0.3 / WP-2.5）扣人口（训练完成时的"人口 −1"，`E2`）：走 <see cref="Map.ConsumePopulationWithin"/>，
		/// 有变化即打脏标记（与 <see cref="AddPopulation"/> 同一套"唯一写入点 + 存档点"约定）。
		/// </summary>
		/// <returns>实际扣除的人数。</returns>
		public int ConsumePopulation(string mapId, HexCubePosition center, int radius, int amount)
		{
			var map = _session.Get(mapId);
			if (map == null) return 0;

			int consumed = map.ConsumePopulationWithin(center, radius, amount);
			if (consumed > 0) _session.MarkDirty(mapId);
			return consumed;
		}

		public void SetOccupant(string MapId, HexCubePosition position, IMapOccupant occupant)
		{
			var map = _session.Get(MapId);
			map.AddOccupant(occupant, position);
			_session.MarkDirty(MapId);
		}

		public TerrainRequirement GetTerrainRequirement(string mapId, HexCubePosition position, string targetTerrain)
		{
			var map = _session.Get(mapId);
			return new TerrainRequirement(map, position, targetTerrain);
		}

		public void RemoveOccupantByPosition(string mapId, HexCubePosition position, IMapOccupant occupant)
		{
			var map = _session.Get(mapId);
			map.RemoveOccupantByPosition(position);
			_session.MarkDirty(mapId);
		}

		public void RemoveBuilding(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			map.RemoveBuilding(position);
			_session.MarkDirty(mapId);
		}

		public MapOccupantInfo? GetBuildingInfo(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map.GetBuildingInfo(position);
		}

		public void SetInvader(string mapId, HexCubePosition position, IMapOccupant invader)
		{
			var map = _session.Get(mapId);
			map.SetInvader(position, invader);
			_session.MarkDirty(mapId);
		}

		public void RemoveInvader(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			map.RemoveInvader(position);
			_session.MarkDirty(mapId);
		}

		public IConsumable CreatePopulationConsumption(string mapId, HexCubePosition position, int amount)
		{
			var map = _session.Get(mapId);
			var cell = map.GetCell(position);
			return new PopulationConsumption(cell, amount);
		}

		public List<HexCubePosition> FindPath(string mapId, HexCubePosition start, HexCubePosition end, FogAppService fog)
		{
			var map = _session.Get(mapId);

			if (start == end)
				return new List<HexCubePosition> { start };

			var openSet = new PriorityQueue<HexCubePosition, float>();
			var gScore = new Dictionary<HexCubePosition, float>();
			var cameFrom = new Dictionary<HexCubePosition, HexCubePosition>();

			gScore[start] = 0f;
			openSet.Enqueue(start, start.DistenceTo(end));

			while (openSet.Count > 0)
			{
				var current = openSet.Dequeue();
				if (current == end)
					return ReconstructPath(cameFrom, current);

				foreach (var neighbor in current.GetNeighbor())
				{
					if (!IsPassable(map, neighbor, fog))
						continue;

					float moveCost = GetMoveCost(map, neighbor);
					float tentativeG = (gScore.TryGetValue(current, out float g) ? g : float.MaxValue) + moveCost;

					if (!gScore.TryGetValue(neighbor, out float neighborG) || tentativeG < neighborG)
					{
						gScore[neighbor] = tentativeG;
						cameFrom[neighbor] = current;
						float fScore = tentativeG + neighbor.DistenceTo(end);
						openSet.Enqueue(neighbor, fScore);
					}
				}
			}

			return new List<HexCubePosition>();
		}

		private static bool IsPassable(Domain.Map map, HexCubePosition pos, FogAppService fog)
		{
			if (!map.TryGetCell(pos, out MapCell cell))
				return true;

			byte visibility = fog.GetVisibility(pos);
			if (visibility == FogUnexplored)
				return true;

			return cell.Terrain != null && cell.Terrain.Passable;
		}

		private static float GetMoveCost(Domain.Map map, HexCubePosition pos)
		{
			if (!map.TryGetCell(pos, out MapCell cell))
				return 1f;

			return cell.Terrain?.MoveCost ?? 1f;
		}

		private static List<HexCubePosition> ReconstructPath(Dictionary<HexCubePosition, HexCubePosition> cameFrom, HexCubePosition current)
		{
			var path = new List<HexCubePosition> { current };
			while (cameFrom.TryGetValue(path[0], out var prev))
			{
				path.Insert(0, prev);
			}
			return path;
		}
	}
}