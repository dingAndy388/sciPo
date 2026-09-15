namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.10）**建筑建造完成**（首次建成，不含升级 —— 升级见 <see cref="BuildingUpgradedEvent"/>）。
	/// <para>消费场景：UI 提示、教程、成就统计、"解锁新建筑时刷新可建列表"等。</para>
	/// </summary>
	public sealed class BuildingCompletedEvent(string mapId, int ownerId, string buildingUId, string buildingId, HexCubePosition position)
	{
		public readonly string MapId = mapId;
		public readonly int OwnerId = ownerId;
		public readonly string BuildingUId = buildingUId;
		public readonly string BuildingId = buildingId;
		public readonly HexCubePosition Position = position;
	}

	/// <summary>（v0.3 / WP-2.10）**建筑升级完成**（同一 uid 换配置，`WP-2.6`）。</summary>
	public sealed class BuildingUpgradedEvent(string mapId, int ownerId, string buildingUId, string fromBuildingId, string toBuildingId)
	{
		public readonly string MapId = mapId;
		public readonly int OwnerId = ownerId;
		public readonly string BuildingUId = buildingUId;
		public readonly string FromBuildingId = fromBuildingId;
		public readonly string ToBuildingId = toBuildingId;
	}

	/// <summary>（v0.3 / WP-2.10）**单位训练完成并落位**（`WP-2.5`）。</summary>
	public sealed class UnitTrainedEvent(string mapId, int ownerId, string unitUId, string unitId, HexCubePosition position)
	{
		public readonly string MapId = mapId;
		public readonly int OwnerId = ownerId;
		public readonly string UnitUId = unitUId;
		public readonly string UnitId = unitId;
		public readonly HexCubePosition Position = position;
	}

	/// <summary>
	/// （v0.3 / WP-2.10）**单位阵亡**（`UNIT-08` 的核心缺口：死亡原本没有任何推送）。
	/// <para>消费场景：亡语/遗言、击杀奖励、战报、成就统计、UI 刷新。</para>
	/// <para>**已落地的消费者**：`UnitLootService`（v0.3.23 / `WP-3.7` / `E20`）—— 只给"玩家击杀敌方"
	/// 掉落（用 <see cref="OwnerId"/> 区分阵亡方阵营、用 <see cref="KillerUId"/> 定位凶手与受益者），
	/// 掉落表来自 `IUnitConfig.DropReward`。`WP-4.7`（单位合并）、`WP-4.8`（建筑）也会消费它。</para>
	/// </summary>
	public sealed class UnitDiedEvent(string mapId, int ownerId, string unitUId, string unitId, HexCubePosition position, string killerUId)
	{
		public readonly string MapId = mapId;
		public readonly int OwnerId = ownerId;      // 阵亡方的所有者
		public readonly string UnitUId = unitUId;
		public readonly string UnitId = unitId;
		public readonly HexCubePosition Position = position;
		public readonly string KillerUId = killerUId; // 凶手 uid（可能是 null：环境/未知来源）
	}

	/// <summary>（v0.3 / WP-2.10）**科技研究完成**（`TECH-05`：原本没有任何推送）。</summary>
	public sealed class ResearchCompletedEvent(string mapId, int ownerId, string treeId, string nodeId)
	{
		public readonly string MapId = mapId;
		public readonly int OwnerId = ownerId;
		public readonly string TreeId = treeId;
		public readonly string NodeId = nodeId;
	}

	/// <summary>（v0.3 / WP-2.10）**随机/剧情事件触发**（`EVT-04`：原本只能轮询 `GetActiveEvents`）。</summary>
	public sealed class GameEventTriggeredEvent(string mapId, int ownerId, string eventId, string name, int duration)
	{
		public readonly string MapId = mapId;
		public readonly int OwnerId = ownerId;
		public readonly string EventId = eventId;
		public readonly string Name = name;
		public readonly int Duration = duration; // 0 = 永久（`WP-2.8` 口径）
	}

	/// <summary>
	/// （v0.7.0 / WP-4.8）**建筑易主（夺取）**：某栋房的 HP 归零后被敌方单位站上，归属改写。
	/// <para>消费场景：胜负判定重算（`VictoryService`）、表现层提示、以及"夺取后修正器/视野要换主人"的联动
	/// （`WP-4.8` 首批只做归属，修正器归属移交登记在 §19.5）。</para>
	/// </summary>
	public sealed class BuildingCapturedEvent(string mapId, int previousOwnerId, int newOwnerId, string buildingUId, string buildingId, HexCubePosition position)
	{
		public readonly string MapId = mapId;
		public readonly int PreviousOwnerId = previousOwnerId;
		public readonly int NewOwnerId = newOwnerId;
		public readonly string BuildingUId = buildingUId;
		public readonly string BuildingId = buildingId;
		public readonly HexCubePosition Position = position;
	}

	/// <summary>
	/// （v0.7.0 / WP-4.8）**建筑离开地图**（被拆 / 被摧毁）—— 胜负判定的"资产减少"推送
	/// （`D95` ③：全灭判定除了单位阵亡，建筑消失也必须触发重算）。
	/// </summary>
	public sealed class BuildingRemovedEvent(string mapId, int ownerId, string buildingUId, string buildingId)
	{
		public readonly string MapId = mapId;
		public readonly int OwnerId = ownerId;
		public readonly string BuildingUId = buildingUId;
		public readonly string BuildingId = buildingId;
	}
}