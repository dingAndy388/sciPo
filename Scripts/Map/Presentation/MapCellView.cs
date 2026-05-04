using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;

public partial class MapCellView : Node2D
{
	private Sprite2D _sprite;

	public ITerrainData Terrain { get; private set; }
	public HexCubePosition CellPosition { get; set; }


	// Temporary fixed constant
	public const float height = 366;
	public const float width = 423;

	private readonly string textureDir = "res://Texture/Terrain/";

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void SetTerrain(ITerrainData terrain)
	{
		_sprite = GetNode<Sprite2D>("Sprite2D");

		this.Terrain = terrain;

		// Chnage Sprite Texture
		Texture2D texture = GD.Load<Texture2D>($"{textureDir}{terrain.Id}.png");
		_sprite.Texture = texture;
	}

	public void SetPosition()
	{
		// Set to the right Position with correct seperation
		int q = CellPosition.ToCoordinate().Item1;
		int r = CellPosition.ToCoordinate().Item2;
		base.Position = new Vector2(height / 2 * r - height * (float)Math.Ceiling((float)r * 0.5d) + height * q, r * width * 3 / 4);
	}
}
