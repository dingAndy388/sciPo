using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Units.Domain;
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

		/// <summary>
		/// （v0.6.5 / WP-5.10）**非配置表的文本资源**（不参与 `ConfigTables` 装配，但要能被 `IConfigSource` 读到）：
		/// 目前只有多语言文案 <c>Config/Strings.{locale}.json</c>。
		/// </summary>
		public static readonly string[] TextResources = { "Strings.zh", "Strings.en", "AI" };

		public static string TablePath(string tableName) => Path.Combine(Check.FindRepoRoot(), "Config", tableName + ".json");

		/// <summary>读取全部真实配置表（含多语言文案）。</summary>
		public static InMemoryConfigSource RealConfigSource()
		{
			var source = new InMemoryConfigSource();
			foreach (string name in TableNames) InjectFile(source, name);
			foreach (string name in TextResources) InjectFile(source, name);
			return source;
		}

		private static void InjectFile(InMemoryConfigSource source, string name)
		{
			string path = TablePath(name);
			if (!File.Exists(path)) return;
			source.Inject(name, File.ReadAllText(path));
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
		/// <para>（v0.3 / WP-3.2）无论哪种方式，工厂都会给它挂上 `SaveRebuilder`（按 uid 重建建筑/单位），
		/// 否则读档只剩地形，验不出实体持久化。</para>
		/// </param>
		/// <param name="enableEnemySpawn">
		/// （v0.3 / WP-3.8）是否装配「地图生成后按地形概率刷新敌方单位」（<c>EnemySpawner</c>）。缺省 **false**：
		/// 多数用例要自己布置格位（工人/建筑/敌人），随机刷怪会抢占这些格位。
		/// <para>敌方玩法本身由 <c>EnemySpawnChecks</c> 显式传 `true` 验收；生产装配默认开
		/// （`CoreDependencies.EnableEnemySpawn`，见 `ServiceContainer`）。</para>
		/// </param>
		public static CoreServices BuildCore(InMemoryConfigSource configSource, bool failOnConfigErrors = true, InMemoryMapRepository mapRepository = null, bool enableEnemySpawn = false,
			ISaveStore saveStore = null, string saveRoot = null, IEnumerable<PlayerContext> players = null)
		{
			return CoreBootstrap.Build(new CoreDependencies
			{
				FileSystem = new InMemoryFileSystem(),
				ConfigSource = configSource,
				Random = new SystemRandom(1234),
				ResourceConfigLoader = null, // 无头环境：生成器配置取 Config/Generator.json
				GeneratorConfigPath = null,
				FailOnConfigErrors = failOnConfigErrors,
				EnableEnemySpawn = enableEnemySpawn,
				// （v0.9.6 / WP-5.11）统一存档单元：会话入口（新开局/存档/读档）与周期任务分区需要它
				SaveStore = saveStore,
				SaveRoot = saveRoot ?? "user://save/",
				MapRepositoryFactory = tables =>
				{
					InMemoryMapRepository repository = mapRepository ?? new InMemoryMapRepository();
					repository.Rebuilder = new SaveRebuilder(new BuildingFactory(tables.Buildings), new UnitFactory(tables.Units));
					repository.TerrainResolver = terrainId => tables.Terrains.GetById(terrainId);
					return repository;
				},
				SessionId = "test",
				// （v0.7.1 / WP-6.1）**夹具显式给"单人类玩家"**：不给的话装配层会按 `Config/AI.json` 的 `Count`
				// 自动补 AI（生产路径要的行为），而绝大多数用例只想有一个人类玩家、再按需 `AddPlayer`。
				Players = players ?? new[] { PlayerContext.Human(PlayerContext.FirstOwnerId) },
			});
		}

		public static CoreServices BuildRealCore(bool failOnConfigErrors = true, InMemoryMapRepository mapRepository = null,
			ISaveStore saveStore = null, string saveRoot = null)
			=> BuildCore(RealConfigSource(), failOnConfigErrors, mapRepository, saveStore: saveStore, saveRoot: saveRoot);
	}
}
