using System;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>验收载具入口（v0.3 / WP-0.2；v0.6.3 / WP-7.1 加分组过滤）。返回码 0 = 全部通过。</summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			Console.WriteLine("=== Science Potato · Headless Checks (M0-1) ===");

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
			};

			string filter = args?.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg) && !arg.StartsWith("-"));
			int ran = 0;

			foreach ((string name, Action run) in groups)
			{
				if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

				run();
				ran++;
			}

			if (filter != null) Console.WriteLine($"[filter] 只运行了 {ran} 组（匹配「{filter}」；不带参数 = 全部 {groups.Length} 组）");

			return Check.Summary();
		}
	}
}
