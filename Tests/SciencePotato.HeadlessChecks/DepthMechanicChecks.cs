using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.8.7 / B2）**深度机制**的验收检查：`WP-4.7` 单位合并 · `WP-4.3` 单位标签 + 条件化修正 ·
	/// `WP-4.6` 驻扎 · `WP-4.2` 范围效果。全部断言"用户可见行为"，不只断言"函数返回 true"。
	/// </summary>
	internal static class DepthMechanicChecks
	{
		private const string MapId = "depth-mechanics";

		public static void RunAll()
		{
			Check.Run("WP-4.7 单位合并：同模板同格 ⇒ HP 相加（上限 MaxHP）；异模板/异格被拒", MergeUnitsRules);
			Check.Run("WP-4.3 标签与能力：15 个单位都有标签；(DamageVs{标签}/DamageTaken) 按目标结算", TagsAndConditionalDamage);
			Check.Run("WP-4.6 驻扎：学者挨着学院 ⇒ Idea +10%；走开/拆宿主即失效", GarrisonNeedsHostAdjacent);
			Check.Run("WP-4.2 范围：骨笛工坊覆盖 1 格内生产建筑 +15%；2 格外不生效", RangeEffectCoversOnlyNearby);
			Check.Run("WP-4.2 相邻同类：两块挨着的农田 ⇒ 加成只算一次（科技 `AdjacentSameTypeBonus`）", AdjacentSameTypeBonus);
		}

		private static (CoreServices Core, MapAppService Map) NewField(int seed = 20261200)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(seed, 20, 20, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			// 产出浮动钉成 0：本组要断言"范围/驻扎"的精确倍率，不能让 ±20% 抖动干扰（`WP-4.5` 的上下限改写正好可复用）
			core.Modifiers.AddModifiers(MapId, 1, "pin_variance", new List<Modifier>
			{
				new Modifier { Target = "OutputVarianceUpper", Type = "Absolute", Value = 0f },
				new Modifier { Target = "OutputVarianceLower", Type = "Absolute", Value = 0f },
			});
			return (core, core.Map);
		}

		private static Unit PlaceUnit(CoreServices core, string unitId, int ownerId, HexCubePosition position)
		{
			string uid = core.Units.PlaceInitialUnit(MapId, unitId, position, ownerId);
			Check.Assert(uid != null, $"{unitId}@{position} 应能落位");
			return (Unit)core.Map.FindOccupantByUId(MapId, uid);
		}

		private static Building PlaceBuilding(CoreServices core, string buildingId, int ownerId, HexCubePosition position, bool ready = true)
		{
			Building building = new BuildingFactory(core.Tables.Buildings).CreateBuilding(buildingId, position, ownerId);
			building.IsReady = ready;
			Check.Assert(core.Map.PlaceBuilding(MapId, position, building), $"{buildingId}@{position} 应能落位");
			return building;
		}

		private static void MergeUnitsRules()
		{
			(CoreServices core, MapAppService map) = NewField();
			Unit a = PlaceUnit(core, "swordsman", 1, new HexCubePosition(5, 5));
			a.HP = 40f;                                   // 100 上限，先打成 40

			// ② 不同模板 / 不同格 / 自己并自己 ⇒ 一律拒绝
			Unit other = PlaceUnit(core, "explorer", 1, new HexCubePosition(4, 5));
			Check.Assert(!core.Units.MergeUnits(MapId, a.GetInfo().UId, other.GetInfo().UId), "不同模板不得合并");

			Unit far = PlaceUnit(core, "swordsman", 1, new HexCubePosition(8, 8));
			Check.Assert(!core.Units.MergeUnits(MapId, a.GetInfo().UId, far.GetInfo().UId), "不同格不得合并");
			Check.Assert(!core.Units.MergeUnits(MapId, a.GetInfo().UId, a.GetInfo().UId), "自己并自己应被拒");

			// ① 正常合并：同格同模板 ⇒ HP 相加（上限 MaxHP）
			Unit second = PlaceUnit(core, "swordsman", 1, new HexCubePosition(6, 5));
			second.HP = 90f;
			int before = map.GetOccupants(MapId).Count(o => o.GetInfo().OwnerId == 1 && o.GetInfo().Type == OccupantType.Unit);

			Check.Assert(core.Units.MergeUnits(MapId, a.GetInfo().UId, second.GetInfo().UId), "同模板同格应能合并");
			Check.AssertEqual(100f, a.HP, "HP 相加后应被 MaxHP 截断（40+90 → 100）");
			Check.AssertEqual(before - 1, map.GetOccupants(MapId).Count(o => o.GetInfo().OwnerId == 1 && o.GetInfo().Type == OccupantType.Unit),
				"合并后被并方应从图上消失");
			Check.Assert(map.FindOccupantByUId(MapId, second.GetInfo().UId) == null, "被并方不应再被 uid 查到");
			Check.AssertEqual(1, core.Units.MergeCount, "合并次数应记账");

			// 未触顶时应是精确相加
			Unit third = PlaceUnit(core, "spearman", 1, new HexCubePosition(10, 10));
			third.HP = 10f;
			Unit fourth = PlaceUnit(core, "spearman", 1, new HexCubePosition(11, 10));
			fourth.HP = 20f;
			Check.Assert(core.Units.MergeUnits(MapId, third.GetInfo().UId, fourth.GetInfo().UId), "第二个模板也应能合并");
			Check.AssertEqual(30f, third.HP, "未触顶时应是精确相加（10+20）");
		}

		private static void TagsAndConditionalDamage()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();

			foreach (IUnitConfig unit in core.Tables.AllUnits())
				Check.Assert(unit.Tags != null && unit.Tags.Count > 0, $"单位 {unit.UnitId} 应有标签（条件化修正的判定依据）");

			Check.Assert(core.Tables.Units.GetUnitConfig("swordsman").Tags.Contains("melee"), "剑士应带 melee");
			Check.Assert(core.Tables.Units.GetUnitConfig("archer").Tags.Contains("ranged"), "弓箭手应带 ranged");
			Check.Assert(core.Tables.Units.GetUnitConfig("wolf").Tags.Contains("beast"), "野狼应带 beast");

			(CoreServices field, _) = NewField(20261201);
			Unit spearman = PlaceUnit(field, "spearman", 1, new HexCubePosition(5, 5));
			float vsMelee = field.Units.Combat.ComputeDamage(MapId, spearman, OccupantType.Unit, field.Tables.Units.GetUnitConfig("swordsman"));
			float vsRanged = field.Units.Combat.ComputeDamage(MapId, spearman, OccupantType.Unit, field.Tables.Units.GetUnitConfig("archer"));
			Check.AssertEqual(vsRanged * 1.2f, vsMelee, "`DamageVsMelee +20%`：对近战目标应高 20%");

			Unit ballista = PlaceUnit(field, "ballista", 1, new HexCubePosition(7, 7));
			float vsBuilding = field.Units.Combat.ComputeDamage(MapId, ballista, OccupantType.Building, null);
			Check.AssertEqual(18.75f, vsBuilding, "`DamageVsBuilding +50%`：弩炮对建筑 25×0.5×1.5 = 18.75");

			Unit guard = PlaceUnit(field, "heavy_guard", 1, new HexCubePosition(9, 9));
			Check.AssertEqual(75f, field.Units.Combat.ApplyAbility(guard, "DamageTaken", 100f), "`DamageTaken -25%`：100 → 75");
			Check.AssertEqual(100f, field.Units.Combat.ApplyAbility(spearman, "DamageTaken", 100f), "没有该能力的单位不受影响");
		}
		private static void GarrisonNeedsHostAdjacent()
		{
			(CoreServices core, _) = NewField(20261202);
			Building school = PlaceBuilding(core, "school", 1, new HexCubePosition(5, 5));
			core.Modifiers.AddModifiers(MapId, 1, school.GetInfo().UId, core.Tables.Buildings.GetBuildingConfig("school").Modifiers);
			float plain = core.Settlement.Settle(MapId, 1).ProductionOf("Idea");

			Unit scholar = PlaceUnit(core, "scholar", 1, new HexCubePosition(6, 5)); // 与学院相邻
			float garrisoned = core.Settlement.Settle(MapId, 1).ProductionOf("Idea");
			Check.Assert(plain > 0f, $"准备：学院基准 Idea 产出应 > 0（实际 {plain}）");
			Check.AssertEqual(plain * 1.1f, garrisoned, "学者驻扎学院 ⇒ Idea 产出 +10%");

			// 走开（不在一格内）⇒ 失效
			// `Map.MoveOccupant` 是**格级** API（只挪格子槽位）；游戏内移动由 `UnitMovementService` 负责把
						// `Unit.Position` 同步过来（`D113` 记录了这个不一致）。测试用格级 API 走位，需自己同步字段。
			Check.Assert(core.Map.MoveOccupant(MapId, scholar, new HexCubePosition(6, 5), new HexCubePosition(12, 12)), "准备：学者应能走开");
			scholar.Position = new HexCubePosition(12, 12);
			Check.AssertEqual(plain, core.Settlement.Settle(MapId, 1).ProductionOf("Idea"), "学者走开后驻扎加成应失效");

			// 回到学院旁 ⇒ 恢复；拆掉宿主 ⇒ 再失效（无残留状态）
			Check.Assert(core.Map.MoveOccupant(MapId, scholar, new HexCubePosition(12, 12), new HexCubePosition(6, 5)), "准备：学者应能走回来");
			scholar.Position = new HexCubePosition(6, 5);
			Check.AssertEqual(garrisoned, core.Settlement.Settle(MapId, 1).ProductionOf("Idea"), "回到学院旁应恢复加成");
			core.Map.RemoveBuilding(MapId, new HexCubePosition(5, 5));
			Check.AssertEqual(plain, core.Settlement.Settle(MapId, 1).ProductionOf("Idea"), "宿主消失后驻扎加成应失效");
		}

		private static void RangeEffectCoversOnlyNearby()
		{
			(CoreServices core, _) = NewField(20261203);
			Building farm = PlaceBuilding(core, "farm", 1, new HexCubePosition(5, 5));
			core.Modifiers.AddModifiers(MapId, 1, farm.GetInfo().UId, core.Tables.Buildings.GetBuildingConfig("farm").Modifiers);
			float plain = core.Settlement.Settle(MapId, 1).ProductionOf("Food");
			Check.AssertEqual(12f, plain, "准备：一块农田 = 12/月（浮动已被钉成 0）");

			// 骨笛工坊（范围 1）紧邻农田 ⇒ 农田 +15%
			PlaceBuilding(core, "bone_flute_workshop", 1, new HexCubePosition(6, 5));
			Check.AssertEqual(plain * 1.15f, core.Settlement.Settle(MapId, 1).ProductionOf("Food"), "骨笛工坊覆盖 1 格内的农田 ⇒ +15%");

			// 挪到 2 格外 ⇒ 不覆盖
			core.Map.RemoveBuilding(MapId, new HexCubePosition(6, 5));
			PlaceBuilding(core, "bone_flute_workshop", 1, new HexCubePosition(7, 5));
			Check.AssertEqual(plain, core.Settlement.Settle(MapId, 1).ProductionOf("Food"), "2 格外的工坊不应再覆盖这块农田");
		}

		private static void AdjacentSameTypeBonus()
		{
			(CoreServices core, _) = NewField(20261204);
			Building farm = PlaceBuilding(core, "farm", 1, new HexCubePosition(5, 5));
			core.Modifiers.AddModifiers(MapId, 1, farm.GetInfo().UId, core.Tables.Buildings.GetBuildingConfig("farm").Modifiers);

			// 科技"振动与波"：相邻同类型建筑产出 +5%
			core.Modifiers.AddModifiers(MapId, 1, "physics_tech", new List<Modifier>
			{
				new Modifier { Target = "AdjacentSameTypeBonus", Type = "Percent", Value = 0.05f },
			}, ModifierStage.Tech);

			Check.AssertEqual(12f, core.Settlement.Settle(MapId, 1).ProductionOf("Food"), "孤零零一块农田 ⇒ 相邻同类加成为 0");

			Building twin = PlaceBuilding(core, "farm", 1, new HexCubePosition(6, 5));
			core.Modifiers.AddModifiers(MapId, 1, twin.GetInfo().UId, core.Tables.Buildings.GetBuildingConfig("farm").Modifiers);
			Check.AssertEqual(24f * 1.05f, core.Settlement.Settle(MapId, 1).ProductionOf("Food"),
				"两块相邻农田 ⇒ 加成只算一次（24 × 1.05），不是每块各算一次");
		}
	}
}
