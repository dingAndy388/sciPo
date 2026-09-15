using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Presentation;

/// <summary>
/// （v0.6.0 / WP-5.2 修复）开发用地图面板：生成地图 → 让 <see cref="MapView"/> 重建格子 → 相机对中。
/// <para>修掉的三处脚手架缺陷（`WIRE-05` 家族）：</para>
/// <list type="number">
/// <item>`ServiceContainer.Instance` 未判空（autoload 启动失败时面板直接 NRE）→ 现在缺服务时禁用按钮并报错一次；</item>
/// <item>缺省 5×5 玩具图 → 改为 **73×143（10439 格）**：正式尺度才能暴露规模问题（`R1`），也才谈得上"看清"；</item>
/// <item>生成后相机停在 (0,0) → 现在对中到地图几何中心（`WP-5.9` 换成按出生点对中）。</item>
/// </list>
/// <para>本面板属于**开发脚手架**：M2 的正式 HUD 是 `WP-5.4` 家族，届时它会被替换而不是继续长功能。</para>
/// </summary>
public partial class DevMapUi : CanvasLayer
{
	/// <summary>
	/// 缺省地图宽度（列）。**73×143 = 10439 格**（正式尺度）。
	/// <para>⚠️ 行列方向（哪个是"宽"）以 `WP-5.8` 的"73×143 基线"为准，当前按字面 `宽×高` 传给生成器。</para>
	/// </summary>
	public const int DefaultWidth = 73;

	/// <summary>缺省地图高度（行）。</summary>
	public const int DefaultHeight = 143;

	private Button _btn;
	private SpinBox _seed;
	private LineEdit _id;
	private SpinBox _height;
	private SpinBox _width;

	private MapAppService _map;
	private MapView _mapView;

	private Camera2D _camera;

	public override void _Ready()
	{
		// 容器节点用 `as` 转换：面板可在没有 MapView 父节点时单独打开（编辑器调试）
		_mapView = GetParent() as MapView;
		if (_mapView == null) GD.PushWarning("[DevMapUi] 父节点不是 MapView：本次生成不会渲染格子");

		_btn = GetNodeOrNull<Button>("BoxContainer/GenerateBtn");
		_seed = GetNodeOrNull<SpinBox>("BoxContainer/SeedInput");
		_id = GetNodeOrNull<LineEdit>("BoxContainer/IDInput");
		_height = GetNodeOrNull<SpinBox>("BoxContainer/HeightInput");
		_width = GetNodeOrNull<SpinBox>("BoxContainer/WidthInput");

		// 缺省值：规模取 73×143（正式尺度），Id 取 `world`（场景里的 5×5 只适合冒烟，不适合开发）
		if (_width != null && _width.Value <= 5f) _width.Value = DefaultWidth;
		if (_height != null && _height.Value <= 5f) _height.Value = DefaultHeight;
		if (_id != null && string.IsNullOrWhiteSpace(_id.Text)) _id.Text = "world";

		_camera = _mapView?.GetNodeOrNull<Camera2D>("Camera2D");

		// 服务注入（v0.6.0 / WP-5.1/5.2）：组合根由 autoload `ServiceContainer` 装配
		_map = ServiceContainer.Instance?.MapService;
		if (_map == null)
		{
			GD.PushError("[DevMapUi] 未取到 MapAppService（autoload `ServiceContainer` 未加载或启动失败）—— 生成按钮不可用");
			if (_btn != null) _btn.Disabled = true;
		}
	}

	private void OnGenerateBtnPressed()
	{
		if (_map == null) return;

		string mapId = _id != null && !string.IsNullOrWhiteSpace(_id.Text) ? _id.Text : "world";
		int width = _width != null ? Mathf.Max(1, (int)_width.Value) : DefaultWidth;
		int height = _height != null ? Mathf.Max(1, (int)_height.Value) : DefaultHeight;
		int seed = _seed != null ? (int)_seed.Value : 0;

		GD.Print($"[DevMapUi] 生成地图 id={mapId} size={width}×{height} seed={seed}");

		ulong startedMs = Time.GetTicksMsec();
		_map.GenerateMap(seed, width, height, mapId);
		ulong generateMs = Time.GetTicksMsec() - startedMs;

		if (_mapView != null)
		{
			_mapView.MapId = mapId;
			_mapView.UpdateAllCells();
		}

		FocusCameraOnMapCenter(width, height);
		GD.Print($"[DevMapUi] 完成：生成 {generateMs} ms，渲染 {_mapView?.RenderedCellCount ?? 0} 格");
	}

	/// <summary>
	/// 把相机移到地图几何中心。`WP-5.9` 之后"开局就看得见自家领地"会换成按出生点对中，
	/// 在那之前让 73×143 的地图**一开局就在视野里**，否则玩家看到的是空白。
	/// </summary>
	private void FocusCameraOnMapCenter(int width, int height)
	{
		if (_camera == null) return;

		var center = new HexCubePosition(width / 2, height / 2);
		Vector2 worldCenter = _mapView.CellLayoutPosition(center);

		if (_camera is CameraController controller) controller.FocusOn(worldCenter);
		else _camera.Position = worldCenter;
	}
}
