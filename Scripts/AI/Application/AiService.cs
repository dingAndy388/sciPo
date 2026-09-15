using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.AI.Application
{
	/// <summary>
	/// （v0.7.3 / WP-6.2）**AI 决策循环**：按固定节拍（`AI.DecisionIntervalDays` 游戏日）对每个 AI 势力做一次
	/// "生存 → 威胁 → 发展"的优先级判断，产出可复盘、可断言的 <see cref="AiDecision"/>。
	/// <para>设计要点（`design/AI.md` 的"决策机制"）：</para>
	/// <list type="bullet">
	/// <item>**只读自己的视野**（自己单位的 `VisionRadius` 覆盖圈内的敌方单位）——"不作弊"的第一条；</item>
	/// <item>威胁等级按 `AI.ThreatThresholds` 的**可见敌数**分档（配置驱动 ⇒ 改表即改手感 → `N3`）；</item>
	/// <item>**前期不造兵**：`Day &lt; NoMilitaryDays` 时计划的军事投入恒为 0（`D93`：360 日 = 1 游戏年）；</item>
	/// <item>本类**只判断不执行**：订单（建造/科研/训练）归 `WP-6.3`/`WP-6.4`，这样"为什么这么做"始终可查。</item>
	/// </list>
	/// <para>幂等：同一 `(mapId, ownerId)` 只挂一条 tick（与月结/事件引擎同一套路）；人类势力不跑本引擎
	/// （由 `SessionOrchestrator` 保证）。</para>
	/// </summary>
	public sealed class AiService
	{
		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly ResourcesAppService _resources;
		private readonly IResourcesConfigRepository _resourceConfigs;
		private readonly IBuildingConfigRepository _buildingConfigs;
		private readonly IUnitsRepository _unitConfigs;
		private readonly ITimeService _time;
		private readonly IAiConfig _config;

		private readonly HashSet<string> _started = new(StringComparer.Ordinal);
		private readonly Dictionary<string, List<AiDecision>> _decisions = new(StringComparer.Ordinal);
		/// <summary>（v0.7.4 / WP-6.3）行动出口（可空）：判断完就交给它下单（建造/科研/军事）。</summary>
		private readonly List<IAiActionSink> _sinks = new();

		/// <summary>（v0.7.6 / WP-6.6）胜负判定（可空）：出局势力**不再决策**（自己把 tick 摘掉）。</summary>
		private VictoryService _victory;

		/// <summary>
		/// （v0.7.6 / WP-6.6）挂上胜负判定：出局后 `Evaluate` 返回 <c>null</c>，tick 自摘
		/// （否则"僵尸 AI"还会继续研究/造兵，玩家会觉得 AI 死了还在动）。
		/// </summary>
		public void AttachVictory(VictoryService victory) => _victory = victory;

		/// <summary>因"已出局"被跳过的决策次数（验收/调试用：证明出局后真的停了）。</summary>
		public int EliminatedSkips { get; private set; }

		/// <summary>（v0.7.4 / WP-6.3）挂上行动出口（由组合根在装配末尾调用；可挂多个：经济 + 军事）。</summary>
		public void AttachActionSink(IAiActionSink sink)
		{
			if (sink != null) _sinks.Add(sink);
		}


		/// <summary>累计决策次数（验收/调试：证明"按节拍"而不是"每帧"）。</summary>
		public int DecisionCount { get; private set; }

		public AiService(
			GameSession session,
			MapAppService map,
			ResourcesAppService resources,
			IResourcesConfigRepository resourceConfigs,
			IBuildingConfigRepository buildingConfigs,
			IUnitsRepository unitConfigs,
			ITimeService time,
			IAiConfig config)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_map = map;
			_resources = resources;
			_resourceConfigs = resourceConfigs;
			_buildingConfigs = buildingConfigs;
			_unitConfigs = unitConfigs;
			_time = time;
			_config = config ?? AiConfigDto.Fallback;
		}

		private static string Key(string mapId, int ownerId) => $"{mapId}|{ownerId}";

		/// <summary>该势力的决策历史（按时间顺序）。</summary>
		public IReadOnlyList<AiDecision> DecisionsOf(string mapId, int ownerId)
			=> _decisions.TryGetValue(Key(mapId, ownerId), out List<AiDecision> list) ? list : new List<AiDecision>();

		/// <summary>最近一次决策（没跑过返回 <c>null</c>）。</summary>
		public AiDecision LastDecision(string mapId, int ownerId)
			=> DecisionsOf(mapId, ownerId).LastOrDefault();

		/// <summary>该势力是否已挂上决策引擎。</summary>
		public bool IsEngineStarted(string mapId, int ownerId) => _started.Contains(Key(mapId, ownerId));

		/// <summary>
		/// （v0.9.9 / `WP-5.11` 收口）**作废某张图上的"已挂上"痕迹**，让 <see cref="StartEngine"/> 能重新挂一次。
		/// <para>为什么必须有：`WorldSaveService.LoadWorld` 会 `ITimeService.Reset()` 把**全部订阅**摘掉，
		/// 但本类的 `_started` 仍记着"挂过了" ⇒ 随后 `StartMap` 里的 `StartEngine` 返回 false、AI 读档后
		/// **再也不决策**（`_started` 与时间轴不一致）。由 `SessionEntryService.Load` 在读档后、`StartMap` 前调用。</para>
		/// </summary>
		/// <returns>被作废的势力数。</returns>
		public int ResetEngines(string mapId)
		{
			if (string.IsNullOrWhiteSpace(mapId)) return 0;

			string prefix = mapId + "|";
			return _started.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
		}

		/// <summary>
		/// **挂上决策节拍**（幂等）：每 `DecisionIntervalDays` 游戏日做一次判断。
		/// </summary>
		/// <returns>本次是否**新**挂上（供启动报告与断言）。</returns>
		public bool StartEngine(string mapId, int ownerId)
		{
			if (string.IsNullOrWhiteSpace(mapId) || _time == null) return false;
			if (!_started.Add(Key(mapId, ownerId))) return false;

			float interval = Math.Max(1, _config.DecisionIntervalDays);
			var task = new IntervalTask(0f, interval, $"ai_{mapId}_{ownerId}", "AiDecision", "none", mapId, ownerId);
			// 出局（`Evaluate` 返回 null）⇒ **自己把 tick 摘掉**：不留"僵尸 AI"在时间轴上空转（`WP-6.6`）
			task.OnCompleted += () =>
			{
				if (Evaluate(mapId, ownerId) == null) _time.Unregister(task);
			};
			_time.Register(task);
			return true;
		}

		/// <summary>
		/// **观测**：只收集"AI 自己看得见"的信息（自己的资产 + 自己视野圈内的敌方单位）。
		/// <para>视野是**几何计算**（每个自己单位覆盖 `VisionRadius` 半径的圈）—— 不查玩家迷雾、也不全图扫描
		/// （`R2`：10k 格上全图扫描是浪费；这里只对"自己的单位 × 全图敌方单位"做距离比较）。</para>
		/// </summary>
		public AiObservation Observe(string mapId, int ownerId)
		{
			List<IMapOccupant> occupants = _map?.GetOccupants(mapId).ToList() ?? new List<IMapOccupant>();

			List<IMapOccupant> ownUnits = occupants
				.Where(o => o.GetInfo().OwnerId == ownerId && o.GetInfo().Type == OccupantType.Unit)
				.ToList();
			List<IMapOccupant> ownBuildings = occupants
				.Where(o => o.GetInfo().OwnerId == ownerId && o.GetInfo().Type == OccupantType.Building)
				.ToList();

			int maxVision = ownUnits.Count == 0 ? 0 : ownUnits.Max(UnitVision);

			// 可见敌方单位 = 落在"任一自己单位的视野圈"内的敌方单位（野怪 ownerId<1 也算敌方，`D78`）
			int visible = 0;
			foreach (IMapOccupant enemy in occupants.Where(o => o.GetInfo().OwnerId != ownerId && o.GetInfo().Type == OccupantType.Unit))
			{
				HexCubePosition enemyPosition = enemy.GetInfo().Position;
				if (ownUnits.Any(unit => unit.GetInfo().Position.DistenceTo(enemyPosition) <= UnitVision(unit))) visible++;
			}

			float foodStock = 0f;
			if (_resources != null && !string.IsNullOrWhiteSpace(DemandResource))
				foodStock = _resources.GetOrCreatePool(mapId, ownerId).GetValue(DemandResource);

			return new AiObservation
			{
				MapId = mapId,
				OwnerId = ownerId,
				Day = _session.CurrentDay,
				UnitCount = ownUnits.Count,
				BuildingCount = ownBuildings.Count,
				HasHousing = ownBuildings.Any(IsHousing),
				FoodStock = foodStock,
				FoodDemandPerMonth = PopulationOf(mapId, ownerId) * UpkeepPerPopulation,
				VisibleEnemies = visible,
				MaxVisionRadius = maxVision,
			};
		}

		/// <summary>
		/// **判断**（观察 → 决策）：生存 → 威胁 → 发展。
		/// <para>可复现：同样的观察 + 同样的配置必然得到同样的决策（`N3` 复盘时只差一份观察快照）。</para>
		/// </summary>
		public AiDecision Decide(AiObservation observation)
		{
			AiThreatLevel threat = ClassifyThreat(observation.VisibleEnemies);
			int noMilitaryDays = _config.NoMilitaryDays;

			// ① 生存：口粮撑不到最低保留月数，或没有住房（人口无从增长）
			if (observation.FoodMonthsLeft < _config.ReserveMonths || !observation.HasHousing)
			{
				string reason = !observation.HasHousing
					? "生存：没有住房（人口无从增长）"
					: $"生存：口粮只能撑 {observation.FoodMonthsLeft:0.#} 个月（低于保留 {_config.ReserveMonths:0.#} 个月）";

				return Build(observation, AiFocus.Survival, threat, 0f, reason);
			}

			// ② 威胁：看得见敌人 ⇒ 按等级调整军事投入（仍受"前期不造兵"窗口约束）
			if (threat != AiThreatLevel.None)
			{
				float share = AiMilitaryPolicy.ShareFor(observation.Day, noMilitaryDays, threat, AiFocus.Threat);
				string reason = observation.Day < noMilitaryDays
					? $"威胁：可见 {observation.VisibleEnemies} 个敌方单位（{threat}），但仍在 {noMilitaryDays} 日不造兵窗口内"
					: $"威胁：可见 {observation.VisibleEnemies} 个敌方单位（{threat}），军事投入 {share:0.##}";

				return Build(observation, AiFocus.Threat, threat, share, reason);
			}

			// ③ 发展：无威胁 ⇒ 建造与科研优先（`design/AI.md`"无威胁或低威胁时优先建造与科研"）；
			//    窗口过后仍保留**基础守备**军费（`AiMilitaryPolicy.BaseShare`），否则"和平期一夜无兵"
			float baseShare = AiMilitaryPolicy.ShareFor(observation.Day, noMilitaryDays, threat, AiFocus.Development);
			return Build(observation, AiFocus.Development, threat, baseShare,
				$"发展：视野内无敌方单位，建造/科研 {_config.ResourceSplit.Build:0.##}/{_config.ResourceSplit.Research:0.##}" +
				(baseShare > 0f ? $"，基础守备 {baseShare:0.##}" : "，不造兵窗口内"));
		}

		/// <summary>**观测 + 判断 + 记录**（tick 与用例都走它）。<returns>出局时返回 <c>null</c>（不记录、不下单）。</returns></summary>
		public AiDecision Evaluate(string mapId, int ownerId)
		{
			// 出局即停（`WP-6.6`）：胜负判定说"既无单位也无建筑" ⇒ 不再决策、不再下单
			if (_victory != null && !_victory.IsAlive(mapId, ownerId))
			{
				EliminatedSkips++;
				return null;
			}

			AiDecision decision = Decide(Observe(mapId, ownerId));
			DecisionCount++;

			string key = Key(mapId, ownerId);
			if (!_decisions.TryGetValue(key, out List<AiDecision> list))
			{
				list = new List<AiDecision>();
				_decisions[key] = list;
			}
			list.Add(decision);

			// 判断完立刻下单（`D100` 的两段式：这里是第二段的入口）。异常不能掀掉 tick。
			foreach (IAiActionSink sink in _sinks)
			{
				try
				{
					sink.Execute(decision);
				}
				catch (Exception) { /* 单个 AI 的异常不能中断时间轴（与月结/事件同口径） */ }
			}

			return decision;
		}

		/// <summary>威胁分级：按 `ThreatThresholds`（配置驱动）。</summary>
		public AiThreatLevel ClassifyThreat(int visibleEnemies)
		{
			IAiThreatThresholds t = _config.ThreatThresholds;
			if (visibleEnemies >= t.Lethal) return AiThreatLevel.Lethal;
			if (visibleEnemies >= t.High) return AiThreatLevel.High;
			if (visibleEnemies >= t.Medium) return AiThreatLevel.Medium;
			if (visibleEnemies >= t.Low) return AiThreatLevel.Low;
			return AiThreatLevel.None;
		}

		private AiDecision Build(AiObservation observation, AiFocus focus, AiThreatLevel threat, float militaryShare, string reason)
			=> new()
			{
				MapId = observation.MapId,
				OwnerId = observation.OwnerId,
				Day = observation.Day,
				Focus = focus,
				Threat = threat,
				PlannedMilitaryShare = militaryShare,
				Reason = reason,
				Observation = observation,
			};

		/// <summary>配置里的口粮资源名（`Settlement.DemandResource`；缺省 Food）。</summary>
		private string DemandResource
			=> _resourceConfigs?.GetResourcesPoolConfig()?.Settlement?.DemandResource ?? "Food";

		private float UpkeepPerPopulation
			=> _resourceConfigs?.GetResourcesPoolConfig()?.Settlement?.PopulationUpkeepPerMonth ?? 0f;

		/// <summary>自己控制的人口（估算口粮需求）：只数"本势力占着该格"的人口。</summary>
		private int PopulationOf(string mapId, int ownerId)
		{
			int population = 0;
			foreach (MapCell cell in _map.GetAllCells(mapId).Where(c => c != null))
			{
				if (cell.Population <= 0) continue;

				IMapOccupant occupant = cell.Occupant ?? cell.Building;
				if (occupant?.GetInfo().OwnerId == ownerId) population += cell.Population;
			}
			return population;
		}

		private bool IsHousing(IMapOccupant building)
			=> _buildingConfigs?.GetBuildingConfig(building.GetInfo().Id)?.IsHousing == true;

		/// <summary>单位的视野半径（读单位表；查不到按 0 = 看不见任何东西，**fail closed**）。</summary>
		private int UnitVision(IMapOccupant unit)
			=> _unitConfigs?.GetUnitConfig(unit.GetInfo().Id)?.VisionRadius ?? 0;
	}
}
