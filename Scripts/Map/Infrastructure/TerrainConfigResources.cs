using Godot;
using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	[GlobalClass, Tool]
	public partial class TerrainConfigResources : Resource, ITerrainData
	{
		[Export] public string Id { get; set; }
		[Export] public string Name { get; set; }
		[Export] public float Weight { get; set; }
		[Export] public float MoveCost { get; set; }
		[Export] public bool Passable { get; set; } = true;
		[Export] public string UnlockTech { get; set; }

		/// <summary>（v0.6.0 / WP-5.3）贴图名（空 = 用 Id）；`.tres` 覆写路径也支持外观字段。</summary>
		[Export] public string Sprite { get; set; }

		/// <summary>（v0.6.0 / WP-5.3）占位色（`#RRGGBB`）；缺贴图时渲染纯色格。</summary>
		[Export] public string Color { get; set; }
	}
}
