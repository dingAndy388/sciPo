using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapAppService(IMapGenerator generator, IMapRepository repo)
	{
		private readonly IMapGenerator _mapGenerator = generator;
		private readonly IMapRepository _mapRepo = repo;

		private const byte FogUnexplored = 0;

		public void GenerateMap(int seed, int width, int height, string Id)
		{
			Domain.Map map = _mapGenerator.Generate(width, height, seed, Id);
			_mapRepo.SaveMap(map);
		}

		public MapCell GetMapCell(string mapId, HexCubePosition position)
		{
			return _mapRepo.LoadMap(mapId).GetCell(position);
		}

		public IEnumerable<MapCell> GetAllCells(string MapId)
		{
			return _mapRepo.LoadMap(MapId).GetAllCells();
		}

		public void SetTerrain(string MapId, HexCubePosition position, ITerrainData terrain)
		{
			var map = _mapRepo.LoadMap(MapId);
			map.SetTerrain(position, terrain);
			_mapRepo.SaveMap(map);
		}

		public bool IsClear(string mapId, HexCubePosition position)
		{
			var map = _mapRepo.LoadMap(mapId);
			return map.GetOccupantInfo(position) == null;
		}

		public MapOccupantInfo? GetOccupantInfo(string mapId, HexCubePosition position)
		{
			var map = _mapRepo.LoadMap(mapId);
			return map.GetOccupantInfo(position);
		}

		public IMapOccupant GetOccupantByUId(string mapId, string uid)
		{
			var map = _mapRepo.LoadMap(mapId);
			return map.GetOccupantByUId(uid);
		}

		public void SetOccupant(string MapId, HexCubePosition position, IMapOccupant occupant)
		{
			var map = _mapRepo.LoadMap(MapId);
			map.AddOccupant(occupant, position);
		}

		public TerrainRequirement GetTerrainRequirement(string mapId, HexCubePosition position, string targetTerrain)
		{
			var map = _mapRepo.LoadMap(mapId);
			return new TerrainRequirement(map, position, targetTerrain);
		}

		public void RemoveOccupantByPosition(string mapId, HexCubePosition position, IMapOccupant occupant)
		{
			var map = _mapRepo.LoadMap(mapId);
			map.RemoveOccupantByPosition(position);
		}

		public void RemoveBuilding(string mapId, HexCubePosition position)
		{
			var map = _mapRepo.LoadMap(mapId);
			map.RemoveBuilding(position);
		}

		public MapOccupantInfo? GetBuildingInfo(string mapId, HexCubePosition position)
		{
			var map = _mapRepo.LoadMap(mapId);
			return map.GetBuildingInfo(position);
		}

		public void SetInvader(string mapId, HexCubePosition position, IMapOccupant invader)
		{
			var map = _mapRepo.LoadMap(mapId);
			map.SetInvader(position, invader);
		}

		public void RemoveInvader(string mapId, HexCubePosition position)
		{
			var map = _mapRepo.LoadMap(mapId);
			map.RemoveInvader(position);
		}

		public IConsumable CreatePopulationConsumption(string mapId, HexCubePosition position, int amount)
		{
			var map = _mapRepo.LoadMap(mapId);
			var cell = map.GetCell(position);
			return new PopulationConsumption(cell, amount);
		}

		public List<HexCubePosition> FindPath(string mapId, HexCubePosition start, HexCubePosition end, FogAppService fog)
		{
			var map = _mapRepo.LoadMap(mapId);

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

			return cell.Terrain != null && cell.Terrain.MoveCost > 0f;
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