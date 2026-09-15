using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Application
{
	/// <summary>
	/// （v0.8.8 / `WP-4.17`）**人口模型（聚落级容量）**：设计稿口径是"**多住房不叠加**" ——
	/// 两座相邻营地共享的那片地只能算一次容量，而不是各算各的（旧实现每座营地各自按 `PopulationCap`
	/// 增长 ⇒ 重叠区域凭空多出一份人口上限）。
	/// <list type="number">
	/// <item>**容量** = 按 `PopulationCap` **降序**逐座住房处理，每座只对"**尚未被更大住房覆盖**的格子"
	/// 贡献容量（贪心去重；小房子完全落在大房子范围内 ⇒ 一点容量都不加）；</item>
	/// <item>**拆住房**：订阅 <see cref="BuildingRemovedEvent"/>，把该住房覆盖范围内的人口**压到重算后的容量**
	/// （多出来的人按确定顺序减员）—— 身在**其它聚落范围内**的人不受影响，这在观感上就是"迁走了"。</item>
	/// </list>
	/// <para>与 `WP-4.6` 驻扎同一条纪律：**不落新状态**，容量每次现算（拆房/升级后立刻正确）。</para>
	/// </summary>
	public sealed class PopulationModelService
	{
		private readonly MapAppService _map;
		private readonly IBuildingConfigRepository _buildingRepo;
		private readonly IDomainEventBus _events;

		public PopulationModelService(MapAppService map, IBuildingConfigRepository buildingRepo, IDomainEventBus events = null)
		{
			_map = map;
			_buildingRepo = buildingRepo;
			_events = events;

			// 压人口的触发点在**拆除路径**（`ConstructionAppService.RemoveBuildingByPosition`）：
			// `BuildingRemovedEvent` 不带位置（`D114`），事件侧无法知道该压哪片地。
		}

		/// <summary>被拆房减员影响过的次数（验收/调试）。</summary>
		public int TrimCount { get; private set; }

		/// <summary>
		/// **聚落级容量**：以 <paramref name="center"/> 为观察点，返回该处**有效**人口上限 ——
		/// **覆盖该点的住房取最大者**（"多住房不叠加"：两座营地摞在一起也只是 9 人上限，不是 18）。
		/// <para>没有任何住房覆盖该点 ⇒ 0（拆掉唯一住房后，那片地的人口会被压到 0 而减员）。</para>
		/// </summary>
		public int CapacityAt(string mapId, int ownerId, HexCubePosition center)
		{
			int capacity = 0;
			foreach (IMapOccupant building in OwnHousing(mapId, ownerId))
			{
				IBuildingConfig config = _buildingRepo.GetBuildingConfig(building.GetInfo().Id);
				if (config == null || config.PopulationCap <= capacity) continue;

				int radius = Math.Max(1, config.PopulationRadius);
				if (building.GetInfo().Position.DistenceTo(center) > radius) continue;   // 不住在这片地

				capacity = config.PopulationCap;   // 覆盖该点的住房里取最大（不叠加）
			}
			return capacity;
		}

		/// <summary>拆住房：把该住房覆盖范围内的人口压到重算后的容量（多出来的人减员）。</summary>
		public int TrimAfterHousingLost(string mapId, int ownerId, HexCubePosition center, int radius)
		{
			int capacity = CapacityAt(mapId, ownerId, center);
			int population = _map.GetPopulationWithin(mapId, center, Math.Max(1, radius));
			if (population <= capacity) return 0;

			int excess = population - capacity;
			int removed = ConsumePopulation(mapId, center, Math.Max(1, radius), excess);
			if (removed > 0) TrimCount++;
			return removed;
		}


		private List<IMapOccupant> OwnHousing(string mapId, int ownerId)
			=> _map.GetOccupants(mapId)
				   .Where(o => o != null && o.GetInfo().OwnerId == ownerId
							   && o.GetInfo().Type == OccupantType.Building && o.IsReady
							   && (_buildingRepo.GetBuildingConfig(o.GetInfo().Id)?.IsHousing ?? false))
				   .ToList();

		private int Cap(IMapOccupant building)
			=> _buildingRepo.GetBuildingConfig(building.GetInfo().Id)?.PopulationCap ?? 0;

		private int ConsumePopulation(string mapId, HexCubePosition center, int radius, int amount)
		{
			int left = amount;
			foreach (HexCubePosition position in center.InRadius(radius)
						 .OrderBy(p => p.DistenceTo(center)).ThenBy(p => p.q).ThenBy(p => p.r))
			{
				if (left <= 0) break;

				MapCell cell = _map.GetMapCell(mapId, position);
				if (cell == null || cell.Population <= 0) continue;

				int take = Math.Min(cell.Population, left);
				cell.SetPopulation(cell.Population - take);
				left -= take;
			}
			return amount - left;
		}
	}
}
