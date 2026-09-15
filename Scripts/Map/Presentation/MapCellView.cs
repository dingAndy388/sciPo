using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;
using SciencePotato.Scripts.Map.Presentation;


public partial class MapCellView : Node2D
	{
	/// <summary>（v0.9.7 / WP-5.4）该格当前的雾档位（`FogAppService.Visible/Fogged/Unexplored`）。</summary>
	public byte FogVisibility { get; private set; } = SciencePotato.Scripts.Fog.Application.FogAppService.Visible;

	/// <summary>（v0.9.7 / WP-5.4）占据物占位标记（懒建；空格时隐藏）——缺美术期的"看得出格子上有东西"。</summary>
	private Polygon2D _occupantMarker;


	/// <summary>已经警告过的贴图路径（缺贴图时**每种只提示一次**：10439 格的图上逐格刷警告会把日志打爆）。</summary>
	private static readonly System.Collections.Generic.HashSet<string> WarnedMissingTextures = new();

	/// <summary>缺贴图时用的 1×1 白纹理（静态缓存：10439 格共用一个纹理，不要逐格创建）。</summary>
	private static Texture2D _placeholderTexture;

	private Sprite2D _sprite;

	/// <summary>缺贴图时画的六边形（懒建）。</summary>
	private Polygon2D _placeholder;

	public ITerrainData Terrain { get; private set; }
	public HexCubePosition CellPosition { get; set; }

	/// <summary>列步长（相邻 q 的横向像素）—— 来自配置表（`WP-5.3`），不在代码里写死。</summary>
	public float CellXStep { get; private set; } = TerrainAppearance.DefaultCellXStep;

	/// <summary>行步长（相邻 r 的纵向像素）。</summary>
	public float CellYStep { get; private set; } = TerrainAppearance.DefaultCellYStep;

	/// <summary>地形贴图目录（配置表给出）。</summary>
	private string _spriteDir = TerrainAppearance.DefaultSpriteDir;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	/// <summary>
	/// （v0.6.0 / WP-5.3）**注入外观参数**（由 <c>MapView</c> 从配置表读出后统一调用）。
	/// <para>必须在 <see cref="SetPosition"/> / <see cref="SetTerrain"/> 之前调用。</para>
	/// </summary>
	public void Configure(IMapAppearanceConfig appearance)
	{
		IMapAppearanceConfig config = appearance ?? TerrainAppearance.Defaults;

		CellXStep = config.CellXStep > 0f ? config.CellXStep : TerrainAppearance.DefaultCellXStep;
		CellYStep = config.CellYStep > 0f ? config.CellYStep : TerrainAppearance.DefaultCellYStep;
		_spriteDir = string.IsNullOrWhiteSpace(config.TerrainSpriteDir) ? TerrainAppearance.DefaultSpriteDir : config.TerrainSpriteDir;
	}

	public void SetTerrain(ITerrainData terrain)
	{
		_sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
		this.Terrain = terrain;
		if (terrain == null || _sprite == null) return;

		// 贴图：优先配置里的 `Sprite`，否则按地形 Id 拼路径（口径见 `TerrainAppearance`）
		string path = TerrainAppearance.ResolveSpritePath(_spriteDir, terrain);
		Texture2D texture = !string.IsNullOrWhiteSpace(path) && ResourceLoader.Exists(path)
			? GD.Load<Texture2D>(path)
			: null;

		// 颜色：优先配置的 `Color`，否则按 Id 派生（缺美术时地图仍"看得出地形差别"）
		RgbColor color = TerrainAppearance.ResolveColor(terrain);
		_sprite.Modulate = new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

		if (texture != null)
		{
			if (_placeholder != null) _placeholder.Visible = false;
			_sprite.Texture = texture;
			_sprite.Scale = Vector2.One;
			return;
		}

		// 缺贴图 → 用**真六边形**占位（v0.6.7 / P0）：矩形占位读不出网格，N1 一眼就看出"不像六边形"
		_sprite.Texture = PlaceholderTexture;
		_sprite.Scale = Vector2.One;                  // 六边形自己按尺寸画，不靠缩放 1×1 纹理
		PlaceholderPolygon.Visible = true;
		PlaceholderPolygon.Color = new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
		PlaceholderPolygon.Polygon = HexPolygon();

		if (WarnedMissingTextures.Add(path ?? terrain.Id))
			GD.PushWarning($"[MapCellView] 缺少地形贴图 {path}：用占位六边形 {color.ToHex()} 渲染（正式美术按 `Document/AssetManifest.csv` 放入即可，不需要改代码）");
	}

	/// <summary>缺贴图时的六边形占位节点（懒建；与 Sprite2D 同格共存，有图时隐藏）。</summary>
	private Polygon2D PlaceholderPolygon
	{
		get
		{
			if (_placeholder != null && GodotObject.IsInstanceValid(_placeholder)) return _placeholder;

			_placeholder = new Polygon2D { Name = "PlaceholderHex" };
			AddChild(_placeholder);
			return _placeholder;
		}
	}

	/// <summary>
	/// （v0.9.7 / WP-5.4）**雾层**：未探索不画、迷雾半透明、可见全亮 —— 档位口径来自 `MapLayerModel`（无头已验）。
	/// <para>雾必须由**格子自己**承担（`Modulate`），不能拆成上一层独立节点：否则上层遮罩盖不住该格的贴图。</para>
	/// </summary>
	public void SetFogVisibility(byte visibility)
	{
		FogVisibility = visibility;

		float opacity = MapLayerModel.FogOpacity(visibility);
		Visible = opacity > 0f;
		Modulate = new Color(1f, 1f, 1f, opacity);
	}

	/// <summary>
	/// （v0.9.7 / WP-5.4）**占据物占位标记**：建筑/单位在缺美术时用一个彩色小六边形标出（颜色表在 `MapLayerModel`）。
	/// <para>换美术时把这里换成真正的建筑/单位贴图即可 —— 口径（哪些该画、什么颜色）不用动。</para>
	/// </summary>
	public void SetOccupantMarker(IMapOccupant occupant, int viewerOwnerId)
	{
		if (occupant == null)
		{
			if (_occupantMarker != null && GodotObject.IsInstanceValid(_occupantMarker)) _occupantMarker.Visible = false;
			return;
		}

		Polygon2D marker = OccupantMarker;
		RgbColor color = MapLayerModel.OccupantPlaceholderColor(occupant, viewerOwnerId);
		marker.Polygon = InnerHexagon();
		marker.Color = new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
		marker.ZIndex = 1;
		marker.Visible = true;
	}

	/// <summary>占据物标记节点（懒建；只建一次）。</summary>
	private Polygon2D OccupantMarker
	{
		get
		{
			if (_occupantMarker != null && GodotObject.IsInstanceValid(_occupantMarker)) return _occupantMarker;

			_occupantMarker = new Polygon2D { Name = "OccupantMarker" };
			AddChild(_occupantMarker);
			return _occupantMarker;
		}
	}

	/// <summary>略小的六边形（与地形占位同形状，缩到 55% ⇒ 看得出"里面还有东西"）。</summary>
	private Vector2[] InnerHexagon()
	{
		Vector2[] full = HexPolygon();
		var inner = new Vector2[full.Length];
		for (int i = 0; i < full.Length; i++) inner[i] = full[i] * 0.55f;
		return inner;
	}

	/// <summary>按当前步长算出的 6 个顶点（Godot 坐标）。</summary>
	private Vector2[] HexPolygon()
	{
		(float X, float Y)[] outline = TerrainAppearance.HexOutline(CellXStep, CellYStep);
		var points = new Vector2[outline.Length];
		for (int i = 0; i < outline.Length; i++) points[i] = new Vector2(outline[i].X, outline[i].Y);
		return points;
	}

	/// <summary>缺贴图时的 1×1 白纹理（配合 `Modulate` 得到纯色块）。</summary>
	private static Texture2D PlaceholderTexture
	{
		get
		{
			if (_placeholderTexture != null && GodotObject.IsInstanceValid(_placeholderTexture)) return _placeholderTexture;

			Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
			image.Fill(Colors.White);
			_placeholderTexture = ImageTexture.CreateFromImage(image);
			return _placeholderTexture;
		}
	}

	/// <summary>
	/// （v0.6.0 / WP-5.3）**六边形格位 → 世界坐标**的唯一换算（步长来自配置表）。
	/// <para>公式：<c>x = X/2·r − X·⌈r/2⌉ + X·q</c>、<c>y = Y·r</c>（`X` = 列步长、`Y` = 行步长）；
	/// 与原型期 <c>height=366 / width=423</c> 的排布逐像素等价（`Y·r = 423·3/4·r`）。</para>
	/// </summary>
	public Vector2 LayoutPosition(HexCubePosition position)
	{
		int q = position.ToCoordinate().Item1;
		int r = position.ToCoordinate().Item2;

		return new Vector2(
			CellXStep / 2f * r - CellXStep * (float)Math.Ceiling(r * 0.5d) + CellXStep * q,
			CellYStep * r);
	}

	public void SetPosition()
	{
		// Set to the right Position with correct seperation
		base.Position = LayoutPosition(CellPosition);
	}
}
