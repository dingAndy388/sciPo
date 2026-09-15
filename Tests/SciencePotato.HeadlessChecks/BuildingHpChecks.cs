using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.0 / WP-4.8）**建筑 HP + 夺取**的验收检查。
	/// <para>口径（`D73`，用户确认）：HP &gt; 0 ⇒ 该格**不可进入**；**HP 归零 ⇒ 转为"可夺取"**（建筑还在、
	/// 仍是原主人的资产）；**敌方单位站上该格 ⇒ 易主**（HP 恢复半血、修正器换主人）。</para>
	/// </summary>
	internal static class BuildingHpChecks
	{
		private const string MapId = "building-hp";

		public static void RunAll()
		{
			Check.Run("WP-4.8 HP 配置：住房/军事有 HP，生产/存储建筑免疫伤害", HpComesFromConfig);
			Check.Run("WP-4.8 掉血：打到归零 ⇒ 建筑还在但转为可夺取（格子可进入）", DamageZeroesIntoCapturable);
			Check.Run("WP-4.8 夺取：单位站上可夺取的敌方建筑 ⇒ 易主 + 半血 + 修正器换主人", UnitCapturesBuilding);
			Check.Run("WP-4.8 不可进入：HP>0 时该格进不去（必须先打）", BuildingBlocksMovementUntilZero);
			Check.Run("WP-4.8 胜负联动：建筑的资产变化触发重算（唯一资产消失 ⇒ 出局）", BuildingLossTriggersVictory);
		}

		private static void HpComesFromConfig()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			IBuildingConfig camp = core.Tables.Buildings.GetBuildingConfig("camp");
			IBuildingConfig school = core.Tables.Buildings.GetBuildingConfig("school");
			IBuildingConfig farm = core.Tables.Buildings.GetBuildingConfig("farm");

			Check.Assert(camp.HasHP && camp.HP > 0f, "营地（住房）应有 HP");
			Check.Assert(!school.HasHP, "学院（研究建筑）不应有 HP");
			Check.Assert(!farm.HasHP, "农田（生产建筑）不应有 HP");

			var factory = new BuildingFactory(core.Tables.Buildings);
			Building instance = factory.CreateBuilding("camp", new HexCubePosition(0, 0), 1);
			Check.Assert(instance.HasHP && instance.HP == camp.HP, "建筑实例的 HP 应来自配置");
			Check.AssertEqual(camp.HP, instance.GetInfo().HP, "`MapOccupantInfo.HP` 应反映当前血量");
			Check.AssertEqual(-1f, factory.CreateBuilding("farm", new HexCubePosition(1, 0), 1).GetInfo().HP,
				"无 HP 模型的建筑沿用 -1 口径（旧语义不变）");
		}

		private static void DamageZeroesIntoCapturable()
		{
			Harness h = NewHarness();
			try
			{
				var cell = new HexCubePosition(3, 3);
				IMapOccupant camp = h.PlaceBuilding("camp", 2, cell);

				Check.Assert(!h.Map.IsClear(MapId, cell), "有 HP 的建筑格初始不可进入");

				var damageable = (IDamageable)camp;
				h.Map.ApplyBuildingDamage(MapId, camp, damageable.MaxHP - 1f);
				Check.Assert(!damageable.IsCapturable, "差 1 点血时还不算可夺取");
				Check.Assert(!h.Map.IsClear(MapId, cell), "还有血 ⇒ 仍不可进入");

				h.Map.ApplyBuildingDamage(MapId, camp, 5f);
				Check.Assert(damageable.IsCapturable, "归零 ⇒ 可夺取");
				Check.Assert(h.Map.IsClear(MapId, cell), "可夺取 ⇒ 格子变成可进入");

				// 建筑**仍在图上**（仍是原主人的资产）：按 uid 仍能查到、格子的 Building 槽位还在
				Check.Assert(h.Map.FindOccupantByUId(MapId, camp.GetInfo().UId) != null, "可夺取的建筑仍在占据物索引里");
				Check.Assert(h.Map.GetMapCell(MapId, cell)?.Building != null, "格子的 Building 槽位仍是它");
				Check.Assert(h.Map.GetOccupants(MapId).Any(o => o.GetInfo().UId == camp.GetInfo().UId),
					"`GetOccupants` 必须计入可夺取的建筑（否则胜负会误判、维护费也会消失）");
			}
			finally { h.Dispose(); }
		}

		private static void UnitCapturesBuilding()
		{
			Harness h = NewHarness();
			try
			{
				var cell = new HexCubePosition(3, 3);
				IMapOccupant camp = h.PlaceBuilding("camp", 2, cell); // AI 的营地（配置里带一条 FoodGrowth +7）
				string uid = camp.GetInfo().UId;

				// 模拟"这栋建筑已经完工"：完工回调会把配置里的修正器挂到owner名下（这里手动做同一件事）
				h.Modifier.AddModifiers(MapId, 2, uid, h.Core.Tables.Buildings.GetBuildingConfig("camp").Modifiers);
				Check.AssertEqual(7f, h.Modifier.GetValue(MapId, 2, "FoodGrowth", 0f), "准备：AI 名下有该建筑的加成");

				h.Map.ApplyBuildingDamage(MapId, camp, ((IDamageable)camp).MaxHP + 10f);
				Check.Assert(((IDamageable)camp).IsCapturable, "准备：已转为可夺取");

				Unit worker = h.PlaceUnit("worker", 1, new HexCubePosition(2, 3));
				Check.Assert(h.Map.MoveOccupant(MapId, worker, new HexCubePosition(2, 3), cell), "应能走进可夺取的格子");

				Check.AssertEqual(1, camp.GetInfo().OwnerId, "易主后归属 = 玩家");
				Check.AssertEqual(((IDamageable)camp).MaxHP / 2f, ((IDamageable)camp).HP, "易主后 HP = 半血");
				Check.AssertEqual(0f, h.Modifier.GetValue(MapId, 2, "FoodGrowth", 0f), "原主人名下的加成应被摘掉");
				Check.AssertEqual(7f, h.Modifier.GetValue(MapId, 1, "FoodGrowth", 0f), "新主人名下应挂上同一加成（修正器移交）");
			}
			finally { h.Dispose(); }
		}

		private static void BuildingBlocksMovementUntilZero()
		{
			Harness h = NewHarness();
			try
			{
				var cell = new HexCubePosition(3, 3);
				h.PlaceBuilding("camp", 2, cell);
				var from = new HexCubePosition(2, 3);
				Unit worker = h.PlaceUnit("worker", 1, from);

				Check.Assert(!h.Map.MoveOccupant(MapId, worker, from, cell),
					"HP>0 的敌方建筑格不可进入（必须先打）");
			}
			finally { h.Dispose(); }
		}

		private static void BuildingLossTriggersVictory()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(20260929, 24, 24, MapId);
			core.Orchestrator.StartMap(MapId);

			RemoveAllOwnedType(core, 2, OccupantType.Unit); // AI 只留建筑

			var cell = new HexCubePosition(6, 6);
			core.Map.SetTerrain(MapId, cell, core.Tables.Terrains.GetById("plain"));
			core.Map.PlaceBuilding(MapId, cell, new BuildingFactory(core.Tables.Buildings).CreateBuilding("camp", cell, 2));

			core.Victory.Evaluate(MapId, "Manual");
			Check.Assert(core.Victory.IsAlive(MapId, 2), "AI 有营地 ⇒ 还活着");

			core.Map.RemoveBuilding(MapId, cell); // 被拆 ⇒ 推 `BuildingRemovedEvent` ⇒ 胜负重算
			Check.Assert(!core.Victory.IsAlive(MapId, 2), "唯一资产消失 ⇒ AI 出局（建筑侧推送也要触发重算）");
			Check.AssertEqual(1, core.Victory.OutcomeOf(MapId).WinnerOwnerId ?? -1, "胜者 = 人类");
		}

		// ────────────── 夹具 ──────────────

		private sealed class Harness : IDisposable
		{
			public string Dir;
			public CoreServices Core;
			public MapAppService Map;
			public ModifierAppService Modifier;
			public UnitsAppService Units;
			public ConstructionAppService Construction;

			public IMapOccupant PlaceBuilding(string buildingId, int ownerId, HexCubePosition position)
			{
				Map.SetTerrain(MapId, position, Core.Tables.Terrains.GetById("plain"));
				IMapOccupant building = new BuildingFactory(Core.Tables.Buildings).CreateBuilding(buildingId, position, ownerId);
				building.IsReady = true;
				Check.Assert(Map.PlaceBuilding(MapId, position, building), "建筑应能落位");
				return building;
			}

			public Unit PlaceUnit(string unitId, int ownerId, HexCubePosition position)
			{
				string uid = Units.PlaceInitialUnit(MapId, unitId, position, ownerId);
				Check.Assert(uid != null, "单位应能落位（格子空 + 地形可通行）");
				return (Unit)Map.FindOccupantByUId(MapId, uid);
			}

			public void Dispose()
			{
				try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { /* 清理失败不影响结论 */ }
			}
		}

		/// <summary>生产装配的等价夹具（含总线：夺取推送由 `MapAppService` 发出、由 `ConstructionAppService` 消费）。</summary>
		private static Harness NewHarness()
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp48-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			// 给营地配一条修正器（`camp` 真实配置里 `Modifiers` 为空）：这样才能验"夺取后按新主人重挂"
			var buildings = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			buildings["Buildings"]["camp"]["Modifiers"] = new Newtonsoft.Json.Linq.JArray
			{
				new Newtonsoft.Json.Linq.JObject
				{
					["Target"] = "FoodGrowth",
					["Type"] = "Absolute",
					["Value"] = 7,
				},
			};

			CoreServices core = ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith("Buildings", buildings.ToString()));
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;

			var store = new JsonSaveStore(new SystemFileSystem(), Path.Combine(dir, "world.save"));
			var tasks = new TaskRepository(Path.Combine(dir, "tasks_"), store);
			var time = new GameTimeService(core.Session.Clock, tasks);
			var bus = new DomainEventBus();

			// 单个修正器仓储实例贯穿所有服务（**不是**为了修 bug：`ModifierRepository` 每次调用都会重读分区，
			// 多实例不会读错；统一成一个只是让"谁持有仓储"更清楚，将来换实现/加缓存时少一个坑）
			var modifierRepo = new ModifierRepository(Path.Combine(dir, "mod_"), store);
			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_"), store),
				core.Tables.Resources, time, modifierRepo, bus);
			var modifier = new ModifierAppService(modifierRepo);
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees, store),
				core.Tables.TechTrees, resources, modifier, time, bus);
			var fog = new FogAppService(1, new FogRepository(Path.Combine(dir, "fog_"), store));

			// ⚠️ 地图服务必须拿到总线：**夺取的推送由它发出**（`BuildingCapturedEvent`）
			MapAppService map = new MapAppService(
				core.MapGenerator, core.Session.Maps, null, null, bus);
			var construction = new ConstructionAppService(
				map, resources, tech, new BuildingFactory(core.Tables.Buildings),
				core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus);

			map.GenerateMap(20260930, 8, 8, MapId);
			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in map.GetAllCells(MapId).ToList())
				map.SetTerrain(MapId, cell.Position, plain);

			return new Harness
			{
				Dir = dir,
				Core = core,
				Map = map,
				Modifier = modifier,
				Units = units,
				Construction = construction,
			};
		}

		private static void RemoveAllOwnedType(CoreServices core, int ownerId, OccupantType type)
		{
			foreach (IMapOccupant occupant in core.Map.GetOccupants(MapId).ToList())
			{
				MapOccupantInfo info = occupant.GetInfo();
				if (info.OwnerId != ownerId || info.Type != type) continue;
				core.Map.RemoveOccupantByPosition(MapId, info.Position, occupant);
			}
		}
	}
}
