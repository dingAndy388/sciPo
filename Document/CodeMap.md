# Science Potato · 代码地图（活文档 · v0.7.4）

> **用途**（工作流 W5）：把"改 X 要同时改哪几处、入口在哪、对应用例叫什么"写在一页纸上，
> 避免每个 WP 都重新 grep 同一批链路。**发现新链路就补进来**，过期条目立刻删（不堆叠）。
> 相关：`log.md` §19（状态与判据）· `Document/TestPlan.md`（怎么验）· `Document/ConfigTableGuide.txt`（怎么填表）。

## 1. 装配与宿主

| 关注点 | 唯一入口 | 同时要改的地方 |
| :--- | :--- | :--- |
| **组合根**（新增服务/仓储） | `Scripts/Core/CoreBootstrap.cs` 的 `Build()` | ① `CoreServices` 加只读属性 ② `CoreDependencies` 加注入项 ③ `WiringChecks` 加一条装配断言 |
| 宿主（Godot autoload） | `Scripts/Autoload/ServiceContainer.cs`（`_Ready`） | 装配自检清单（`ReportWiring`）；`Scene/Autoload/service_container.tscn` 的脚本路径 |
| 会话状态 | `Scripts/Core/GameSession.cs`（玩家表）/ `MapSession`（地图缓存） | 玩家表变化要同步 `WiringChecks` + `SessionSetupChecks` |
| 会话级启动链 | `Scripts/Core/SessionOrchestrator.cs` → `SessionSetupService.Setup` → 逐 owner 资源池/月结/事件/AI 决策 | 新子系统要挂在 `StartMap` 里（**不要**让调用方自己记得调）；人类 / 非人类的差别只在这里分（`IsHuman`） |
| 开局布置 | `Scripts/Core/SessionSetupService.cs` + `Config/Generator.json` 的 `Start` 段 | `SessionOrchestrator.AttachSetup`（装配）、`SessionSetupChecks` |

## 2. 配置表（7 张 + 1 组文本）

| 表 | 文件 | DTO / 仓库 | 接口 | 派生出的东西 |
| :--- | :--- | :--- | :--- | :--- |
| Terrains | `Config/Terrains.json` | `TerrainConfigDto` / `TerrainsConfigRepository` | `ITerrainData`、`IMapAppearanceConfig`（表根外观段） | `ConfigTables.Appearance`（格子步长/贴图目录） |
| Resources | `Config/Resources.json` | `ResourcesPoolConfigDto` | `IResourceConfig`、`ISettlementConfig` | 资源名 = 各处费用的 key；`{资源名}Growth/Limit` 目标名 |
| Buildings | `Config/Buildings.json` | `BuildingConfigDto` | `IBuildingConfig` | 建造/升级/产出入池/训练名单/维护费 |
| Units | `Config/Units.json` | `UnitConfigDto` | `IUnitConfig` | 单位属性/维护/掉落/敌方刷新 |
| TechTrees | `Config/TechTrees.json` | `TechTreeConfigDto` | `ITechNodeConfig` | 跨树前置、科技解锁 |
| Events | `Config/Events.json` | （`EventsConfigDto` 根对象） | `IEventConfig` | 触发概率/前置/Modifier |
| Generator | `Config/Generator.json` | `GeneratorConfigDto` | `IMapGeneratorConfig`、`IStartSetupConfig`（`Start` 段） | `ConfigTables.Start`（出生点/开局单位） |
| AI | `Config/AI.json`（策略参数：几个 AI / 不造兵窗口 / 分配比例 / 威胁阈值） | `AiConfigDto` / `AiConfigLoader` | `IAiConfig`（`CoreServices.Ai`） | 玩家表（`AI.Count` → 1 人类 + N AI） |
| 文本 | `Config/Strings.{zh,en}.json` | `I18nService.Load` | `II18nService` | UI 键；`ConfigFixtures.TextResources` 要同步 |

> **集合字段陷阱**：JSON 集合**不要**在 DTO 里预置默认值（Newtonsoft 默认 append，会读成两份，`D89`）。

## 3. 子系统入口

| 子系统 | 服务（入口方法） | 关键类型 | 用例组 |
| :--- | :--- | :--- | :--- |
| 时间 | `CoreServices.Time`（`Register`/`Reset`）、`GameClock` | `IntervalTask` / `LinearTask` / `IProgressTask` | `Clock`、`TimeBaseline`、`TaskLifecycle` |
| 地图 | `MapAppService`（`GenerateMap`/`GetAllCells`/`SetOccupant`/`IsClear`/`GetTerrainRequirement`） | `Map`、`MapCell`、`MapSession`、`TerrainSetRequirement` | `MapSession`、`Occupancy`、`Movement` |
| 建造（含 HP/夺取） | `ConstructionAppService`（捕获消费：修正器移交给新主人） | `IDamageable`、`Building.HP/CaptureBy`、`Map.ApplyBuildingDamage`、`BuildingCapturedEvent`/`BuildingRemovedEvent` | `BuildingHp`、`ContentBuilding` |
| 建造 | `ConstructionAppService`（`StartConstruction`/`UpgradeBuilding`/`RemoveBuilding`） | `Building`、`BuildingFactory`、`BuilderBinding` | `Builder`、`Upgrade`、`ContentBuilding` |
| 单位 | `UnitsAppService`（`TrainUnit`/`CreateUnit`/`PlaceInitialUnit`/`ExcuteAction`） | `Unit`、`UnitFactory`、`UnitMovementService`、`UnitCombatService`、`UnitLootService` | `TrainingQueue`、`Movement`、`Combat`、`Loot` |
| 资源 | `ResourcesAppService`（`GetOrCreatePool`/`AddResource`/`RefreshLimits`） | `ResourcesPool`、`ResourcesConsumption` | `ConfigTable`、`ContentBuilding` |
| 修正器 | `ModifierAppService`（`GetValue`） | `ModifierManager`、`IModifierRepository` | `ConfigTable`、`BuildingProduction` |
| 科技 | `TechTreesAppService`（`CanResearch`/`StartResearch`） | `TechTree`、`TechNode` | `TechPrerequisite`、`TechTreeConcurrency` |
| 事件 | `EventAppService`（`StartEventsEngine`/`GetActiveEvents`） | `ActiveEvent` | `EventEngine` |
| 迷雾 | `FogAppService`（`RevealArea`/`GetVisibility`/`Load`） | `FogSaveData` | `SessionSetup` |
| 月结 | `MonthlySettlementService`（`StartSettlement`/`Settle`） | `IUpkeepDemandSource`、`IPopulationSink`、`MonthlySettlementReport` | `MonthlySettlement`、`PopulationGrowth` |
| 存档 | `WorldSaveService`（`SaveWorld`/`LoadWorld`） | `ISaveStore`、`SaveMapper`、`SaveRebuilder` | `WorldPersistence`、`SaveUnit` |
| 玩家/开局 | `SessionOrchestrator`、`SessionSetupService` | `PlayerContext`、`PlayerSpawn`、`PlayerStartReport` | `Wiring`、`SessionSetup` |
| 胜负 | `VictoryService`（`Evaluate`/`OutcomeOf`/`IsAlive`） | `PlayerStatus`、`GameOutcome` | `Victory` |
| AI 决策 | `AiService`（`StartEngine`/`Observe`/`Decide`/`Evaluate`/`DecisionsOf`） | `AiDecision`、`AiObservation`、`AiFocus`、`AiThreatLevel`、`IAiConfig` | `AiConfig`、`AiDecision` |
| AI 经济 | `AiEconomyService`（`Execute(decision)`；实现 `IAiActionSink`） | `AiEconomyResult`、`BuilderBinding`（工人占用）、`StartConstruction`/`Research`（**只走玩家同一套服务**） | `AiEconomy` |
| 外观/表现 | `MapView`（`UpdateAllCells`）、`MapCellView`（`Configure`/`SetTerrain`）、`CameraController` | `IMapAppearanceConfig`、`TerrainAppearance`、`RgbColor` | `Appearance` + 冒烟 |
| i18n | `II18nService`（`T`） | `I18nService`、`I18nMarkers` | `I18n` |

## 4. 表现层与场景

| 场景/脚本 | 作用 | 配套 |
| :--- | :--- | :--- |
| `Scene/Map/map_view.tscn`（主场景） | 地图渲染 + 相机 + 开发面板 | `MapView.cs`、`CameraController.cs`、`DevMapUi.cs` |
| `Scene/Map/map_cell_view.tscn` | 单格视图（Sprite2D 节点名必须是 `Sprite2D`；缺图时脚本再挂一个 `Polygon2D` 六边形占位） | `MapCellView.cs`、`TerrainAppearance.HexOutline` |
| `Scene/Dev/smoke_report.tscn` | **无头冒烟**（三层验收第 3 层） | `Scripts/Dev/SmokeReport.cs`；参数 `--smoke` / `--size=WxH` / `--seed=N` / `--ai=N` |

## 5. 三条命令（提交前必跑）

```powershell
# W1：一条命令跑完三层（默认 4 路并行分片；日志在 %TEMP%\sp-verify.txt）
powershell -File Tools/verify.ps1
# 快速单组（分钟级反馈）
powershell -File Tools/verify.ps1 -Groups MonthlySettlement -SkipSmoke
# 排查闪烁时串行
powershell -File Tools/verify.ps1 -Serial
```

> **不要**用 `dotnet run`（无 `--no-build` 时全在等 MSBuild，实测 15 s+）；统一 `dotnet build` → 直跑 `Tests\…\bin\Debug\net8.0\SciencePotato.HeadlessChecks.exe`。
> Godot 跑的是 `.godot/mono/temp/bin/Debug` 里的程序集：**改完代码必须先 build 再跑冒烟**。
