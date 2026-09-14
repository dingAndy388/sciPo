using System;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>M0-1 验收载具入口（v0.3 / WP-0.2）。返回码 0 = 全部通过。</summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			Console.WriteLine("=== Science Potato · Headless Checks (M0-1) ===");

			ClockChecks.RunAll();
			ConfigChecks.RunAll();
			MapSessionChecks.RunAll();
			CoreBootstrapChecks.RunAll();
			ConfigTableChecks.RunAll();
			TimeBaselineChecks.RunAll();
			TechPrerequisiteChecks.RunAll();
			BuildingProductionChecks.RunAll();
			EventEngineChecks.RunAll();
			TaskLifecycleChecks.RunAll();
			PopulationGrowthChecks.RunAll();

			return Check.Summary();
		}
	}
}
