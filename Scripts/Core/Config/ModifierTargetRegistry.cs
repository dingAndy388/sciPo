using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Core.Config
{
	/// <summary>
	/// （v0.3 / WP-1.4）Modifier <c>Target</c> 名登记表 —— <c>MOD-05</c>「目标名拼写不符即静默降级」的对策：
	/// 启动期用本表核对所有配置里的 Target 名，未知名字记 warning（WP-2.7 起其中一部分将升级为 error）。
	/// <para>登记表由两部分构成：</para>
	/// <list type="number">
	/// <item>**协议内固定名**（§13.z「Modifier Target 统一登记表」+ 填表指南附录 B）：资源增长、人口、建造/训练速度、单位属性、上限、敌方与破坏类。</item>
	/// <item>**按表派生名**（避免填表者另造词）：资源表的每条资源派生 <c>{Name}Growth</c>；单位表的每个单位派生 <c>{UnitId}Attack</c> / <c>{UnitId}HP</c>（附录 B「单位ID+Attack / 单位ID+HP」）。</item>
	/// </list>
	/// </summary>
	public sealed class ModifierTargetRegistry
	{
		/// <summary>协议内固定 Target 名。</summary>
		public static readonly IReadOnlyList<string> Canonical = new[]
		{
			// 资源增长（IdeaGrowth / FoodGrowth / MineralGrowth 等；Gold/Wood 由资源表派生）
			"IdeaGrowth", "FoodGrowth", "MineralGrowth",
			// 人口
			"PopulationGrowth",
			// 建造 / 训练 / 研发速度
			"BuildingSpeed", "UnitTrainingSpeed", "ResearchSpeed",
			// 单位属性（全局口径）
			"UnitAttack", "UnitSpeed", "UnitHP",
			// 单位属性（通用属性名，见附录 B「Attack」）
			"Attack", "HP", "Movement", "AttackRadius", "AttackDamage", "VisionRadius",
			// 上限与视野
			"ResourceLimit",
			// 敌方（AI）与破坏
			"EnemyAttack", "EnemyDefense", "BuildingDamage",
			// （v0.8.1 / `WP-7.3`）科技效果新增：升级造价折扣 / 食物消耗（93 节点里有若干条用它们）
			"BuildingUpgradeCost", "FoodConsumption",
		};

		private readonly HashSet<string> _targets = new();

		public ModifierTargetRegistry(IEnumerable<string> targets)
		{
			if (targets == null) return;
			foreach (string target in targets)
				if (!string.IsNullOrWhiteSpace(target)) _targets.Add(target);
		}

		public IReadOnlyCollection<string> Targets => _targets;

		public bool Contains(string target) => !string.IsNullOrWhiteSpace(target) && _targets.Contains(target);

		/// <summary>
		/// 依据已装载的配置表派生完整登记表：固定名 ∪ 资源派生名 ∪ 单位派生名。
		/// 表缺失（校验报告里已记 error）时跳过该来源，不影响其余派生。
		/// </summary>
		public static ModifierTargetRegistry From(ConfigTables tables)
		{
			var targets = new HashSet<string>(Canonical);

			IResourcesPoolConfig resources = tables?.Resources?.GetResourcesPoolConfig();
			if (resources?.Resources != null)
				foreach (IResourceConfig resource in resources.Resources)
				{
					if (string.IsNullOrWhiteSpace(resource?.Name)) continue;

					// （v0.6.3 / WP-7.2a）产出目标名与**存储上限**目标名都由资源表派生：
					// `{资源名}Growth` = 产出（农田/矿场），`{资源名}Limit` = 存储上限（仓库）；`ResourceLimit` = 全部资源
					targets.Add($"{resource.Name}Growth");
					targets.Add($"{resource.Name}Limit");
				}

			IEnumerable<IUnitConfig> units = tables?.Units?.GetAll();
			if (units != null)
				foreach (IUnitConfig unit in units)
				{
					if (string.IsNullOrWhiteSpace(unit?.UnitId)) continue;
					targets.Add($"{unit.UnitId}Attack");
					targets.Add($"{unit.UnitId}HP");
				}

			return new ModifierTargetRegistry(targets);
		}
	}
}
