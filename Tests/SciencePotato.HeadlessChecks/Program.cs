using System;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>验收载具入口（v0.3 / WP-0.2；v0.6.3 / WP-7.1 加分组过滤）。返回码 0 = 全部通过。</summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			// （v0.6.3）分组表：默认"一次跑全部"，也支持 `-- <组名子串>` 只跑几组。
			// 为什么要这个：全套 40~60 秒，排错时为了看一组失败要等整轮 —— 而"迭代慢"会让人少跑测试。
			var groups = new (string Name, Action Run)[]
			{
				("Clock", ClockChecks.RunAll),
				("Config", ConfigChecks.RunAll),
				("MapSession", MapSessionChecks.RunAll),
				("CoreBootstrap", CoreBootstrapChecks.RunAll),
				("ConfigTable", ConfigTableChecks.RunAll),
				("TimeBaseline", TimeBaselineChecks.RunAll),
				("TechPrerequisite", TechPrerequisiteChecks.RunAll),
				("BuildingProduction", BuildingProductionChecks.RunAll),
				("EventEngine", EventEngineChecks.RunAll),
				("TaskLifecycle", TaskLifecycleChecks.RunAll),
				("PopulationGrowth", PopulationGrowthChecks.RunAll),
				("TrainingQueue", TrainingQueueChecks.RunAll),
				("Builder", BuilderChecks.RunAll),
				("Upgrade", UpgradeChecks.RunAll),
				("TechTreeConcurrency", TechTreeConcurrencyChecks.RunAll),
				("DomainEvent", DomainEventChecks.RunAll),
				("WorldPersistence", WorldPersistenceChecks.RunAll),
				("Occupancy", OccupancyChecks.RunAll),
				("Movement", MovementChecks.RunAll),
				("SaveUnit", SaveUnitChecks.RunAll),
				("EnemySpawn", EnemySpawnChecks.RunAll),
				("Combat", CombatChecks.RunAll),
				("Loot", LootChecks.RunAll),
				("MonthlySettlement", MonthlySettlementChecks.RunAll),
				("Wiring", WiringChecks.RunAll),
				("Appearance", AppearanceChecks.RunAll),
				("ContentBuilding", ContentBuildingChecks.RunAll),
				("SessionSetup", SessionSetupChecks.RunAll),
				("I18n", I18nChecks.RunAll),
			("Victory", VictoryChecks.RunAll),
			("BuildingHp", BuildingHpChecks.RunAll),
			("AiConfig", AiConfigChecks.RunAll),
			("AiDecision", AiDecisionChecks.RunAll),
			("AiEconomy", AiEconomyChecks.RunAll),
			("AiMilitary", AiMilitaryChecks.RunAll),
			("AiFairness", AiFairnessChecks.RunAll),
			("AiVictory", AiVictoryChecks.RunAll),
			};

			// `--list-groups`：只输出组名（不给脚本混进横幅），供 `Tools/verify.ps1` 分片
			if (args != null && args.Contains("--list-groups"))
			{
				foreach ((string name, Action _) in groups) Console.WriteLine(name);
				return 0;
			}

			Console.WriteLine("=== Science Potato · Headless Checks (M0-1) ===");

			// （v0.6.7 / 工作流 W1）过滤：`--only-group=X` 精确匹配（分片用，避免 "Config" 撞上 "ConfigTable"）；
			// 裸参数做子串匹配（人用着方便）。不传任何过滤 = 全部。
			string[] exact = args == null
				? Array.Empty<string>()
				: args.Where(arg => arg.StartsWith("--only-group=", StringComparison.Ordinal))
					  .Select(arg => arg.Substring("--only-group=".Length)).ToArray();

			string[] filters = args == null
				? Array.Empty<string>()
				: args.Where(arg => !string.IsNullOrWhiteSpace(arg) && !arg.StartsWith("-")).ToArray();

			int ran = 0;

			foreach ((string name, Action run) in groups)
			{
				bool wanted = exact.Length > 0
					? exact.Any(e => string.Equals(name, e, StringComparison.OrdinalIgnoreCase))
					: filters.Length == 0 || filters.Any(f => name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
				if (!wanted) continue;

				run();
				ran++;
			}

			if (exact.Length > 0 || filters.Length > 0)
			{
				// 拼串放在插值之外（本轮教训：插值洞里写引号/单引号极易写出非法字面量）
				string exactList = string.Join(",", exact);
				string filterList = string.Join(",", filters);
				Console.WriteLine($"[filter] 只运行了 {ran} 组（精确：{exactList}；子串：{filterList}；不带参数 = 全部 {groups.Length} 组）");
			}

			return Check.Summary();
		}
	}
}
