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
		private Camera2D _camera;

		private Dictionary<IPosition, MapCellView> _cells;

		[Export] public PackedScene CellScene { get; set; }
		[Export] public string MapID;

		public override void _Ready()
		{
			GD.Print("run");
			_cellContainer = GetNode<Node2D>("MapCells");
			_camera = GetNode<Camera2D>("Camera2D");

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
			GD.Print("instantiated");
			MapCellView cell = CellScene.Instantiate<MapCellView>();
			AddChild(cell);
			cell.CellPosition = position;
			cell.SetPosition();
			GD.Print(cell.Position);
			cell.SetTerrain(terrain);
			_cells[position] = cell;
		}
		public override void _Input(InputEvent @event)
		{
			if (@event is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Right))
			{
				_camera.Position -= motion.Relative;
			}
		}

	}
}

