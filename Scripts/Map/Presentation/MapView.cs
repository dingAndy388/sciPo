using Godot;
using SciencePotato.Scripts.Common.Domain;
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

			if (_cellContainer == null)
				GD.PushWarning("[MapView] 场景缺少 `MapCells` 子节点：格子视图将挂在本节点下");
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
			(_cellContainer != null ? _cellContainer : this).AddChild(cell);
			cell.Configure(_appearance);        // 外观来自配置表（WP-5.3）：先注入再算格位/取贴图
			cell.CellPosition = position;
			cell.SetPosition();
			cell.SetTerrain(terrain);
			_cells[position] = cell;
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

		private void RemoveCellView(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out MapCellView existing)) return;

			_cells.Remove(position);
			if (GodotObject.IsInstanceValid(existing)) existing.QueueFree();
		}
	}
}


