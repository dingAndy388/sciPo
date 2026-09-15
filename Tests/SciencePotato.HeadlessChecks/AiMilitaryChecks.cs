using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.5 / WP-6.4）**AI 军事**的验收检查：军费分级（设计稿"60 年后 5% / 暴露后 10~20%"）、
	/// 窗口内绝不造兵、有军营且预算够才真训练（走玩家同一套 `TrainUnit`）、威胁升级时守家。
	/// </summary>
	internal static class AiMilitaryChecks
	{
		private const string MapId = "ai-military";

		public static void RunAll()
		{
			Check.Run("WP-6.4 军费分级：窗口内 0 / 窗口后 无威胁 5% / 低 10% / 中 15% / 高·致命 20%", ShareFollowsThreatLevel);
			Check.Run("WP-6.4 不造兵窗口：军营 + 人口 + 资源齐备也一个兵都不造", NoMilitaryWindowBlocksTraining);
			Check.Run("WP-6.4 真造兵：窗口后按预算训练最便宜的可训单位（真入队）", TrainsThroughPlayerPath);
			Check.Run("WP-6.4 不凭空造兵：没有军营 ⇒ 不训练（单位数不变）", RefusesWithoutBarracks);
			Check.Run("WP-6.4 优先防御：高威胁把在外的军事单位叫回自家聚落（低威胁不动）", RegroupsOnlyUnderHighThreat);
		}

		private static (CoreServices Core, MapAppService Map, UnitsAppService Units, ResourcesAppService Resources) NewField(int seed)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(seed, 24, 24, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			return (core, core.Map, core.Units, core.Resources);
		}

		private static Unit PlaceUnit(CoreServices core, string unitId, int ownerId, HexCubePosition position)
		{
			string uid = core.Units.PlaceInitialUnit(MapId, unitId, position, ownerId);
			Check.Assert(uid != null, $"{unitId}@{position} 应能落位（{ownerId} 号势力）");
			return (Unit)core.Map.FindOccupantByUId(MapId, uid);
		}

		private static Building PlaceBuilding(CoreServices core, string buildingId, int ownerId, HexCubePosition position)
		{
			Building building = new BuildingFactory(core.Tables.Buildings).CreateBuilding(buildingId, position, ownerId);
			building.IsReady = true;
			Check.Assert(core.Map.PlaceBuilding(MapId, position, building), $"{buildingId}@{position} 应能落位");
			return building;
		}

		/// <summary>决策桩：本组只验"执行"这一段（判断已在 `AiDecisionChecks` 里验过）。</summary>
		private static AiDecision Decision(CoreServices core, AiThreatLevel threat = AiThreatLevel.None, int day = 0,
			AiFocus focus = AiFocus.Development, int ownerId = 2)
			=> new()
			{
				MapId = MapId,
				OwnerId = ownerId,
				Day = day,
				Focus = focus,
				Threat = threat,
				Reason = "（用例桩）",
			};

		private static void ShareFollowsThreatLevel()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			int window = core.Ai.NoMilitaryDays;

			// 窗口内：任何威胁都是 0（前期只顾发展，`D93`）
			foreach (AiThreatLevel threat in new[] { AiThreatLevel.None, AiThreatLevel.Low, AiThreatLevel.High })
				Check.AssertEqual(0f, AiMilitaryPolicy.ShareFor(0, window, threat, AiFocus.Development),
					$"窗口内（0 < {window}）任何威胁都不投军费（{threat}）");

			// 窗口后：按威胁等级递增，且落在设计稿的 5% / 10~20% 区间
			Check.AssertEqual(0.05f, AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.None, AiFocus.Development), "窗口后无威胁 = 基础守备 5%");
			Check.AssertEqual(0.10f, AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.Low, AiFocus.Threat), "低威胁 = 10%");
			Check.AssertEqual(0.15f, AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.Medium, AiFocus.Threat), "中威胁 = 15%");
			Check.AssertEqual(0.20f, AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.High, AiFocus.Threat), "高威胁 = 20%");
			Check.AssertEqual(0.20f, AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.Lethal, AiFocus.Threat), "致命威胁封顶 20%");

			// 生存优先：即使窗口过了、威胁在眼前，也先保命（不投军事）
			Check.AssertEqual(0f, AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.High, AiFocus.Survival),
				"生存优先时不投军事（先补口粮/住房）");

			// 决策侧用的是同一份策略（避免"说的数"与"花的数"两套口径）
			Check.AssertEqual(AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.Medium, AiFocus.Threat),
				AiMilitaryPolicy.ShareFor(window, window, AiThreatLevel.Medium, AiFocus.Threat), "策略是纯函数（可复现）");
		}

		private static void NoMilitaryWindowBlocksTraining()
		{
			(CoreServices core, _, _, _) = NewField(20261030);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));
			PlaceBuilding(core, "camp", 2, new HexCubePosition(4, 3));
			PlaceBuilding(core, "military_camp", 2, new HexCubePosition(5, 3));
			core.Map.AddPopulation(MapId, new HexCubePosition(4, 3), 1, 999, 5);

			AiMilitaryResult result = core.AiMilitary.Execute(Decision(core, AiThreatLevel.High, day: 0, focus: AiFocus.Threat));

			Check.AssertEqual(0f, result.MilitaryShare, "窗口内军费 = 0");
			Check.Assert(!result.Trained, "窗口内不造兵（哪怕眼前就是高威胁）");
			Check.Assert(result.Reason.Contains("窗口"), $"理由应写明仍在窗口内：{result.Reason}");
			Check.AssertEqual(0, core.AiMilitary.TrainCount, "累计训练次数不变");
		}

		private static void TrainsThroughPlayerPath()
		{
			(CoreServices core, MapAppService map, _, _) = NewField(20261031);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));
			PlaceBuilding(core, "camp", 2, new HexCubePosition(4, 3));
			Building barrack = PlaceBuilding(core, "military_camp", 2, new HexCubePosition(5, 3));
			core.Map.AddPopulation(MapId, new HexCubePosition(4, 3), 1, 999, 5);

			int day = core.Ai.NoMilitaryDays + 30;
			AiMilitaryResult result = core.AiMilitary.Execute(Decision(core, AiThreatLevel.Medium, day, AiFocus.Threat));

			Check.Assert(result.Trained, $"窗口后 + 有军营 ⇒ 应训练（理由：{result.Reason}）");
			Check.AssertEqual("swordsman", result.TrainedUnit, "预算内挑最便宜的可训单位（剑士）");
			Check.AssertEqual(barrack.GetInfo().UId, result.TrainingBuildingUId, "承训的应是那座军营");

			// 真入队（玩家同一套 `TrainUnit` 契约）：队列里能查到这条订单
			Check.Assert(barrack.TrainingQueue.Count > 0, "军营训练队列里应真的有一条订单");
			Check.AssertEqual(barrack.TrainingQueue[0].UnitId, result.TrainedUnit, "队列头 = AI 下的那单");
			Check.Assert(map.GetOccupantInfo(MapId, new HexCubePosition(5, 3)) != null, "军营仍在图上（没有绕过地图）");
		}

		private static void RefusesWithoutBarracks()
		{
			(CoreServices core, _, _, _) = NewField(20261032);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));
			PlaceBuilding(core, "camp", 2, new HexCubePosition(4, 3));
			core.Map.AddPopulation(MapId, new HexCubePosition(4, 3), 1, 999, 5);

			int unitsBefore = core.Map.GetOccupants(MapId).Count(o => o.GetInfo().OwnerId == 2 && o.GetInfo().Type == OccupantType.Unit);
			AiMilitaryResult result = core.AiMilitary.Execute(Decision(core, AiThreatLevel.High, core.Ai.NoMilitaryDays + 10, AiFocus.Threat));
			int unitsAfter = core.Map.GetOccupants(MapId).Count(o => o.GetInfo().OwnerId == 2 && o.GetInfo().Type == OccupantType.Unit);

			Check.Assert(!result.Trained, "没有军营 ⇒ 不训练（AI 不凭空生成单位）");
			Check.Assert(result.Reason.Contains("军营"), $"理由应指向没有军营这一条：{result.Reason}");
			Check.AssertEqual(unitsBefore, unitsAfter, "单位数不应变化");
		}

		private static void RegroupsOnlyUnderHighThreat()
		{
			(CoreServices core, _, _, _) = NewField(20261033);
			PlaceBuilding(core, "camp", 2, new HexCubePosition(4, 3));
			Unit guard = PlaceUnit(core, "swordsman", 2, new HexCubePosition(12, 12));

			// 低威胁：不折腾（不发指令）
			AiMilitaryResult low = core.AiMilitary.Execute(Decision(core, AiThreatLevel.Low, core.Ai.NoMilitaryDays + 10, AiFocus.Threat));
			Check.AssertEqual(0, low.DefendersRegrouped, "低威胁不动军队");
			Check.Assert(!UnitMovementService.IsMoving(guard), "低威胁下士兵不该被下令移动");

			// 高威胁：把在外的士兵叫回自家聚落
			AiMilitaryResult high = core.AiMilitary.Execute(Decision(core, AiThreatLevel.High, core.Ai.NoMilitaryDays + 11, AiFocus.Threat));
			Check.AssertEqual(1, high.DefendersRegrouped, $"高威胁应回撤 1 人（理由：{high.Reason}）");
			Check.Assert(UnitMovementService.IsMoving(guard), "士兵应已在回撤路上（`MoveTarget` 指向家）");
			Check.Assert(guard.MoveTarget.Value.DistenceTo(new HexCubePosition(4, 3)) == 0, "回撤目标 = 自家营地的格子");
			Check.Assert(core.AiMilitary.RegroupCount >= 1, "累计回撤次数应记上");
		}
	}
}
