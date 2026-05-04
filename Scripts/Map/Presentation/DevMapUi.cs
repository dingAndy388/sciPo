using Godot;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Presentation;

public partial class DevMapUi : CanvasLayer
{
	private Button _btn;
	private SpinBox _seed;
	private LineEdit _id;
	private SpinBox _height;
	private SpinBox _width;

	private MapAppService _generator;
	private MapView _mapView;

	private Camera2D _camera;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_mapView = GetParent<MapView>();

		_btn = GetNode<Button>("BoxContainer/GenerateBtn");
		_seed = GetNode<SpinBox>("BoxContainer/SeedInput");
		_id = GetNode<LineEdit>("BoxContainer/IDInput");
		_height = GetNode<SpinBox>("BoxContainer/HeightInput");
		_width = GetNode<SpinBox>("BoxContainer/WidthInput");

		_generator = ServiceContainer.Instance.MapGeneration;
	}

	private void OnGenerateBtnPressed()
	{
		GD.Print("DevUI: Pressed");

		GD.Print("DevUI: Generating");
		_generator.GenerateMap((int)_seed.Value, (int)_width.Value, (int)_height.Value, _id.Text);
		_mapView.MapID = _id.Text;
		_mapView.UpdateAllCells();
		GD.Print("DevUI: Done");
	}
}
