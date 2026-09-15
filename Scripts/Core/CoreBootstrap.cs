using SciencePotato.Scripts.AI.Application;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.AI.Infrastructure;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Units.Domain;
using SciencePotato.Scripts.Units.Infrastructure;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using System;
using SciencePotato.Scripts.Construction.Application;
using SciencePotato.Scripts.Core.Save;
using SciencePotato.Scripts.Events.Application;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Intent.Application;
using SciencePotato.Scripts.Fog.Infrastructure;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Infrastructure;
using SciencePotato.Scripts.Units.Application;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3 建；v0.6.0 / WP-5.1 补全）**唯一的组合根**：把"配置表 + 会话状态 + 全部应用服务"装配成一个可运行的 <see cref="CoreServices"/>。
	/// <para>设计要点（对应 `WIRE-01` / `DEP-02`）：</para>
	/// <list type="bullet">
	/// <item>只有本方法与 Godot 适配层可以 <c>new</c> 具体类型；其余一律通过构造函数注入接口。</item>
	/// <item>装配是**显式顺序**的（基础设施 → 配置表 → 存档接线 → 时间的会话状态 → 生成器 → 应用服务），
	/// 不引入 DI 容器，用"扁平顺序"避免构造循环依赖。</item>
	/// <item>配置缺失时**快速失败并给出明确消息**（`WIRE-04`），而不是让 null 在运行时爆炸。</item>
	/// <item>（v0.6.0 / WP-5.1）**换装点只有这里一处**：宿主（`ServiceContainer`）与无头用例都从本入口拿同一份装配，
	/// 因此"生产路径上少了某个服务"这类缺陷不会再出现（改造前 Construction/Units/Tech/Events/Resources/Fog/月结/存档
	/// 只存在于测试夹具里）。</item>
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

			// 3.2) 统一存档的**逐仓储接线**（v0.6.0 / WP-5.1）：
			//      有存档单元时，任务/资源/科技/迷雾/修正器仓储都写进它的分区（存档点一次原子落盘）；
			//      没有存档单元（无头用例 / 纯内存工具）时**保持 v0.5 的语义**：不挂任务仓储、不写任何文件
			//      —— 否则测试会往 "user://..." 这种伪路径上写盘，把「没有存档单元 = 无持久化」的契约弄脏。
			ISaveStore store = dependencies.SaveStore;
			string saveRoot = string.IsNullOrWhiteSpace(dependencies.SaveRoot) ? "user://save/" : dependencies.SaveRoot;

			// 3.3) **没有存档单元时，仓储前缀改到系统临时目录**（v0.7.0 修：`D74` 的补充）。
			//  `GenericJsonRepository` 在 store == null 时退化为"直接写 filePath" —— 而 `SaveRoot` 的缺省值是
			//  `user://save/`（Godot 语义）。无头夹具/工具里没有存档单元，于是第一次写盘就会拿
			//  `user:\save\...` 当 Windows 路径 → 抛"路径非法"（`WP-4.19` 的用例第一次跑到"建资源池"就炸了）。
			//  统一丢到系统临时目录：语义仍是"不持久化"（进程结束即无意义），且绝不污染仓库。
			if (store == null)
			{
				saveRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
					"sp-nostore-" + Guid.NewGuid().ToString("N"), string.Empty);
				System.IO.Directory.CreateDirectory(saveRoot);
			}

			ITaskRepository tasks = store == null ? null : new TaskRepository(saveRoot + "tasks_", store);
			var timeService = new GameTimeService(clock, tasks);

			// 3.5) 领域事件总线（v0.3 / WP-2.10）：**全进程一份**，由应用服务发布、表现层/统计订阅。
			//      放在组合根创建，是为了避免"每个服务各 new 一个总线"导致订阅方收不到消息（静默失联）。
			var domainEvents = new DomainEventBus();

			// 3.8) 多语言（v0.6.5 / WP-5.10）：UI 文案外置成 Config/Strings.{locale}.json。
			//      缺文件只记 warning（少一种语言不该让游戏开不了），缺键在 UI 上以 ⟦key⟧ 显形。
			I18nService i18n = I18nService.Load(dependencies.ConfigSource, dependencies.Locales, dependencies.DefaultLocale);
			if (!string.IsNullOrWhiteSpace(dependencies.Locale)) i18n.SetLocale(dependencies.Locale);

			// 3.9) AI 配置（v0.7.1 / WP-6.1）：策略参数（几个 AI / 前期不造兵窗口 / 分配比例 / 威胁阈值）。
			//      与多语言同一处理方式：由 IConfigSource 直接读 + 单独校验，不并进 7 张配置表
			IAiConfig aiConfig = AiConfigLoader.Load(dependencies.ConfigSource, tables, report);

			// 4) 会话状态（Map 常驻内存）：地图仓库由宿主工厂按**整表**构造（v0.3 / WP-3.2：读档要重建建筑/单位）
			var rebuilder = new SaveRebuilder(new BuildingFactory(tables.Buildings), new UnitFactory(tables.Units));
			var mapRepository = dependencies.MapRepositoryFactory(tables);
			AttachRebuilder(mapRepository, rebuilder);
			var maps = new MapSession(mapRepository);

			// 4.5) 玩家表（v0.6.0 / WP-4.18）：缺省 = 单人类玩家；注入多玩家时按 ownerId 升序确定遍历顺序
			// 4.2) 玩家表（v0.6.0 / WP-4.18；v0.7.1 / WP-6.1 起由 AI 配置驱动）：宿主显式给表就用它，
			//      否则按"1 个人类 + `AI.Count` 个 AI"自建 —— 让"AI 几个"只有一个出处（`Config/AI.json`）。
			var session = new GameSession(maps, clock, dependencies.SessionId, dependencies.Players ?? BuildPlayers(aiConfig));

			// 5) 地图生成器：地形表 + 生成器表（可选的 .tres 覆写只服务于编辑器调参）
			var mapGenerator = new VoronoiMapGenerator(
				tables.Terrains,
				tables.Generator,
				dependencies.ResourceConfigLoader,
				dependencies.GeneratorConfigPath);

			// 6) 应用服务（v0.6.0 / WP-5.1：全部到齐；顺序 = 依赖的拓扑序，不引入 DI 容器）
			// 6.1) 资源 / 修正器 / 科技（资源池是三者共同的下游）
			// 修正器仓储：**全进程一个实例**（`ModifierRepository` 每次调用都会重读分区，多实例不会读错；
			// 统一成一个只是让"谁持有仓储"清楚，将来换实现/加缓存时少一个坑）
			var modifierRepo = new ModifierRepository(saveRoot + "modifiers_", store);
			var modifiers = new ModifierAppService(modifierRepo);

			var resources = new ResourcesAppService(
				new ResourcesRepository(saveRoot + "resources_", store),
				tables.Resources,
				timeService,
				modifierRepo,
				domainEvents); // v0.6.3 / WP-7.2a：订阅建筑落成/升级 → 立刻重算存储上限（仓库）

			TechTreesAppService tech = tables.TechTrees == null ? null : new TechTreesAppService(
				new TechTreesRepository(saveRoot + "techtrees_", tables.TechTrees, store),
				tables.TechTrees,
				resources,
				modifiers,
				timeService,
				domainEvents);

			// 6.2) 迷雾：**人类玩家**的视野（按 owner 拆分属 `WP-4.10`；在那之前只有人类需要被"遮住"）
			FogAppService fog = new FogAppService(session.HumanOwnerId, new FogRepository(saveRoot + "fog_", store));

			// 6.3) 生成后处理器（敌方单位按地形概率放置）在**组合根**装配，而不是只在测试里 ——
			//      否则"敌方封锁"这条玩法规则在生产路径上不存在。单位表装载失败时不挂（已在报告里记 error）
			var postProcessors = new List<IMapPostProcessor>();
			if (dependencies.EnableEnemySpawn && tables.Units != null)
				postProcessors.Add(new EnemySpawner(tables.Units, new UnitFactory(tables.Units)));

			var mapService = new MapAppService(mapGenerator, maps, postProcessors, null, domainEvents);

			// 6.4) 建造 / 单位（单位服务内部再分派移动与战斗：移动与战斗互相需要，构造期互相注入会成环）
			ConstructionAppService construction = tables.Buildings == null ? null : new ConstructionAppService(
				mapService, resources, tech, new BuildingFactory(tables.Buildings), tables.Buildings,
				timeService, modifiers, fog, domainEvents);

			UnitsAppService units = tables.Units == null ? null : new UnitsAppService(
				mapService, tech, resources, construction, timeService, tables.Units,
				new UnitFactory(tables.Units), fog, tables.Buildings, domainEvents, modifiers); // WP-4.4：训练耗时/移动力读修正器

			// 6.5) 月度结算（`WP-3.9`/`WP-3.10`）：需求来源 = 人口维护 + 单位维护 + 建筑维护；
			//      缺哪张表就少挂哪一项（配置缺失已在报告里记 error，这里不制造二次异常）
			var demandSources = new List<IUpkeepDemandSource>();
			if (tables.Resources?.GetResourcesPoolConfig() != null)
				demandSources.Add(new PopulationUpkeepDemandSource(mapService, tables.Resources.GetResourcesPoolConfig()));
			if (tables.Units != null) demandSources.Add(new UnitMaintenanceUpkeepDemandSource(mapService, tables.Units));
			if (tables.Buildings != null) demandSources.Add(new BuildingMaintenanceUpkeepDemandSource(mapService, tables.Buildings));

			MonthlySettlementService settlement = tables.Resources == null ? null : new MonthlySettlementService(
				resources,
				tables.Resources,
				modifiers,
				timeService,
				demandSources,
				store,
				populationSink: mapService,                        // 减员经地图按地块扣人（`MapAppService : IPopulationSink`）
				buildingRepo: tables.Buildings,                  // WP-4.5：产出浮动读建筑 OutputVariance
				occupantQuery: mapService,                       // WP-4.5/4.2：按建筑算浮动与范围覆盖（IOccupantQuery）
				unitConfigs: tables.Units,                      // WP-4.6：驻扎加成读单位表
				random: dependencies.Random ?? new SystemRandom(20260914));

			// 6.6) 事件引擎（**只对人类玩家启动**，见 `SessionOrchestrator`）；事件表缺失时不装配
			EventAppService events = tables.Events == null ? null : new EventAppService(
				tables.Events, resources, tech, modifiers, timeService,
				dependencies.Random ?? new SystemRandom(20260914), domainEvents, store, clock);

			// 6.7) 世界存档（存档点唯一入口）+ 会话级子系统编排（v0.6.0 / WP-4.18）
			IClockRepository clockRepo = store == null ? null : new FileClockRepository(saveRoot + "clock_", store);
			var worldSave = new WorldSaveService(
				session, mapService, timeService, clockRepo, tasks,
				construction, units, tech, resources, events, fog, store, settlement);

			var orchestrator = new SessionOrchestrator(session, resources, settlement, events);

			// 6.8) 开局布置（v0.6.4 / WP-5.9）：出生点 / 开局单位 / 开局资源 / 人类开局视野。
			//      交给编排器在 StartMap 里调用 —— "生成地图 → 布置 → 启动子系统"成为一条不可省略的链。
			var setup = new SessionSetupService(session, mapService, units, resources, fog, tables.Start);
			orchestrator.AttachSetup(setup);

			// 6.10) AI 决策循环（v0.7.3 / WP-6.2）：只对非人类势力启动；策略参数来自 `Config/AI.json`
			var ai = new AiService(session, mapService, resources, tables.Resources, tables.Buildings, tables.Units, timeService, aiConfig);
			orchestrator.AttachAi(ai);

			// 6.11) AI 经济分配（v0.7.4 / WP-6.3）：判断完就下单（只走玩家同一套应用服务）
			var aiEconomy = new AiEconomyService(session, mapService, resources, construction, tech, tables.Units, tables.Buildings, tables.TechTrees, tables.Resources, aiConfig);
			ai.AttachActionSink(aiEconomy);

			// 6.12) AI 军事（v0.7.5 / WP-6.4）：军费分级 → 训练 + 威胁升级时守家（同样只走玩家同一套服务）
			var aiMilitary = new AiMilitaryService(session, mapService, resources, units, tables.Units, tables.Buildings, aiConfig);
			ai.AttachActionSink(aiMilitary);

			// 6.9) 胜负判定（v0.7.0 / WP-4.19）：每月判定（吃月结推送）+ 全灭（吃单位阵亡推送）
			var victory = new VictoryService(session, mapService, settlement, domainEvents);

			// 6.14) 人口模型（v0.8.8 / WP-4.17）：聚落级容量（多住房不叠加）+ 拆住房减员
			var populationModel = tables.Buildings == null ? null : new PopulationModelService(mapService, tables.Buildings, domainEvents);
			construction?.AttachPopulationModel(populationModel);

			// 6.15) 信息与门控（v0.8.9 / WP-4.13 + WP-4.14）：产出归因 + UI 门控
			var attribution = new ProductionAttributionService(modifierRepo, mapService, tables.TechTrees);
			var uiGate = tech == null ? null : new UiGateService(tech, tables.TechTrees);

			// 6.16) 意图契约（v0.9.4 / WP-5.7）：表现层唯一入口 —— UI 只表达意图，规则（谁/门控/资源）在这里判
			var intent = new HumanIntentHandler(session, mapService, construction, units, tech, uiGate, events);

			// 6.17) 会话入口（v0.9.6 / WP-5.11）：新开局 / 存档 / 读档 / 退出 + 自动存档点（含 U7 多 owner 读档）
			var entry = new SessionEntryService(session, mapService, orchestrator, worldSave);

			// 6.13) AI 与胜负（v0.7.6 / WP-6.6）：出局即停（不再决策/下单）
			ai.AttachVictory(victory);

			return new CoreServices
			{
				Session = session,
				Time = timeService,
				ConfigSource = dependencies.ConfigSource,
				Tables = tables,
				ConfigReport = report,
				Appearance = tables.Appearance,
				MapGenerator = mapGenerator,
				Map = mapService,
				DomainEvents = domainEvents,
				SaveStore = store,
				Tasks = tasks,
				Resources = resources,
				Modifiers = modifiers,
				Tech = tech,
				Fog = fog,
				Construction = construction,
				Units = units,
				Events = events,
				Settlement = settlement,
				WorldSave = worldSave,
				Orchestrator = orchestrator,
				Setup = setup,
				Victory = victory,
				Ai = aiConfig,
				AiService = ai,
				AiEconomy = aiEconomy,
				AiMilitary = aiMilitary,
				Population = populationModel,
				Attribution = attribution,
				UiGate = uiGate,
				Intent = intent,
				Entry = entry,
				I18n = i18n,
			};
		}

		/// <summary>
		/// （v0.7.1 / WP-6.1）按 AI 配置生成玩家表：**1 个人类（owner=1）+ `Count` 个 AI（owner=2..）**。
		/// <para>出生点规则对所有人相同（`SessionSetupService`），AI 与人类的唯一差别是"谁下指令"（`D88`）。</para>
		/// </summary>
		private static List<PlayerContext> BuildPlayers(IAiConfig aiConfig)
		{
			var players = new List<PlayerContext> { PlayerContext.Human(PlayerContext.FirstOwnerId) };
			int count = Math.Max(0, aiConfig?.Count ?? 0);

			for (int i = 0; i < count; i++) players.Add(PlayerContext.Ai(PlayerContext.FirstOwnerId + 1 + i));
			return players;
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

