using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Xml.Schema;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapAppService(IMapGenerator generator, IMapRepository repo)
	{
		private IMapGenerator _mapGenerator = generator;
		private IMapRepository _mapRepo = repo;

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
			ITerrainData oldTer = map.GetTerrain(position);
			map.SetTerrain(position, terrain);

			_mapRepo.SaveMap(map);
		}

		public bool IsClear(string mapId,HexCubePosition posittion)
		{
			var map = _mapRepo.LoadMap(mapId);
			
			if(map.GetOccupantInfo(posittion) == null)
				return true;
			return false;
		}

		public IEnumerable<MapOccupantInfo> GetOccupantInfo(string apId, HexCubePosition position)
		{
			var map = _mapRepo.LoadMap(apId);

			return map.GetOccupantInfo(position);
		}

		public IMapOccupant GetOccupantByUId(string mapId, long uid)
		{
			var map = _mapRepo.LoadMap(mapId);
			return map.GetOccupantByUId(uid);
		}

		public void SetOccupant(string MapId, HexCubePosition position,IMapOccupant occupant)
		{
			var map = _mapRepo.LoadMap(MapId);

			map.AddOccupant(occupant, position);
		}

		public TerrainRequirement GetTerrainRequirement(string mapId, HexCubePosition position,string targetTerrain)
		{
			var map = _mapRepo.LoadMap(mapId);
			return new TerrainRequirement(map, position, targetTerrain);
		}

        public void RemoveOccupantByPosition(string mapId, HexCubePosition position, IMapOccupant occupant)
        {
			var map = _mapRepo.LoadMap(mapId);
			map.RemoveOccupantByPosition(position, occupant);
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
    }
}
