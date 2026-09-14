using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-3.1）Map 常驻内存的验收检查。
	/// 核心目标：证明 **M0-2 ③④ 的阻塞点（占据物/人口跨调用丢失）已被解除**。
	/// </summary>
	internal static class MapSessionChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-3.1 同一地图只读盘一次（Get 走内存缓存）", SameMapLoadedOnce);
			Check.Run("M0-2 前置：占据物跨调用不丢失（可按 uid 取回）", OccupantSurvivesAcrossCalls);
			Check.Run("M0-2 前置：人口累加不被读盘重置", PopulationSurvivesAcrossCalls);
			Check.Run("存档点：只在 Flush 时写盘，且只写脏地图", FlushWritesOnlyDirtyMaps);
			Check.Run("当前边界：落盘后新会话仍读不到占据物（WP-3.2 待补）", PersistenceStillTerrainOnly);
		}

		private static (MapSession session, InMemoryMapRepository repo) NewSession()
		{
			var repo = new InMemoryMapRepository();
			var map = new Map(7, 4, 4, "m1");
			for (int q = 0; q < 4; q++)
				for (int r = 0; r < 4; r++)
				{
					var cell = new MapCell(new HexCubePosition(q, r));
					cell.SetTerrain(new TerrainConfigDto { Id = "plain", Name = "平原", MoveCost = 5f, Passable = true });
					map.SetCell(new HexCubePosition(q, r), cell);
				}
			repo.SaveMap(map);

			return (new MapSession(repo), repo);
		}

		private static void SameMapLoadedOnce()
		{
			(MapSession session, InMemoryMapRepository repo) = NewSession();

			Map first = session.Get("m1");
			Map second = session.Get("m1");
			Map third = session.Get("m1");

			Check.Assert(first != null, "首次取地图不应为 null");
			Check.Assert(ReferenceEquals(first, second) && ReferenceEquals(second, third), "应返回同一个内存实例");
			Check.AssertEqual(1, repo.LoadCount, "读盘次数");
			Check.Assert(session.IsLoaded("m1"), "应记录为已加载");
		}

		private static void OccupantSurvivesAcrossCalls()
		{
			(MapSession session, _) = NewSession();
			var position = new HexCubePosition(1, 1);
			var occupant = new FakeOccupant(position, "house", "uid-house-1", 0);

			session.Get("m1").AddOccupant(occupant, position);
			session.MarkDirty("m1");

			// 模拟"下一次调用"：仍从会话取地图
			IMapOccupant found = session.Get("m1").GetOccupantByUId("uid-house-1");

			Check.Assert(found != null, "占据物应能按 uid 取回（改造前此处抛 KeyNotFoundException）");
			Check.AssertEqual("uid-house-1", found.GetInfo().UId, "取回对象的 uid");

			MapOccupantInfo? info = session.Get("m1").GetOccupantInfo(position);
			Check.Assert(info.HasValue, "按位置应能取到占据物信息");
		}

		private static void PopulationSurvivesAcrossCalls()
		{
			(MapSession session, _) = NewSession();
			var position = new HexCubePosition(2, 2);

			session.Get("m1").GetCell(position).AddPopulation(3);

			Check.AssertEqual(3, session.Get("m1").GetCell(position).Population,
				"人口应保留在内存聚合中（改造前每次读盘会归零）");
		}

		private static void FlushWritesOnlyDirtyMaps()
		{
			(MapSession session, InMemoryMapRepository repo) = NewSession();
			Check.AssertEqual(1, repo.SaveCount, "准备阶段：仓库已保存 1 次");

			// 读取不应产生写盘
			session.Get("m1");
			session.FlushAll();
			Check.AssertEqual(1, repo.SaveCount, "只读不脏时不应写盘");

			// 改动 + 存档点
			session.Get("m1").GetCell(new HexCubePosition(0, 0)).AddPopulation(1);
			session.MarkDirty("m1");
			Check.Assert(session.IsDirty("m1"), "应标记为脏");

			session.FlushAll();

			Check.AssertEqual(2, repo.SaveCount, "存档点应写盘一次");
			Check.Assert(!session.IsDirty("m1"), "写盘后应清除脏标记");
		}

		private static void PersistenceStillTerrainOnly()
		{
			(MapSession session, InMemoryMapRepository repo) = NewSession();
			var position = new HexCubePosition(1, 1);

			session.Get("m1").AddOccupant(new FakeOccupant(position, "house", "uid-house-1", 0), position);
			session.Get("m1").GetCell(position).AddPopulation(2);
			session.MarkDirty("m1");
			session.FlushAll();

			// 新建会话（等价于"重开游戏"）：占据物与人口仍不在存档中 —— WP-3.2 才补齐
			var reopened = new MapSession(repo);
			Map map = reopened.Get("m1");

			Check.Assert(map != null, "地形应能读回");
			Check.Assert(map.GetCell(position).Terrain != null, "地形应存在");
			Check.AssertEqual(0, map.GetCell(position).Population, "人口尚未落盘（预期：WP-3.2 修复）");

			bool occupantGone = true;
			try { map.GetOccupantByUId("uid-house-1"); occupantGone = false; }
			catch (Exception) { occupantGone = true; }
			Check.Assert(occupantGone, "占据物尚未落盘（预期：WP-3.2 修复）");
		}
	}
}
