using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Domain
{
	// This is the aggregate root of map domain
	public class Map
	{
		// aggregate of mapcell
		private Dictionary<HexCubePosition, MapCell> _cells;

		//dict of all occupants
		private Dictionary<string, IMapOccupant> _occupants = new();

		public int seed;
		public int width, height;
		public readonly string Id;

		public Map(int seed, int width, int height, string Id)
		{
			this.seed = seed;
			this.width = width;
			this.height = height;
			this.Id = Id;

			this._cells = [];
		}

		public void SetCell(HexCubePosition pos, MapCell cell)
		{
			_cells[pos] = cell;
		}

		public void SetTerrain(HexCubePosition position, ITerrainData terrain)
		{
			if (!_cells.TryGetValue(position, out _))
				return;
			ITerrainData old = _cells[position].Terrain;
			_cells[position].SetTerrain(terrain);
		}

		/// <summary>
		/// （v0.3 / WP-3.4）**占据物进入的唯一入口**：写入 `cell.Occupant` 与 `_occupants` 索引，
		/// 并在目标格已被占用时**拒绝**（旧实现直接覆盖 → 一格两实体、索引与格子不一致）。
		/// </summary>
		/// <returns>是否放置成功。</returns>
		public bool PlaceOccupant(IMapOccupant occupant, HexCubePosition position)
		{
			if (occupant == null) return false;
			if (!_cells.TryGetValue(position, out MapCell cell)) return false;
			if (cell.Occupant != null) return false; // 一格一占据物（`MAP-03`：不再静默覆盖）

			cell.SetOccupant(occupant);
			_occupants[occupant.GetInfo().UId] = occupant;

			// （v0.7.0 / WP-4.8 / `D73`）站上一栋"可夺取"的敌方建筑 ⇒ 易主（训练完成落位也走这里）
			TryCaptureAt(cell, occupant);
			return true;
		}

		/// <summary>
		/// （v0.8.8 / `WP-4.9`）**附属建筑进入的入口**：与 <see cref="PlaceBuilding"/> 的区别是**不动
		/// `cell.Building`**（宿主留在原位），只把附属建筑挂到宿主的 `Attachments` 与本格的 `Attachments`，
		/// 并进 `_occupants` 索引（产出/迷雾/AI 与 uid 查询都能看到它）。
		/// </summary>
		public bool PlaceAttachment(IMapOccupant attachment, HexCubePosition position)
		{
			if (attachment == null) return false;
			if (!_cells.TryGetValue(position, out MapCell cell)) return false;
			if (cell.Building is not Building host) return false;

			if (attachment is Building building) building.HostUId = host.GetInfo().UId;

			host.Attachments.Add(attachment);
			cell.Attachments.Add(attachment);
			_occupants[attachment.GetInfo().UId] = attachment;
			return true;
		}

		/// <summary>（v0.8.8 / `WP-4.9`）移除附属建筑（拆宿主时一并清，避免僵尸索引）。</summary>
		public IMapOccupant RemoveAttachment(HexCubePosition position, string uid)
		{
			if (!_cells.TryGetValue(position, out MapCell cell)) return null;

			IMapOccupant removed = cell.Attachments.FirstOrDefault(a => a.GetInfo().UId == uid);
			if (removed == null) return null;

			cell.Attachments.Remove(removed);
			if (cell.Building is Building host) host.Attachments.RemoveAll(a => a.GetInfo().UId == uid);
			_occupants.Remove(uid);
			return removed;
		}

		/// <summary>（v0.8.8 / `WP-4.9`）清掉某格的**全部附属建筑**（宿主被拆时调用）：返回被清掉的 uid 列表。</summary>
		public List<string> ClearAttachments(HexCubePosition position)
		{
			var cleared = new List<string>();
			if (!_cells.TryGetValue(position, out MapCell cell)) return cleared;

			foreach (IMapOccupant attachment in cell.Attachments.ToList())
			{
				string uid = attachment?.GetInfo().UId;
				if (!string.IsNullOrEmpty(uid))
				{
					_occupants.Remove(uid);
					cleared.Add(uid);
				}
			}
			cell.Attachments.Clear();
			if (cell.Building is Building host) host.Attachments.Clear();
			return cleared;
		}

		/// <summary>
		/// （v0.3 / WP-3.4）**建筑进入的唯一入口**：同时写 `cell.Building` 与占据物槽位。
		/// <para>旧实现只有建造路径写 `cell.Occupant`、`cell.Building` 恒空（`MAP-04`）→ `GetBuildingInfo` 查不到、
		/// 拆除整体失效；统一入口后两者从落位那一刻起就一致。</para>
		/// </summary>
		public bool PlaceBuilding(IMapOccupant building, HexCubePosition position)
		{
			if (building == null) return false;
			if (!_cells.TryGetValue(position, out MapCell cell)) return false;
			if (cell.Occupant != null && !ReferenceEquals(cell.Occupant, building)) return false;

			cell.SetBuilding(building);
			cell.SetOccupant(building);
			_occupants[building.GetInfo().UId] = building;
			return true;
		}

		/// <summary>
		/// （v0.3 / WP-3.4）**占据物离开的唯一入口**：格子、建筑槽位、`_occupants` 索引三处一起清。
		/// <para>旧实现只清 `cell.Occupant` → `_occupants` 里留下**僵尸索引**（`MAP-03`/`UNIT-05`）：
		/// 已阵亡的单位仍能按 uid 查到，任务/战斗回调于是对着尸体干活。</para>
		/// <para>（v0.3 / WP-3.6 / `E9`）**收尾时把入侵者顶上位**：被挑战方阵亡/被移除后，
		/// 同格那一对交战中幸存的进攻方接管该格（design/unit.md「必须先击败敌人才能进入该地块」）。
		/// 放在这里而不是让战斗服务自己补一步：任何移除路径（阵亡、拆除、脚本）都不会留下
		/// "格子里有人、占据物槽位却是空的"悬空态（那会让 `IsClear` 误判为可建造）。</para>
		/// </summary>
		/// <returns>被移除的占据物（无则 null）。</returns>
		public IMapOccupant RemoveOccupant(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out MapCell cell)) return null;

			IMapOccupant occupant = cell.Occupant;
			if (occupant == null) return null;

			string uid = occupant.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants.Remove(uid);
			if (ReferenceEquals(cell.Building, occupant)) cell.RemoveBuilding();
			cell.RemoveOccupant();

			PromoteInvaderAt(cell);
			return occupant;
		}

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`）**格内是否有一对正在交战的单位**：`cell.Occupant`（被挑战方）+
		/// `cell.Invader`（进攻方）同时存在（`D56`：`Invader` 槽位就是留给这条规则的）。
		/// </summary>
		public bool IsEngagedAt(HexCubePosition position)
			=> _cells.TryGetValue(position, out MapCell cell) && cell.Invader != null;

		/// <summary>（v0.3 / WP-3.6）该格的**进攻方**（无交战时为 null）。</summary>
		public IMapOccupant GetInvader(HexCubePosition position)
			=> _cells.TryGetValue(position, out MapCell cell) ? cell.Invader : null;

		/// <summary>（v0.3 / WP-3.6）该格的占据物（槽位权威；空格/地图外返回 null）。</summary>
		public IMapOccupant GetOccupantAt(HexCubePosition position)
			=> _cells.TryGetValue(position, out MapCell cell) ? cell.Occupant : null;

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`）**占据物进入交战的唯一入口**：进攻方从 <paramref name="from"/> 挪进
		/// <paramref name="to"/>，落进该格的 `Invader` 槽位（被挑战方仍是 `Occupant`）。
		/// <list type="number">
		/// <item>目标格必须**已有一个占据物**（被挑战方）——"攻打空地"不是交战，走移动/建造；</item>
		/// <item>该格**只能有一对**：已有 `Invader` 时拒绝（design/unit.md「每个地块中只能有一个单位或一对正在交战的单位」）；</item>
		/// <item>`from` 与位移一样从原格摘除，并保持 `_occupants` 索引指向进攻方（任务/存档/查询都靠它）。</item>
		/// </list>
		/// <para>**为什么不改 `MapCell` 结构**：一格一个 `Occupant` 的权威已经修好（`WP-3.4`），
		/// 而 `Invader` 槽位从早期版本就存在、一直空着 —— 用它表达"一对"是最小改动，
		/// "格内多占据物"（`WP-4.9`）到来时再统一成集合。</para>
		/// </summary>
		/// <returns>是否进入交战成功（false = 目标不合法 / 该格已有一对）。</returns>
		public bool BeginEngagement(IMapOccupant invader, HexCubePosition from, HexCubePosition to)
		{
			if (invader == null) return false;
			if (!_cells.TryGetValue(to, out MapCell target)) return false;
			if (target.Occupant == null) return false;
			if (ReferenceEquals(target.Occupant, invader)) return false;
			if (target.Invader != null) return false; // 一格一对

			if (from != to && _cells.TryGetValue(from, out MapCell origin) && ReferenceEquals(origin.Occupant, invader))
				origin.RemoveOccupant(); // 与 `MoveOccupant` 同一约定：离开原格

			target.SetInvader(invader);

			string uid = invader.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants[uid] = invader;

			return true;
		}

		/// <summary>
		/// （v0.3 / WP-3.6）**退出交战**：进攻方离开该格（阵亡/停战/换格），被挑战方不受影响。
		/// <para>注意方向：**被挑战方**阵亡不走这里 —— 它在 <see cref="RemoveOccupant"/> 里被摘除后
		/// 由 <see cref="PromoteInvaderAt"/> 把进攻方顶上位。</para>
		/// </summary>
		/// <returns>退出的进攻方（本就没有交战时为 null）。</returns>
		public IMapOccupant EndEngagement(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out MapCell cell)) return null;

			IMapOccupant invader = cell.Invader;
			cell.RemoveInvader();
			if (invader == null) return null;

			string uid = invader.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants.Remove(uid);

			return invader;
		}

		/// <summary>（v0.3 / WP-3.6）把该格的进攻方顶成占据物（被挑战方已离场时的收尾；返回是否发生了顶替）。</summary>
		private bool PromoteInvaderAt(MapCell cell)
		{
			IMapOccupant invader = cell?.Invader;
			if (invader == null) return false;
			if (cell.Occupant != null) return false; // 该格已有占据物（正常不会走到：一格一对）

			cell.RemoveInvader();
			cell.SetOccupant(invader);

			string uid = invader.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants[uid] = invader;

			return true;
		}

		/// <summary>（v0.3 / WP-3.4）**建筑离开的唯一入口**：清 `cell.Building` + 占据物槽位 + 索引。</summary>
		/// <returns>被移除的建筑（无则 null）。</returns>
		public IMapOccupant RemoveBuildingAt(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out MapCell cell)) return null;

			IMapOccupant building = cell.Building;
			cell.RemoveBuilding();
			if (building == null) return null;

			string uid = building.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants.Remove(uid);
			if (ReferenceEquals(cell.Occupant, building)) cell.RemoveOccupant();
			return building;
		}

		/// <summary>
		/// （v0.7.0 / WP-4.8 / `D73`）**对建筑造成伤害的唯一入口**：扣血；HP 归零 ⇒ 把建筑从占据物槽位摘下
		/// （格子因此变成**可进入**），但建筑仍在 `cell.Building` 与 `_occupants` 索引里 —— 它还是原主人的资产，
		/// 只是"等着被占"。
		/// <para>为什么摘的是占据物槽位而不是删掉建筑：设计稿的夺取口径是"**HP 归零不自动易主**，
		/// 我方单位**站上该格**才易主" —— 若不摘掉槽位，格子永远是"被占"的，单位永远上不去。</para>
		/// </summary>
		/// <returns>是否命中了一个有 HP 的建筑（无 HP 的建筑返回 false：调用方沿用"只记账"的旧口径）。</returns>
		public bool ApplyBuildingDamage(HexCubePosition position, float damage)
		{
			if (!_cells.TryGetValue(position, out MapCell cell)) return false;
			if (cell.Building is not IDamageable building || !building.HasHP) return false;

			building.TakeDamage(damage);

			// 归零 ⇒ 转"可夺取"：摘下占据物槽位（建筑槽位与索引保留）
			if (building.IsCapturable && ReferenceEquals(cell.Occupant, cell.Building))
				cell.RemoveOccupant();

			return true;
		}

		/// <summary>
		/// （v0.7.0 / WP-4.8 / `D73`）**夺取**：单位进入某格时，若该格立着一栋**可夺取**的敌方建筑，
		/// 则归属改为进入者的主人，HP 恢复到半血。
		/// </summary>
		/// <returns>被夺取的建筑（未发生夺取返回 null）。</returns>
		private IMapOccupant TryCaptureAt(MapCell cell, IMapOccupant incoming)
		{
			if (cell.Building is not IDamageable captive || !captive.IsCapturable) return null;
			if (incoming == null || incoming.GetInfo().Type != OccupantType.Unit) return null; // 只有单位能占，建筑之间不互相夺取

			int newOwner = incoming.GetInfo().OwnerId;
			if (newOwner == cell.Building.GetInfo().OwnerId) return null; // 自家单位站上去不是"夺取"

			captive.CaptureBy(newOwner);
			return cell.Building;
		}

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）**占据物移动的唯一入口**：从 <paramref name="from"/> 摘除、落到
		/// <paramref name="to"/>（目标被**别的**占据物占着则拒绝且保持原状 —— 一格一占据物）。
		/// <para>旧实现的移动只改 <c>Unit.Position</c>，地图索引与格子仍指着**旧格**（僵尸占据物）：
		/// 于是"下一格有没有敌人""同格交战"这类判定全部失真。移动与落位现在共用同一套占用权威。</para>
		/// <para>（v0.7.0 / WP-4.8）落位成功后检查**夺取**：站上一栋 HP 归零的敌方建筑 ⇒ 易主。</para>
		/// </summary>
		/// <returns>是否移动成功。</returns>
		public bool MoveOccupant(IMapOccupant occupant, HexCubePosition from, HexCubePosition to)
		{
			if (occupant == null) return false;
			if (!_cells.TryGetValue(to, out MapCell target)) return false;
			if (target.Occupant != null && !ReferenceEquals(target.Occupant, occupant)) return false;

			if (from != to && _cells.TryGetValue(from, out MapCell origin) && ReferenceEquals(origin.Occupant, occupant))
				origin.RemoveOccupant();

			target.SetOccupant(occupant);

			string uid = occupant.GetInfo().UId;
			if (!string.IsNullOrEmpty(uid)) _occupants[uid] = occupant;

			TryCaptureAt(target, occupant);
			return true;
		}

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）该格是否被**敌方单位**占据 —— 封锁判定（不可进入 / 不可建造 / 不可采集）的
		/// 唯一查询入口。
		/// <para>只看 <see cref="MapOccupantInfo.IsHostile"/>（由占据物自己声明），因此地图模块不需要认识
		/// Units 模块的类型；不存在的地形格视为"无敌方"。</para>
		/// </summary>
		public bool IsHostileAt(HexCubePosition position)
			=> _cells.TryGetValue(position, out MapCell cell) && cell.Occupant != null && cell.Occupant.GetInfo().IsHostile;

		public MapCell GetCell(HexCubePosition position)
		{
			return _cells[position];
		}

		public IEnumerable<MapCell> GetAllCells()
		{
			return _cells.Values;
		}

		public bool TryGetCell(HexCubePosition position, out MapCell cell)
		{
			return _cells.TryGetValue(position, out cell);
		}

		public ITerrainData GetTerrain(HexCubePosition position)
		{
			if (!_cells.TryGetValue(position, out _))
				return null;
			return _cells[position].Terrain;
		}

		public MapOccupantInfo? GetOccupantInfo(HexCubePosition position)
		{
			if(_cells.TryGetValue(position, out MapCell cell))
				if (cell.Occupant!=null)
					return cell.Occupant.GetInfo();
			return null;
		}

		public IMapOccupant GetOccupantByUId(string uid)
		{
			return _occupants[uid];
		}	

		/// <summary>
		/// （v0.3 / WP-2.2）**空安全**的按 uid 查询：给"任务回调里核对实体是否还在"用
		/// （攻击/移动循环在宿主消失后要自行注销，不能因为查不到就抛异常把整个日派发打断）。
		/// <para>注意：这只是**查询**侧的安全网，不改占用权威 —— "僵尸索引"（移除后仍留在 `_occupants` 里）
		/// 由 `WP-3.4` 统一修（`MAP-03` / `UNIT-05`）。</para>
		/// </summary>
		public bool TryGetOccupantByUId(string uid, out IMapOccupant occupant)
		{
			if (string.IsNullOrEmpty(uid))
			{
				occupant = null;
				return false;
			}
			return _occupants.TryGetValue(uid, out occupant);
		}

		/// <summary>
		/// （v0.3 / WP-2.5）按**范围内总计**扣人口（与 <see cref="AddPopulationWithin"/> 同一口径的反向操作）：
		/// 先扣中心格，不足再按半径展开顺序扣其余格；扣到 0 为止（不会出现负人口）。
		/// <para>训练一个单位完成时消耗人口（`E2`）走这里；未来的"赤字减员"（`WP-3.10`）同样复用。</para>
		/// </summary>
		/// <returns>实际扣除的人数。</returns>
		public int ConsumePopulationWithin(HexCubePosition center, int radius, int amount)
		{
			if (amount <= 0) return 0;

			int remaining = amount;
			foreach (HexCubePosition pos in center.InRadius(radius))
			{
				if (remaining <= 0) break;
				if (!_cells.TryGetValue(pos, out MapCell cell) || cell.Population <= 0) continue;

				int taken = Math.Min(cell.Population, remaining);
				cell.SetPopulation(cell.Population - taken);
				remaining -= taken;
			}
			return amount - remaining;
		}

		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**随机减少人口**：把 <paramref name="amount"/> 人**按地块随机**扣掉
		/// （每次随机挑一个还有人的格子扣 1 人，扣空的格子退出候选）。
		/// <para>为什么不是"从中心格开始扣"（<see cref="ConsumePopulationWithin"/> 的口径）：那条口径服务的是
		/// 训练消耗（`E2`，从训练建筑所在聚落取人）；减员的语义是"饥荒饿死人"，不该永远先死在聚落中心 ——
		/// 设计口径是"**按地块随机**减员"（log §9.2 `C9`），随机分布才能让玩家在地图上看到人口散着掉。</para>
		/// <para>候选先按 (q,r) 显式排序再按 <paramref name="random"/> 抽索引 ⇒ 同一随机源必得同一结果
		/// （确定性的一半；另一半由调用方给的随机源决定）。</para>
		/// </summary>
		/// <returns>实际扣除的人数（人口不足时少于 <paramref name="amount"/>，不会出现负人口）。</returns>
		public int ApplyPopulationLoss(int amount, IRandom random)
		{
			if (amount <= 0 || random == null) return 0;

			List<MapCell> candidates = GetAllCells()
				.Where(cell => cell.Population > 0)
				.OrderBy(cell => cell.Position.q)
				.ThenBy(cell => cell.Position.r)
				.ToList();

			int lost = 0;
			while (lost < amount && candidates.Count > 0)
			{
				int index = random.Next(0, candidates.Count);
				MapCell cell = candidates[index];

				cell.SetPopulation(cell.Population - 1);
				lost++;

				if (cell.Population <= 0) candidates.RemoveAt(index); // 扣空的格子不再参与抽签
			}
			return lost;
		}

		/// <summary>（v0.3 / WP-2.3）范围内人口**总计**（含中心格）。</summary>
		public int GetPopulationWithin(HexCubePosition center, int radius)
		{
			int total = 0;
			foreach (HexCubePosition pos in center.InRadius(radius))
				if (_cells.TryGetValue(pos, out MapCell cell))
					total += cell.Population;

			return total;
		}

		/// <summary>
		/// （v0.3 / WP-2.3）按**范围内总计上限**增加人口（设计稿：营地「半径 1 格内总计 9 人」）。
		/// <para>旧实现是「每格各自到 cap」：半径 1 的 7 格各自可达 9 → 实际容量 63，与设计稿不符（`CON-05`）。
		/// 增长落在**中心格**（聚落中心 = 住房所在格）：设计稿描述的是聚落级总量，不是逐格配额。</para>
		/// </summary>
		/// <returns>实际增加的人数（0 = 已达上限或地块不存在）。</returns>
		public int AddPopulationWithin(HexCubePosition center, int radius, int cap, int amount)
		{
			if (amount <= 0 || cap <= 0) return 0;
			if (!_cells.TryGetValue(center, out MapCell cell)) return 0;

			int room = cap - GetPopulationWithin(center, radius);
			if (room <= 0) return 0;

			int added = Math.Min(room, amount);
			cell.AddPopulation(added);
			return added;
		}

		public void AddOccupant(IMapOccupant occupant,HexCubePosition position)
		{
			// v0.3 / WP-3.4：统一走"进入的唯一入口"（含占用冲突拒绝与索引一致）
			PlaceOccupant(occupant, position);
		}

		public void RemoveOccupantByPosition(HexCubePosition position)
		{
			// v0.3 / WP-3.4：统一走"离开的唯一入口"（含 `_occupants` 索引清理，修僵尸索引）
			RemoveOccupant(position);
		}

		public void SetBuilding(HexCubePosition position, IMapOccupant building)
		{
			// v0.3 / WP-3.4：建筑落位统一走 PlaceBuilding（同时写 cell.Building 与占据物槽位）
			PlaceBuilding(building, position);
		}

		public void RemoveBuilding(HexCubePosition position)
		{
			RemoveBuildingAt(position);
		}

		public bool VerifyTerrain(HexCubePosition position, string targetTerrain)
		{
			if(_cells.TryGetValue(position,out _))
				if (_cells[position].Terrain.Id==targetTerrain)
					return true;
			return false;
		}

        public MapOccupantInfo? GetBuildingInfo(HexCubePosition position)
        {
			// （v0.3 / WP-2.7 验收时发现）与 GetOccupantInfo 对齐：格子上没有建筑时返回 null，
			// 而不是对着 null 调 GetInfo() 抛 NRE —— 任何"查询任意格"的调用方（UI / 建造校验 / 测试）都会踩到。
			if (_cells.TryGetValue(position, out MapCell cell) && cell.Building != null)
				return cell.Building.GetInfo();
			return null;
        }

		public void SetInvader(HexCubePosition position, IMapOccupant invader)
		{
			if (_cells.TryGetValue(position, out MapCell cell))
				cell.SetInvader(invader);
		}

		public void RemoveInvader(HexCubePosition position)
		{
			if (_cells.TryGetValue(position, out MapCell cell))
				cell.RemoveInvader();
		}
    }
}
