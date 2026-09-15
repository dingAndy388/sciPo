using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;

namespace SciencePotato.Scripts.Units.Application
{
	/// <summary>
	/// （v0.3 / WP-3.5 / `UNIT-10`）**单位移动**：把 design/unit.md 的 R1~R7 从"硬编码在应用服务里的一段 tick"
	/// 提炼成独立服务，并修掉 `D23`（MP 上限被压成单次回复量 → 单位 100% 不可移动）。
	/// <list type="bullet">
	/// <item>**R1** 单位的 `Movement` = **每 10 游戏日回复的 MP 总量**（不是"机动性"）。</item>
	/// <item>**R2** 回复是**离散**的：每满 10 游戏日 `MP += M`（由 `UnitMoveDays` 节拍保证）。</item>
	/// <item>**R3** 静止（无移动指令）时 **MP 上限 = M**（挂机不能超出一个月能量）。</item>
	/// <item>**R4** 移动中（有目的地且未到达）**MP 无上限**，可跨多个回复周期累积。</item>
	/// <item>**R5** `MP ≥ 下一格地形消耗` → **立即位移且不消耗游戏时间**；够就继续连跳多格。</item>
	/// <item>**R6** 到达**最终目的地**时 **MP 清零**（多余作废）；未到达则结转。</item>
	/// <item>**R7** 目的地可随时更改（改目的地会重算路径）；"正在通过的一格不可取消"在瞬时位移模型下自动成立。</item>
	/// </list>
	/// <para>地形消耗来自 `Terrains.json` 的 `MoveCost`（平原 5 / 山地 25 …），通行性来自 `Passable`
	/// （`E8`：不再看 `MoveCost == 0` 之类的魔法值）。</para>
	/// <para>（v0.3 / WP-3.6 / `E9`/`E19`）**与战斗的接口**：敌方格在普通位移下仍不可进入，
	/// 但"能打的单位"会走 <see cref="UnitCombatService.EnterEnemyCell"/> **攻进去**（同格交战）；
	/// 每走完一格还会通知战斗侧两件事 —— "到位就开火"（`TryOpenFire`）与"进入敌方射程就受击"（`OnUnitMoved`）。
	/// 旧实现"视距内出现敌人就停步"（`HasEnemyInVision`）已删除：那是 `WP-3.5` 的过渡行为。</para>
	/// </summary>
	public sealed class UnitMovementService(
		MapAppService map,
		FogAppService fog,
		IUnitsRepository configs,
		ITimeService time,
		ModifierAppService modifiers = null)
	{
		private readonly MapAppService _map = map ?? throw new ArgumentNullException(nameof(map));
		private readonly FogAppService _fog = fog;
		private readonly IUnitsRepository _configs = configs ?? throw new ArgumentNullException(nameof(configs));
		private readonly ITimeService _time = time ?? throw new ArgumentNullException(nameof(time));

		/// <summary>（v0.8.5 / `WP-4.4`）修正器入口（可空）：`UnitSpeed` 的消费点。</summary>
		private readonly ModifierAppService _modifiers = modifiers;

		/// <summary>最近累计的"因 MP 不足而未移动"次数（观测/调试用）。</summary>
		public int WaitingForMp { get; private set; }

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`/`E19`）**战斗引擎**（可空 = 纯移动，不交战）。
		/// <para>用可空属性回连而不是构造参数：`UnitCombatService` 反过来需要本服务（接近目标、通行判定），
		/// 构造期互相注入会成环；由 `UnitsAppService` 在两者都建好后接上
		/// （`movement.Combat = combat`）。</para>
		/// </summary>
		public UnitCombatService Combat { get; set; }

		/// <summary>单位每个回复周期的 MP 总量（R1；缺配置时按 1 处理，避免死锁）。</summary>
		/// <param name="mapId">
		/// （v0.8.5 / `WP-4.4`）地图 Id：传入时按 `UnitSpeed` 修正器结算（`Movement × (1 + ΣPercent) + ΣAbsolute`）；
		/// 传 <c>null</c> 表示"不查修正器"（旧调用方/纯几何计算用的兼容路径）。
		/// </param>
		public float MovementOf(Unit unit, string mapId = null)
		{
			float movement = _configs.GetUnitConfig(unit?.GetInfo().Id)?.Movement ?? 1f;

			if (_modifiers != null && unit != null && !string.IsNullOrWhiteSpace(mapId))
				movement = _modifiers.GetValue(mapId, unit.GetInfo().OwnerId, "UnitSpeed", movement);

			return movement > 0f ? movement : 1f;
		}

		/// <summary>注册单位的移动/回蓝循环（每 <see cref="TimeConstants.UnitMoveDays"/> 游戏日一次）。</summary>
		public void RegisterMoveLoop(string mapId, string unitUid, float initialProgress = 0f)
		{
			var task = new IntervalTask(initialProgress, TimeConstants.UnitMoveDays, unitUid, "UnitMove", "none", mapId, 0);
			task.OnCompleted += () => Tick(mapId, unitUid);
			_time.Register(task);
		}

		/// <summary>
		/// 下达移动指令（R7：可随时改目的地）。返回路径长度（0 = 不可达 / 单位不存在）。
		/// <para>不立即扣 MP、也不立即位移 —— MP 是累积机制，够一格才动（R5）。</para>
		/// </summary>
		public int SetDestination(string mapId, string unitUid, HexCubePosition destination)
		{
			if (_map.FindOccupantByUId(mapId, unitUid) is not Unit unit) return 0;

			unit.MoveTarget = destination;
			unit.MovePath = _map.FindPath(mapId, unit.Position, destination, _fog);
			unit.IsIdle = unit.Position == destination || unit.MovePath.Count <= 1;

			if (unit.MovePath.Count <= 1)
			{
				unit.MoveTarget = null;
				return 0;
			}

			// 口径统一：`MovePath[0]` = **下一格**（去掉起点）——`Tick` 的重算分支也遵循同一约定，
			// 否则第一格会变成"给脚下的格子付通行费"（首版就是这么错的：MP 被扣但位置不变）
			unit.MovePath.RemoveAt(0);

			return unit.MovePath.Count; // 剩余待走格数
		}

		/// <summary>
		/// 一个回复周期的结算（R2~R5）：回蓝 → 若在移动中则连续位移到「MP 不够下一格 / 受阻 / 到达」为止。
		/// </summary>
		public void Tick(string mapId, string unitUid)
		{
			if (_map.FindOccupantByUId(mapId, unitUid) is not Unit unit) return;

			float movement = MovementOf(unit, mapId); // （v0.8.5 / WP-4.4）带地图 → 结算 UnitSpeed 修正器
			unit.CurrentMP += movement; // R2：每满一个回复周期 +M

			if (!IsMoving(unit))
			{
				// R3：静止时上限 = M（挂机不积累能量）
				if (unit.CurrentMP > movement) unit.CurrentMP = movement;

				unit.MoveTarget = null;

				// v0.3 / WP-3.6：**交战中不算空闲** —— 否则建造/驻扎门控会把"正在砍人"的单位当闲人
				unit.IsIdle = string.IsNullOrWhiteSpace(unit.AttackTargetUid);
				return;
			}

			// R7：目的地/路径失效时重算（终点被改、路径被占）
			// 重算条件：路径为空，或路径的第一格仍是"脚下"（说明它来自 SetDestination 的旧口径）—— 
			// 每次 tick 无脑重算会让 8 邻域 A* 在长跑里变成热点（`WP-3.5` 的性能顺带修）
			if (unit.MovePath == null || unit.MovePath.Count == 0 || unit.MovePath[0] == unit.Position)
			{
				unit.MovePath = _map.FindPath(mapId, unit.Position, unit.MoveTarget.Value, _fog);
				if (unit.MovePath.Count <= 1)
				{
					Stop(unit);
					return;
				}
				unit.MovePath.RemoveAt(0); // 去掉起点
			}

			// R5：够一格就走一格，走完仍够就继续（同一时刻连跳，不消耗游戏时间）
			while (unit.MovePath.Count > 0)
			{
				HexCubePosition next = unit.MovePath[0];

				if (!CanEnter(mapId, next))
				{
					// v0.3 / WP-3.6（`E9`）：敌方格不再是"死路" —— 能打的单位**攻进去**（进入敌格即交战，
					// 一格一对）；打不动（无伤害 / 该格已有入侵者）或地形不可通行 → 保持封锁、就地停下。
					// 攻进去**不消耗 MP**（design：攻击不消耗 MP），因此这里不扣地形费。
					if (Combat != null && Combat.EnterEnemyCell(mapId, unit, next)) return;

					Stop(unit);
					return;
				}

				float cost = TerrainCost(mapId, next);
				if (unit.CurrentMP < cost)
				{
					WaitingForMp++; // 不足则等下一个周期（不做"部分位移"）
					return;
				}

				// WP-3.8：位移走"占据物移动的唯一入口"（旧实现只改 unit.Position → 旧格留着僵尸占据物、
				// 索引与格子指向分歧）；占用冲突（目标格被别人占着）时就地停下，而不是硬挤进去
				if (!TryMoveTo(mapId, unit, next))
				{
					Stop(unit);
					return;
				}

				unit.CurrentMP -= cost;
				unit.MovePath.RemoveAt(0);

				bool arrived = unit.Position == unit.MoveTarget.Value;

				// v0.3 / WP-3.6：走到位就把攻击指令兑现（近战攻进目标格 / 远程射程内开火）。
				// 注意顺序：开火会置 `IsIdle = false`，因此**不能**先 `Stop`（那会把战斗中的人当成空闲）。
				if (Combat != null && Combat.TryOpenFire(mapId, unit))
				{
					unit.MovePath.Clear();
					if (arrived) unit.CurrentMP = 0f; // R6：到达目的地 → 剩余作废
					return;
				}

				// v0.3 / WP-3.6（`E19`）：进入敌方射程 → 对方开始按日攻击（取代 `WP-3.5` 的"遇敌即停"）
				Combat?.OnUnitMoved(mapId, unit);

				if (arrived)
				{
					unit.CurrentMP = 0f; // R6：到达目的地 → 剩余作废
					Stop(unit);
					return;
				}
			}

			Stop(unit);
		}

		/// <summary>下一格地形消耗（`Terrains.MoveCost`；缺地形按 1 处理以免死循环）。</summary>
		public float TerrainCost(string mapId, HexCubePosition position)
		{
			MapCell cell = _map.GetMapCell(mapId, position);
			return cell?.Terrain?.MoveCost ?? 1f;
		}

		/// <summary>
		/// 能否进入该格：
		/// <list type="number">
		/// <item>`E8`：地形必须 `Passable`（地形通行权限是模拟侧的权威事实，未探索也一样不能穿水）；</item>
		/// <item>`WP-3.8` / `B8`：**被敌方单位占据的格子封锁** —— design/unit.md「若地块被敌方单位占据，
		/// 必须先击败敌人才能进入」（敌人死亡后自动恢复，因为封锁就是"那格有没有敌方占据物"）。</item>
		/// </list>
		/// <para>（v0.3 / WP-3.6）**封锁不再是死路**：能打的单位通过"攻进去"（`E9` 同格交战）进入敌格 ——
		/// 那条路径走 <see cref="UnitCombatService.EnterEnemyCell"/>（占据物进入交战的唯一入口），
		/// 而不是普通位移，因此本方法对敌方格仍然返回 `false`（普通位移挤不进去，这是"一格一个占据物"的机制）。</para>
		/// <para>地图外（格不存在）视为不可进入：寻路一旦把越界格排进路径，位移就会试图"走出地图"。</para>
		/// </summary>
		public bool CanEnter(string mapId, HexCubePosition position)
		{
			MapCell cell = _map.GetMapCell(mapId, position);
			if (cell == null) return false;

			if (!(cell.Terrain != null && cell.Terrain.Passable)) return false;

			return !_map.IsHostileAt(mapId, position);
		}

		/// <summary>
		/// 位移一格（`WP-3.8` 起走**占据物移动的唯一入口**）：占用权威（格子 + `_occupants` 索引）随位置一起更新，
		/// 位移成功后再更新迷雾（离开的格子重置视野、进入的格子揭示）。
		/// </summary>
		/// <returns>是否真的动了（false = 目标格已被别的占据物占着，调用方应停下）。</returns>
		private bool TryMoveTo(string mapId, Unit unit, HexCubePosition position)
		{
			HexCubePosition from = unit.Position;

			if (!_map.MoveOccupant(mapId, unit, from, position)) return false;

			unit.Position = position;

			int visionRadius = _configs.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0;

			if (_fog != null)
			{
				_fog.ResetArea(from, visionRadius);
				_fog.RevealArea(position, visionRadius);
			}

			return true;
		}

		private static void Stop(Unit unit)
		{
			unit.MoveTarget = null;
			unit.IsIdle = true;
		}

		/// <summary>（v0.3 / WP-3.5）单位当前是否处于移动中（R4 的判定：有目的地且未到达）。</summary>
		public static bool IsMoving(Unit unit) => unit?.MoveTarget.HasValue == true && unit.MoveTarget.Value != unit.Position;
	}
}
