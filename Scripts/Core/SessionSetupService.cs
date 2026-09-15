using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Units.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core
{
	/// <summary>（v0.6.4 / WP-5.9）某个势力的出生点与其开局布置结果（启动报告与断言都用它）。</summary>
	public sealed class PlayerSpawn
	{
		public int OwnerId { get; init; }

		public HexCubePosition Position { get; init; }

		/// <summary>实际放下的开局单位 UId（放不下则为空）。</summary>
		public List<string> UnitUIds { get; init; } = new();

		public override string ToString() => $"owner={OwnerId} @({Position.q},{Position.r}) 单位×{UnitUIds.Count}";
	}

	/// <summary>
	/// （v0.6.4 / WP-5.9）**开局布置**：把"一张刚生成的地图"变成"一局能玩的游戏"。
	/// <para>职责（`D88`：**人类与 AI 规则完全相同**，唯一差别是谁下指令）：</para>
	/// <list type="number">
	/// <item>按玩家表逐个挑出生点：可通行地形、无占据物、**两两间距 ≥ `MinSpawnDistance`**（人类先挑，AI 依次挑）；</item>
	/// <item>发放开局额外资源（资源表的初始储备由资源池自己给，这里只补 `Start.InitialResources`）；</item>
	/// <item>放置开局单位（`InitialUnits`，**不收费**：开局布置不是"生产"，人类与 AI 同样待遇）；</item>
	/// <item>揭示人类出生点周围视野（`RevealRadius`）—— AI 的视野由按 owner 的迷雾负责（`WP-4.10`）。</item>
	/// </list>
	/// <para>幂等：同一 `mapId` 重复调用直接返回上次结果，不会重复放人（`SessionOrchestrator` 会在 `StartMap` 里调它）。</para>
	/// </summary>
	public sealed class SessionSetupService
	{
		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly UnitsAppService _units;
		private readonly ResourcesAppService _resources;
		private readonly FogAppService _fog;
		private readonly IStartSetupConfig _config;

		private readonly Dictionary<string, List<PlayerSpawn>> _spawns = new(StringComparer.Ordinal);

		public SessionSetupService(
			GameSession session,
			MapAppService map,
			UnitsAppService units = null,
			ResourcesAppService resources = null,
			FogAppService fog = null,
			IStartSetupConfig config = null)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_map = map;
			_units = units;
			_resources = resources;
			_fog = fog;
			_config = config;
		}

		/// <summary>配置缺省的间距（`Start.MinSpawnDistance`）。</summary>
		public int MinSpawnDistance => _config?.MinSpawnDistance ?? 20;

		public int RevealRadius => _config?.RevealRadius ?? 3;

		public IReadOnlyList<string> InitialUnits => _config?.InitialUnits ?? new List<string> { "worker" };

		public IReadOnlyList<PlayerSpawn> SpawnsOf(string mapId)
			=> mapId != null && _spawns.TryGetValue(mapId, out List<PlayerSpawn> list) ? list : new List<PlayerSpawn>();

		/// <summary>某势力的出生点（未布置过返回 <c>null</c>）。</summary>
		public PlayerSpawn SpawnOf(string mapId, int ownerId)
			=> SpawnsOf(mapId).FirstOrDefault(s => s.OwnerId == ownerId);

		/// <summary>布置一张地图上的全部势力（幂等）。返回每个势力的出生点与开局单位。</summary>
		public IReadOnlyList<PlayerSpawn> Setup(string mapId)
		{
			if (string.IsNullOrWhiteSpace(mapId)) return new List<PlayerSpawn>();
			if (_spawns.TryGetValue(mapId, out List<PlayerSpawn> existing)) return existing;

			Map.Domain.Map map = _session.Maps.Get(mapId);
			if (map == null) return new List<PlayerSpawn>();

			var placed = new List<PlayerSpawn>();
			var spawnPositions = new List<HexCubePosition>();

			// 玩家表已按 ownerId 升序 → 人类（owner=1）先挑，AI 依次挑，结果可复现
			foreach (PlayerContext player in _session.Players)
			{
				HexCubePosition spawn = PickSpawn(map, mapId, spawnPositions, player.OwnerId);
				var result = new PlayerSpawn { OwnerId = player.OwnerId, Position = spawn };
				spawnPositions.Add(spawn);

				// ① 开局额外资源（在资源表初始储备之外）
				if (_resources != null && _config?.InitialResources != null)
					foreach (KeyValuePair<string, float> pair in _config.InitialResources)
						_resources.AddResource(pair.Key, pair.Value, mapId, player.OwnerId);

				// ② 开局单位：人类与 AI **同样待遇**（不收费、同数量）—— 这是"不作弊"的第一条
				foreach (string unitId in InitialUnits)
				{
					HexCubePosition cell = FindFreeAdjacent(mapId, map, spawn, result.UnitUIds.Count);
					string uid = _units?.PlaceInitialUnit(mapId, unitId, cell, player.OwnerId);
					if (!string.IsNullOrWhiteSpace(uid)) result.UnitUIds.Add(uid);
				}

				// ③ 人类出生点周围揭示视野（AI 侧等 `WP-4.10` 的按 owner 迷雾）
				if (player.IsHuman && RevealRadius > 0) _fog?.RevealArea(spawn, RevealRadius);

				placed.Add(result);
			}

			_spawns[mapId] = placed;
			return placed;
		}

		/// <summary>
		/// 挑出生点：按固定顺序（(q,r) 升序）过滤"可通行 + 无占据物 + 离已有出生点足够远"，再用
		/// **ownerId 决定性的索引**挑一个 —— 同 seed 同玩家表必然得到同一布局（可复现）。
		/// <para>间距约束无候选时退化为"不重叠即可"：宁可出生点挨近，也不能出现"没有出生点"的局（那局直接玩不了）。</para>
		/// </summary>
		private HexCubePosition PickSpawn(Map.Domain.Map map, string mapId, List<HexCubePosition> taken, int ownerId)
		{
			var candidates = new List<HexCubePosition>();
			var fallback = new List<HexCubePosition>();

			foreach (MapCell cell in map.GetAllCells().Where(c => c != null))
			{
				if (cell.Terrain == null || !cell.Terrain.Passable) continue;
				if (!_map.IsClear(mapId, cell.Position)) continue; // 已被占（含敌方封锁格 / 交战中的格）

				if (taken.All(t => t != cell.Position)) fallback.Add(cell.Position);
				if (taken.All(t => HexDistance(t, cell.Position) >= MinSpawnDistance)) candidates.Add(cell.Position);
			}

			List<HexCubePosition> pool = candidates.Count > 0 ? candidates : fallback;
			if (pool.Count == 0) return default;

			pool.Sort((a, b) => a.q != b.q ? a.q.CompareTo(b.q) : a.r.CompareTo(b.r));

			// ownerId 决定性地分散选择：owner 1 取首个候选，owner 2 取约 1/2 处，owner 3 取约 1/3 处…
			int index = ownerId <= PlayerContext.FirstOwnerId
				? 0
				: (pool.Count / Math.Max(2, ownerId)) * (ownerId - 1) % pool.Count;

			return pool[index];
		}

		/// <summary>在出生点附近找一个空格放第 <paramref name="offset"/> 个开局单位（同格只放 1 个）。</summary>
		private HexCubePosition FindFreeAdjacent(string mapId, Map.Domain.Map map, HexCubePosition spawn, int offset)
		{
			if (offset == 0 && IsFree(mapId, map, spawn)) return spawn;

			foreach (HexCubePosition neighbor in spawn.GetNeighbor())
				if (IsFree(mapId, map, neighbor)) return neighbor;

			return spawn; // 放不下就回落到出生点（`PlaceInitialUnit` 自己会判断能不能放）
		}

		/// <summary>
		/// 该格是否可放单位：**必须**先确认格子在地图内（`GetNeighbor` 会给出图外的坐标，
		/// 直接查格会抛 `KeyNotFoundException` —— 本 WP 的用例抓到的第二条）。
		/// </summary>
		private bool IsFree(string mapId, Map.Domain.Map map, HexCubePosition position)
		{
			if (!map.TryGetCell(position, out MapCell cell)) return false;
			if (cell.Terrain?.Passable == false) return false;

			return _map.IsClear(mapId, position);
		}

		/// <summary>六边形距离（出生点间距用它；与寻路口径一致）。</summary>
		public static int HexDistance(HexCubePosition a, HexCubePosition b) => a.DistenceTo(b);
	}
}
