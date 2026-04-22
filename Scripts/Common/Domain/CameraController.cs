using Godot;

public partial class CameraController : Camera2D
{
	private float _zoomLevel = 1.0f;
	private const float ZoomIncrement = 0.1f;
	private const float MinZoom = 0.01f;
	private const float MaxZoom = 5.0f;

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("zoom_in"))
		{
			_zoomLevel = Mathf.Clamp(_zoomLevel * (1 - ZoomIncrement), MinZoom, MaxZoom);
			Zoom = new Vector2(_zoomLevel, _zoomLevel);
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed("zoom_out"))
		{
			_zoomLevel = Mathf.Clamp(_zoomLevel * (1 + ZoomIncrement), MinZoom, MaxZoom);
			Zoom = new Vector2(_zoomLevel, _zoomLevel);
			GetViewport().SetInputAsHandled();
		}
		else if (@event is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Right))
		{
			Position -= motion.Relative;
		}
	}
}
