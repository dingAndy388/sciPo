using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Domain;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.6.4 / WP-5.9）**开局布置**的验收检查：出生点（间距约束 / 可通行）、开局单位（人类与 AI 同待遇）、
	/// 开局资源、人类开局视野、幂等。
	/// <para>为什么这组要单独验：`D88` 的核心承诺是"人类与 AI 规则完全相同，差别只在谁下指令" ——
	/// 如果 AI 的出生点更差、单位更少，那"不作弊"就是空话。本组把可验证的那部分钉死。</para>
	/// </summary>
	internal static class SessionSetupChecks
	{
		private const string MapId = "setup";

		public static void RunAll()
		{
			Check.Run("WP-5.9 开局：每个势力各得一个出生点（可通行、离得够远）", SpawnsRespectDistanceAndTerrain);
			Check.Run("WP-5.9 开局：人类与 AI 拿到相同的开局单位并直接可用", InitialUnitsAreSymmetric);
			Check.Run("WP-5.9 开局：人类出生点周围视野被揭示（按 RevealRadius）", HumanSpawnIsRevealed);
			Check.Run("WP-5.9 开局：重复 StartMap 不重复放人（幂等）", SetupIsIdempotent);
			Check.Run("WP-5.9 开局：配置缺省与真实表一致（间距 20 / 揭示 3 / 1 工人）", ConfigDefaultsMatch);
		}

		private static void SpawnsRespectDistanceAndTerrain()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(20260922, 40, 40, MapId);

			var spawns = core.Setup.Setup(MapId);
			Check.AssertEqual(2, spawns.Count, "两个势力各一个出生点");

			foreach (PlayerSpawn spawn in spawns)
			{
				MapCell cell = core.Map.GetMapCell(MapId, spawn.Position);
				Check.Assert(cell?.Terrain?.Passable == true, $"owner={spawn.OwnerId} 的出生点应在可通行地形上");
			}

			int distance = SessionSetupService.HexDistance(spawns[0].Position, spawns[1].Position);
			Check.Assert(distance >= core.Tables.Start.MinSpawnDistance,
				$"两个出生点间距 {distance} 应 ≥ MinSpawnDistance {core.Tables.Start.MinSpawnDistance}");
		}

		private static void InitialUnitsAreSymmetric()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(20260923, 40, 40, MapId);

			var spawns = core.Setup.Setup(MapId);

			foreach (PlayerSpawn spawn in spawns)
			{
				Check.AssertEqual(core.Tables.Start.InitialUnits.Count, spawn.UnitUIds.Count,
					$"owner={spawn.OwnerId} 的开局单位数应 = 配置（人类与 AI 同待遇）");
			}

			Check.AssertEqual(spawns[0].UnitUIds.Count, spawns[1].UnitUIds.Count, "两个势力的开局单位数相同");

			// 单位真的在图上，而且**直接就绪**（开局不该等训练队列）
			foreach (PlayerSpawn spawn in spawns)
				foreach (string uid in spawn.UnitUIds)
				{
					IMapOccupant unit = core.Map.FindOccupantByUId(MapId, uid);
					Check.Assert(unit != null, $"开局单位 {uid} 应在图上");
					Check.Assert(unit.GetInfo().OwnerId == spawn.OwnerId, $"开局单位 {uid} 的归属应为 owner={spawn.OwnerId}");
					Check.Assert(unit.IsReady, $"开局单位 {uid} 应直接就绪");
				}
		}

		private static void HumanSpawnIsRevealed()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Map.GenerateMap(20260924, 40, 40, MapId);

			var spawns = core.Setup.Setup(MapId);
			var human = spawns.First(s => s.OwnerId == core.Session.HumanOwnerId);

			Check.AssertEqual(FogAppService.Visible, core.Fog.GetVisibility(human.Position), "人类出生点自身应可见");

			int visible = 0;
			foreach (HexCubePosition pos in human.Position.InRadius(core.Tables.Start.RevealRadius))
				if (core.Fog.GetVisibility(pos) == FogAppService.Visible) visible++;

			Check.Assert(visible > 0, "出生点周围应有可见格（`RevealRadius` 生效）");
		}

		private static void SetupIsIdempotent()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Map.GenerateMap(20260925, 40, 40, MapId);

			var first = core.Setup.Setup(MapId);
			int unitsAfterFirst = core.Map.GetOccupants(MapId).Count();

			var second = core.Setup.Setup(MapId);

			Check.AssertEqual(first[0].Position.q, second[0].Position.q, "重复布置应返回同一出生点（同 q）");
			Check.AssertEqual(first[0].Position.r, second[0].Position.r, "重复布置应返回同一出生点（同 r）");
			Check.AssertEqual(unitsAfterFirst, core.Map.GetOccupants(MapId).Count(), "重复布置不应重复放人");
		}

		private static void ConfigDefaultsMatch()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			IStartSetupConfig start = core.Tables.Start;

			Check.AssertEqual(20, start.MinSpawnDistance, "`Start.MinSpawnDistance`（Config/Generator.json）");
			Check.AssertEqual(3, start.RevealRadius, "`Start.RevealRadius`");
			Check.AssertEqual(1, start.InitialUnits.Count, "`Start.InitialUnits` 条数");
			Check.AssertEqual("worker", start.InitialUnits[0], "开局单位 Id");
			Check.AssertEqual(0, start.InitialResources.Count, "`Start.InitialResources`（空 = 只用资源表初始储备）");

			// 缺表时也要有兜底（表现层/无头工具不该因为少一张表就崩）
			CoreServices broken = ConfigFixtures.BuildCore(new InMemoryConfigSource(), failOnConfigErrors: false);
			Check.AssertEqual(20, broken.Tables.Start.MinSpawnDistance, "缺表时的兜底间距");
		}
	}
}
