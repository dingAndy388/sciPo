using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Units.Domain;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using System;

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

			// 1) 7 张配置表（v0.3 / WP-1.4）：装载「能不能解析」+ 校验「内容对不对」，两者结论合并在同一份报告里
			var report = new ConfigReport();
			ConfigTables tables = ConfigTableLoader.Load(dependencies.ConfigSource, report);
			ConfigValidator.Validate(tables, report);

			// 2) 分级处置：error 快速失败（默认），warning 只随报告带出、不阻断启动
			if (report.HasErrors && dependencies.FailOnConfigErrors)
				throw new InvalidOperationException(
					$"[CoreBootstrap] 配置表校验未通过（{report.Summary()}）：\n{report.ToLines()}");

			// 3) 时间（纯 C#，可手动推进）：时钟 + 逐日派发总线（v0.3 / WP-1.5）
			var clock = new GameClock();
			var timeService = new GameTimeService(clock);

			// 3.5) 领域事件总线（v0.3 / WP-2.10）：**全进程一份**，由应用服务发布、表现层/统计订阅。
			//      放在组合根创建，是为了避免"每个服务各 new 一个总线"导致订阅方收不到消息（静默失联）。
			var domainEvents = new DomainEventBus();

			// 4) 会话状态（Map 常驻内存）：地图仓库由宿主工厂按**整表**构造（v0.3 / WP-3.2：读档要重建建筑/单位）
			var rebuilder = new SaveRebuilder(new BuildingFactory(tables.Buildings), new UnitFactory(tables.Units));
			var mapRepository = dependencies.MapRepositoryFactory(tables);
			AttachRebuilder(mapRepository, rebuilder);
			var maps = new MapSession(mapRepository);
			var session = new GameSession(maps, clock, dependencies.SessionId);

			// 5) 地图生成器：地形表 + 生成器表（可选的 .tres 覆写只服务于编辑器调参）
			var mapGenerator = new VoronoiMapGenerator(
				tables.Terrains,
				tables.Generator,
				dependencies.ResourceConfigLoader,
				dependencies.GeneratorConfigPath);

			// 6) 应用服务
			var mapService = new MapAppService(mapGenerator, maps);

			return new CoreServices
			{
				Session = session,
				Time = timeService,
				ConfigSource = dependencies.ConfigSource,
				Tables = tables,
				ConfigReport = report,
				MapGenerator = mapGenerator,
				Map = mapService,
				DomainEvents = domainEvents,
				SaveStore = dependencies.SaveStore,
			};
		}

		/// <summary>
		/// （v0.3 / WP-3.2）给仓库补挂实体重建器：`GodotMapRepository` 由构造参数接收，测试替身用可写属性。
		/// <para>这里用"鸭子类型"（`is` 模式匹配）而不是往 `IMapRepository` 上加接口成员：接口是"读写地图"的最小契约，
		/// 重建器只与**读档实现**有关，不该污染接口（也避免所有替身都要实现它）。</para>
		/// </summary>
		private static void AttachRebuilder(IMapRepository repository, SaveRebuilder rebuilder)
		{
			if (repository is GodotMapRepository godotRepository) godotRepository.AttachRebuilder(rebuilder);
		}
	}
}

