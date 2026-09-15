using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Units.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.AI.Application
{
	/// <summary>
	/// （v0.7.5 / WP-6.4）**AI 的军事**：按 <see cref="AiMilitaryPolicy"/> 的军费占比决定造不造兵、造什么，
	/// 并在威胁升级时把人拉回自家聚落守家（`design/AI.md`：威胁分级 + 优先防御 + 不主动侵略）。
	/// <para>与 `WP-6.3` 同一条纪律：**只走玩家同一套服务** —— 训练走
	/// `UnitsAppService.TrainUnit`（军营/队列/人口/资源全由它校验），移动走
	/// `UnitsAppService.ExcuteAction(..., "CanMove")`（能力门控/寻路/MP 都由它负责）。AI 没有后门。</para>
	/// <para>**不主动侵略**（`A-AI-9`）：本服务只做"守家 + 按预算造兵"，不派兵去拆人 —— 主动进攻归后续
	/// 版本（用户 `N3` 复看后再定手感）。</para>
	/// </summary>
	public sealed class AiMilitaryService : IAiActionSink
	{
		/// <summary>军费预算 = 当前可用石材 × 军费占比：预算不够一个兵就不造（不把经济吃空）。</summary>
		private const string BudgetResource = "BasicMinerals";

		/// <summary>军营 Id 前缀（一级军营；升级到更高等级归后续 WP）。</summary>
		private const string MilitaryBuildingPrefix = "military_camp";

		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly ResourcesAppService _resources;
		private readonly UnitsAppService _units;
		private readonly IUnitsRepository _unitConfigs;
		private readonly IBuildingConfigRepository _buildingConfigs;
		private readonly IAiConfig _config;

		private readonly List<AiMilitaryResult> _history = new();

		public AiMilitaryService(
			GameSession session,
			MapAppService map,
			ResourcesAppService resources,
			UnitsAppService units,
			IUnitsRepository unitConfigs,
			IBuildingConfigRepository buildingConfigs,
			IAiConfig config)
		{
			_session = session;
			_map = map;
			_resources = resources;
			_units = units;
			_unitConfigs = unitConfigs;
			_buildingConfigs = buildingConfigs;
			_config = config ?? AiConfigDto.Fallback;
		}

		public AiMilitaryResult LastResult { get; private set; }

		public int TrainCount { get; private set; }

		public int RegroupCount { get; private set; }

		public IReadOnlyList<AiMilitaryResult> History => _history;

		void IAiActionSink.Execute(AiDecision decision) => Execute(decision);

		/// <summary>**执行一轮军事行动**。</summary>
		public AiMilitaryResult Execute(AiDecision decision)
		{
			if (decision == null || _map == null) return null;

			string mapId = decision.MapId;
			int ownerId = decision.OwnerId;
			float share = AiMilitaryPolicy.ShareFor(decision.Day, _config.NoMilitaryDays, decision.Threat, decision.Focus);

			(string trained, string buildingUId, string trainNote) = share <= 0f
				? (null, null, decision.Day < _config.NoMilitaryDays
					? $"不造兵：仍在 {_config.NoMilitaryDays} 日窗口内"
					: "不造兵：生存优先（先保口粮与人口）")
				: TryTrain(mapId, ownerId, decision.Threat, share);

			int regrouped = 0;
			string regroupNote = null;
			if (decision.Threat >= AiThreatLevel.High && AiMilitaryPolicy.ShouldRegroup(decision.Threat))
			{
				regrouped = Regroup(mapId, ownerId);
				regroupNote = $"防御：{regrouped} 个军事单位向自家聚落回撤";
			}

			var result = new AiMilitaryResult
			{
				MapId = mapId,
				OwnerId = ownerId,
				Day = decision.Day,
				Threat = decision.Threat,
				MilitaryShare = share,
				TrainedUnit = trained,
				TrainingBuildingUId = buildingUId,
				DefendersRegrouped = regrouped,
				Reason = regroupNote == null ? trainNote : $"{trainNote}；{regroupNote}",
			};

			if (result.Trained) TrainCount++;
			RegroupCount += regrouped;

			LastResult = result;
			_history.Add(result);
			return result;
		}

		// ────────────── 造兵 ──────────────

		/// <summary>按军费预算挑一个可负担的单位训练（返回 `(unitId, 军营 uid, 说明)`）。</summary>
		private (string UnitId, string BuildingUId, string Note) TryTrain(string mapId, int ownerId, AiThreatLevel threat, float share)
		{
			List<IMapOccupant> barracks = OwnBuildings(mapId, ownerId)
				.Where(o => o.GetInfo().Id.StartsWith(MilitaryBuildingPrefix, StringComparison.Ordinal))
				.Where(o => o.IsReady)
				.OrderBy(o => o.GetInfo().Position.q).ThenBy(o => o.GetInfo().Position.r)
				.ToList();

			if (barracks.Count == 0) return (null, null, "不造兵：没有军营（先建军营才能训练）");

			float budget = _resources.GetOrCreatePool(mapId, ownerId).GetValue(BudgetResource) * share;

			foreach (IMapOccupant barrack in barracks)
			{
				var building = barrack as Building;
				IBuildingConfig config = _buildingConfigs?.GetBuildingConfig(barrack.GetInfo().Id);
				if (building == null || config?.TrainableUnits == null) continue;

				// 队列满了就换下一个军营（`TrainingQueueLimit`）
				if (config.TrainingQueueLimit > 0 && building.TrainingQueue.Count >= config.TrainingQueueLimit) continue;

				var candidates = config.TrainableUnits
					.Select(unitId => _unitConfigs.GetUnitConfig(unitId))
					.Where(unit => unit != null)
					.Where(unit => Affordable(unit, budget))
					.OrderBy(unit => TotalCost(unit))
					.ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
					.ToList();

				foreach (IUnitConfig unit in candidates)
				{
					if (!_units.TrainUnit(mapId, barrack.GetInfo().UId, unit.UnitId)) continue;
					return (unit.UnitId, barrack.GetInfo().UId,
						$"造兵：训练 {unit.UnitId}（预算 {budget:0.#}={BudgetResource}×{share:0.##}，花费 {TotalCost(unit):0.#}）");
				}
			}

			return (null, null, $"不造兵：预算 {budget:0.#} 内没有可训练的单位（或军营队列已满）");
		}

		/// <summary>军费预算够不够养这个兵（按造价合计判断，不含人口/队列等由 `TrainUnit` 校验的条件）。</summary>
		private static bool Affordable(IUnitConfig unit, float budget) => TotalCost(unit) <= budget + 0.001f;

		private static float TotalCost(IUnitConfig unit) => unit.ResourceCost?.Values.Sum() ?? 0f;

		// ────────────── 守家 ──────────────

		/// <summary>
		/// **优先防御**：把军事单位（`Actions` 含 `CanAttack`）叫回自家建筑旁 —— 只对"已经跑出去"的单位下令，
		/// 已经在自家聚落里的不动（避免每拍都重发指令刷掉玩家的移动/战斗状态）。
		/// </summary>
		private int Regroup(string mapId, int ownerId)
		{
			List<IMapOccupant> homes = OwnBuildings(mapId, ownerId).ToList();
			if (homes.Count == 0) return 0;

			int regrouped = 0;
			foreach (IMapOccupant unit in OwnMilitaryUnits(mapId, ownerId))
			{
				HexCubePosition from = unit.GetInfo().Position;
				IMapOccupant nearest = homes
					.OrderBy(home => from.DistenceTo(home.GetInfo().Position))
					.FirstOrDefault();
				if (nearest == null) continue;

				HexCubePosition home = nearest.GetInfo().Position;
				if (from.DistenceTo(home) <= 1) continue; // 已经在家门口

				if (_units.ExcuteAction(mapId, unit.GetInfo().UId, home, string.Empty, "CanMove")) regrouped++;
			}
			return regrouped;
		}

		// ────────────── 小工具 ──────────────

		private List<IMapOccupant> OwnBuildings(string mapId, int ownerId)
			=> _map.GetOccupants(mapId)
				   .Where(o => o.GetInfo().OwnerId == ownerId && o.GetInfo().Type == OccupantType.Building)
				   .ToList();

		private IEnumerable<IMapOccupant> OwnMilitaryUnits(string mapId, int ownerId)
			=> _map.GetOccupants(mapId)
				   .Where(o => o.GetInfo().OwnerId == ownerId && o.GetInfo().Type == OccupantType.Unit)
				   .Where(o => _unitConfigs?.GetUnitConfig(o.GetInfo().Id)?.Actions?.Contains("CanAttack") == true)
				   .OrderBy(o => o.GetInfo().Position.q).ThenBy(o => o.GetInfo().Position.r);
	}
}
