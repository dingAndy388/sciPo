# Science Potato · 测试计划（活文档 · v0.6.0）

> **用途**：回答"改完怎么知道没坏"。三层验收的顺序、命令、判据与用例地图都在这里；
> 状态与 WP 归属见 `log.md` §19（活文档），决策见 `log.md` §20。
> **纪律**：新增 WP **只加不减**验收项；任何"临时跳过"的检查都必须在本文件登记一行（含原因与恢复条件）。

## 1. 三层验收（每批交付前都必须全绿）

| 层 | 命令 | 判据 | 覆盖什么 | 覆盖不到什么 |
| :--- | :--- | :--- | :--- | :--- |
| ① 构建 | `dotnet build 'Science Potato.csproj'` | **0 error** | 类型/接口/命名空间口径 | 运行期行为 |
| ② 无头检查 | `dotnet run --project Tests\SciencePotato.HeadlessChecks` | **退出码 0**，末尾 `结果：通过 N / 失败 0` | 纯 C# 核心：时钟/配置/地图/建造/单位/战斗/月结/存档/装配/玩家表 | 引擎适配层（资源加载、autoload、场景、`user://`） |
| ③ Godot 无头冒烟 | 见 §2 | **退出码 0** 且 stdout 含 `[SMOKE] 汇总：通过 17 / 失败 0` | 真引擎：装配自检、**外观配置链路比对**、资源加载、73×143 生成、逐 owner 子系统启动、`user://` 存档往返、**表现层端到端（实例化主场景 + 渲染 10439 格）** | 视觉/手感/数值体感/AI 像不像人 |

人只看四类主观项（`log.md` §19.1 的 **N1**~**N4**）：视觉与操作手感、数值手感、AI 行为、美术与发布。

## 2. 复现命令（可直接粘贴）

```powershell
# ① 构建
dotnet build 'Science Potato.csproj'

# ② 无头检查（**194 条**；退出码 0 = 全通过；也支持 `exe <组名子串>` 只跑几组）
dotnet run --project Tests\SciencePotato.HeadlessChecks

# ③ Godot 无头冒烟（`--smoke` = 独立存档 user://save/smoke.json，且每次运行先删旧档）
$exe = 'E:\Godot_v4.6-stable_mono_win64\Godot_v4.6-stable_mono_win64.exe'
& $exe --headless --path 'e:\Godot\science-potato' res://Scene/Dev/smoke_report.tscn `
       --quit-after 3000 --log-file "$env:TEMP\sp_smoke.log" -- --smoke

# ③b 换规模 / 换种子（`WP-5.8`；用于性能基线与大地图自查）
& $exe --headless --path 'e:\Godot\science-potato' res://Scene/Dev/smoke_report.tscn --quit-after 6000 -- --smoke --size=73x143 --seed=20260914

# ③c 多势力口径（加 1 个 AI：验"每个势力各自一套子系统"；AI 决策属批次 6）
#   期望：玩家表 2 个势力 · AI 行 `事件=False`（AI 不面对事件）· 周期任务 9 条（2×(月结1+成长3)+人类事件1）· 月结 6 次
& $exe --headless --path 'e:\Godot\science-potato' res://Scene/Dev/smoke_report.tscn --quit-after 3000 -- --smoke --ai=1
```

> **已知缺口（`log.md` §19.5 `U7`）**：`--ai=1` 时读档只恢复 4 条任务（人类那一份）—— `WorldSaveService.LoadWorld(mapId, ownerId)` 目前是单 owner 签名，多势力读档归 `WP-5.11`。冒烟因此仍以**单势力**作为基线。

> 注意：Godot 侧运行的是 `.godot/mono/temp/bin/Debug` 里的程序集 —— 改完 C# **必须先 ① 构建再跑 ③**，否则会拿旧程序集跑出"改了没生效"的假象。

## 3. 用例地图（`Tests/SciencePotato.HeadlessChecks`）

| 用例文件 | 覆盖 | 关键 WP |
| :--- | :--- | :--- |
| `ClockChecks` / `TimeBaselineChecks` | 游戏日派发 / 三档流速 / 暂停 / 单帧上限 | `WP-1.1`/`1.2`/`1.5` |
| `ConfigChecks` | 地形 JSON 可解析与字段口径（M0-1 ②） | `WP-0.4` |
| `ConfigTableChecks`（v0.6.3 增补） | 7 张表装载 / 引用校验 / 分级处置 + **资源口径=设计稿**（名/初始/上限/修正器目标）+ **旧别名残留** | `WP-1.4`、`WP-7.1` |
| `CoreBootstrapChecks` | 组合根快速失败与最小装配 | `WP-1.3` |
| `WiringChecks`（v0.6.0 新增） | **装配通电**（全部服务 + 存档接线）· **玩家表** · **`SessionOrchestrator`** 逐 owner 启动 / 幂等 / 事件只对人类 | `WP-5.1`、`WP-4.18` |
| `ContentBuildingChecks`（v0.6.4 新增） | **生产建筑内容**：农田 144/年 · 矿场 100/月 · 仓库抬高上限（幂等）· 造价/耗时=设计稿 · **多点地块任一匹配**（`D86` 回归锁）· 初始储备与造价张力 | `WP-7.2a` |
| `AppearanceChecks`（v0.6.1 新增） | **外观数据驱动**：格步长/目录来自配置、颜色解析、贴图路径解析、缺美术兜底、格位换算与原型逐像素一致 | `WP-5.3` |
| `MapSessionChecks` / `WorldPersistenceChecks` / `SaveUnitChecks` | Map 常驻内存 / 存档等价 / 统一存档单元 | `WP-3.1`~`3.3` |
| `OccupancyChecks` / `BuilderChecks` / `UpgradeChecks` | 占据物与拆除 / 建造者绑定 / 升级链 | `WP-2.4`~`2.6`、`WP-3.4` |
| `MovementChecks` / `CombatChecks` / `EnemySpawnChecks` / `LootChecks` | R1~R7 移动 / 同格交战 / 敌方封锁 / 掉落进池 | `WP-3.5`~`3.8` |
| `MonthlySettlementChecks` / `PopulationGrowthChecks` | 月结四段式 / 赤字减员 / 人口增长 | `WP-2.3`、`WP-3.9`、`WP-3.10` |
| `EventEngineChecks` / `TechPrerequisiteChecks` / `TechTreeConcurrencyChecks` | 事件按日掷骰 / 跨树前置 / 研究并发 | `WP-2.1`、`WP-2.8`、`WP-2.9` |
| `DomainEventChecks` / `TaskLifecycleChecks` / `TrainingQueueChecks` | 领域事件总线 / 任务生命周期 / 训练队列 | `WP-2.2`、`WP-2.10` |
| `BuildingProductionChecks` | 建筑产出入池 + 修正器回收 | `WP-2.7` |

## 4. 冒烟报告读法（`Scene/Dev/smoke_report.tscn`）

一行一条结论，`[SMOKE][FAIL]` 只出现在失败时；退出码即布尔结论。当前基线（v0.6.0）：

| 行 | 基线值 | 说明 |
| :--- | :--- | :--- |
| `[SMOKE][BASE] size=73x143=10439 cells …` | 见 `Document/PerfBaseline.md` §2 | 一次跑出**全部**耗时/内存/存档体积（改动生成/渲染/存档后对比这一行） |
| `装配：全部应用服务就位` | PASS | 11 个服务 + 任务仓储 |
| `外观比对：表 CellXStep=366 … 服务 366/317.25/…` | PASS | **配置 → 服务 → 表现层**链路自证（换美术只改表） |
| `[MapView] 外观：列步长=366 行步长=317.25 贴图目录=…` | — | 表现层实际读到的值 |
| `[MapCellView] 缺少地形贴图 … 用占位色 #7BA05B 渲染` | 每种地形 1 条 | 缺美术时的兜底：画纯色块 + 只警告一次（不崩、不刷屏） |
| `装配：全部应用服务就位` | PASS | 11 个服务 + 任务仓储 |
| `地图 smoke：N 格，生成耗时 M ms` | **10439 格 / 220~310 ms** | `R1` 的第一个规模数据点（`WP-5.8` 建完整基线） |
| `表现层：渲染 10439 格耗时 M ms` | **~944 ms**（含 10439 个 `Node2D`+`Sprite2D` 实例化） | `R1` 的渲染规模数据点；贴图全缺时**只按地形告警一次**、不崩（缺图兜底 + 警告去重） |
| `表现层：MapView 取到 MapAppService（旧版必 NRE）` | PASS | `WP-5.2` 的核心回归：旧代码 `_mapQuery` 从未赋值 |
| `周期任务：实际 5 条` | 月结 1 + 事件 1 + 资源成长 3 | 与资源表 `GrowInterval > 0` 的条数一致 |
| `玩家1(owner=1,人类) 资源池=True 月结=True 事件=True` | — | 逐势力启动明细 |
| `推进 90 日：月结次数=3，掷骰次数=270` | 3 / 270 | 月结按 30 日、事件按日（3 条事件 × 90 日） |
| `玩家资源池：Gold=3，Wood=1.5，Idea=0` | 3 个月的自然增长 | 与 `Resources.json` 的 `BaseGrowth` 一致（产出只由成长任务入池，月结只**汇总报告**，不重复入池） |
| `存档往返：loaded=True，恢复任务 4 条` | — | 月结 1 + 资源成长 3（事件引擎在恢复时单独重挂） |

## 5. 已知未覆盖 / 待补（登记制）

| # | 未覆盖 | 为什么现在不覆盖 | 补的时机 |
| :--- | :--- | :--- | :--- |
| 1 | 视觉与操作手感（缩放/平移是否顺手、73×143 上能否看清） | 需要人看 | **N1**（M1 后） |
| 2 | 数值手感（产出/维护/时长是否合理） | 需要人玩 | **N2** |
| 3 | AI 行为像不像人 | 需要人看 | **N3**（批次 6 后） |
| 4 | 导出包（`--export-release`）里资源是否齐全 | 尚无导出模板 | `WP-8.4` |
| 5 | 迷雾在 10439 格上的性能 | 迷雾尚未按 owner 拆分 | `WP-4.10`、`WP-5.5` |
| 6 | 多 AI 势力的完整对局 | AI 决策未实现（当前只有"资源 + 月结"） | 批次 6 |

## 6. 维护约定

1. **只加不减**：新增 WP 必须带来新的断言或新的冒烟行；删掉一条断言要在本文件登记原因。
2. **装配类 WP 必须补 `WiringChecks` 断言**（`log.md` `D81`）：组合根少装一个服务就要能红。
3. **判据写进 `log.md` §19.3**，本文件只写"怎么跑、期望什么"，避免两处口径打架。
4. 冒烟基线值变更时，同步更新 §4 表格与 `log.md` §19.6 的进度行。
