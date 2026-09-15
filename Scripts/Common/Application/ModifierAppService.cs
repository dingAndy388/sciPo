using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Common.Application
{
	public class ModifierAppService
	{
		private readonly IModifierRepository _repo;

		public ModifierAppService(IModifierRepository repo)
		{
			_repo = repo;
		}

		/// <summary>
		/// （v0.8.3 / `WP-4.1`）挂一条修正。<paramref name="stage"/> 决定它在阶段管道里的结算位置
		/// （建筑 → 科技 → 事件；缺省 = 建筑）。
		/// </summary>
		public void AddModifier(string mapId, int ownerId, string sourceId, Modifier modifier,
			ModifierStage stage = ModifierStage.Building)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));

			var value = new ModifierValue(
				modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute,
				modifier.Value, sourceId, stage);

			manager.AddModifier(modifier.Target, value);

			_repo.SaveModifier(mapId, ownerId, manager.GetAllModifiers());
		}

		/// <summary>
		/// （v0.3 / `WP-2.4`；v0.8.3 / `WP-4.1` 加 <paramref name="stage"/>）批量挂修正。
		/// <para>调用方按来源传阶段：建筑/升级 = `Building`（缺省）、科技节点 = `Tech`、事件 = `Event`。</para>
		/// </summary>
		public void AddModifiers(string mapId, int ownerId, string sourceId, List<Modifier> modifiers,
			ModifierStage stage = ModifierStage.Building)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			foreach (var modifier in modifiers)
			{
				var value = new ModifierValue(
					modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute,
					modifier.Value, sourceId, stage);
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
				/// <summary>（v0.8.5 / WP-4.5）该目标名**有没有**修正器（区分"没有修正器"与"修正器值为 0"）。</summary>
		public bool HasTarget(string mapId, int ownerId, string target)
			=> !string.IsNullOrWhiteSpace(target) && _repo.LoadModifiers(mapId, ownerId).ContainsKey(target);

		/// <summary>
		/// （v0.8.5 / `WP-4.1`）**宿主作用域查询**（服务层入口）：只结算 `sourceId` 这个宿主挂上的修正。
		/// <para>用途：想回答"这栋建筑/这个节点自己贡献了多少"（`WP-4.13` 产出归因会用它），
		/// 而不把范围内的邻居与全局科技都算进来。</para>
		/// </summary>
		public float GetValueForHost(string mapId, int ownerId, string hostSourceId, IEnumerable<string> targets, float baseValue = 1f)
		{
			if (string.IsNullOrWhiteSpace(hostSourceId)) return baseValue;

			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			return manager.GetValueForHost(hostSourceId, targets?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? System.Array.Empty<string>(), baseValue);
		}

		public float GetValue(string mapId, int ownerId, string target, float baseValue = 1f)
			=> GetValue(mapId, ownerId, new[] { target }, baseValue);

		/// <summary>
		/// （v0.3 / WP-3.9）**多 Target 读取**（与 `ModifierManager.GetValue` 同语义：按顺序取第一个已登记的 Target）。
		/// <para>需求场景：资源表的 `DependentModifiers` 是一个列表（例如 `["IdeaGrowth"]`，未来可能写多个候选名），
		/// 结算器要按与 `ResourceGrowth` 任务**完全一致**的公式汇总产出，因此必须能一次传入整列。
		/// 缺省值与单 Target 重载完全一致（不含任何候选时返回 <paramref name="baseValue"/>）。</para>
		/// </summary>
		public float GetValue(string mapId, int ownerId, IEnumerable<string> targets, float baseValue)
		{
			string[] names = targets?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();
			if (names.Length == 0) return baseValue;

			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			return manager.GetValue(names, baseValue);
		}
	}
}