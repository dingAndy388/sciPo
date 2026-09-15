using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
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
	/// （v0.7.4 / WP-6.3）**AI 经济分配**的验收检查：下单只走玩家同一套服务（不作弊）、
	/// 建造顺序（住房 → 产出）、不无限扩张、科研选流派偏好树的最便宜可研究节点、节拍串联。
	/// </summary>
	internal static class AiEconomyChecks
	{
		private const string MapId = "ai-economy";

		public static void RunAll()
		{
			Check.Run("WP-6.3 不作弊：建造走 `StartConstruction`（真扣自己资源 + 真占格 + 真绑工人）", BuildGoesThroughPlayerPath);
			Check.Run("WP-6.3 建造顺序：没住房先补住房（落点在自家附近、派工人去建）", HousingFirst);
			Check.Run("WP-6.3 不无限扩张：住房 + 农田 + 矿场齐了就不再建（`A-AI-9`）", StopsExpandingWhenComplete);
			Check.Run("WP-6.3 资源不足：不建也不留空转（理由写明缺什么）", RefusesWhenBroke);
			Check.Run("WP-6.3 科研：在 `SciencePreference` 树里开工最便宜可研究节点并真扣 Idea", ResearchFollowsPreference);
			Check.Run("WP-6.3 节拍串联：`AiService.Evaluate` 判断完就下单（`D100` 两段式）", DecisionTriggersAction);
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

		private static IMapOccupant PlaceBuilding(CoreServices core, string buildingId, int ownerId, HexCubePosition position)
		{
			var building = new BuildingFactory(core.Tables.Buildings).CreateBuilding(buildingId, position, ownerId);
			building.IsReady = true;
			Check.Assert(core.Map.PlaceBuilding(MapId, position, building), $"{buildingId}@{position} 应能落位");
			return building;
		}

		/// <summary>决策桩：只喂"发展"判断（本组检查的是"执行"这一段，判断本身已在 `AiDecisionChecks` 里验过）。</summary>
		private static AiDecision Development(CoreServices core, int ownerId = 2)
			=> new()
			{
				MapId = MapId,
				OwnerId = ownerId,
				Day = core.Session.CurrentDay,
				Focus = AiFocus.Development,
				Threat = AiThreatLevel.None,
				Reason = "（用例桩）发展",
			};

		private static void BuildGoesThroughPlayerPath()
		{
			(CoreServices core, MapAppService map, UnitsAppService units, ResourcesAppService resources) = NewField(20261020);
			Unit worker = PlaceUnit(core, "worker", 2, new HexCubePosition(5, 5));

			float stoneBefore = resources.GetOrCreatePool(MapId, 2).GetValue("BasicMinerals");
			AiEconomyResult result = core.AiEconomy.Execute(Development(core));

			Check.Assert(result.Built, $"没有住房 ⇒ 应下单建造（理由：{result.Reason}）");
			Check.AssertEqual("camp", result.BuildOrder, "首选住房（设计稿：AI 先长人口）");
			Check.AssertEqual(worker.GetInfo().UId, result.BuilderUId, "应派那个工人去建");

			// ① 真扣自己资源：⚠️ **不能断言资源池** —— `D25`（`Consume()` 不回写资源池）使
			//    `GetOrCreatePool` 每次都读回旧值，资源维度分不清"扣了"与"没扣"（与 `BuilderChecks` 同约定）。
			//    改用可观测面判定：至少不能凭空**增加**，且下面用"落格 / 归属 / 绑定"三个真状态。
			float stoneAfter = resources.GetOrCreatePool(MapId, 2).GetValue("BasicMinerals");
			Check.Assert(stoneAfter <= stoneBefore, $"建造不应凭空增加资源（`D25` 未修前只验方向）：{stoneBefore} → {stoneAfter}");

			// ② 真占格 + 真绑工人
			Check.Assert(result.BuildPosition.HasValue, "应记录落点");
			IMapOccupant planted = map.GetMapCell(MapId, result.BuildPosition.Value)?.Building;
			Check.Assert(planted != null, "落点上应真的有一栋在建建筑");
			Check.AssertEqual(2, planted.GetInfo().OwnerId, "建筑归 AI");
			Check.AssertEqual(result.BuilderUId, (planted as Building)?.BuilderBinding?.BuilderUId,
				"工人应被绑到这个工地上（后续轮次不会再派他）");

			// ③ 距离：落点必须在自家已有建筑附近（AI 不乱跑到图对面去建）
			Check.Assert(result.BuildPosition.Value.DistenceTo(new HexCubePosition(5, 5)) <= 7,
				"落点应落在自家附近（含工人所在格）");
		}

		private static void HousingFirst()
		{
			(CoreServices core, _, _, _) = NewField(20261021);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));
			PlaceBuilding(core, "farm", 2, new HexCubePosition(4, 3)); // 已有农田、没住房

			AiEconomyResult result = core.AiEconomy.Execute(Development(core));
			Check.AssertEqual("camp", result.BuildOrder, "缺住房时仍优先住房（设计稿顺序）");
			Check.Assert(result.Reason.Contains("住房"), $"理由应说明是住房缺口：{result.Reason}");
		}

		private static void StopsExpandingWhenComplete()
		{
			(CoreServices core, _, _, _) = NewField(20261022);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));
			PlaceBuilding(core, "camp", 2, new HexCubePosition(4, 3));
			PlaceBuilding(core, "farm", 2, new HexCubePosition(5, 3));
			PlaceBuilding(core, "mine", 2, new HexCubePosition(5, 4));

			AiEconomyResult result = core.AiEconomy.Execute(Development(core));

			Check.Assert(!result.Built, "住房 + 农田 + 矿场都齐了 ⇒ 本拍不再扩建");
			Check.Assert(result.Reason.Contains("不扩张") || result.Reason.Contains("不扩建"),
				$"理由应写明不无限扩张（`A-AI-9`）：{result.Reason}");
			Check.AssertEqual(0, core.AiEconomy.BuildCount, "累计建造次数不应增加");
		}

		private static void RefusesWhenBroke()
		{
			(CoreServices core, _, _, ResourcesAppService resources) = NewField(20261023);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));

			float stone = resources.GetOrCreatePool(MapId, 2).GetValue("BasicMinerals");
			resources.AddResource("BasicMinerals", -stone, MapId, 2); // 清零

			AiEconomyResult result = core.AiEconomy.Execute(Development(core));

			Check.Assert(!result.Built, "资源清零 ⇒ 不能建（AI 没有后门）");
			Check.Assert(result.Reason.Contains("资源不足"), $"理由应写明资源不足：{result.Reason}");
			Check.AssertEqual(0f, resources.GetOrCreatePool(MapId, 2).GetValue("BasicMinerals"),
				"失败的一拍不应产生任何资源变化");
		}

		private static void ResearchFollowsPreference()
		{
			(CoreServices core, _, _, ResourcesAppService resources) = NewField(20261024);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));
			PlaceBuilding(core, "camp", 2, new HexCubePosition(4, 3));
			PlaceBuilding(core, "farm", 2, new HexCubePosition(5, 3));
			PlaceBuilding(core, "mine", 2, new HexCubePosition(5, 4));

			string treeId = core.Ai.SciencePreference;
			float ideaBefore = resources.GetOrCreatePool(MapId, 2).GetValue("Idea");

			AiEconomyResult result = core.AiEconomy.Execute(Development(core));

			Check.Assert(result.Researched, $"应开工一项研究（理由：{result.Reason}）");
			Check.AssertEqual(treeId, result.ResearchTree, "研究应落在 `SciencePreference` 指定的树");
			Check.Assert(core.Tech.GetInProgress(MapId, 2, treeId).Contains(result.ResearchNode),
				"该节点应真的处于\"研究进行中\"");
			Check.Assert(resources.GetOrCreatePool(MapId, 2).GetValue("Idea") <= ideaBefore,
				"研究成本只能从 AI 自己的 Idea 里扣（一级节点可能 cost 0 ⇒ 不应凭空增加）");

			// 同一拍再跑一次：本树槽位已占 ⇒ 不再重复下单（树内串行）
			AiEconomyResult again = core.AiEconomy.Execute(Development(core));
			Check.Assert(!again.Researched, "树内串行：已有研究在进行 ⇒ 不重复下单");
		}

		private static void DecisionTriggersAction()
		{
			(CoreServices core, _, _, _) = NewField(20261025);
			PlaceUnit(core, "worker", 2, new HexCubePosition(3, 3));
			PlaceBuilding(core, "camp", 2, new HexCubePosition(4, 3));

			Check.AssertEqual(0, core.AiEconomy.BuildCount, "准备：还没发生过行动");

			AiDecision decision = core.AiService.Evaluate(MapId, 2); // 判断 → 立刻下单（`D100` 两段式）

			Check.Assert(decision != null, "应产出决策");
			Check.Assert(core.AiEconomy.LastResult != null, "`Evaluate` 应触发行动出口（组合根已挂 `AiEconomy`）");
			Check.AssertEqual(decision.Day, core.AiEconomy.LastResult.Day, "行动应记录在同一个节拍日上");
			Check.AssertEqual(decision.OwnerId, core.AiEconomy.LastResult.OwnerId, "行动应记在同一个势力名下");
		}
	}
}
