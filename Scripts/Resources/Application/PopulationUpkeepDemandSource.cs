using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Resources.Application
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `TIME-14`、`C7`、`RES-01`）**人口维护需求**：`人口总和 × 每人每月需求`
	/// （设计稿 design/resources.md：「Food … 基础需求：人口总和 × 3/月」）。
	/// <para>两个参数都来自 `Config/Resources.json` 的 `Settlement` 段（需求资源名 + 每人每月需求），
	/// 因此"填表即生效"：改表不用改代码，也不会出现"代码里 3、表里 2"的两处真相。</para>
	/// <para><b>已知边界</b>：人口目前是**地图级**的（`MapCell.Population` 上没有所有者），
	/// 因此多玩家下按整图人口收一次；多玩家人口隔离属批次 4（见 §18.4.2）。</para>
	/// </summary>
	public sealed class PopulationUpkeepDemandSource(MapAppService map, IResourcesPoolConfig config) : IUpkeepDemandSource
	{
		/// <summary>归因名（报告里的 `Source` 字段）。</summary>
		public const string SourceName = "population";

		private readonly MapAppService _map = map;
		private readonly IResourcesPoolConfig _config = config;

		public string SourceId => SourceName;

		public IEnumerable<UpkeepDemand> CollectUpkeep(string mapId, int ownerId)
		{
			ISettlementConfig settlement = _config?.Settlement;

			// 没写结算参数 / 没配需求资源 / 需求为 0 → 本月没有人口需求（校验器会在启动期提示"填了不生效"的隐患）
			if (settlement == null
				|| string.IsNullOrWhiteSpace(settlement.DemandResource)
				|| settlement.PopulationUpkeepPerMonth <= 0f)
				yield break;

			int population = _map?.GetTotalPopulation(mapId) ?? 0;
			if (population <= 0) yield break;

			yield return new UpkeepDemand(
				settlement.DemandResource,
				population * settlement.PopulationUpkeepPerMonth,
				SourceName,
				population);
		}
	}
}
