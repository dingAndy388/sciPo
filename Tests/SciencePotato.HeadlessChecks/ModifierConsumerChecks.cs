using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.8.3~v0.8.5 / `WP-4.1` + `WP-4.4` + `WP-4.5`）**修正器管道的正向断言** —— B1 的收口证据。
	/// <para>与"零回归"不同，这里断言的是<用户可见行为>：填了 `BuildingSpeed -30 日`，建造任务的目标天数
	/// **真的**从 120 变成 90；填了 `UnitSpeed +20%`，工人移动力真的从 10 变 12。没有这一组，
	/// "机制→内容生效"就只是说法。</para>
	/// </summary>
	internal static class ModifierConsumerChecks
	{
		private const string MapId = "modifier-consumers";

		public static void RunAll()
		{
			Check.Run("WP-4.1 阶段管道：建筑→科技→事件 顺序结算且可复现", StagesAreOrdered);
			Check.Run("WP-4.1 多 target 累加 + 宿主作用域（`GetValueForHost`）", MultiTargetAndHostScope);
			Check.Run("WP-4.4 `BuildingSpeed`：建造任务时长按 -30 日 / -10% 缩短", BuildingSpeedShortensBuild);
			Check.Run("WP-4.4 `ResearchSpeed` + `UnitTrainingSpeed`：研究/训练任务时长缩短", ResearchAndTrainingSpeed);
			Check.Run("WP-4.4 `UnitSpeed` + `FoodConsumption`：移动力/口粮需求生效且**不跨 owner 泄漏**", UnitSpeedAndFoodConsumption);
			Check.Run("WP-4.5 产出浮动：±20% 内波动，且上下限可被修正器改写为 +5%/-1%", OutputVarianceRollsAndOverrides);
		}

		private static (CoreServices Core, MapAppService Map) NewField(int seed = 20261100)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(seed, 20, 20, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			return (core, core.Map);
		}

		private static void Add(CoreServices core, int ownerId, string sourceId, ModifierStage stage,
			params (string Target, string Type, float Value)[] modifiers)
			=> core.Modifiers.AddModifiers(MapId, ownerId, sourceId,
				modifiers.Select(m => new Modifier { Target = m.Target, Type = m.Type, Value = m.Value }).ToList(), stage);

		private static Unit PlaceWorker(CoreServices core, HexCubePosition position)
		{
			string uid = core.Units.PlaceInitialUnit(MapId, "worker", position, 1);
			Check.Assert(uid != null, "工人应能落位");
			return (Unit)core.Map.FindOccupantByUId(MapId, uid);
		}

		private static Building PlaceBuilding(CoreServices core, string buildingId, int ownerId, HexCubePosition position, bool ready = true)
		{
			Building building = new BuildingFactory(core.Tables.Buildings).CreateBuilding(buildingId, position, ownerId);
			building.IsReady = ready;
			Check.Assert(core.Map.PlaceBuilding(MapId, position, building), $"{buildingId} 应能落位");
			return building;
		}

		private static float TaskTarget(CoreServices core, string type)
		{
			List<TaskSnapshot> tasks = core.Tasks?.GetCurrentTasks(MapId) ?? new List<TaskSnapshot>();
			var task = tasks.FirstOrDefault(t => t.Type == type);
			Check.Assert(task != null, $"应存在 {type} 任务");
			return task.Target;
		}

		private static void StagesAreOrdered()
		{
			(CoreServices core, _) = NewField();

			// 同 target 三阶段：建筑 +10（Absolute）、科技 ×2（Percent）、事件 ×1.5（Percent）
			Add(core, 1, "b", ModifierStage.Building, ("X", "Absolute", 10f));
			Add(core, 1, "t", ModifierStage.Tech, ("X", "Percent", 1f));
			Add(core, 1, "e", ModifierStage.Event, ("X", "Percent", 0.5f));

			// base 10 → 建筑 (10+10)=20 → 科技 ×2 = 40 → 事件 ×1.5 = 60
			Check.AssertEqual(60f, core.Modifiers.GetValue(MapId, 1, "X", 10f), "三阶段应按 建筑→科技→事件 依次结算");
			Check.AssertEqual(60f, core.Modifiers.GetValue(MapId, 1, "X", 10f), "同一输入应可复现（与注册顺序无关）");

			// 反向注册顺序（事件先挂）结果必须相同 —— 这正是阶段管道要解决的问题
			(CoreServices reversed, _) = NewField();
			Add(reversed, 1, "e", ModifierStage.Event, ("X", "Percent", 0.5f));
			Add(reversed, 1, "t", ModifierStage.Tech, ("X", "Percent", 1f));
			Add(reversed, 1, "b", ModifierStage.Building, ("X", "Absolute", 10f));
			Check.AssertEqual(60f, reversed.Modifiers.GetValue(MapId, 1, "X", 10f),
				"换注册顺序结果不变（旧实现会随加载顺序漂移）");
		}

		private static void MultiTargetAndHostScope()
		{
			(CoreServices core, _) = NewField();

			// 多 target：两个名字都属于同一语义（旧实现只取第一个命中 ⇒ 第二个被静默忽略）
			Add(core, 1, "warehouse", ModifierStage.Building, ("BasicMineralsLimit", "Absolute", 500f));
			Add(core, 1, "tech_limit", ModifierStage.Tech, ("ResourceLimit", "Absolute", 100f));
			float limit = core.Modifiers.GetValue(MapId, 1, new[] { "BasicMineralsLimit", "ResourceLimit" }, 1000f);
			Check.AssertEqual(1600f, limit, "多 target 应累加（1000 + 500 + 100）");

			// 同宿主同目标同类只算一次（宿主不自我叠加）
			Add(core, 1, "dup", ModifierStage.Building, ("Y", "Absolute", 5f), ("Y", "Absolute", 5f));
			Check.AssertEqual(15f, core.Modifiers.GetValue(MapId, 1, "Y", 10f), "同宿主同一目标的第二条同类修正不应叠加");

			// 宿主作用域：只算来自指定宿主的修正
			Add(core, 1, "other", ModifierStage.Tech, ("Y", "Absolute", 7f));
			Check.AssertEqual(15f, core.Modifiers.GetValueForHost(MapId, 1, "dup", new[] { "Y" }, 10f),
				"宿主作用域只结算该宿主的修正");
			Check.AssertEqual(17f, core.Modifiers.GetValueForHost(MapId, 1, "other", new[] { "Y" }, 10f),
				"换一个宿主应只看到它自己的修正");
		}
		private static void BuildingSpeedShortensBuild()
		{
			(CoreServices core, _) = NewField();
			Unit worker = PlaceWorker(core, new HexCubePosition(5, 5));
			var site = new HexCubePosition(6, 5);

			// 基准：农田 120 日
			core.Construction.StartConstruction(MapId, "farm", site, 1, new BuilderBinding { BuilderUId = worker.GetInfo().UId, TargetPosition = site });
			core.Session.Clock.AdvanceDays(90);
			Check.Assert(!core.Map.GetMapCell(MapId, site).Building.IsReady, "基准工期 120 日：第 90 日不应完工");
			core.Session.Clock.AdvanceDays(30);
			Check.Assert(core.Map.GetMapCell(MapId, site).Building.IsReady, "基准工期 120 日：第 120 日应完工");

			// 科技：建造时间 -30 日（Absolute）—— 设计稿"初步测量"类效果的写法
			(CoreServices scaled, _) = NewField();
			Unit worker2 = PlaceWorker(scaled, new HexCubePosition(5, 5));
			var site2 = new HexCubePosition(6, 5);
			Add(scaled, 1, "tech_build", ModifierStage.Tech, ("BuildingSpeed", "Absolute", -30f));
			scaled.Construction.StartConstruction(MapId, "farm", site2, 1, new BuilderBinding { BuilderUId = worker2.GetInfo().UId, TargetPosition = site2 });
			scaled.Session.Clock.AdvanceDays(89);
			Check.Assert(!scaled.Map.GetMapCell(MapId, site2).Building.IsReady, "`-30 日`：第 89 日不应完工（工期 90）");
			scaled.Session.Clock.AdvanceDays(1);
			Check.Assert(scaled.Map.GetMapCell(MapId, site2).Building.IsReady, "`-30 日` 应让工期 120 → 90（第 90 日完工）");

			// 叠一条 -10%（Percent）：同阶段先累加 Absolute 再乘 Percent ⇒ (120-30)×0.9 = 81
			(CoreServices both, _) = NewField();
			Unit worker3 = PlaceWorker(both, new HexCubePosition(5, 5));
			var site3 = new HexCubePosition(6, 5);
			Add(both, 1, "a", ModifierStage.Tech, ("BuildingSpeed", "Absolute", -30f));
			Add(both, 1, "b", ModifierStage.Tech, ("BuildingSpeed", "Percent", -0.1f));
			both.Construction.StartConstruction(MapId, "farm", site3, 1, new BuilderBinding { BuilderUId = worker3.GetInfo().UId, TargetPosition = site3 });
			both.Session.Clock.AdvanceDays(80);
			Check.Assert(!both.Map.GetMapCell(MapId, site3).Building.IsReady, "第 80 日不应完工（工期 81）");
			both.Session.Clock.AdvanceDays(1);
			Check.Assert(both.Map.GetMapCell(MapId, site3).Building.IsReady, "`-30 日` + `-10%` ⇒ (120-30)×0.9 = 81 日完工");

			// 跨 owner 隔离：加成挂在 AI（2）名下，玩家（1）的工期不受影响
			(CoreServices isolated, _) = NewField();
			Unit worker4 = PlaceWorker(isolated, new HexCubePosition(5, 5));
			var site4 = new HexCubePosition(6, 5);
			Add(isolated, 2, "ai_tech", ModifierStage.Tech, ("BuildingSpeed", "Absolute", -60f));
			isolated.Construction.StartConstruction(MapId, "farm", site4, 1, new BuilderBinding { BuilderUId = worker4.GetInfo().UId, TargetPosition = site4 });
			isolated.Session.Clock.AdvanceDays(89);
			Check.Assert(!isolated.Map.GetMapCell(MapId, site4).Building.IsReady, "AI 的科技不该给玩家加速（第 89 日仍不应完工）");
		}

		private static void ResearchAndTrainingSpeed()
		{
			// 研究：`chemistry/taming_of_fire` 10 日 → 科技 -50% ⇒ 5 日（对照：无修正器 10 日）
			(CoreServices core, _) = NewField();
			Add(core, 1, "tech_research", ModifierStage.Tech, ("ResearchSpeed", "Percent", -0.5f));
			core.Resources.AddResource("Idea", 500f, MapId, 1);
			core.Tech.Research(MapId, 1, "chemistry", "taming_of_fire");
			core.Session.Clock.AdvanceDays(4);
			Check.Assert(!core.Tech.GetOrCreateTechTree(MapId, 1, "chemistry").IsResearched("taming_of_fire"), "`ResearchSpeed -50%`：第 4 日不应完成（10 日 ×0.5 = 5 日）");
			core.Session.Clock.AdvanceDays(1);
			core.Session.Clock.AdvanceDays(3);
			Check.Assert(core.Tech.GetOrCreateTechTree(MapId, 1, "chemistry").IsResearched("taming_of_fire"), "第 8 日应完成（10 日 ×0.5 = 5 日，含日节拍余量）");

			// 对照：不加修正器 ⇒ 第 7 日仍不应完成（证明真的缩短了时长，而不是靠多跑几天）
			(CoreServices plain, _) = NewField();
			plain.Resources.AddResource("Idea", 500f, MapId, 1);
			plain.Tech.Research(MapId, 1, "chemistry", "taming_of_fire");
			plain.Session.Clock.AdvanceDays(7);
			Check.Assert(!plain.Tech.GetOrCreateTechTree(MapId, 1, "chemistry").IsResearched("taming_of_fire"), "对照：无修正器时第 7 日不应完成（基准 10 日）");

			// 训练：军营 + 人口 + 资源 ⇒ 剑士 35 日 → 训练速度 -50% ⇒ 17.5
			(CoreServices army, _) = NewField();
			Building barrack = PlaceBuilding(army, "military_camp", 1, new HexCubePosition(4, 4));
			army.Map.AddPopulation(MapId, new HexCubePosition(4, 4), 1, 999, 5);
			Add(army, 1, "tech_train", ModifierStage.Tech, ("UnitTrainingSpeed", "Percent", -0.5f));
			Check.Assert(army.Units.TrainUnit(MapId, barrack.GetInfo().UId, "swordsman"), "准备：应能下单训练");
			int unitsBefore = army.Map.GetOccupants(MapId).Count(o => o.GetInfo().Type == OccupantType.Unit && o.GetInfo().OwnerId == 1);
			army.Session.Clock.AdvanceDays(17);
			Check.AssertEqual(unitsBefore, army.Map.GetOccupants(MapId).Count(o => o.GetInfo().Type == OccupantType.Unit && o.GetInfo().OwnerId == 1), "`UnitTrainingSpeed -50%`：第 17 日还不该出兵（基准 35 日）");
			army.Session.Clock.AdvanceDays(1);
			Check.AssertEqual(unitsBefore + 1, army.Map.GetOccupants(MapId).Count(o => o.GetInfo().Type == OccupantType.Unit && o.GetInfo().OwnerId == 1), "第 18 日应出兵（35 日 ×0.5 = 17.5）");
		}

		private static void UnitSpeedAndFoodConsumption()
		{
			(CoreServices core, _) = NewField();
			Unit worker = PlaceWorker(core, new HexCubePosition(5, 5));
			Check.AssertEqual(10f, core.Units.Movement.MovementOf(worker, MapId), "准备：工人移动力 10");

			Add(core, 1, "tech_speed", ModifierStage.Tech, ("UnitSpeed", "Percent", 0.2f));
			Check.AssertEqual(12f, core.Units.Movement.MovementOf(worker, MapId), "`UnitSpeed +20%` 应让移动力 10 → 12");
			Check.AssertEqual(10f, core.Units.Movement.MovementOf(worker), "不传地图（兼容路径）不结算修正器 ⇒ 仍 10");

			(CoreServices plainCore, _) = NewField();
			plainCore.Map.AddPopulation(MapId, new HexCubePosition(5, 5), 1, 999, 4);
			plainCore.Resources.AddResource("Food", 1000f, MapId, 1);
			float demandPlain = plainCore.Settlement.Settle(MapId, 1).DemandByResource.GetValueOrDefault("Food");

			(CoreServices cheap, _) = NewField();
			cheap.Map.AddPopulation(MapId, new HexCubePosition(5, 5), 1, 999, 4);
			cheap.Resources.AddResource("Food", 1000f, MapId, 1);
			Add(cheap, 1, "tech_food", ModifierStage.Tech, ("FoodConsumption", "Percent", -0.25f));
			float demandCheap = cheap.Settlement.Settle(MapId, 1).DemandByResource.GetValueOrDefault("Food");

			Check.Assert(demandPlain > 0f, $"准备：基准口粮需求应 > 0（实际 {demandPlain}）");
			Check.AssertEqual(demandPlain * 0.75f, demandCheap, "`FoodConsumption -25%` 应让口粮需求降 25%");

			(CoreServices other, _) = NewField();
			other.Map.AddPopulation(MapId, new HexCubePosition(5, 5), 1, 999, 4);
			other.Resources.AddResource("Food", 1000f, MapId, 1);
			Add(other, 2, "ai_food", ModifierStage.Tech, ("FoodConsumption", "Percent", -0.25f));
			Check.AssertEqual(demandPlain, other.Settlement.Settle(MapId, 1).DemandByResource.GetValueOrDefault("Food"),
				"AI 的减耗不该改变玩家的口粮需求");
		}
		private static void OutputVarianceRollsAndOverrides()
		{
			(CoreServices core, _) = NewField();
			Building farm = PlaceBuilding(core, "farm", 1, new HexCubePosition(5, 5));
			core.Modifiers.AddModifiers(MapId, 1, farm.GetInfo().UId,
				core.Tables.Buildings.GetBuildingConfig("farm").Modifiers);

			float baseline = 12f; // 农田 FoodGrowth Absolute 12/月（基准产出）
			var rolls = new List<float>();
			for (int i = 0; i < 8; i++)
				rolls.Add(core.Settlement.Settle(MapId, 1).ProductionOf("Food"));

			Check.Assert(rolls.All(r => r >= baseline * 0.79f && r <= baseline * 1.21f),
				$"农田（±20%）的每月产出应落在 [80%,120%]：{string.Join(",", rolls.Select(r => r.ToString("0.#")))}");
			Check.Assert(rolls.Distinct().Count() > 1, "产出应有浮动（不是每月同一个数）");

			// 上下限改写：观星台"上限 +5% / 下限 -1%"（`WP-4.2` 按此填范围效果；这里只验证消费点）
			(CoreServices narrowed, _) = NewField();
			Building farm2 = PlaceBuilding(narrowed, "farm", 1, new HexCubePosition(5, 5));
			narrowed.Modifiers.AddModifiers(MapId, 1, farm2.GetInfo().UId,
				narrowed.Tables.Buildings.GetBuildingConfig("farm").Modifiers);
			Add(narrowed, 1, "observatory", ModifierStage.Tech,
				("OutputVarianceUpper", "Absolute", 0.05f), ("OutputVarianceLower", "Absolute", 0.01f));

			var narrowedRolls = new List<float>();
			for (int i = 0; i < 8; i++)
				narrowedRolls.Add(narrowed.Settlement.Settle(MapId, 1).ProductionOf("Food"));

			Check.Assert(narrowedRolls.All(r => r >= baseline * 0.989f && r <= baseline * 1.051f),
				$"上限/下限被改写后应落在 [99%,105%]：{string.Join(",", narrowedRolls.Select(r => r.ToString("0.#")))}");
		}
	}
}
