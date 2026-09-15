using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Presentation
{
	/// <summary>
	/// （v0.9.7 / `WP-5.4`）**图层化模型**（`I2` 前半段）：把"一个格子在哪些图层上有东西、雾该多淡"
	/// 从渲染代码里抽出来，做成**纯 C#**（无 Godot 依赖）⇒
	/// <list type="bullet">
	/// <item>无头用例能锁住"图层口径"（哪格该出现在建筑层/单位层，未探索格不该被画出来）；</item>
	/// <item>Godot 侧只剩"按模型往四个容器里挂节点"，换美术/换渲染方式不动模型。</item>
	/// </list>
	/// <para>图层：<c>terrain</c>（地形）/ <c>buildings</c> / <c>units</c> / <c>fog</c>（按 owner 的可见性）。</para>
	/// </summary>
	public static class MapLayerModel
	{
		public const string Terrain = "terrain";
		public const string Buildings = "buildings";
		public const string Units = "units";
		public const string Fog = "fog";

		// ── （v0.9.10 / WP-8.1）**贴图路径口径**：与 `Document/AssetManifest.csv` 完全一致 ──
		// 一句话：`res://Texture/{类别}/{id}.png`，id 就是配置表里的 id（表即文件名）。
		public const string TerrainSpriteDir = "res://Texture/Terrain/";
		public const string BuildingSpriteDir = "res://Texture/Building/";
		public const string UnitSpriteDir = "res://Texture/Unit/";
		public const string ResourceSpriteDir = "res://Texture/Resource/";
		public const string EventSpriteDir = "res://Texture/Event/";
		public const string TechSpriteDir = "res://Texture/Tech/";
		public const string UiSpriteDir = "res://Texture/UI/";
		public const string AudioDir = "res://Audio/";

		/// <summary>占据物（建筑 / 单位）的贴图路径；<c>null</c> = 这类占据物没有贴图约定。</summary>
		public static string OccupantSpritePath(IMapOccupant occupant)
		{
			if (occupant?.GetInfo() == null || string.IsNullOrWhiteSpace(occupant.GetInfo().Id)) return null;

			return occupant switch
			{
				Building => BuildingSpriteDir + occupant.GetInfo().Id + ".png",
				Unit => UnitSpriteDir + occupant.GetInfo().Id + ".png",
				_ => null,
			};
		}

		/// <summary>资源图标路径（顶栏/面板用；资源 id 见 `Config/Resources.json`）。</summary>
		public static string ResourceSpritePath(string resourceId) => ResourceSpriteDir + resourceId + ".png";

		/// <summary>事件插图路径（弹窗用；事件 id 见 `Config/Events.json` 的 `EventId`）。</summary>
		public static string EventSpritePath(string eventId) => EventSpriteDir + eventId + ".png";

		/// <summary>科技图标路径（树/节点 id 见 `Config/TechTrees.json`）。</summary>
		public static string TechSpritePath(string treeId, string nodeId) => TechSpriteDir + treeId + "/" + nodeId + ".png";

		/// <summary>UI 贴图路径（id 见 `Document/ArtSpec.md` 的 UI 规格表）。</summary>
		public static string UiSpritePath(string uiId) => UiSpriteDir + uiId + ".png";

		/// <summary>该格应出现在哪些图层上（雾层按 `ownerId` 的可见性判定；未探索的格子**不出现**在任何层）。</summary>
		public static IReadOnlyList<string> LayersOf(MapAppService map, FogAppService fog, string mapId, int ownerId, HexCubePosition position)
		{
			var layers = new List<string>(4);
			byte visibility = fog?.GetVisibility(ownerId, position) ?? FogAppService.Visible;

			// 未探索：表现层不该画出这一格（否则玩家能看到没去过的地方的地形）
			if (visibility == FogAppService.Unexplored) return layers;

			MapCell cell = map?.GetMapCell(mapId, position);
			if (cell?.Terrain != null) layers.Add(Terrain);

			switch (cell?.Occupant)
			{
				case Building:
					layers.Add(Buildings);
					break;
				case Unit:
					layers.Add(Units);
					break;
			}

			layers.Add(Fog); // 已探索（可见 / 迷雾）⇒ 交回雾层决定不透明度
			return layers;
		}

		/// <summary>雾层的不透明度：可见 = 1、迷雾 = 0.55、未探索 = 0（表现层据此调 `Modulate.a`）。</summary>
		public static float FogOpacity(byte visibility)
		{
			switch (visibility)
			{
				case FogAppService.Visible: return 1f;
				case FogAppService.Fogged: return 0.55f;
				default: return 0f;
			}
		}

		/// <summary>占据物的占位着色（美术缺位时的"看得出有东西"）：建筑偏暖、单位偏亮、敌方偏红。</summary>
		public static RgbColor OccupantPlaceholderColor(IMapOccupant occupant, int viewerOwnerId)
		{
			if (occupant == null) return new RgbColor(255, 255, 255, 255);

			bool friendly = occupant.GetInfo().OwnerId == viewerOwnerId;
			return occupant switch
			{
				Building => friendly ? new RgbColor(230, 200, 140, 255) : new RgbColor(220, 120, 120, 255),
				Unit => friendly ? new RgbColor(200, 240, 200, 255) : new RgbColor(240, 130, 130, 255),
				_ => new RgbColor(255, 255, 255, 255),
			};
		}
	}
}
