using Godot;
using SciencePotato.Scripts.Map.Domain;
using System;

public partial class MapCellView : Node2D
{
    private Sprite2D _sprite;

    public ITerrainData Terrain { get; private set; }
    public IPosition CellPosition { get; set; }


    // Temporary fixed constant
    public const int height = 100;
    public const int width = 115;

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
        GD.Print(_sprite.Texture);
    }

    public void SetPosition()
    {
        // Set to the right Position with correct seperation
        int row = CellPosition.ToCoordinate().Item1;
        int col = CellPosition.ToCoordinate().Item2;
        base.Position = new Vector2(row * width*3/4, col * height+(row % 2)*height/2);
    }
}
