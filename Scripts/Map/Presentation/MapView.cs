using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Presentation
{
	public partial class MapView : Node2D
	{
		private MapAppService _mapQuery;
		private MapAppService _modificationService;

		private Node2D _cellContainer;

		private Dictionary<HexCubePosition, MapCellView> _cells;

		[Export] public PackedScene CellScene { get; set; }
		[Export] public string MapId;

		public override void _Ready()
		{
			_cellContainer = GetNode<Node2D>("MapCells");
			_cells = new();
		}

		public void UpdateAllCells()
		{
			ClearAllCells();
			foreach (var cell in _mapQuery.GetAllCells(MapId))
			{
				CreateCellView(cell.Terrain, cell.Position);
			}
		}

		public void ClearAllCells()
		{
			_cells.Clear();
			foreach (var child in GetChildren())
			{
				if (child.GetType() == typeof(MapCellView))
				{
					child.QueueFree();
				}
			}
		}

		public void UpdateCell(HexCubePosition position)
		{
			MapCell cell = _mapQuery.GetMapCell(MapId, position);
			CreateCellView(cell.Terrain, cell.Position);
		}

		public void CreateCellView(ITerrainData terrain, HexCubePosition position)
		{
			MapCellView cell = CellScene.Instantiate<MapCellView>();
			AddChild(cell);
			cell.CellPosition = position;
			cell.SetPosition();
			GD.Print($"MapView: instantiated cell at {cell.Position}");
			cell.SetTerrain(terrain);
			_cells[position] = cell;
		}

	}
}

