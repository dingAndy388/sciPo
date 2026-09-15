using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.6.0 / WP-5.3）**外观数据驱动**的验收检查：格步长/贴图目录来自配置表、颜色可解析、
	/// 缺美术时有确定性兜底 —— 目标是"M2 判据 ⑤：替换美术不需要改代码"。
	/// <para>为什么这些能无头验：外观解析被抽成纯函数（`TerrainAppearance`），
	/// Godot 侧只剩"取路径 → 加载或占位"两行。</para>
	/// </summary>
	internal static class AppearanceChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-5.3 外观：格步长/贴图目录来自配置表（不是代码常量）", AppearanceComesFromConfig);
			Check.Run("WP-5.3 外观：每个地形都有可解析的占位色", EveryTerrainHasColor);
			Check.Run("WP-5.3 外观：颜色解析支持 #RGB/#RRGGBB/#RRGGBBAA 且拒绝非法值", ColorParsing);
			Check.Run("WP-5.3 外观：贴图路径解析（Sprite 优先 / 回退 Id / 不重复扩展名）", SpritePathResolution);
			Check.Run("WP-5.3 外观：缺 Color/Sprite 时按 Id 派生稳定兜底（同 Id 同色）", FallbackIsDeterministic);
			Check.Run("WP-5.3 外观：格位换算与原型排布逐像素一致（换配置即换尺寸）", LayoutMatchesLegacyFormula);
		}

		private static void AppearanceComesFromConfig()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			IMapAppearanceConfig appearance = core.Appearance;

			Check.Assert(appearance != null, "组合根应暴露外观参数（缺表也要有兜底，不能为 null）");
			Check.AssertEqual(366f, appearance.CellXStep, "配置表里的 CellXStep");
			Check.AssertEqual(317.25f, appearance.CellYStep, "配置表里的 CellYStep");
			Check.AssertEqual("res://Texture/Terrain/", appearance.TerrainSpriteDir, "配置表里的贴图目录");

			// 缺表（地形表装载失败）时不能为 null，否则表现层算不出格位
			CoreServices broken = ConfigFixtures.BuildCore(new InMemoryConfigSource(), failOnConfigErrors: false);
			Check.AssertEqual(TerrainAppearance.DefaultCellXStep, broken.Appearance.CellXStep, "缺表时的兜底列步长");
		}

		private static void EveryTerrainHasColor()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			foreach (ITerrainData terrain in core.Tables.AllTerrains())
			{
				Check.Assert(RgbColor.TryParse(terrain.Color, out RgbColor color, out string error),
					$"地形 {terrain.Id} 的 Color=\"{terrain.Color}\" 应可解析（{error}）");
				Check.Assert(color.IsOpaque, $"地形 {terrain.Id} 的占位色应不透明（否则地图上会出现透明格）");
			}

			// 校验器不该因为外观字段而产生 error（真实配置要求 0 error）
			Check.AssertEqual(0, core.ConfigReport.ErrorCount, "真实配置的 error 数");
		}

		private static void ColorParsing()
		{
			Check.Assert(RgbColor.TryParse("#7BA05B", out RgbColor full, out _) && full.R == 0x7B && full.G == 0xA0 && full.B == 0x5B,
				"#RRGGBB 解析");
			Check.Assert(RgbColor.TryParse("7BA05B", out RgbColor bare, out _) && bare == full, "不带 # 也应接受");
			Check.Assert(RgbColor.TryParse("#ABC", out RgbColor shortForm, out _) && shortForm.R == 0xAA && shortForm.G == 0xBB && shortForm.B == 0xCC,
				"#RGB 短式展开");
			Check.Assert(RgbColor.TryParse("#7BA05B80", out RgbColor withAlpha, out _) && withAlpha.A == 0x80 && !withAlpha.IsOpaque,
				"#RRGGBBAA 带 alpha");
			Check.AssertEqual("#7BA05B", full.ToHex(), "回写口径");

			Check.Assert(!RgbColor.TryParse("#7BA05", out _, out string error1) && error1 != null, "位数不对应被拒");
			Check.Assert(!RgbColor.TryParse("red", out _, out _), "颜色名应被拒（避免两种口径）");
			Check.Assert(!RgbColor.TryParse("  ", out _, out _), "空白应被拒");
		}

		private static void SpritePathResolution()
		{
			var explicitName = new StubTerrain("plain") { Sprite = "grass_a" };
			var implicitName = new StubTerrain("water") { Sprite = "" };
			var withExtension = new StubTerrain("forest") { Sprite = "tree.png" };

			Check.AssertEqual("res://Texture/Terrain/grass_a.png",
				TerrainAppearance.ResolveSpritePath("res://Texture/Terrain/", explicitName), "Sprite 优先于 Id");
			Check.AssertEqual("res://Texture/Terrain/water.png",
				TerrainAppearance.ResolveSpritePath("res://Texture/Terrain/", implicitName), "Sprite 为空时回退 Id");
			Check.AssertEqual("res://Texture/Terrain/tree.png",
				TerrainAppearance.ResolveSpritePath("res://Texture/Terrain/", withExtension), "已带扩展名时不重复追加");
			Check.AssertEqual("res://Art/Terrain/x.png",
				TerrainAppearance.ResolveSpritePath("res://Art/Terrain", new StubTerrain("x")), "目录无结尾斜杠时自动补");
			Check.AssertEqual("res://Texture/Terrain/x.png",
				TerrainAppearance.ResolveSpritePath(null, new StubTerrain("x")), "目录为空时用兜底目录");
		}

		private static void FallbackIsDeterministic()
		{
			var noColor = new StubTerrain("mountain") { Color = "" };
			var badColor = new StubTerrain("mountain") { Color = "zzz" };

			RgbColor first = TerrainAppearance.ResolveColor(noColor);
			RgbColor second = TerrainAppearance.ResolveColor(noColor);
			RgbColor fromBad = TerrainAppearance.ResolveColor(badColor);

			Check.AssertEqual(first, second, "同一地形 Id 的派生色应稳定");
			Check.AssertEqual(first, fromBad, "Color 非法时同样走派生（不能是随机色）");
			Check.Assert(!first.Equals(TerrainAppearance.ResolveColor(new StubTerrain("plain"))), "不同 Id 应派生出不同颜色（否则分不清地形）");
			Check.Assert(!first.Equals(new RgbColor(0, 0, 0, 255)), "派生色不应是纯黑（N1 要看得清）");
		}

		private static void LayoutMatchesLegacyFormula()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			IMapAppearanceConfig appearance = core.Appearance;

			// 原型期公式：x = 366/2*r − 366*ceil(r/2) + 366*q、y = 423*3/4*r
			foreach ((int q, int r) in new[] { (0, 0), (1, 0), (0, 1), (3, 5), (12, 40), (72, 142) })
			{
				float expectedX = 366f / 2f * r - 366f * (float)System.Math.Ceiling(r * 0.5d) + 366f * q;
				float expectedY = 423f * 3f / 4f * r;

				float actualX = appearance.CellXStep / 2f * r - appearance.CellXStep * (float)System.Math.Ceiling(r * 0.5d) + appearance.CellXStep * q;
				float actualY = appearance.CellYStep * r;

				Check.AssertEqual(expectedX, actualX, $"({q},{r}) 的 x");
				Check.AssertEqual(expectedY, actualY, $"({q},{r}) 的 y");
			}
		}

		/// <summary>测试用地形（只填外观相关字段）。</summary>
		private sealed class StubTerrain(string id) : ITerrainData
		{
			public string Id { get; set; } = id;
			public string Name { get; set; } = id;
			public float Weight { get; set; } = 1f;
			public float MoveCost { get; set; } = 1f;
			public bool Passable { get; set; } = true;
			public string UnlockTech { get; set; }
			public string Sprite { get; set; }
			public string Color { get; set; }
		}
	}
}
