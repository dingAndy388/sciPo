using Godot;

/// <summary>
/// （v0.6.0 / WP-5.2 重写）**相机操作**：滚轮缩放 + WASD 平移 + 左键拖动平移。
/// <para>操作口径（M1 的"能看清整张 73×143 地图"是硬需求，`WP-5.8` 会在此基础上验规模）：</para>
/// <list type="bullet">
/// <item>缩放：`zoom_in` / `zoom_out` 输入动作（`project.godot` 已绑滚轮上/下）—— 缩放**以光标为锚点**，
/// 否则放大后目标格会跑出视野，玩家每次缩放都要重新找位置。</item>
/// <item>平移：WASD（物理键位：非 QWERTY 布局也能用）+ 左键拖动；拖动位移按 zoom 归一化，
/// 保证"鼠标移动多少像素，画面就跟多少像素"。</item>
/// <item>点击 vs 拖动：位移超过 <see cref="DragThresholdPixels"/> 才算拖动，并把 <see cref="WasDragged"/> 置位 ——
/// M2 的"点格子选单位"要据此忽略拖动的尾巴（否则每次拖完都会顺手选中一格）。</item>
/// </list>
/// <para>相机只读输入、只动自己；不持有任何游戏服务（表现层不得反向依赖玩法）。</para>
/// </summary>
public partial class CameraController : Camera2D
{
	/// <summary>WASD 平移速度（屏幕像素 / 真实秒；实际位移会按 zoom 归一化到世界坐标）。</summary>
	[Export] public float PanSpeedPixelsPerSecond { get; set; } = 900f;

	/// <summary>滚轮缩放的倍率步长（每档乘/除 <c>1 + step</c>）。</summary>
	[Export] public float ZoomStep { get; set; } = 0.1f;

	[Export] public float MinZoom { get; set; } = 0.05f;

	[Export] public float MaxZoom { get; set; } = 4.0f;

	/// <summary>超过该位移（屏幕像素）才视为"拖动"，否则视为"点击"。</summary>
	[Export] public float DragThresholdPixels { get; set; } = 4.0f;

	private const float ZoomIncrement = 0.1f;

	private float _zoomLevel = 1.0f;

	private bool _dragging;
	private float _dragDistance;

	/// <summary>最近一次左键按下→抬起之间是否构成拖动（M2 的"点选"据此短路）。</summary>
	public bool WasDragged { get; private set; }

	/// <summary>当前缩放比例（取 X 分量：本相机不使用非等比缩放）。</summary>
	public float ZoomLevel => Zoom.X;

	public override void _Ready()
	{
		_zoomLevel = Mathf.Clamp(Zoom.X, MinZoom, MaxZoom);
		Zoom = new Vector2(_zoomLevel, _zoomLevel);
	}

	/// <summary>WASD 连续平移（按真实秒积分，与帧率无关）。</summary>
	public override void _Process(double delta)
	{
		Vector2 direction = Vector2.Zero;

		if (Input.IsPhysicalKeyPressed(Key.W)) direction.Y -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.S)) direction.Y += 1f;
		if (Input.IsPhysicalKeyPressed(Key.A)) direction.X -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.D)) direction.X += 1f;

		if (direction == Vector2.Zero) return;

		float zoom = Mathf.Max(ZoomLevel, MinZoom);
		Position += direction.Normalized() * (PanSpeedPixelsPerSecond / zoom) * (float)delta;
	}

	/// <summary>把视野移到世界坐标（开局对中 / "跳到最后事件发生地"都走它）。</summary>
	public void FocusOn(Vector2 worldPosition) => Position = worldPosition;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("zoom_in"))
		{
			StepZoom(-ZoomIncrement, GetGlobalMousePosition());
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event.IsActionPressed("zoom_out"))
		{
			StepZoom(+ZoomIncrement, GetGlobalMousePosition());
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
		{
			if (button.Pressed)
			{
				_dragging = true;
				_dragDistance = 0f;
				WasDragged = false;
			}
			else
			{
				_dragging = false;
				WasDragged = _dragDistance > DragThresholdPixels;
			}
			return;
		}

		if (@event is InputEventMouseMotion motion && _dragging)
		{
			_dragDistance += motion.Relative.Length();

			// 除以 zoom：屏幕上拖 1 像素 = 世界坐标移动 1/zoom（与手感一致，而不是"放大了就拖不动"）
			Position -= motion.Relative / Mathf.Max(ZoomLevel, MinZoom);
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>
	/// 缩放一步。<paramref name="anchor"/> = 缩放前后保持不动的世界坐标（滚轮缩放传光标位置）。
	/// <para>实现：先记录锚点在屏幕上的位置，缩放后把它反推回同一屏幕位置 —— 这就是"以光标为中心缩放"。</para>
	/// </summary>
	private void StepZoom(float increment, Vector2 anchor)
	{
		Vector2 screenAnchor = (anchor - Position) * ZoomLevel;

		_zoomLevel = Mathf.Clamp(_zoomLevel * (1f + increment), MinZoom, MaxZoom);
		Zoom = new Vector2(_zoomLevel, _zoomLevel);

		Position = anchor - screenAnchor / _zoomLevel;
	}
}

