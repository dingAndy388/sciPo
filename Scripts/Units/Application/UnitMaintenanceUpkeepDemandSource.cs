using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Units.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Units.Application
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `UNIT-19`、`C8`、设计稿漏项 L2）**单位维护需求**：
	/// 按"图上每个玩家单位 × 它的模板维护费"汇总（资源名与数量都来自单位表的 `Maintenance`）。
	/// <para>为什么在 Units 侧实现：维护费是**单位模板字段**，而"图上有哪些单位"是 Units 知道、经济侧不该知道的事实
	/// （经济侧因此不必反向依赖 Map / Units —— `IUpkeepDemandSource` 是两者之间的唯一契约）。</para>
	/// <para>口径：① **敌方单位不参与**（设计稿的敌方单位没有维护费，且它们不属于玩家经济）；
	/// ② 只收**本玩家**的单位；③ 训练中的单位（已落位但 `IsReady=false`）同样计入 —— 它已经在图上吃粮了。</para>
	/// </summary>
	public sealed class UnitMaintenanceUpkeepDemandSource(MapAppService map, IUnitsRepository units) : IUpkeepDemandSource
	{
		/// <summary>归因名前缀（每条需求的 `Source` = `unit:worker` / `unit:archer` …）。</summary>
		public const string SourceName = "unit";

		private readonly MapAppService _map = map;
		private readonly IUnitsRepository _units = units;

		public string SourceId => SourceName;

		public IEnumerable<UpkeepDemand> CollectUpkeep(string mapId, int ownerId)
		{
			if (_map == null || _units == null) yield break;

			// ① 数一数本玩家的单位（按模板分组）：地图的占据物视图是"单位在哪"的唯一权威（`WP-3.4`）
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (IMapOccupant occupant in _map.GetOccupants(mapId))
			{
				if (occupant is not Unit unit) continue;

				MapOccupantInfo info = unit.GetInfo();
				if (info.IsHostile) continue;              // 敌方单位不吃玩家的粮
				if (info.OwnerId != ownerId) continue;     // 别的玩家的单位记在别人账上

				counts[info.Id] = counts.GetValueOrDefault(info.Id) + 1;
			}

			// ② 按模板把维护费汇总成需求（同一模板合成一条，报告里一眼能看出"是哪支部队在花钱"）
			foreach (KeyValuePair<string, int> group in counts.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
			{
				IUnitConfig config = _units.GetUnitConfig(group.Key);
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
