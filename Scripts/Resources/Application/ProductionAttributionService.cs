using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.TechTree.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Resources.Application
{
	/// <summary>
	/// （v0.8.9 / `WP-4.13`）**产出归因**的一行：某个「来源」对某个目标名贡献了多少。
	/// <para>`Kind` 是 **`SourceId` 语义规范**（`D115`）的落地：`building`（建筑 uid）/ `tech`（科技节点 Id）/
	/// `other`（其它，含事件 Id —— 判据见服务）/ `base`（资源表基准）。</para>
	/// </summary>
	public sealed record ProductionAttribution(string SourceId, string Target, string Kind, float Absolute, float Percent)
	{
		public float Apply(float value) => (value + Absolute) * (1f + Percent);

		public override string ToString()
			=> $"{Kind}:{SourceId} {Target} +{Absolute:0.#} {Percent:+0.##%;-0.##%;0}";
	}

	/// <summary>
	/// （v0.8.9 / `WP-4.13`）**"查看资源加减项"**：把某资源当月的产出拆成一条条来源
	/// （设计稿「解锁资源收获面板，允许查看资源的加减项」）。
	/// <para>读的是**修正器仓储的原始条目**（不是聚合值）—— 所以面板能回答"这 12 点食物是谁给的"，
	/// 而不是只给一个总数；配合 `ModifierManager.GetValueForHost` 还能按宿主逐栋核算。</para>
	/// </summary>
	public sealed class ProductionAttributionService
	{
		private readonly IModifierRepository _repo;
		private readonly MapAppService _map;
		private readonly ITechTreesConfigRepository _techConfigs;

		public ProductionAttributionService(IModifierRepository repo, MapAppService map, ITechTreesConfigRepository techConfigs = null)
		{
			_repo = repo;
			_map = map;
			_techConfigs = techConfigs;
		}

		/// <summary>该资源当月产出的全部来源（不含 `base`；基准值由调用方叠加）。</summary>
		public IReadOnlyList<ProductionAttribution> Attribute(string mapId, int ownerId, IResourceConfig resource)
		{
			var rows = new List<ProductionAttribution>();
			if (resource?.DependentModifiers == null) return rows;

			Dictionary<string, List<ModifierValue>> data = _repo.LoadModifiers(mapId, ownerId);

			foreach (string target in resource.DependentModifiers)
			{
				if (string.IsNullOrWhiteSpace(target)) continue;
				if (!data.TryGetValue(target, out List<ModifierValue> values) || values == null) continue;

				foreach (ModifierValue value in values)
				{
					rows.Add(new ProductionAttribution(
						value.SourceId,
						target,
						KindOf(mapId, value.SourceId),
						value.Type == ModifierType.Absolute ? value.Value : 0f,
						value.Type == ModifierType.Percentage ? value.Value : 0f));
				}
			}
			return rows;
		}

		/// <summary>
		/// （`D115`）**`SourceId` 语义判定**：建筑（uid 能在图上查到）/ 科技（节点 Id 能在某棵树里查到）/ other。
		/// <para>判定顺序固定：先查建筑（uid 是随机串，不会撞科技 Id），再查科技，剩下归 other
		/// （事件 Id 目前也落在这里 —— 事件是临时效果，归因面板把它们单独列在 `other` 并不影响"加减项"的可读性）。</para>
		/// </summary>
		public string KindOf(string mapId, string sourceId)
		{
			if (string.IsNullOrWhiteSpace(sourceId)) return "base";

			if (_map?.FindOccupantByUId(mapId, sourceId) is Building) return "building";

			if (_techConfigs != null)
				foreach (string treeId in _techConfigs.GetTreeIds())
					if (_techConfigs.GetTechNodeConfig(treeId, sourceId) != null) return "tech";

			return "other";
		}
	}
}
