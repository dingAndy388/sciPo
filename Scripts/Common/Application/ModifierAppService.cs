using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Application
{
	public class ModifierAppService
	{
		private readonly IModifierRepository _repo;

		public ModifierAppService(IModifierRepository repo)
		{
			_repo = repo;
		}

		public void AddModifier(string mapId, int ownerId, string sourceId, Modifier modifier)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));

			var value = new ModifierValue(
				modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute,
				modifier.Value, sourceId);

			manager.AddModifier(modifier.Target, value);

			_repo.SaveModifier(mapId, ownerId, manager.GetAllModifiers());
		}

		public void AddModifiers(string mapId, int ownerId, string sourceId, List<Modifier> modifiers)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			foreach (var modifier in modifiers)
			{
				var value = new ModifierValue(
					modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute,
					modifier.Value, sourceId);
				manager.AddModifier(modifier.Target, value);
			}
			_repo.SaveModifier(mapId, ownerId, manager.GetAllModifiers());
		}

		public void RemoveModifiersBySourceId(string mapId, int ownerId, string sourceId)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			manager.RemoveModifiersBySourceId(sourceId);
			_repo.SaveModifier(mapId, ownerId, manager.GetAllModifiers());
		}

		/// <summary>
		/// （v0.3 / WP-2.3）读取某玩家在指定 Target 上的计算值：<c>(base + ΣAbsolute) × (1 + ΣPercent)</c>。
		/// <para>用途：让"速率/产出"类消费点能读到修正器。此前 `PopulationGrowth` 只登记在
		/// <see cref="SciencePotato.Scripts.Core.Config.ModifierTargetRegistry"/> 里、**没有任何消费点** ——
		/// 填了也不生效（§18.4「PopulationGrowth 修正器未接线」）。人口增长是该 target 的第一个消费点；
		/// 其余（`BuildingSpeed` / `UnitTrainingSpeed` / `ResearchSpeed` 等）随 `WP-4.4` 分批接线。</para>
		/// </summary>
		public float GetValue(string mapId, int ownerId, string target, float baseValue = 1f)
		{
			if (string.IsNullOrWhiteSpace(target)) return baseValue;

			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			return manager.GetValue(new[] { target }, baseValue);
		}
	}
}