using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Units.Application
{
	/// <summary>
	/// （v0.3 / WP-3.7 / `E20`）**掉落消费端**：`UnitDiedEvent` 的订阅者 —— 把"敌方死了该掉什么"
	/// 翻译成"给谁多少资源"，再交给 <see cref="ILootSink"/> 入池。
	/// <para>为什么做成**事件消费端**而不是写进战斗引擎：掉落是"阵亡的一个后果"，而阵亡的推送
	/// （<see cref="UnitDiedEvent"/>，`WP-2.10` / `UNIT-08`）已经存在，且 `WP-4.7`（单位合并）、
	/// `WP-4.8`（建筑）也要消费它。战斗引擎（`UnitCombatService`）因此**一行都不用改**，
	/// 掉落口径集中在本类里可以被单独验收。</para>
	/// <para>**口径（与 log §18.6 的草案一致）**：</para>
	/// <list type="number">
	/// <item>**只给"玩家击杀敌方"**：被杀者配置 `IsHostile=true`、凶手能在图上找回、凶手**不是**敌方单位
	/// （敌方互殴、环境/未知来源的阵亡一律不掉）—— 凶手 uid 缺失即归此类；</item>
	/// <item>**进击杀者所有者**的资源池（不是阵亡者所有者，也不是"最近的城市"）；</item>
	/// <item>**玩家单位阵亡不掉落**（design：「不掉落单位或建筑」；`E21` 的"不返还资源"自 `WP-2.5` 起已成立）；</item>
	/// <item>每类"没掉成"的原因都单独计数（见下方属性）—— 掉落为什么没发生必须可查，否则"表填了却没收益"
	/// 只能靠猜（`D25` 可观测性）。</item>
	/// </list>
	/// <para>**装载位置**：`UnitsAppService` 构造时建一份并挂到领域事件总线上（总线按**引用**幂等、
	/// 订阅者异常隔离，因此"同一进程同一总线只应有一个掉落服务"—— 重复装配会导致重复入池）。</para>
	/// <para>**本类不做的事**：池上限裁剪（`ResourcesPool.AddValue` 的既有口径，实际入池量由
	/// <see cref="ILootSink.GrantLoot"/> 返回）、驻扎加成清理（`E21` 后半，依赖驻扎系统 `WP-4.6`）。</para>
	/// </summary>
	public sealed class UnitLootService
	{
		private readonly MapAppService _map;
		private readonly IUnitsRepository _configs;
		private readonly ILootSink _loot;
		private readonly IDomainEventBus _events;

		public UnitLootService(
			MapAppService map,
			IUnitsRepository configs,
			ILootSink loot = null,
			IDomainEventBus events = null)
		{
			_map = map ?? throw new ArgumentNullException(nameof(map));
			_configs = configs;
			_loot = loot;
			_events = events;

			_events?.Subscribe<UnitDiedEvent>(OnUnitDied);
		}

		// ────────────────────────── 观测口径（UI / 用例；不参与玩法）──────────────────────────

		/// <summary>处理过的阵亡事件总数（应等于下面四个"没掉成"计数 + <see cref="Drops"/>）。</summary>
		public int HandledDeaths { get; private set; }

		/// <summary>真正入池的掉落次数（一次击杀 = 1，即使被池上限吃掉一部分也仍算发生）。</summary>
		public int Drops { get; private set; }

		/// <summary>**玩家单位**阵亡而不掉落的次数（`E21`：不掉落单位或建筑）。</summary>
		public int PlayerDeathsIgnored { get; private set; }

		/// <summary>找不出"玩家凶手"而没掉落的次数（凶手 uid 缺失/已不在图上/凶手也是敌方单位）。</summary>
		public int UnattributedDeathsIgnored { get; private set; }

		/// <summary>该敌种没有掉落表（`DropReward` 为空）的次数。</summary>
		public int EmptyTablesIgnored { get; private set; }

		/// <summary>没有接资源池（`ILootSink` 为 null，纯战斗装配）而没掉落的次数。</summary>
		public int UnwiredDeathsIgnored { get; private set; }

		/// <summary>最近一次掉落的敌种 Id（"表真的被消费了"的证据）。</summary>
		public string LastDroppedUnitId { get; private set; }

		/// <summary>最近一次掉落的受益者（击杀者所有者）。</summary>
		public int LastLootOwnerId { get; private set; }

		/// <summary>最近一次掉落**实际**入池的数量（被上限裁剪后的净值）。</summary>
		public IReadOnlyDictionary<string, float> LastGranted { get; private set; } = new Dictionary<string, float>();

		/// <summary>累计实际入池的资源总量（跨资源求和，仅用于观测"这一局掉落了多大量"）。</summary>
		public float TotalGranted { get; private set; }

		// ────────────────────────── 入口 ──────────────────────────

		/// <summary>
		/// **一次阵亡的掉落判定**（`E20`）：四条前置按顺序检查，任一条不过就**分类计数并返回**
		/// （顺序即优先级：玩家阵亡 → 没接线 → 找不出凶手 → 空表）。
		/// </summary>
		public void OnUnitDied(UnitDiedEvent death)
		{
			if (death == null) return;
			HandledDeaths++;

			// ① 被杀的是玩家单位 → 不掉落（design 口径；也顺手挡掉"敌方互殴掉给敌人自己"）
			IUnitConfig victimConfig = _configs?.GetUnitConfig(death.UnitId);
			if (victimConfig == null || !victimConfig.IsHostile)
			{
				PlayerDeathsIgnored++;
				return;
			}

			// ② 没有资源池接线（纯战斗装配）→ 记一笔，不抛：阵亡流程不能被掉落打断
			if (_loot == null)
			{
				UnwiredDeathsIgnored++;
				return;
			}

			// ③ 凶手必须能定位到"玩家单位"：uid 缺失、已不在图上（连锁死亡/环境伤害）、凶手也是敌方 → 不掉
			Unit killer = ResolvePlayerKiller(death);
			if (killer == null)
			{
				UnattributedDeathsIgnored++;
				return;
			}

			// ④ 掉落表为空 → 该敌种就是不产资源（配置合法，不是错误）
			Dictionary<string, float> rewards = victimConfig.DropReward;
			if (rewards == null || rewards.Count == 0)
			{
				EmptyTablesIgnored++;
				return;
			}

			int ownerId = killer.GetInfo().OwnerId;
			Dictionary<string, float> granted = _loot.GrantLoot(death.MapId, ownerId, rewards)
				?? new Dictionary<string, float>();

			Drops++;
			LastDroppedUnitId = death.UnitId;
			LastLootOwnerId = ownerId;
			LastGranted = granted;

			foreach (KeyValuePair<string, float> entry in granted) TotalGranted += entry.Value;
		}

		/// <summary>
		/// **凶手的玩家身份判定**：凶手 uid → 地图上的单位 → 不是敌方配置。
		/// <para>用配置（`IsHostile`）而不是"凶手 owner ≠ 敌方 owner"来判断：敌方所有者（`-1`）
		/// 是 `EnemySpawner` 的实现细节，配置才是口径来源（`WP-3.8`：敌方单位由 `IsHostile` 派生）。</para>
		/// </summary>
		/// <returns>凶手单位；任一条不成立 → null。</returns>
		private Unit ResolvePlayerKiller(UnitDiedEvent death)
		{
			if (string.IsNullOrWhiteSpace(death.KillerUId)) return null;
			if (_map.FindOccupantByUId(death.MapId, death.KillerUId) is not Unit killer) return null;
			if (_configs?.GetUnitConfig(killer.GetInfo().Id)?.IsHostile ?? true) return null;

			return killer;
		}
	}
}
