using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
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

	/// <summary>
	/// 缺省地图 Id（用具名常量而不是字面量：面板里"文案必须走 i18n 键"的纪律检查只允许键，
	/// 而这个 Id 会出现在存档文件名里，散落的字面量改起来容易漏）。
	/// </summary>
	public const string DefaultMapId = "world";

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
		if (_id != null && string.IsNullOrWhiteSpace(_id.Text)) _id.Text = DefaultMapId;

		_camera = _mapView?.GetNodeOrNull<Camera2D>("Camera2D");

		// 服务注入（v0.6.0 / WP-5.1/5.2）：组合根由 autoload `ServiceContainer` 装配
		_map = ServiceContainer.Instance?.MapService;
		if (_map == null)
		{
			GD.PushError("[DevMapUi] 未取到 MapAppService（autoload `ServiceContainer` 未加载或启动失败）—— 生成按钮不可用");
			if (_btn != null) _btn.Disabled = true;
		}

		ApplyLocaleText();
	}

	/// <summary>
	/// （v0.6.5 / WP-5.10）把面板文案从 **i18n 键**取出来（面板里不再写死任何一种语言的文本）。
	/// <para>缺键时 <c>T()</c> 返回 <c>⟦key⟧</c>，在编辑器/游戏里一眼看得见 —— 这正是"缺键可视"的目的。</para>
	/// </summary>
	private void ApplyLocaleText()
	{
		II18nService i18n = ServiceContainer.Instance?.Services?.I18n;
		if (i18n == null) return; // 没有 i18n 就用场景里已有的文本（面板不至于变空）

		if (_btn != null) _btn.Text = i18n.T("ui.generate");
		if (_width != null) _width.Prefix = i18n.T("ui.width") + " ";
		if (_height != null) _height.Prefix = i18n.T("ui.height") + " ";
		if (_seed != null) _seed.Prefix = i18n.T("ui.seed") + " ";
		if (_id != null) _id.PlaceholderText = i18n.T("ui.mapId");

		GD.Print($"[DevMapUi] 语言={i18n.Locale}，可用={string.Join("/", i18n.AvailableLocales)}，缺键={i18n.MissingKeys.Count}");
	}

	private void OnGenerateBtnPressed()
	{
		if (_map == null) return;

		string mapId = _id != null && !string.IsNullOrWhiteSpace(_id.Text) ? _id.Text : DefaultMapId;
		int width = _width != null ? Mathf.Max(1, (int)_width.Value) : DefaultWidth;
		int height = _height != null ? Mathf.Max(1, (int)_height.Value) : DefaultHeight;
		int seed = _seed != null ? (int)_seed.Value : 0;

		GD.Print($"[DevMapUi] 生成地图 id={mapId} size={width}×{height} seed={seed}");

		ulong startedMs = Time.GetTicksMsec();
		_map.GenerateMap(seed, width, height, mapId);
		ulong generateMs = Time.GetTicksMsec() - startedMs;

		// （v0.6.4 / WP-5.9）生成之后**把这一局跑起来**：开局布置（出生点/单位/视野）+ 逐势力启动月结与资源池。
		// 不调这一步的话，玩家看到的是一张"没有自己、也没有对手"的地图（M1 的"看得见、点得动"就不成立）。
		var orchestrator = ServiceContainer.Instance?.Orchestrator;
		var started = orchestrator?.StartMap(mapId);
		string spawnInfo = string.Empty;
		if (started != null && started.Count > 0)
		{
			int humanOwner = ServiceContainer.Instance?.Session?.HumanOwnerId ?? 1;
			var spawn = orchestrator.SpawnOf(mapId, humanOwner);
			if (spawn != null) spawnInfo = $"；出生点=({spawn.Position.q},{spawn.Position.r}) 开局单位×{spawn.UnitUIds.Count}";
		}

		if (_mapView != null)
		{
			_mapView.MapId = mapId;
			_mapView.UpdateAllCells();
		}

		FocusCameraOnMapCenter(width, height, mapId);
		GD.Print($"[DevMapUi] 完成：生成 {generateMs} ms，渲染 {_mapView?.RenderedCellCount ?? 0} 格{spawnInfo}");
	}

	/// <summary>
	/// 把相机移到**人类出生点**（`WP-5.9` 之后开局就有出生点了）；没有出生点时退化为地图几何中心。
	/// <para>这一步是 M1 手感的直接来源：开局看不见自己家的地图，玩家第一件事就得手动找。</para>
	/// </summary>
	private void FocusCameraOnMapCenter(int width, int height, string mapId)
	{
		if (_camera == null) return;

		var orchestrator = ServiceContainer.Instance?.Orchestrator;
		int humanOwner = ServiceContainer.Instance?.Session?.HumanOwnerId ?? 1;
		var spawn = orchestrator?.SpawnOf(mapId, humanOwner);

		var target = spawn?.Position ?? new HexCubePosition(width / 2, height / 2);
		Vector2 worldCenter = _mapView != null
			? _mapView.CellLayoutPosition(target)
			: new Vector2(target.q * 366f, target.r * 317.25f);

		if (_camera is CameraController controller) controller.FocusOn(worldCenter);
		else _camera.Position = worldCenter;
	}
}
