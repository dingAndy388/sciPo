using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using SciencePotato.Scripts.Units.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-3.6 / `E9`~`E12`、`E18`、`E19`、`UNIT-12`）**同格战斗**的验收检查 ——
	/// M0-3 ③ 的门槛是「同格交战 N 日后 HP 归零、单位移除、掉落进池」；掉落（`E20`）归 `WP-3.7`，
	/// 本组锁住前两半与四条派生口径：
	/// <list type="number">
	/// <item>**进入敌格即交战**（`E9`）：近战"攻进去"、一格一对、胜利者占据该格；</item>
	/// <item>**按日结算**（`E10`）：`AttackDamage` = 每日伤害，双方各一条循环、互相掉血；</item>
	/// <item>**射程**（`E11`）：0 = 同格（近战）、≥1 = 隔格；近战敌人打不到隔格的远程；</item>
	/// <item>**对建筑衰减 50%**（`E12`）与**建筑作为目标**（`E18`）：`0.5 × 1.5 = 0.75`；</item>
	/// <item>**敌方反应**（`E19`）：进入敌方射程即受击（"遇敌即停"的过渡行为已删除）；</item>
	/// <item>**交战状态随存档保留**（`MapSave` v3）与**读档后战斗继续**（循环重建）。</item>
	/// </list>
	/// <para>夹具 = 真实配置（单位时长缩短到 2~3 日）+ 平原沙盘 + 手动摆放敌我单位：
	/// 刷新器关掉（避免随机敌人抢占用例的格位），敌方单位走 `EnemySpawner.Spawn` 的生产路径落地。</para>
	/// </summary>
	internal static class CombatChecks
	{
		private const string MapId = "combat-map";

		private const int SmallMap = 10;

		public static void RunAll()
		{
			Check.Run("WP-3.6 伤害口径：对建筑 ×0.5、加伤乘其后（`E12` 的 0.5×1.5=0.75）", DamageFormulaMatchesDesign);
			Check.Run("M0-3 ③ 近战攻进敌格即交战：同格一对、第二人进不来、攻击不消耗 MP（`E9`）", BreakIntoEnemyCell);
			Check.Run("M0-3 ③ 同格交战按日结算：双方互相掉血，N 日后一方 HP 归零被移除（`E9`/`E10`）", SameCellExchangeIsMutual);
			Check.Run("M0-3 ③ 击杀后胜者占据该格并解除封锁（`E9`，掉落归 `WP-3.7`）", WinnerTakesTheCell);
			Check.Run("WP-3.6 远程隔格开火：不共格，且近战敌人打不到它（`E11`）", RangedAttacksWithoutCoLocation);
			Check.Run("WP-3.6 敌方反应：进入其射程即受击、离开射程循环自行注销（`E19`）", HostileReactsToRangeEntry);
			Check.Run("WP-3.6 建筑作为目标：可瞄准、伤害 ×0.5、无友伤（`E18`）", BuildingIsAttackableWithHalfDamage);
			Check.Run("WP-3.6 交战状态随存档保留：`MapSave` v3 的 `Invader` 槽位 + 单位 `MaxHP`", EngagementSurvivesSaveLoad);
			Check.Run("WP-3.6 读档恢复交战循环：双向各一条，敌方反击不会因读档消失", LoopsRestoreAfterReload);
			Check.Run("WP-3.6 无伤害单位保持封锁，战斗单位不算空闲（`B8` + 建造门控）", NonCombatantsStayBlocked);
			Check.Run("WP-3.6 配置校验：`CanAttack` 与 `AttackDamage` 的口径自检（新增 2 条 warning）", ConfigRulesForCombat);
		}

		// ────────────────────────── 用例：交战模型 ──────────────────────────

		/// <summary>`E12` 的公式本身就是设计稿的验收口径，因此直接断言纯函数（不依赖地图与修正器装配）。</summary>
		private static void DamageFormulaMatchesDesign()
		{
			Check.AssertEqual(1f, CombatRules.TargetFactor(OccupantType.Unit), "单位目标不衰减（系数 1）");
			Check.AssertEqual(0.5f, CombatRules.TargetFactor(OccupantType.Building), "对建筑衰减 50%（`E12`）");

			// 设计稿原文："对建筑伤害衰减 50%，加伤乘其后（0.5×1.5=0.75）"
			Check.AssertEqual(0.75f, CombatRules.Damage(1f, OccupantType.Building, 0.5f), "衰减在前、加伤乘其后（0.5×1.5）");
			Check.AssertEqual(1.5f, CombatRules.Damage(1f, OccupantType.Unit, 0.5f), "单位目标：加伤直接生效");
			Check.AssertEqual(4.5f, CombatRules.Damage(6f, OccupantType.Building, 0.5f), "弓箭手 6 打建筑 +50% → 4.5（= 6×0.5×1.5）");
			Check.AssertEqual(18.75f, CombatRules.Damage(25f, OccupantType.Building, 0f, 0.5f), "弩炮 25 对建筑 +50% → 18.75");

			// `E11`：射程判据（同格恒可、远程看半径）
			Unit none = null;
			Check.Assert(!CombatRules.CanReach(none, new HexCubePosition(0, 0)), "空攻击者恒为 false");
		}

		/// <summary>`E9`：近战把"封锁格"变成"交战入口"——攻进去、同格一对、第二人进不来。</summary>
		private static void BreakIntoEnemyCell()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);

				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));
				float mpBefore = swordsman.CurrentMP;

				Check.Assert(!h.Map.IsClear(MapId, enemyCell), "准备：敌方格被占据");
				Check.Assert(!h.Movement.CanEnter(MapId, enemyCell), "准备：普通位移进不去敌方格（`B8`）");

				Check.Assert(h.Units.ExcuteAction(MapId, swordsman.GetInfo().UId, enemyCell, wolf.GetInfo().UId, "CanAttack"),
					"近战单位在相邻格下达攻击指令应被接受");

				// ① 进入敌格即交战（`E9`）：双方同格，占据物槽位 = 被挑战方、`Invader` 槽位 = 进攻方
				Check.AssertEqual(enemyCell, swordsman.Position, "近战攻击应让单位**攻进**目标格（同格交战）");
				Check.AssertEqual(enemyCell, wolf.Position, "被挑战方仍在原格");
				Check.Assert(h.Map.IsEngagedAt(MapId, enemyCell), "该格应处于交战状态（一格一对）");

				MapCell cell = h.MapOf(MapId).GetCell(enemyCell);
				Check.AssertEqual(wolf.GetInfo().UId, cell.Occupant.GetInfo().UId, "占据物槽位仍是被挑战方");
				Check.AssertEqual(swordsman.GetInfo().UId, cell.Invader.GetInfo().UId, "进攻方落在 `Invader` 槽位（`D56` 预留给这一对）");

				// ② 敌人还活着 → 该格对外仍然封锁（"必须先击败它才能进入"）
				Check.Assert(!h.Map.IsClear(MapId, enemyCell), "交战中该格不是空地");
				Check.Assert(!h.Movement.CanEnter(MapId, enemyCell), "敌人未死 → 普通位移仍不可进入");

				// ③ 攻击不消耗 MP（design 口径）；战斗中不算空闲（否则建造/驻扎门控会把它当闲人）
				Check.AssertEqual(mpBefore, swordsman.CurrentMP, "攻进敌格不消耗 MP（攻击不是移动）");
				Check.Assert(!swordsman.IsIdle, "交战中不算空闲");

				// ④ 一格一对：第二个单位打不进同一格
				Unit second = h.SpawnFor(1, "swordsman", new HexCubePosition(4, 4));
				Check.Assert(!h.Units.ExcuteAction(MapId, second.GetInfo().UId, enemyCell, wolf.GetInfo().UId, "CanAttack"),
					"该格已有一对交战单位 → 第二个单位的攻击应被拒绝");
				Check.AssertEqual(new HexCubePosition(4, 4), second.Position, "被拒的攻击不得产生位移");
				Check.AssertEqual(swordsman.GetInfo().UId, cell.Invader.GetInfo().UId, "原本的进攻方不受影响");

				// ⑤ 交战单位的维护费照算（它还在图上吃粮）：`GetOccupants` 必须计入 `Invader` 槽位
				int swordsmen = (int)new UnitMaintenanceUpkeepDemandSource(h.Map, h.Tables.Units)
					.CollectUpkeep(MapId, 1)
					.Where(demand => demand.Source == "unit:swordsman")
					.Sum(demand => demand.Units);
				Check.AssertEqual(2, swordsmen, "两个民兵（一个在 `Invader` 槽位）都应计入维护口径");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`E9`/`E10`：同格交战 = 两条按日循环（各打各的），直到一方 HP 归零被移除。</summary>
		private static void SameCellExchangeIsMutual()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);

				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);                       // HP 60 / ATK 6 / 射程 0
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4)); // HP 100 / ATK 8 / 射程 0
				h.Units.ExcuteAction(MapId, swordsman.GetInfo().UId, enemyCell, wolf.GetInfo().UId, "CanAttack");

				Check.AssertEqual(2, h.Units.Combat.ActiveLoops, "交战对 = 两条循环（双向各一条）");
				Check.AssertEqual(100f, swordsman.MaxHP, "生命上限来自配置（`E15` 前置字段）");

				h.Clock.AdvanceDays(1);
				Check.AssertEqual(52f, wolf.HP, "第 1 日：野狼挨 8 点（`E10`：ATK = 每日伤害）");
				Check.AssertEqual(94f, swordsman.HP, "第 1 日：民兵也挨 6 点（design「双方按秒计算伤害」）");
				Check.AssertEqual(2, h.Units.Combat.AttacksResolved, "一日一次结算，双方各一次");
				Check.AssertEqual(14f, h.Units.Combat.TotalDamage, "累计伤害 = 8 + 6");

				// 野狼 60 HP / 每日 8 → 第 8 日阵亡（民兵 100 HP / 每日 6 → 第 17 日才会死）
				h.Clock.AdvanceDays(7);
				Check.Assert(h.Map.FindOccupantByUId(MapId, wolf.GetInfo().UId) == null, "野狼应按日掉血直到阵亡并被移除");
				Check.AssertEqual(1, h.Units.Combat.Kills, "击杀数 = 1");
				Check.AssertEqual(0, h.Units.Combat.ActiveLoops, "任一方消失 → 两条循环都注销（不留空转任务）");
				Check.Assert(swordsman.HP <= 58f && swordsman.HP > 0f,
					$"民兵至少挨了 7 日（6×7=42），实际 HP={swordsman.HP}（第 8 日它先击杀对手，反击循环随之中止）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>击杀的收尾：胜者占据该格、封锁解除、阵亡事件推送；掉落（`E20`）明确不在本轮。</summary>
		private static void WinnerTakesTheCell()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);

				var died = new List<UnitDiedEvent>();
				h.Bus.Subscribe<UnitDiedEvent>(died.Add);

				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);
				string wolfUid = wolf.GetInfo().UId;
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));
				float goldBefore = h.Pool().GetValue("Gold"); // 掉落归 `WP-3.7`：本轮资源池不应有变化

				h.Units.ExcuteAction(MapId, swordsman.GetInfo().UId, enemyCell, wolfUid, "CanAttack");
				h.Clock.AdvanceDays(8);

				// ① 胜者占据该格（"必须先击败敌人才能进入该地块" ⇒ 打赢了就进来了）
				MapCell cell = h.MapOf(MapId).GetCell(enemyCell);
				Check.AssertEqual(swordsman.GetInfo().UId, cell.Occupant.GetInfo().UId, "击杀后被挑战方的位置应由胜者接管");
				Check.Assert(cell.Invader == null, "交战槽位应清空（一格一对已结束）");
				Check.Assert(!h.Map.IsEngagedAt(MapId, enemyCell), "该格不再是交战状态");
				Check.AssertEqual(enemyCell, swordsman.Position, "胜者站在该格");
				Check.Assert(swordsman.IsIdle, "战斗结束后回到空闲（可建造/可移动）");

				// ② 封锁解除：不再是敌方格，但仍被胜者占着
				Check.Assert(!h.Map.IsHostileAt(MapId, enemyCell), "敌方单位消失 → 封锁解除（`B8`）");
				Check.Assert(h.Movement.CanEnter(MapId, enemyCell), "该格恢复为可通行地形");
				Check.Assert(!h.Map.IsClear(MapId, enemyCell), "但被胜者占着，所以不是空地（不能在这格建造）");
				Check.Assert(h.Map.FindOccupantByUId(MapId, wolfUid) == null, "阵亡单位不应还能按 uid 查到（无僵尸索引）");

				// ③ 阵亡推送与"不返还"
				Check.AssertEqual(1, died.Count, "阵亡应推送一次");
				Check.AssertEqual(wolfUid, died[0].UnitUId, "阵亡单位 uid");
				Check.AssertEqual(EnemySpawner.HostileOwnerId, died[0].OwnerId, "阵亡方是敌方阵营");
				Check.AssertEqual(swordsman.GetInfo().UId, died[0].KillerUId, "凶手 uid");

				// ④ 掉落仍归 `WP-3.7`：这里锁住「本轮不消费 `DropReward`」的事实（否则 `WP-3.7` 无从验收）
				Check.AssertEqual(goldBefore, h.Pool().GetValue("Gold"), "本轮不做进池，掉落归 `WP-3.7`");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`E11`/`E19`：远程隔格开火（不共格、近战敌人打不到它），射程外则先接近再开火。</summary>
		private static void RangedAttacksWithoutCoLocation()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);

				Unit wolf = h.PlaceEnemy("wolf", new HexCubePosition(3, 4));         // 射程 0（近战）
				Unit archer = h.SpawnFor(1, "archer", new HexCubePosition(1, 4));    // 射程 3 / HP 60 / ATK 6
				Check.AssertEqual(2, archer.Position.DistenceTo(wolf.Position), "准备：距离 2（射程 3 之内）");

				Check.Assert(h.Units.ExcuteAction(MapId, archer.GetInfo().UId, wolf.Position, wolf.GetInfo().UId, "CanAttack"),
					"射程内应接受攻击指令");
				Check.AssertEqual(new HexCubePosition(1, 4), archer.Position, "远程攻击**不共格**（隔格开火，`E11`）");
				Check.Assert(!h.Map.IsEngagedAt(MapId, wolf.Position), "隔格攻击不占用交战槽位");
				Check.AssertEqual(1, h.Units.Combat.ActiveLoops, "只有攻击方一条循环（野狼射程 0，打不到 2 格外）");

				h.Clock.AdvanceDays(1);
				Check.AssertEqual(54f, wolf.HP, "第 1 日：野狼挨 6 点（`E10` 每日伤害）");
				Check.AssertEqual(60f, archer.HP, "弓箭手不掉血：近战敌人够不着隔格目标");
				Check.AssertEqual(1, h.Units.Combat.ActiveLoops, "野狼始终开不出反击循环");

				// 射程之外的目标 → 先接近（R7：目的地可随时改），走到位再开火
				Unit far = h.PlaceEnemy("wolf", new HexCubePosition(8, 4));
				Check.Assert(h.Units.ExcuteAction(MapId, archer.GetInfo().UId, far.Position, far.GetInfo().UId, "CanAttack"),
					"射程外也应接受指令（先接近）");
				Check.AssertEqual(new HexCubePosition(1, 4), archer.Position, "接令时不应瞬移");
				Check.Assert(archer.MoveTarget.HasValue, "应下达移动指令（走到射程内）");
				Check.AssertEqual(far.GetInfo().UId, archer.AttackTargetUid, "并记住攻击目标（走到位后由战斗侧兑现）");

				h.Clock.AdvanceDays(12); // 25 MP → 4 格（到 (5,4)，距目标 3）→ 到位即开火
				Check.AssertEqual(new HexCubePosition(5, 4), archer.Position, "应停在射程内的格子上（不会继续贴到敌人脸上）");
				Check.Assert(far.HP < 60f, $"走到位后应自动开火（目标 HP={far.HP}）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`E19`：进入敌方射程即受击，且**不再"遇敌即停"**；离开射程后循环自行注销。</summary>
		private static void HostileReactsToRangeEntry()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);

				Unit eagle = h.PlaceEnemy("eagle", new HexCubePosition(6, 4));        // 射程 2 / ATK 8
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(1, 4)); // 视野 4 → 旧实现会在 (3,4) 停下

				Check.AssertEqual(5, swordsman.Position.DistenceTo(eagle.Position), "准备：在猛禽射程之外");
				Check.AssertEqual(0, h.Units.Combat.ActiveLoops, "准备：还没有交战循环");

				// 走到 (5,4)：途中在 (4,4) 进入猛禽射程（半径 2）
				h.Movement.SetDestination(MapId, swordsman.GetInfo().UId, new HexCubePosition(5, 4));
				h.Clock.AdvanceDays(10);

				Check.AssertEqual(new HexCubePosition(5, 4), swordsman.Position,
					"单位应走完全程 —— 旧实现「视距内出现敌人就停步」会在 (3,4) 就停下（过渡行为已删除）");
				Check.AssertEqual(100f, swordsman.HP, "移动当日尚未挨打（敌方循环在本日派发之后才挂上，次日才结算）");
				Check.AssertEqual(swordsman.GetInfo().UId, eagle.AttackTargetUid, "进入射程后敌方开始攻击它（`E19`）");
				Check.AssertEqual(1, h.Units.Combat.ActiveLoops, "敌方开出一条按日循环");

				h.Clock.AdvanceDays(1);
				Check.AssertEqual(92f, swordsman.HP, "第 1 日挨 8 点（猛禽 ATK 8 当日伤害）");

				// 撤出射程：走回 (0,4)（距离 6 > 2）；移动要等下一个回复周期，期间继续挨打
				h.Movement.SetDestination(MapId, swordsman.GetInfo().UId, new HexCubePosition(0, 4));
				h.Clock.AdvanceDays(11);

				Check.AssertEqual(new HexCubePosition(0, 4), swordsman.Position, "撤出到射程之外");
				Check.Assert(swordsman.HP > 0f && swordsman.HP < 92f, $"撤退途中还会挨打，但应活着回来（HP={swordsman.HP}）");
				Check.AssertEqual(0, h.Units.Combat.ActiveLoops, "目标脱离射程 → 循环自行注销（无效循环不留恋）");
				Check.Assert(eagle.AttackTargetUid == null, "敌方的瞄准关系应被清空（不再对着空气开火）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`E12`/`E18`：建筑可被瞄准、伤害按 50% 衰减、加伤乘其后，且没有友伤。</summary>
		private static void BuildingIsAttackableWithHalfDamage()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);

				var enemyBuildingCell = new HexCubePosition(3, 4);
				Check.Assert(h.Build(2, "camp", enemyBuildingCell), "准备：敌方玩家在 (3,4) 建了一座营地");
				string enemyBuildingUid = h.Map.GetOccupantInfo(MapId, enemyBuildingCell).Value.UId;

				Unit archer = h.SpawnFor(1, "archer", new HexCubePosition(1, 4)); // 射程 3 / ATK 6

				Check.Assert(h.Units.ExcuteAction(MapId, archer.GetInfo().UId, enemyBuildingCell, enemyBuildingUid, "CanAttack"),
					"建筑是合法攻击目标（`E18`）");
				h.Clock.AdvanceDays(1);
				Check.AssertEqual(1, h.Units.Combat.BuildingHits, "对建筑结算了一次攻击");
				Check.AssertEqual(3f, h.Units.Combat.LastBuildingDamage, "弓箭手 6 → 对建筑衰减 50% 后 3 点（`E12`）");
				Check.Assert(h.Map.GetBuildingInfo(MapId, enemyBuildingCell) != null,
					"建筑还在：本轮不拆建筑（建筑 HP 归 `WP-4.8`，凭空造一套会与它的口径打架）");

				// 加伤乘其后（`E12`）：`UnitAttack` +50% → 6 × 0.5 × 1.5 = 4.5
				h.Modifiers.AddModifier(MapId, 1, "test-attack",
					new Modifier { Target = CombatRules.PlayerAttackBonusTarget, Type = "Percent", Value = 0.5f });
				h.Clock.AdvanceDays(1);
				Check.AssertEqual(2, h.Units.Combat.BuildingHits, "第 2 日：又一次对建筑结算");
				Check.AssertEqual(4.5f, h.Units.Combat.LastBuildingDamage, "衰减在前、加伤乘其后：6×0.5×1.5 = 4.5");

				// 无友伤：自家建筑不是目标
				var ownCell = new HexCubePosition(1, 3);
				Check.Assert(h.Build(1, "camp", ownCell), "准备：自家在 (1,3) 建了一座营地");
				string ownUid = h.Map.GetOccupantInfo(MapId, ownCell).Value.UId;
				Check.Assert(!h.Units.ExcuteAction(MapId, archer.GetInfo().UId, ownCell, ownUid, "CanAttack"),
					"同 owner 的目标（建筑）应被拒绝：没有友伤");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`E9`：交战状态（进攻方）随地图存档保留；旧版本档缺少该槽位时按"未交战"读回。</summary>
		private static void EngagementSurvivesSaveLoad()
		{
			Harness h = NewHarness();
			try
			{
				Check.AssertEqual(3, MapSave.CurrentVersion, "地图存档版本应升到 3（新增交战进攻方槽位）");

				h.GeneratePlainMap(20260917, SmallMap);
				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));
				string swordsmanUid = swordsman.GetInfo().UId;
				string wolfUid = wolf.GetInfo().UId;

				h.Units.ExcuteAction(MapId, swordsmanUid, enemyCell, wolfUid, "CanAttack");
				h.Clock.AdvanceDays(2); // 双方都掉过血，确保"状态"而不只是"位置"被验证

				float swordsmanHp = swordsman.HP;
				float wolfHp = wolf.HP;
				Check.AssertEqual(enemyCell, swordsman.Position, "准备：进攻方已攻进敌格");

				// 存档（`SaveMapper` → `MapSave` v3）→ 逐出内存缓存 → 重新读档
				h.SessionMaps.Flush(MapId);
				h.SessionMaps.Evict(MapId);
				Map restored = h.MapOf(MapId);

				MapCell cell = restored.GetCell(enemyCell);
				Check.AssertEqual(wolfUid, cell.Occupant.GetInfo().UId, "读档后占据物槽位仍是被挑战方");
				Check.Assert(cell.Invader != null, "读档后交战槽位应恢复（`Invader` 落盘）");
				Check.AssertEqual(swordsmanUid, cell.Invader.GetInfo().UId, "交战中的进攻方按 uid 原样恢复");
				Check.Assert(restored.IsEngagedAt(enemyCell), "读档后该格仍是交战状态");

				var reloadedSwordsman = (Unit)h.Map.FindOccupantByUId(MapId, swordsmanUid);
				var reloadedWolf = (Unit)h.Map.FindOccupantByUId(MapId, wolfUid);
				Check.AssertEqual(enemyCell, reloadedSwordsman.Position, "读档后双方仍在同一格");
				Check.AssertEqual(swordsmanHp, reloadedSwordsman.HP, "进攻方 HP 精确保留");
				Check.AssertEqual(wolfHp, reloadedWolf.HP, "被挑战方 HP 精确保留");
				Check.AssertEqual(100f, reloadedSwordsman.MaxHP, "生命上限由配置派生（不落盘也能还原）");

				// 旧档（v2，没有 `Invader` 字段）仍可读：视为"未交战"，不猜字段、不需要迁移
				h.SessionMaps.Flush(MapId);
				h.DowngradeToV2(MapId);
				h.SessionMaps.Evict(MapId);
				Map legacy = h.MapOf(MapId);

				Check.AssertEqual(wolfUid, legacy.GetCell(enemyCell).Occupant.GetInfo().UId, "旧档的占据物照旧恢复");
				Check.Assert(legacy.GetCell(enemyCell).Invader == null, "旧档没有交战槽位 → 读回为未交战");
				Check.Assert(h.Map.FindOccupantByUId(MapId, swordsmanUid) == null, "旧档里的进攻方不再是该格的一部分（不静默造假状态）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>读档重建：交战双方（含敌方的反击）各自恢复一条循环，战斗继续推进。</summary>
		private static void LoopsRestoreAfterReload()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);
				Unit wolf = h.PlaceEnemy("wolf", new HexCubePosition(3, 4));
				Unit swordsman = h.SpawnFor(1, "swordsman", new HexCubePosition(2, 4));
				h.Units.ExcuteAction(MapId, swordsman.GetInfo().UId, wolf.Position, wolf.GetInfo().UId, "CanAttack");
				h.Clock.AdvanceDays(1);

				Check.AssertEqual(2, h.Units.Combat.ActiveLoops, "准备：交战对双向两条循环");
				Check.AssertEqual(52f, wolf.HP, "准备：第 1 日野狼挨 8 点");

				// 模拟 `WorldSaveService.LoadWorld` 的两步：作废旧订阅者 → 按实体重建任务
				h.Time.Reset();
				h.Units.Combat.ResetLoops();
				Check.AssertEqual(0, h.Units.Combat.ActiveLoops, "作废后不应残留交战循环登记");

				int restored = h.Units.RestoreUnitTasks(MapId, new Dictionary<string, float>());
				Check.Assert(restored >= 3, $"民兵（移动 + 交战）+ 野狼（交战）应重建 3 条任务（实际 {restored}）");
				Check.AssertEqual(2, h.Units.Combat.ActiveLoops, "交战双方各恢复一条循环（敌方反击不会因读档消失）");

				h.Clock.AdvanceDays(1);
				Check.AssertEqual(44f, wolf.HP, "恢复后继续按日结算（野狼再挨 8 点）");
				Check.AssertEqual(88f, swordsman.HP, "民兵的反击循环同样恢复（再挨 6 点）");
			}
			finally { Cleanup(h.Dir); }
		}

		/// <summary>`B8`：0 伤害的单位打不进敌格（封锁保持），且不会白挂循环。</summary>
		private static void NonCombatantsStayBlocked()
		{
			Harness h = NewHarness();
			try
			{
				h.GeneratePlainMap(20260917, SmallMap);
				var enemyCell = new HexCubePosition(3, 4);
				Unit wolf = h.PlaceEnemy("wolf", enemyCell);
				Unit worker = h.SpawnFor(1, "worker", new HexCubePosition(2, 4)); // ATK 0 / 无 CanAttack

				Check.Assert(!h.Units.ExcuteAction(MapId, worker.GetInfo().UId, enemyCell, wolf.GetInfo().UId, "CanAttack"),
					"工人没有 CanAttack 能力 → 攻击指令在能力门控处被拒");
				Check.Assert(!h.Units.Combat.EnterEnemyCell(MapId, worker, enemyCell),
					"0 伤害的单位攻不进敌格 → 该格保持封锁（`B8`）");
				Check.AssertEqual(0, h.Units.Combat.ActiveLoops, "0 伤害不白挂按日循环");
				Check.AssertEqual(0, h.Movement.SetDestination(MapId, worker.GetInfo().UId, enemyCell),
					"以敌方格为目的地的移动指令应判不可达（普通位移挤不进去）");

				h.Clock.AdvanceDays(10);
				Check.AssertEqual(new HexCubePosition(2, 4), worker.Position, "10 日后工人仍被敌人挡在外面");
				Check.Assert(worker.IsIdle, "被挡住后回到空闲（可以就地建造/待命）");
			}
			finally { Cleanup(h.Dir); }

			// 攻击能力门控：即使填了伤害，`Actions` 里没有 `CanAttack` 的单位也不能开战（走路撞上敌人同样不能）
			Newtonsoft.Json.Linq.JObject table = RealUnitsTable();
			table["Units"]["worker"]["AttackDamage"] = 20; // 只给伤害、不给 CanAttack（校验器会为它记 warning）
			Harness gated = NewHarness(1, ConfigFixtures.RealConfigSourceWith("Units", table.ToString()));
			try
			{
				gated.GeneratePlainMap(20260917, SmallMap);
				Unit enemy = gated.PlaceEnemy("wolf", new HexCubePosition(3, 4));
				Unit builder = gated.SpawnFor(1, "worker", new HexCubePosition(2, 4));
				Check.AssertEqual(20f, builder.AttackDamage, "准备：该工人被填上了伤害");

				Check.Assert(!gated.Units.Combat.EnterEnemyCell(gated.MapId, builder, new HexCubePosition(3, 4)),
					"没有 CanAttack 能力 → 即使有伤害也不能攻进敌格（`Actions` 是开战能力的唯一门控）");
				Check.AssertEqual(0, gated.Units.Combat.ActiveLoops, "没有 CanAttack 能力 → 不挂任何交战循环");

				gated.Movement.SetDestination(gated.MapId, builder.GetInfo().UId, new HexCubePosition(6, 4));
				gated.Clock.AdvanceDays(20);
				Check.AssertEqual(new HexCubePosition(3, 4), enemy.Position, "对照：敌人（不移动）仍在原格");
				Check.Assert(builder.Position != new HexCubePosition(3, 4), "走路撞上敌人不会「顺便」开战（绕得开就绕，绕不开就停）");
			}
			finally { Cleanup(gated.Dir); }
		}

		/// <summary>`E9`/`E11`/`E19` 的两条新校验：`CanAttack` 与 `AttackDamage` 的口径自检（warning 级）。</summary>
		private static void ConfigRulesForCombat()
		{
			Check.Assert(HasUnitsWarning(SetUnitField("swordsman", "AttackDamage", 0), "AttackDamage<=0"),
				"有 CanAttack 却 0 伤害：应提示（战斗不会有任何效果，近战还进不去敌格）");

			Newtonsoft.Json.Linq.JObject table = RealUnitsTable();
			table["Units"]["worker"]["AttackDamage"] = 20;
			Check.Assert(HasUnitsWarning(table.ToString(), "缺少 CanAttack"),
				"有伤害却不含 CanAttack 的玩家单位：应提示（只能被动挨打）");

			ConfigReport report = ConfigFixtures.BuildRealCore().ConfigReport;
			Check.Assert(!report.ForTable("Units").Any(issue =>
					issue.Message.Contains("AttackDamage<=0") || issue.Message.Contains("缺少 CanAttack")),
				"真实配置表不应触发这两条战斗口径 warning");
		}

		// ────────────────────────── 配置表注入小工具 ──────────────────────────

		private static Newtonsoft.Json.Linq.JObject RealUnitsTable()
			=> Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigFixtures.TablePath("Units")));

		/// <summary>取真实 Units 表 → 给某个单位改一个字段 → 返回新的 JSON 文本。</summary>
		private static string SetUnitField(string unitId, string field, Newtonsoft.Json.Linq.JToken value)
		{
			Newtonsoft.Json.Linq.JObject table = RealUnitsTable();
			table["Units"][unitId][field] = value;
			return table.ToString();
		}

		/// <summary>用改过的 Units 表装配一次（不快速失败），看报告里有没有指定片段的 warning。</summary>
		private static bool HasUnitsWarning(string unitsJson, string fragment)
			=> ConfigFixtures
				.BuildCore(ConfigFixtures.RealConfigSourceWith("Units", unitsJson), failOnConfigErrors: false)
				.ConfigReport.ForTable("Units")
				.Any(issue => issue.Level == ConfigIssueLevel.Warning && issue.Message.Contains(fragment));

		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public string Dir;
			public int OwnerId = 1;
			public string MapId = CombatChecks.MapId;
			public GameClock Clock;
			public GameTimeService Time;
			public MapAppService Map;
			public MapSession SessionMaps;
			public FogAppService Fog;
			public ResourcesAppService Resources;
			public ConstructionAppService Construction;
			public UnitsAppService Units;
			public UnitMovementService Movement;
			public EnemySpawner Spawner;
			public ModifierAppService Modifiers;
			public DomainEventBus Bus;
			public ConfigTables Tables;
			public InMemoryMapRepository Repository;

			public Map MapOf(string mapId) => SessionMaps.Get(mapId);

			/// <summary>生成沙盘：全图铺平原（裸坐标不应被 Voronoi 水地形挡住）。</summary>
			public void GeneratePlainMap(int seed, int size)
			{
				Map.GenerateMap(seed, size, size, MapId);

				ITerrainData plain = Tables.Terrains.GetById("plain");
				foreach (MapCell cell in Map.GetAllCells(MapId).ToList())
					Map.SetTerrain(MapId, cell.Position, plain);
			}

			/// <summary>放置一个敌方单位：走**生产路径**（`EnemySpawner.Spawn` → `Map.PlaceOccupant`）。</summary>
			public Unit PlaceEnemy(string unitId, HexCubePosition position)
			{
				IUnitConfig config = Tables.Units.GetUnitConfig(unitId);
				Check.Assert(config != null && config.IsHostile, $"{unitId} 应是敌方单位配置");
				Check.Assert(Spawner.Spawn(MapOf(MapId), config, position), $"{unitId} 应能落在 {position}");

				return (Unit)Map.FindOccupantByUId(MapId, Map.GetOccupantInfo(MapId, position).Value.UId);
			}

			/// <summary>走训练路径放一个玩家单位（先在该格补 1 人：训练完成要扣人口）。</summary>
			public Unit SpawnFor(int ownerId, string unitId, HexCubePosition position)
			{
				Map.AddPopulation(MapId, position, 0, 9, 1);
				Resources.AddResource("Gold", 500f, MapId, ownerId);
				Resources.AddResource("Wood", 500f, MapId, ownerId);

				Units.CreateUnit(MapId, unitId, position, ownerId);
				Clock.AdvanceDays(3); // 快配置：玩家单位训练 3 日 → 就绪

				return (Unit)Map.FindOccupantByUId(MapId, Map.GetOccupantInfo(MapId, position).Value.UId);
			}

			/// <summary>为指定玩家在某格建一座建筑并等它完工（快配置：建造 2 日）。</summary>
			public bool Build(int ownerId, string buildingId, HexCubePosition position)
			{
				Resources.AddResource("Gold", 500f, MapId, ownerId);
				Resources.AddResource("Wood", 500f, MapId, ownerId);

				if (!Construction.StartConstruction(MapId, buildingId, position, ownerId)) return false;

				Clock.AdvanceDays(2);
				return Map.GetBuildingInfo(MapId, position) != null;
			}

			/// <summary>资源池快照（按存档分区重新读 —— 与游戏里的读取路径一致）。</summary>
			public ResourcesPool Pool() => Resources.GetOrCreatePool(MapId, OwnerId);

			/// <summary>（v0.3 / WP-3.6）把内存存档降到 v2 并抹掉交战槽位（模拟旧版本写出的档）。</summary>
			public void DowngradeToV2(string mapId) => Repository.DowngradeToV2(mapId);
		}

		/// <summary>装配一套可跑的应用服务（与其余检查同一套路；地图仓库用内存替身）。</summary>
		/// <param name="source">可选：自定义配置源（用例要"改表"时传，缺省 = 真实表 + 时长缩短）。</param>
		private static Harness NewHarness(int ownerId = 1, InMemoryConfigSource source = null)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp36-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			InMemoryConfigSource configSource = source ?? ConfigFixtures.RealConfigSource();
			Shorten(configSource);

			var mapRepository = new InMemoryMapRepository();
			CoreServices core = ConfigFixtures.BuildCore(configSource, mapRepository: mapRepository);
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue; // `D28`

			var time = new GameTimeService(core.Session.Clock, new TaskRepository(Path.Combine(dir, "tasks_")));
			var bus = new DomainEventBus();
			var resources = new ResourcesAppService(
				new ResourcesRepository(Path.Combine(dir, "res_")),
				core.Tables.Resources,
				time,
				new ModifierRepository(Path.Combine(dir, "mod_")));
			var modifier = new ModifierAppService(new ModifierRepository(Path.Combine(dir, "mod_")));
			var tech = new TechTreesAppService(
				new TechTreesRepository(Path.Combine(dir, "tech_"), core.Tables.TechTrees),
				core.Tables.TechTrees,
				resources,
				modifier,
				time,
				bus);
			var fog = new FogAppService(ownerId, new FogRepository(Path.Combine(dir, "fog_")));
			var construction = new ConstructionAppService(
				core.Map, resources, tech, new BuildingFactory(core.Tables.Buildings),
				core.Tables.Buildings, time, modifier, fog, bus);
			var units = new UnitsAppService(
				core.Map, tech, resources, construction, time, core.Tables.Units,
				new UnitFactory(core.Tables.Units), fog, core.Tables.Buildings, bus, modifier);

			return new Harness
			{
				Dir = dir,
				OwnerId = ownerId,
				Clock = core.Session.Clock,
				Time = time,
				Map = core.Map,
				SessionMaps = core.Session.Maps,
				Fog = fog,
				Resources = resources,
				Construction = construction,
				Units = units,
				Movement = units.Movement,
				Spawner = new EnemySpawner(core.Tables.Units, new UnitFactory(core.Tables.Units)),
				Modifiers = modifier,
				Bus = bus,
				Tables = core.Tables,
				Repository = mapRepository,
			};
		}

		/// <summary>建筑/升级 2 日、玩家单位 3 日（其余字段与真实表一致 —— 敌方行的射程/伤害/掉落保持原样）。</summary>
		private static void Shorten(InMemoryConfigSource source)
		{
			// 以**配置源里的当前内容**为基础（用例可能已经覆写过某张表）：否则会把改过的表覆盖回真实表
			var buildings = Newtonsoft.Json.Linq.JObject.Parse(
				source.LoadText("Buildings") ?? File.ReadAllText(ConfigFixtures.TablePath("Buildings")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)buildings["Buildings"]).Properties())
			{
				entry.Value["Duration"] = 2;
				entry.Value["UpgradeDuration"] = 2;
			}

			var units = Newtonsoft.Json.Linq.JObject.Parse(
				source.LoadText("Units") ?? File.ReadAllText(ConfigFixtures.TablePath("Units")));
			foreach (Newtonsoft.Json.Linq.JProperty entry in ((Newtonsoft.Json.Linq.JObject)units["Units"]).Properties())
			{
				// 敌方单位不参与训练（Duration 无意义）：保持 0，避免"训练 3 日才能落位"的噪声
				if (entry.Value["IsHostile"]?.ToObject<bool>() == true) continue;

				entry.Value["Duration"] = 3;
			}

			source.Inject("Buildings", buildings.ToString());
			source.Inject("Units", units.ToString());
		}

		private static void Cleanup(string dir)
		{
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch (IOException)
			{
				// 临时目录清理失败不影响验收结果
			}
		}
	}
}
