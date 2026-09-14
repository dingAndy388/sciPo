using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using System.IO;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>（v0.3 / WP-0.4）配置表验收检查：对应 M0-1 ②（表可解析 + 引用完整）的地形部分。</summary>
	internal static class ConfigChecks
	{
		public static void RunAll()
		{
			Check.Run("M0-1 ② Config/Terrains.json 可解析（5 类地形）", TerrainJsonParses);
			Check.Run("M0-1 ② 地形消耗量级与可通行标记符合设计", TerrainValuesMatchDesign);
			Check.Run("M0-1 ② 未知/空地形 Id 返回 null（不抛异常）", TerrainUnknownIdReturnsNull);
		}

		private static ITerrainConfigRepository LoadTerrains()
		{
			string path = Path.Combine(Check.FindRepoRoot(), "Config", "Terrains.json");
			Check.Assert(File.Exists(path), $"找不到地形配置表：{path}");
			return new TerrainsConfigRepository(File.ReadAllText(path));
		}

		private static void TerrainJsonParses()
		{
			var repo = LoadTerrains();
			int count = 0;
			foreach (ITerrainData _ in repo.GetAll()) count++;
			Check.AssertEqual(5, count, "地形条目数（plain/desert/forest/mountain/water）");
		}

		private static void TerrainValuesMatchDesign()
		{
			var repo = LoadTerrains();

			ITerrainData plain = repo.GetById("plain");
			Check.Assert(plain != null, "缺少 plain");
			Check.AssertEqual(5f, plain.MoveCost, "plain.MoveCost");
			Check.Assert(plain.Passable, "plain 应可通行");

			ITerrainData desert = repo.GetById("desert");
			Check.AssertEqual(8f, desert.MoveCost, "desert.MoveCost");

			ITerrainData forest = repo.GetById("forest");
			Check.AssertEqual(10f, forest.MoveCost, "forest.MoveCost");

			ITerrainData mountain = repo.GetById("mountain");
			Check.AssertEqual(25f, mountain.MoveCost, "mountain.MoveCost（设计示例中的 25 消耗地块）");

			ITerrainData water = repo.GetById("water");
			Check.Assert(water != null, "缺少 water");
			Check.Assert(!water.Passable, "water 应不可通行（需科技解锁）");
			Check.AssertEqual("buoyancy", water.UnlockTech, "water.UnlockTech");
		}

		private static void TerrainUnknownIdReturnsNull()
		{
			var repo = LoadTerrains();
			Check.AssertEqual(null, repo.GetById("test1"), "旧的原型地形 Id 应返回 null");
			Check.AssertEqual(null, repo.GetById(""), "空 Id 应返回 null");
			Check.AssertEqual(null, repo.GetById(null), "null Id 应返回 null");
		}
	}
}
