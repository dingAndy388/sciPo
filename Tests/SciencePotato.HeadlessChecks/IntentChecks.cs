using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Intent.Application;
using SciencePotato.Scripts.Intent.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Linq;
using System.Reflection;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.9.4 / `WP-5.7`）**意图契约**（`I2`、`CON-02`/`CON-08` 收口）的验收检查。
	/// <para>两条主张：① 表现层只需要 `IActionHandler`（`PlayerIntent` 进 / `IntentResult` 出），
	/// "谁能做 · 门控 · 前置 · 资源 · 地形"这些规则全在处理器里判；② 每个拒绝都给 **i18n 键**，且键都有文案。</para>
	/// </summary>
	internal static class IntentChecks
	{
		private const string MapId = "intent-map";

		public static void RunAll()
		{
			Check.Run("WP-5.7 接线：`CoreServices.Intent` 可用（表现层唯一入口）", HandlerIsWired);
			Check.Run("WP-5.7 建造意图：格位空闲 → 开工；同格再建 → 预检与执行都拒绝", BuildIntentStartsConstruction);
			Check.Run("WP-5.7 研究门控：根节点（计数）可直接研究；非根节点先给 `UiLocked`，解锁后放行", ResearchRespectsUiGate);
			Check.Run("WP-5.7 研究前置：前置未满足给 `Prereq`；满足后真的能研究完成", ResearchRespectsPrerequisites);
			Check.Run("WP-5.7 越权：不能给 AI 势力下意图，也不能指挥 AI 的单位（`NotHuman`）", IntentsCannotCommandAi);
			Check.Run("WP-5.7 移动意图：自己的工人可派往可通行格（真进入移动态）；缺目标给 `NoTarget`", MoveIntentMovesOwnUnit);
			Check.Run("WP-5.7 坏意图：null / 未知种类 / 不存在的事件 都不抛异常，且都带键", MalformedIntentsAreRejected);
			Check.Run("WP-5.7 i18n：`IntentKeys` 的每个键都有文案（装载后 0 缺键）", EveryKeyHasText);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void HandlerIsWired()
		{
			(CoreServices core, _) = NewField();

			Check.Assert(core.Intent != null, "组合根应装配 `IActionHandler`（表现层只有它）");
			Check.Assert(core.Intent.CanRequest(PlayerIntent.Research(MapId, 1, "math", "counting"), out _),
				"根节点「计数」的研究意图应通过只读预检");
			Check.Assert(!core.Intent.CanRequest(null, out string reason), "null 意图应被拒");
			Check.AssertEqual(IntentKeys.NoIntent, reason, "null 意图的原因键");
		}

		private static void BuildIntentStartsConstruction()
		{
			(CoreServices core, MapAppService map) = NewField();
			core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(4, 4), 1);
			core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(4, 5), 1);

			var site = new HexCubePosition(6, 6);
			IntentResult first = core.Intent.Handle(PlayerIntent.Build(MapId, 1, "camp", site));

			Check.Assert(first.Ok, $"应在空格开工建造：{first}");
			Check.AssertEqual("camp", map.GetOccupantInfo(MapId, site).Value.Id, "格位上应出现营地（施工中）");

			Check.Assert(!core.Intent.CanRequest(PlayerIntent.Build(MapId, 1, "camp", site), out string takenReason),
				"同一格位已被占用 → 预检应拒绝（UI 可以直接灰化按钮）");
			Check.AssertEqual(IntentKeys.Rejected, takenReason, "占用格位的拒绝键");

			IntentResult again = core.Intent.Handle(PlayerIntent.Build(MapId, 1, "camp", site));
			Check.Assert(!again.Ok, "重复建造应被规则层拒绝");
			Check.AssertEqual(IntentKeys.Rejected, again.MessageKey, "重复建造的拒绝键");
		}
		private static void ResearchRespectsUiGate()
		{
			(CoreServices core, _) = NewField();

			// 根节点是"研究功能的入口"：面板还没解锁时也能点（否则第一条科技永远进不去）
			IntentResult root = core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "counting"));
			Check.Assert(root.Ok, $"根节点「计数」应能开工：{root}");
			core.Session.Clock.AdvanceDays(1);
			Check.Assert(core.Tech.GetOrCreateTechTree(MapId, 1, "math").IsResearched("counting"), "计数应在 1 日内完成");

			// 计数解锁了研究面板 → 非根节点放行；这条同时锁住 `WP-4.14` 的接线
			IntentResult nonRoot = core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "arithmetic"));
			Check.Assert(nonRoot.Ok, $"计数研究后「算术」应可研究（面板已解锁）：{nonRoot}");

			// 对照：未研究计数时，非根节点给 `UiLocked`
			(CoreServices locked, _) = NewField(seed: 20261501);
			IntentResult blocked = locked.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "arithmetic"));
			Check.Assert(!blocked.Ok, "研究面板未解锁时不应放行非根节点");
			Check.AssertEqual(IntentKeys.UiLocked, blocked.MessageKey, "面板未解锁的拒绝键应可翻译（`R4`）");
		}

		private static void ResearchRespectsPrerequisites()
		{
			(CoreServices core, _) = NewField();

			core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "counting"));
			core.Session.Clock.AdvanceDays(1);

			// 面板已解锁，但「毕达哥拉斯学派」的前置（记数系统 + 初步测量）还没研究
			IntentResult blocked = core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "pythagorean_school"));
			Check.Assert(!blocked.Ok, "前置未满足时不得开工");
			Check.AssertEqual(IntentKeys.Prerequisite, blocked.MessageKey, "前置未满足的拒绝键");

			// 沿真实前置链补齐后应能研究完成（意图层不绕过规则，也不额外加限制）
			foreach (string node in new[] { "arithmetic", "measurement", "basic_geometry", "numeral_system", "preliminary_survey" })
			{
				core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", node));
				core.Session.Clock.AdvanceDays(200);
			}
			IntentResult ok = core.Intent.Handle(PlayerIntent.Research(MapId, 1, "math", "pythagorean_school"));
			Check.Assert(ok.Ok, $"前置齐备后应能开工：{ok}");
			core.Session.Clock.AdvanceDays(200);
			Check.Assert(core.Tech.GetOrCreateTechTree(MapId, 1, "math").IsResearched("pythagorean_school"), "应真正研究完成");
		}

		private static void IntentsCannotCommandAi()
		{
			(CoreServices core, _) = NewField();
			string aiWorker = core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(8, 8), 2);

			IntentResult aiBuild = core.Intent.Handle(PlayerIntent.Build(MapId, 2, "camp", new HexCubePosition(9, 9)));
			Check.Assert(!aiBuild.Ok, "不能给 AI 势力下建造意图（UI 只服务人的手）");
			Check.AssertEqual(IntentKeys.NotHuman, aiBuild.MessageKey, "越权（势力）的拒绝键");

			IntentResult aiMove = core.Intent.Handle(PlayerIntent.Move(MapId, 1, aiWorker, new HexCubePosition(8, 9)));
			Check.Assert(!aiMove.Ok, "不能移动 AI 的单位");
			Check.AssertEqual(IntentKeys.NotHuman, aiMove.MessageKey, "越权（单位）的拒绝键");
		}

		private static void MoveIntentMovesOwnUnit()
		{
			(CoreServices core, MapAppService map) = NewField();
			string uid = core.Units.PlaceInitialUnit(MapId, "worker", new HexCubePosition(4, 4), 1);

			IntentResult ok = core.Intent.Handle(PlayerIntent.Move(MapId, 1, uid, new HexCubePosition(5, 4)));
			Check.Assert(ok.Ok, $"自己的工人应可派往相邻平原：{ok}");

			var unit = (Unit)map.FindOccupantByUId(MapId, uid);
			Check.Assert(UnitMovementService.IsMoving(unit), "单位应进入移动态（目标格 ≠ 当前格）");

			// 缺目标：`HasTarget=false` 的裸意图
			var noTarget = new PlayerIntent { Kind = IntentKind.MoveUnit, MapId = MapId, OwnerId = 1, SourceUid = uid };
			IntentResult rejected = core.Intent.Handle(noTarget);
			Check.Assert(!rejected.Ok, "缺格位目标的移动意图应被拒");
			Check.AssertEqual(IntentKeys.NoTarget, rejected.MessageKey, "缺目标的拒绝键");
		}

		private static void MalformedIntentsAreRejected()
		{
			(CoreServices core, _) = NewField();

			Check.Assert(!core.Intent.Handle(null).Ok, "null 意图应被拒（而不是抛异常）");

			var unknown = new PlayerIntent { Kind = (IntentKind)99, MapId = MapId, OwnerId = 1 };
			IntentResult unknownResult = core.Intent.Handle(unknown);
			Check.Assert(!unknownResult.Ok, "未知意图种类应被拒");
			Check.AssertEqual(IntentKeys.UnknownKind, unknownResult.MessageKey, "未知种类的拒绝键");

			IntentResult badEvent = core.Intent.Handle(PlayerIntent.DecideEvent(MapId, 1, "no_such_event"));
			Check.Assert(!badEvent.Ok, "不存在的事件决策应被拒");
			Check.AssertEqual(IntentKeys.Rejected, badEvent.MessageKey, "事件决策失败的拒绝键");

			IntentResult noMap = core.Intent.Handle(PlayerIntent.Research("", 1, "math", "counting"));
			Check.Assert(!noMap.Ok, "缺地图的意图应被拒");
			Check.AssertEqual(IntentKeys.NoMap, noMap.MessageKey, "缺地图的拒绝键");
		}

		private static void EveryKeyHasText()
		{
			(CoreServices core, _) = NewField();

			FieldInfo[] keys = typeof(IntentKeys).GetFields(BindingFlags.Public | BindingFlags.Static);
			Check.Assert(keys.Length > 0, "`IntentKeys` 应有常量键（否则本用例失去意义）");
			foreach (FieldInfo field in keys)
			{
				string key = (string)field.GetValue(null);
				Check.Assert(core.I18n.HasKey(key), $"键 `{key}` 必须有文案（zh）");
				Check.Assert(core.I18n.HasKey(key) == false || core.I18n.T(key) != key, $"键 `{key}` 的文案不应回退成键本身");
			}
			Check.AssertEqual(0, core.I18n.MissingKeys.Count, $"装载后不应有缺键（实际 {core.I18n.MissingKeys.Count} 个）");
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private static (CoreServices Core, MapAppService Map) NewField(int seed = 20261500)
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			core.Map.GenerateMap(seed, 20, 20, MapId);

			ITerrainData plain = core.Tables.Terrains.GetById("plain");
			foreach (MapCell cell in core.Map.GetAllCells(MapId).ToList())
				core.Map.SetTerrain(MapId, cell.Position, plain);

			foreach (int owner in new[] { 1, 2 })
			{
				core.Resources.AddResource("Idea", 50000f, MapId, owner);
				core.Resources.AddResource("BasicMinerals", 50000f, MapId, owner);
				core.Resources.AddResource("Food", 50000f, MapId, owner);
			}
			return (core, core.Map);
		}
	}
}
