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
}