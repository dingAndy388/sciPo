using Godot;
using SciencePotato.Scripts.Map.Application;
using System;

namespace SciencePotato.Scripts.Map.Presentation
{
	public partial class MapView : Node2D
	{
		private MapQueryService _mapQuery;
		private MapModificationService _modificationService;

		private Node2D _cellContainer;
		private Camera2D _camera;

		[Export] public PackedScene cellScene { get; set; }

		public override void _Ready()
		{
			_cellContainer = GetNode<Node2D>("MapCells");
			_camera = GetNode<Camera2D>("Camera");

			_mapQuery = ServiceContainer.Instance.mapQuery;
			_modificationService = ServiceContainer.Instance.MapMod;
		}


	}
}

