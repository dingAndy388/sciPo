using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Application
{
	public class MapAppService(IMapGenerator generator, MapSession session, IEnumerable<IMapPostProcessor> postProcessors = null, Func<int, IRandom> randomFactory = null) : IPopulationSink
	{
		private readonly IMapGenerator _mapGenerator = generator;
		private readonly MapSession _session = session;

		/// <summary>
		/// （v0.3 / WP-3.10）减员用的随机源工厂（种子 → 随机源）：与 `EnemySpawner` 同一口径
		/// （缺省 <see cref="SystemRandom"/>），测试可注入受控随机源得到确定的"谁先被扣"。
		/// </summary>
		private readonly Func<int, IRandom> _randomFactory = randomFactory ?? (seed => new SystemRandom(seed));

		/// <summary>减员调用序号：混进随机种子，避免同一年内多次减员反复从同一格开始扣。</summary>
		private int _lossCallCount;

		/// <summary>（v0.3 / WP-3.8）生成后处理器（敌方单位刷新等）：由组合根按整表装配，缺省 = 不加工。</summary>
		private readonly List<IMapPostProcessor> _postProcessors = postProcessors != null
			? new List<IMapPostProcessor>(postProcessors)
			: new List<IMapPostProcessor>();

		private const byte FogUnexplored = 0;

		/// <summary>（v0.3 / WP-3.8）已挂载的生成后处理器数量（装配自检 / 日志用）。</summary>
		public int PostProcessorCount => _postProcessors.Count;

		public void GenerateMap(int seed, int width, int height, string Id)
		{
			Domain.Map map = _mapGenerator.Generate(width, height, seed, Id);

			// v0.3 / WP-3.8（`B7`/`UNIT-14`）：**生成后处理**在"地形已定、实体未落位"的窗口里跑
			// （敌方单位按地形概率放置）。处理器只依赖聚合根，因此仍是"种子 → 地图"的纯生成语义。
			foreach (IMapPostProcessor processor in _postProcessors)
				processor?.Process(map);

			// v0.3 / WP-3.1：新生成的地图放入会话缓存，并在存档点落盘
			_session.Set(Id, map);
			_session.MarkDirty(Id);
			_session.Flush(Id);
		}

		public MapCell GetMapCell(string mapId, HexCubePosition position)
		{
			// v0.3 / WP-3.8：越界格返回 null（旧实现抛 KeyNotFoundException —— 寻路把地图外格子当"可通行"
			// 时会把它排进路径，随后 CanEnter/TerrainCost 一起炸；封锁判定也必须先能问"这格存在吗"）
			var map = _session.Get(mapId);
			if (map == null) return null;
			return map.TryGetCell(position, out MapCell cell) ? cell : null;
		}

		public IEnumerable<MapCell> GetAllCells(string MapId)
		{
			return _session.Get(MapId).GetAllCells();
		}

		public void SetTerrain(string MapId, HexCubePosition position, ITerrainData terrain)
		{
			var map = _session.Get(MapId);
			map.SetTerrain(position, terrain);
			_session.MarkDirty(map.Id);
		}

		public bool IsClear(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map.GetOccupantInfo(position) == null;
		}

		public MapOccupantInfo? GetOccupantInfo(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map.GetOccupantInfo(position);
		}

		public IMapOccupant GetOccupantByUId(string mapId, string uid)
		{
			var map = _session.Get(mapId);
			return map.GetOccupantByUId(uid);
		}

		/// <summary>
		/// （v0.3 / WP-2.2）**空安全**查询：任务回调里核对"实体是否还在"时必须用它
		/// （查不到的 uid 返回 <c>null</c>，而不是抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>）。
		/// </summary>
		public IMapOccupant FindOccupantByUId(string mapId, string uid)
		{
			var map = _session.Get(mapId);
			return map != null && map.TryGetOccupantByUId(uid, out IMapOccupant occupant) ? occupant : null;
		}

		/// <summary>
		/// （v0.3 / WP-2.3）**唯一的人口写入点**（`CON-05`）：走 <see cref="Map.AddPopulationWithin"/> 的
		/// 「范围内总计上限」口径，并在真正变化时把地图标记为脏 —— 否则人口改动不会被下一个存档点写盘
		/// （这是"人口不落盘"的一半；另一半是实体序列化，归 `WP-3.2`）。
		/// </summary>
		/// <returns>实际增加的人数。</returns>
		public int AddPopulation(string mapId, HexCubePosition center, int radius, int cap, int amount)
		{
			var map = _session.Get(mapId);
			if (map == null) return 0;

			int added = map.AddPopulationWithin(center, radius, cap, amount);
			if (added > 0) _session.MarkDirty(mapId);
			return added;
		}

		/// <summary>（v0.3 / WP-2.3）范围内人口总计（只读；供断言与未来的 UI/结算器使用）。</summary>
		public int GetPopulationWithin(string mapId, HexCubePosition center, int radius)
		{
			var map = _session.Get(mapId);
			return map?.GetPopulationWithin(center, radius) ?? 0;
		}

		/// <summary>
		/// （v0.3 / WP-3.9 / `TIME-14`）**全图人口总计**（只读；月度结算器收"人口维护费"用）。
		/// <para>地图不存在时返回 0 而不是抛（结算跑在日边界上，抛异常会打断整批派发）。</para>
		/// </summary>
		public int GetTotalPopulation(string mapId)
		{
			var map = _session.Get(mapId);
			return map?.GetAllCells().Sum(cell => cell.Population) ?? 0;
		}

		/// <summary>
		/// （v0.3 / WP-3.9）本图**全部占据物**（地图不存在 → 空集合，不抛）。
		/// <para>占用权威（`WP-3.4`）保证 `cell.Occupant` 与 uid 索引一致，因此"按格取占据物"就是全量视图；
		/// 经济结算需要它来统计"图上有哪些单位"（单位维护费），但**返回值是 <see cref="IMapOccupant"/>** ——
		/// 地图模块不必认识 Units 模块的类型，判定留给消费方。</para>
		/// <para>（v0.3 / WP-3.6）**交战中的进攻方也计入**：攻进敌格的单位仍然是"图上存在、仍在吃粮"的单位，
		/// 漏掉它会让单位维护费少算（它在 `Invader` 槽位而不是 `Occupant`）。</para>
		/// </summary>
		public IEnumerable<IMapOccupant> GetOccupants(string mapId)
		{
			var map = _session.Get(mapId);
			if (map == null) return Enumerable.Empty<IMapOccupant>();

			return map.GetAllCells()
				.SelectMany(cell => new[] { cell.Occupant, cell.Invader })
				.Where(occupant => occupant != null);
		}

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`）该格是否**正有一对单位交战**（被挑战方 + 进攻方）。
		/// <para>与 <see cref="IsClear"/> 的关系：交战中该格**不是**空地（`IsClear` 看占据物槽位），
		/// 建造/移动的封锁照旧；"一对"是这条规则的可读判据。</para>
		/// </summary>
		public bool IsEngagedAt(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map != null && map.IsEngagedAt(position);
		}

		/// <summary>（v0.3 / WP-3.6）该格的**进攻方**（无交战时 null）。</summary>
		public IMapOccupant GetInvader(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map?.GetInvader(position);
		}

		/// <summary>（v0.3 / WP-3.6）该格的占据物对象（槽位权威；空格/地图不存在 → null）。</summary>
		public IMapOccupant GetOccupantAt(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map?.GetOccupantAt(position);
		}

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`）**占据物进入交战的唯一入口**（[`Map.BeginEngagement`]）：进攻方挪进目标格、
		/// 落进该格的 `Invader` 槽位，被挑战方仍是占据物。
		/// <para>成功即打脏标记：交战状态随地图存档保留（否则读档后"打了一半的仗"会凭空消失）。</para>
		/// </summary>
		/// <returns>是否进入成功。</returns>
		public bool BeginEngagement(string mapId, IMapOccupant invader, HexCubePosition from, HexCubePosition to)
		{
			var map = _session.Get(mapId);
			if (map == null) return false;

			if (!map.BeginEngagement(invader, from, to)) return false;

			_session.MarkDirty(mapId);
			return true;
		}

		/// <summary>（v0.3 / WP-3.6）**退出交战**（进攻方离场）：返回退出的进攻方对象。</summary>
		public IMapOccupant EndEngagement(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			if (map == null) return null;

			IMapOccupant invader = map.EndEngagement(position);
			if (invader != null) _session.MarkDirty(mapId);

			return invader;
		}

		/// <summary>
		/// （v0.3 / WP-2.5）扣人口（训练完成时的"人口 −1"，`E2`）：走 <see cref="Map.ConsumePopulationWithin"/>，
		/// 有变化即打脏标记（与 <see cref="AddPopulation"/> 同一套"唯一写入点 + 存档点"约定）。
		/// </summary>
		/// <returns>实际扣除的人数。</returns>
		public int ConsumePopulation(string mapId, HexCubePosition center, int radius, int amount)
		{
			var map = _session.Get(mapId);
			if (map == null) return 0;

			int consumed = map.ConsumePopulationWithin(center, radius, amount);
			if (consumed > 0) _session.MarkDirty(mapId);
			return consumed;
		}

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**整图人口总计**（<see cref="IPopulationSink"/>）：
		/// 与 <see cref="GetTotalPopulation"/> 同一口径，只是按契约名字暴露给经济侧（月度结算器算减员基数）。
		/// </summary>
		public int GetPopulation(string mapId) => GetTotalPopulation(mapId);

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**实施一次人口减员**（<see cref="IPopulationSink"/>）：
		/// 按地图种子派生随机源 → 走 <see cref="Map.ApplyPopulationLoss"/> 按地块随机扣人 → 有变化即标脏
		/// （与 <see cref="AddPopulation"/> / <see cref="ConsumePopulation"/> 同一套"唯一写入点 + 存档点"约定，
		/// 否则"读档一次减员全复原"）。
		/// </summary>
		/// <returns>实际减少的人数。</returns>
		public int ApplyPopulationLoss(string mapId, int amount)
		{
			var map = _session.Get(mapId);
			if (map == null || amount <= 0) return 0;

			// 种子混入调用序号（质数步进）：同一年里多次减员不该总是从同一格开始扣
			IRandom random = _randomFactory(map.seed + _lossCallCount * 7919) ?? new SystemRandom(map.seed);
			_lossCallCount++;

			int lost = map.ApplyPopulationLoss(amount, random);
			if (lost > 0) _session.MarkDirty(mapId);
			return lost;
		}

		/// <summary>
		/// （v0.3 / WP-3.4）**放置建筑**：同时写 `cell.Building` 与占据物槽位（修 `MAP-04`：旧实现只写后者，
		/// 于是 `GetBuildingInfo`/拆除全部失效）。目标格已被别的实体占用时拒绝。
		/// </summary>
		public bool PlaceBuilding(string mapId, HexCubePosition position, IMapOccupant building)
		{
			var map = _session.Get(mapId);
			if (map == null) return false;

			if (!map.PlaceBuilding(building, position)) return false;

			_session.MarkDirty(mapId);
			return true;
		}

		public void SetOccupant(string MapId, HexCubePosition position, IMapOccupant occupant)
		{
			var map = _session.Get(MapId);
			map.AddOccupant(occupant, position);
			_session.MarkDirty(MapId);
		}

		public TerrainRequirement GetTerrainRequirement(string mapId, HexCubePosition position, string targetTerrain)
		{
			var map = _session.Get(mapId);
			return new TerrainRequirement(map, position, targetTerrain);
		}

		public void RemoveOccupantByPosition(string mapId, HexCubePosition position, IMapOccupant occupant)
		{
			var map = _session.Get(mapId);
			map.RemoveOccupantByPosition(position);
			_session.MarkDirty(mapId);
		}

		public void RemoveBuilding(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			map.RemoveBuilding(position);
			_session.MarkDirty(mapId);
		}

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）**占据物移动的唯一入口**：位移后格子/索引/建筑槽位一致
		/// （修"移动只改 `Unit.Position` → 旧格留着僵尸占据物"）。
		/// </summary>
		/// <returns>是否移动成功（目标被别的占据物占着则拒绝，调用方应停下而不是硬挤）。</returns>
		public bool MoveOccupant(string mapId, IMapOccupant occupant, HexCubePosition from, HexCubePosition to)
		{
			var map = _session.Get(mapId);
			if (map == null) return false;
			if (!map.MoveOccupant(occupant, from, to)) return false;

			_session.MarkDirty(mapId);
			return true;
		}

		public MapOccupantInfo? GetBuildingInfo(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map.GetBuildingInfo(position);
		}

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）该格是否被**敌方单位**封锁（不可进入 / 不可建造 / 不可采集）。
		/// <para>与 <see cref="IsClear"/> 的关系：`IsClear` 是"有没有占据物"（占用的机制），本方法是设计稿
		/// 「地块封锁」那条规则的可读判据（敌人存活才封锁；友方/自己占据不算"封锁"）。</para>
		/// </summary>
		public bool IsHostileAt(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			return map != null && map.IsHostileAt(position);
		}

		/// <summary>（v0.3 / WP-3.8）把「该格没有被敌方封锁」表达成 <see cref="IRequirement"/>（与地形前提同一族）。</summary>
		public NoHostileRequirement GetNoHostileRequirement(string mapId, HexCubePosition position)
			=> new NoHostileRequirement(_session.Get(mapId), position);

		public void SetInvader(string mapId, HexCubePosition position, IMapOccupant invader)
		{
			var map = _session.Get(mapId);
			map.SetInvader(position, invader);
			_session.MarkDirty(mapId);
		}

		public void RemoveInvader(string mapId, HexCubePosition position)
		{
			var map = _session.Get(mapId);
			map.RemoveInvader(position);
			_session.MarkDirty(mapId);
		}

		public IConsumable CreatePopulationConsumption(string mapId, HexCubePosition position, int amount)
		{
			var map = _session.Get(mapId);
			var cell = map.GetCell(position);
			return new PopulationConsumption(cell, amount);
		}

		public List<HexCubePosition> FindPath(string mapId, HexCubePosition start, HexCubePosition end, FogAppService fog)
		{
			var map = _session.Get(mapId);

			if (start == end)
				return new List<HexCubePosition> { start };

			var openSet = new PriorityQueue<HexCubePosition, float>();
			var gScore = new Dictionary<HexCubePosition, float>();
			var cameFrom = new Dictionary<HexCubePosition, HexCubePosition>();

			gScore[start] = 0f;
			openSet.Enqueue(start, start.DistenceTo(end));

			// v0.3 / WP-3.5：**搜索步数上限**（格子数 × 16）。A* 在"目标不可达 + 8 邻域图"这类组合下
			// 可能反复入队同一节点（启发式不一致时），而在日边界上挂死会直接冻结整个模拟 —— 
			// 宁可返回"没有路径"，也不能让一次寻路吃掉无限时间。
			int steps = 0;
			int maxSteps = System.Linq.Enumerable.Count(map.GetAllCells()) * 16 + 64;

			while (openSet.Count > 0 && steps++ < maxSteps)
			{
				var current = openSet.Dequeue();
				if (current == end)
					return ReconstructPath(cameFrom, current);

				foreach (var neighbor in current.GetNeighbor())
				{
					if (!IsPassable(map, neighbor, fog))
						continue;

					float moveCost = GetMoveCost(map, neighbor);
					float tentativeG = (gScore.TryGetValue(current, out float g) ? g : float.MaxValue) + moveCost;

					if (!gScore.TryGetValue(neighbor, out float neighborG) || tentativeG < neighborG)
					{
						gScore[neighbor] = tentativeG;
						cameFrom[neighbor] = current;
						float fScore = tentativeG + neighbor.DistenceTo(end);
						openSet.Enqueue(neighbor, fScore);
					}
				}
			}

			return new List<HexCubePosition>();
		}

		private static bool IsPassable(Domain.Map map, HexCubePosition pos, FogAppService fog)
		{
			// v0.3 / WP-3.8：地图外的格子不可通行（旧实现把越界格当可通行 → 路径可能"走出地图"，
			// 而 Tick 的 CanEnter/TerrainCost 拿到不存在的格会炸）
			if (!map.TryGetCell(pos, out MapCell cell))
				return false;

			byte visibility = fog.GetVisibility(pos);
			if (visibility == FogUnexplored)
				return true;

			if (!(cell.Terrain != null && cell.Terrain.Passable))
				return false;

			// v0.3 / WP-3.8（`B8`）：敌方单位占据的格子**封锁**（必须先击败敌人才能进入）——
			// 寻路与位移用同一套判据（`UnitMovementService.CanEnter`），否则会出现"路径指着敌人的格子"的假可达
			return !map.IsHostileAt(pos);
		}

		private static float GetMoveCost(Domain.Map map, HexCubePosition pos)
		{
			if (!map.TryGetCell(pos, out MapCell cell))
				return 1f;

			return cell.Terrain?.MoveCost ?? 1f;
		}

		private static List<HexCubePosition> ReconstructPath(Dictionary<HexCubePosition, HexCubePosition> cameFrom, HexCubePosition current)
		{
			var path = new List<HexCubePosition> { current };
			while (cameFrom.TryGetValue(path[0], out var prev))
			{
				path.Insert(0, prev);
			}
			return path;
		}
	}
}