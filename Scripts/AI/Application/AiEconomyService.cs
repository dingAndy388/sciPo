using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.AI.Application
{
	/// <summary>
	/// （v0.7.4 / WP-6.3）**AI 的经济分配**：把 `WP-6.2` 的判断变成订单 —— 只走**玩家同一套**应用服务
	/// （`ConstructionAppService.StartConstruction` / `TechTreesAppService.Research`）。
	/// <para>因此"不作弊"是结构性的：地形/科技前置/资源够不够都由那些服务校验，AI 没有"凭空造"的入口
	/// （`A-AI-2`）。本类也不读玩家的探查结果 —— 建造/研究都是"自家地盘上的事"。</para>
	/// <para>策略（`design/AI.md` 发展逻辑：住房 → 产出 → 科研；设计稿同时要求 AI **不无限扩张** `A-AI-9`）：</para>
	/// <list type="number">
	/// <item>**科研**：空闲（本树并发槽位有空）且 Idea 够 ⇒ 在 `SciencePreference` 指定的树里挑**最便宜**的可研究节点；
	/// 该树暂时没有可研究的就放过这一拍（跨树选择归后续 WP，先保持"流派偏好"这一个维度的语义）；</item>
	/// <item>**建造**：没有住房 ⇒ 住房；有住房但没有产出建筑 ⇒ 缺口粮建农田、否则建矿场；
	/// 住房与产出都齐了 ⇒ **不建**（不无限扩张）；</item>
	/// <item>**落点**：从自家已有建筑周边（半径 ≤ `PlacementRadius`）里挑可进入的空格，逐个交给
	/// `StartConstruction` 的**真实校验**去否决，第一个被接受的就是落点（AI 不自己判断地形规则）；</item>
	/// <item>**建造者**：派一个自家工人（已被别的工地绑走的除外）；工人都占着 ⇒ 这一拍不建。</item>
	/// </list>
	/// </summary>
	public sealed class AiEconomyService : IAiActionSink
	{
		/// <summary>落点搜索半径（格）：AI 只在自己聚落附近扩建（也顺带压住 `R2` 的开销）。</summary>
		private const int PlacementRadius = 6;

		/// <summary>单次行动最多试几个落点（超出就放弃这一拍：宁可晚一拍，也不要在 1 万格上扫图）。</summary>
		private const int PlacementAttempts = 60;

		/// <summary>建造者单位 Id（`Config/Units.json` 里的工人）。</summary>
		private const string WorkerUnitId = "worker";

		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly ResourcesAppService _resources;
		private readonly ConstructionAppService _construction;
		private readonly TechTreesAppService _tech;
		private readonly IUnitsRepository _unitConfigs;
		private readonly IBuildingConfigRepository _buildingConfigs;
		private readonly ITechTreesConfigRepository _techConfigs;
		private readonly IResourcesConfigRepository _resourceConfigs;
		private readonly IAiConfig _config;

		public AiEconomyService(
			GameSession session,
			MapAppService map,
			ResourcesAppService resources,
			ConstructionAppService construction,
			TechTreesAppService tech,
			IUnitsRepository unitConfigs,
			IBuildingConfigRepository buildingConfigs,
			ITechTreesConfigRepository techConfigs,
			IResourcesConfigRepository resourceConfigs,
			IAiConfig config)
		{
			_session = session;
			_map = map;
			_resources = resources;
			_construction = construction;
			_tech = tech;
			_unitConfigs = unitConfigs;
			_buildingConfigs = buildingConfigs;
			_techConfigs = techConfigs;
			_resourceConfigs = resourceConfigs;
			_config = config ?? AiConfigDto.Fallback;
		}

		/// <summary>最近一次结果（验收/调试）。</summary>
		public AiEconomyResult LastResult { get; private set; }

		/// <summary>累计建造次数 / 研究次数（冒烟与用例的"真的动了"证据）。</summary>
		public int BuildCount { get; private set; }

		public int ResearchCount { get; private set; }

		/// <summary>行动历史（按时间顺序）。</summary>
		public IReadOnlyList<AiEconomyResult> History => _history;

		private readonly List<AiEconomyResult> _history = new();

		void IAiActionSink.Execute(AiDecision decision) => Execute(decision);

		/// <summary>**执行一轮经济行动**（观测 → 判断 → 下单 的最后一段）。</summary>
		public AiEconomyResult Execute(AiDecision decision)
		{
			if (decision == null || _map == null) return null;

			string mapId = decision.MapId;
			int ownerId = decision.OwnerId;

			// ① 科研（与建造互不冲突：研究吃 Idea、建造吃 BasicMinerals）
			(string treeId, string nodeId, string researchNote) = TryResearch(mapId, ownerId);

			// ② 建造（按"住房 → 产出"的顺序，够了就不建）
			(string buildingId, string planNote) = ChooseBuilding(mapId, ownerId);
			string outcomeNote = null;
			HexCubePosition? position = null;
			string builderUId = null;
			if (buildingId != null)
			{
					(position, builderUId, outcomeNote) = TryBuild(mapId, ownerId, buildingId);
				if (position == null) buildingId = null;
			}

			var result = new AiEconomyResult
			{
				MapId = mapId,
				OwnerId = ownerId,
				Day = decision.Day,
				BuildOrder = buildingId,
				BuildPosition = position,
				BuilderUId = builderUId,
				ResearchTree = treeId,
				ResearchNode = nodeId,
				// "想建什么"与"建成了没/为什么没成"都记下来（`N3` 复盘只看这一行）
				Reason = $"{researchNote}；{planNote}{(outcomeNote == null ? string.Empty : " → " + outcomeNote)}",
			};

			if (result.Built) BuildCount++;
			if (result.Researched) ResearchCount++;

			LastResult = result;
			_history.Add(result);
			return result;
		}

		// ────────────── 科研 ──────────────

		/// <summary>在流派偏好树里挑最便宜的可研究节点并开工（返回 `(treeId, nodeId, 说明)`）。</summary>
		private (string TreeId, string NodeId, string Note) TryResearch(string mapId, int ownerId)
		{
			string treeId = _config.SciencePreference;
			ITechTreeConfig tree = string.IsNullOrWhiteSpace(treeId) ? null : _techConfigs?.GetTechTreeConfig(treeId);
			if (tree == null) return (null, null, "科研：流派偏好树不存在（配置 `SciencePreference`）");

			// 树内串行：本树已有在研究中的节点 ⇒ 这一拍不重复下单（`Concurrency=1` 的语义）
			if (_tech.GetInProgress(mapId, ownerId, treeId).Count > 0)
				return (null, null, $"科研：{treeId} 树已有研究在进行");

			var nodes = _tech.GetOrCreateTechTree(mapId, ownerId, treeId).Nodes;
			var candidates = tree.Techs.Values
				.Where(node => !(nodes.TryGetValue(node.Id, out TechNode current) && current.Researched))
				.Where(node => _tech.CanStartResearch(mapId, ownerId, treeId, node.Id))
				.OrderBy(node => node.Cost)
				.ThenBy(node => node.Id, StringComparer.Ordinal)
				.ToList();

			if (candidates.Count == 0) return (null, null, $"科研：{treeId} 树暂无可研究的节点（前置未满足）");

			ITechNodeConfig pick = candidates[0];
			float idea = _resources.GetOrCreatePool(mapId, ownerId).GetValue(ResearchResource);
			if (idea < pick.Cost) return (null, null, $"科研：Idea 不够（{idea:0.#} < {pick.Cost:0.#}）");

			_tech.Research(mapId, ownerId, treeId, pick.Id);

			// 用"是否真的进了进行中列表"当成功判据（`Research` 是 void，无法用返回值判断）
			if (!_tech.GetInProgress(mapId, ownerId, treeId).Contains(pick.Id))
				return (null, null, $"科研：{treeId}:{pick.Id} 被拒（资源或槽位）");

			return (treeId, pick.Id, $"科研：开工 {treeId}:{pick.Id}（花费 Idea {pick.Cost:0.#}）");
		}

		// ────────────── 建造 ──────────────

		/// <summary>按"住房 → 产出"的顺序选下一座建筑（都齐了返回 <c>null</c> = 不扩张，`A-AI-9`）。</summary>
		private (string BuildingId, string Note) ChooseBuilding(string mapId, int ownerId)
		{
			List<IMapOccupant> own = OwnBuildings(mapId, ownerId);

			if (!own.Any(IsHousing)) return (HousingBuildingId, "建造：没有住房 ⇒ 先补住房");

			bool hasFarm = own.Any(o => BuildingIdOf(o) == "farm");
			bool hasMine = own.Any(o => BuildingIdOf(o) == "mine");
			if (!hasFarm || !hasMine)
			{
				// 缺口粮先农田（食物是人口维护的刚需），否则矿场（建造与科研的原料）
				if (!hasFarm && HasFoodPressure(mapId, ownerId)) return ("farm", "建造：口粮有压力 ⇒ 农田");
				if (!hasMine) return ("mine", "建造：缺矿场 ⇒ 石材产能");
				return ("farm", "建造：缺农田 ⇒ 食物产能");
			}

			return (null, "建造：住房与产出建筑都齐了 ⇒ 不扩建（`A-AI-9` 不无限扩张）");
		}

		private (HexCubePosition? Position, string BuilderUId, string Note) TryBuild(string mapId, int ownerId, string buildingId)
		{
			IBuildingConfig config = _buildingConfigs?.GetBuildingConfig(buildingId);
			if (config == null) return (null, null, $"建造：{buildingId} 不在建筑表");

			// 资源够不够（先判一次，省得白扫落点）；真正的扣费仍由 `StartConstruction` 负责
			foreach ((string type, float amount) in config.ResourceCost)
			{
				float stock = _resources.GetOrCreatePool(mapId, ownerId).GetValue(type);
				if (stock + 0.001f < amount)
					return (null, null, $"建造：{buildingId} 资源不足（{type} {stock:0.#} < {amount:0.#}）");
			}

			string builderUId = IdleWorkerOf(mapId, ownerId);
			if (builderUId == null) return (null, null, $"建造：{buildingId} 没有空闲工人（都在工地上）");

						foreach (HexCubePosition candidate in CandidatePositions(mapId, ownerId))
			{
				var binding = new BuilderBinding { BuilderUId = builderUId, TargetPosition = candidate };
				if (_construction.StartConstruction(mapId, buildingId, candidate, ownerId, binding))
					return (candidate, builderUId, $"建造：{buildingId} 开工于 ({candidate.q},{candidate.r})（工人 {builderUId}）");
			}

			return (null, null, $"建造：{buildingId} 找不到合法落点（{PlacementRadius} 格内都被占/地形不符）");
		}

		/// <summary>
		/// 落点候选（确定性顺序）：自家建筑与单位周围半径 <see cref="PlacementRadius"/> 内**空着**的格子，
		/// 先近后远、同层按 (q,r) 排序。最多 <see cref="PlacementAttempts"/> 个（不在 1 万格上扫图，`R2`）。
		/// <para>把**自己的单位**也算锚点：开局第一座建筑（营地）还没有任何建筑可依靠，只能围着工人建。</para>
		/// </summary>
		private IEnumerable<HexCubePosition> CandidatePositions(string mapId, int ownerId)
		{
			// ⚠️ 去重键必须用坐标：`HexCubePosition` 没重写 `ToString()`（同名无法区分格子），
			// 旧写法 `seen.Add(position.ToString())` 会让**除第一格之外的候选全被当成重复**丢掉。
			var seen = new HashSet<(int Q, int R)>();
			int attempts = 0;

			foreach (IMapOccupant anchor in _map.GetOccupants(mapId)
						 .Where(o => o.GetInfo().OwnerId == ownerId)
						 .OrderBy(o => o.GetInfo().Position.q).ThenBy(o => o.GetInfo().Position.r))
			{
				HexCubePosition center = anchor.GetInfo().Position;

				// 以锚点为中心**先近后远**（同层按 q,r）：AI 的扩建贴着自己的聚落长，
				// 而不是从半径边缘开始（`InRadius` 的原始顺序不是距离序）。
				List<HexCubePosition> ring = center.InRadius(PlacementRadius)
					.OrderBy(position => center.DistenceTo(position))
					.ThenBy(position => position.q)
					.ThenBy(position => position.r)
					.ToList();

				foreach (HexCubePosition position in ring)
				{
					if (attempts >= PlacementAttempts) yield break;
					if (!seen.Add((position.q, position.r))) continue;
					if (!_map.IsClear(mapId, position)) continue;

					// 图外坐标必须在这里排掉：`GetMapCell` 越界返回 null，若不判空就会被当成"空地"
					// 白白吃掉尝试次数（`PlacementAttempts` 用完 → 明明有合法落点也说"找不到"）。
					MapCell cell = _map.GetMapCell(mapId, position);
					if (cell == null || cell.Building != null) continue;

					attempts++;
					yield return position;
				}
			}
		}

		/// <summary>派一个**没被工地占用**的自家工人；没有则返回 <c>null</c>。</summary>
		private string IdleWorkerOf(string mapId, int ownerId)
		{
			var busy = new HashSet<string>(StringComparer.Ordinal);
			foreach (IMapOccupant building in OwnBuildings(mapId, ownerId))
			{
				string uid = (building as Building)?.BuilderBinding?.BuilderUId;
				if (!string.IsNullOrWhiteSpace(uid)) busy.Add(uid);
			}

			foreach (IMapOccupant unit in _map.GetOccupants(mapId)
						 .Where(o => o.GetInfo().OwnerId == ownerId && o.GetInfo().Type == OccupantType.Unit)
						 .OrderBy(o => o.GetInfo().Position.q).ThenBy(o => o.GetInfo().Position.r))
			{
				if (busy.Contains(unit.GetInfo().UId)) continue;
				if (CanBuild(unit.GetInfo().Id)) return unit.GetInfo().UId;
			}

			return null;
		}

		// ────────────── 小工具 ──────────────

		private List<IMapOccupant> OwnBuildings(string mapId, int ownerId)
			=> _map.GetOccupants(mapId)
				   .Where(o => o.GetInfo().OwnerId == ownerId && o.GetInfo().Type == OccupantType.Building)
				   .ToList();

		private static string BuildingIdOf(IMapOccupant building) => building.GetInfo().Id;

		/// <summary>该单位能不能建造（单位表 `Actions` 含 `CanBuild`；查不到 = 不能，fail closed）。</summary>
		private bool CanBuild(string unitId)
		{
			IUnitConfig config = _unitConfigs?.GetUnitConfig(unitId);
			return config?.Actions != null && config.Actions.Contains("CanBuild", StringComparer.OrdinalIgnoreCase);
		}

		private bool IsHousing(IMapOccupant building)
			=> _buildingConfigs?.GetBuildingConfig(BuildingIdOf(building))?.IsHousing == true;

		/// <summary>口粮是否有压力（撑不到 3 个月 ⇒ 该补农田）。</summary>
		private bool HasFoodPressure(string mapId, int ownerId)
		{
			ISettlementConfig settlement = _resourceConfigs?.GetResourcesPoolConfig()?.Settlement;
			float demand = _map.GetTotalPopulation(mapId) * (settlement?.PopulationUpkeepPerMonth ?? 0f);
			float stock = _resources.GetOrCreatePool(mapId, ownerId).GetValue(settlement?.DemandResource ?? "Food");
			return demand <= 0f ? stock <= 0f : stock / demand < 3f;
		}

		/// <summary>住房 Id（升级到二/三级走 `UpgradeTo`，归后续 WP）。</summary>
		private const string HousingBuildingId = "camp";

		/// <summary>研究消耗的资源名（科技表口径：`Idea`）。</summary>
		private const string ResearchResource = "Idea";
	}
}
