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