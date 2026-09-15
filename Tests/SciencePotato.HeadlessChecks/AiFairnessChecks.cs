using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
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
	/// （v0.7.7 / WP-6.5）**AI 信息公平**：把"不作弊"钉成测试判据（`design/AI.md` 的"AI 的限制"）。
	/// <para>四条判据（全部是**差分**判据：改变"AI 不该知道的东西"，断言 AI 的决策/行为不变）：</para>
	/// <list type="number">
	/// <item>**视野之外的信息不进决策**（`WP-6.2` 的几何视野）：把敌人挪出视野 ≡ 世上没有这个敌人；</item>
	/// <item>**不读对手的状态**：玩家有多少资产/资源，都与 AI 的判断无关；</item>
	/// <item>**不凭空生成**：没有军营就没有兵、没有资源来源就不长资源（只按月结成长）；</item>
	/// <item>**科技选择与玩家无关**：玩家在研究什么都改变不了 AI 选哪个节点。</item>
	/// </list>
	/// <para>为什么用差分：这类缺陷的表现是"AI 悄悄变聪明"，正向断言（"AI 造了兵"）抓不到它。</para>
	/// </summary>
	internal static class AiFairnessChecks
	{
		private const string MapId = "ai-fairness";

		public static void RunAll()
		{
			Check.Run("WP-6.5 不作弊·视野：视野外的敌人与世上没有这个敌人，决策完全一致", OutOfSightIsInvisible);
			Check.Run("WP-6.5 不作弊·对手状态：玩家资产/资源多寡不影响 AI 判断", OpponentStateDoesNotLeak);
			Check.Run("WP-6.5 不作弊·不凭空生成：没军营就没兵，新建筑只能长在自家附近", NeverSpawnsFromThinAir);
			Check.Run("WP-6.5 不作弊·资源：AI 的资源不会超过配置上限（没有隐藏收入）", ResourcesStayWithinConfigLimits);
			Check.Run("WP-6.5 不作弊·科技：玩家在研究什么都改变不了 AI 的科技选择", TechChoiceIgnoresOpponent);
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

		private static void PlaceBuilding(CoreServices core, string buildingId, int ownerId, HexCubePosition position)
		{
			var building = new BuildingFactory(core.Tables.Buildings).CreateBuilding(buildingId, position, ownerId);
			building.IsReady = true;
			Check.Assert(core.Map.PlaceBuilding(MapId, position, building), $"{buildingId}@{position} 应能落位");
		}

		/// <summary>局面的"决策指纹"：AI 判断的可比较摘要（侧重点 / 威胁 / 计划军费 / 可见敌数）。</summary>
		private static string Fingerprint(CoreServices core)
		{
			AiDecision decision = core.AiService.Decide(core.AiService.Observe(MapId, 2));
			return $"{decision.Focus}|{decision.Threat}|{decision.PlannedMilitaryShare:0.###}|{decision.Observation.VisibleEnemies}" +
				   $"|{decision.Observation.UnitCount}|{decision.Observation.BuildingCount}|{decision.Observation.HasHousing}";
		}

		private static void PrepareBaseline(CoreServices core, HexCubePosition workerAt)
		{
			PlaceUnit(core, "worker", 2, workerAt);
			PlaceBuilding(core, "camp", 2, new HexCubePosition(workerAt.q + 1, workerAt.r));
			core.Resources.AddResource("Food", 500f, MapId, 2);
		}

		private static void OutOfSightIsInvisible()
		{
			// A：敌人站在 AI 视野之外（工人视野 3，敌人在 20 格外）
			(CoreServices far, _, _, _) = NewField(20261040);
			PrepareBaseline(far, new HexCubePosition(5, 5));
			PlaceUnit(far, "swordsman", 1, new HexCubePosition(20, 5));
			string withHiddenEnemy = Fingerprint(far);

			// B：世上压根没有这个敌人
			(CoreServices none, _, _, _) = NewField(20261040);
			PrepareBaseline(none, new HexCubePosition(5, 5));
			string withoutEnemy = Fingerprint(none);

			Check.AssertEqual(withoutEnemy, withHiddenEnemy,
				"视野外的敌人不该影响任何判断（若实现偷看全图，这里就会不等）");
			Check.AssertEqual("0", withHiddenEnemy.Split('|')[3], "指纹里的可见敌数应为 0");

			// 对照组：把同一个敌人挪进视野 ⇒ 指纹必须变（证明上面那条不是因为"AI 什么都不看"）
			PlaceUnit(far, "swordsman", 1, new HexCubePosition(4, 5)); // 工人 (5,5) 视野 3 内，且不与营地 (6,5) 冲突
			Check.Assert(Fingerprint(far) != withHiddenEnemy, "同一个敌人进视野后判断必须变化（对照组：防判据恒真）");
		}

		private static void OpponentStateDoesNotLeak()
		{
			// A：对手一无所有
			(CoreServices poor, _, _, _) = NewField(20261041);
			PrepareBaseline(poor, new HexCubePosition(5, 5));
			string poorPrint = Fingerprint(poor);

			// B：对手一堆资产 + 满仓资源（都在 AI 视野之外，也不该被读账本）
			(CoreServices rich, _, _, ResourcesAppService richResources) = NewField(20261041);
			PrepareBaseline(rich, new HexCubePosition(5, 5));
			for (int i = 0; i < 5; i++) PlaceUnit(rich, "swordsman", 1, new HexCubePosition(18, 2 + i));
			PlaceBuilding(rich, "school", 1, new HexCubePosition(19, 8));
			richResources.AddResource("BasicMinerals", 5000f, MapId, 1);
			richResources.AddResource("Idea", 5000f, MapId, 1);
			string richPrint = Fingerprint(rich);

			Check.AssertEqual(poorPrint, richPrint, "对手富得流油也不该改变 AI 的判断（不读对手账本）");
		}

		private static void NeverSpawnsFromThinAir()
		{
			(CoreServices core, MapAppService map, _, _) = NewField(20261042);
			PrepareBaseline(core, new HexCubePosition(5, 5));

			int unitsBefore = OwnCount(map, OccupantType.Unit);
			int interval = core.Ai.DecisionIntervalDays;
			core.AiService.StartEngine(MapId, 2);
			core.Session.Clock.AdvanceDays(interval * 3); // 3 个节拍：决策 + 经济下单都真的跑起来

			int unitsAfter = OwnCount(map, OccupantType.Unit);
			Check.AssertEqual(unitsBefore, unitsAfter, "没有军营 ⇒ 跑再多节拍也不该多出一个兵（不凭空生成）");

			// 新出现的建筑必须是"AI 自己建起来的"：落在自家已有资产 6 格内（不是凭空出现在别处）
			var anchors = map.GetOccupants(MapId)
				.Where(o => o.GetInfo().OwnerId == 2 && o.GetInfo().Type == OccupantType.Unit)
				.Select(o => o.GetInfo().Position)
				.ToList();

			foreach (IMapOccupant building in map.GetOccupants(MapId)
						 .Where(o => o.GetInfo().OwnerId == 2 && o.GetInfo().Type == OccupantType.Building
									 && o.GetInfo().Id != "camp"))
			{
				Check.Assert(anchors.Any(a => a.DistenceTo(building.GetInfo().Position) <= 7),
					$"新建的 {building.GetInfo().Id} 应长在自家单位附近（实际 {building.GetInfo().Position}）");
			}
		}

		private static void ResourcesStayWithinConfigLimits()
		{
			(CoreServices core, _, _, ResourcesAppService resources) = NewField(20261043);
			PrepareBaseline(core, new HexCubePosition(5, 5));

			resources.RefreshLimits(MapId, 2);
			ResourcesPool pool = resources.GetOrCreatePool(MapId, 2);

			foreach (IResourceConfig resource in core.Tables.AllResources())
			{
				float limit = pool.GetLimit(resource.Name);
				float value = pool.GetValue(resource.Name);
				Check.Assert(value <= limit + 0.001f, $"{resource.Name}={value} 不应超过配置上限 {limit}（没有隐藏收入）");
			}

			core.AiService.StartEngine(MapId, 2);
			core.Session.Clock.AdvanceDays(core.Ai.DecisionIntervalDays);
			ResourcesPool after = resources.GetOrCreatePool(MapId, 2);
			foreach (IResourceConfig resource in core.Tables.AllResources())
				Check.Assert(after.GetValue(resource.Name) <= after.GetLimit(resource.Name) + 0.001f,
					$"{resource.Name} 跑过一拍后仍应在上限内（AI 没有给自己发资源）");
		}

		private static void TechChoiceIgnoresOpponent()
		{
			string treeId;

			// A：玩家什么都没研究
			(CoreServices quiet, _, _, _) = NewField(20261044);
			PrepareBaseline(quiet, new HexCubePosition(5, 5));
			treeId = quiet.Ai.SciencePreference;
			AiEconomyResult quietPick = quiet.AiEconomy.Execute(Decision(quiet));

			// B：玩家把偏好树的第一节点抢在研究（AI 的科技选择不该被"对手在做什么"左右）
			(CoreServices busy, _, _, _) = NewField(20261044);
			PrepareBaseline(busy, new HexCubePosition(5, 5));
			busy.Tech.Research(MapId, 1, treeId, "counting");
			AiEconomyResult busyPick = busy.AiEconomy.Execute(Decision(busy));

			Check.Assert(quietPick.Researched, $"准备：AI 应开工研究（{quietPick.Reason}）");
			Check.AssertEqual(quietPick.ResearchNode, busyPick.ResearchNode,
				"玩家在研究什么，不该改变 AI 选哪个科技节点（`A-AI-9` 不完美克制）");
			Check.AssertEqual(quietPick.ResearchTree, busyPick.ResearchTree, "选择仍在同一个流派树");
		}

		private static AiDecision Decision(CoreServices core, int ownerId = 2)
			=> new()
			{
				MapId = MapId,
				OwnerId = ownerId,
				Day = core.Session.CurrentDay,
				Focus = AiFocus.Development,
				Threat = AiThreatLevel.None,
				Reason = "（用例桩）",
			};

		private static int OwnCount(MapAppService map, OccupantType type)
			=> map.GetOccupants(MapId).Count(o => o.GetInfo().OwnerId == 2 && o.GetInfo().Type == type);
	}
}
