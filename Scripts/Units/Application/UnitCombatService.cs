using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Units.Application
{
	/// <summary>
	/// （v0.3 / WP-3.6 / `E9`~`E12`、`E18`、`E19`、`UNIT-12`）**战斗引擎**：把"交战"从散在
	/// <c>UnitsAppService</c> 里的两段 tick 提炼成独立服务，并按设计稿补齐四件事：
	/// <list type="number">
	/// <item>**同格交战 + 一格一对**（`E9`）：近战单位通过"攻进去"与目标**同格**（进攻方落进 `MapCell.Invader`，
	/// 被挑战方留在占据物槽位）；该格已有入侵者时第二个单位攻不进来。</item>
	/// <item>**按日结算**（`E10`）：一条循环 = 一对（进攻方→被挑战方），每 1 游戏日结算一次伤害，
	/// `AttackDamage` 就是"每日伤害"；被挑战方**打得到就打回去**（双向各一条循环，design「双方按秒计算伤害」）。</item>
	/// <item>**目标类型衰减**（`E12`）与**建筑作为目标**（`E18`）：伤害公式见 <see cref="CombatRules"/>；
	/// 建筑目前**还没有 HP**（`D7` / `WP-4.8`），因此本轮做到"可瞄准 + 公式生效 + 可观测"，
	/// 不做"拆建筑"（数值没有落点，凭空造一个会与 `WP-4.8` 的口径打架）。</item>
	/// <item>**敌方反应**（`E19`）：玩家单位**进入敌方射程**时，敌方开始按日攻击它 ——
	/// 取代 `WP-3.5` 留下的"视距内出现敌人就停步"（过渡行为，见缺陷"移动遇到敌人即停"）。</item>
	/// </list>
	/// <para>**移动与战斗的分工**：位移、MP、迷雾的更新仍归 <see cref="UnitMovementService"/>；
	/// 本服务只回答"这一格能不能以交战的方式进入""现在该不该开火"。两者用可空属性
	/// <see cref="UnitMovementService.Combat"/> 回连，避免构造期循环依赖。</para>
	/// <para>**不在这里做的事**（明确划给 `WP-3.7`）：掉落进资源池、驻扎加成的清理。阵亡的
	/// "从地图移除 + 注销任务 + 推送 <see cref="UnitDiedEvent"/>"沿用 `WP-2.10`/`WP-3.4` 已落地的口径。</para>
	/// </summary>
	public sealed class UnitCombatService
	{
		private readonly MapAppService _map;
		private readonly IUnitsRepository _configs;
		private readonly UnitMovementService _movement;
		private readonly ITimeService _time;
		private readonly FogAppService _fog;
		private readonly IDomainEventBus _events;
		private readonly ModifierAppService _modifiers;

		/// <summary>
		/// 已登记的"交战循环"：键 = 任务键 <c>atk_{攻击方uid}_{目标uid}</c>。
		/// <para>为什么需要它：`ITimeService.Register` 只按**引用**去重，重复下达同一对攻击会留下两条循环
		/// （同一个单位一天挨两刀）；这里用键做幂等，并让"注销某一对""按 uid 回收"成为可能
		/// （`IntervalTask` 没有完成语义，只能由宿主/对手消失来终结）。</para>
		/// </summary>
		private readonly Dictionary<string, IntervalTask> _loops = new(StringComparer.Ordinal);

		/// <summary>配置表里最大的攻击半径：`OnUnitMoved` 的扫描窗口（不必扫全图）。</summary>
		private readonly int _reactionRadius;

		public UnitCombatService(
			MapAppService map,
			IUnitsRepository configs,
			UnitMovementService movement,
			ITimeService time,
			FogAppService fog = null,
			IDomainEventBus events = null,
			ModifierAppService modifiers = null)
		{
			_map = map ?? throw new ArgumentNullException(nameof(map));
			_configs = configs ?? throw new ArgumentNullException(nameof(configs));
			_movement = movement ?? throw new ArgumentNullException(nameof(movement));
			_time = time ?? throw new ArgumentNullException(nameof(time));
			_fog = fog;
			_events = events;
			_modifiers = modifiers;

			_reactionRadius = Math.Max(1, _configs.GetAll()?.Where(c => c != null).Select(c => c.AttackRadius).DefaultIfEmpty(0).Max() ?? 1);
		}

		// ────────────────────────── 观测口径（UI / 用例；不参与玩法）──────────────────────────

		/// <summary>成立过的交战对数（"攻进敌格"成功次数）。</summary>
		public int EngagementsStarted { get; private set; }

		/// <summary>新开出的交战循环数（同一对重复下令不会增加）。</summary>
		public int ExchangesStarted { get; private set; }

		/// <summary>已结算的攻击次数（每次 = 1 日一次伤害）。</summary>
		public int AttacksResolved { get; private set; }

		/// <summary>累计造成的伤害（含对建筑的"账面伤害"）。</summary>
		public float TotalDamage { get; private set; }

		/// <summary>阵亡数。</summary>
		public int Kills { get; private set; }

		/// <summary>对建筑结算的攻击次数（`E18`；建筑 HP 归 `WP-4.8`，本轮只记账）。</summary>
		public int BuildingHits { get; private set; }

		/// <summary>最近一次对建筑算出的伤害（打一次即可断言 `E12` 的 0.5 衰减）。</summary>
		public float LastBuildingDamage { get; private set; }

		/// <summary>当前在跑的交战循环数（读档/泄漏排查用）。</summary>
		public int ActiveLoops => _loops.Count;

		// ────────────────────────── 入口：主动攻击 ──────────────────────────

		/// <summary>
		/// （`E9`/`E11`）**玩家主动攻击**的实现（`ExcuteAction("CanAttack")` 的唯一落点）：
		/// <list type="bullet">
		/// <item>**远程**（`AttackRadius >= 1`）：射程内 → 原地开火（隔格，不共格）；射程外 → 先走到射程内，
		/// 走到位由 <see cref="TryOpenFire"/> 兑现开火。</item>
		/// <item>**近战**（`AttackRadius == 0`）：相邻 → **立刻攻进敌格**（进入敌格即交战，一格一对）；
		/// 更远 → 先走到相邻格，再攻进去。</item>
		/// <item>目标与攻击方同格（已被卷入交战）→ 直接开火（双方按日结算）。</item>
		/// </list>
		/// <para>校验（任一不过即返回 false 且**无副作用**）：攻击方存在且已就绪、目标是**敌方**占据物、
		/// 目标还活着。同 owner 的目标一律拒绝（没有友伤）——敌对口径见 <see cref="CombatRules.IsEnemy"/>。</para>
		/// </summary>
		/// <returns>是否接受了攻击指令。</returns>
		public bool Engage(string mapId, string attackerUid, string targetUid)
		{
			if (_map.FindOccupantByUId(mapId, attackerUid) is not Unit attacker) return false;
			if (!attacker.IsReady) return false; // 训练中的单位不接受战斗指令

			IMapOccupant target = ResolveTarget(mapId, targetUid);
			if (target == null || !CombatRules.IsEnemy(attacker, target)) return false;
			if (target is Unit victim && victim.HP <= 0f) return false;

			HexCubePosition targetPosition = target.GetInfo().Position;

			// 已在同一格：交战对的两个成员互相开火
			if (attacker.Position == targetPosition)
				return target is Unit coLocated && StartExchange(mapId, attacker, coLocated);

			if (CombatRules.IsRanged(attacker))
			{
				if (CombatRules.CanReach(attacker, targetPosition)) return StartExchange(mapId, attacker, target);

				return Approach(mapId, attacker, target, targetPosition.InRadius(attacker.AttackRadius));
			}

			// 近战（`E11`：0 = 必须同格）：相邻即可"攻进去"
			if (attacker.Position.DistenceTo(targetPosition) == 1)
				return EnterEnemyCell(mapId, attacker, targetPosition);

			return Approach(mapId, attacker, target, targetPosition.InRadius(1));
		}

		/// <summary>
		/// （`E9`）**攻进敌格**：占据物进入交战的唯一入口（`Map.BeginEngagement` 的调用方）。
		/// <list type="number">
		/// <item>目标格必须是被**敌方单位**占着的格子（空地/建筑格走移动与建造，不是这条路）；</item>
		/// <item>进攻方必须有伤害（0 伤害的工人打不动敌人 → 该格保持**封锁**，与 `WP-3.8` 的 `B8` 一致）；</item>
		/// <item>该格**只能有一对**（已有入侵者 → 拒绝，design「一个单位或一对正在交战的单位」）。</item>
		/// </list>
		/// <para>成功即完成位移（`Unit.Position` + 迷雾 + 从原格摘除）、**不消耗 MP**（设计稿：攻击不消耗 MP）、
		/// 并结束当前移动指令（攻击动作优先于"继续赶路"）。</para>
		/// </summary>
		/// <returns>是否攻进去了（调用方据此决定"停下"还是"保持封锁"）。</returns>
		public bool EnterEnemyCell(string mapId, Unit attacker, HexCubePosition cell)
		{
			if (!CanEngageInto(mapId, attacker, cell, out Unit defender)) return false;

			HexCubePosition from = attacker.Position;
			if (!_map.BeginEngagement(mapId, attacker, from, cell)) return false;

			attacker.Position = cell;
			attacker.MoveTarget = null;
			attacker.MovePath?.Clear();
			RefreshFog(from, cell, attacker);

			EngagementsStarted++;
			StartExchange(mapId, attacker, defender);

			// `E19`：新格可能落在**别的**敌人的射程里（同时被两个敌人夹击是合法的）
			OnUnitMoved(mapId, attacker);
			return true;
		}

		/// <summary>
		/// （`E9`）**走到位就把攻击指令兑现**：近战 → 攻进目标格（同格交战）；远程 → 射程内开火。
		/// <para>由 <see cref="UnitMovementService"/> 在每走完一格后调用（旧实现把这段逻辑写在注释里、
		/// 从未落地 —— 于是"近战贴着敌人干站着"）。返回 false 表示"还没到位/打不着"，移动可以继续。</para>
		/// </summary>
		/// <returns>是否已开火（true = 移动应结束，攻击循环接管）。</returns>
		public bool TryOpenFire(string mapId, Unit unit)
		{
			if (unit == null || string.IsNullOrWhiteSpace(unit.AttackTargetUid)) return false;

			IMapOccupant target = ResolveTarget(mapId, unit.AttackTargetUid);
			if (target == null || !CombatRules.IsEnemy(unit, target) || (target is Unit dead && dead.HP <= 0f))
			{
				unit.AttackTargetUid = null; // 目标没了 → 清掉指令，不用玩家再点一次
				return false;
			}

			if (CombatRules.IsRanged(unit))
				return CombatRules.CanReach(unit, target) && StartExchange(mapId, unit, target);

			HexCubePosition targetPosition = target.GetInfo().Position;
			if (unit.Position == targetPosition) return target is Unit coLocated && StartExchange(mapId, unit, coLocated);
			if (unit.Position.DistenceTo(targetPosition) == 1) return EnterEnemyCell(mapId, unit, targetPosition);

			return false;
		}

		/// <summary>
		/// （`E19`）**落地反应**：单位刚落到新格后，凡是"射程覆盖到它"的敌方单位立刻开始按日攻击它。
		/// <para>这就是设计稿的「玩家单位进入敌方攻击范围时，敌方开始攻击」——取代了 `WP-3.5` 的
		/// 「视距内出现敌人就停步」（那条过渡行为已被删除：它既不是"受击"也不是"可攻击"，
		/// 只让玩家在敌人附近反复重新下令）。</para>
		/// <para>只扫 <c>最大 AttackRadius</c> 半径内的格子（配置表里最大 3 格），不扫全图：
		/// 本方法跑在每次位移之后，退化成 `O(全图)` 会让长距离行军变成热点。</para>
		/// </summary>
		public void OnUnitMoved(string mapId, Unit moved)
		{
			if (moved == null) return;

			HexCubePosition position = moved.Position;
			foreach (MapCell cell in _map.GetAllCells(mapId))
			{
				if (position.DistenceTo(cell.Position) > _reactionRadius) continue;

				EnsureReaction(mapId, cell.Occupant, moved);
				EnsureReaction(mapId, cell.Invader, moved);
			}
		}

		/// <summary>
		/// **伤害公式的唯一实现**（`E12`）：先按目标类型衰减（对建筑 ×0.5），再乘攻击方加伤
		/// （玩家单位 `UnitAttack` / 敌方单位 `EnemyAttack`），建筑目标额外乘 `BuildingDamage`
		/// （弩炮"对建筑 +50%" ⇒ `0.5×1.5 = 0.75`）。
		/// <para>公式本体在 <see cref="CombatRules.Damage"/>（纯函数、可单测）；修正器缺失（无组合根装配）
		/// 时返回衰减后的裸伤害。`Absolute` 型修正由 `ModifierManager` 在衰减**之后**加上（装备加伤不参与衰减）。</para>
		/// </summary>
		public float ComputeDamage(string mapId, Unit attacker, OccupantType targetType)
		{
			if (attacker == null) return 0f;

			float damage = attacker.AttackDamage * CombatRules.TargetFactor(targetType);
			if (_modifiers == null) return damage;

			int ownerId = attacker.GetInfo().OwnerId;
			damage = _modifiers.GetValue(mapId, ownerId, CombatRules.BonusTargetOf(attacker), damage);

			if (targetType == OccupantType.Building)
				damage = _modifiers.GetValue(mapId, ownerId, CombatRules.BuildingBonusTarget, damage);

			return damage;
		}

		// ────────────────────────── 读档接线 ──────────────────────────

		/// <summary>
		/// （`WP-3.6`）**作废交战循环登记**：由 `WorldSaveService.LoadWorld` 紧跟在 `ITimeService.Reset()`
		/// 之后调用 —— 那时旧订阅者已全部作废，若登记表还留着键，`RestoreAttackLoop` 会被自己挡掉
		/// （"读档后敌人不再反击"）。
		/// </summary>
		public void ResetLoops() => _loops.Clear();

		/// <summary>
		/// （`WP-3.6`）**读档重建交战循环**：单位的 `AttackTargetUid` 来自存档，这里按同一对重新挂上按日循环
		/// （由 `UnitsAppService.RestoreUnitTasks` 调用，进度从任务快照回填）。
		/// </summary>
		/// <returns>是否真的挂上（已存在同一对时返回 false）。</returns>
		public bool RestoreAttackLoop(string mapId, string attackerUid, string targetUid, float initialProgress = 0f)
			=> EnsureLoop(mapId, attackerUid, targetUid, initialProgress);

		/// <summary>
		/// **停手**（UI 的"取消攻击"）：注销该单位作为**攻击方**的全部循环并清掉瞄准关系。
		/// <para>单向语义：被攻击方的循环不受影响（"我停手"不等于"对方停手"）——它会在下一拍
		/// 发现自己打得到你，或者自己脱离射程时自行注销。</para>
		/// </summary>
		/// <returns>注销的循环数。</returns>
		public int StopAttacksOf(string mapId, string unitUid)
		{
			if (string.IsNullOrWhiteSpace(unitUid)) return 0;

			int removed = 0;
			foreach (string key in _loops.Keys.Where(k => k.StartsWith($"atk_{unitUid}_", StringComparison.Ordinal)).ToList())
			{
				StopLoop(key);
				removed++;
			}

			if (_map.FindOccupantByUId(mapId, unitUid) is Unit unit)
			{
				unit.AttackTargetUid = null;
				unit.IsIdle = true;
			}

			return removed;
		}

		// ────────────────────────── 内部：交战与接近 ──────────────────────────

		/// <summary>任务键：`atk_{攻击方uid}_{目标uid}`（`WP-2.2` 起就是这条约定，读档进度表按它取值）。</summary>
		private static string KeyOf(string attackerUid, string targetUid) => $"atk_{attackerUid}_{targetUid}";

		/// <summary>
		/// **开火**（交战对的一条方向）：记住"在打谁"、挂上按日循环，并让对方**打得到就打回来**。
		/// <list type="number">
		/// <item>攻击方 `IsIdle = false`（在战斗中不算空闲 —— 否则建造/驻扎门控会把它当闲人）；</item>
		/// <item>同一对**只挂一条**循环（`EnsureLoop` 幂等）：重复点同一个目标不会一天挨两刀；</item>
		/// <item>反击（`E19` + design「双方按秒计算伤害」）：被挑战方若能打到攻击方（同格恒可 / 远程看射程），
		/// 也挂一条反向循环 —— 打不到就白挨打（野狼射程 0 打不到 2 格外的弓箭手，这是远程的核心优势）。</item>
		/// </list>
		/// </summary>
		/// <returns>恒为 true：走到这里说明指令已被接受（无论是否**新**开出循环）。</returns>
		private bool StartExchange(string mapId, Unit attacker, IMapOccupant target)
		{
			string attackerUid = attacker.GetInfo().UId;
			string targetUid = target.GetInfo().UId;

			attacker.AttackTargetUid = targetUid;
			attacker.IsIdle = false;

			if (EnsureLoop(mapId, attackerUid, targetUid)) ExchangesStarted++;

			if (target is Unit defender
				&& defender.AttackDamage > 0f
				&& CombatRules.CanReach(defender, attacker.Position))
			{
				defender.AttackTargetUid = attackerUid;
				defender.IsIdle = false;
				if (EnsureLoop(mapId, targetUid, attackerUid)) ExchangesStarted++;
			}

			return true;
		}

		/// <summary>
		/// 敌方反应（`E19`）：这个占据物是否应该开始攻击 <paramref name="moved"/>（并挂上循环）。
		/// <para>门槛：是**敌方单位**、已就绪、**有伤害**（工人的 0 伤害不白挂循环）、射程覆盖得到它。</para>
		/// </summary>
		private void EnsureReaction(string mapId, IMapOccupant candidate, Unit moved)
		{
			if (candidate is not Unit other || ReferenceEquals(other, moved)) return;
			if (!other.IsReady) return;
			if (!CombatRules.IsEnemy(other, moved)) return;
			if (other.AttackDamage <= 0f) return;
			if (!CombatRules.CanReach(other, moved.Position)) return;

			other.AttackTargetUid = moved.GetInfo().UId;
			other.IsIdle = false;
			if (EnsureLoop(mapId, other.GetInfo().UId, moved.GetInfo().UId)) ExchangesStarted++;
		}

		/// <summary>能否以"交战"的方式进入该格（`E9` 的三条前置）：目标是被敌方单位占着的格、进攻方有伤害、该格还没有入侵者。</summary>
		private bool CanEngageInto(string mapId, Unit attacker, HexCubePosition cell, out Unit defender)
		{
			defender = null;
			if (attacker == null || !attacker.IsReady) return false;

			// 攻击能力门控：`Actions` 里没有 `CanAttack` 的单位（工人）**不能**开战 —— 包括"走路撞上敌人"
			// 这条路径（否则填表里"有伤害却缺 CanAttack"的警告就成了空话）。敌方单位不走这里（它们不移动）。
			if (!(_configs.GetUnitConfig(attacker.GetInfo().Id)?.Actions?.Contains("CanAttack") ?? false)) return false;

			if (_map.GetOccupantAt(mapId, cell) is not Unit occupant) return false; // 空地/建筑格不走这条路
			if (!CombatRules.IsEnemy(attacker, occupant) || occupant.HP <= 0f) return false;
			if (attacker.AttackDamage <= 0f) return false;      // 打不动 → 保持封锁（`B8` 的语义）
			if (_map.IsEngagedAt(mapId, cell)) return false;    // 一格一对

			defender = occupant;
			return true;
		}

		/// <summary>
		/// **先接近再打**：在候选格里挑第一个"能进去且路径可达"的落点下达移动指令（`WP-3.5` 的 R7：
		/// 目的地可随时改），并记住攻击目标 —— 走到位由 <see cref="TryOpenFire"/> 兑现。
		/// <para>候选顺序显式排序（离自己近的优先，再按 (q,r)）：不依赖字典/集合的枚举顺序，同一局面必得同一结果。</para>
		/// </summary>
		/// <returns>是否接受了"接近"指令（都不可达 → false，不留下任何状态）。</returns>
		private bool Approach(string mapId, Unit attacker, IMapOccupant target, IEnumerable<HexCubePosition> candidates)
		{
			string uid = attacker.GetInfo().UId;
			string targetUid = target.GetInfo().UId;
			HexCubePosition targetPosition = target.GetInfo().Position;

			foreach (HexCubePosition cell in candidates
				.Where(pos => pos != targetPosition && _movement.CanEnter(mapId, pos))
				.OrderBy(pos => attacker.Position.DistenceTo(pos))
				.ThenBy(pos => pos.q)
				.ThenBy(pos => pos.r))
			{
				if (_movement.SetDestination(mapId, uid, cell) <= 0) continue;

				attacker.AttackTargetUid = targetUid;
				attacker.IsIdle = false;
				return true;
			}

			return false;
		}

		/// <summary>按格解析目标：占据物槽位与交战槽位都算（交战中的对手在 `Invader` 槽位里）。</summary>
		private IMapOccupant ResolveTarget(string mapId, string uid)
			=> string.IsNullOrWhiteSpace(uid) ? null : _map.FindOccupantByUId(mapId, uid);

		/// <summary>攻进敌格后的视野更新：离开的格重置、进入的格揭示（与 <see cref="UnitMovementService"/> 同一约定）。</summary>
		private void RefreshFog(HexCubePosition from, HexCubePosition to, Unit unit)
		{
			if (_fog == null) return;

			int vision = _configs.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0;
			_fog.ResetArea(from, vision);
			_fog.RevealArea(to, vision);
		}

		// ────────────────────────── 内部：按日结算 ──────────────────────────

		/// <summary>
		/// 挂上一条交战循环（**幂等**：同一对只挂一条）。
		/// <para>口径（`E10`）：每 <see cref="TimeConstants.UnitAttackDays"/>（1 游戏日）结算一次伤害；
		/// `Id` = 任务键（读档进度表按它取值）、`UId` = `"none"`（循环不属于某个实体的"名下任务"，
		/// 它的生命周期由交战双方决定 —— 与既有 `atk_*` 约定一致）。</para>
		/// </summary>
		private bool EnsureLoop(string mapId, string attackerUid, string targetUid, float initialProgress = 0f)
		{
			if (string.IsNullOrWhiteSpace(attackerUid) || string.IsNullOrWhiteSpace(targetUid)) return false;

			string key = KeyOf(attackerUid, targetUid);
			if (_loops.ContainsKey(key)) return false;

			var task = new IntervalTask(initialProgress, TimeConstants.UnitAttackDays, key, "UnitAttack", "none", mapId, 0);
			task.OnCompleted += () => Tick(mapId, attackerUid, targetUid, task);

			_loops[key] = task;
			_time.Register(task);
			return true;
		}

		private void StopLoop(string key)
		{
			if (key == null || !_loops.TryGetValue(key, out IntervalTask task)) return;

			_time.Unregister(task);
			_loops.Remove(key);
		}

		/// <summary>
		/// 按 uid 回收交战循环（**两侧都算**）：阵亡时把"它打别人"与"别人打它"的每一对一次性摘掉
		/// （只摘一侧会让另一方对着尸体继续结算）。
		/// </summary>
		private int StopLoopsInvolving(string uid)
		{
			if (string.IsNullOrWhiteSpace(uid)) return 0;

			string asAttacker = $"atk_{uid}_";
			string asTarget = $"_{uid}";

			int removed = 0;
			foreach (string key in _loops.Keys
				.Where(k => k.StartsWith(asAttacker, StringComparison.Ordinal) || k.EndsWith(asTarget, StringComparison.Ordinal))
				.ToList())
			{
				StopLoop(key);
				removed++;
			}

			return removed;
		}

		/// <summary>
		/// **一次按日结算**（`E9`/`E10`）：任一方消失 / 目标脱离射程 → 注销循环；否则结算一次伤害。
		/// <para>循环任务没有"完成"语义（`TIME-02`），因此**终结条件必须写在这里**：目标死了、目标被移走、
		/// 自己死了、近战被挤开导致不同格、远程目标跑出射程 —— 每一种都注销，不留空转任务。</para>
		/// </summary>
		private void Tick(string mapId, string attackerUid, string targetUid, IntervalTask task)
		{
			if (_map.FindOccupantByUId(mapId, attackerUid) is not Unit attacker)
			{
				StopLoop(task.Id); // 攻击方已不在图上（阵亡/被移除）
				return;
			}

			IMapOccupant target = ResolveTarget(mapId, targetUid);
			if (target == null || (target is Unit victim && victim.HP <= 0f))
			{
				StopLoop(task.Id); // 目标已消失/已成尸体
				return;
			}

			// 目标脱离射程（近战被挤开、远程跑出半径）→ 停手：design 没有"追击"规则
			if (!CombatRules.CanReach(attacker, target))
			{
				attacker.AttackTargetUid = null;
				attacker.IsIdle = true;
				StopLoop(task.Id);
				return;
			}

			OccupantType targetType = target.GetInfo().Type;
			float damage = ComputeDamage(mapId, attacker, targetType);

			if (target is Unit unitTarget)
			{
				unitTarget.HP -= damage;
				AttacksResolved++;
				TotalDamage += damage;

				if (unitTarget.HP <= 0f)
				{
					Kills++;
					KillUnit(mapId, unitTarget, attackerUid);
				}

				return;
			}

			// `E18`/`E12`：建筑是合法目标，衰减系数已生效；**建筑 HP 归 `WP-4.8`**（`D7`），
			// 因此本轮只记账（`BuildingHits`/`LastBuildingDamage`）—— 不臆造"拆掉一栋还没有 HP 的建筑"。
			BuildingHits++;
			LastBuildingDamage = damage;
			TotalDamage += damage;
		}

		/// <summary>
		/// **阵亡处理**：离开占位（区分被挑战方 / 进攻方两种槽位）、更新视野、回收任务与交战循环、
		/// 清掉仍瞄准它的单位、推送 <see cref="UnitDiedEvent"/>。
		/// <para>**不在这里做的事**：掉落进资源池（`E20`）、驻扎加成清理（`E21`）—— 明确归 `WP-3.7`；
		/// "不返还资源"自 `WP-2.5` 起就成立（训练成本在入队时已扣，阵亡没有可返还的对象）。</para>
		/// </summary>
		private void KillUnit(string mapId, Unit victim, string killerUid)
		{
			MapOccupantInfo info = victim.GetInfo();
			HexCubePosition position = info.Position;

			// ① 离开该格：被挑战方阵亡 → 走"离开的唯一入口"（地图会把进攻方顶上位 ⇒ 胜者占据该格，
			//    这正是设计稿的"必须先击败敌人才能进入该地块"）；进攻方阵亡 → 只退出交战槽位
			bool wasInvader = ReferenceEquals(_map.GetInvader(mapId, position), victim);
			if (wasInvader) _map.EndEngagement(mapId, position);
			else _map.RemoveOccupantByPosition(mapId, position, victim);

			// ② 视野：阵亡者不再提供视野（`WP-2.3` 的"视野随生随灭"）
			_fog?.ResetArea(position, _configs.GetUnitConfig(info.Id)?.VisionRadius ?? 0);

			// ③ 任务与循环：先摘掉与它相关的每一对交战循环，再按 uid 回收它名下的周期任务（移动/训练…）
			StopLoopsInvolving(info.UId);
			_time.UnregisterByUId(info.UId);

			// ④ 清掉仍瞄准它的单位（同格交战的另一方、围殴者）—— 否则它们会继续"对着空气开火"
			ClearStaleTargets(mapId, info.UId);

			// ⑤ 推送（`WP-2.10` / `UNIT-08`）：掉落（`WP-3.7`）、战报、成就、UI 刷新都挂在这里
			_events?.Publish(new UnitDiedEvent(mapId, info.OwnerId, info.UId, info.Id, position, killerUid));
		}

		/// <summary>把"瞄准已消失目标"的单位恢复为空闲（循环已在 <see cref="StopLoopsInvolving"/> 里摘掉）。</summary>
		private void ClearStaleTargets(string mapId, string deadUid)
		{
			if (string.IsNullOrWhiteSpace(deadUid)) return;

			foreach (MapCell cell in _map.GetAllCells(mapId))
			{
				foreach (IMapOccupant occupant in new[] { cell.Occupant, cell.Invader })
				{
					if (occupant is not Unit unit || unit.AttackTargetUid != deadUid) continue;

					unit.AttackTargetUid = null;
					unit.IsIdle = true;
				}
			}
		}
	}
}
