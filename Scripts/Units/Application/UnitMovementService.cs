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
	/// </summary>
	public sealed class UnitMovementService(
		MapAppService map,
		FogAppService fog,
		IUnitsRepository configs,
		ITimeService time)
	{
		private readonly MapAppService _map = map ?? throw new ArgumentNullException(nameof(map));
		private readonly FogAppService _fog = fog;
		private readonly IUnitsRepository _configs = configs ?? throw new ArgumentNullException(nameof(configs));
		private readonly ITimeService _time = time ?? throw new ArgumentNullException(nameof(time));

		/// <summary>最近累计的"因 MP 不足而未移动"次数（观测/调试用）。</summary>
		public int WaitingForMp { get; private set; }

		/// <summary>单位每个回复周期的 MP 总量（R1；缺配置时按 1 处理，避免死锁）。</summary>
		public float MovementOf(Unit unit)
		{
			float movement = _configs.GetUnitConfig(unit?.GetInfo().Id)?.Movement ?? 1f;
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

			float movement = MovementOf(unit);
			unit.CurrentMP += movement; // R2：每满一个回复周期 +M

			if (!IsMoving(unit))
			{
				// R3：静止时上限 = M（挂机不积累能量）
				if (unit.CurrentMP > movement) unit.CurrentMP = movement;

				unit.MoveTarget = null;
				unit.IsIdle = true;
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

				if (HasEnemyInVision(mapId, unit))
				{
					Stop(unit); // 视距内出现敌人 → 停下（战斗交给 `WP-3.6`）
					return;
				}

				if (!CanEnter(mapId, next))
				{
					Stop(unit); // 不可通行地形（`Passable=false`，如未解锁的水域）
					return;
				}

				float cost = TerrainCost(mapId, next);
				if (unit.CurrentMP < cost)
				{
					WaitingForMp++; // 不足则等下一个周期（不做"部分位移"）
					return;
				}

				unit.CurrentMP -= cost;
				MoveTo(mapId, unit, next);
				unit.MovePath.RemoveAt(0);

				if (unit.Position == unit.MoveTarget.Value)
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
		/// 能否进入该格（`E8`）：**只看 `Passable`**（地形通行权限是模拟侧的权威事实，未探索也一样不能穿水）。
		/// <para>首版沿用了"未探索放行"的迷雾例外，结果单位可以走进看不见的水域；迷雾只该影响玩家**看见什么**，
		/// 不该改变模拟的通行判定（`WP-4.11` 的解锁判定接在同一处）。</para>
		/// </summary>
		public bool CanEnter(string mapId, HexCubePosition position)
		{
			MapCell cell = _map.GetMapCell(mapId, position);
			if (cell == null) return true;


			return cell.Terrain != null && cell.Terrain.Passable;
		}

		/// <summary>视距内是否有敌方单位（有则停止移动）。</summary>
		private bool HasEnemyInVision(string mapId, Unit unit)
		{
			int ownerId = unit.GetInfo().OwnerId;
			int visionRadius = _configs.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 3;
			HexCubePosition position = unit.Position;

			foreach (MapCell cell in _map.GetAllCells(mapId))
			{
				if (cell.Occupant is Unit other
					&& other.GetInfo().OwnerId != ownerId
					&& position.DistenceTo(cell.Position) <= visionRadius)
					return true;
			}

			return false;
		}

		/// <summary>位移一格：更新位置与迷雾（离开的格子重置视野、进入的格子揭示）。</summary>
		private void MoveTo(string mapId, Unit unit, HexCubePosition position)
		{
			int visionRadius = _configs.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0;

			HexCubePosition from = unit.Position;
			unit.Position = position;

			if (_fog != null)
			{
				_fog.ResetArea(from, visionRadius);
				_fog.RevealArea(position, visionRadius);
			}
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
