using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.3 / WP-0.4；v0.6.0 / WP-5.3 补外观段）地形配置表根对象：
	/// <c>{ "CellXStep": 366, "CellYStep": 317.25, "TerrainSpriteDir": "res://Texture/Terrain/", "Terrains": [ ... ] }</c>。
	/// <para>外观参数放在**地形表**而不是代码里：换一套美术只需要改这张表（列步长/行步长/贴图目录/每行颜色），
	/// 代码里不再出现任何具体路径或尺寸常量（`R5`）。</para>
	/// </summary>
	public class TerrainsConfigDto : ITerrainsConfig, IMapAppearanceConfig
	{
		[JsonProperty("Terrains")]
		public List<TerrainConfigDto> TerrainsData { get; set; }

		/// <summary>列步长（相邻 q 的横向像素）；0 或缺省 = 用内置兜底值。</summary>
		[JsonProperty("CellXStep")]
		public float CellXStepValue { get; set; }

		/// <summary>行步长（相邻 r 的纵向像素）；0 或缺省 = 用内置兜底值。</summary>
		[JsonProperty("CellYStep")]
		public float CellYStepValue { get; set; }

		/// <summary>地形贴图目录（含结尾斜杠）；空 = 用内置兜底值。</summary>
		[JsonProperty("TerrainSpriteDir")]
		public string TerrainSpriteDirValue { get; set; }

		float IMapAppearanceConfig.CellXStep
			=> CellXStepValue > 0f ? CellXStepValue : TerrainAppearance.DefaultCellXStep;

		float IMapAppearanceConfig.CellYStep
			=> CellYStepValue > 0f ? CellYStepValue : TerrainAppearance.DefaultCellYStep;

		string IMapAppearanceConfig.TerrainSpriteDir
			=> string.IsNullOrWhiteSpace(TerrainSpriteDirValue) ? TerrainAppearance.DefaultSpriteDir : TerrainSpriteDirValue;

		List<ITerrainData> ITerrainsConfig.Terrains
			=> TerrainsData?.Select(t => (ITerrainData)t).ToList() ?? new List<ITerrainData>();
	}
}
