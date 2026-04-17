using Godot;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Presentation
{
	public partial class MapView : Node2D
	{
		private MapQueryService _mapQuery;
		private MapModificationService _modificationService;

		private Node2D _cellContainer;

		private Dictionary<IPosition, MapCellView> _cells;

		[Export] public PackedScene CellScene { get; set; }
		[Export] public string MapID;

		public override void _Ready()
		{
			_cellContainer = GetNode<Node2D>("MapCells");

			_mapQuery = ServiceContainer.Instance.MapQuery;
			_modificationService = ServiceContainer.Instance.MapMod;

			_modificationService.CellTerrainChanged += HandleTerrainChanged;

			_cells = new(); 
		}

        private void HandleTerrainChanged(MapModificationService.CellTerrainChangedEvent evt)
        {
			UpdateCell(evt.Position);
        }

        public void UpdateAllCells()
		{
			ClearAllCells();
			foreach (var cell in _mapQuery.GetAllCells(MapID))
			{
				CreateCellView(cell.terrain, cell.position);
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

		public void UpdateCell(IPosition position)
		{
			MapCell cell = _mapQuery.GetMapCell(MapID, position);
            CreateCellView(cell.terrain, cell.position);
        }

        public void CreateCellView(ITerrainData terrain,IPosition position)
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

