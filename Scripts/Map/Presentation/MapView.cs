using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Presentation
{
	/// <summary>
	/// （v0.3 起逐步演进；v0.6.0 / WP-5.2 修复）地图表现层：把 <see cref="MapAppService"/> 的格子渲染成 <see cref="MapCellView"/>。
	/// <para>**本类不含玩法逻辑**：只做"读地图 → 建/更/删视图"。所有改世界的动作都走应用服务。</para>
	/// <para>WP-5.2 修掉的三处脚手架缺陷（`WIRE-05` 家族）：</para>
	/// <list type="number">
	/// <item>服务注入：<c>_mapQuery</c> 从未被赋值（`UpdateAllCells` 必然 NRE）→ 现在从 <c>ServiceContainer</c> 取，
	/// 取不到就明确报错并**不抛异常**（编辑器里打开场景不该让引擎崩）。</item>
	/// <item>父子关系：格子视图原先 <c>AddChild</c> 挂到本节点上，`MapCells` 容器形同虚设 → 现在统一挂到容器。</item>
	/// <item>清理：原先遍历本节点子节点且不清 <c>_cells</c> 对应的视图 → 现在按容器清理，并支持"单格重建"不残留旧视图。</item>
	/// </list>
	/// </summary>
	public partial class MapView : Node2D
	{
		/// <summary>地图查询/操作服务（组合根产物）。</summary>
		private MapAppService _map;

		/// <summary>（v0.6.0 / WP-5.3）外观参数（格子步长 / 贴图目录）：来自配置表，换美术不改代码。</summary>
		private IMapAppearanceConfig _appearance = TerrainAppearance.Defaults;

		private Node2D _cellContainer;

		/// <summary>（v0.9.7 / WP-5.4）**图层容器**：地形层（格子视图）/ 占据物层（占位标记）。</summary>
		private Node2D _terrainLayer;
		private Node2D _occupantLayer;

		/// <summary>（v0.9.7 / WP-5.4）组合根（表现层只读它的查询/意图服务：`Intent` / `Fog` / `UiGate` / `I18n`）。</summary>
		private SciencePotato.Scripts.Core.CoreServices _core;

		/// <summary>（v0.9.7 / WP-5.4）交互模型：点击 → `PlayerIntent` → 规则层（表现层不碰应用服务）。</summary>
		private MapInteractionModel _interaction;

		private readonly Dictionary<HexCubePosition, MapCellView> _cells = new();

		[Export] public PackedScene CellScene { get; set; }

		/// <summary>当前显示的地图 Id（由 <c>DevMapUi</c> 或关卡脚本设置）。</summary>
		[Export] public string MapId;

		/// <summary>最近一次 <see cref="UpdateAllCells"/> 建出的格子数（冒烟/调试用）。</summary>
		public int RenderedCellCount { get; private set; }

		/// <summary>服务是否已就绪（未就绪时所有更新方法安全地什么都不做）。</summary>
		public bool IsWired => _map != null;

		public override void _Ready()
		{
			_cellContainer = GetNodeOrNull<Node2D>("MapCells");

			// 服务注入（WP-5.2 ①）：组合根由 autoload 装配；表现层只读取，不自己 new 任何服务
			_map = ServiceContainer.Instance?.MapService;
			if (_map == null)
				GD.PushWarning("[MapView] 未取到 MapAppService：请确认 autoload `ServiceContainer` 已加载（本场景仍可打开，但不会渲染地图）");

			// 外观参数（WP-5.3）：同样来自组合根（地形表根字段），不在代码里写死尺寸/路径
			_appearance = ServiceContainer.Instance?.Core?.Appearance ?? TerrainAppearance.Defaults;
			GD.Print($"[MapView] 外观：列步长={_appearance.CellXStep} 行步长={_appearance.CellYStep} 贴图目录={_appearance.TerrainSpriteDir}");

			// （v0.9.7 / WP-5.4）组合根与图层容器：交互/迷雾都从这里拿（拿不到就退化成"只渲染"）
			_core = ServiceContainer.Instance?.Core;
			EnsureLayers();
			if (_core == null)
				GD.PushWarning("[MapView] 未取到组合根：交互（选址建造/单位指令）与迷雾渲染将不可用");

			if (_cellContainer == null)
				GD.PushWarning("[MapView] 场景缺少 `MapCells` 子节点：格子视图将挂在本节点下");
		}

		/// <summary>（v0.9.7 / WP-5.4）建立图层容器（缺则建；已存在则复用 —— 场景里手工加的层优先）。</summary>
		private void EnsureLayers()
		{
			Node2D parent = _cellContainer != null ? _cellContainer : this;

			_terrainLayer = parent.GetNodeOrNull<Node2D>("TerrainLayer") ?? new Node2D { Name = "TerrainLayer" };
			_occupantLayer = parent.GetNodeOrNull<Node2D>("OccupantsLayer") ?? new Node2D { Name = "OccupantsLayer" };

			if (_terrainLayer.GetParent() == null) parent.AddChild(_terrainLayer);
			if (_occupantLayer.GetParent() == null) parent.AddChild(_occupantLayer);
			_occupantLayer.ZIndex = 1; // 占据物画在地形之上（缺美术期的占位语义）
		}

		/// <summary>重建全部格子（换图或首次显示时调用）。</summary>
		public void UpdateAllCells()
		{
			ClearAllCells();

			if (!IsWired || string.IsNullOrWhiteSpace(MapId)) return;

			System.Collections.Generic.IEnumerable<MapCell> cells = _map.GetAllCells(MapId);
			if (cells == null) return;

			foreach (MapCell cell in cells)
			{
				if (cell?.Terrain == null) continue; // 未填充地形的格子不渲染（生成中途的地图）
				CreateCellView(cell.Terrain, cell.Position);
			}

			RenderedCellCount = _cells.Count;
		}

		/// <summary>清空全部格子视图与索引。</summary>
		public void ClearAllCells()
		{
			Node parent = _cellContainer != null ? _cellContainer : this;
			foreach (Node child in parent.GetChildren())
			{
				if (child is MapCellView) child.QueueFree();
			}

			_cells.Clear();
			RenderedCellCount = 0;
		}

		/// <summary>重建单格视图（地形变化/覆盖物变化时调用；旧视图会被释放，不残留）。</summary>
		public void UpdateCell(HexCubePosition position)
		{
			if (!IsWired || string.IsNullOrWhiteSpace(MapId)) return;

			// （v0.3 / WP-3.8）`GetMapCell` 对不存在的格返回 null（旧实现抛异常）—— 视图侧同样跳过
			MapCell cell = _map.GetMapCell(MapId, position);
			if (cell == null) return;

			RemoveCellView(position);
			if (cell.Terrain != null) CreateCellView(cell.Terrain, cell.Position);
		}

		public void CreateCellView(ITerrainData terrain, HexCubePosition position)
		{
			if (terrain == null) return;

			if (CellScene == null)
			{
				GD.PushWarning("[MapView] 未设置 `CellScene`：无法创建格子视图");
				return;
			}

			RemoveCellView(position); // 同一格只允许一个视图（重复创建会叠图）

			MapCellView cell = CellScene.Instantiate<MapCellView>();
			(_terrainLayer != null ? _terrainLayer : (_cellContainer != null ? _cellContainer : this)).AddChild(cell);
			cell.Configure(_appearance);        // 外观来自配置表（WP-5.3）：先注入再算格位/取贴图
			cell.CellPosition = position;
			cell.SetPosition();
			cell.SetTerrain(terrain);
			_cells[position] = cell;

			ApplyPresentation(cell, position);  // （v0.9.7 / WP-5.4）雾档位 + 占据物占位
		}

		// ────────────────────────── 图层化（v0.9.7 / WP-5.4） ──────────────────────────

		/// <summary>
		/// 重画全部格子的**表现层状态**（雾档位 + 占据物占位）。生成地图后、单位/建筑变化后、迷雾变化后调用。
		/// <para>只读 `FogAppService` / `MapAppService`，不改变世界。</para>
		/// </summary>
		public void RefreshPresentation()
		{
			if (!IsWired || string.IsNullOrWhiteSpace(MapId)) return;

			foreach (var pair in _cells) ApplyPresentation(pair.Value, pair.Key);
		}

		/// <summary>把"该格当前的表现"贴到一个格子视图上（口径在 `MapLayerModel`，这里只做赋值）。</summary>
		private void ApplyPresentation(MapCellView view, HexCubePosition position)
		{
			if (view == null) return;

			int viewer = _core?.Session?.HumanOwnerId ?? 1;
			if (_core?.Fog != null)
				view.SetFogVisibility(_core.Fog.GetVisibility(viewer, position));
			else
				view.SetFogVisibility(FogAppService.Visible);

			IMapOccupant occupant = _map.GetOccupantAt(MapId, position);
			view.SetOccupantMarker(occupant, viewer);
		}

		/// <summary>**世界坐标 → 格位**（`CellLayoutPosition` 的逆运算 + 最近邻兜底）。找不到返回 <c>null</c>。</summary>
		public HexCubePosition? CellAtWorldPosition(Vector2 world)
		{
			if (!IsWired || string.IsNullOrWhiteSpace(MapId)) return null;

			float xStep = _appearance.CellXStep > 0f ? _appearance.CellXStep : TerrainAppearance.DefaultCellXStep;
			float yStep = _appearance.CellYStep > 0f ? _appearance.CellYStep : TerrainAppearance.DefaultCellYStep;

			int r = (int)System.Math.Round(world.Y / yStep);
			int q = (int)System.Math.Round((world.X + xStep * (float)System.Math.Ceiling(r * 0.5d) - xStep / 2f * r) / xStep);
			var guess = new HexCubePosition(q, r);

			// 六边形排布的取整会落到隔壁格 ⇒ 在候选与 6 个邻居里取"世界坐标最近"的那个
			HexCubePosition best = guess;
			float bestDistance = float.MaxValue;
			foreach (HexCubePosition candidate in Neighbours(guess))
			{
				float distance = CellLayoutPosition(candidate).DistanceTo(world);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = candidate;
				}
			}

			return _map.GetMapCell(MapId, best) != null ? best : null;
		}

		private static IEnumerable<HexCubePosition> Neighbours(HexCubePosition center)
		{
			yield return center;
			(int Dq, int Dr)[] offsets = { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };
			foreach ((int dq, int dr) in offsets) yield return new HexCubePosition(center.q + dq, center.r + dr);
		}

		/// <summary>
		/// （v0.6.0 / WP-5.3）把格位换算成世界坐标（相机对中/跳转用）—— 与格子视图共用同一套步长，
		/// 因此"相机对准的格"和"画出来的格"永远一致。
		/// </summary>
		public Vector2 CellLayoutPosition(HexCubePosition position)
		{
			float xStep = _appearance.CellXStep > 0f ? _appearance.CellXStep : TerrainAppearance.DefaultCellXStep;
			float yStep = _appearance.CellYStep > 0f ? _appearance.CellYStep : TerrainAppearance.DefaultCellYStep;

			int q = position.ToCoordinate().Item1;
			int r = position.ToCoordinate().Item2;

			return new Vector2(
				xStep / 2f * r - xStep * (float)System.Math.Ceiling(r * 0.5d) + xStep * q,
				yStep * r);
		}

		// ────────────────────────── 交互（v0.9.7 / WP-5.4） ──────────────────────────

		/// <summary>（v0.9.7 / WP-5.4）交互模型：点击 → 意图（表现层唯一的写入口）。未接线时为 <c>null</c>。</summary>
		public MapInteractionModel Interaction
		{
			get
			{
				EnsureInteraction();
				return _interaction;
			}
		}

		/// <summary>进入"选址建造"模式（UI 的建造按钮调它；真正开工发生在下一次点击）。</summary>
		public bool SetBuildMode(string buildingId)
		{
			EnsureInteraction();
			if (_interaction == null) return false;

			_interaction.SetBuildMode(buildingId);
			return true;
		}

		/// <summary>进入"单位指令"模式（UI 选中单位后调它）。</summary>
		public bool RequestMove(string unitUid, out string reasonKey)
		{
			EnsureInteraction();
			if (_interaction == null) { reasonKey = "intent.rejected"; return false; }

			return _interaction.SetMoveMode(unitUid, out reasonKey);
		}

		/// <summary>回到点选模式（右键 / ESC）。</summary>
		public void CancelInteraction()
		{
			EnsureInteraction();
			_interaction?.Cancel();
		}

		private void EnsureInteraction()
		{
			if (_core?.Intent == null || _map == null || string.IsNullOrWhiteSpace(MapId)) return;
			if (_interaction != null && _interaction.MapId == MapId) return;

			int human = _core.Session?.HumanOwnerId ?? 1;
			_interaction = new MapInteractionModel(MapId, human, _map, _core.Intent, _core.Events, _core.UiGate);
		}

		/// <summary>
		/// （v0.9.7 / WP-5.4）**最小交互**：左键 = 按当前模式提交意图；右键/ESC = 取消；`B` = 建造营地（占位演示）。
		/// <para>本方法**只表达意图**：扣资源、门控、前置全在规则层判；失败原因只拿到 i18n 键，由这里翻成文案。</para>
		/// </summary>
		public override void _UnhandledInput(InputEvent @event)
		{
			// 键盘：ESC 取消、B 进入"建造营地"（缺 UI 期的占位入口；正式 UI 会换成建造面板按钮）
			if (@event is InputEventKey { Pressed: true, Echo: false } key)
			{
				if (key.Keycode == Key.Escape) { CancelInteraction(); return; }
				if (key.Keycode == Key.B)
				{
					if (SetBuildMode("camp")) GD.Print("[MapView] 建造模式：营地（左键点格子开工，右键取消）");
					return;
				}
				return;
			}

			if (@event is not InputEventMouseButton { Pressed: true } mouse) return;

			if (mouse.ButtonIndex == MouseButton.Right)
			{
				CancelInteraction();
				return;
			}

			if (mouse.ButtonIndex != MouseButton.Left) return;

			EnsureInteraction();
			if (_interaction == null)
				return;

			HexCubePosition? cell = CellAtWorldPosition(GetGlobalMousePosition());
			if (cell == null) return;

			PrintFeedback(_interaction.Click(cell.Value));
			RefreshPresentation();
		}

		/// <summary>（v0.9.7 / WP-5.4）把交互结果打到日志：**表现层只写键**，文案来自 i18n（`R4`）。</summary>
		private void PrintFeedback(InteractionFeedback feedback)
		{
			if (feedback == null) return;

			string text = feedback.MessageKey != null
				? (_core?.I18n?.T(feedback.MessageKey) ?? feedback.MessageKey)
				: "（无提示）";
			GD.Print($"[MapView] {(feedback.Ok ? "✓" : "✗")} {text} | {feedback.Detail}");
		}

		private void RemoveCellView(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out MapCellView existing)) return;

			_cells.Remove(position);
			if (GodotObject.IsInstanceValid(existing)) existing.QueueFree();
		}
	}
}


