using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-1.4）配置表测试夹具：把仓库里**真实的** <c>Config/*.json</c> 读进内存配置源，
	/// 并集中提供 <see cref="CoreBootstrap"/> 的装配入口，避免各检查类各写一份。
	/// </summary>
	internal static class ConfigFixtures
	{
		/// <summary>7 张配置表（与 <c>Config/{name}.json</c> 一一对应）。</summary>
		public static readonly string[] TableNames = { "Terrains", "Resources", "Buildings", "Units", "TechTrees", "Events", "Generator" };

		public static string TablePath(string tableName) => Path.Combine(Check.FindRepoRoot(), "Config", tableName + ".json");

		/// <summary>读取全部真实配置表。</summary>
		public static InMemoryConfigSource RealConfigSource()
		{
			var source = new InMemoryConfigSource();
			foreach (string name in TableNames)
			{
				string path = TablePath(name);
				if (!File.Exists(path)) continue;
				source.Inject(name, File.ReadAllText(path));
			}
			return source;
		}

		/// <summary>真实配置 + 用给定文本覆写某一张表（用于制造"这张表写错了"的场景）。</summary>
		public static InMemoryConfigSource RealConfigSourceWith(string tableName, string json)
		{
			InMemoryConfigSource source = RealConfigSource();
			source.Inject(tableName, json);
			return source;
		}

		/// <summary>按给定配置源装配核心：测试里唯一的 <see cref="CoreBootstrap"/> 入口。</summary>
		/// <param name="mapRepository">
		/// （v0.3 / WP-2.3）可选：传入自己的地图仓库替身，便于断言存档点行为（`LoadCount`/`SaveCount`）。
		/// </param>
		public static CoreServices BuildCore(InMemoryConfigSource configSource, bool failOnConfigErrors = true, InMemoryMapRepository mapRepository = null)
		{
			var repository = mapRepository ?? new InMemoryMapRepository();
			return CoreBootstrap.Build(new CoreDependencies
			{
				FileSystem = new InMemoryFileSystem(),
				ConfigSource = configSource,
				Random = new SystemRandom(1234),
				ResourceConfigLoader = null, // 无头环境：生成器配置取 Config/Generator.json
				GeneratorConfigPath = null,
				FailOnConfigErrors = failOnConfigErrors,
				MapRepositoryFactory = _ => repository,
				SessionId = "test",
			});
		}

		public static CoreServices BuildRealCore(bool failOnConfigErrors = true, InMemoryMapRepository mapRepository = null)
			=> BuildCore(RealConfigSource(), failOnConfigErrors, mapRepository);
	}
}
