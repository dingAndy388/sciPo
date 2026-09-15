using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Map.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Construction.Application
{
	/// <summary>
	/// （v0.3 / WP-3.10 / `RES-01`）**建筑维护需求**：按"图上每栋玩家建筑 × 它的模板维护费"汇总
	/// （资源名与数量都来自建筑表的 <see cref="IBuildingConfig.Maintenance"/>）。
	/// <para>与单位维护（<c>UnitMaintenanceUpkeepDemandSource</c>）同构：维护费是**建筑模板字段**，
	/// "图上有哪些建筑"是 Construction 侧知道、经济侧不该知道的事实 —— 经济侧因此不必反向依赖 Map/Construction
	/// （`IUpkeepDemandSource` 是两者之间的唯一契约，`D59`）。</para>
	/// <para>口径：① 只收**本玩家**的建筑（别人的建筑记在别人账上）；② 敌方占据物不参与；
	/// ③ 建造中 / 升级中的建筑都计入（它已经在图上占着地、也得维护）；④ 表里没填维护费的建筑收 0（条目直接丢弃）。</para>
	/// <para>**当前配置里全部建筑留空** —— 设计稿只给了"单位维护"数值，建筑维护尚未定义；
	/// 本条来源的作用是"填表即生效"，同时让验收能断言"需求来源已挂上"（装配自检）。</para>
	/// </summary>
	public sealed class BuildingMaintenanceUpkeepDemandSource(MapAppService map, IBuildingConfigRepository buildings) : IUpkeepDemandSource
	{
		/// <summary>归因名前缀（每条需求的 `Source` = `building:camp` / `building:workshop` …）。</summary>
		public const string SourceName = "building";

		private readonly MapAppService _map = map;
		private readonly IBuildingConfigRepository _buildings = buildings;

		public string SourceId => SourceName;

		public IEnumerable<UpkeepDemand> CollectUpkeep(string mapId, int ownerId)
		{
			if (_map == null || _buildings == null) yield break;

			// ① 数一数本玩家的建筑（按模板分组）：地图的占据物视图是"建筑在哪"的唯一权威（`WP-3.4`）
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (IMapOccupant occupant in _map.GetOccupants(mapId))
			{
				if (occupant is not Building) continue;

				MapOccupantInfo info = occupant.GetInfo();
				if (info.IsHostile) continue;              // 敌方占据物不吃玩家的粮
				if (info.OwnerId != ownerId) continue;     // 别的玩家的建筑记在别人账上

				counts[info.Id] = counts.GetValueOrDefault(info.Id) + 1;
			}

			// ② 按模板把维护费汇总成需求（同一模板合成一条，报告里一眼能看出"是哪几栋在花钱"）
			foreach (KeyValuePair<string, int> group in counts.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
			{
				IBuildingConfig config = _buildings.GetBuildingConfig(group.Key);
				if (config?.Maintenance == null || config.Maintenance.Count == 0) continue;

				foreach (KeyValuePair<string, float> upkeep in config.Maintenance.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
				{
					if (upkeep.Value <= 0f || string.IsNullOrWhiteSpace(upkeep.Key)) continue;

					yield return new UpkeepDemand(
						upkeep.Key,
						upkeep.Value * group.Value,
						$"{SourceName}:{group.Key}",
						group.Value);
				}
			}
		}
	}
}
