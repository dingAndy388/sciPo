using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Domain;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.9.7 / `WP-5.6`）**地图存档性能**（`MAP-07`/`MAP-08`）的验收检查。
	/// <list type="bullet">
	/// <item>**脏格增量**：存档点只把"变过的格子"换成新 DTO，未变的复用旧 DTO；<b>脏格为 0 时整次跳过写盘</b>；</item>
	/// <item>**计数可验**：`LastSavedDirtyCells` 是精确值（不是估计），用例直接断言"改 1 格 ⇒ 1"；</item>
	/// <item>**补全**：`DeleteMap` / `ListMaps` 在应用层可用（`MAP-08`）；</item>
	/// <item>**正确性不许退化**：增量路径写出的存档必须与全量写出**逐格等价**（读回来能拿到新数据）。</item>
	/// </list>
	/// </summary>
	internal static class MapSavePerfChecks
	{
		private const string MapId = "perf-map";

		public static void RunAll()
		{
			Check.Run("WP-5.6 首次存档 = 全量（脏格数 = 总格数，且不跳过）", FirstSaveIsFull);
			Check.Run("WP-5.6 无变化 ⇒ 整个存档点跳过写盘（脏格 0 / 落盘次数不变）", UnchangedSaveIsSkipped);
			Check.Run("WP-5.6 改 1 格地形 ⇒ 脏格数精确为 1（存档仍含全部格）", SingleTerrainEditIsIncremental);
			Check.Run("WP-5.6 建一栋建筑 ⇒ 脏格数也只计它的那一格", OccupantChangeCountsItsCell);
			Check.Run("WP-5.6 `MAP-08` 补全：多张图能列出、`DeleteMap` 后不再出现、删不存在的图返回 false", DeleteAndListAreComplete);
			Check.Run("WP-5.6 增量路径的正确性：改地形 → 增量落盘 → 读档拿到新地形", IncrementalSaveRoundTrips);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void FirstSaveIsFull()
		{
			(CoreServices core, InMemoryMapRepository repo) = NewField();

			Check.AssertEqual(400, repo.LastSavedTotalCells, "20×20 的地图应有 400 格");
			Check.AssertEqual(400, repo.LastSavedDirtyCells, "首次存档没有基线 ⇒ 全量（脏格数 = 总格数）");
			Check.Assert(!repo.LastSaveSkippedWrite, "首次存档必须真的写盘");
			Check.AssertEqual(1, repo.SaveCount, "生成地图时落盘一次");
		}

		private static void UnchangedSaveIsSkipped()
		{
			(CoreServices core, InMemoryMapRepository repo) = NewField();
			int before = repo.SaveCount;

			core.Session.Maps.MarkDirty(MapId);
			core.Session.Maps.Flush(MapId);

			Check.AssertEqual(0, repo.LastSavedDirtyCells, "什么都没改 ⇒ 脏格数应为 0");
			Check.Assert(repo.LastSaveSkippedWrite, "脏格数 0 时应整次跳过写盘");
			Check.AssertEqual(before, repo.SaveCount, "跳过的存档点不应计入落盘次数");
		}

		private static void SingleTerrainEditIsIncremental()
		{
			(CoreServices core, InMemoryMapRepository repo) = NewField();
			int before = repo.SaveCount;

			var target = new HexCubePosition(3, 4);
			core.Map.SetTerrain(MapId, target, core.Tables.Terrains.GetById("desert"));
			core.Session.Maps.Flush(MapId);

			Check.AssertEqual(1, repo.LastSavedDirtyCells, "只改了一格 ⇒ 脏格数应精确为 1");
			Check.AssertEqual(400, repo.LastSavedTotalCells, "总格数不变");
			Check.Assert(!repo.LastSaveSkippedWrite, "有脏格 ⇒ 必须写盘");
			Check.AssertEqual(before + 1, repo.SaveCount, "有变化的存档点应计入落盘次数");
		}

		private static void OccupantChangeCountsItsCell()
		{
			(CoreServices core, InMemoryMapRepository repo) = NewField();

			core.Resources.AddResource("BasicMinerals", 2000f, MapId, 1);
			core.Resources.AddResource("Food", 1000f, MapId, 1);
			var site = new HexCubePosition(6, 6);
			Check.Assert(core.Construction.StartConstruction(MapId, "camp", site, 1), "前置：应能开工建造");

			core.Session.Maps.Flush(MapId);

			// 占据物 DTO 参与比较 ⇒ 只有落位那一格算脏（其余 399 格复用旧 DTO）
			Check.AssertEqual(1, repo.LastSavedDirtyCells, $"只有建筑那一格变了（实际 {repo.LastSavedDirtyCells}）");
			Check.Assert(repo.LastSavedTotalCells == 400, "总格数仍应是 400");
		}

		private static void DeleteAndListAreComplete()
		{
			(CoreServices core, InMemoryMapRepository repo) = NewField();

			core.Map.GenerateMap(7, 20, 20, "perf-a");
			core.Map.GenerateMap(7, 20, 20, "perf-b");

			Check.AssertEqual(3, core.Map.ListMaps().Count, $"盘上应有三张图（夹具的 {MapId} + perf-a + perf-b）");
			Check.Assert(core.Map.ListMaps().Any(m => m.Id == "perf-a"), "列表应含 perf-a");

			Check.Assert(core.Map.DeleteMap("perf-a"), "删除应成功");
			Check.AssertEqual(2, core.Map.ListMaps().Count, "删掉一张后应剩两张");
			Check.Assert(core.Map.ListMaps().All(m => m.Id != "perf-a"), "列表不应再含 perf-a");
			Check.Assert(!core.Map.DeleteMap("perf-a"), "重复删除应返回 false（而不是抛异常）");
			Check.Assert(!core.Map.DeleteMap("no_such_map"), "删不存在的图应返回 false");
		}

		private static void IncrementalSaveRoundTrips()
		{
			(CoreServices core, InMemoryMapRepository repo) = NewField();

			var target = new HexCubePosition(5, 5);
			Check.AssertEqual("plain", core.Map.GetMapCell(MapId, target).Terrain.Id, "前置：初始是平原");

			core.Map.SetTerrain(MapId, target, core.Tables.Terrains.GetById("desert"));
			core.Session.Maps.Flush(MapId);
			Check.AssertEqual(1, repo.LastSavedDirtyCells, "增量落盘（1 格）");

			// 驱逐缓存 ⇒ 下次访问从"盘上"读回来：增量写出的内容必须完整
			core.Session.Maps.Evict(MapId);
			Check.AssertEqual("desert", core.Map.GetMapCell(MapId, target).Terrain.Id, "增量存档里那一格应是沙漠");
			Check.AssertEqual(400, core.Map.GetAllCells(MapId).Count(), "增量存档仍应含全部 400 格");
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private static (CoreServices Core, InMemoryMapRepository Repository) NewField(int seed = 20261800)
		{
			var repo = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildRealCore(mapRepository: repo);
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Map.GenerateMap(seed, 20, 20, MapId); // 生成即落盘（首次 = 全量）
			return (core, repo);
		}
	}
}
