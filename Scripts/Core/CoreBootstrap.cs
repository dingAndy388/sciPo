using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using System;
using System.Linq;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）**唯一的组合根**：把"配置表 + 会话状态 + 应用服务"装配成一个可运行的 <see cref="CoreServices"/>。
	/// <para>设计要点（对应 `WIRE-01` / `DEP-02`）：</para>
	/// <list type="bullet">
	/// <item>只有本方法与 Godot 适配层可以 <c>new</c> 具体类型；其余一律通过构造函数注入接口。</item>
	/// <item>装配是**显式顺序**的（基础设施 → 配置表 → 时间的会话状态 → 生成器 → 应用服务），
	/// 不引入 DI 容器，用"扁平顺序"避免构造循环依赖。</item>
	/// <item>配置缺失时**快速失败并给出明确消息**（`WIRE-04`），而不是让 null 在运行时爆炸。</item>
	/// </list>
	/// </summary>
	public static class CoreBootstrap
	{
		public static CoreServices Build(CoreDependencies dependencies)
		{
			if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));
			if (dependencies.ConfigSource == null) throw new ArgumentNullException(nameof(dependencies.ConfigSource), "缺少 IConfigSource");
			if (dependencies.MapRepositoryFactory == null) throw new ArgumentNullException(nameof(dependencies.MapRepositoryFactory), "缺少 MapRepositoryFactory");

			// 1) 配置表：当前已通电的只有 Terrains（其余 6 张由 WP-1.4 接入）
			ITerrainConfigRepository terrainConfig = BuildTerrainConfig(dependencies.ConfigSource);

			// 2) 时间（纯 C#，可手动推进）
			var clock = new GameClock();

			// 3) 会话状态（Map 常驻内存）：地图仓库由宿主工厂按地形配置构造
			var mapRepository = dependencies.MapRepositoryFactory(terrainConfig);
			var maps = new MapSession(mapRepository);
			var session = new GameSession(maps, clock, dependencies.SessionId);

			// 4) 地图生成器（Godot 的 .tres 配置可选，缺失时回退默认值）
			IMapGenerator mapGenerator = BuildMapGenerator(dependencies, terrainConfig);

			// 5) 应用服务
			var mapService = new MapAppService(mapGenerator, maps);

			return new CoreServices
			{
				Session = session,
				ConfigSource = dependencies.ConfigSource,
				TerrainConfig = terrainConfig,
				MapGenerator = mapGenerator,
				Map = mapService,
			};
		}

		private static ITerrainConfigRepository BuildTerrainConfig(Common.Domain.IConfigSource configSource)
		{
			string json = configSource.LoadText("Terrains");
			if (string.IsNullOrWhiteSpace(json))
				throw new InvalidOperationException(
					"[CoreBootstrap] 缺少地形配置表 Config/Terrains.json（v0.3 / WP-0.4 起地形由 JSON 提供：" +
					"编辑器中为 res://Config/Terrains.json，热更时为 user://Config/Terrains.json）");

			var repository = new TerrainsConfigRepository(json);
			if (!repository.GetAll().Any())
				throw new InvalidOperationException("[CoreBootstrap] 地形配置表解析成功但没有任何条目（Terrains 数组为空）");

			return repository;
		}

		private static IMapGenerator BuildMapGenerator(CoreDependencies dependencies, ITerrainConfigRepository terrainConfig)
		{
			// 生成器配置路径仅在有 Godot 资源加载器时使用；无头环境传 null 即可（生成器内部回退默认值）
			return new VoronoiMapGenerator(
				terrainConfig,
				dependencies.ResourceConfigLoader,
				dependencies.GeneratorConfigPath);
		}
	}
}
