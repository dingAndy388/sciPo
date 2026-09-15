using System;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.6.0 / WP-5.3）**外观解析的纯函数层**：把"某地形该用哪张贴图、什么颜色"从表现层拿出来，
	/// 于是它可以被无头用例直接验证，Godot 侧只剩"取到路径 → 加载/兜底"这一行。
	/// <para>两个关键口径：</para>
	/// <list type="bullet">
	/// <item>贴图名：优先用配置里的 <c>Sprite</c>；为空则回退到地形 Id（保持旧约定可用）；</item>
	/// <item>颜色：优先用配置里的 <c>Color</c>；为空或非法则按 Id **确定性**派生一个可区分的颜色
	/// —— 缺美术时地图仍然"看得出地形差别"，而不是一片同色（否则 N1 无法判断能不能看清）。</item>
	/// </list>
	/// </summary>
	public static class TerrainAppearance
	{
		/// <summary>兜底列步长（= 原型期实测值，配置缺失时保证不崩）。</summary>
		public const float DefaultCellXStep = 366f;

		/// <summary>兜底行步长（= 原型期 423×3/4）。</summary>
		public const float DefaultCellYStep = 317.25f;

		/// <summary>兜底贴图目录。</summary>
		public const string DefaultSpriteDir = "res://Texture/Terrain/";

		/// <summary>贴图扩展名（美术只需按 `{Sprite|Id}.png` 放文件）。</summary>
		public const string SpriteExtension = ".png";

		/// <summary>配置缺失/不完整时的默认外观（组合根与表现层都用它，保证任何情况下都能渲染）。</summary>
		public static IMapAppearanceConfig Defaults { get; } = new MapAppearanceDefaults();

		/// <summary>贴图文件名解析：<c>Sprite</c> 非空则用它，否则用地形 Id。</summary>
		public static string ResolveSpriteId(ITerrainData terrain)
			=> !string.IsNullOrWhiteSpace(terrain?.Sprite) ? terrain.Sprite.Trim() : terrain?.Id;

		/// <summary>贴图完整路径（目录 + 名 + .png）；目录为空时用 <see cref="DefaultSpriteDir"/>。</summary>
		public static string ResolveSpritePath(string spriteDir, ITerrainData terrain)
		{
			string name = ResolveSpriteId(terrain);
			if (string.IsNullOrWhiteSpace(name)) return null;

			string dir = string.IsNullOrWhiteSpace(spriteDir) ? DefaultSpriteDir : spriteDir.Trim();
			if (!dir.EndsWith("/", StringComparison.Ordinal)) dir += "/";

			// 已带扩展名（美术直接写全文件名）时不再重复追加
			return name.EndsWith(SpriteExtension, StringComparison.OrdinalIgnoreCase) ? dir + name : dir + name + SpriteExtension;
		}

		/// <summary>
		/// 占位色：配置的 <c>Color</c> 合法就用它；否则按 Id 派生（同一 Id 永远同色，跨运行稳定）。
		/// <para>派生用的是一段简单的 FNV-1a 哈希映射到 HSV 的色相环 —— 目的不是"好看"，
		/// 而是"任意 Id 都得到一个能和其他地形区分的颜色"。</para>
		/// </summary>
		public static RgbColor ResolveColor(ITerrainData terrain)
		{
			if (RgbColor.TryParse(terrain?.Color, out RgbColor parsed, out _)) return parsed;

			string id = terrain?.Id ?? string.Empty;
			float hue = (Hash(id) % 360u) / 360f;

			(byte r, byte g, byte b) = HsvToRgb(hue, 0.45f, 0.80f);
			return new RgbColor(r, g, b, 255);
		}

		/// <summary>
		/// （v0.6.7 / P0）**六边形轮廓**：返回以格心为原点的 6 个顶点（x 右、y 下，Godot 屏幕坐标），
		/// 供"缺美术时的占位块"画成真六边形 —— 矩形占位读不出网格（`N1` 的观察）。
		/// <para>几何：列步长 <paramref name="xStep"/>、行步长 <paramref name="yStep"/>（= 六边形高的 3/4）⇒
		/// 六边形高 = <c>yStep·4/3</c>，宽 = <c>xStep</c>；顶点按 30° 起、每 60° 一个
		/// （平顶朝上、左右各一尖角，与 `LayoutPosition` 的"列错位"排布配合天然无缝）。</para>
		/// <para>抽成纯函数是为了**无头可验**：顶点数、宽高比、不越界都能断言，不必起引擎。</para>
		/// </summary>
		public static (float X, float Y)[] HexOutline(float xStep, float yStep)
		{
			float width = xStep > 0f ? xStep : DefaultCellXStep;
			float height = (yStep > 0f ? yStep : DefaultCellYStep) * 4f / 3f;

			float halfW = width / 2f;
			float halfH = height / 2f;
			float quarterH = height / 4f;

			// 与 Godot 的 y 向下一致：上排两点 → 左右两尖 → 下排两点
			return new[]
			{
				(-halfW / 2f, -halfH),
				(halfW / 2f, -halfH),
				(halfW, 0f),
				(halfW / 2f, halfH),
				(-halfW / 2f, halfH),
				(-halfW, 0f),
			};
		}

		/// <summary>六边形的高（= 行步长 × 4/3）；占位块与真实美术的图幅都用它。</summary>
		public static float HexHeight(float yStep) => (yStep > 0f ? yStep : DefaultCellYStep) * 4f / 3f;

		/// <summary>稳定哈希（FNV-1a 32 位）：跨进程、跨平台一致，不依赖 `string.GetHashCode()` 的随机化。</summary>
		public static uint Hash(string text)
		{
			unchecked
			{
				uint hash = 2166136261u;
				foreach (char c in text ?? string.Empty)
				{
					hash ^= c;
					hash *= 16777619u;
				}
				return hash;
			}
		}

		/// <summary>HSV → RGB（h/s/v ∈ [0,1]）；纯函数，无引擎依赖。</summary>
		public static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
		{
			float sector = (h - (float)Math.Floor(h)) * 6f;
			int index = (int)Math.Floor(sector);
			float f = sector - index;

			float p = v * (1f - s);
			float q = v * (1f - s * f);
			float t = v * (1f - s * (1f - f));

			(float r, float g, float b) = (index % 6) switch
			{
				0 => (v, t, p),
				1 => (q, v, p),
				2 => (p, v, t),
				3 => (p, q, v),
				4 => (t, p, v),
				_ => (v, p, q),
			};

			return (ToByte(r), ToByte(g), ToByte(b));
		}

		private static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value * 255f), 0, 255);

		/// <summary>默认外观（配置表缺失时的兜底实现）。</summary>
		private sealed class MapAppearanceDefaults : IMapAppearanceConfig
		{
			public float CellXStep => DefaultCellXStep;

			public float CellYStep => DefaultCellYStep;

			public string TerrainSpriteDir => DefaultSpriteDir;
		}
	}
}
