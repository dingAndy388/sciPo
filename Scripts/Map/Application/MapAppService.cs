using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapAppService(IMapGenerator generator, IMapRepository repo)
	{
		private IMapGenerator _mapGenerator = generator;
		private IMapRepository _mapRepo = repo;

		public record CellTerrainChangedEvent(HexCubePosition Position, ITerrainData OldTerr, ITerrainData NewTerr);
		public event Action<CellTerrainChangedEvent> CellTerrainChanged;

		public void GenerateMap(int seed, int width, int height, string Id)
		{
			Domain.Map map = _mapGenerator.Generate(width, height, seed, Id);

			_mapRepo.SaveMap(map);
		}

		public MapCell GetMapCell(string MapId, HexCubePosition position)
		{
			return _mapRepo.LoadMap(MapId).GetCell(position);
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

			// Forward Event
			map.CellTerrainChanged += ForwardTerrainEvent;

			_mapRepo.SaveMap(map);
		}

		private void ForwardTerrainEvent(Domain.Map.CellTerrainChangedEvent evt)

		{
			CellTerrainChanged?.Invoke(new CellTerrainChangedEvent(evt.HexCubePosition, evt.OldTerr, evt.NewTerr));
		}

		public bool IsClear(string MapId,HexCubePosition posittion)
		{
			var map = _mapRepo.LoadMap(MapId);
			
			if(map.GetOccupantInfo(posittion) == null)
				return true;
			return false;
		}

		public MapOccupantInfo? GetOccupantInfo(string MapId, HexCubePosition position)
		{
			var map = _mapRepo.LoadMap(MapId);

			return map.GetOccupantInfo(position);
		}

		public void SetOccupant(string MapId, HexCubePosition position,IMapOccupant occupant)
		{
			var map = _mapRepo.LoadMap(MapId);

			map.SetOccupant(occupant, position);
		}

		public TerrainRequirement GetTerrainRequirement(string MapId, HexCubePosition position,string targetTerrain)
		{
			var map = _mapRepo.LoadMap(MapId);
			return new TerrainRequirement(map, position, targetTerrain);
		}
	}
}
