using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.3 / WP-6.2）**AI 决策循环**的验收检查：节拍、威胁分级、只读自己视野、三级优先级、
	/// 前期不造兵窗口，以及"人类不跑 AI 引擎"。
	/// <para>本 WP 只验"判断"（<see cref="AiDecision"/>）；下单（建造/科研/训练）归 `WP-6.3`/`WP-6.4`。</para>
	/// </summary>
	internal static class AiDecisionChecks
	{
		private const string MapId = "ai-decision";

		public static void RunAll()
		{
			Check.Run("WP-6.2 决策节拍：每 `DecisionIntervalDays` 日一次（不是每帧）", DecisionTicksOnSchedule);
			Check.Run("WP-6.2 威胁分级：按 `ThreatThresholds` 的可见敌数分档", ThreatLevelsFollowConfig);
			Check.Run("WP-6.2 只读自己视野：视野外的敌人看不见（不作弊的第一条）", OnlySeesItsOwnVision);
			Check.Run("WP-6.2 优先级：无住房 ⇒ 生存；有住房 + 无敌 ⇒ 发展", FocusFollowsPriority);
			Check.Run("WP-6.2 前期不造兵：窗口内计划军费 = 0，窗口过后 = 配置值", NoMilitaryWindowIsRespected);
			Check.Run("WP-6.2 人类不跑 AI 引擎（启动报告区分人类与 AI）", HumanHasNoAiEngine);
		}

		/// <summary>一个"开局已启动"的场地：2 个势力（人类 1 + AI 2）、地图全铺 plain（方便手动摆位）。</summary>
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

		private static void PlaceBuilding(CoreServices core, string buildingId, int ownerId, HexCubePosition position)
		{
			var building = new SciencePotato.Scripts.Construction.Domain.BuildingFactory(core.Tables.Buildings)
				.CreateBuilding(buildingId, position, ownerId);
			building.IsReady = true;
			Check.Assert(core.Map.PlaceBuilding(MapId, position, building), $"{buildingId}@{position} 应能落位");
		}

		private static void DecisionTicksOnSchedule()
		{
			(CoreServices core, _, _, _) = NewField(20261010);

			Check.Assert(core.AiService.StartEngine(MapId, 2), "应能挂上 AI 决策引擎");
			Check.Assert(!core.AiService.StartEngine(MapId, 2), "重复挂载应被拒（幂等）");
			Check.AssertEqual(0, core.AiService.DecisionsOf(MapId, 2).Count, "刚挂上还没到节拍，不应决策");

			int interval = core.Ai.DecisionIntervalDays;
			core.Session.Clock.AdvanceDays(interval);
			Check.AssertEqual(1, core.AiService.DecisionsOf(MapId, 2).Count, $"走了 {interval} 日应恰好决策 1 次");

			core.Session.Clock.AdvanceDays(interval * 2);
			Check.AssertEqual(3, core.AiService.DecisionsOf(MapId, 2).Count, "再走 2 个节拍应累计 3 次");

			AiDecision last = core.AiService.LastDecision(MapId, 2);
			Check.AssertEqual(interval * 3, last.Day, "决策应记录发生当日（可复盘）");
			Check.Assert(!string.IsNullOrWhiteSpace(last.Reason), "决策应带人类可读理由");
		}

		private static void ThreatLevelsFollowConfig()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			IAiThreatThresholds t = core.Ai.ThreatThresholds;

			Check.AssertEqual(AiThreatLevel.None, core.AiService.ClassifyThreat(0), "看不见敌人 = 无威胁");
			Check.AssertEqual(AiThreatLevel.Low, core.AiService.ClassifyThreat(t.Low), "达到 Low 阈值");
			Check.AssertEqual(AiThreatLevel.Medium, core.AiService.ClassifyThreat(t.Medium), "达到 Medium 阈值");
			Check.AssertEqual(AiThreatLevel.High, core.AiService.ClassifyThreat(t.High), "达到 High 阈值");
			Check.AssertEqual(AiThreatLevel.Lethal, core.AiService.ClassifyThreat(t.Lethal + 5), "超过 Lethal 阈值");
		}

		private static void OnlySeesItsOwnVision()
		{
			// ① 敌人在视野外 ⇒ 看不见（AI 不该"全知"）
			(CoreServices far, _, _, _) = NewField(20261011);
			PlaceUnit(far, "worker", 2, new HexCubePosition(5, 5));
			PlaceUnit(far, "swordsman", 1, new HexCubePosition(20, 5));

			AiObservation outside = far.AiService.Observe(MapId, 2);
			Check.AssertEqual(0, outside.VisibleEnemies, "视野外的敌人不应被看见（AI 不作弊）");
			Check.Assert(outside.MaxVisionRadius >= 3, "视野半径应来自单位表（工人 = 3）");
			Check.AssertEqual(AiThreatLevel.None, far.AiService.ClassifyThreat(outside.VisibleEnemies), "看不见 = 无威胁");

			// ② 同一场地上把敌人放进视野圈内 ⇒ 看见了
			PlaceUnit(far, "swordsman", 1, new HexCubePosition(7, 5));
			AiObservation inside = far.AiService.Observe(MapId, 2);
			Check.AssertEqual(1, inside.VisibleEnemies, "视野圈内的敌人应被看见");
			Check.AssertEqual(AiThreatLevel.Low, far.AiService.ClassifyThreat(inside.VisibleEnemies), "1 个敌人 = 低威胁");

			// ③ 视野是"自己单位的圈"：AI 没有单位时什么也看不见
			(CoreServices blind, _, _, _) = NewField(20261012);
			PlaceUnit(blind, "swordsman", 1, new HexCubePosition(5, 5));
			AiObservation noUnits = blind.AiService.Observe(MapId, 2);
			Check.AssertEqual(0, noUnits.VisibleEnemies, "没有自己的单位 ⇒ 看不见任何东西");
			Check.AssertEqual(0, noUnits.MaxVisionRadius, "没有自己的单位 ⇒ 视野半径 0");
		}

		private static void FocusFollowsPriority()
		{
			(CoreServices core, _, _, ResourcesAppService resources) = NewField(20261013);
			PlaceUnit(core, "worker", 2, new HexCubePosition(5, 5));

			// ① 没有住房 ⇒ 生存（人口无从增长）
			AiDecision survival = core.AiService.Evaluate(MapId, 2);
			Check.AssertEqual(AiFocus.Survival, survival.Focus, "没有住房时应优先生存");
			Check.AssertEqual(0f, survival.PlannedMilitaryShare, "生存优先时不投军事");
			Check.Assert(survival.Reason.Contains("住房"), "理由应说明是住房问题（可复盘）");

			// ② 补一座营地（住房）+ 足量口粮 ⇒ 无可见敌人 ⇒ 发展
			PlaceBuilding(core, "camp", 2, new HexCubePosition(6, 5));
			resources.AddResource("Food", 500f, MapId, 2);

			AiDecision development = core.AiService.Evaluate(MapId, 2);
			Check.Assert(development.Observation.HasHousing, "观测应看到自己的住房");
			Check.AssertEqual(AiFocus.Development, development.Focus, "有住房 + 无可见敌人 ⇒ 发展");
			Check.AssertEqual(AiThreatLevel.None, development.Threat, "威胁等级应为无");

			// ③ 口粮见底 ⇒ 回到生存（即使有住房）
			float stock = resources.GetOrCreatePool(MapId, 2).GetValue("Food");
			resources.AddResource("Food", -stock, MapId, 2); // 走服务（内部会存盘；直接改临时池不会落库）
			Check.AssertEqual(0f, resources.GetOrCreatePool(MapId, 2).GetValue("Food"), "准备：口粮已见底");

			// 人口落在营地格上（`AddPopulationWithin` 把增长放中心格）⇒ 归本势力 ⇒ 产生口粮需求
			core.Map.AddPopulation(MapId, new HexCubePosition(6, 5), 1, 999, 4);
			AiDecision starving = core.AiService.Evaluate(MapId, 2);
			Check.Assert(starving.Observation.FoodDemandPerMonth > 0f, "4 人口应产生口粮需求");
			Check.AssertEqual(AiFocus.Survival, starving.Focus, "口粮见底时应回到生存");
			Check.Assert(starving.Threat == AiThreatLevel.None, "威胁不变（场上只有自己的资产 + 无可见敌人）");
		}

		private static void NoMilitaryWindowIsRespected()
		{
			(CoreServices core, _, _, ResourcesAppService resources) = NewField(20261014);
			PlaceUnit(core, "worker", 2, new HexCubePosition(5, 5));
			PlaceUnit(core, "swordsman", 1, new HexCubePosition(6, 5)); // 视野内的敌人
			PlaceBuilding(core, "camp", 2, new HexCubePosition(7, 5));
			resources.AddResource("Food", 500f, MapId, 2);

			AiDecision early = core.AiService.Evaluate(MapId, 2);
			Check.AssertEqual(AiFocus.Threat, early.Focus, "看得见敌人 ⇒ 威胁优先");
			Check.AssertEqual(AiThreatLevel.Low, early.Threat, "1 个敌人 = 低威胁");
			Check.AssertEqual(0f, early.PlannedMilitaryShare, $"窗口内（{core.Ai.NoMilitaryDays} 日）不应造兵");
			Check.Assert(early.Reason.Contains(core.Ai.NoMilitaryDays.ToString()), "理由应写明仍在窗口内");

			// 跳过窗口（1 游戏年）
			core.Session.Clock.AdvanceDays(core.Ai.NoMilitaryDays);
			resources.AddResource("Food", 500f, MapId, 2);

			AiDecision late = core.AiService.Evaluate(MapId, 2);
			Check.AssertEqual(AiFocus.Threat, late.Focus, "过了窗口仍看得见敌人 ⇒ 仍以威胁为重");
			Check.AssertEqual(core.Ai.ResourceSplit.Military, late.PlannedMilitaryShare, "窗口过后按配置投入军事");
		}

		private static void HumanHasNoAiEngine()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(20261015, 24, 24, MapId);

			var reports = core.Orchestrator.StartMap(MapId);
			Check.AssertEqual(core.Session.Players.Count, reports.Count, "每个势力都应有启动报告");

			PlayerStartReport human = reports.First(r => r.OwnerId == 1);
			PlayerStartReport ai = reports.First(r => r.OwnerId == 2);

			Check.Assert(!human.AiEngineStarted, "人类不跑 AI 决策引擎");
			Check.Assert(ai.AiEngineStarted, "AI 势力应挂上决策引擎");
			Check.Assert(!core.AiService.IsEngineStarted(MapId, 1), "人类侧确实没有 AI 引擎");
			Check.Assert(core.AiService.IsEngineStarted(MapId, 2), "AI 侧确实有 AI 引擎");
		}
	}
}
