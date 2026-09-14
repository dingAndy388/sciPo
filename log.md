# Science Potato 架构诊断日志

> **文件用途**：记录对现有代码库（commit `c6dda9a` "core engine MVP"）做架构审查后发现的**全部问题**，作为后续改造/修复的依据清单。每条问题包含：现象 → 证据（`文件:行号`）→ 根因 → 影响 → 修复方向 → 关联项。
>
> **诊断范围**：`Scripts/`（98 个 .cs）、`Config/*.tres`、`Document/*.json`、`Scene/*.tscn`、`project.godot`、`Science Potato.csproj`、`UML/*.txt`
> **诊断方法**：静态阅读 + 全仓库正则交叉检索（**未运行游戏，未做运行时验证**）
> **诊断阶段**：架构可行性评估【第一阶段】产出；【第二/三/四阶段】（设计稿比对、差距分析、改造清单）完成后需回来修订本文档
> **写入时间**：2026-09-14

---

## 0. 如何阅读本文档

1. **先看第 1 节分级标准**，再按级别从 P0 往下读。
2. **第 2 节总览表**给每条问题一个 ID（如 `MAP-02`）；后续讨论、拆 PR、写设计稿时请直接引用 ID。
3. **第 3 节详细诊断**是主体，每条问题独立成块，可单独复制成 issue。
4. **第 4 节**是"契约与实现的错位表"、**第 5 节**是"配置表 ↔ DTO ↔ 代码 交叉核对"，修数据层缺口时先查这两节。
5. **第 6 节**归纳公共根因（多个 P0/P1 同源）；**第 7 节**是 v0.1 的初步修复路线（已被第 13 节取代）；**第 8 节**是待确认/未验证项。
6. 本文档**只做诊断与方向性判断，不含实现代码**。
7. **第 10~17 节为第二/三/四阶段产出（v0.2）**：设计稿解读与规格锁定（10）· 差距分析 93 行（11）· 新增问题 21 条（12）· 改造清单 49 个工作包（13）· 里程碑 M0/M1/M2 与验收标准（14）· 配置表字段增量规格（15）· 装配方案（16）· 既有条目修订与统计变更（17）。

---

## 1. 分级标准

| 级别 | 定义 | 处理时机 |
| :--- | :--- | :--- |
| **P0 阻塞级** | 不解决则项目核心目标（内容数据驱动 / 多玩家 / 4X 存档一致性）**无法成立**；或当前处于"编译通过但功能不通电"状态 | 必须最先做，且阻塞其它所有改造 |
| **P1 严重级** | 功能性缺陷：数据丢失、必崩路径、资源泄漏、逻辑与配置/文档不符、多玩家必炸 | 紧随 P0，直接影响可玩性 |
| **P2 中等级** | 健壮性、性能、一致性、可维护性问题；不修也能跑，但持续放债 | P1 之后批量处理 |
| **P3 轻微级** | 命名/整洁性/文档不一致；影响可读性与协作效率 | 顺手清理，不单独立项 |

**分类维度**（每条问题同时标注）：
- `数据层缺口` = 配置表结构 / 序列化契约需要改
- `逻辑层缺口` = 需要新增或重写 C# 类
- `装配层缺口` = 依赖注入 / 生命周期 / 场景接线需要补
- `架构层缺陷` = 分层、依赖方向、聚合根职责问题

---

## 2. 问题总览表

**统计**：共 **97 条**（P0: **10** / P1: **43** / P2: 37 / P3: 7）

> ⚠️ **v0.2 修订说明**：v0.1 的 79 条中，**3 条已并入他条**（`TIME-06`→`TIME-13`、`UNIT-02`/`UNIT-03`→`UNIT-10`），**2 条升级**（`TIME-05`→P0、`DEP-07`→P0）、1 条升级（`CON-05`→P1）、1 条降级（`TECH-02`→P2）；
> 另有 **21 条为 v0.2 新增**（见第 12 节）。下表 2.1~2.4 仍为 v0.1 原始条目，逐条修订记录见第 17.1 节，最终统计见第 17.2 节。

### 2.1 P0 阻塞级（v0.1 原 8 条；v0.2 曾为 18 条；**v0.3 收敛为 10 条**，见 §17.3）

| ID | 模块 | 问题 | 类型 |
| :--- | :--- | :--- | :--- |
| `WIRE-01` | 装配 | 组装层只接通 Map，其余 7 个模块（Construction/Units/Resources/TechTrees/Events/Fog/Modifier）的 AppService/Factory/Repository 全无实例化点 | 装配层缺口 |
| `WIRE-02` | 装配 | `GodotTimeService` 从未实例化、未入场景树、`SetTaskRepository` 从未调用 → 时间系统整体不通电 | 装配层缺口 |
| `WIRE-03` | 装配/配置 | 5 张 JSON 配置表（`Document/*.json`）**没有任何加载入口**；`GenericConfigRepository` 需要的 json 字符串无来源 | 装配层缺口 + 数据层缺口 |
| `MAP-01` | Map | Map 没有运行时内存态：`MapAppService` 每个方法都 Load→改→Save，聚合根实际是"磁盘往返的 DTO" | 架构层缺陷 |
| `MAP-02` | Map | 存档**只存地形**（position + terrain），建筑/单位/人口/任务全部不落盘且不可序列化 → 读档即丢世界 | 数据层缺口 + 架构层缺陷 |
| `TIME-01` | 时间 | `GodotTimeService._Process` 对**每个** IProgressTask **每帧写一次 JSON 文件** | 架构层缺陷 + 性能 |
| `EVT-01` | 事件 | `Document/EventsConfig.json` 是裸数组，与 `EventsConfigDto` 要求的 `{"Events":[...]}` 不匹配 → 事件表加载为空 | 数据层缺口 |
| `TIME-02` | 时间 | `IntervalTask` 永不自动注销，且完成时不清账 → 订阅列表与任务文件无限增长 | 逻辑层缺口 |

### 2.2 P1 严重级（v0.1 原 29 条；v0.2 后 35 条；**v0.3 后 43 条**，见 §17.3）

| ID | 模块 | 问题 | 类型 |
| :--- | :--- | :--- | :--- |
| `WIRE-04` | 配置 | `GodotConfigService.Load/LoadAll` 无存在性/空值保护，且只能读 `.tres/.res`，无法读 JSON | 逻辑层缺口 |
| `WIRE-05` | 表现层 | `MapView._mapQuery/_modificationService` 从未注入，`UpdateAllCells` 必 NullReference | 装配层缺口 |
| `MAP-03` | Map | `Map.GetCell`/`GetOccupantByUId` 用 Dictionary 索引器（缺 key 直接抛异常），却被 AppService 无保护调用 | 逻辑层缺口 |
| `MAP-04` | Map | `AddOccupant`/`SetBuilding`/`SetInvader` 三套并行写槽位，`TryGetValue` 判断无效，`MapCell.Building` 槽位无人写入 | 架构层缺陷 |
| `MAP-05` | Map/配置 | 地形资产 Id 是 `test1~test5`，JSON 配置要求 `"plain"` → 所有 TerrainRequirement 永不满足，建造/训练全被拒 | 数据层缺口 |
| `MAP-06` | Map/配置 | 5 个地形 `.tres` 全部缺 `MoveCost`（float 默认 0）→ **已探索区全部不可通行**（未探索区反而可走） | 数据层缺口 |
| `MAP-07` | Map | `SaveMap` 每次全量重写整张地图；叠加 `MAP-01` 后 IO 复杂度 = O(操作数 × 格数) | 架构层缺陷 + 性能 |
| `TIME-03` | 时间 | 任务快照以 `task.Id` 为字典键 → 同名任务静默互相覆盖（建造用 BuildingId、训练用 UnitId 作 Id） | 逻辑层缺口 |
| `TIME-05` | 时间 | 只有"秒"，没有游戏日/回合概念；`Scale` 无人设置；Domain 无法脱离 Godot 自驱 | 架构层缺陷 |
| `MOD-01` | Modifier | `GetValue` 的 `return` 写在 target 循环内部 → 多 target 只有第一个生效 | 逻辑层缺口 |
| `MOD-02` | Modifier | 数值公式硬编码 `(base+abs)*(1+per)`，无优先级/上限/叠乘策略/互斥 | 逻辑层缺口 |
| `MOD-03` | Modifier | 作用域仅 (mapId, ownerId)，无对象级作用域 → 无法给单个单位挂临时 buff，也无法表达全局环境状态 | 架构层缺陷 |
| `CON-01` | 建造 | 条件检查与资源扣除非原子、无回滚；失败静默 `void` 返回，`UML` 设计的 `BuildFailed(ErrorMsg)` 未实现 | 逻辑层缺口 |
| `CON-02` | 建造/单位 | 能力分发靠 `switch` 白名单字符串，新增动作必须改核心 switch（开闭原则不满足） | 架构层缺陷 |
| `CON-03` | 建造 | `ResumeConstruction` 续跑逻辑与首次完成逻辑不一致（丢视野、丢人口任务） | 逻辑层缺口 |
| `CON-04` | 建造 | 人口增长任务把 `OwnerId` 写成 0 → 任务快照归属错误，多玩家/续跑必错 | 逻辑层缺口 |
| `UNIT-01` | 单位 | `UnitFactory` 参数语义错位（`config.Movement` 被当 MP 传入并被复用）；`config.Attack` 是死字段 | 逻辑层缺口 |
| `UNIT-02` | 单位 | MP 上限被压成 `MoveRechargePerTick`，大于该值的地形代价将导致单位永久卡死 | 逻辑层缺口 |
| `UNIT-03` | 单位 | 移动节拍 10 秒 / 攻击节拍 1 秒，与"连续时间驱动"目标不符，且节拍硬编码 | 架构层缺陷 |
| `UNIT-04` | 单位 | 攻击任务每次攻击都新建且永不注销，目标死亡后仍空转 | 逻辑层缺口 |
| `UNIT-05` | 单位 | 单位移动只改自身 `Position`，不同步 `MapCell.Occupant` → `IsClear`/`GetOccupantInfo` 返回过期数据 | 逻辑层缺口 |
| `UNIT-09` | 单位 | 单位完全没有持久化层；`IUnitsRepository` 名字像聚合仓库，实际只是配置读取接口 | 架构层缺陷 + 命名 |
| `TECH-01` | 科技 | `CreateResearchTask` 把 `nodeId` 当 `treeId` 用 → 读档后科技树数据错乱 | 逻辑层缺口 |
| `TECH-02` | 科技 | 研究消耗硬编码 `"Idea"`；节点配置只有 `float Cost`，无法配置多资源成本 | 数据层缺口 |
| `FOG-01` | 迷雾 | `FogAppService` 是有状态对象且 `ownerId` 由构造函数注入 → 多玩家必然数据串台；却被多模块共享同一实例 | 架构层缺陷 |
| `DEP-01` | 依赖 | Domain 反向依赖：`Common.Domain.PopulationConsumption` 引用 `Map.Domain.MapCell`（Common → Map） | 架构层缺陷 |
| `DEP-02` | 依赖 | AppService 网状互相注入（Units → Construction → Resources/TechTrees/Map/Fog/Modifier），无编排层/事件总线 → 循环依赖隐患 | 架构层缺陷 |
| `DEP-03` | 依赖 | 无 Session/GameContext，`mapId`/`ownerId` 逐方法字符串传参 → 多玩家/多地图无隔离边界 | 架构层缺陷 |
| `EVT-02` | 事件 | 实现是"每秒判定"，文档写"每天判定"，且永久事件无撤销入口 | 逻辑层缺口 |

### 2.3 P2 中等级（v0.1 原 35 条；v0.2 后共 37 条，新增见第 12 节）

| ID | 模块 | 问题 |
| :--- | :--- | :--- |
| `WIRE-06` | 装配 | 无 DI 容器、无组装规范，全靠手写 `new`；`GodotConfigService` 在同一处被 new 两次 |
| `MAP-08` | Map | `DeleteMap`/`ListMaps` 抛 `NotImplementedException` |
| `MAP-09` | Map | `SaveMap` 在 `Terrain == null` 时 NRE |
| `MAP-10` | Map | `HexCubePosition` 是**可变** struct 却实现 Equals/GetHashCode 并用作字典键 |
| `MAP-12` | Map | `MapCell` 只有 3 个 occupant 槽位且语义模糊，无法表达多建筑/多部队/叠加地形效果 |
| `MAP-13` | Map | A* 用空 List 同时承担"无路径"与"到达"两种语义 |
| `TIME-04` | 时间 | `RemoveTask` 把 `null` 写进任务文件，回读时列表含 null |
| `TIME-06` | 时间 | 节拍硬编码散落各处（事件 1s、移动 10s、攻击 1s、资源/人口按配置） |
| `TIME-07` | 时间 | `ITimeService` 的实现是 Godot `Node`，Domain 层无法在纯 C# 环境驱动 |
| `TIME-08` | 时间 | 任务快照与实体状态不同源（配合 `MAP-02`），无存档一致性保证 |
| `TIME-09` | 时间 | `LinearTask` 完成后仍留在订阅列表中，直到手动 Unregister |
| `MOD-04` | Modifier | 无长驻 `ModifierManager`，每次操作都 Load→new→Save |
| `MOD-05` | Modifier | `Type` 用字符串判定，非 `"Percent"` 的值静默降级为 Absolute；`Modifier` 是可变 struct |
| `MOD-06` | Modifier | `RemoveModifierByTarget` 命名与语义不符（无法按 target 批量清除） |
| `MOD-07` | Modifier | 无叠加护栏：`Duration=0` 的 Modifier 永久残留，且无 UI/日志可观测 |
| `CON-05` | 建造 | 人口任务无上限、无注销；`Population` 不落盘 |
| `CON-06` | 建造 | 建筑被移除时不清理其注册的循环任务（人口任务继续跑） |
| `CON-07` | 建造/单位 | 建筑表独有 `IsHousing/Population*` 字段，单位表无等价抽象，表结构不对称 |
| `UNIT-06` | 单位 | `HasEnemyInRadius` 把 `VisionRadius` 当停火线，与 `AttackRadius` 语义混淆 |
| `UNIT-07` | 单位 | 近战寻位只取第一个可进入邻居，可能进入敌格/与目标重叠 |
| `UNIT-08` | 单位 | 单位阵亡无事件、无回调 → 遗言/经验/战报无处挂 |
| `TECH-03` | 科技 | `Research()` 不校验 `CanResearch` 就扣费，可能白扣费（内部静默失败） |
| `TECH-04` | 科技 | 研究任务 `UId = "none"`，与地图/实体无关联 |
| `TECH-05` | 科技 | 科技完成无任何"推送"通知（无事件/无回调），UI 只能轮询 |
| `TECH-06` | 科技 | 反序列化后 `TechNode.Config` 为 null，必须靠 `HydrateConfigs` 手动回填（隐式契约） |
| `EVT-03` | 事件 | `StartEventsEngine` 无幂等保护，重复调用会注册多个事件循环 |
| `EVT-04` | 事件 | 事件无触发日志/无 UI 推送/无队列，玩家不可感知 |
| `EVT-05` | 事件 | 先扣资源后挂 Modifier，中途失败无回滚 |
| `FOG-02` | 迷雾 | 迷雾记忆永不回退为 Unexplored，无"遗忘"策略（当前等价永久记忆式） |
| `FOG-03` | 迷雾 | `RevealArea/ResetArea` 每次重算半径枚举，O(r²) 且无缓存 |
| `FOG-04` | 迷雾 | 迷雾存档每次全量序列化整张矩阵 |
| `DEP-04` | 依赖 | 命名空间与目录错位（`TechTrees/` ↔ `TechTree.Domain`；`MapAppService` 混用 `Domain.Map`/`Map.Domain`） |
| `DEP-05` | 依赖 | 命名误导：`IUnitsRepository` 实为配置仓库 |
| `DEP-06` | 工程 | 两套 JSON 库并存（Newtonsoft 用于配置/任务/修正；System.Text.Json 用于地图存档） |
| `DEP-07` | 工程 | 无测试工程、无 CI；Domain 无法脱离 Godot 独立运行（唯一 `IMapRepository` 实现依赖 `FileAccess`） |

### 2.4 P3 轻微级（7 条，v0.2 无变化）

| ID | 模块 | 问题 |
| :--- | :--- | :--- |
| `MAP-11` | Map | `Map.SetTerrain` 取了 `old` 未使用；`HexCubePosition.Translate` 忽略自身坐标（语义 bug） |
| `MOD-08` | Modifier | `ModifierManager` 未实现 UML 图里的 `IModifier` 契约（该接口在代码中不存在） |
| `TIME-10` | 时间 | `ITaskRepository` 只有 CRUD，无按类型/所有者查询，无调试接口 |
| `CON-08` | 建造 | `targetParam` 用 `"treeId:nodeId"` 字符串协议 + `Split` 约定，无结构体承载 |
| `DEP-08` | 工程 | 空目录 `Scripts/Time`、`Scripts/Event`、`Scripts/Research`、`Scripts/Tree`；csproj 排除不存在的路径 |
| `DEP-09` | 工程 | `IRandom.Next(int max, int min)` 与实现 `Next(int min, int max)` 参数顺序矛盾，调用方靠实现语义才正确 |
| `DEP-10` | 文档 | `UML/PackageDiagram.txt` 与代码不一致（`IModifier`、契约归属等） |

---

## 3. 详细诊断

> 格式：**现象 → 证据 → 根因 → 影响 → 修复方向 → 关联**。不含实现代码。

### 3.1 装配与配置（WIRE-*）

#### `WIRE-01` 组装层只接通了 Map —— 【P0｜装配层缺口】
- **现象**：`ConstructionAppService`、`UnitsAppService`、`ResourcesAppService`、`TechTreesAppService`、`EventAppService`、`FogAppService`、`ModifierAppService`、`BuildingFactory`、`UnitFactory`、`GodotTimeService` 以及全部 `*Repository`（除 Map 外）**没有任何实例化点**。它们全部只作为构造函数参数类型存在。
- **证据**：`Scripts/Autoload/ServiceContainer.cs:26-36` 只做了 4 件事：new `GodotConfigService` → new `VoronoiMapGenerator` → new `GodotMapRepository` → new `MapAppService`；全仓库检索 `new .*AppService|new .*Repository|new .*Factory` 仅命中该文件第 31、33 行。
- **根因**：架构演进顺序是"自底向上建模块"（git log：modifiers → units → techtree → resource → time → building），但**自顶向下的组装入口始终没跟上**，`ServiceContainer` 停留在"地图可生成"这一 MVP 里程碑。
- **影响**：① 建造/科技/事件/单位/资源增长这一整条玩法链路目前**完全不通电**；② 任何设计稿里"建筑升级消耗资源""单位阵亡触发遗言"之类的需求，第一步都不是写玩法，而是先把依赖图接起来；③ 因为 `FogAppService` 有状态（见 `FOG-01`），接线方式会直接决定多玩家能不能做。
- **修复方向**：把 `ServiceContainer` 从"手写 4 行"升级为**显式的组合根（Composition Root）**：统一构造全部 Repository/Factory/AppService，明确每个服务是单例还是每玩家一份；同时把 `GodotTimeService` 挂进场景树（见 `WIRE-02`）。**这一项是后续所有改造的前置依赖。**
- **关联**：`WIRE-02`、`WIRE-03`、`FOG-01`、`DEP-02`、`DEP-03`

#### `WIRE-02` 时间系统从未启动 —— 【P0｜装配层缺口】
> ⚠️ **v0.3 降级（P0 → P1）**：不做也能通过 M0-1/M0-2 的 headless 验收（测试用 `ManualTimeDriver`）；Godot 侧驱动是 **M1** 的前置。级别收敛依据见 §17.3。
- **现象**：`GodotTimeService` 是一个 `Node`（靠 `_Process` 驱动），但全仓库没有任何 `new GodotTimeService`，`Scene/Autoload/service_container.tscn` 里也只有一个孤立的 `Node`（无子节点、无脚本挂载时间服务），`SetTaskRepository` 从未被调用。
- **证据**：`Scripts/Common/Infrastructure/GodotTimeService.cs:8`（`: Node, ITimeService`）、`:15-18`（`SetTaskRepository`）、`Scene/Autoload/service_container.tscn:5-6`
- **根因**：`ITimeService` 被设计成"被注入的接口"，但唯一实现是 Godot 节点，而节点必须在场景里或由别的节点 `AddChild` 才能收到 `_Process` —— 组装代码缺失。
- **影响**：即使 `WIRE-01` 接好，所有 `Register` 进去的任务也**不会走 `OnTick`**：建造不完成、资源不增长、事件不触发、单位不动。
- **修复方向**：把时间服务作为 `ServiceContainer` 的子节点创建（或做成第二个 Autoload），并在组装阶段注入 `ITaskRepository`；同时决定 `Scale` 的初始值与修改入口（见 `TIME-05`）。
- **关联**：`WIRE-01`、`TIME-01`、`TIME-05`

#### `WIRE-03` 配置表没有任何加载入口 —— 【P0｜装配层缺口 + 数据层缺口】
- **现象**：5 张 JSON 配置表躺在 `Document/`（`BuildingsConfig.json` / `UnitsConfig.json` / `TechTreesConfig.json` / `ResourcesConfig.json` / `EventsConfig.json`），但**没有任何代码读它们**。
- **证据**：`GenericConfigRepository.cs:11-21` 的构造函数要求传入 `string json`（已反序列化好的字符串），而全仓库检索不到任何 `File.ReadAllText(<配置路径>)`；唯一读文件的工具 `GodotConfigService.cs:10-32` 只能 `ResourceLoader.Load<T>` `.tres/.res`，**不具备读 JSON 的能力**；`ServiceContainer.cs:16,35` 暴露了一个 `ConfigLoader` 属性，但无人使用。
- **根因**：配置层设计了"JSON 字符串 → DTO → 接口"的管道，但**入口段（从磁盘/打包资源取 JSON）缺失**，且 `IConfigLoader` 的职责（Godot Resource 加载）与 JSON 加载需求不匹配。另外 `Document/` 不是 Godot 的 `res://` 资源路径（不在导出包内），生产环境读不到。
- **影响**：所有"只填表就能做"的承诺目前**一条都不成立**；策划改 JSON 在运行时无任何效果。
- **修复方向**：① 决定配置表的最终位置（`res://Config/*.json` 作为打包资源，或 `user://` 支持热更）；② 提供一个 `IJsonConfigProvider`/`FileConfigSource`，负责"路径 → JSON 字符串"，再由组合根喂给各 `*ConfigRepository`；③ 保留 `IConfigLoader` 给 `.tres` 地形与生成器配置（那部分目前工作正常）。
- **关联**：`EVT-01`、`WIRE-01`、`WIRE-04`、`MAP-05`、`MAP-06`

#### `WIRE-04` 配置/资源加载无保护 —— 【P1｜逻辑层缺口】
- **现象**：`GodotConfigService.Load<T>` 直接返回 `ResourceLoader.Load<T>(path)`（找不到即 null，无日志无抛错）；`LoadAll<T>` 直接 `DirAccess.Open(path)` 后 `dir.GetFiles()`（目录不存在时 `Open` 返回 null → NRE）。
- **证据**：`Scripts/Common/Infrastructure/GodotConfigService.cs:11-12`、`:17-19`
- **根因**：加载层没有"失败即显式报错"的约定，调用方（`VoronoiMapGenerator`、`GodotMapRepository`）也默认"一定成功"。
- **影响**：地形 `.tres` 被改名/删除、或地图存档里引用了不存在的地形 Id 时，会以 NRE 或"地形全 null"的形式静默降级（配合 `MAP-09` 会直接崩在存档）。
- **修复方向**：加载层统一"返回 null + 明确错误日志"或"抛业务异常"，并在组合根做一次启动期校验（所有配置表必须能解析、所有被引用的 Id 必须存在）。
- **关联**：`MAP-09`、`DEP-07`

#### `WIRE-05` MapView 依赖从未注入 —— 【P1｜装配层缺口】
- **现象**：`MapView._mapQuery` 与 `_modificationService` 两个字段声明后**从未赋值**，但 `UpdateAllCells()` 直接使用 `_mapQuery.GetAllCells(MapId)`。
- **证据**：`Scripts/Map/Presentation/MapView.cs:11-12`（声明）、`:31`（使用）；`Scripts/Map/Presentation/DevMapUi.cs:21,29`（同级 UI 从 `ServiceContainer.Instance` 取服务，MapView 却没有）
- **根因**：表现层取服务的方式不统一：`DevMapUi` 走静态单例，`MapView` 留了字段却没接。
- **影响**：点 "Generate" 后 `MapView.UpdateAllCells()` 必抛 `NullReferenceException`；当前"地图能显示"的能力实际上是不完整的。
- **修复方向**：统一表现层取服务的方式（推荐走 `ServiceContainer` 的显式只读属性），并在 `_Ready` 里做断言。
- **关联**：`WIRE-01`、`WIRE-06`

#### `WIRE-06` 无组合根规范 / 重复实例化 —— 【P2｜装配层缺口】
- **现象**：同一处代码里 `new GodotConfigService()` 出现两次（第 29、35 行），产出两个不同实例；Repository 内部还会各自 `new GodotConfigService()`（如 `GodotMapRepository.cs:16`）。
- **证据**：`Scripts/Autoload/ServiceContainer.cs:29,35`、`Scripts/Map/Infrastructure/GodotMapRepository.cs:16`
- **根因**：没有依赖注入容器（也不强制引入），但缺少"组合根是唯一 new 的地方"的约定。
- **影响**：配置缓存（`ConfigManager.cs:12` 的 `ConfigCache`）形同虚设；未来做配置热重载时会多份缓存不一致。
- **修复方向**：确立约定——**只有组合根 new 具体类型**，其余一切通过构造函数注入接口；顺手清理重复实例。
- **关联**：`WIRE-01`、`DEP-02`

### 3.2 Map 与持久化（MAP-*）

#### `MAP-01` Map 不是运行时聚合根，而是"磁盘往返的 DTO" —— 【P0｜架构层缺陷】
- **现象**：`MapAppService` 的**每一个**方法都先 `_mapRepo.LoadMap(mapId)` 反序列化出全新的 Map 对象，改完再 `SaveMap`；没有任何字段缓存 Map 实例。`GetCell` / `GetAllCells` 这类纯查询也照样读盘。
- **证据**：`Scripts/Map/Application/MapAppService.cs:23`、`:33-36`、`:40-41`、`:46-47`、`:52-53`、`:58-60`、`:64-65`、`:70-72`、`:76-78`、`:82-84`、`:88-90`、`:94-95`、`:100-102`、`:107`
- **根因**：为了"简单+随时可存"，把 Repository 当成了聚合根本身；`Map` 被当作可序列化 DTO 使用，而"单一数据源（Map 是聚合根）"的设计目标没有落地到内存对象生命周期上。
- **影响**：① **对象身份丢失**——每次 Load 出来的 occupant 都是新实例，若这些实例持有状态（如单位的移动路径、攻击目标），状态在两次调用之间蒸发；② 一张地图上只要超过几十个格子，每次点击都是"全量反序列化 + 全量写盘"，帧率与 IO 不可接受（见 `MAP-07`）；③ 后面写 AI/寻路/战斗时，任何"遍历全部占据物"的操作都要重新读盘。
- **修复方向**：引入 **MapSession/GameSession**（见 `DEP-03`），把 Map 实例常驻内存并明确其生命周期与脏标记；`IMapRepository` 退化为"加载/保存边界"，只在开局、存档点、退出时调用。**这是从"能跑通"到"能做成游戏"的分水岭。**
- **关联**：`MAP-02`、`MAP-07`、`DEP-03`、`TIME-08`

#### `MAP-02` 存档只存地形 —— 建筑/单位/人口全部丢失 —— 【P0｜数据层缺口 + 架构层缺陷】
> ⚠️ **v0.3 降级（P0 → P1）**：完整存档是 **M0-3 ①** 的验收项；M0-2 不考读档（内存态由 `WP-3.1` 覆盖）。级别收敛依据见 §17.3。
- **现象**：地图存档结构只有 `position + terrain` 两个字段；`SaveMap` 也只写这两个字段；`MapCell` 上的 `Occupant`/`Building`/`Invader`/`Population` 全都不落盘。而 `Building`/`Unit` 的字段是 `readonly` 且无 `[JsonConstructor]`，**技术上不可反序列化**。
- **证据**：`Scripts/Map/Infrastructure/HexCubeCellSave.cs:7-8`（仅两字段）、`GodotMapRepository.cs:63-69`（只写 position/terrain）、`Scripts/Map/Domain/MapCell.cs:11-15`（四个运行期状态）、`Scripts/Construction/Domain/Building.cs:14-18`、`Scripts/Units/Domain/Unit.cs:8-11`
- **根因**：存储层只对"地图地形生成结果"负责，**没有为占据物设计任何持久化契约**；`IMapOccupant` 只有一个 `GetInfo()` 快照方法，不承载持久化语义。
- **影响**：**读档即世界重置**。所有与"世界状态持续存在"相关的 4X 核心体验（城市发展、部队保留、战争迷雾进度、生产队列）都无法成立。这是对设计稿影响最大的单条问题。
- **修复方向**：① 为每类 occupant 定义**独立的存档 DTO + 序列化/反序列化路径**（不要试图直接序列化 Domain 实体）；② 每个存档格子扩为"地形 + 占据物列表/槽位 + 人口"；③ 存档时把"实体状态"与"任务快照"在同一事务里写（否则会出现"存档里有建筑、任务列表里没有它"的错位）。
- **关联**：`MAP-01`、`TIME-08`、`CON-05`、`UNIT-09`、`FOG-04`

#### `MAP-03` 字典索引器取格子/占据物，缺 key 直接抛异常 —— 【P1｜逻辑层缺口】
- **现象**：`Map.GetCell` 用 `_cells[position]`；`Map.GetOccupantByUId` 用 `_occupants[uid]`。二者都不判存在性。
- **证据**：`Scripts/Map/Domain/Map.cs:44-47`、`:74-77`；无保护调用点：`MapAppService.cs:53`（`GetOccupantByUId`）、`:103`（`GetCell`）、`UnitsAppService.cs:271,303-304`（按 uid 取占据物）
- **根因**：`Map` 混用了"宽容 API（TryGetCell）"与"严格 API（索引器）"两套风格，调用方不知道哪个会抛。
- **影响**：单位死亡/建筑被拆除后，仍在跑的任务会按已失效的 uid 取 occupant → `KeyNotFoundException` 崩溃（配合 `UNIT-04`、`CON-06` 极易触发）。
- **修复方向**：`Map` 统一向外提供"找不到返回 null/`false`"的安全 API（或抛**语义明确的业务异常**），并在任务回调入口统一做"实体仍存在"校验。
- **关联**：`UNIT-04`、`CON-06`、`MAP-13`

#### `MAP-04` 三种"写槽位"入口并存，`Building` 槽位成了死数据 —— 【P1｜架构层缺陷】
- **现象**：`Map` 同时提供 `AddOccupant`（写 `cell.Occupant` + `_occupants` 索引）、`SetBuilding`（写 `cell.Building`）、`SetInvader`（写 `cell.Invader`）；`AddOccupant` 里的 `if (_cells.TryGetValue(position, out _))` 判断**没有任何效果**（后续照样写 `_occupants`）。
- **证据**：`Scripts/Map/Domain/Map.cs:79-84`（AddOccupant）、`:93-97`（SetBuilding）、`:122-126`（SetInvader）；`MapCell.cs:22-40`；实际建造只调用 `SetOccupant`：`ConstructionAppService.cs:79`、`UnitsAppService.cs:81`
- **根因**：数据结构先于业务设计（槽位是"以后可能要"的预留），业务实现时选择了最省事的一个入口，其余入口就被遗弃。
- **影响**：① `MapCell.Building` 永远是 null，但 `Map.GetBuildingInfo`（`Map.cs:115-121`；v0.3.6 / WP-2.7 起补 null 保护，空格子返回 null 而不是 NRE）与 `RemoveBuilding`（`Map.cs:99-105`）在读/写它 → 建筑移除逻辑实际不生效（`ConstructionAppService.cs:110-121` 依赖它）；② `_occupants` 索引只在 `AddOccupant` 维护，`RemoveOccupantByPosition` 不清理索引 → **单位/建筑被删除后仍能按 uid 查到**（僵尸引用），与 `MAP-03` 组合就是崩溃源。
- **修复方向**：明确"一格一占据物"或"一格多槽位"的**唯一模型**，废弃其余入口；新增/移除占据物必须**同时**维护格子的槽位与 uid 索引（建议由 Map 统一封装成单一方法对）。
- **关联**：`MAP-03`、`MAP-12`、`CON-06`、`UNIT-05`

> 🔎 **已确认（v0.3.6 / WP-2.7 验收）**：本条在无头用例里被实测复现 —— 建造走的 `MapAppService.SetOccupant` 只写 `cell.Occupant`，`cell.Building` 恒为 null，因此 `RemoveBuildingByPosition`（`ConstructionAppService.cs:110-121`）对自建建筑**完全无效**（既不拆地块、也不回收产出修正器）。同时发现同族的 `Map.GetBuildingInfo` 在空格子上会抛 NRE（已补 null 保护，见 `D26`）。本轮**不修**（占用模型归 `WP-3.4`），但已把"缺陷仍在"固定成断言（`D27`），修好时用例会失败并提醒改成正向断言。

#### `MAP-05` 地形 Id 与配置表引用的地形名不匹配 —— 【P1｜数据层缺口】
- **现象**：仓库里的地形资产是 `test1 ~ test5`，而建筑/单位配置要求的地形是 `"plain"`。
- **证据**：`Config/Terrains/test1.tres:8`（`Id = "test1"`）… `test5.tres:9`（`Id = "test5"`）vs `Document/BuildingsConfig.json:11`（`"TerrainRequirements": ["plain"]`）、`Document/UnitsConfig.json:6`
- **根因**：地形是原型期的占位资产，JSON 配置表按"正式地形名"书写，两边从未对齐。
- **影响**：`TerrainRequirement.IsMet()`（`Map/TerrainRequirement.cs:16-21`）永远返回 false → **所有建筑与单位都无法建造/训练**（即使 `WIRE-01/02/03` 都接好，玩法依然走不通）。这是"看起来修好了其实没通"的典型陷阱。
- **修复方向**：**只填表/只改资产**即可解决 —— 补齐 `plain/forest/mountain/water/desert` 的 `.tres`（或把 JSON 里的地形名改成 `test*`）。同时建议在启动期加"配置引用完整性校验"（见 `WIRE-04`）。
- **关联**：`MAP-06`、`WIRE-03`、`WIRE-04`

#### `MAP-06` 所有地形 `.tres` 缺 `MoveCost` → 已探索区全不可通行 —— 【P1｜数据层缺口】
- **现象**：5 个地形资源文件里都只填了 `Id/Name/Weight`，**没有 `MoveCost`**；`MoveCost` 是 `float`，默认 0，而通行判定的规则是"`MoveCost > 0` 才可通行"。
- **证据**：`Config/Terrains/test1.tres:8-12`（无 MoveCost）、`Scripts/Map/Infrastructure/TerrainConfigResources.cs:12`（`float MoveCost` 默认 0）、判定处 `MapAppService.cs:151-156`、`UnitsAppService.cs:252-259`
- **根因**：原型数据未补全 + `0` 在业务上被赋予"不可通行"的语义（典型"魔法值"风险）。
- **影响**：① **未探索格反而可通行**（因为判定里有 `if (visibility == Unexplored) return true;`，`MapAppService.cs:152-153`）→ 单位可以走进迷雾；② **已探索格全部不可通行** → 单位一旦周围被视野点亮就寸步难行；③ 与 `UNIT-02`（MP 上限被压缩）叠加后表现为"单位完全不动"。
- **修复方向**：**只填表**：给每个地形补 `MoveCost`（如 plain=1、forest=2、mountain=0、water=0）。同时建议把"0 表示不可通行"换成显式 `bool Passable`，避免语义歧义。
- **关联**：`MAP-05`、`UNIT-02`、`WIRE-04`

#### `MAP-07` 存档全量重写，叠加"每次操作读盘" → O(操作 × 格数) —— 【P1｜架构层缺陷 + 性能】
- **现象**：`SaveMap` 每次调用都会遍历**全部格子**生成 `MapSave` 并整体覆盖写文件；而 `MapAppService` 的每个操作都会调用它一次。
- **证据**：`Scripts/Map/Infrastructure/GodotMapRepository.cs:51-78`（`:63-69` 遍历所有 cell）；`MapAppService.cs` 所有写操作 `:35,59 附近,71,77,89,95` 都跟一次 `SaveMap`
- **根因**：没有脏标记/增量存档概念；`Map` 也没有"版本/变更集"。
- **影响**：地图规模一大（4X 常见 100×100 = 1 万格），单次建造 = 1 万格序列化 + 文件覆盖写。放到真实玩法里会直接卡死主线程。
- **修复方向**：与 `MAP-01` 一并修：内存常驻 + 脏格子集合 + 分帧/后台存盘；`GodotMapRepository` 增加"增量保存"或至少"只在存档点整图保存"。
- **关联**：`MAP-01`、`MAP-02`、`TIME-01`

#### `MAP-08` `DeleteMap`/`ListMaps` 未实现 —— 【P2｜逻辑层缺口】
- **现象**：两个接口方法直接 `throw new NotImplementedException()`。
- **证据**：`Scripts/Map/Infrastructure/GodotMapRepository.cs:18-21`、`:80-83`
- **根因**：MVP 阶段只需要"生成/读取"，删除与列表未做。
- **影响**：无法做"局列表/继续游戏/删除存档"这类基础 UI；测试也无法清理地图文件。
- **修复方向**：实现为文件系统操作（列出 `user://maps/*.json`、删除对应文件），并处理"文件被占用/不存在"的失败分支。
- **关联**：`MAP-07`、`DEP-07`

#### `MAP-09` `SaveMap` 在 `Terrain == null` 时 NRE —— 【P2｜逻辑层缺口】
- **现象**：保存时直接取 `cell.Terrain.Id`，若地形未分配（空白地图或加载失败）则空引用。
- **证据**：`Scripts/Map/Infrastructure/GodotMapRepository.cs:67`
- **根因**：`Map.GetBlankMap`（`VoronoiMapGenerator.cs:105-119`）创建格子时**不设地形**，`DistributeTerrain` 才补；若过程中异常或因 `WIRE-04` 加载失败，就会出现"无地形格子"被保存。
- **影响**：存档流程崩溃，且发生在"游戏进行中"（比启动崩溃更难排查）。
- **修复方向**：保存前校验地形非空（缺失即记录并跳过/补默认地形）；生成器在创建格子时给一个显式默认地形。
- **关联**：`WIRE-04`、`MAP-05`

#### `MAP-10` 可变 struct 作为字典键 —— 【P2｜架构层缺陷】
- **现象**：`HexCubePosition` 的 `q`/`r` 有公共 setter，同时实现了 `Equals`/`GetHashCode`/`==`，并被用作 `Dictionary<HexCubePosition, MapCell>` 与 `Dictionary<HexCubePosition, byte>`（迷雾）的键。
- **证据**：`Scripts/Common/Domain/HexCubePosition.cs:10-11`（可变属性）、`:38-45`（哈希与相等性）；使用处 `Map.cs:12`、`FogAppService.cs:17-18`
- **根因**：结构体按"数据容器"写，而用法规格是"值对象/键"。
- **影响**：任何对已入字典的 `HexCubePosition` 执行 `q`/`r` 赋值的逻辑，都会导致**键的哈希变化 → 字典查不到/永久泄漏**（这类 bug 极难定位）。目前直接赋值的地方少，但这是定时炸弹。
- **修复方向**：把 `q`/`r` 改为只读（`init`/get-only），提供 `WithOffset(...)` 之类的返回新值的方法；若某些渲染代码需要可变，另设一个可变的表现层结构。
- **关联**：`MAP-11`、`MAP-12`

#### `MAP-11` 未使用的变量与语义错误的坐标方法 —— 【P3｜整洁性】
- **现象**：① `Map.SetTerrain` 取出了 `ITerrainData old` 却从未使用；② `HexCubePosition.Translate(Vector2 factor)` 完全忽略自身 `q/r`，直接把 `factor` 转成新坐标（名为 Translate 实为 "FromVector"）。
- **证据**：`Scripts/Map/Domain/Map.cs:40`；`Scripts/Common/Domain/HexCubePosition.cs:28-31`
- **根因**：迭代遗留 + 命名语义不严谨。
- **影响**：`Translate` 若被误用会静默产生错误坐标（例如"把单位向东移动 1 格"变成"瞬移到 (1,0)"）。
- **修复方向**：删除死变量；`Translate` 改为真正相对位移或重命名为 `FromVector2`。
- **关联**：`MAP-10`、`DEP-09`

#### `MAP-12` `MapCell` 三槽位模型表达能力不足 —— 【P2｜架构层缺陷】
- **现象**：格子被建模为"Occupant + Building + Invader + Population"四个固定字段。
- **证据**：`Scripts/Map/Domain/MapCell.cs:11-15`
- **根因**：按"当前唯一玩法（一建筑/一单位）"硬编码的槽位，而非"占据物集合 + 类型过滤"。
- **影响**：无法表达"一格里多支部队堆叠""一格多层地形效果（道路+资源点+建筑）""中立资源点与建筑共存"等 4X 常见需求；每加一种语义都要改核心数据结构（违反"只填表"）。
- **修复方向**：改为 `IReadOnlyList<IMapOccupant>`（或 `Dictionary<OccupantType, List<...>>`）并保留按类型查询的便捷 API；把 `Population` 之类的地块属性移到独立的 `MapCellState`。**决策时需与设计稿核对外来部队/资源点/地形改造类需求。**
- **关联**：`MAP-04`、`MAP-02`

#### `MAP-13` A* 的失败语义与到达语义混淆 —— 【P2｜逻辑层缺口】
- **现象**：`FindPath` 在"起点=终点"和"无路径"以外，所有情况都返回 `List`；无法区分"无路径"与"空路径"被调用方各自解释。
- **证据**：`Scripts/Map/Application/MapAppService.cs:109-110`（起点即终点返回单元素）、`:143`（无路径返回空列表）；调用方 `UnitsAppService.cs:163-165`（`Count <= 1` 视为放弃移动）
- **根因**：返回值承载了三种语义（到达/无路径/无效起点）。
- **影响**：单位"原地不动"的原因无法区分（地形不可达 vs 已到达 vs 目标非法），排查困难；UI 也无法给出正确反馈。
- **修复方向**：返回显式结果类型（成功/无路径/起点非法）+ 路径列表；或返回 `null` 表示无路径。
- **关联**：`MAP-06`、`UNIT-07`、`CON-01`

### 3.3 时间与任务（TIME-*）

> 说明：设计稿里的"TaskManager 是任务聚合根"在代码中**不存在**；实际角色由 `ITimeService`（订阅列表）+ `ITaskRepository`（快照文件）两者分担，且二者都不具备"聚合根"的职责（去重、生命周期、一致性）。

#### `TIME-01` 每帧对每个任务写盘 —— 【P0｜架构层缺陷 + 性能】
> ⚠️ **v0.3 降级（P0 → P1）**：每帧写盘是性能/IO 缺陷，不改变 headless 断言的正确性（由 `WP-2.2` 一并处理）。级别收敛依据见 §17.3。
- **现象**：`_Process` 里对每个 `IProgressTask` 先 `OnTick`，紧接着把快照写进 `ITaskRepository`；而 `TaskRepository.AddTask` 内部**立即 `Save`（整文件覆盖写）**。
- **证据**：`Scripts/Common/Infrastructure/GodotTimeService.cs:46-56`（循环内写盘）、`Scripts/Common/Infrastructure/TaskRepository.cs:19-23`（`AddOrUpdate` + `Save`）
- **根因**：把"持久化"当成了 tick 的副作用，而不是"存档点"的职责；也没有脏标记或节流。
- **影响**：① 纯 IO 灾难（每帧 N 个任务 × JSON 全量序列化 + 文件覆盖）；② 同一文件高频读写，存档随时可能是半写状态；③ 与 `TIME-02` 叠加后任务数只增不减，性能持续劣化。
- **修复方向**：① 任务快照改为**内存态 + 定期/按需存盘**（每 N 秒或存档点）；② 若必须高频持久化，改为"追加日志 + 定期压缩"；③ 把持久化职责从 `_Process` 里彻底剥离。
- **关联**：`TIME-02`、`TIME-03`、`TIME-04`、`MAP-07`

#### `TIME-02` `IntervalTask` 永不注销 —— 【P0｜逻辑层缺口】
> ⚠️ **v0.3 降级（P0 → P1）**：循环任务不注销是泄漏；M0-2 的单次流程（建造/训练/事件）仍能完成。级别收敛依据见 §17.3。
- **现象**：`IntervalTask.OnTick` 只做 `Progress += delta` → 到点触发 → `Progress -= Target`，**从不设置 `IsCompleted` 也不触发任何"结束"信号**；而 `ITimeService.Unregister` 需要调用方显式调用。现有全部循环任务（资源增长、人口增长、事件判定、单位移动、单位攻击）**没有一处会注销自己**。
- **证据**：`Scripts/Common/Domain/IntervalTask.cs:46-57`；注册点：`ResourcesAppService.cs:88`、`ConstructionAppService.cs:151`、`EventAppService.cs:41`、`UnitsAppService.cs:147`、`:298`
- **根因**：`IProgressTask` 只提供 `OnCompleted` 一个事件（语义是"完成"），循环任务没有"被取消/被销毁"的契约；`ITickable` 也没有 `IsFinished` 之类的生命周期标志。
- **影响**：① 单位死亡、建筑拆除后，相关循环任务仍在 `_subscribers` 里空转（引用被持有 → 无法 GC）；② 每次 tick 都写任务文件（`TIME-01`），于是**已死对象的任务文件永久存在**；③ 长期游玩必然卡顿。
- **修复方向**：给 `ITickable` 增加"已终结/可回收"语义（或引入 `TaskScope`/`Owner` 概念），让时间服务在 tick 后自动清理；并在建筑/单位被移除时统一注销其名下所有任务（见 `CON-06`）。
- **关联**：`CON-06`、`UNIT-04`、`TIME-09`、`TIME-01`

#### `TIME-03` 任务快照以 `Id` 为键互相覆盖 —— 【P1｜逻辑层缺口】
- **现象**：`TaskRepository` 把快照存进 `Dictionary<string, TaskSnapshot>`，键是 `task.Id`。
- **证据**：`Scripts/Common/Infrastructure/TaskRepository.cs:21,33`；`Id` 取值五花八门：建造用 `config.BuildingId`（`ConstructionAppService.cs:77`）、训练用 `config.UnitId`（`UnitsAppService.cs:79`）、研究用 `nodeId`（`TechTreesAppService.cs:64`）、事件到期用 `EventId`（`EventAppService.cs:64`）、资源增长用 `resource.Name`（`ResourcesAppService.cs:67`）、人口用 `buildingUid`（`ConstructionAppService.cs:141`）、单位移动用 `uid`（`UnitsAppService.cs:145`）
- **根因**：`Id`/`UId`/`Type` 三个字段（`TaskSnapshot.cs:9-11`）**没有明确语义分工**，被各模块按各自方便填写。
- **影响**：**同类型任务互相覆盖**——同时造两座 `house`，第二座的快照覆盖第一座；同时训练两个 `worker` 同理。读档时只能恢复一个，另一座建筑永远停在"未就绪"。
- **修复方向**：明确 `Id`（业务模板 Id）/`UId`（实例唯一 Id）/`Type`（任务类型）的语义，并**统一以 UId 作为存储键**；对"以模板 Id 为键"的历史用法做兼容迁移。
- **关联**：`TIME-08`、`CON-03`、`UNIT-09`、`TECH-04`

#### `TIME-04` `RemoveTask` 把 null 写进文件 —— 【P2｜逻辑层缺口】
- **现象**：`RemoveTask` 用 `base.AddOrUpdate(task.Id, null, path)` 把值置为 null 后保存；`GetCurrentTasks` 直接 `GetAll()` 返回列表。
- **证据**：`Scripts/Common/Infrastructure/TaskRepository.cs:31-35`、`:25-29`；`GenericJsonRepository.cs:31-40`
- **根因**：缺少"删除键"的 API（`GenericJsonRepository` 只有 AddOrUpdate / GetById / GetAll），用"写 null"代替删除。
- **影响**：任务文件里堆积 `"id": null`；回读后 `List<TaskSnapshot>` 含 null 项，任何不做 null 判断的 `foreach` 都会 NRE。
- **修复方向**：给 `GenericJsonRepository` 增加 `Remove(id)`；或保存前剔除 null 项。
- **关联**：`TIME-01`、`TIME-03`

#### `TIME-05` 时间只有"秒"，无游戏日/回合，`Scale` 无入口 —— 【P1｜架构层缺陷】
- **现象**：`ITickable.OnTick(float delta)` 的 delta 是真实秒（乘 `Scale`）；`Scale` 默认 1 且**全仓库没有任何地方设置它**；没有任何"游戏日/回合/季节"抽象。
- **证据**：`Scripts/Common/Domain/ITickable.cs:11`、`GodotTimeService.cs:10,44`、`Scripts/Common/Application/ITimeService.cs:14`；而配置文档却用"每天"描述事件（`Document/ConfigTableGuide.txt:448`）
- **根因**：时间系统只做了"帧 → 秒"的缩放，没有做"秒 → 游戏时间单位"的换算层。
- **影响**：① 所有"每回合/每天/每季"玩法（人口增长、事件、生产队列）只能用秒硬编码，策划无法调节奏；② 无法实现"暂停/加速/回合制切换"这类 4X 常规操作；③ 与设计稿的"连续时间驱动"结合时缺少统一的"节拍"定义。
- **修复方向**：引入 `GameClock`（当前游戏时间、时间单位换算、倍率、暂停），让 `ITimeService` 只负责"推进时钟"，任务按"游戏时间"计算。
- **关联**：`TIME-06`、`EVT-02`、`UNIT-03`、`WIRE-02`

#### `TIME-06` 节拍值硬编码散落各处 —— 【P2｜架构层缺陷】
- **现象**：事件判定 1 秒、单位攻击 1 秒、单位移动 10 秒；资源增长与人口增长则来自配置；建筑/研究时长来自配置。
- **证据**：`EventAppService.cs:39`（1f）、`UnitsAppService.cs:296`（1f）、`UnitsAppService.cs:145`（10f）、`ResourcesAppService.cs:67`、`ConstructionAppService.cs:141`
- **根因**：没有统一定义"节拍"概念，各模块按"感觉"取值。
- **影响**：① 时间尺度不一致（单位 10 秒才挪一步，敌人却 1 秒砍一刀，战斗表现失衡）；② 策划无法通过配置调节这些节拍；③ 与 `TIME-05` 的"天/回合"缺失互为因果。
- **修复方向**：把节拍改为配置项（单位/建筑/事件的配置表或全局时间配置），并统一走 `GameClock` 的换算。
- **关联**：`TIME-05`、`UNIT-03`、`EVT-02`
> ✅ **已修复（v0.3.4 / WP-1.5）**：硬编码节拍全部收敛为 `Scripts/Core/Time/TimeConstants.cs` 的具名常量（`EventRollDays` / `UnitAttackDays` / `UnitMoveDays` / `ResourceSettlementDays`），并改按**游戏日**计；单位与人口的节拍继续来自配置表（`Duration` / `PopulationGrowthInterval`），由校验器做单位自检。`TIME-05`/`TIME-07` 的诉求同批解决（见 §18.3 D18/D19）。

#### `TIME-07` Domain 无法脱离 Godot 驱动 —— 【P2｜架构层缺陷】
- **现象**：`ITimeService` 唯一实现是 `GodotTimeService : Node`，`OnTick` 由 Godot `_Process` 触发；没有"手动步进"的实现或 API。
- **证据**：`Scripts/Common/Infrastructure/GodotTimeService.cs:8,42-57`；`ITimeService.cs:10-15` 未暴露 `Advance(delta)` 之类的步进方法
- **根因**：为了图快，把"驱动器"与"时间引擎"合并了。
- **影响**：① 无法为 Domain 写**无引擎的单元测试**（"Domain 是纯 C#"的目标在时间维度上被打破）；② 无法做"服务器权威模拟/离线结算/存档回放"。
- **修复方向**：拆成"时间推进器（纯 C#，可手动 Advance）"+"Godot 适配器（_Process → Advance）"两层；Domain 只依赖前者。
- **关联**：`DEP-07`、`TIME-05`、`WIRE-02`
> ✅ **已修复（v0.3.4 / WP-1.2 + WP-1.5）**：`GameTimeService`（纯 C#、可手动驱动）+ `GodotTimeDriver`（`Node`，`_Process → Clock.Advance`）两层已分离；`GodotTimeService`（唯一 Godot 依赖的时间实现）已删除。无头检查里时间系统全部由 `ManualTimeDriver` 驱动，不需要引擎。

#### `TIME-08` 任务快照与实体状态不同源 —— 【P2｜架构层缺陷】
- **现象**：任务快照写进独立 JSON 文件；而实体状态（建筑/单位）**根本不落盘**（`MAP-02`）。两者生命周期完全脱节。
- **证据**：`TaskRepository.cs`（独立文件）、`GodotMapRepository.cs:63-69`（不存实体）、`ConstructionAppService.cs:97-108`（`ResumeConstruction` 需要外部提供 snapshot）
- **根因**：没有统一的"存档事务"概念，各模块各自落盘。
- **影响**：① 存档可能出现"有建造任务、无对应建筑"（或反之）；② 续跑 API 需调用方自己拼装快照，容易漏字段（见 `CON-03`）。
- **修复方向**：引入统一的存档单元（SaveUnit/SaveSlot），把"地图 + 实体 + 任务 + 资源 + 迷雾 + 科技"在同一版本号下一起写；读档用同一版本号做一致性校验。
- **关联**：`MAP-02`、`TIME-03`、`CON-03`、`TIME-05`

#### `TIME-09` `LinearTask` 完成后仍常驻订阅列表 —— 【P2｜逻辑层缺口】
- **现象**：`LinearTask` 完成后仅把 `IsCompleted = true`，对象仍留在 `_subscribers`（每帧仍会 `OnTick` 后立即 return）；依赖调用方在 `OnCompleted` 回调里 `Unregister`。
- **证据**：`Scripts/Common/Domain/LinearTask.cs:30-36`；手动注销点：`ConstructionAppService.cs:90`、`TechTreesAppService.cs:76`、`EventAppService.cs:68`；**`UnitsAppService.cs:83-89` 的训练任务没有注销**
- **根因**：完成与回收的职责被交给调用方，且没有兜底机制。
- **影响**：任何忘记 `Unregister` 的任务都会永久占用每帧 tick 与写盘（`TIME-01`）。
- **修复方向**：时间服务在 tick 后自动移除 `IsCompleted` 的任务（或引入任务终结回调），调用方不再承担注销职责。
- **关联**：`TIME-02`、`TIME-01`

#### `TIME-10` 任务仓储无查询能力 —— 【P3｜整洁性】
- **现象**：`ITaskRepository` 只有 `GetCurrentTasks/AddTask/RemoveTask`，且每次查询都要 `Load` 整个文件。
- **证据**：`Scripts/Common/Domain/ITaskRepository.cs:10-15`、`TaskRepository.cs:25-29`
- **根因**：按"最小实现"设计，未考虑调试与按实体查询。
- **影响**：无法做"某建筑当前有哪些任务""按类型过滤""任务面板 UI"；排查问题时只能读 JSON 文件。
- **修复方向**：补 `GetTasksByOwner/ByType/ByUId`；配合 `TIME-01` 的内存态改造自然解决性能问题。
- **关联**：`TIME-01`、`TIME-03`

### 3.4 Modifier 数值系统（MOD-*）

> 说明：设计目标里的"ModifierManager 是数值修正器"**部分成立**——它确实集中管理数值，但它是**临时构造的**、**作用域是玩家级的**、**公式不可配置的**。

#### `MOD-01` `GetValue` 在循环内 return，多 target 只有第一个生效 —— 【P1｜逻辑层缺口】
- **现象**：`GetValue(string[] targets, float baseValue)` 遍历 targets，一旦某个 target 存在于字典就 `return (result + abs) * (1 + per)`；后续 target 完全不被处理，跨 target 的累加也不会发生。
- **证据**：`Scripts/Common/Domain/ModifierManager.cs:34-57`（`return` 位于 `:54`，处于 `foreach` 内部）
- **根因**：把"找出第一个匹配的 target"与"累加全部匹配 target"混在一个循环里写，分支写错。
- **影响**：任何"多个 Modifier 目标同时影响同一数值"的设计都无法生效（例如资源同时受 `GoldGrowth` 与全局 `EconomyRate` 影响时只有一个起作用）；策划按文档（`Document/ConfigTableGuide.txt:46-51` 明确支持 `DependentModifiers` 列表）填多个 target 会得到莫名结果。
- **修复方向**：累加所有命中 target 的 abs/per 后再统一套公式；补单元测试覆盖"零个/一个/多个 target"。
- **关联**：`MOD-02`、`DEP-07`

#### `MOD-02` 数值公式硬编码 —— 【P1｜逻辑层缺口】
- **现象**：计算永远是 `(BaseValue + ΣAbsolute) × (1 + ΣPercentage)`。没有优先级/阶段，也没有上下限、叠乘（multiplicative stacking）、互斥或"取最大值"等策略。
- **证据**：`Scripts/Common/Domain/ModifierManager.cs:54`
- **根因**：把"数值体系"简化成一条公式，未预留阶段/策略维度。
- **影响**：① "叠乘加成""加成上限""某类加成取最高值而非叠加"这类常见数值设计无法表达；② 百分比与绝对值混合时会出现"加成被意外放大/缩小"（先加后乘）；③ 数值平衡只能靠配置"凑"。
- **修复方向**：引入"修正阶段/管道"（Base → Additive → Multiplicative → Cap）与可选策略字段；保持旧公式作为默认阶段以向后兼容。
- **关联**：`MOD-01`、`MOD-05`、`MOD-07`

#### `MOD-03` 作用域只有 (mapId, ownerId) —— 【P1｜架构层缺陷】
- **现象**：修正器的存储/加载键是"地图 + 玩家"；没有任何对象级（单位/建筑/格子）作用域。
- **证据**：`Scripts/Common/Domain/IModifierRepository.cs:7-8`（参数即 `mapId, ownerId`）、`ModifierRepository.cs:15-18`（文件路径 = 前缀 + `mapId + "_" + ownerId`）
- **根因**：修正器最初是为"资源增长率"这一个用例设计的，作用域直接对齐"玩家的资源池"。
- **影响**：① **无法给单个单位/建筑挂临时增益**（"该单位本回合 +5 攻击""新种的树 +50% 产粮 30 秒"只能改全局 `Attack`，影响所有人）；② **无法表达全局环境状态**（天气/季节/世界事件：按 owner 挂会变成 N 份拷贝，且新加入玩家挂不上）；③ 也无法表达格子级修正（地形效果、资源点加成）。
- **修复方向**：把作用域抽象为"修正器宿主"（`Player / Entity / Cell / World` + 宿主 Id），存储层按作用域分桶；`ModifierManager` 保持纯计算职责。**这是"天气/全局状态/单体 buff"类需求能否落地的前置条件。**
- **关联**：`MOD-02`、`FOG-01`、`DEP-03`、`UNIT-01`

#### `MOD-04` 无长驻管理器，每次操作 Load→new→Save —— 【P2｜架构层缺陷】
- **现象**：`ModifierAppService` 每个方法都 `LoadModifiers` → `new ModifierManager` → 修改 → `SaveModifier`；`ResourcesAppService` 在每次资源增长 tick 时也重新 Load 一次修正器。
- **证据**：`Scripts/Common/Application/ModifierAppService.cs:17,25`、`:30,38`、`:43,45`；`ResourcesAppService.cs:71`
- **根因**：与 `MAP-01` 同源——"Repository 即聚合根"的简化写法。
- **影响**：① 资源增长每个节拍都读一次修正器文件（IO 放大）；② 没有内存中的单一真实来源，多模块并发修改可能互相覆盖（后写覆盖前写）。
- **修复方向**：与 `MAP-01`/`DEP-03` 一起，把修正器提升为随 Session 常驻的内存聚合，落盘交给存档流程。
- **关联**：`MAP-01`、`MOD-03`、`DEP-03`

#### `MOD-05` 类型判定依赖字符串、值为可变 struct —— 【P2｜逻辑层缺口】
- **现象**：配置里的 `Type` 是 `"Percent"`/`"Absolute"` 字符串，代码用 `modifier.Type == "Percent" ? Percentage : Absolute` 三元判断——**任何拼错的类型都会静默降级为绝对值**；`Modifier`（DTO）字段带 public setter。
- **证据**：`Scripts/Common/Application/ModifierAppService.cs:20,33`、`Scripts/Common/Domain/Modifier.cs:10-15`
- **根因**：为 JSON 友好性牺牲类型安全，且未做枚举解析与校验。
- **影响**：策划把 `"percent"` 写成小写、或写成 `"%"`，效果从"×1.2"变成"+0.2"，**且没有任何报错**——数值 bug 极难发现。
- **修复方向**：配置表改用枚举名（Newtonsoft 支持 `StringEnumConverter`），解析失败即报错；把 `Modifier` 改回 `readonly struct`/record。
- **关联**：`MOD-01`、`MOD-02`、`WIRE-03`

#### `MOD-06` `RemoveModifierByTarget` 命名与语义不符 —— 【P2｜整洁性】
- **现象**：该方法实际做的是"从某个 target 的列表里移除**指定的一条** value"，而非"移除某个 target 的所有修正"。
- **证据**：`Scripts/Common/Domain/ModifierManager.cs:22-32`（参数含 `ModifierValue value`）；全仓库无调用点
- **根因**：命名按"意图"写、实现按"参数"写。
- **影响**：后续接入者极易误解而写出错误的清理逻辑（真正的批量清理只有 `RemoveModifiersBySourceId`，`:60-70`）。
- **修复方向**：重命名（如 `RemoveSingle(target, value)`）并补充"按 target 清空"的方法。
- **关联**：`MOD-03`

#### `MOD-07` 无叠加护栏与可观测性 —— 【P2｜架构层缺陷】
- **现象**：修正器无数量上限、无"同 SourceId 重复添加"检测；`Duration = 0` 的事件修正器（文档称"永久"）挂上后没有撤销入口；也没有任何"当前生效修正器"查询 API 供 UI 展示。
- **证据**：`Scripts/Common/Domain/ModifierManager.cs:13-20`（无条件 Add）；`EventAppService.cs:62`（仅 `Duration > 0` 才注册到期任务）；`ModifierAppService` 无查询方法
- **根因**：只实现了"加"与"按 sourceId 删"，没实现"可观测/可治理"。
- **影响**：① 每次事件触发都叠加一份修正器，同类事件反复触发会让数值失控（`GoldGrowth` 被叠加 10 次）；② 玩家无法看到"我为什么产出这么多"；③ 出问题无法定位是哪条修正器造成的。
- **修复方向**：加"同 SourceId 覆盖式添加"选项、数量/数值上限，以及"按宿主列出生效修正器（含来源）"的查询接口。
- **关联**：`MOD-02`、`MOD-03`、`EVT-03`

#### `MOD-08` 契约缺失：UML 里的 `IModifier` 不存在 —— 【P3｜文档】
- **现象**：`UML/PackageDiagram.txt` 把 `IModifier` 画成了核心契约（并声称 `IMapOccupant` "Hold" 它），但代码里只有 `Modifier`（DTO struct）与 `ModifierValue`（值 struct），**没有 `IModifier` 接口**，`IMapOccupant` 也不持有任何修正器。
- **证据**：`UML/PackageDiagram.txt:15,47,52`；`Scripts/Common/Domain/Modifier.cs:10`、`ModifierValue.cs:9`
- **根因**：UML 是设计意图，代码走了"集中式修正器表"的另一条路（与 `MOD-03` 的作用域问题同源）。
- **影响**：新人按 UML 理解架构会完全找不到落点；也掩盖了"实体级修正器"这一未被实现的设计意图。
- **修复方向**：结合 `MOD-03` 的宿主化改造，重新决定是"实体持修正器"还是"集中表 + 宿主键"，然后同步 UML。
- **关联**：`MOD-03`、`DEP-10`

### 3.5 建造系统（CON-*）

#### `CON-01` 条件检查与扣费非原子、失败无返回 —— 【P1｜逻辑层缺口】
- **现象**：建造流程是"把配置里的资源/地形/科技要求全部展开成对象 → `All(IsConsumable) && IsClear && All(IsMet)` → 全部通过才 `ForEach(Consume)`"。全程无事务、无补偿、无失败原因返回；方法是 `void`，失败仅静默 `return`。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:45-95`（判定 `:66-69`、扣费 `:71`、静默返回 `:94`）；单位侧同构：`UnitsAppService.cs:46-93`（判定 `:69-71`、静默返回 `:92`）
- **根因**：把"多条件判定"实现为"客户端整体预检"，没有把"扣费+创建实体"当作一个不可分割的业务操作。
- **影响**：① UI 无法告诉玩家"为什么不能造"（缺少资源？地形不对？科技没研究？）——`UML/BuildSequence.txt:19` 设计的 `BuildFailed(ErrorMsg)` 从未实现；② 若未来引入"部分消耗"（如分期付款、先扣后补）或异步校验，会出现资源被扣但建筑没建（或反之）的脏状态；③ 与 `MAP-03` 组合时，`_map.IsClear` 依赖已被删除但仍存在于索引中的僵尸占据物，可能给出错误结果。
- **修复方向**：引入"业务结果"返回类型（成功 / 失败原因列表），让 UI 能展示；把扣费与实体创建收拢为一个幂等的领域操作，并明确失败回滚策略。
- **关联**：`MAP-03`、`MAP-13`、`EVT-05`、`TECH-03`

#### `CON-02` 能力分发靠 `switch` 白名单 —— 【P1｜架构层缺陷】
- **现象**：配置里的 `Actions`（如 `["CanBuild","CanMove"]`）只是**允许清单**，实际行为由一个 `switch (action)` 硬编码分发；两处 switch 分别只认识 1 个和 3 个动作字符串。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:123-137`（仅 `CanResearch`）、`Scripts/Units/Application/UnitsAppService.cs:106-126`（`CanBuild/CanAttack/CanMove`）；配置侧 `Document/BuildingsConfig.json:31`、`Document/UnitsConfig.json:12`
- **根因**："预制能力组件"的抽象停在了"字符串 + switch"这一层，没有上升为"能力对象/处理器注册表"。
- **影响**：① 每加一种能力（如 `CanTrade`、`CanHeal`、`CanPlant`、`CanUpgrade`）都必须修改 `AppService` 内部 switch —— **违反设计哲学第 1 条"程序只写预制行为组件"**（组件应该是可插拔的，而不是被 switch 枚举的）；② 动作参数只能靠 `string targetParam` 传递（见 `CON-08`），无法表达多参数/结构化参数；③ 建筑与单位各自维护一份 switch，同一种能力在两边重复实现（`CanBuild` 只在单位侧、`CanResearch` 只在建筑侧）。
- **修复方向**：定义 `IActionHandler`/能力组件契约（`CanHandle(actionId)` + `Execute(context)`），由组合根注册；配置表里的 `Actions` 字符串映射到 Handler Id。**这是让"只填表新增能力"成立的关键改造。**
- **关联**：`CON-08`、`UNIT-01`、`TECH-02`、`MOD-03`

#### `CON-03` 续跑逻辑与首次完成逻辑不一致 —— 【P1｜逻辑层缺口】
- **现象**：建造完成时的处理（置 `IsReady`、挂修正器、开视野、注册人口任务、注销任务）与 `ResumeConstruction` 里的处理（只置 `IsReady`、挂修正器、注销任务）**步骤不同**。
- **证据**：对比 `Scripts/Construction/Application/ConstructionAppService.cs:81-91`（首次）与 `:97-108`（续跑）：续跑缺 `_fog.RevealArea`、缺 `RegisterHousingTask`
- **根因**：续跑逻辑是"照着首次逻辑手抄"的，抄漏了。
- **影响**：**读档后，所有在建造中的建筑即使建成也不会开视野、不会产生人口增长**——玩家会看到"建筑好了但地图还是黑的、人口不涨"。这是"读档后玩法静默降级"的典型问题。
- **修复方向**：把"建造完成"收敛为**单一领域方法**（`CompleteConstruction(...)`），首次完成与续跑都调用它；快照里保存必要上下文（配置 Id、Owner、位置）。
- **关联**：`TIME-03`、`TIME-08`、`CON-05`、`UNIT-09`

#### `CON-04` 人口任务 `OwnerId` 写成 0 —— 【P1｜逻辑层缺口】
- **现象**：注册人口增长任务时，`IntervalTask` 的 `ownerId` 参数被**硬编码为 0**，而快照里显然应该是建筑所有者的 Id。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:141`（`new IntervalTask(0, interval, buildingUid, "PopulationGrowth", "none", mapId, 0)`）；对比同一文件的建造任务 `:77` 正确传入 `ownerId`
- **根因**：`IntervalTask` 的构造参数长（7 个），顺序相近且都为字符串/数字，写错不易被编译器发现。
- **影响**：**多玩家环境下任务归属错误**：任务快照里的 OwnerId 永远是 0，读档恢复时会把任务挂到玩家 0 名下；资源/修正器又是按 (mapId, ownerId) 分桶的，进而导致"人口增长改了别人的数据"或"谁都没改"。
- **修复方向**：修正传参；更根本的是**消灭长参数列表**，改为快照/上下文对象或命名参数，避免同类错误再发生。
- **关联**：`TIME-03`、`DEP-03`、`CON-03`

#### `CON-05` 人口机制不完整（无上限、不落盘、不清理） —— 【P2｜架构层缺陷】
- **现象**：人口存在 `MapCell.Population` 上，由建筑的循环任务按半径逐格 `+1`（到 `cap` 停止）；人口不落盘（`MAP-02`）；任务永不注销（`TIME-02`）；建筑被拆除后任务继续跑（`CON-06`）。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:139-163`（任务与半径展开）、`Scripts/Map/Domain/MapCell.cs:15,42-50`、`Document/BuildingsConfig.json:13-16`（`IsHousing/PopulationRadius/PopulationCap/PopulationGrowthInterval`）
- **根因**：人口是"建筑配置的一个副作用"，没有独立的领域模型（没有人口聚合、没有容量校验、没有迁移）。
- **影响**：① 读档后人口归零但建筑还在，"人口受限的建造"会立刻失效；② 多个住房覆盖同一格时，各自独立 `+1`，实际增长速度被格子数放大且无全局上限校验；③ 后期做"人口消耗粮食/迁移/劳动力分配"时无处挂。
- **修复方向**：把人口提升为地块/聚落状态的一部分并纳入存档；由统一的"人口增长系统"（单一 ITickable）集中处理，而不是每个建筑各自注册任务。
- **关联**：`MAP-02`、`CON-06`、`TIME-02`、`CON-07`

#### `CON-06` 拆除建筑不清理其名下任务 —— 【P2｜逻辑层缺口】
- **现象**：`RemoveBuildingByPosition` 只做"移除占据物、重置视野、按 uid 移除修正器"，**没有注销该建筑注册过的循环任务**（人口增长任务仍以 buildingUid 继续运行）。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:110-121`；任务注册处 `:139-152`（任务 Id/Type 记录了 buildingUid，但没有任何索引可反查）
- **根因**：任务注册是"散弹式"的（新建就注册），没有"归属/作用域"的概念（与 `TIME-02` 同源）。
- **影响**：拆除住房后人口仍持续增长；任务回调里再按 uid 取建筑会失败（配合 `MAP-03` 直接崩溃）。
- **修复方向**：为任务引入 Owner/Scope（如"挂在 uid 下的任务集合"），移除实体时按 Scope 批量注销；这也是 `TIME-02` 的通用解法。
- **关联**：`TIME-02`、`MAP-03`、`MAP-04`、`CON-05`

#### `CON-07` 建筑表与单位表结构不对称 —— 【P2｜数据层缺口】
- **现象**：建筑配置独有 `IsHousing / PopulationRadius / PopulationCap / PopulationGrowthInterval / VisionRadius / Actions`，单位配置独有 `MoveRechargePerTick / AttackRadius / AttackDamage / PopulationCost / HP / Attack / Movement`；两边没有共享的抽象（"占据物配置"基类或组合块）。
- **证据**：`Scripts/Construction/Domain/IBuildingConfig.cs:10-25` vs `Scripts/Units/Domain/IUnitConfig.cs:6-22`
- **根因**：两张表各自演进，没有抽公共配置块。
- **影响**：① "给建筑加视野/给单位加人口容纳"这类交叉需求要么复制字段、要么改两处；② 未来加入"可移动的建筑（车/船）""可攻击的建筑"时，需要把两个接口的字段合并或做适配层；③ 配置校验逻辑（成本、地形、科技要求）在两边重复实现（`ConstructionAppService.cs:49-69` 与 `UnitsAppService.cs:51-71` 几乎一样）。
- **修复方向**：抽取公共配置契约（如 `IOccupantConfig`：Id/Name/ResourceCost/TerrainRequirements/TechRequirements/Duration/Modifiers/Actions/VisionRadius），建筑与单位各自扩展；把"条件构建 + 校验"抽成共享服务。
- **关联**：`CON-01`、`CON-02`、`TECH-02`

#### `CON-08` 动作参数用字符串协议传递 —— 【P3｜整洁性】
- **现象**：动作的附加参数是一个 `string targetParam`，需要调用方按 `"treeId:nodeId"` 的约定拼装，服务端 `Split(':')` 解析。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:123,165-169`；单位侧 `UnitsAppService.cs:106`（同样的 `targetParam` 形态，但语义完全不同：一个是科技节点，一个是 uid/坐标）
- **根因**：动作分发只有"一个字符串"的位置，只好把结构化参数塞进字符串。
- **影响**：参数格式错误只能静默 `return`（`ConstructionAppService.cs:169`）；UI 必须知道每个动作的参数编码规则（耦合）。
- **修复方向**：与 `CON-02` 的能力组件改造一起，用结构化请求对象（`ActionRequest`）承载参数。
- **关联**：`CON-02`

### 3.6 单位系统（UNIT-*）

#### `UNIT-01` `UnitFactory` 参数语义错位、字段冗余 —— 【P1｜逻辑层缺口】
- **现象**：创建单位时把 `config.Movement` 同时传给了 `mp`（移动点池）与 `movement`（机动性）两个参数；而 `config.Attack` 只被写进 `Unit.Attack`（一个从未被战斗逻辑读取的字段），真正用于伤害的是 `config.AttackDamage`。
- **证据**：`Scripts/Units/Domain/UnitFactory.cs:15-17`；`Scripts/Units/Domain/Unit.cs:15-25`（`Attack`/`Movement`/`MovementPoint` 三个语义重叠字段）；实际伤害来源 `UnitsAppService.cs:326`（`defUnit.HP -= atkUnit.AttackDamage`）
- **根因**：配置表与实体字段各自演进：`Attack`（配置）→ 语义分裂为 `Attack`（意图）与 `AttackDamage`（实现），而 `Movement` 与 `MovementPoint` 职责边界未定义。
- **影响**：① 策划改 `Attack` 字段**不会有任何效果**（数值不生效的隐性 bug，极易被当成"平衡问题"排查很久）；② `MovementPoint` 与 `MoveRechargePerTick` 语义重叠，导致 `UNIT-02` 的 MP 上限错误；③ 未来的"攻击力受 Modifier 影响"（配置表已有 `swordsmanAttack` 目标，见 `Document/TechTreesConfig.json:11`）**当前根本不会被应用**——因为没有读取修正器的战斗计算路径。
- **修复方向**：明确每个字段的唯一语义并在配置表/代码里对齐（建议：`Attack` 即伤害，删掉 `AttackDamage`；`MovementPoint` 作为"移动点上限"，`MoveRechargePerTick` 改为"每节拍恢复量"）；战斗与移动计算必须走 `ModifierManager`（这才是"推拉结合"里"拉"的真正消费点）。
- **关联**：`UNIT-02`、`MOD-03`、`MOD-01`、`TECH-02`

#### `UNIT-02` MP 上限被压成单次恢复量 → 单位可能永久卡死 —— 【P1｜逻辑层缺口】
- **现象**：每 10 秒的移动节拍里，移动点被写成 `CurrentMP = min(CurrentMP + MoveRechargePerTick, MoveRechargePerTick)`：**上限恒等于单次恢复量**，也就是"每节拍只能积累到够走一格（且只能是代价 ≤ 恢复量的一格）"。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:158`；配合地形代价 `GetCellMoveCost`（`:221-222`）、不可通行判定 `:214-219`（行号按 v0.3.5 复核）
- **根因**：`Unit.MovementPoint`（配置的"机动性/上限"）与 `MoveRechargePerTick`（"每节拍恢复"）两个概念混淆，实现时选了后者当上限。
- **影响**：① 只要存在 `MoveCost > MoveRechargePerTick` 的地形，单位**永远无法进入**（MP 永远不够）——目前因为地形 `MoveCost` 全是 0（`MAP-06`）而"看起来"是别的问题；② 一旦补上地形代价（例如森林=2，而弓箭手恢复=1.5），弓箭手会被永久卡在森林边缘；③ "攒机动力翻山"这类设计无法表达。
- **修复方向**：把 `MovementPoint`（上限/池容量）与 `MoveRechargePerTick`（恢复速率）分开使用；明确"移动点不足时是否可以跨节拍累积"的规则，并让设计师可配置。
- **关联**：`MAP-06`、`UNIT-01`、`UNIT-03`、`UNIT-07`

#### `UNIT-03` 移动 10 秒 / 攻击 1 秒，节拍失衡 —— 【P1｜架构层缺陷】
- **现象**：单位移动的循环任务节拍硬编码为 10 秒；攻击任务节拍硬编码为 1 秒。二者相差 10 倍，且都与"连续时间驱动"的表述不符（实际是"离散节拍"）。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:145`（`IntervalTask(0, 10f, uid, "UnitMove", ...)`）、`:296`（`IntervalTask(0, 1f, $"atk_...", "UnitAttack", ...)`）
- **根因**：没有统一的"战斗/移动节拍"概念（`TIME-05`/`TIME-06` 的局部表现），实现时各取所需。
- **影响**：① 战斗表现失衡（近战单位从"贴上去"到"开始砍"之间最多要等 10 秒，而一旦开始则是 1 秒一刀）；② 单位移动意图的响应延迟极长（玩家点一下要等最多 10 秒才动一步）；③ 设计稿若要求"实时/连续移动"，需重写这段驱动逻辑。
- **修复方向**：移动与攻击共用统一的节拍（或改为真正的连续推进：按 delta 累积 MP、按 delta 推进路径进度），节拍值进配置。
- **关联**：`TIME-05`、`TIME-06`、`UNIT-02`、`UNIT-04`

#### `UNIT-04` 攻击任务永不注销，目标死亡后空转 —— 【P1｜逻辑层缺口】
- **现象**：每次发起攻击都会新建一个 `IntervalTask`（Id 含攻击者与目标 uid），但**从不注销**；`AttackTick` 在目标不存在/已死/超范围时只是 `return`，任务继续每秒触发。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:294-299`（注册）、`:301-307`（目标缺失早退）、`:325-332`（目标死亡只清攻击者状态，未注销任务）
- **根因**：同 `TIME-02`——循环任务缺少"结束"语义，且攻击任务没有与之绑定的生命周期。
- **影响**：① 每次攻击留下一个永久循环任务（+每帧写盘，`TIME-01`）→ 一场战争后任务列表爆炸；② 目标单位被删除后仍按 uid 查询（`:303-304`），与 `MAP-03`（索引器）/`MAP-04`（僵尸索引）组合可能崩溃或"打到幽灵"；③ 单位移动、攻击状态与任务之间无一致性保证。
- **修复方向**：攻击改为"由单位自己的单一 tick 驱动"（单位作为一个 `ITickable`，在 OnTick 里按状态机处理移动/攻击），而不是每次动作都新建任务——这样单位的死亡就自然终止其行为；同时补齐任务注销。
- **关联**：`TIME-02`、`MAP-03`、`UNIT-03`、`UNIT-09`

#### `UNIT-05` 单位移动不同步格子占用（数据不一致） —— 【P1｜逻辑层缺口】
- **现象**：单位移动时只修改自身 `Position` 并刷新迷雾，**没有更新旧格子的 `Occupant` 与新格子的 `Occupant`**。于是 `Map.GetOccupantInfo(旧格)` 仍返回该单位，`Map.IsClear(新格)` 仍返回 true。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:199`（友军跳跃）、`:222-226`（正常移动）；读取侧 `Map.cs:66-72`（`GetOccupantInfo` 读 `cell.Occupant`）、`MapAppService.cs:38-42`（`IsClear` 据此判断）
- **根因**：`IMapOccupant` 在"地图格子槽位"与"对象自身坐标"两处冗余存储位置，移动时只更新了一处。
- **影响**：① 玩家可以往"显示为空但实际有敌人"的格子建造/移动（`IsClear` 误判）；② `Map.GetOccupantByUId` 是索引查询（能查到），而 `GetOccupantInfo(position)` 是格子查询（返回过期），两张视图彻底不一致 → AI/战斗判定随时出错；③ 存档若按格子写（`MAP-02` 的修复方向）会写出错误的占用关系。
- **修复方向**：确立"格子槽位是唯一权威 + 移动必须原子地迁移槽位"或"格子槽位仅作缓存、由位置索引统一派生"，二者择一并写进 `Map` 的 API 约束。
- **关联**：`MAP-04`、`CON-01`、`UNIT-04`、`MAP-03`

#### `UNIT-06` 停火线用 `VisionRadius`，与 `AttackRadius` 混淆 —— 【P2｜逻辑层缺口】
- **现象**：移动节拍里用**视野半径**判断"附近是否有敌人"并据此停下；而攻击判定用的是 `AttackRadius`（0 表示近战、>0 表示远程）。两个半径语义不同却被用于相似的"距离判断"。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:236-250`（`HasEnemyInRadius`，半径来自 `VisionRadius`，默认值硬编码 3）、`:275-283`（攻击半径判定）、`Document/UnitsConfig.json:13,16`（`VisionRadius: 3/4/5`，`AttackRadius: 0/0/3`）
- **根因**：没有区分"感知/警戒"与"攻击"两个概念；也缺"警戒半径/索敌逻辑"这一层。
- **影响**：① 视野 5 的弓箭手一旦发现 5 格外的敌人就**永远停下不动**（因为停下后没有索敌行为，只会原地待命）→ "单位走到一半突然发呆"的诡异表现；② 无法表达"警戒但不攻击""追击""撤退"等行为。
- **修复方向**：引入独立的"感知/警戒半径"配置与一个显式的"单位行为状态机"（Idle / MoveTo / Engage / Attack），明确各自的距离语义。
- **关联**：`UNIT-03`、`UNIT-04`、`UNIT-07`、`CON-07`

#### `UNIT-07` 近战寻位只取第一个可进入邻居 —— 【P2｜逻辑层缺口】
- **现象**：近战需要贴身时，用 `GetNeighbor().FirstOrDefault(可进入)` 取目标邻格，**不检查该格子是否已被占据、是否有其他单位、是否是自己**。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:286-291`；`CanEnterCell`（`:252-259`）只看地形与迷雾，不看占据物
- **根因**：占位校验缺失（也与 `UNIT-05` 的占用不同步互为因果——即便想查也查不准）。
- **影响**：单位可能走进已被占据的格子、走到自己脚下、或被"卡住"永远到不了目标（`neighbor == default` 时静默放弃）。
- **修复方向**：寻位时做完整的占位/通行校验（并依赖 `UNIT-05` 修好后的权威占用视图）；失败时给出可预期的降级行为（原地待命 / 寻次优格）。
- **关联**：`UNIT-05`、`MAP-13`、`UNIT-06`

#### `UNIT-08` 单位阵亡无事件、无回调 —— 【P2｜架构层缺陷】
- **现象**：目标 HP ≤ 0 时的处理是"从地图上移除 + 重置视野 + 清攻击者状态"，**没有任何事件/回调/日志**。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:325-332`
- **根因**：设计哲学里"业务动作通过 UId 主动推送"的**推送侧完全没有实现**——全仓库没有任何事件总线/领域事件/回调机制（只有任务完成事件 `OnCompleted`）。
- **影响**：① "死亡遗言/亡语""击杀奖励（经验/资源/科技进度）""战报/通知""成就/统计"这类需求**全部无处挂载**；② UI 无法感知战场变化，只能轮询全图。
- **修复方向**：引入轻量的领域事件发布/订阅（或"战斗结果"返回值 + 由应用层分发），先覆盖"实体创建/完成/移除/受击/死亡"这几类关键事件。**这是设计稿里大量"触发/联动"类需求（遗言、连锁、任务奖励）的共同前置。**
- **关联**：`CON-01`、`UNIT-04`、`TECH-05`、`EVT-04`

#### `UNIT-09` 单位完全没有持久化层 —— 【P1｜架构层缺陷 + 命名】
- **现象**：不存在"单位仓库"的任何实现；名字最像的 `IUnitsRepository` 实际只有一个 `GetUnitConfig(unitId)` 方法（**配置读取**），其实现 `UnitsConfigRepository` 也是配置仓库（继承 `GenericConfigRepository`）。
- **证据**：`Scripts/Units/Domain/IUnitsRepository.cs:9-12`、`Scripts/Units/Infrastructure/UnitsConfigRepository.cs:6,16-21`；实体的不可序列化问题见 `MAP-02`
- **根因**：单位模块只做了"配置 → 创建 → 行为"，没有做"状态持久化"；命名沿用了"Repository"却指配置。
- **影响**：① 读档后所有单位消失（与 `MAP-02` 同一根因，但单位侧连接口位都没有）；② 命名误导会让后续开发者以为"单位持久化已经有人在管"。
- **修复方向**：把配置仓库重命名为 `IUnitConfigRepository`（与建筑侧 `IBuildingConfigRepository` 对称），并**新增真正的单位状态持久化契约**（或在统一的存档单元里处理，见 `TIME-08`）。
- **关联**：`MAP-02`、`TIME-08`、`DEP-05`、`CON-07`

### 3.7 科技系统（TECH-*）

#### `TECH-01` `CreateResearchTask` 把 `nodeId` 当 `treeId` —— 【P1｜逻辑层缺口】
- **现象**：读档恢复研究任务时，用 `snapshot.Id`（存的是**科技节点 Id**）去当作**科技树 Id** 获取/创建科技树。
- **证据**：`Scripts/TechTrees/Application/TechTreesAppService.cs:84`（`GetOrCreateTechTree(mapId, ownerId, snapshot.Id)`）；对比写入侧 `:64`（`Id` 存的是 `nodeId`）
- **根因**：任务快照的字段语义不明（`TIME-03` 同一根因）——快照里既没有"所属科技树"也没有"节点"的区分，只有一个 `Id`。
- **影响**：读档后该研究任务会去找一棵**以节点名命名的科技树**（不存在 → `GetOrCreateTechTree` 会新建一棵空树并保存），随后 `tree.Research(nodeId)` 在空树里静默失败（`TechTree.cs:78` 直接 return）→ **科技永远研究不完、且存档里被塞进一棵垃圾科技树**。
- **修复方向**：`TaskSnapshot` 增加明确的上下文载荷（如 `TreeId` + `NodeId` 两个字段，或统一的 `payload` 字典）；写入与读取用同一套语义。
- **关联**：`TIME-03`、`TECH-04`、`TECH-06`

#### `TECH-02` 研究成本硬编码为单资源 `"Idea"` —— 【P1｜数据层缺口】
- **现象**：研究消耗写死 `new Consumption("Idea", cost)`；节点配置里成本只是一个 `float Cost`，没有资源类型、没有多资源成本。
- **证据**：`Scripts/TechTrees/Application/TechTreesAppService.cs:57`；`Scripts/TechTrees/Domain/ITechNodeConfig.cs:10`（`float Cost`）、`Document/TechTreesConfig.json:8`（`"Cost": 50`）
- **根因**：科技树配置表比建筑/单位表"低一代"——后两者早已是 `Dictionary<string, float> ResourceCost`，科技还停留在单数值 + 硬编码资源名。
- **影响**：① 无法做"高级科技需要金子+纸+思想"这类设计（**纯数据层缺口，但改表的同时必须改代码**：`Cost` → `ResourceCost` 会破坏现有配置解析）；② 资源名"硬编码"意味着策划不能把科技点改叫别的名字。
- **修复方向**：把 `ITechNodeConfig.Cost: float` 升级为 `Dictionary<string, float> ResourceCost`（与建筑/单位表统一），服务层用与建造一致的"多资源消耗 + 校验"流程（见 `CON-07` 的共享校验服务）。
- **关联**：`CON-07`、`CON-01`、`TECH-03`

#### `TECH-03` 研究不校验前置就扣费 → 可能白扣 —— 【P2｜逻辑层缺口】
- **现象**：`Research` 流程是"取成本 → 校验资源够 → 扣费 → 建延时任务 → 完成时再调用 `tree.Research(nodeId)`"；而 `TechTree.Research` 内部会再校验 `CanResearch`（前置未满足即**静默 return**）。
- **证据**：`Scripts/TechTrees/Application/TechTreesAppService.cs:50-80`（扣费 `:62`，无前置校验）、`Scripts/TechTrees/Domain/TechTree.cs:76-86`（`:81` 前置不满足则静默失败）
- **根因**：前置校验与资源校验被放在两个不同层次，且都没有把失败原因向上传递（与 `CON-01` 同源）。
- **影响**：玩家（或 AI）在"前置未满足"时点研究 → **资源被扣，科技却没解锁**，且没有任何提示。这是最容易被玩家投诉的那类 bug。
- **修复方向**：在执行前统一做"全部前置（资源 + 科技前置 + 是否已研究）"的校验，并把失败原因返回给 UI；扣费与置位要么同成、要么同败。
- **关联**：`TECH-02`、`CON-01`、`TECH-05`

#### `TECH-04` 研究任务 `UId = "none"` —— 【P2｜架构层缺陷】
- **现象**：研究任务把 `UId` 硬编码为字符串 `"none"` 并附注释"不是占据物"。
- **证据**：`Scripts/TechTrees/Application/TechTreesAppService.cs:64`（`new LinearTask(0, duration, nodeId, "Research", false, "none", mapId, ownerId)`）
- **根因**：`UId` 被设计成"地图占据物的唯一 Id"，但任务体系里存在大量"非占据物任务"（研究、事件、资源增长），**缺少通用的任务主体标识**。
- **影响**：① 无法回答"这条任务属于谁/属于什么"（UI 无法把任务显示在正确的对象上）；② 多玩家/多科技树时无法区分（两个人同时研究同一节点，任务文件键相同 → 覆盖）。
- **修复方向**：把任务主体抽象为"目标引用"（类型 + Id），占据物只是其中一种；或为任务引入 `OwnerScope`（玩家/实体/全局）。
- **关联**：`TIME-03`、`TECH-01`、`DEP-03`

#### `TECH-05` 科技完成没有任何"推送" —— 【P2｜架构层缺陷】
- **现象**：研究完成时只做"树内置位 + 保存 + 挂修正器 + 注销任务"，没有事件/回调/通知可供 UI 或其它系统订阅。
- **证据**：`Scripts/TechTrees/Application/TechTreesAppService.cs:65-77`
- **根因**：与 `UNIT-08` 同源——"推送侧"机制整体缺失。
- **影响**：① 无法实现"研究完成后解锁新建筑/新单位/新动作"这类**内容联动**（只能靠查询时到处 `IsResearched` 判断，属于轮询方案）；② UI 无法弹出"研究完成"提示；③ 无法做"研究完成触发事件/任务"的链式设计。
- **修复方向**：与 `UNIT-08` 一起引入领域事件（`ResearchCompleted` 等），至少让应用层能发布/订阅。
- **关联**：`UNIT-08`、`TECH-03`、`EVT-04`

#### `TECH-06` 反序列化后配置需手动回填（隐式契约） —— 【P2｜逻辑层缺口】
- **现象**：`TechNode.Config` 标了 `[JsonIgnore]`，因此从存档反序列化后为 null；必须由 `TechTree.HydrateConfigs(configRepo)` 逐节点回填，且该方法只在"从仓储取树"的路径上被调用。
- **证据**：`Scripts/TechTrees/Domain/TechNode.cs:9-10`（`[JsonIgnore]`）、`TechTree.cs:45-57`（Hydrate）、`TechTreesRepository.cs:35-38`（仅此处调用）
- **根因**："数据（进度）+ 配置（定义）"分离的必然结果，但回填时机没有强制约束。
- **影响**：任何绕过 `TechTreesRepository.GetTreeById` 的取树路径（例如直接反序列化、或未来新增的缓存层）都会拿到 `Config == null` 的节点，随后 `CanResearch`/`GetCost` 等静默走"空配置分支"返回 false/0（`TechTree.cs:66-93`）。
- **修复方向**：把"配置回填"做成构造/加载的强约束（工厂方法或"加载后必须 Hydrate"的显式 API），或在节点内部通过 Id 惰性查询配置仓库。
- **关联**：`TECH-01`、`TECH-02`

### 3.8 随机事件系统（EVT-*）

#### `EVT-01` 事件配置表结构与 DTO 不匹配 —— 【P0｜数据层缺口】
> ✅ **已修复（v0.3.3 / WP-1.4，v0.3.7 / WP-2.8 收尾）**：表根对象已改为 `{ "Events": [ ... ] }`，装载段对裸数组/形状不符判 error 并阻断启动（无头检查覆盖）。
- **现象**：仓库里的事件配置是**裸数组**；而 DTO 要求根对象含 `Events` 键。
- **证据**：`Document/EventsConfig.json:1`（`[ {...}, ... ]`）vs `Scripts/Events/Domain/EventsConfigDto.cs:9`（`[JsonProperty("Events")] public List<EventConfigDto> EventsData`）；设计文档也写的是带包裹的版本（`Document/ConfigTableGuide.txt:404-406`）
- **根因**：配置表更新了结构（从数组改成对象包裹），但仓库里的那份 JSON 没同步；也可能这份 JSON 是从早期版本直接迁移过来的。
- **影响**：`JsonConvert.DeserializeObject<EventsConfigDto>` 对数组根会**抛异常或得到 null**（取决于序列化器设置，Newtonsoft 对根类型不匹配会抛 `JsonSerializationException`）→ 事件系统要么启动即崩、要么永远没有事件。属于"一接线就暴露"的 P0 数据问题。
- **修复方向**：**只填表**：把 `Document/EventsConfig.json` 改为 `{ "Events": [ ... ] }`（与 `ConfigTableGuide.txt` 的样板保持一致）。顺便建议在配置加载后加"表非空"断言（见 `WIRE-04`）。
- **关联**：`WIRE-03`、`WIRE-04`、`EVT-02`

#### `EVT-02` 每秒判定 vs 文档"每天判定"，且永久事件无撤销 —— 【P1｜逻辑层缺口】
> ✅ **已修复（v0.3.4 / WP-1.5 + v0.3.7 / WP-2.8）**：判定节拍由 1 秒改为**每游戏日一次**（`TimeConstants.EventRollDays` + 逐日派发），字段随设计改名为 `TriggerChancePerDay`（%/日）；永久事件改为 `ActiveEvent` 显式状态，可由 `GetActiveEvents` 查询，并在"生效中不重复触发"（`G8`）。剩余：状态不落盘（§18.4 → `WP-3.2`）。
- **现象**：事件引擎注册的循环任务节拍是 **1 秒**，每个节拍对**所有事件**逐一做概率判定；而策划文档明确写"TriggerChance 每天独立判定"。另外 `Duration = 0`（永久）的事件挂上修正器后**没有任何撤销入口**。
- **证据**：`Scripts/Events/Application/EventAppService.cs:39`（`IntervalTask(0, 1f, ...)`）、`:46-51`（逐事件判定）、`:62`（仅 `Duration > 0` 才注册到期任务）、`:44` vs `Document/ConfigTableGuide.txt:448`（"TriggerChance 每天独立判定"）、`:449`（"Duration=0 表示永久效果，不会被撤销"）
- **根因**：没有"游戏日"概念（`TIME-05`），只能用秒近似；永久效果被当成"不需要撤销"。
- **影响**：① 实际触发频率是文档意图的 **N 倍**（与"一天几秒"的换算强相关），设计数值全部失真；② 永久事件叠加后（`MOD-07`）数值失控且玩家无法理解；③ "永久"在存档/读档、设备之间无法维护语义（因为没有发生记录）。
- **修复方向**：以 `GameClock` 的"天"为判定单位（`TIME-05`）；把永久事件显式建模为"持续生效的世界状态"，提供查询与（若设计需要）手工取消的入口。
- **关联**：`TIME-05`、`TIME-06`、`MOD-07`、`EVT-01`

#### `EVT-03` `StartEventsEngine` 无幂等保护 —— 【P2｜逻辑层缺口】
> ✅ **已修复（v0.3.7 / WP-2.8）**：`StartEventsEngine` 按 `(mapId, ownerId)` 幂等（`_startedEngines`），重复调用不再叠加日节拍任务；用例断言"重复启动后订阅数不变"。
- **现象**：`StartEventsEngine(mapId, ownerId)` 每次调用都会注册一个新的 1 秒循环任务；任务 Id 由 `mapId/ownerId` 拼成，但注册时**不做"是否已注册"检查**。
- **证据**：`Scripts/Events/Application/EventAppService.cs:37-42`
- **根因**：缺少"引擎是否已启动"的状态标记。
- **影响**：场景重载、读档后再次启动、或 UI 重复调用 → 多个事件引擎同时运行，触发概率成倍放大（且每个引擎各自写修正器 → `MOD-07`）。
- **修复方向**：加启动标记/幂等校验；或把"事件引擎"作为随 Session 生命周期管理的单例系统。
- **关联**：`TIME-02`、`MOD-07`、`EVT-02`

#### `EVT-04` 事件无日志、无 UI 推送、无队列 —— 【P2｜架构层缺陷】
> ◐ **部分修复（v0.3.7 / WP-2.8）**：已提供 `GetActiveEvents`（含剩余天数）与 `GetTriggerCounts`（累计触发次数）供 UI/排查使用；仍缺"触发瞬间的推送/日志"（领域事件总线 → `WP-2.10`）与历史记录。
- **现象**：事件触发后只做"扣资源 + 挂修正器 + （可选）注册到期任务"，没有任何对外通知、没有触发记录、没有"当前生效事件"的查询接口。
- **证据**：`Scripts/Events/Application/EventAppService.cs:44-73`
- **根因**：与 `TECH-05`/`UNIT-08` 同源——推送侧缺失；事件被视为纯数值挂载。
- **影响**：① 玩家不知道自己触发了什么事件（`Name`/`Description` 字段配了却没人用，见 `EventConfigDto.cs:9-10`）；② 无法做事件面板/历史记录/弹窗；③ 无法排查"数值为什么变了"（配合 `MOD-07`）。
- **修复方向**：事件触发时发布领域事件（携带配置文案与修正器信息）；提供"当前生效事件列表"查询供 UI 展示倒计时。
- **关联**：`UNIT-08`、`TECH-05`、`MOD-07`、`EVT-02`

#### `EVT-05` 先扣资源后挂修正器，无回滚 —— 【P2｜逻辑层缺口】
- **现象**：判定阶段遍历所有前置条件（资源够 + 科技满足）→ 通过后 `ConsumePrerequisites` 立刻扣资源 → 紧接挂修正器。若挂修正器/注册到期任务抛异常，资源已经扣掉。
- **证据**：`Scripts/Events/Application/EventAppService.cs:52-71`（判定 `:52`、扣费 `:55`、挂修正器 `:57-60`、到期任务 `:62-71`）
- **根因**：与 `CON-01` 同源——"检查-执行"分离但没有事务边界。
- **影响**：极端情况（配置错误导致 `Modifier.Target` 为空、文件写入失败）会出现"资源扣了、事件没生效"。虽然概率低，但由于事件是概率触发的，这类脏状态很难被发现与复现。
- **修复方向**：与 `CON-01` 一起统一"业务操作要么全成要么全败"的策略，至少做到"先准备、后提交"的顺序（把所有可能失败的操作放在扣费之前校验）。
- **关联**：`CON-01`、`TECH-03`、`MOD-07`

### 3.9 战争迷雾（FOG-*）

#### `FOG-01` 有状态单例 + 构造注入 ownerId → 多玩家必炸 —— 【P1｜架构层缺陷】
- **现象**：`FogAppService` 在构造函数里接收 `ownerId`，并把迷雾矩阵（`_fogMatrix`）与视野计数（`_visionCount`）作为**实例字段**持有。它是全仓库唯一"带玩家状态的服务"，而 `MapAppService`/`UnitsAppService`/`ConstructionAppService` 的构造函数都直接注入**单个** `FogAppService` 实例。
- **证据**：`Scripts/Fog/Application/FogAppService.cs:14,17-24`（`_ownerId` + 两个字典）；被注入处 `MapAppService.cs:105`（作为方法参数）、`UnitsAppService.cs:24,34,43`、`ConstructionAppService.cs:23,33,42`
- **根因**：迷雾是"每玩家视角"的数据，却被实现为"单例服务"，且**服务与数据没有分离**（服务里存了数据）。多玩家支持在设计初期没有落到类型上。
- **影响**：① **第二个玩家加入时，两个玩家的视野会互相污染**（同一实例、同一 ownerId）；② `FogAppService` 的实例无法安全共享给不同玩家；③ 现有 AppService 的构造函数签名（注入单实例）**架构上无法表达"每玩家一份"**——这是多玩家改造中最先撞到的墙；④ 因为 `MapAppService.FindPath` 直接把 `FogAppService` 当方法参数传来传去，改造会牵动寻路、单位移动、建造等多个模块。
- **修复方向**：把迷雾拆成"无状态服务/规则"+"每玩家（或每观察者）的迷雾状态对象"，由 Session 按 ownerId 提供；`FindPath` 等 API 改为接收"可见性查询接口"而不是具体服务实例。**这是所有"多玩家/观察者视角"需求的前置。**
- **关联**：`DEP-03`、`MAP-01`、`MOD-03`、`FOG-04`

#### `FOG-02` 迷雾记忆永不回退，且无"遗忘"策略 —— 【P2｜逻辑层缺口】
- **现象**：`RevealArea` 只会把格子提升到 `Visible`（并给外围一圈标记 `Fogged`）；`ResetArea` 只把 `Visible` 降级为 `Fogged`（当视野计数归零）。**`Fogged` 永远不会回到 `Unexplored`**，也没有"上次看到的时间戳/记忆衰减"。
- **证据**：`Scripts/Fog/Application/FogAppService.cs:56-97`（`RevealArea` `:62-64`、`ResetArea` `:90-95`）
- **根因**：实现采用了"记忆式迷雾"这一种模式，没有把迷雾模式参数化。
- **影响**：① 若设计稿要求"离开视野后变回未知/需要重新侦察"，当前实现无法表达；② "已探索区域"永久保留地形记忆，但**占据物（单位/建筑）不落盘**（`MAP-02`），读档后会出现"地图亮着但上面什么都没有"的诡异状态（玩家以为有城市，实际是空的）。
- **修复方向**：把迷雾模式做成可配置策略（永久记忆 / 变回雾 / 完全重置）；并与 `MAP-02` 一起决定"记忆内容"是否包含占据物。
- **关联**：`MAP-02`、`FOG-04`、`FOG-01`

#### `FOG-03` 半径枚举每次重算，无缓存 —— 【P2｜性能】
- **现象**：`RevealArea`/`ResetArea` 每次调用都重新枚举半径内的全部格子（以及外圈环）；单位每移动一格都会调用一次 Reset + 一次 Reveal。
- **证据**：`Scripts/Fog/Application/FogAppService.cs:116-142`（两个枚举器）；调用频率见 `UnitsAppService.cs:201-202,225-226`
- **根因**：没有预计算"半径模板"（同半径的偏移集合是可复用的）。
- **影响**：视野半径 5 时每次移动约 2×91 次字典操作，单位多时是明显的可优化热点（虽然目前不是瓶颈，但配合"每格移动都刷新"会放大）。
- **修复方向**：缓存半径模板（按 radius 预生成偏移列表）；移动时只做"增量更新"（进入/离开的格子集合差异）。
- **关联**：`FOG-01`、`FOG-04`

#### `FOG-04` 迷雾存档每次全量序列化 —— 【P2｜性能 + 数据层缺口】
- **现象**：`Save` 每次都把**整张迷雾矩阵**转成"`"q,r" → byte`"的字典并整体写文件；`Load` 反向解析全部键值并逐条 `Split(',')`。
- **证据**：`Scripts/Fog/Application/FogAppService.cs:99-114`（保存）、`:33-49`（加载）、`Scripts/Fog/Infrastructure/FogRepository.cs:25-36`
- **根因**：没有增量/压缩策略，也没有利用"迷雾矩阵是稀疏/规则结构"这一特点。
- **影响**：① 大地图存档体积膨胀（1 万格 → 1 万个字符串键）；② 每次保存都是 O(格数) 的字符串拼接与解析；③ 迷雾与其它玩家数据混在同一个 `mapId_ownerId` 文件里（见 `DEP-06`/`TIME-08`），存档一致性无保障。
- **修复方向**：改为紧凑表示（按行/块的位图或 RLE），或只在存档点保存；并纳入统一存档单元（`TIME-08`）。
- **关联**：`FOG-03`、`TIME-08`、`MAP-02`

### 3.10 依赖方向与工程基础（DEP-*）

#### `DEP-01` Domain 层反向依赖：Common → Map —— 【P1｜架构层缺陷】
- **现象**：`Common/Domain` 下的 `PopulationConsumption` 直接引用 `Map.Domain.MapCell`（构造函数参数与字段类型），导致"公共契约层"依赖"具体业务域"。
- **证据**：`Scripts/Common/Domain/PopulationConsumption.cs:1,5-7`
- **根因**："人口消耗"这一业务概念被放进了公共契约目录，而实现时又必须知道格子类型。
- **影响**：① `Common` 无法被其它模块独立引用（任何引用 `IConsumable` 的代码都会被迫拖上 Map 域）；② 若未来出现"第二张地图类型/格子类型"，公共层会被迫修改；③ 破坏了"Domain 隔离"的既定边界（设计哲学第 2 条）。
- **修复方向**：把 `PopulationConsumption` 移到"拥有者"模块（如 Construction 或新建的 Population 模块），或在 `Common` 定义"可消耗宿主"接口（`IPopulationHolder`）由 `MapCell` 实现，反转依赖方向。
- **关联**：`CON-05`、`CON-07`、`IConsumable` 的归属

#### `DEP-02` AppService 网状互依赖，无编排层/事件总线 —— 【P1｜架构层缺陷】
- **现象**：应用服务之间直接互相注入：`ConstructionAppService` 依赖 Map/Resources/TechTrees/Modifier/Fog；`UnitsAppService` 依赖 Map/TechTrees/Resources/**ConstructionAppService**/Fog；`EventAppService` 依赖 Resources/TechTrees/Modifier；`TechTreesAppService` 依赖 Resources/Modifier；`ResourcesAppService` 依赖 ModifierRepository。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:16-23`、`Scripts/Units/Application/UnitsAppService.cs:17-24`、`Scripts/Events/Application/EventAppService.cs:14-19`、`Scripts/TechTrees/Application/TechTreesAppService.cs:13-17`、`Scripts/Resources/Application/ResourcesAppService.cs:11-14`
- **根因**：用"服务互相调用"代替了"用例编排 + 领域事件"；每个模块直接伸手拿别的模块的能力。
- **影响**：① 依赖图接近全连通，**任何改动都可能波及多个模块**；② 一旦出现反向调用（例如建造完成通知单位模块）就会形成**构造循环依赖**，而手写 `new` 的组装方式会直接卡死（无 DI 容器可延迟解析）；③ 单个 AppService 的构造参数已达 8 个（`ConstructionAppService.cs:25-33`），新增能力会继续膨胀；④ 无法独立测试任一模块（构造它需要先构造几乎所有其它模块）。
- **修复方向**：引入较薄的用例编排层或领域事件（配合 `UNIT-08`），把"跨模块联动"从"构造函数耦合"改为"消息/事件订阅"；至少为跨模块协作定义"端口接口"（只暴露需要的方法）。
- **关联**：`UNIT-08`、`WIRE-01`、`DEP-03`

#### `DEP-03` 无 Session/GameContext，mapId/ownerId 逐方法传参 —— 【P1｜架构层缺陷】
- **现象**：所有应用服务方法都以 `(string mapId, ..., int ownerId)` 开头，玩家/地图上下文靠字符串与整数在调用链上手工传递；没有任何"当前对局/当前玩家"的对象。
- **证据**：全局遍布，例如 `MapAppService.cs:21,31,38,44,50,56,62,68,74,80,86,92,98,105`、`ConstructionAppService.cs:45,97,110,123`、`UnitsAppService.cs:46,95,106`、`EventAppService.cs:37,44`、`FogAppService.cs:26,33,99`
- **根因**：单地图/单玩家的 MVP 假设下，"上下文"是最省事的做法；但多玩家目标要求"谁在操作"是一等概念。
- **影响**：① **多玩家无法隔离**：无法按玩家分派服务实例/状态（`FOG-01`、`MOD-03` 的根因之一）；② 遗漏传参不会被编译器发现（`CON-04` 就是实例）；③ 每个 API 签名冗长、易错；④ 无法实现"当前视角/当前玩家"切换（观战、回放、AI 托管）。
- **修复方向**：引入 `GameSession`（地图集 + 玩家表 + 各玩家的子系统实例），把 `mapId/ownerId` 收敛为会话/玩家对象的属性；应用服务改为"每会话/每玩家"实例或显式接收会话对象。**与 `MAP-01`、`FOG-01`、`MOD-03` 是同一改造工程。**
- **关联**：`MAP-01`、`FOG-01`、`MOD-03`、`WIRE-01`、`CON-04`

#### `DEP-04` 命名空间与目录错位 —— 【P2｜可维护性】
- **现象**：目录 `Scripts/TechTrees/` 下的命名空间是 `SciencePotato.Scripts.TechTree`（单数）；`MapAppService` 中大量使用 `Domain.Map` 前缀以避免与 `Map.Domain` 命名空间冲突（同一文件里 `Map.Domain.Map` 与域内 `Map` 类并存）。
- **证据**：`Scripts/TechTrees/Domain/TechTree.cs:5`（`TechTree.Domain`）、`Scripts/TechTrees/Application/TechTreesAppService.cs:7`（别名）、`Scripts/Map/Application/MapAppService.cs:17,33,146,158`（`Domain.Map`）
- **根因**：目录在演进中改过名/复数化，命名空间未同步；`Map` 类名与命名空间同名是 C# 常见命名陷阱。
- **影响**：新代码的 `using` 全靠猜；IDE 搜索类名会命中多处；重构时容易漏文件。
- **修复方向**：统一"目录名 = 命名空间末段"，并给域根类起不冲突的名字（或统一用别名 `using MapDomain = ...`）。
- **关联**：`DEP-05`、`DEP-10`

#### `DEP-05` 命名误导：配置仓库叫 `IUnitsRepository` —— 【P2｜可维护性】
- **现象**：建筑侧是 `IBuildingConfigRepository`（配置仓库），单位侧却是 `IUnitsRepository`（但只提供 `GetUnitConfig`）；而 `IResourcesRepository`（真正的状态仓库：`SaveResources/LoadResourcesPool`）与 `IResourcesConfigRepository`（配置）是分开的。
- **证据**：`Scripts/Units/Domain/IUnitsRepository.cs:9-12` vs `Scripts/Construction/Domain/IBuildingConfigRepository.cs:9-12`、`Scripts/Resources/Domain/IResourcesRepository.cs:3-7`、`IResourcesConfigRepository.cs:3-6`
- **根因**：三个模块由不同时间点实现，命名约定没有统一裁决。
- **影响**：开发者会误以为单位已有状态持久化（`UNIT-09`）；后续真要加单位状态仓库时会撞名。
- **修复方向**：按资源模块的命名法统一：配置仓库一律 `I*ConfigRepository`，状态仓库一律 `I*Repository`。
- **关联**：`UNIT-09`、`DEP-04`、`CON-07`

#### `DEP-06` 两套 JSON 库、两套存档目录约定 —— 【P2｜工程一致性】
- **现象**：配置/任务/修正器用 `Newtonsoft.Json`；地图存档用 `System.Text.Json`（`GodotMapRepository.cs:8,33,75`）；而 `ResourcesPool`/`TechTree` 等又用 Newtonsoft 特性标注。存档路径靠各仓库各自拼接 `前缀 + mapId + "_" + ownerId`（三个仓库各写一份 `BuildFilePath`）。
- **证据**：`GodotMapRepository.cs:8,14,25,33,53,75`（System.Text.Json + `user://maps/`）、`TaskRepository.cs:14-23`、`ModifierRepository.cs:15-18`、`ResourcesRepository.cs:20-23`、`TechTreesRepository.cs:22-25`、`FogRepository.cs:20-23`
- **根因**：没有统一的"存档基础设施"（路径规范、序列化器、原子写入、版本号），各仓库各写一遍。
- **影响**：① 序列化行为不一致（命名策略、null 处理、枚举、私有字段）；② 存档文件分散、命名规则重复实现（改一次要改五处）；③ 无统一版本号/迁移机制，未来改存档结构会破坏兼容（对 4X 长线游戏是硬伤）。
- **修复方向**：建立统一存档基础设施：单一序列化器与设置、统一路径与原子写入（先写临时文件再替换）、统一 `saveVersion` 与迁移钩子。
- **关联**：`TIME-08`、`MAP-07`、`FOG-04`

#### `DEP-07` 无测试工程、无 CI、Domain 不可独立运行 —— 【P2｜工程基础】
- **现象**：解决方案里只有主工程（`Science Potato.csproj`，Godot.NET.Sdk），没有测试项目；`.csproj:11-12` 还排除了两个不存在的路径（`Scripts\Uniit\**`、`Scripts\Units\Events\**`）。Domain 层虽声称"纯 C#"，但：唯一 `IMapRepository` 实现用 `FileAccess`（Godot API）、唯一 `ITimeService` 实现是 Node、地形与生成器配置走 `ResourceLoader`。
- **证据**：`Science Potato.csproj:1-20`、`Scene/Autoload/*`、`Scripts/Map/Infrastructure/GodotMapRepository.cs:27-30,76`、`Scripts/Common/Infrastructure/GodotConfigService.cs:12,24`、`Scripts/Common/Infrastructure/GodotTimeService.cs:8`
- **根因**：MVP 阶段优先"能在编辑器里跑起来"，测试与抽象边界让位于速度。
- **影响**：① **任何重构（含本文档里的 P0/P1 修复）都没有安全网**，只能靠手测；② Domain 的"隔离"目前只是"文件夹隔离"，`Scripts/Common/Domain` 之外仍会被引擎 API 渗透；③ 无法验证"多 target 修正""时间推进""存档一致性"这类纯逻辑（这些恰恰是最容易出错又最难手测的部分）。
- **修复方向**：① 新建 xUnit/NUnit 测试工程（net8.0），先覆盖最关键的纯逻辑（`ModifierManager.GetValue`、`IntervalTask/LinearTask`、`HexCubePosition`、`TechTree.CanResearch`、`ResourcesPool` 边界）；② 为 `IMapRepository`/`ITimeService`/`IConfigLoader` 提供内存实现，使 Domain/Application 能脱离 Godot 运行；③ 移除 csproj 里的无效排除。
- **关联**：`MOD-01`、`TIME-07`、`DEP-06`、`WIRE-04`

#### `DEP-08` 空目录与失效的 csproj 排除项 —— 【P3｜整洁性】
- **现象**：`Scripts/Time`、`Scripts/Event`、`Scripts/Research`、`Scripts/Tree` 是空目录（规划过、未落地）；`.csproj` 排除的 `Scripts\Uniit\**`、`Scripts\Units\Events\**` 都不存在。
- **证据**：目录列表（`Get-ChildItem` 结果为空）、`Science Potato.csproj:11-12`
- **根因**：模块规划变动后未清理。
- **影响**：误导新人以为"时间域/事件域有独立目录"（实际都在 `Common`）；csproj 排除项在出现同名文件时会**静默地把代码排出编译**（很难排查）。
- **修复方向**：删除空目录与失效排除项；若要保留分区意图，改成真实目录 + README 说明。
- **关联**：`DEP-04`、`WIRE-03`

#### `DEP-09` `IRandom` 接口签名与实现参数顺序相反 —— 【P3｜工程隐患】
- **现象**：接口声明 `int Next(int max, int min)`，实现的参数名是 `(int min, int max)`。真正的语义由实现决定；唯一调用点写的是 `_random.Next(0, x)`（即 min=0, max=x）。
- **证据**：`Scripts/Common/Domain/IRandom.cs:7` vs `Scripts/Common/Infrastructure/SystemRandom.cs:13-16`；调用 `Scripts/Map/Infrastructure/VoronoiMapGenerator.cs:68-69`
- **根因**：接口按"先范围后期望"的直觉写，实现按 .NET `Random.Next(min,max)` 写。
- **影响**：任何**照着接口签名**写的调用（`Next(max, min)`）都会静默产生错误范围，甚至 `min > max` 抛异常；随机地图生成这种"看不见的错误"很难被发现。
- **修复方向**：统一参数顺序与命名（建议与 BCL 一致：`Next(min, max)`），并给 `IRandom` 补测试。
- **关联**：`DEP-07`、`MAP-11`

#### `DEP-10` UML 与文档和代码不一致 —— 【P3｜文档】
- **现象**：`UML/PackageDiagram.txt` 声明了不存在的 `IModifier` 契约、把 `ITickable` 归入"Contracts"并画了与实现不符的依赖关系（如 `IMapOccupant -> IModifier : Hold`）；`UML/BuildSequence.txt` 描述的成功/失败事件在代码中不存在。
- **证据**：`UML/PackageDiagram.txt:15,47,52`、`UML/BuildSequence.txt:17,19`
- **根因**：UML 写于实现之前，之后未同步。
- **影响**：新人按 UML 理解会走错方向；评审时无法用 UML 作为"应当实现成什么样"的判据。
- **修复方向**：在 `WIRE-01` 组合根落地后，用真实依赖图重画 UML；`BuildSequence` 补上实现后的真实分支（含失败原因返回）。
- **关联**：`MOD-08`、`WIRE-01`、`CON-01`

---

## 4. 契约 ↔ 实现 ↔ 使用状态 对照表

> 用途：改造时一眼看出"哪些契约是活的、哪些是死的、哪些接线了但没通电"。

| 契约 | 定义位置 | 实现 | 运行时状态 |
| :--- | :--- | :--- | :--- |
| `ITickable` | `Common/Domain/ITickable.cs:9` | `LinearTask`、`IntervalTask` | ⚠️ 契约在、驱动器缺失（`WIRE-02`） |
| `IProgressTask` | `Common/Domain/IProgressTask.cs:9` | `LinearTask`、`IntervalTask` | ⚠️ 无非占据物主体标识、无终结语义（`TIME-02/03/09`） |
| `ITimeService` | `Common/Application/ITimeService.cs:10` | `GodotTimeService`（Node） | ❌ 未实例化、未入场景（`WIRE-02`） |
| `IRequirement` | `Common/Domain/IRequirement.cs:9` | `TerrainRequirement`、`TechRequirement` | ✅ 已用；条件类型偏少（无资源/人口/事件条件） |
| `IConsumable` | `Common/Domain/IConsumable.cs:9` | `ResourcesConsumption`、`PopulationConsumption` | ✅ 已用；后者反向依赖 Map（`DEP-01`） |
| `IMapOccupant` | `Common/Domain/IMapOccupant.cs:9` | `Building`、`Unit` | ✅ 已用；只提供 `GetInfo()`，无持久化契约（`MAP-02`） |
| `IRandom` | `Common/Domain/IRandom.cs:5` | `SystemRandom` | ✅ 地图生成使用；签名顺序矛盾（`DEP-09`） |
| `IConfigLoader` | `Common/Domain/IConfigLoader.cs:5` | `GodotConfigService` | ⚠️ 只能读 `.tres/.res`，无法读 JSON（`WIRE-03`） |
| `ITaskRepository` | `Common/Domain/ITaskRepository.cs:10` | `TaskRepository` | ❌ 未实例化；键冲突与每帧写盘（`TIME-01/03`） |
| `IModifierRepository` | `Common/Domain/IModifierRepository.cs:5` | `ModifierRepository` | ⚠️ 未实例化；作用域仅玩家（`MOD-03`） |
| `IMapGenerator` | `Map/Domain/IMapGenerator.cs:3` | `VoronoiMapGenerator`（另有 `PerlinMapGenerator`、`RandomAnchorMapGenerator` **未被使用**） | ✅ 已用 |
| `IMapGeneratorConfig` | `Map/Domain/IMapGeneratorConfig.cs:3` | `GeneratorConfigResources`（`.tres`） | ✅ 已用（`Density=4`） |
| `ITerrainData` | `Map/Domain/ITerrainData.cs:3` | `TerrainConfigResources`（`.tres`） | ⚠️ 已用，但数据缺 `MoveCost`（`MAP-06`） |
| `IMapRepository` | `Map/Domain/IMapRepository.cs:5` | `GodotMapRepository` | ⚠️ 已用；`DeleteMap/ListMaps` 未实现（`MAP-08`） |
| `IResourceConfig` / `IResourcesPoolConfig` | `Resources/Domain/*.cs` | `ResourceConfigDto` / `ResourcesPoolConfigDto` | ✅ 结构正确（对应 `ResourcesConfig.json`） |
| `IResourcesConfigRepository` | `Resources/Domain/IResourcesConfigRepository.cs:3` | `ResourcesConfigRepository` | ❌ 未实例化（`WIRE-01/03`） |
| `IResourcesRepository` | `Resources/Domain/IResourcesRepository.cs:3` | `ResourcesRepository` | ❌ 未实例化 |
| `IBuildingConfig` / `IBuildingsConfig` | `Construction/Domain/*.cs` | `BuildingConfigDto` / `BuildingsConfigDto` | ✅ 结构正确（`BuildingsConfig.json`） |
| `IBuildingConfigRepository` | `Construction/Domain/IBuildingConfigRepository.cs:9` | `BuildingsConfigRepository` | ❌ 未实例化 |
| `IUnitConfig` / `IUnitsConfig` | `Units/Domain/*.cs` | `UnitConfigDto` / `UnitsConfigDto` | ✅ 结构正确（`UnitsConfig.json`） |
| `IUnitsRepository` | `Units/Domain/IUnitsRepository.cs:9` | `UnitsConfigRepository` | ❌ 未实例化；**命名错位**（其实是配置仓库，`DEP-05`） |
| `ITechNodeConfig` / `ITechTreeConfig` / `ITechTreesConfig` | `TechTrees/Domain/*.cs` | `*ConfigDto` | ⚠️ 结构正确但成本只有 `float Cost`（`TECH-02`） |
| `ITechTreesConfigRepository` | `TechTrees/Domain/ITechTreesConfigRepository.cs:3` | `TechTreesConfigRepository` | ❌ 未实例化 |
| `ITechTreesRepository` | `TechTrees/Domain/ITechTreesRepository.cs:3` | `TechTreesRepository` | ❌ 未实例化 |
| `IEventConfig` / `IEventsConfig` | `Events/Domain/*.cs` | `EventConfigDto` / `EventsConfigDto` | ❌ **JSON 结构不符**（`EVT-01`） |
| `IEventConfigRepository` | `Events/Domain/IEventConfigRepository.cs:5` | `EventConfigRepository` | ❌ 未实例化 |
| `IFogRepository` | `Fog/Domain/IFogRepository.cs:3` | `FogRepository` | ❌ 未实例化 |

**其它孤立/未接线实现**（P3，可顺手处理）：

| 文件 | 现状 |
| :--- | :--- |
| `Scripts/Map/Infrastructure/PerlinMapGenerator.cs` | 实现了 `IMapGenerator` 但无人使用（备选算法） |
| `Scripts/Map/Infrastructure/RandomAnchorMapGenerator.cs` | 同上 |
| `Scripts/Common/Presentation/CameraController.cs` | **未被任何场景引用**；`Scene/Map/map_view.tscn:11-12` 里的 `Camera2D` 没挂脚本，所以 `project.godot` 配的 `zoom_in/zoom_out` 输入实际无效 |
| `Scripts/Common/Domain/ConfigManager.cs` | 只是个字符串缓存，无人使用（且没有"谁负责填充缓存"的路径） |
| `Scripts/Common/Application/ModifierAppService.cs:15` `AddModifier`（单个） | 无调用点（只用 `AddModifiers` / `RemoveModifiersBySourceId`） |
| `Scripts/Resources/Domain/ResourcesPool.cs:56` `AddLimit` | 无调用点（资源上限无法增长） |

---

## 5. 配置表 ↔ DTO ↔ 代码 交叉核对

### 5.1 配置来源与存放位置

| 配置 | 仓库位置 | DTO / 接口 | 结构是否匹配 | 加载入口 |
| :--- | :--- | :--- | :--- | :--- |
| 资源池 | `Document/ResourcesConfig.json` | `ResourcesPoolConfigDto`/`IResourcesPoolConfig` | ✅ 匹配（根 `{"Resources":[...]}`） | ❌ 无 |
| 建筑 | `Document/BuildingsConfig.json` | `BuildingsConfigDto`/`IBuildingsConfig` | ✅ 匹配（根 `{"Buildings":{id:{...}}}`） | ❌ 无 |
| 单位 | `Document/UnitsConfig.json` | `UnitsConfigDto`/`IUnitsConfig` | ✅ 匹配（根 `{"Units":{id:{...}}}`） | ❌ 无 |
| 科技树 | `Document/TechTreesConfig.json` | `TechTreesConfigDto`/`ITechTreesConfig` | ✅ 匹配（根 `{"TechTrees":{...}}`） | ❌ 无 |
| 事件 | `Document/EventsConfig.json` | `EventsConfigDto`/`IEventsConfig` | ❌ **不匹配**：文件是裸数组，DTO 要求 `{"Events":[...]}`（`EVT-01`） | ❌ 无 |
| 地形 | `Config/Terrains/*.tres` | `TerrainConfigResources`/`ITerrainData` | ⚠️ 字段缺 `MoveCost`（`MAP-06`） | ✅ `GodotConfigService.LoadAll` |
| 地图生成器 | `Config/Generator/Generator.tres` | `GeneratorConfigResources`/`IMapGeneratorConfig` | ✅（`Density=4.0`） | ✅ `GodotConfigService.Load` |

**结论**：
- `Document/` **不是 Godot 资源路径**（既不是 `res://` 也不是 `user://`），导出后不可用；需要决定迁移到 `res://Config/*.json`（随包发布）还是 `user://`（支持热更）。
- 5 张 JSON 表全部**没有加载入口**（`WIRE-03`），`.tres` 两条链路是唯一通电的配置。

### 5.2 字段级核对要点（只列有风险的部分）

| 表 | 字段 | 现状 | 风险 |
| :--- | :--- | :--- | :--- |
| Buildings | `ResourceCost: {"Wood":20}` | 多资源消耗已支持，`ConstructionAppService.cs:49-56` 展开为 `IConsumable` 列表 | ✅ 无需改动（这就是设计中"多资源消耗直接支持"的证据） |
| Buildings | `Actions: ["CanResearch"]` | 只有 `CanResearch` 在建筑侧 switch 中实现（`ConstructionAppService.cs:131-136`） | ⚠️ 新增动作要改核心代码（`CON-02`） |
| Buildings | `IsHousing/PopulationRadius/PopulationCap/PopulationGrowthInterval` | 全部生效于 `RegisterHousingTask`，但任务不注销、人口不落盘、`OwnerId` 写成 0 | ❌ 功能不完整（`CON-04/05/06`） |
| Buildings | `VisionRadius` | 建造完成时 `RevealArea` 生效（`ConstructionAppService.cs:85`） | ⚠️ 续跑路径不生效（`CON-03`） |
| Units | `Attack: 8` / `AttackDamage: 8` | 两个字段数值相同，但**只有 `AttackDamage` 被使用**（`UnitsAppService.cs:326`） | ❌ 策划改 `Attack` 无效（`UNIT-01`） |
| Units | `Movement: 4` / `MoveRechargePerTick: 2` | 前者只作为初始 MP 与未使用字段；后者成为 MP 上限 | ❌ 语义冲突（`UNIT-01/02`） |
| Units | `PopulationCost: 1` | 由 `PopulationConsumption` 生效（`UnitsAppService.cs:62-67`） | ✅ 可用（但人口不落盘） |
| Units | `VisionRadius` | 同时被"开视野"和"停止移动的索敌半径"两处使用 | ⚠️ 语义过载（`UNIT-06`） |
| TechTrees | `Cost: 50` | 服务层硬编码资源名 `"Idea"`（`TechTreesAppService.cs:57`） | ❌ 无法配置多资源（`TECH-02`） |
| TechTrees | `Modifiers: [{Target:"swordsmanAttack"}]` | 修正器能被挂载，但**战斗计算根本不读修正器** | ❌ 数值不生效（`UNIT-01`） |
| TechTrees | `Prerequisites` | 生效（`TechTree.cs:100-124`），**v0.3.5 / WP-2.1 起支持跨树** | ✅ 跨树前置（`TECH-07` 已修） |
| Events | `TriggerChance` | 按 1 秒判定（文档写"每天"） | ❌ 频率与文档不符（`EVT-02`） |
| Events | `Duration: 0`（永久） | 挂上后不撤销、且每次触发叠加一份 | ❌ 数值失控（`EVT-02`、`MOD-07`） |
| Events | `Name/Description` | 配置齐全但**代码从不读取** | ⚠️ UI 无法展示（`EVT-04`） |
| Resources | `DependentModifiers: ["GoldGrowth"]` | 生效，但 `GetValue` 只处理第一个命中的 target | ❌ 多 target 失效（`MOD-01`） |
| Resources | `BaseLimit` | 生效（`ResourcesPool.AddValue` 截断） | ⚠️ 上限无法增长（`AddLimit` 无调用） |
| Terrains（.tres） | `Id` | 值为 `test1..test5`，与 JSON 引用的 `plain` 不匹配 | ❌ 建造/训练全被拒（`MAP-05`） |
| Terrains（.tres） | `MoveCost` | 未填写 → 默认 0 → 已探索格不可通行 | ❌ 移动逻辑异常（`MAP-06`） |

### 5.3 文档（`Document/ConfigTableGuide.txt`）与实现的不一致清单

| 文档描述 | 位置 | 实现现状 | 对应问题 |
| :--- | :--- | :--- | :--- |
| 事件 `TriggerChance` "每天独立判定" | `ConfigTableGuide.txt:448` | 每秒判定 | `EVT-02` |
| 事件 `Duration=0` "永久效果，不会被撤销（除非手动调用 RemoveModifiers）" | `ConfigTableGuide.txt:449` | 无任何"手动撤销"入口 | `EVT-02`、`MOD-07` |
| 事件 "Duration>0 时到期自动撤销该事件的所有 Modifier" | `ConfigTableGuide.txt:450` | 已实现（按 `EventId` 为 sourceId） | ✅ |
| `DependentModifiers` 支持多个 target 累加 | `ConfigTableGuide.txt:46-51` | 只累加第一个命中 target | `MOD-01` |
| 事件表根为 `{"Events":[...]}` | `ConfigTableGuide.txt:404-406` | 仓库里的 JSON 是裸数组 | `EVT-01` |
| 地形 `.tres` 应包含 `MoveCost`（plain=1/forest=2/…） | `ConfigTableGuide.txt:471-484` | 5 个 `.tres` 都没填 `MoveCost`，Id 还是 `test*` | `MAP-05`、`MAP-06` |
| 资源 `Idea` "建议 Interval=0 靠事件/建筑产出" | `ConfigTableGuide.txt:111` | 一致（`ResourcesConfig.json:24-25`） | ✅ |

---

## 6. 公共根因（问题聚类）

> 79 条问题中，绝大多数可以归到 8 个根因上。**修根因比逐条打补丁更省力**。

### `ROOT-1` "Repository 即聚合根"的简化写法 → 6 条问题
以磁盘往返代替内存聚合：`MAP-01`、`MAP-07`、`MOD-04`、`TIME-08`、`DEP-06`（+ 影响 `UNIT-05`）
**表现**：每个操作读盘写盘；实体对象身份丢失；多模块并发修改互相覆盖。
**建议**：引入 `GameSession` + 内存聚合（Map / ModifierManager / 各玩家资源池）+ 明确的存档点。

### `ROOT-2` 缺少"会话/玩家上下文"与每玩家状态建模 → 4 条问题
`DEP-03`、`FOG-01`、`MOD-03`、`CON-04`（+ 影响多玩家的一切需求）
**表现**：`mapId/ownerId` 手工传参；迷雾服务持有玩家状态；修正器只挂在玩家上。
**建议**：`GameSession` + `PlayerContext`，把"每玩家状态"从服务里剥离成数据对象。

### `ROOT-3` 任务体系缺"主体标识 + 生命周期" → 8 条问题
`TIME-02`、`TIME-03`、`TIME-09`、`TIME-10`、`CON-06`、`UNIT-04`、`TECH-01`、`TECH-04`
**表现**：任务没有稳定的存储键、没有归属、没有终结语义；同名覆盖、永不回收、读档恢复错乱。
**建议**：任务模型升级为 `TaskId(UId) + OwnerScope + Payload`，时间服务负责"完成即回收"，实体移除时按 Scope 批量注销。

### `ROOT-4` "推送侧"（领域事件）整体缺失 → 4 条问题
`UNIT-08`、`TECH-05`、`EVT-04`、`CON-01`（失败原因返回）
**表现**：只能轮询；任何"完成后联动/触发/提示"的需求都无落点。
**建议**：引入轻量领域事件（至少覆盖 实体创建/完成/移除 · 科技完成 · 事件触发 · 战斗受击/死亡）。

### `ROOT-5` 架构意图未落到装配层（组合根缺失）→ 5 条问题
`WIRE-01`、`WIRE-02`、`WIRE-03`、`WIRE-05`、`WIRE-06`
**表现**：模块写完但没有组装；配置没有入口；时间服务没启动。
**建议**：把 `ServiceContainer` 建成唯一组合根，并加启动期配置校验。

### `ROOT-6` 数值体系"一条公式 + 玩家级存储" → 5 条问题
`MOD-01`、`MOD-02`、`MOD-03`、`MOD-07`、`UNIT-01`（战斗/移动不读修正器）
**表现**：多 target 失效；无阶段/上限；无法做对象级 buff 或全局环境状态；战斗数值走硬编码而非 Modifier。
**建议**：修正器引入"宿主作用域 + 阶段管道"，并把战斗/移动的数值读取全部改为经过 `ModifierManager`。

### `ROOT-7` 缺少测试与内存实现的安全网 → 1 条（但阻塞所有重构）
`DEP-07`
**表现**：Domain 无法脱离 Godot 运行；改动只能靠手测。
**建议**：先建测试工程 + 内存版 Repository/TimeService，再动核心。

### `ROOT-8` 原型资产/配置未与"设计文档期望"对齐 → 3 条
`MAP-05`、`MAP-06`、`EVT-01`（属"只填表/只改资产"即可解决的一类）
**表现**：地形 Id 与 JSON 不匹配、地形缺 MoveCost、事件表结构过期。
**建议**：一次性对齐数据，并在组合根做"启动期校验"防止再发生。

---

## 7. 初步修复路线图（待设计稿定稿后修订）

> ⚠️ **本节为 v0.1 的初步路线图，已被第 13 节「第四阶段：最小化改造清单」取代（保留为历史记录）**；新的批次、依赖与里程碑见第 13、14 节。

> 排序逻辑：**先通电 → 再保证核心数据不丢 → 再修玩法正确性 → 最后体验与整洁**。
> "只填表"= 纯数据/资产改动；"写代码"= 需新增或重写 C# 类。

### 阶段 A：让链路"通上电"（P0，前置）

| 序 | 任务 | 涉及文件/模块 | 新增类/接口/字段 | 工作量 | 填表/写代码 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| A1 | 组合根改造：构造全部 AppService/Factory/Repository | `Scripts/Autoload/ServiceContainer.cs` | 无（仅组装）；建议加 `ServiceRegistry` 简化 | 中 | 写代码 |
| A2 | 启动时间服务并挂进场景树 | `GodotTimeService.cs`、`Scene/Autoload/service_container.tscn` | 无 | 小 | 写代码 |
| A3 | 配置 JSON 加载入口（路径 → json 字符串） | 新增 `IJsonConfigSource`（或类似）+ 组合根 | 1 个接口 + 1 个实现 | 小 | 写代码 |
| A4 | 配置表落位迁移（`Document/` → `res://Config/`） | `Document/*.json`、csproj/project 导出设置 | 无 | 小 | 填表 + 少量配置 |
| A5 | 修事件表结构（裸数组 → `{"Events":[]}`） | `Document/EventsConfig.json` | 无 | 小 | **只填表** |
| A6 | 补齐地形资产：Id 对齐 + `MoveCost` | `Config/Terrains/*.tres` | 无（建议后续加 `Passable` 字段取代魔法值 0） | 小 | **只填表/资产** |
| A7 | 启动期配置校验（表非空、引用 Id 存在、枚举值合法） | 组合根 + 配置仓库 | 1 个校验器 | 小 | 写代码 |
| A8 | 修 `MapView` 依赖注入 | `MapView.cs`、`DevMapUi.cs` | 无 | 小 | 写代码 |

**阶段 A 完成标志**：单玩家可以"生成地图 → 资源增长 → 建造 → 单位训练/移动/战斗 → 研究 → 事件触发"跑通。

### 阶段 B：核心数据与多玩家地基（P0/P1）

| 序 | 任务 | 涉及文件/模块 | 新增类/接口/字段 | 工作量 | 填表/写代码 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| B1 | 引入 `GameSession` / `PlayerContext`，收敛 `mapId/ownerId` | 全部 AppService + 组合根 | `GameSession`、`PlayerContext` | 大 | 写代码 |
| B2 | Map 常驻内存 + 脏标记 + 存档点写盘 | `MapAppService`、`IMapRepository`、`GodotMapRepository` | `MapSession`（或 `MapRuntime`） | 大 | 写代码 |
| B3 | 实体持久化：占据物/人口存档 DTO 与装载路径 | Map/Construction/Units 存档层 | `*SaveDto`（建筑/单位/格子状态）+ Mapper | 大 | 写代码（含数据层结构） |
| B4 | 统一存档基础设施（单序列化器、原子写、版本号、迁移钩子） | 5 个 `*Repository` | `ISaveStore`（+版本常量） | 中 | 写代码 |
| B5 | 任务体系升级：UId 键 + OwnerScope + 完成即回收 + 按 Scope 批量注销 | `ITickable`/`IProgressTask`/`TaskSnapshot`/`TaskRepository`/`GodotTimeService` | 任务主体引用类型、`IScopedTask` | 大 | 写代码 |
| B6 | 时间分层：`GameClock`（游戏时间/倍率/暂停）+ 纯 C# 推进器 | 新增 `GameClock`/`ITimeDriver` | 2 个类型 + 节拍配置 | 中 | 写代码 |
| B7 | 迷雾"服务/状态"分离（每玩家一份） | `FogAppService`、三个依赖它的 AppService | `IFogState`/`FogStateRegistry` | 中 | 写代码 |
| B8 | 修正器宿主化（Player/Entity/Cell/World + 阶段管道） | `IModifierRepository`/`ModifierManager`/`ModifierAppService` | 宿主键、阶段枚举 | 中 | 写代码（含配置字段） |

### 阶段 C：玩法正确性（P1）

| 序 | 任务 | 涉及文件/模块 | 新增类/接口/字段 | 工作量 | 填表/写代码 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| C1 | 领域事件机制（实体/科技/事件/战斗） | 新增 `IDomainEventBus` + 各模块发布点 | 1 个接口 + 事件类型 | 中 | 写代码 |
| C2 | 建造/训练/研究的统一"条件校验 + 结果返回"（含失败原因） | Construction/Units/TechTrees AppService | `OperationResult`、共享校验服务 | 中 | 写代码 |
| C3 | 能力组件化：`IActionHandler` 注册表取代 switch | Construction/Units AppService + 配置解析 | 1 个接口 + 若干 Handler | 中 | 写代码 |
| C4 | 建造完成逻辑收敛（首次与续跑共用同一方法） | `ConstructionAppService` | `CompleteConstruction(...)` | 小 | 写代码 |
| C5 | 单位行为状态机（Idle/MoveTo/Engage/Attack）+ 连续推进 | `UnitsAppService`（拆分为 `UnitMovementService`/`UnitCombatService`） | 状态机类型 | 大 | 写代码 |
| C6 | 单位/建筑数值全部走 Modifier（攻击、防御、移动） | `UnitsAppService` + `ModifierAppService` | 无（新读取路径） | 中 | 写代码 |
| C7 | 单位占用同步（格子槽位与坐标一致） | `Map` + `UnitsAppService` | 无（API 约束） | 中 | 写代码 |
| C8 | 科技：多资源成本 + 前置校验 + 完成事件 | `ITechNodeConfig`/`TechTreesAppService`/配置表 | `ResourceCost` 字段 | 中 | 写代码 + **填表** |
| C9 | `ModifierManager.GetValue` 修正（多 target 累加） | `ModifierManager.cs` | 无 | 小 | 写代码 |
| C10 | `IRandom` 签名统一、`MapCell` 槽位模型检讨 | `IRandom.cs`/`SystemRandom.cs`/`MapCell.cs` | 视设计稿决定 | 小~中 | 写代码 |

### 阶段 D：体验与性能（P2）

| 序 | 任务 | 涉及文件/模块 | 新增类/接口/字段 | 工作量 | 填表/写代码 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| D1 | 任务持久化改为"存档点 + 内存态"（去掉每帧写盘） | `GodotTimeService`/`TaskRepository` | 无 | 小 | 写代码 |
| D2 | 事件：UI 推送、历史记录、当前生效列表 | `EventAppService` + UI | 查询 API | 中 | 写代码 |
| D3 | 迷雾性能（半径模板缓存 + 增量更新 + 紧凑存档） | `FogAppService`/`FogRepository` | 无 | 中 | 写代码 |
| D4 | 人口系统集中化（单一增长系统 + 落盘 + 全局上限） | Construction/Map/存档层 | `PopulationState` + 增长服务 | 中 | 写代码 |
| D5 | 资源上限可增长（`AddLimit` 接入）、资源面板数据源 | `ResourcesAppService` + UI | 无 | 小 | 写代码 |
| D6 | `MapAppService` 拆分（查询/修改/寻路分离） | `MapAppService` | `MapQueryService`/`PathfindingService` | 中 | 写代码 |
| D7 | 迷雾模式可配置（永久记忆/回雾/重置） | `FogAppService` + 地图配置表 | 1 个配置字段 | 小 | 写代码 + 填表 |

### 阶段 E：工程与整洁（P3）

| 序 | 任务 | 涉及文件/模块 | 新增类/接口/字段 | 工作量 | 填表/写代码 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| E1 | 测试工程 + 内存实现（Map/Time/Config） | 新增测试项目 | 若干净实现 | 中 | 写代码 |
| E2 | 命名空间/目录对齐（TechTrees、Map 别名） | 多个文件 | 无 | 小 | 写代码 |
| E3 | 命名对齐（`IUnitsRepository` → `IUnitConfigRepository`） | Units 模块 | 无 | 小 | 写代码 |
| E4 | 删除空目录与失效 csproj 排除项；接线 `CameraController` | csproj/场景/空目录 | 无 | 小 | 写代码 |
| E5 | UML 与文档同步（PackageDiagram / BuildSequence / 本日志） | `UML/*`、`Document/*` | 无 | 小 | 文档 |
| E6 | `Map` 安全 API 化（消除索引器崩溃） | `Map.cs`/`MapAppService.cs` | 无 | 小 | 写代码 |

### 7.x 额外标注

**只需填表/改资产即可解决（不改代码）**：
- `A5` 事件表结构（`EVT-01`）——**必须是 P0 里最先做的一条，成本最低**
- `A6` 地形 Id 对齐与 `MoveCost`（`MAP-05`、`MAP-06`）
- 资源/建筑/单位/科技的**数值调整**（Modifier 目标名已约定，`ConfigTableGuide.txt:487-497`）
- 新增地形类型（`.tres` + 贴图）、新增资源条目、新增建筑/单位条目（只要不新增 Action）

**必须写代码（且可能改配置字段）**：
- 任何"新增能力/Action"（`C3`，当前要改 switch）
- 任何"新条件/新消耗类型"（`IRequirement`/`IConsumable` 实现 + 在 AppService 里组装）
- 任何"对象级/全局/格子级修正器"（`B8`、`MOD-03`）
- 任何"持久化"（`B2`、`B3`，含单位/建筑/人口）
- 任何"完成后联动/触发"（`C1`、`ROOT-4`）
- 科技多资源成本（`C8`：改 `ITechNodeConfig` 同时要改 JSON）

**必须动核心架构（不可绕过）**：
- `B1`、`B2`、`B5`、`B7` —— 这四项决定"多玩家 + 存档 + 长线运行"是否可能，且都触及 `MapAppService`/`GodotTimeService`/`FogAppService` 的签名与职责。

---

## 8. 待确认 / 待验证清单

### 8.1 尚未验证的技术假设（需在修复前实测）

| # | 待验证项 | 验证方式 | 关联 |
| :--- | :--- | :--- | :--- |
| V1 | Newtonsoft 能否按预期序列化/反序列化带 `[JsonProperty]` 的**私有字段**（`ResourcesPool._value/_limit`）与 `TechTree._nodes/_researchedIds` | 写最小测试（或直接跑一次存档往返） | `MAP-02`、`TIME-08` |
| V2 | 游戏实际运行时，"地形 MoveCost=0"到底表现为"不能走"还是"能走"（受视野判定分支影响） | 实机跑一遍寻路 | `MAP-06` |
| V3 | `GodotTimeService` 未挂场景时是否真的完全无 tick（确认没有别处创建） | 断点/日志 | `WIRE-02` |
| V4 | 事件 JSON 是裸数组时，Newtonsoft 反序列化的实际行为（抛异常 or 得到空对象） | 最小测试 | `EVT-01` |
| V5 | `GodotMapRepository` 的 `user://maps/` 在导出包中的可写性 | 导出测试 | `MAP-07` |
| V6 | `MapView` 在 `DevMapUi` 点击后是否确实抛 NRE（还是因为没有地图文件而先返回 null） | 实机 | `WIRE-05`、`MAP-03` |
| V7 | 地图生成 `VoronoiMapGenerator` 的权重/锚点密度是否与设计期望一致（`Density=4.0` 的含义） | 实机看产出 | 配置 |

### 8.2 需要设计/产品决策的问题（会直接改变路线图）

| # | 决策点 | 为什么重要 | 关联 |
| :--- | :--- | :--- | :--- |
| Q1 | **多玩家形态**：热座（同一设备轮流）/ 局域网 / 联机服务器？ | 决定是否需要确定性模拟、权威服务器、回放；决定 `GameSession` 的形态 | `ROOT-2`、`B1` |
| Q2 | 时间是否要"回合/游戏日"？（设计稿说"连续时间驱动"，但配置文档说"每天"） | 决定 `GameClock` 的设计与所有节拍配置 | `TIME-05`、`EVT-02` |
| Q3 | 配置表最终位置：`res://`（只读、随包）还是 `user://`（可热更、Mod 友好）？ | 决定 A3/A4 的实现 | `WIRE-03` |
| Q4 | 迷雾模式：永久记忆 / 离开即变雾 / 完全重置？ | 决定 `FogAppService` 的数据结构（是否需要时间戳） | `FOG-02` |
| Q5 | 是否需要"对象级 buff / 全局环境状态（天气等）"？ | 决定 `MOD-03` 是否必须做宿主化（工作量差异大） | `MOD-03`、`B8` |
| Q6 | 是否引入成熟 DI 容器，还是坚持手写组合根？ | 决定 `WIRE-01`/`DEP-02` 的实现方式 | `WIRE-01`、`WIRE-06` |
| Q7 | 是否接受"存档不兼容旧档"（还是需要迁移）？ | 决定存档基础设施是否必须带版本迁移 | `DEP-06`、`B4` |
| Q8 | 单位战斗是否要"逐格连续移动"（当前是 10 秒一格） | 决定 `C5` 是重写还是调参 | `UNIT-03` |

### 8.3 与【第二/三/四阶段】的衔接

### 8.3 与【第二/三/四阶段】的衔接 —— ✅ 已完成（v0.2）

- **第二阶段（设计稿解读）**✅ 已完成 → 见 **第 10 节**：5 份设计稿的规模、核心功能、涉及系统，以及经设计方确认的**权威规格**（时间模型 / 移动模型 R1~R7 / 战斗模型 / 经济模型 / 字段漏项 L1~L6 / 矛盾关闭 C1~C10）。
- **第三阶段（差距分析）**✅ 已完成 → 见 **第 11 节**：93 行判定（✅13 / ⚠️33 / ❌47），每个缺口的证据均引用第 3 节与第 12 节的问题 ID。
- **第四阶段（改造清单）**✅ 已完成 → 见 **第 13 节**：49 个工作包 / 6 个批次 + 三张专项清单（纯填表、必须新增类型、必须动核心签名）；配套 **第 14 节**（里程碑 M0/M1/M2 与可自动验证的退出条件）、**第 15 节**（配置表字段增量规格）、**第 16 节**（装配方案）、**第 17 节**（修订与统计）。

### 8.4 本文档的已知限制

1. **未运行游戏**：所有结论来自静态阅读与交叉检索；第 8.1 节的假设需实测确认。
2. **未读设计稿**：第 7 节优先级仅基于"工程阻塞程度"，尚未按"设计稿价值"排序。
3. **行号会漂移**：引用的是 commit `c6dda9a` 时的行号，改造后需重新核对。
4. **未评估美术/音频/本地化/多人网络同步**：超出本次诊断范围。

---

## 9. 变更记录

| 版本 | 日期 | 变更 |
| :--- | :--- | :--- |
| v0.1 | 2026-09-14 | 首次成文：基于 commit `c6dda9a` 的第一阶段架构诊断，收录 79 条问题（P0×8 / P1×29 / P2×35 / P3×7）、8 条公共根因、A~E 五阶段初步路线图、8 项待验证假设与 8 项待决策点。设计稿（第二~四阶段）尚未纳入。 |
| **v0.2** | 2026-09-14 | 纳入第二/三/四阶段：**设计规格锁定**（日为基础·30日/月·360日/年·三档流速·暂停；移动模型 R1~R7 与地形消耗量级 5/8/10/25/50；战斗同格+按日+建筑衰减 50%；经济月结+logistic 减员+单位维护默认值）、**93 行差距分析**、**49 个工作包 / 6 批次**、**M0/M1/M2 里程碑与验收标准**、**配置表字段增量规格**、**分层装配方案**（地形配置转 JSON）。新增 21 条问题（P0 8 / P1 9 / P2 4）、修订 14 条既有条目、关闭 4 项设计矛盾与 1 项废弃系统；统计 79 → **97 条**。 |
| **v0.3** | 2026-09-14 | **P0 收敛与依赖补全**：按「M0-1/M0-2 验收是否需要」重新判定 18 条 P0 → **保留 10 条、8 条降为 P1**（`WIRE-02`/`MAP-02`/`TIME-01`/`TIME-02`/`TIME-14`/`CON-09`/`MAP-16`/`UNIT-10`，理由见 §17.3）；新增 **§13.z**（49 个 WP 的级别 / 全部依赖边 / 大项风险与降级方案 / 依赖主干图 / 批次重排）；统计 P0 18→10、P1 35→43（总数仍 97）。 |
| v0.3.1 | 2026-09-14 | **M0-1 首批实现落地**：WP-0.1 清理、WP-0.2 验收载具（离线降级为可执行检查，12/12 通过、退出码 0）、WP-0.3 IO/配置抽象、WP-0.4 地形转 JSON（Config/Terrains.json）、WP-1.1 GameClock（逐日派发 / 三档 / 暂停 / 单帧上限）；dotnet build 通过。详见 §18。 |
| v0.3.2 | 2026-09-14 | **M0-1 续推**：WP-1.3 组合根（`CoreBootstrap`/`GameSession`/`CoreServices`/`CoreDependencies`）与 WP-3.1 Map 常驻内存（`MapSession`）落地；`MapAppService` 改走会话缓存（解除 M0-2 ③④ 的阻塞点）；无头验收从 12 项扩到 **20 项全部通过**。新增决策 D7~D10（含"导出需包含 `*.json`"的待办风险）。 |
| v0.3.3 | 2026-09-14 | **WP-1.4 完成（M0-1 ② 通关）**：7 张配置表统一为 `Config/{表名}.json` 并全部通电（`ConfigTables`）+ 启动期分级校验（`ConfigValidator`/`ConfigReport`，error 阻断 / warning 放行）；新增 `ModifierTargetRegistry`（Target 名登记表，由资源/单位表派生）。**校验器当场抓出两个真实缺陷**：`UnitsConfigDto` 缺 `[JsonProperty("Units")]` 导致整张单位表静默解析为 0 条；`Events` 表为裸数组（与 `EventsConfigDto` 根对象不符）→ 均修复。无头验收 **33/33 通过、退出码 0**，真实配置 **0 error / 0 warning**。新增决策 D11~D17。 |
| **v0.3.4** | 2026-09-14 | **WP-1.5 口径重标定（秒 → 游戏日）+ WP-1.2 `GodotTimeDriver`**：`Duration`/`GrowInterval`/`PopulationGrowthInterval` 全部改按**日**计（Resources 5/8/0 → 30 月结；Buildings 10/30/20 → 90/120/60、人口 15 → 300；Units 8/12/15 → 30/35/50，对齐设计 工人 30 日/民兵 35 日/弓箭手 50 日）；硬编码节拍收敛到 `Scripts/Core/Time/TimeConstants.cs`；新增纯 C# **`GameTimeService`**（订阅 `GameClock.DayElapsed`，逐日派发 `OnTick(1 日)`）替换从未入场景的 `GodotTimeService`；新增 `Scripts/Autoload/GodotTimeDriver.cs`（`Node, ITimeDriver`，由 `ServiceContainer` 自动挂载）；校验器新增**单位自检**（>360 日 → warning「疑似仍是秒口径」）。**顺带修复**：`ResourcesAppService` 的 `BaseGrowth>0` 门槛会让 `BaseGrowth=0` 的资源永不结算（Idea 的 250 idea/月 无处到账）→ 改为只看 `GrowInterval`。无头验收 **43/43 通过、退出码 0**（真实配置仍 0 error / 0 warning）。新增决策 D18~D22。 |
| **v0.3.5** | 2026-09-14 | **WP-2.1 跨树前置修复（`TECH-07`，M0-2 ①）**：`TechPrerequisite{TreeId,NodeId}` + JSON 兼容层（旧 `"nodeId"` 表不破）；`TechTree.CanResearch` 支持跨树（解析器由应用层注入，未注入 = fail closed）；`HydrateConfigs` 补入"存档后新增"的节点；`TechTreesAppService` 新增 `CanResearch` 并给 `Research` 补前置闸门（不再"先扣 Idea 再静默丢弃"）；`ConfigValidator` 把跨树前置缺树/缺节点与**跨树成环**判 error（全局 DFS）；`Config/TechTrees.json` 新增 `science/counting`（0 idea / 0 日）与最小 `physics` 树（`simple_machine_intuition` ← `science:counting`）；`ConfigTableGuide` 升 v1.2。无头验收 **49/49 通过、退出码 0**（真实配置 0 error / 1 条预期 warning）。新增决策 D23~D25 与 §18.4 回归看板 |
| **v0.3.6** | 2026-09-14 | **WP-2.7 建筑产出接线（M0-2 ②）**：建筑 Id 收敛为设计稿口径（营地 `camp` / 工坊 `workshop` / 学院 `school` / 军营 `military_camp`，旧的 `house`/`library`/`barracks` 废弃），数值取自设计稿「建造时间 / 效果」列（营地 90 日·人口上限 9·半径 1·间隔 300；工坊 90 日；学院 240 日·`IdeaGrowth` +250/月；军营 60 日）；`ConfigTableGuide` 升 v1.3。**顺带修**：`Map.GetBuildingInfo` 在空格子上抛 NRE（与 `GetOccupantInfo` 对齐后返回 null）。**新增断言**：建成学院后 6 个月恰好 +1500 idea、回收修正器即停，并把 `MAP-04`（建筑只写 `cell.Occupant` → 拆除对自建建筑失效）固定成断言防止悄悄改动。无头验收 **52/52 通过、退出码 0**。新增决策 D26~D28 |
| **v0.3.7** | 2026-09-14 | **WP-2.8 事件按日掷骰 + 字段改名（M0-2 ⑤）**：`TriggerChance` → `TriggerChancePerDay`（口径 = %/日；旧名不做静默迁移，由校验器提示改名）；事件引擎改为 `ActiveEvent` 逐日倒计时（触发日起算、0=永久）+ `GetActiveEvents`/`GetTriggerCounts`/`RollCount`，**生效中不重复触发**（`G8`）、`StartEventsEngine` 幂等（`EVT-03`）；`ConfigTableGuide` Events 段同步。无头验收 **58/58 通过、退出码 0**（固定种子 3 年触发次数 gold_rush=2 / plague=2，两次运行一致）。新增决策 D29~D30 |


---

> 第 10~17 节为第二/三/四阶段产出（v0.2）。本节原有的维护约定已迁移至全文末尾。


---

# 10. 第二阶段：设计稿解读与设计规格锁定

> 来源：`design/` 下 5 份设计稿（与仓库内容逐字校验一致）。**10.3 的条目是与设计方确认后的权威口径，后续实现以此为准。**

## 10.1 设计稿清单与规模

| 设计稿 | 规模 | 分类统计 |
| :--- | :--- | :--- |
| `design/buildings.md` | **24 个建筑**（13 列表格） | 区域建筑 3 · 附属建筑 1 · 标准建筑 2 · 生产建筑 9 · 住房建筑 3 · 军事建筑 3 · 存储建筑 3 |
| `design/events.md` | **20 个事件**（10 列表格） | 永久效果 6 · 带消耗 8 · 触发概率全为 `%/日` · 含科技前置 11 |
| `design/research_tree.md` | **93 个节点**（8 列表格） | 数学 30 · 物理 35 · 化学 28；前置为"无"仅 2 个；多前置 46 个；跨树引用 33 处（物理→数学 23、化学→数学 10） |
| `design/resources.md` | 3 种资源池 | Idea 500/10000 · Food 300/2000 · BasicMinerals 200/1500 |
| `design/unit.md` | **15 个单位** | 玩家 10（工人/探险者/民兵/长矛兵/弓箭手/重装卫士/工程师/弩炮/学者/化学家）· 敌方 5（野狼/野猪/山鹰/巨角山羊/巨鳄） |

## 10.2 各设计稿核心功能与涉及系统

| 设计稿 | 核心功能 | 涉及系统 |
| :--- | :--- | :--- |
| buildings | 三类建筑（区域/生产/基础设施）多级升级；**只能由建造者单位建造**；住房与军事建筑有 HP 且可被夺取；建筑产出资源、提供人口与存储上限、可训练单位、有视野与范围光环 | 地图/地块 · 建造 · 单位 · 资源 · 科技 · 数值修正 · 人口 · 迷雾 · 时间 · 战斗 |
| events | 20 个历史题材随机事件；**触发时时间暂停**、玩家决策后继续；效果为产量/建造速度/人口增长修正，含 6 个永久效果 | 事件 · 时间（暂停） · 资源 · 科技 · 修正 · 任务速率 · 人口 · UI |
| research_tree | 3 棵独立科技树 93 节点；树内串行、三树并行；效果含数值修正、解锁建筑/升级/UI/地形通行 | 科技 · 资源 · 修正 · 建筑解锁与升级 · 单位解锁 · 地形通行 · UI 门控 |
| resources | 3 种资源；来源 = 建筑产出 + 事件 + 科技；Food 有人口消耗与赤字减员；上限可由仓库提升 | 资源 · 人口 · 建筑产出 · 事件 · 科技 · 修正 · 时间（月/年） |
| unit | 10 玩家单位（由建筑训练）+ 5 敌方单位（地图生成、封锁地块、掉落资源）；移动力累积；同格持续战斗；可合并；可驻扎 | 单位 · 地图/地形 · 建造 · 战斗 · 人口 · 资源 · 修正 · 迷雾 · 地图生成 |

## 10.3 设计规格锁定（权威口径）

### 10.3.1 时间模型

| 项 | 口径 |
| :--- | :--- |
| 基础单位 | **游戏日**（连续递增，从 0 起）；字段 `CurrentDay` |
| 换算 | 30 日 = 1 月；12 月 = 1 年 = **360 日** |
| 流速档位 | 标准 **1 日/真实秒** ｜ 第二档 **3 日/秒** ｜ 第三档 **6 日/秒** ｜ 暂停 = 0 |
| 节拍 | `GameClock` 派发**日边界事件**；所有周期以"每 N 游戏日"表达：MP 回复 10 日、经济结算 30 日、减员评估 360 日 |
| 唯一性 | 一帧若跨多个日边界必须**逐日派发**（保证"每日掷骰/每 10 日回复"不漏算） |
| "秒"字样 | 设计稿中出现的"秒"一律等同"日"（设计方确认的不严谨写法） |
| 显示 | 第 0 日显示为 `0年1月1日` |

### 10.3.2 移动模型（R1~R7，已确认）

| # | 规则 |
| :--- | :--- |
| R1 | 单位"移动力 M"（工人10/探险者40/民兵25/长矛兵20/弓箭手25/重装15/工程师20/弩炮5/学者20/化学家20）= **每 10 游戏日回复的 MP 总量** |
| R2 | 回复是**离散**的：每满 10 游戏日 `MP += M` |
| R3 | 静止（无移动指令）时 **MP 上限 = M**（挂机不可超过一个回复周期） |
| R4 | 移动中（已下达最终目的地且未到达）**MP 无上限**，可跨多个周期累积 |
| R5 | MP ≥ 下一格地形消耗 → **立即位移，不消耗游戏时间**；若仍够下一格则同一时刻连续通过多格 |
| R6 | 到达**最终目的地**时 MP 清零（剩余作废）；未到达则结转 |
| R7 | 目的地可随时更改；"正在通过的一格不可取消"在"瞬时位移"模型下**自动满足**，无需额外实现 |

**数值自洽性验证**：10 移动力单位跨 **25 消耗**地块 —— 第 10 日 MP=10、第 20 日 MP=20、**第 30 日 MP=30 ≥ 25 → 位移，余 5 因到达终点作废** → 与设计示例"需要等待 30 天"完全一致。

**地形移动消耗**（本阶段采用的新量级，取代旧的 `Document/ConfigTableGuide.txt:478-484` 示例表）：

| 地形 | 平原 | 沙漠 | 森林 | 山地 | 水域 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| MoveCost | 5 | 8 | 10 | **25** | 50（需"浮力定律"解锁） |
| 是否默认可通行 | 是 | 是 | 是 | 是（代价高） | 否 |

### 10.3.3 战斗模型

| 项 | 口径 |
| :--- | :--- |
| 交战 | 敌我单位处于**同一地块**内交战；每格只能有 1 个单位或 1 对交战单位 |
| 结算 | 按**游戏日**结算（`ATK` 即"每日伤害"，节拍 1 日） |
| 攻击范围 | `0` = 近战（须相邻）；`≥1` = 隔格攻击（**近战/远程由该字段派生**，无需额外标签） |
| 对建筑伤害 | 统一衰减 **50%**，加伤**乘在衰减之后**（弩炮 0.5 × 1.5 = 0.75）；预留按单位单独调整数值的空间 |
| 合并 | 同模板单位、HP 之和 ≤ 上限（**生命值列 = 上限**、初始满血）、移动到同格 → 合并 |
| 死亡 | HP 归零移除、不返还资源、驻扎加成消失、掉落资源直接入池 |

### 10.3.4 经济模型

| 项 | 口径 |
| :--- | :--- |
| 结算周期 | **每月（30 游戏日）**；首次结算发生在第 30 日 |
| 产量表达 | Modifier `Absolute` = +N/月；`Percent` = ×(1+p)；`base` 即"每月基础产量"（**与现有 `ModifierManager` 公式天然对齐**） |
| 产出浮动 | 默认 ±20%（区间可被修正，如观星台把农田改为上限 +5%/下限 −1%） |
| 人口消耗 | 人口 × 3 Food/月 |
| 单位维护 | 默认值（Food/月）：工人 1 · 探险者 2 · 民兵 2 · 长矛兵 3 · 弓箭手 3 · 工程师 2 · 弩炮 4 · 学者 2 · 化学家 2 · 重装卫士 5 |
| 赤字减员 | 连续赤字 ≥ **36 月（3 年 = 1080 日）** → 年评估：`p = 1/(1+e^(−k(r−0.5)))`，期望减员 = 总人口 × p × 0.05，实际值随机抖动（k=8、系数 0.05，均配置化） |
| 溢处理 | 超出存储上限的资源丢弃（沿用现有 `ResourcesPool.AddValue` 截断） |

## 10.4 设计稿字段漏项（表格缺列，已确认补法）

| # | 漏项 | 引用来源 | 处理（已确认） |
| :--- | :--- | :--- | :--- |
| L1 | **视野半径**：`unit.md` 与 `buildings.md` 两张表均无此列 | unit 正文"所有单位驱散两格迷雾"；buildings 正文"建筑两格内不再有迷雾"；科技 5 处"视野 +1"；探险者"视野 +1" | 两张表补"视野半径"列：默认 **2**；特殊单位在此基础上加减（探险者 3）；科技修正 +1 |
| L2 | **单位维护费**：正文有、表格无 | unit 正文"维护：每月消耗的食物（若启用）" | 补列 + 采用 10.3.4 默认值 |
| L3 | **兵种标签/分类**：正文有"兵种克制"，但表格"分类"列只有"单位/敌方单位" | unit 正文 3 处 | 补 `Tags`（近战/远程/攻城/学者/侦察…）；**近战/远程由攻击范围派生**，仅在需要额外标签时填写 |
| L4 | `resources.md` 引用的"巧合中的研讨会"事件在 events 表不存在 | resources「Ideas → 联动」段 | 设计方确认仅为示例 → **忽略** |
| L5 | 建筑表无"是否可被夺取"标记 | buildings 正文"只有住房设施和军事建筑具有生命值" | 补 `HasHP`（住房/军事 = true）与 `IsVictoryCritical` |
| L6 | **地形移动消耗表**缺失 | 全文仅"解锁水域通行（移动消耗 5.0）"一处 | 采用 10.3.2 的量级表（平原5/沙漠8/森林10/山地25/水域50） |

## 10.5 设计矛盾关闭清单（已确认）

| # | 原矛盾 | 结论 |
| :--- | :--- | :--- |
| C1 | 视野口径（buildings 2 格 / unit 2 格 / 现有配置 3·4·5） | 以"表格 + 默认 2 格"为准，正文中的旧数值作废 |
| C2 | "攻城器械对城墙加成"但全项目无城墙 | **城墙废弃**；统一为"对建筑伤害加成"（弩炮已写"对建筑伤害 +50%"） |
| C3 | 文明级指标（社会稳定度、社会繁荣度） | **已废弃**，相关设计删除（`RES-01` 范围相应缩小） |
| C4 | 事件"持续/瞬时"分类 | **已废弃**（表格"分类"列可删） |
| C5 | 产量口径混用（+N/月、+%、+N/年） | 统一为"**每月**结算"：绝对量 = +N/月，比例 = ×(1+p)，年产量先折算为月（144/年 → 12/月） |
| C6 | 移动力量级与地形消耗量级 | 已确认采用 10.3.2 的新量级 |
| C7 | "连续三年食物不足"的判定 | 实现为"连续赤字 ≥ 36 月（1080 日）" |
| C8 | 矿石分级（4 级共享存储上限） | **暂不实现**，仅在资源配置预留 `ShareLimitGroup` 字段 |
| C9 | 训练/建造/升级/科技消耗的口径 | 统一为 `ResourceCost` 多元结构（科技当前只消耗 idea，数值不变） |
| C10 | 解锁（Unlock）与前置（Requirement）的关系 | **两者等价**，仅 UI 表现不同 → 不需要独立解锁状态，改为**前置的派生查询** |

## 10.6 跨文档 12 项共同需求（N1~N12）与当前状态

| # | 共同需求 | 状态（v0.2） |
| :--- | :--- | :--- |
| N1 | 游戏时间（日/月/年）成为一等概念 | 缺口 → `TIME-05`（升 P0）、`TIME-12`、`TIME-13` |
| N2 | 内容解锁（科技解锁建筑/升级/UI/地形通行） | **降级**：解锁 = 前置（派生查询）→ `DEP-11`（P2） |
| N3 | 产出归因（按来源列出增减项） | **降级**：`ModifierValue.SourceId` 已具备，仅缺查询 API → `MOD-07`（P2） |
| N4 | 空间作用域效果（半径内建筑/相邻同类型/驻扎） | 缺口 → `MOD-03`、`UNIT-13` |
| N5 | 修正作用面扩展（时长/上限/消耗/伤害/视野） | "产量"部分**已对齐**（月结 + 现有公式）；其余 → `TIME-11`、`RES-02` |
| N6 | 任务进度受修正（建造速度/训练速度） | 缺口 → `TIME-11` |
| N7 | 单位角色/状态（驻扎/建造中/训练队列/合并/克制/维护） | 缺口 → `UNIT-11`、`UNIT-12`、`UNIT-13`、`UNIT-18`、`UNIT-19` |
| N8 | 建筑层级与所有权变更（区域+附属嵌套/HP/易主） | 缺口 → `MAP-12`（含原 `MAP-15`）、`MAP-16` |
| N9 | 敌方单位与地图生成联动 + 地块封锁 | 缺口 → `UNIT-14` |
| N10 | 玩家决策流程（事件暂停 + 选择） | 缺口 → `EVT-06` |
| N11 | 文明级指标 | **已废弃**（见 C3） |
| N12 | 人口系统深化（人口转化/食物消耗/赤字减员/住房上限） | 缺口 → `RES-01`、`CON-05`（升 P1） |

---

# 11. 第三阶段：差距分析（93 行判定：✅13 / ⚠️33 / ❌47）

> 判定：`✅` 直接支持 ｜ `⚠️` 部分支持 ｜ `❌` 不支持。缺口类型：`D`数据层 / `L`逻辑层 / `W`装配层 / `A`架构层。
> 行号（`A1`/`B10`…）用于第四阶段改造清单的"覆盖"列引用。

## A. 时间与节拍（9 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| A1 | 连续时间、0 起递增、显示 x年x月x日（30日/月·360日/年） | ❌ | A：只有"秒"，无游戏时间概念；`GodotTimeService.cs:44` 用 `delta*Scale` | ✘ | 新建 `GameClock`+`GameTime`，`ITimeService` 改按日推进 — **中** |
| A2 | 三档流速 1/3/6 日每真实秒 + 暂停 | ⚠️ | A：`Scale` 存在但无人设置（`GodotTimeService.cs:10`；`WIRE-02`） | ✔（扩 `Scale` 语义） | `GameClock` 承载 `SpeedTier/Paused`；UI 三档切换 — **小** |
| A3 | 一帧跨多日时逐日派发（每日掷骰/每 10 日回复不漏算） | ❌ | A：无"日边界事件"概念 | ✘ | `GameClock` 内逐日派发 — **小** |
| A4 | 所有时长/节拍以"日"计（建造 30~300、研究 0~180、事件 5~30、MP 10、人口 180~300、经济 30） | ⚠️ | D：`Duration`/`GrowInterval` 现为秒；硬编码 `1f`/`10f`（`EventAppService.cs:39`、`UnitsAppService.cs:145,296`） | ✔（只填表 + 常量集中） | 配置改"日" + 节拍常量进时间配置 — **小~中** ｜ **✅ v0.3.4 / WP-1.5 已落地**（`TimeConstants` 单点定义 + 校验器单位自检） |
| A5 | 任务进度受修正（建造速度 ±50%/30%、训练速度 +20%/50%） | ❌ | L：`LinearTask.OnTick` 直接 `Progress += delta`（`LinearTask.cs:30-36`） | ✘ | 任务 tick 时按 `Type` 查询修正并加权 — **中** |
| A6 | 事件触发时**暂停**，等玩家确认 | ❌ | L+W：`ITimeService` 无暂停（`ITimeService.cs:10-15`）；事件无交互入口（`EventAppService.cs:44-73`） | ✘ | `GameClock.Paused` + 事件挂起状态 + 面板 — **中** |
| A7 | 任务完成即回收 / 按实体注销 | ❌ | L：`IntervalTask` 永不注销（`IntervalTask.cs:46-57`；`TIME-02/09`）；无作用域 | ✘ | 任务加 UId 键 + OwnerScope + 自动回收 — **大** |
| A8 | 任务持久化走存档点（不每帧写盘） | ❌ | A：每帧每任务写 JSON（`GodotTimeService.cs:51-55`→`TaskRepository.cs:19-23`） | ✔（改内存态） | 剥离 tick 内持久化，纳入统一存档 — **中** |
| A9 | 游戏时间进存档/读档恢复 | ❌ | D：无字段 | ✔ | 存档加 `CurrentDay` + 档位 — **小** |

## B. 地图与地块（12 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| B1 | 地形 5 类与消耗（平原5/沙漠8/森林10/山地25/水域50） | ⚠️ | D：`.tres` 无 `MoveCost` 且 Id=test1..5（`MAP-05/06`） | ✔（只填资产/表） | **地形配置转 JSON** + 补齐 5 类 — **小** |
| B2 | 山地/未解锁水域不可通行；浮力定律解锁水域 5.0 | ❌ | L：通行只读静态 `MoveCost`（`MapAppService.cs:151-156`、`UnitsAppService.cs:252-259`） | ✘ | 通行判定接入"每玩家解锁集合" — **中** |
| B3 | 每格 1 单位或 1 对交战单位；占用权威一致 | ⚠️ | L：三槽位模型（`MapCell.cs:11-15`）+ 移动不同步槽位（`UNIT-05`） | ✘ | `Map` 统一"进入/离开"API + 占用校验 — **中** |
| B4 | 区域建筑 + 附属建筑嵌套 | ❌ | A：单槽位、无父子关系（`MAP-12`） | ✘ | `MapCell` 改建筑集合 + `Building.HostUId` — **大** |
| B5 | 建筑 2 格内**永久清除**迷雾 | ⚠️ | L：`RevealArea` 只置 `Visible`、`ResetArea` 只降级（`FogAppService.cs:56-97`） | ✔（加语义） | 迷雾状态加"永久可见"标记 — **小~中** |
| B6 | 单位视野 2 格（探险者 3；科技 +1 共 5 处） | ⚠️ | D：两张表都无视野列（L1）；现配置 3/4/5 需重填 | ✔ | 补视野列 + `VisionRadius` 修正 target — **小** |
| B7 | 敌方单位按地形概率在地图生成时放置 | ❌ | L：生成器只产地形（`IMapGenerator.cs:5`、`VoronoiMapGenerator.cs:25-37`） | ✘ | 新增生成后处理 `EnemySpawner` — **中** |
| B8 | 敌方存活封锁地块（不可进入/建造/采集） | ❌ | L：`IsClear` 只判 `Occupant`（`MapAppService.cs:38-42`） | ✔（新增 `IRequirement`） | `NoHostileRequirement` + 建造校验接入 — **小** |
| B9 | 记忆式迷雾 + 仅视野内可见敌方 | ✅ | `FogAppService.cs:62-64,93-95` 已是记忆式 | ✔ | 读取时按 `GetVisibility` 过滤 — **小** |
| B10 | 建筑/单位/人口/任务的完整存档 | ❌ | A+D：只存 `position+terrain`（`HexCubeCellSave.cs:7-8`、`GodotMapRepository.cs:63-69`）；实体不可序列化（`MAP-02`） | ✘ | 实体存档 DTO + Mapper + 统一存档单元 — **大** |
| B11 | Map 常驻内存（不再每操作读盘） | ❌ | A：每方法 Load/Save（`MAP-01/07`） | ✘ | `MapSession` + 脏标记 + 存档点 — **大** |
| B12 | 建筑易主（住房 HP 归零 → 成为敌方建筑） | ❌ | L：无 HP、无 Owner 变更、无归属迁移（`MAP-16`） | ✘ | 新用例 + 修正器/迷雾/人口迁移 — **大** |

## C. 经济与资源（12 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| C1 | 3 资源初始值/上限（500·10000 / 300·2000 / 200·1500） | ✅ | 只填表（`ResourcesPool.cs:25-36`） | ✔ | 重写 `ResourcesConfig.json`（Id 规范化） — **小** |
| C2 | 每月（30 日）结算 | ⚠️ | D：`GrowInterval` 现为秒（5/8） | ✔（只填表） | 统一填 30 日 — **小** ｜ **✅ v0.3.4 / WP-1.5 已落地**（三资源均 `GrowInterval=30`，1080 日结算 36 次） |
| C3 | 建筑产出（School 250/月、矿场 100/月、农田 12/月、日晷 150/月） | ⚠️ | L：无"建筑产出"概念，但**可用 Modifier Absolute 表达**；挂载点已存在（`ConstructionAppService.cs:84`，拆除 `:119`） | ✔ | 建筑 `Modifiers` 填 `{Target:"IdeaGrowth",Absolute,250}` — **小（数据）** |
| C4 | 产出归因 / "查看资源加减项"面板 | ⚠️ | L：`ModifierValue.SourceId` 已有（`ModifierValue.cs:13`），**缺查询 API**（`MOD-07`） | ✔ | `ModifierAppService` 加"按 target 汇总来源"查询 — **中** |
| C5 | 上限提升（仓库 +500/1000/2000；科技 +10%/+200） | ⚠️ | L：`AddLimit` 存在但**无调用**（`ResourcesPool.cs:56-62`） | ✔ | 接线上限消费点（读 `ResourceLimit`） — **小** |
| C6 | 产出浮动 ±20%（观星台改写上限 +5%/下限 −1%） | ❌ | L：结算无随机（`ResourcesAppService.cs:69-86`） | ✘ | 加 `ProductionVariance` 字段 + 2 个修正 target + 用 `IRandom` — **中** |
| C7 | 人口食物消耗（人口 ×3/月） | ❌ | L：无"需求"概念 | ✘ | 月度结算器（需求/赤字） — **中** |
| C8 | 单位维护费（1~5 Food/月） | ❌ | D+L：单位表无维护列（L2）；无逻辑 | ✘ | 配置加 `Maintenance` + 月度结算扣费 — **中** |
| C9 | 赤字连续 3 年 → 按缺口 logistic 随机减员 | ❌ | L：无 | ✘ | 赤字记录 + 年评估 + 按地块随机减员 — **中~大** |
| C10 | 上限截断（溢出丢弃） | ✅ | `ResourcesPool.cs:43-49` | ✔ | 无需改动 |
| C11 | 科技/事件的绝对与比例产量加成 | ✅ | 公式已是 `(base+abs)*(1+per)`（`ModifierManager.cs:54`） | ✔（需修 `MOD-01`） | 修多 target 累加 — **小** |
| C12 | 第 30 日首次结算 | ✅ | `IntervalTask` 从 0 计时（`IntervalTask.cs:46-56`） | ✔ | 无需改动 |

## D. 建筑与建造（15 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| D1 | 24 建筑 + 7 分类 + 前置/晋级/可建地块等 13 列 | ⚠️ | D：`IBuildingConfig` 无 `Category`/`Upgrade*`/`TrainableUnits`（`IBuildingConfig.cs:10-25`） | ✔（加字段） | 扩接口与 DTO + 重写配置表 — **中** |
| D2 | 建造消耗（石材）+ 多元升级消耗（13 条） | ✅ | 多资源已支持（`ConstructionAppService.cs:49-56`） | ✔ | 只填表 |
| D3 | 建造时间 30~300 日 | ⚠️ | D：`Duration` 为秒口径 | ✔ | 填"日"值 — **小** |
| D4 | 建筑升级 lv.I→II→III（消耗+时间+条件） | ❌ | L：无升级用例（`CON-09`） | ✘ | 新增 `CanUpgrade` 动作 + `UpgradeTask`（复用完成路径） — **中~大** |
| D5 | 升级条件 = 已解锁科技节点 | ⚠️ | L：`TechRequirement` 可用但依赖跨树修复（`TECH-07`） | ✔ | 用 `TechRequirements` 表达 — **小** |
| D6 | 建筑必须由建造者单位建造；每建筑同时仅 1 建造者 | ❌ | L：`StartConstruction` 不校验建造者（`ConstructionAppService.cs:45-95`）；`Build` 只要求同格（`UnitsAppService.cs:128-133`） | ✘ | 建造请求带单位 uid + 校验能力/位置/占用 — **中** |
| D7 | 仅住房+军事有 HP；HP 归零被夺取 | ❌ | L：`Building` 无 HP（`Building.cs:16-24`）；`MapOccupantInfo.HP` 可用 | ✘ | `Building` 加 `HP/MaxHP` + 夺取用例 — **大** |
| D8 | 建筑可修复（工程师） | ❌ | L：无 | ✘ | 新增 `CanRepair` 能力组件 — **中** |
| D9 | 建筑可拆除 | ✅ | `ConstructionAppService.cs:110-121` | ✔ | 补：注销其名下任务（`CON-06`） — **小** |
| D10 | 建筑 `Actions` 标注可训练单位、训练校验建筑等级 | ❌ | D+L：`Actions` 现为"能力"字符串；`CreateUnit` 不涉及建筑（`UNIT-11`） | ✘ | 配置加 `TrainableUnits` + 训练带建筑 uid — **中** |
| D11 | 住房提供人口容量/增长间隔（9/半径1/300日） | ⚠️ | L：已有 `RegisterHousingTask`（`ConstructionAppService.cs:139-152`），但 `OwnerId=0`、不注销、人口不落盘（`CON-04/05`） | ✔（修 bug） | 修传参 + 注销 + 人口存档 — **中** |
| D12 | 仓库提升石材上限 | ⚠️ | 同 C5 | ✔ | 填 `ResourceLimit` 修正 — **小（数据）** |
| D13 | **范围修正**（观星台 3 格农田浮动；骨笛工坊 1 格生产建筑 +15%；振动与波相邻同类型 +5%） | ❌ | A：Modifier 作用域仅玩家级（`IModifierRepository.cs:7-8`；`MOD-03`） | ✘ | 修正器宿主化（Entity/Cell/World）+ 目标标签筛选 — **大** |
| D14 | 建筑视野 2 格（升级后变化） | ⚠️ | L：完成时 `RevealArea`（`:85`）但**续跑丢失**（`CON-03`）；无视野列 | ✔ | 补视野列 + 完成逻辑收敛为单一方法 — **小~中** |
| D15 | 附属建筑随区域建筑升级保留 | ❌ | 依赖 B4 | ✘ | 随 B4 一并设计 — **大** |

## E. 单位与战斗（21 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| E1 | 10 玩家单位 + 5 敌方单位及其全部属性 | ⚠️ | D：现配置仅 3 个原型单位；缺 Tags/维护/视野/影响范围/特殊能力列 | ✔（加字段） | 扩 `IUnitConfig` + 重写表 — **中** |
| E2 | 训练**结束后**人口 −1 | ⚠️ | L：现在训练**前**扣（`UnitsAppService.cs:62-75`） | ✔ | 人口扣除移入完成回调 — **小** |
| E3 | 训练队列（上限 5、同时 1 个） | ❌ | L：任务 Id=`UnitId` 互相覆盖（`TIME-03`） | ✘ | 建筑训练队列组件 + 任务键改 UId — **中** |
| E4 | 单位出现在建筑格或相邻格 | ⚠️ | L：现直接放在指定格（`:81`） | ✔ | 完成时寻找可用格 — **小** |
| E5 | **移动力离散累积模型**（R1~R7） | ❌ | L：MP 上限被压成单次恢复量（`UnitsAppService.cs:158`；`UNIT-10`） | ✘ | 重写移动 tick — **中** |
| E6 | 移动不消耗游戏时间 | ❌ | L：同 E5 | ✘ | 同 E5 — **中** |
| E7 | 目的地可改；已通过格不可撤（瞬时位移下自然成立） | ⚠️ | L：每次 tick 重算路径（`:161-166`） | ✔ | 目的地变更时重算 — **小** |
| E8 | 不可通行地形（山地/未解锁水域） | ⚠️ | L：只看 `MoveCost>0`（`:252-259`） | ✔（修 `MAP-06` 后） | 随 B1/B2 — **小** |
| E9 | **同格战斗**（双方处于同一地块，一格一对） | ❌ | L：现为邻格/射程攻击、不进敌格（`UNIT-12`） | ✘ | 新增交战状态：进入敌格即交战 — **大** |
| E10 | 战斗按**日**结算（ATK=每日伤害） | ⚠️ | L：攻击 1 秒 tick（`:296`） | ✔ | 节拍改 1 日 — **小** |
| E11 | 攻击范围 0=近战相邻、≥1=隔格 | ✅ | `:275-283,310-321` | ✔ | 保留（"近战/远程"由该字段派生） |
| E12 | 对建筑伤害衰减 50%，加伤乘其后（0.5×1.5=0.75） | ❌ | L：无目标类型与系数（`:326`） | ✘ | 伤害计算加"目标类型系数" — **小~中** |
| E13 | 单位标签 + 条件化修正（长矛兵对近战 +20%、重装 −25%、弩炮对建筑 +50%） | ❌ | D+L：无标签、无防御侧修正（`UNIT-18`） | ✘ | `Tags` + 条件修正求值器 — **中** |
| E14 | 弓箭手优先攻击远程 | ❌ | L：无索敌策略 | ✘ | 目标选择加优先级 — **中** |
| E15 | 单位合并（同模板、HP 之和≤上限、同格） | ❌ | L：无 `MaxHP`、无合并（`UNIT-17`） | ✘ | `Unit` 加 `MaxHP` + 合并用例 — **中** |
| E16 | 驻扎建筑提供产出加成（学者/化学家，影响 1 格） | ❌ | L：无驻扎（`UNIT-13`） | ✘ | 单位-建筑绑定 + 驻扎修正 — **中~大** |
| E17 | 工程师修复建筑 | ❌ | L：无 | ✘ | 能力组件 — **中** |
| E18 | 攻击目标扩展到建筑 | ⚠️ | L：攻击只接受 `Unit` 目标（`:272,306`） | ✔ | 目标抽象为占据物 + 建筑 HP — **中** |
| E19 | 敌方：不移动、进入其攻击范围即受伤 | ⚠️ | L：现为"发现敌人即停步"（`UNIT-06`） | ✘ | 重写索敌/受击 — **中** |
| E20 | 敌方死亡掉落资源入池 | ⚠️ | L：无掉落、无死亡事件（`UNIT-08`） | ✔（新增事件） | 配置加 `Loot` + 死亡处理 — **小~中** |
| E21 | 单位死亡：移除/不返还/驻扎加成消失 | ⚠️ | L：移除已有（`:325-332`），缺驻扎清理与事件 | ✔ | 补齐清理 — **中** |

## F. 科技（11 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| F1 | 3 树 93 节点；树内串行、三树并行、未来可配并发 | ⚠️ | L：**无并发限制**，可无限并行（`TechTreesAppService.cs:50-80`） | ✘ | 按树研究队列 + 并发上限配置 — **中** |
| F2 | **跨树前置**（46 多前置、33 处跨树） | ❌ | L：`CanResearch` 只查本树集合 → **物理树 35 节点永久不可解锁**（`TECH-07`） | ✘ | 前置结构改 `{treeId,nodeId}` + 跨树查询 — **中**（P0 最急） |
| F3 | 消耗 idea（0~8000），含 0 消耗 0 时长节点 | ✅ | `Cost` + `LinearTask(Target=0)`（`:54-64`） | ✔ | 只填表 |
| F4 | 研究时间 0~180 日 | ⚠️ | D：秒口径 | ✔ | 填日值 — **小** |
| F5 | 效果=数值修正（产量/上限/消耗/时长/视野/伤害 六类） | ⚠️ | L：唯一消费点是产量（`ResourcesAppService.cs:71-73`）；其余五类无消费点 | ✔（逐类接线） | 上限→`AddLimit`；时长→任务 Target 修正；视野→视野读取；伤害→战斗求值 — **中~大** |
| F6 | 效果=解锁建筑/建筑升级（=前置） | ✅ | `TechRequirement.cs:18-21` | ✔ | 只填表 |
| F7 | 效果=解锁 UI 面板/研究功能 | ❌ | W：无 UI 门控机制 | ✔（派生查询） | 表现层按 `IsResearched` 门控 — **中（表现层）** |
| F8 | 效果=解锁水域通行 5.0 | ❌ | 同 B2 | ✘ | 通行权限 + 科技解锁 — **中** |
| F9 | 研究是 School 的操作（按建筑等级） | ⚠️ | L：`CanResearch` 已存在（`ConstructionAppService.cs:123-137`）但不校验等级/不绑定建筑 | ✔ | 校验建筑等级 — **小~中** |
| F10 | 科技完成推送（提示/联动） | ❌ | L：无事件（`TECH-05`） | ✘ | 领域事件 — **中** |
| F11 | 研究进度持久化/续跑 | ❌ | L：`nodeId` 被当 `treeId`（`TECH-01`）→ 读档错乱 | ✔ | 修 + 快照加"树/节点" — **小~中** |

## G. 事件（8 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| G1 | 20 事件、%/日 触发、5~30 日或永久 | ⚠️ | D+L：JSON 是裸数组（`EVT-01`）；实现是 1 秒判定（`EVT-02`） | ✔ | 重写表 + 按日掷骰 — **小~中** |
| G2 | 触发消耗资源（8 条），不足不触发 | ✅ | `EventAppService.cs:75-108` | ✔ | 只填表 |
| G3 | 前置=已研究科技（11 条，含跨树） | ✅ | `IEventConfig.cs:15`（按 treeId 分组，天然支持跨树） | ✔ | 只填表 |
| G4 | 效果=产量修正（含 +N/月 绝对量） | ✅ | `ModifierManager.cs:54` | ✔ | 只填表（target 名统一） |
| G5 | 效果=建造速度 −50%/+30%、人口增长 −20% | ❌ | L：任务进度无修正（`TIME-11`）、人口节拍取自建筑字段 | ✘ | 任务进度乘修正 + 人口节拍乘修正 — **中** |
| G6 | 时间暂停 + 玩家决策 | ❌ | 同 A6（`EVT-06`） | ✘ | `GameClock.Paused` + 挂起/选择 UI — **中** |
| G7 | 永久事件（6 条） | ⚠️ | L：`Duration=0` 已表示永久，但无撤销/查询（`MOD-07`） | ✔ | 加"生效中事件"查询 — **小** |
| G8 | 重复触发不失控 | ❌ | L：无条件 `Add`（`ModifierManager.cs:13-20`） | ✔ | 同 SourceId 覆盖式添加 — **小** |

## H. 迷雾（1 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| H1 | 迷雾性能（每格移动刷新）与紧凑存档 | ⚠️ | L：半径每次重算（`FOG-03`）、全量序列化（`FOG-04`） | ✔ | 半径模板缓存 + 增量 + 紧凑表示 — **中** |

## I. 装配与工程（4 行）

| # | 设计稿功能 | 判定 | 缺口 & 证据 | 扩展点 | 最小改动方案 + 工作量 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| I1 | 全部模块通电（可玩链路） | ❌ | W：只接通 Map（`WIRE-01/02/03`） | ✘ | 组合根 + 配置 JSON 入口 + 时间驱动入场景 — **中**（一切的前置） |
| I2 | 多玩家（每玩家迷雾/资源/修正器/科技） | ❌ | A：`FogAppService` 单例持 owner（`FOG-01`）；无 Session（`DEP-03`） | ✘ | `GameSession`/`PlayerContext` + 迷雾状态分离 — **大** |
| I3 | 存档一致性（统一版本号、原子写） | ❌ | A：各模块各自落盘（`DEP-06`、`TIME-08`） | ✘ | 统一存档单元 + 版本迁移 — **中~大** |
| I4 | 测试安全网 | ❌ | W：无测试工程、Domain 依赖 Godot（`DEP-07`） | ✔ | 测试项目 + 内存实现 — **中** |

### 差距汇总

| 判定 | 行数 | 分布 |
| :--- | :--- | :--- |
| ✅ 直接支持 | **13** | B9、C1、C10、C11、C12、D2、D9、E11、F3、F6、G2、G3、G4 |
| ⚠️ 部分支持 | **33** | A2、A4、B1、B3、B5、B6、C2、C3、C4、C5、D1、D3、D5、D11、D12、D14、E1、E2、E4、E7、E8、E10、E18、E19、E20、E21、F1、F4、F5、F9、G1、G7、H1 |
| ❌ 不支持 | **47** | A1、A3、A5、A6、A7、A8、A9、B2、B4、B7、B8、B10、B11、B12、C6、C7、C8、C9、D4、D6、D7、D8、D10、D13、D15、E3、E5、E6、E9、E12、E13、E14、E15、E16、E17、F2、F7、F8、F10、F11、G5、G6、G8、I1、I2、I3、I4 |

---

# 12. 新增问题（v0.2 · 21 条）

> 采用与第 3 节相同的格式。级别已按设计确认后的口径评定。

#### `TIME-12` 缺少 `GameClock`（游戏时间/流速档位/暂停） —— 【P0｜架构层缺陷】
- **现象**：全项目没有"游戏时间"概念；`ITickable.OnTick(float delta)` 的 delta 是真实秒乘 `Scale`（`GodotTimeService.cs:44`），`Scale` 默认 1 且无人设置。
- **证据**：`Scripts/Common/Domain/ITickable.cs:11`、`Scripts/Common/Infrastructure/GodotTimeService.cs:10,44`、`Scripts/Common/Application/ITimeService.cs:14`
- **根因**：时间系统只做了"帧 → 秒"缩放，没做"秒 → 游戏日"换算层（设计口径：30 日/月、360 日/年、三档 1/3/6 日每真实秒）。
- **影响**：① 所有"每回合/每天/每月"玩法（人口、事件、经济、MP）只能用秒近似，策划无法调节奏；② 无法实现暂停（事件决策需要）；③ 一帧跨多日时会漏算"每日掷骰"。
- **修复方向**：新建纯 C# `GameClock`（`CurrentDay`、`SpeedTier`、`Paused`、**逐日派发 `DayElapsed`**）+ `ITimeDriver` 适配层；Godot 侧只负责 `Advance(realDelta)`。
- **关联**：`TIME-05`、`TIME-13`、`A1/A2/A3/A6`、`WP-1.1/1.2`
> ✅ **已修复（v0.3.1 / WP-1.1，v0.3.4 / WP-1.2 补齐驱动）**：`GameClock`（日基础 / 三档 / 暂停 / 逐日派发 / 单帧上限）+ `GodotTimeDriver`（`Node, ITimeDriver`，唯一"真实秒 → 游戏日"换算点，由 `ServiceContainer` 自动挂载）已落地。

#### `TIME-13` 全部"秒"口径需重标定为"游戏日" —— 【P0｜数据层 + 逻辑层缺口】
- **现象**：配置表的 `Duration`/`GrowInterval` 语义是秒；代码里散落硬编码节拍：事件 1 秒、单位攻击 1 秒、单位移动 10 秒。
- **证据**：`EventAppService.cs:39`、`UnitsAppService.cs:145,296`、`ConstructionAppService.cs:141`、`ResourcesAppService.cs:67`；设计侧证据见 `TIME-06`
- **根因**：设计稿统一使用"日"，实现停留在开发期的秒制。
- **影响**：档位切换后各系统节奏不一致；数值无法与设计稿对齐（如"300 日的营地人口增长"）。
- **修复方向**：配置全部改为"日"值；硬编码节拍集中为 `TimeConstants`；`GrowInterval = 30`（月结）。
- **关联**：`TIME-06`（并入本条）、`A4`、`WP-1.5`
> ✅ **已修复（v0.3.4 / WP-1.5）**：① 硬编码节拍收敛到 `Scripts/Core/Time/TimeConstants.cs`（事件 1 日 / 攻击 1 日 / 移动 10 日 / 月结 30 日），`EventAppService.cs:39` 与 `UnitsAppService.cs:145,296` 改引常量；② 新类 `GameTimeService` 把 `GameClock.DayElapsed` 转成逐日 `OnTick(1 日)`，`ITimeService.Scale`（秒制残源）与从未入场景的 `GodotTimeService` 一并删除；③ 5 张表的时长字段按"日"重标定（Resources 30 月结 / Buildings 90·120·60 + 人口 300 / Units 30·35·50）；④ `ConfigValidator.ValidateDayUnit` 自检 >360 日 → warning，防止再次误填秒值。验收：`TimeBaselineChecks` 10 项通过（总计 43/43）。

#### `TIME-14` 缺少月度经济结算器 —— 【P0｜架构层缺陷】
> ⚠️ **v0.3 降级（P0 → P1）**：M0-2 ② 只需"产出按月到账"（现有增长任务 + `GrowInterval=30`），**不需要**需求/赤字/减员。级别收敛依据见 §17.3。
- **现象**：没有统一的"月结算"阶段；资源只有"各资源独立定时增长 + 上限截断"（`ResourcesAppService.cs:58-90`），没有"需求（人口消耗/单位维护）→ 赤字 → 惩罚"闭环。
- **证据**：`Scripts/Resources/Application/ResourcesAppService.cs:58-90`、`Scripts/Resources/Domain/ResourcesPool.cs:38-63`
- **根因**：资源被建模为"增长器"，而不是"供给-需求"经济体。
- **影响**：① 食物消耗、单位维护、赤字减员全部无法落地（`RES-01`/`UNIT-19`）；② 产出无法按来源汇总展示（`MOD-07`）；③ 未来加"税收/贸易/维护费"都要另起一套。
- **修复方向**：新增 `MonthlySettlementService`（订阅月边界）：汇总产出修正 → 计算需求 → 扣减 → 记录赤字 → 触发减员评估。
- **关联**：`RES-01`、`UNIT-19`、`C7/C8/C9/C12`、`WP-3.9/3.10`

#### `TIME-11` 任务进度不受 Modifier 影响 —— 【P1｜逻辑层缺口】
- **现象**：`LinearTask.OnTick` 直接 `Progress += delta`；设计稿的 `BuildingSpeed`（建造速度 ±30%/−50%）、`UnitTrainingSpeed`（训练速度 +20%/+50%）无处生效。
- **证据**：`Scripts/Common/Domain/LinearTask.cs:30-36`、`Scripts/Common/Domain/IntervalTask.cs:46-57`（差距 `A5`）
- **根因**：任务把"推进速率"写死为 1.0，没有修正读取点。
- **影响**：科技树与事件中约 15 个"加速/减速"效果全部失效；经济与生产节奏无法被玩法影响。
- **修复方向**：任务 tick 时按 `Type` 查询修正并加权（`ConstructionSpeed`/`TrainingSpeed`/`ResearchSpeed`）。
- **关联**：`A5/G5/F5`、`WP-4.4`

#### `TECH-07` 跨树前置失效（物理树 35 节点永久不可解锁） —— 【P0｜逻辑层 + 数据层缺口】
- **现象**：科技节点前置只有"节点名列表"、不含所属树；`CanResearch` 只在本树已研究集合里查找。而设计稿中**物理树的根节点前置是数学节点**（简单机械直觉 ← 计数）。
- **证据**：`Scripts/TechTrees/Domain/TechTree.cs:100-124`、`Scripts/TechTrees/Domain/ITechNodeConfig.cs:14`（行号按 v0.3.5 修复后复核）、`design/research_tree.md`（物理 35 节点中 23 个引用数学、化学 10 个引用数学）
- **根因**：`ITechNodeConfig.Prerequisites: List<string>` 缺少树维度；`CanResearch` 未做跨树查询。
- **影响**：**物理树整体不可解锁**（其所有根节点均依赖数学节点）；化学树 10 个节点同样卡死 → 93 个节点中约 45 个永久不可达。
- **修复方向**：前置结构改为 `List<{TreeId, NodeId}>`；`CanResearch` 改为按树查询；`HydrateConfigs` 与配置表同步改造。
- **关联**：`F2`、`WP-2.1`、`D5`（建筑升级条件同样受益）

> ✅ **已修复（v0.3.5 / WP-2.1，2026-09-14）**：`ITechNodeConfig.Prerequisites` 改为 `List<TechPrerequisite{TreeId,NodeId}>`（JSON 兼容层同时接受 `"nodeId"` / `"treeId:nodeId"` / 结构体三种写法，**旧表不需要改写**）；`TechTree.CanResearch` 逐条走 `IsPrerequisiteMet`，跨树靠应用层注入的只读解析器（未注入 = fail closed），`TechTreesAppService` 新增 `CanResearch` 并把解析器接到每次取树之后；`HydrateConfigs` 同时补入"存档后新增"的节点；校验器把**跨树缺树 / 缺节点**与**跨树成环**判 error（成环检测改为跨树统一建边后的全局 DFS）。填报侧新增 `science/counting`（0 idea / 0 日）与最小 `physics` 树，**M0-2 ① 已通过**（49/49 检查、退出码 0）。剩余：93 节点规模差已挂 §18.4，`WP-2.9` 负责单树串行与树集合缓存

#### `CON-09` 没有"建筑升级"用例 —— 【P0｜逻辑层缺口】
> ⚠️ **v0.3 降级（P0 → P1）**：M0-2 ②③④ 全部只需 lv.I 建筑（School lv.I / 营地 / 工坊lv.I），升级链不在验收内。级别收敛依据见 §17.3。
- **现象**：建筑只有"建造"与"移除"两条路径，没有升级动作、没有升级任务类型、没有等级字段；而设计稿 24 个建筑中有 21 个存在 lv.I→II→III 晋级链。
- **证据**：`Scripts/Construction/Application/ConstructionAppService.cs:45-137`、`Scripts/Construction/Domain/IBuildingConfig.cs:10-25`、`design/buildings.md`（晋级列）
- **根因**：MVP 只实现"建一栋"，未实现"养成一栋"。
- **影响**：建筑系统成长线缺失；科技树中 16 条"解锁 XX 升级"无处落地；"附属建筑随区域建筑升级保留"也无从实现。
- **修复方向**：新增 `CanUpgrade` 动作 + `UpgradeTask`；把"建造完成"收敛为单一方法 `CompleteConstruction`（同时修 `CON-03`）；配置加 `UpgradeTo/UpgradeCost/UpgradeDuration/UpgradeTechRequirements`。
- **关联**：`D4/D5/D14/D15`、`WP-2.6`

#### `MAP-16` 建筑 HP 与所有权变更（夺取易主）缺失 —— 【P0｜逻辑层 + 架构层缺口】
> ⚠️ **v0.3 降级（P0 → P1）**：建筑 HP 与易主属城市攻防（批次 4），M0-1/M0-2/M0-3 验收均不涉及。级别收敛依据见 §17.3。
- **现象**：`Building` 无 HP 字段，`GetInfo()` 固定传 `-1`；无 Owner 变更 API；修正器与迷雾均按 ownerId 分桶存放，易主后无人迁移。
- **证据**：`Scripts/Construction/Domain/Building.cs:16-24`、`Scripts/Common/Domain/MapOccupantInfo.cs:29`（HP 字段已存在但建筑恒为 -1）、`Scripts/Common/Domain/IModifierRepository.cs:7-8`、`Scripts/Fog/Application/FogAppService.cs:14-24`
- **根因**：建筑被设计成"不可摧毁的地块装饰"，而设计稿规定住房与军事建筑是**胜负关键**（HP 归零被夺取）。
- **影响**：① 设计稿的胜负条件无法实现；② 城市攻防玩法缺失；③ 即使硬加 HP，易主后修正器/迷雾/人口归属会全部错乱（产出仍算旧主）。
- **修复方向**：`Building` 加 `HP/MaxHP`；新增"夺取"用例：转移 OwnerId → 迁移修正器 → 转移迷雾与人口 → 触发领域事件。
- **关联**：`D7/D15/B12`、`WP-4.8`

#### `MAP-17` 地形移动消耗表缺失且量级不匹配 —— 【P0｜数据层缺口】
- **现象**：设计稿全文没有地形消耗数值表（仅科技效果里出现"水域解锁后 5.0"）；仓库现有 5 个 `.tres` 既无 `MoveCost` 也未按设计命名（`test1~test5`）。旧 `ConfigTableGuide` 示例表量级（1.0/2.0/0/1.5）与设计示例"25 消耗地块"差一个数量级。
- **证据**：`Config/Terrains/*.tres`、`Scripts/Map/Infrastructure/TerrainConfigResources.cs:12`、`Document/ConfigTableGuide.txt:478-484`、`MAP-05/06`
- **根因**：地形是原型占位资产；设计侧未提供消耗表。
- **影响**：移动节奏无法确定（若沿用旧量级，工人一次回复可连走 10 格，玩法失控）。
- **修复方向**：**地形配置转 JSON** + 采用新量级（平原5/沙漠8/森林10/山地25/水域50）+ 补齐解锁规则。
- **关联**：`B1/B2/E8`、`WP-0.4`

#### `UNIT-10` 移动力离散累积模型未实现 —— 【P0｜逻辑层缺口】
> ⚠️ **v0.3.5 显式缺陷标注（`D23`）**：本条**不只是"数值偏差"，而是"单位完全不可移动"** —— `MoveTick` 把 MP 上限写成了单次恢复量（`UnitsAppService.cs:158`），而单位表的 `MoveRechargePerTick` = 3 / 2 / 1.5 **全部小于最小地形消耗 5**（平原），因此三支部队今天**一格都走不了**。M0-1/M0-2 的验收项不涉及移动，本轮**按计划不改行为**（修复归 `WP-3.5`），但 M1 手测前必须知道这一点，避免把"单位不动"误判成手感问题或新引入的 bug。回归条件见 §18.4。
> ⚠️ **v0.3 降级（P0 → P1）**：**M0-3 ②** 才验收移动模型；M0-2 ④ 只要求"单位出现"。级别收敛依据见 §17.3。
- **现象**：实现把 MP 上限压成"单次恢复量"（`CurrentMP = min(CurrentMP + recharge, recharge)`），与设计口径 R1~R7（静默上限 = M、移动中无上限、够则瞬时连格位移、到达清零）完全不同；移动节拍为 10 秒而非 10 游戏日。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:152-236`（`:158` 是关键一行）、`design/unit.md`（移动力累积段）；本条**吸收原 `UNIT-02`（MP 上限压死）与 `UNIT-03`（节拍失衡）**
- **根因**：`MovementPoint`（池容量）与 `MoveRechargePerTick`（恢复量）语义混淆，实现时把后者当上限。
- **影响**：① 移动力数值（10/40/25）全部无法生效；② 消耗大于恢复量的地形永远无法通过；③ "跨 25 消耗地块等 30 天"的手感无法重现。
- **修复方向**：按 R1~R7 重写移动 tick；用 `MP` 字段取代 `Movement/MovementPoint` 的混用。
- **关联**：`E5/E6/E7/E8`、`WP-3.5`、消耗量级见 `MAP-17`

#### `UNIT-11` 训练未与建筑绑定（地点/等级/队列） —— 【P1｜逻辑层 + 数据层缺口】
- **现象**：`CreateUnit(mapId, unitId, position, ownerId)` 完全不涉及建筑；建筑配置的 `Actions` 是"能力"字符串而非"可训练单位"；无训练队列（任务 Id 用 `UnitId`，同名互相覆盖）。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:46-93`、`Scripts/Construction/Domain/IBuildingConfig.cs:18`、`Scripts/Common/Infrastructure/TaskRepository.cs:21`、`TIME-03`
- **根因**：单位被视为"凭空生成"，而设计稿规定**所有单位由建筑产出**（工坊/军营/School lv.II…）并校验建筑等级。
- **影响**：① 玩家可在任意格训练任意单位；② 同名单位同时训练会互相覆盖任务快照；③ 兵源与建筑成长（工坊 lv.II 训练速度 +20%）脱节。
- **修复方向**：配置加 `TrainableUnits`；训练请求携带建筑 uid 并校验等级/科技/资源/人口；新增每建筑训练队列（上限 5、同时 1 个）；完成时人口 −1 并落位。
- **关联**：`D10/D6`、`E2/E3/E4`、`WP-2.5`

#### `UNIT-12` 战斗模型与设计不符（邻格攻击 → 同格交战） —— 【P1｜逻辑层缺口】
- **现象**：现有实现是"近战贴到邻格、远程在射程内隔格攻击"，且单位**不会进入敌格**；设计稿要求"双方处于**同一地块**内交战，每格只能有一个单位或一对交战单位"，并按游戏日持续结算伤害。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:271-336`（`:288-292` 近战寻位、`:313-324` 距离校验、`:326` 伤害）、`design/unit.md`（战斗机制段）
- **根因**：战斗被实现为"两个对象互相扣血"，而不是"一个格子上的交战状态"。
- **影响**：① 战斗表现与战术（占位封锁、一格一对）无法实现；② 单位合并（`E15`）依赖"同格"语义；③ 敌方单位"玩家必须击败它才能进入该地块"（`UNIT-14`）也无法表达。
- **修复方向**：引入 `CellCombat`（交战状态：攻击方/防守方/位置）替代"邻格攻击"；伤害按日结算；`ATK` 为每日伤害。
- **关联**：`E9/E10/E15/E18/E19`、`WP-3.6`

#### `UNIT-13` 无驻扎系统与影响范围字段 —— 【P1｜逻辑层 + 数据层缺口】
- **现象**：单位配置没有"影响范围"字段；没有"单位驻扎建筑"的概念。设计稿中学者（驻扎学院 Idea +10%）、化学家（驻扎矿场矿物 +10%）依赖此机制。
- **证据**：`Scripts/Units/Domain/IUnitConfig.cs:6-22`、`Scripts/Units/Domain/Unit.cs:13-28`、`design/unit.md`（影响范围列）
- **根因**：单位只有"移动/攻击"两种状态，没有"驻扎"这一持续状态。
- **影响**：学者/化学家的核心价值无法实现；单位"死亡后驻扎加成消失"也无从处理；范围类效果（`MOD-03`）少了一个主要消费者。
- **修复方向**：新增驻扎绑定（单位 uid ↔ 建筑 uid）；单位配置加 `InfluenceRadius`；驻扎时挂 Entity 级修正（依赖 `MOD-03` 宿主化）。
- **关联**：`E16/E17`、`MOD-03`、`WP-4.6`

#### `UNIT-14` 无敌方单位与地块封锁 —— 【P1｜架构层缺陷】
- **现象**：地图生成只产地形，没有"按地形概率放置敌方单位"的流程；没有"敌方存活则不可进入/建造/采集"的规则。
- **证据**：`Scripts/Map/Domain/IMapGenerator.cs:5`、`Scripts/Map/Infrastructure/VoronoiMapGenerator.cs:25-37`、`Scripts/Map/Application/MapAppService.cs:38-42`（`IsClear` 只判占用）
- **根因**：生成器职责止于地形；"占领/封锁"这一地块语义未建模。
- **影响**：① 探索没有阻力，早期玩法空转；② 敌方掉落（`E20`）与"击败才能进入"的战斗动机缺失。
- **修复方向**：新增生成后处理（`IMapPostProcessor`/`EnemySpawner`，读地形概率表）+ 新增 `NoHostileRequirement` 接入建造与移动校验。
- **关联**：`B7/B8/E19`、`WP-3.8`

#### `UNIT-18` 无单位标签与条件化伤害修正（克制/减伤/对建筑加成） —— 【P1｜逻辑层 + 数据层缺口】
- **现象**：实现只有"攻击侧伤害"一个数值，没有"目标类型/目标标签"条件、没有"防御侧减伤"。设计稿要求：长矛兵"对近战敌人伤害 +20%"、弓箭手"优先攻击远程敌人"、重装卫士"受到伤害 −25%"、弩炮"对建筑伤害 +50%"。
- **证据**：`Scripts/Units/Application/UnitsAppService.cs:326`（直接 `HP -= AttackDamage`）、`Scripts/Units/Domain/IUnitConfig.cs:6-22`；本条**吸收原提议的 `UNIT-15`**
- **根因**：Modifier 体系只有"作用于某 target 的绝对/比例值"，没有"条件筛选"维度（`MOD-02` 的另一面）。
- **影响**：兵种克制（设计正文明确要求的机制）完全缺失；攻城/护卫等单位的定位无法表达。
- **修复方向**：单位加 `Tags`（近战/远程由 `AttackRange` 派生，额外标签如攻城/学者/侦察另填）；新增"条件化修正"求值（条件 = 目标标签 / 目标类型 / 自身标签）；伤害公式按"衰减 → 加伤"顺序（0.5×1.5=0.75）。
- **关联**：`E12/E13/E14`、`MOD-03`、`WP-4.3`

#### `UNIT-19` 无单位维护费 —— 【P1｜数据层 + 逻辑层缺口】
- **现象**：单位配置没有 `Maintenance` 列（设计正文有、表格漏填，已确认补），资源侧没有"周期性扣减"。
- **证据**：`Scripts/Units/Domain/IUnitConfig.cs:6-22`、`design/unit.md`（正文"维护：每月消耗的食物"）
- **根因**：单位只考虑一次性训练成本，未考虑持有成本。
- **影响**：军队规模没有经济约束，4X 的"扩张 vs 经济"张力缺失；与人口食物消耗（`RES-01`）本应共用同一套"月度需求"结算。
- **修复方向**：配置加 `Maintenance`（默认值见 §10.3.4）→ 并入 `MonthlySettlementService`（`TIME-14`）。
- **关联**：`C8`、`TIME-14`、`WP-3.9`

#### `EVT-06` 无时间暂停与玩家决策流程 —— 【P1｜架构层缺陷】
> ⏳ **未修**：事件仍是触发即结算；暂停 + 决策流程归 `WP-4.12`（`GameClock.Paused` 已具备能力）。
- **现象**：事件系统是全自动的（触发即扣资源、挂修正器），没有暂停、没有选项、没有等待确认；`ITimeService` 也没有暂停能力。
- **证据**：`Scripts/Events/Application/EventAppService.cs:44-73`、`Scripts/Common/Application/ITimeService.cs:10-15`、`design/events.md`（"事件发生时时间会暂停，直到确认后才继续"）
- **根因**：事件被建模为"概率性数值挂载"，而非"需要玩家交互的流程"。
- **影响**：设计稿的事件体验（决策、后果、叙事）无法实现；事件触发后玩家甚至不知道发生了什么（`EVT-04`）。
- **修复方向**：`GameClock.Paused` + 待处理事件队列 + UI 面板；事件流程（触发 → 暂停 → 展示 → 玩家确认/选择 → 恢复）。
- **关联**：`A6/G6`、`WP-4.12`

#### `RES-01` 无资源需求 / 赤字 / 人口减员 —— 【P1｜逻辑层 + 架构层缺口】
- **现象**：资源池只有"加值 + 上限截断"，没有"需求"概念，也没有赤字记录与人口惩罚；`MapCell.Population` 只是一个 int，没有"食物需求"关联。
- **证据**：`Scripts/Resources/Domain/ResourcesPool.cs:38-63`、`Scripts/Map/Domain/MapCell.cs:15,42-50`、`design/resources.md`（Food 基础需求 = 人口 ×3/月；连续三年不足则随机减少人口）
- **根因**：经济体只实现了"收入"，没有"支出"与"惩罚"。
- **影响**：人口扩张没有成本约束；"粮食危机"这一核心玩法张力缺失。
- **修复方向**：并入 `MonthlySettlementService`：计算需求（人口 ×3 + 单位维护）→ 扣减 → 记录连续赤字月数 → 连续 ≥36 月触发年评估减员（logistic 公式见 §10.3.4）。
- **关联**：`C7/C9/N12`、`TIME-14`、`WP-3.9/3.10`

#### `RES-02` 无产量浮动及其修正（观星台） —— 【P2｜逻辑层 + 数据层缺口】
- **现象**：资源结算没有随机浮动；观星台"使三格范围内农田产量浮动上限变为 +5%、下限变为 −1%"需要"浮动区间"这一可被修正的数据。
- **证据**：`Scripts/Resources/Application/ResourcesAppService.cs:69-86`、`design/resources.md`（农田 144/年 浮动 ±20%）、`design/buildings.md`（观星台）
- **根因**：产量被视为确定值；随机性只在生成器与事件中使用。
- **影响**：农田的"±20%"与观星台的独特价值无法表达；农业玩法失去波动感。
- **修复方向**：配置加 `ProductionVariance`（默认 0.2）；新增"浮动上限/下限"两个修正 target；结算时用 `IRandom` 在区间内取值。
- **关联**：`C6`、`MOD-03`（范围修正）、`WP-4.5`

#### `FOG-05` 无"永久清除迷雾"语义 —— 【P2｜逻辑层缺口】
- **现象**：`RevealArea` 只把格子提升到 `Visible`、在外圈标记 `Fogged`；`ResetArea` 只把 `Visible` 降级为 `Fogged`。没有"清除/永久可见"状态。设计稿要求"建筑的两格范围内不会再有迷雾"。
- **证据**：`Scripts/Fog/Application/FogAppService.cs:10-12,56-97`
- **根因**：迷雾只有三态（未探索/雾/可见），且"可见"依赖视野计数，没有"永久"维度。
- **影响**：建筑（尤其升级后的区域建筑）应提供的"领土无雾"效果无法实现；玩家需要反复清理同一片区域。
- **修复方向**：迷雾状态增加"永久可见"标记（不受 `ResetArea` 影响），由建筑 `VisionRadius` 与升级状态驱动。
- **关联**：`B5/D14`、`WP-4.10`

#### `FOG-06` 地形通行无法被科技解锁 —— 【P2｜逻辑层缺口】
- **现象**：通行判定只读静态 `Terrain.MoveCost > 0`；设计稿的"浮力定律：解锁水域通行（移动消耗 5.0）"要求"默认不可通行 + 科技解锁后变为可通行（且有代价）"。
- **证据**：`Scripts/Map/Application/MapAppService.cs:151-156`、`Scripts/Units/Application/UnitsAppService.cs:252-259`、`design/research_tree.md`（浮力定律）
- **根因**：地形属性是全局静态的，没有"每玩家的地形解锁状态"。
- **影响**：水域/山地相关科技失去意义；海军/渡河玩法无法扩展。
- **修复方向**：新增"每玩家地形解锁集合"（由科技前置派生）+ 通行判定接入（地形配置加 `UnlockTech`）。
- **关联**：`B2/F8`、`WP-4.11`

#### `DEP-11` 缺少"解锁"派生查询与 UI 门控 —— 【P2｜装配层 + 表现层缺口】
- **现象**：设计确认"解锁 = 前置，仅 UI 表现不同"，但项目里既没有统一的"某内容是否已解锁"查询，也没有 UI 门控（如"计数"节点解锁资源面板与研究功能）。
- **证据**：`Scripts/TechTrees/Domain/TechTree.cs:59-62`（只有 `IsResearched`）、表现层仅有 `DevMapUi`/`MapView`（`Scripts/Map/Presentation/`）
- **根因**：原设计设想独立"解锁注册表"；确认等价后，缺的是"派生查询 + 接入点"。
- **影响**：UI 无法按游戏进程逐步开放；建筑升级按钮/研究按钮/资源面板的显示条件无处判断。
- **修复方向**：提供 `IsUnlocked(...)` 派生查询（基于 `TechRequirements`）；表现层在渲染与交互前统一门控。
- **关联**：`F7/N2`、`WP-4.14`

### 12.x 未立条目的提案（已合并说明）

| 曾提议 ID | 处理 | 原因 |
| :--- | :--- | :--- |
| `MAP-14`（无产出归因模型） | **不立条目**，并入 `C3`（建筑产出用 Modifier Absolute 表达）+ `MOD-07`（查询 API） | 现有 `ModifierValue.SourceId` 已具备归因能力，无需新系统 |
| `MAP-15`（地块只能容纳 1 个建筑） | **并入 `MAP-12`** | 同一数据结构问题（区块槽位模型） |
| `UNIT-15`（无兵种分类/克制/对建筑系数） | **并入 `UNIT-18`** | 同一机制（标签 + 条件化修正） |
| `TIME-06`（节拍硬编码） | **并入 `TIME-13`** | 同一口径问题 |
| `UNIT-02`（MP 上限压死）、`UNIT-03`（节拍失衡） | **并入 `UNIT-10`** | 同一模型的设计侧与实现侧 |

---

# 13. 第四阶段：最小化改造清单

> 排序原则：**先通电 → 再保证核心数据不丢 → 再修玩法正确性 → 最后深度机制与表现层**。
> "性质"列：`代码` / `填表` / `资产`。工作量：小 / 中 / 大。

## 批次 0 · 工程基座（前置，4 个 WP）

| WP | 内容 | 覆盖 | 涉及文件/模块 | 新增类型/字段 | 量 | 性质 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| WP-0.1 | 清理死代码与失效配置 | `DEP-08/04/05/09`、`A?` | 删空目录 `Scripts/Time·Event·Research·Tree`；删 `ITaskRepository.cs:1` 的 `using Godot`；删 csproj 失效排除项；统一 `IRandom.Next` 参数顺序 | — | 小 | 代码 |
| WP-0.2 | 新建测试工程 + 内存实现 | `I4`、`DEP-07` | 新增 `Tests/`（xUnit, net8.0）；新增内存替身 | `InMemoryFileSystem`、`InMemoryMapRepository`、`ManualTimeDriver`、`InMemoryConfigSource` | 中 | 代码 |
| WP-0.3 | IO/配置抽象（纯 C# 接口 + Godot 适配） | `B10`（部分）、`WIRE-04` | `Common/Infrastructure/` 分层 | `IFileSystem`、`IConfigSource`、`GodotFileSystem`、`GodotJsonConfigSource` | 小 | 代码 |
| WP-0.4 | **地形配置转 JSON** | `B1`、`MAP-17`、`MAP-05/06` | 新增 `res://Config/Terrains.json`；`VoronoiMapGenerator` 改依赖 `ITerrainConfigSource`；`TerrainConfigResources` 退役（`.tres` 仅留编辑器可视化） | `TerrainConfigDto`、`ITerrainConfig`、`TerrainsConfigRepository` | 小 | 代码+填表 |

## 批次 1 · 时间与装配通电（P0 · 里程碑 M0-1，5 个 WP）

| WP | 内容 | 覆盖 | 涉及文件/模块 | 新增类型/字段 | 量 | 性质 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| WP-1.1 | **`GameClock`（纯 C#）**：`CurrentDay`、档位（1/3/6 日每真实秒）、暂停、**逐日派发 `DayElapsed`** | `A1/A2/A3`、`TIME-12` | 新增 `Scripts/Core/Time/` | `GameClock`、`GameTime`、`TimeSpeedTier` | 中 | 代码 |
| WP-1.2 | 时间驱动适配（Godot 只负责 `Advance`） | `A1`、`WIRE-02` | `GodotTimeService` → `GodotTimeDriver : Node, ITimeDriver` | `ITimeDriver` | 小 | 代码 | → **✅ 完成（v0.3.4）** |
| WP-1.3 | **`CoreBootstrap` + `GameSession`**（会话持有地图与各玩家子系统状态） | `I2`、`DEP-03`、`MAP-01`、`FOG-01` | 新增 `Scripts/Bootstrap/`；`ServiceContainer` 极薄化 | `CoreBootstrap`、`GameSession`、`PlayerContext`、`CoreDependencies` | **大** | 代码 |
| WP-1.4 | **7 张配置表全部通电** + 启动期校验（表非空 / Id 引用完整 / 枚举合法） | `WIRE-01/03/04` | 各 `*ConfigRepository`、`ServiceContainer` | `ConfigValidator` | 中 | 代码 |
| WP-1.5 | **口径重标定**：所有时长/节拍改"日"；硬编码集中为常量 | `A4`、`TIME-13` | 5 张配置表 + `EventAppService.cs:39`、`UnitsAppService.cs:145,296` | `TimeConstants` | 小~中 | 代码+填表 | → **✅ 完成（v0.3.4）** |

## 批次 2 · 单玩家可玩（P0 · 里程碑 M0-2，10 个 WP）

| WP | 内容 | 覆盖 | 涉及文件/模块 | 新增类型/字段 | 量 | 性质 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| WP-2.1 | **跨树前置修复**（P0 死锁） | `F2`、`TECH-07` | `ITechNodeConfig`、`TechTree.CanResearch`、`TechTreesRepository.HydrateConfigs` | `TechPrerequisite{TreeId,NodeId}` | 中 | 代码+填表 |
| WP-2.2 | 任务体系最小修：**UId 作存储键 + 完成即回收** | `A7`（部分）、`TIME-03/04/09` | `TaskSnapshot`、`TaskRepository`、`GodotTimeDriver` | — | 中 | 代码 |
| WP-2.3 | **人口任务修 bug**：OwnerId、注销、人口落盘 | `D11`、`CON-04/05/06` | `ConstructionAppService.cs:141`、`MapCell`、存档层 | — | 中 | 代码 |
| WP-2.4 | **建造者校验**：必须由建造者单位建造、每建筑同时 1 个、可在相邻格建造 | `D6` | `ConstructionAppService.StartConstruction`、`UnitsAppService.Build` | `BuilderBinding` | 中 | 代码 |
| WP-2.5 | **训练绑定建筑 + 队列**（可训练单位、建筑等级校验、上限 5、完成时人口 −1、落位） | `D10`、`E2/E3/E4`、`UNIT-11` | `UnitsAppService`、`IBuildingConfig`、`Building` | `TrainingQueue`、`TrainableUnits` | 中 | 代码+填表 |
| WP-2.6 | **建筑升级**（lv.I→II→III）+ 完成逻辑收敛为 `CompleteConstruction`（修 `CON-03`） | `D4/D5/D14`、`CON-09` | `ConstructionAppService` | `UpgradeTask`、`UpgradeTo/UpgradeCost/UpgradeDuration/UpgradeTechRequirements` | 中~大 | 代码+填表 |
| WP-2.7 | **建筑产出接线**（Modifier Absolute + 月结 `GrowInterval=30`） | `C2/C3/C11/D12` | `ResourcesAppService`、建筑配置 | — | 小 | 填表 |
| WP-2.8 | **事件按日掷骰 + 表结构重写** | `G1/G2/G3/G4`、`EVT-01/02` | `EventAppService`（改为订阅日边界）、`EventsConfig.json` | — | 小~中 | 代码+填表 |
| WP-2.9 | 科技树：**单树串行 + 三树并行**（并发可配，为"一树多研发"预留） | `F1` | `TechTreesAppService` | `Concurrency` 配置 | 中 | 代码 |
| WP-2.10 | **最小领域事件总线**（研究完成/建造完成/单位死亡/事件触发） | `F10`、`TECH-05`、`UNIT-08` | 新增 `IDomainEventBus`；各 AppService 发布点 | `DomainEventBus` + 事件类型 | 中 | 代码 |

## 批次 3 · 世界不丢 + 真实移动战斗（P0/P1 · 里程碑 M0-3，10 个 WP）

| WP | 内容 | 覆盖 | 涉及文件/模块 | 新增类型/字段 | 量 | 性质 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| WP-3.1 | **Map 常驻内存**（`MapSession` + 脏标记 + 存档点；`IMapRepository` 退为边界） | `B11`、`MAP-01/07` | `MapAppService` 拆 `MapQuery`/`MapCommand` | `MapSession`、`MapQueryService` | 大 | 代码 |
| WP-3.2 | **实体持久化**：建筑/单位/人口/任务/迷雾/时间完整存档 | `B10`、`MAP-02` | 存档层 + Mapper | `BuildingSaveDto`、`UnitSaveDto`、`CellSaveDto`、`SaveMapper` | 大 | 代码 |
| WP-3.3 | **统一存档单元**（单序列化器、原子写、`saveVersion` 迁移钩子） | `I3`、`DEP-06`、`TIME-08` | 5 个 `*Repository` 收敛 | `ISaveStore` | 中 | 代码 |
| WP-3.4 | **地块占用权威一致**（统一进入/离开 API、修僵尸索引） | `B3`、`MAP-03/04`、`UNIT-05` | `Map`、`MapCell`、`UnitsAppService` | — | 中 | 代码 |
| WP-3.5 | **单位移动模型重写**（R1~R7）+ 地形消耗与通行权限 | `E5/E6/E7/E8`、`UNIT-10`、`B1/B2`（部分） | 新增 `UnitMovementService` | — | 中 | 代码+填表 |
| WP-3.6 | **同格战斗**（进入敌格即交战、一格一对）+ 按日结算 + 建筑衰减 50% + 建筑作为目标 | `E9/E10/E11/E12/E18`、`UNIT-12` | 新增 `UnitCombatService`、`Unit`（`MaxHP`） | 交战状态模型 | 大 | 代码 |
| WP-3.7 | 单位死亡：移除 / 不返还 / 掉落入池 / 清理驻扎 | `E20/E21` | `UnitCombatService` | `Loot` 配置字段 | 中 | 代码 |
| WP-3.8 | **敌方单位**：生成后按地形概率放置 + 地块封锁（不可进入/建造/采集） | `B7/B8/E19`、`UNIT-14` | 新增 `IMapPostProcessor`/`EnemySpawner`、`NoHostileRequirement` | 2 个类型 + `SpawnTable` | 中 | 代码+填表 |
| WP-3.9 | **月度经济结算器**（产出汇总 → 需求 → 扣减 → 赤字记录） | `C7/C8/C12`、`UNIT-19`、`RES-01`（部分） | 新增 `MonthlySettlementService` | 结算器 + `Maintenance` 字段 | 中 | 代码+填表 |
| WP-3.10 | 赤字 → **连续 3 年 → logistic 随机减员** | `C9`、`RES-01` | `MonthlySettlementService` | — | 中 | 代码 |

## 批次 4 · 深度机制（P1，14 个 WP）

| WP | 内容 | 覆盖 | 涉及文件/模块 | 新增类型/字段 | 量 | 性质 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| WP-4.1 | **修正器升级**：多 target 累加（修 `MOD-01`）+ 宿主作用域（Player/Entity/Cell/World）+ 阶段管道 | `D13`、`MOD-01/02/03` | `ModifierManager`、`IModifierRepository`、`ModifierAppService` | `ModifierHost`、`ModifierStage` | 大 | 代码 |
| WP-4.2 | **范围效果**（观星台 3 格 / 骨笛工坊 1 格 / 振动与波相邻同类型） | `D13` | 空间查询 + 目标标签筛选 | `ModifierScope`、建筑 `Category` | 中~大 | 代码+填表 |
| WP-4.3 | **单位标签 + 条件化修正**（对近战 +20% / 受伤 −25% / 对建筑 +50%） | `E13/E14`、`UNIT-18` | 战斗求值器 | `Tags`、`TargetPriority`、`ConditionalModifiers` | 中 | 代码+填表 |
| WP-4.4 | **修正作用面扩展**：上限（接 `AddLimit`）/ 时长（任务 Target）/ 视野 / 消耗 / 任务速率 | `C5`、`F5`、`TIME-11` | `ResourcesPool`、任务、视野读取、消耗校验 | — | 中~大 | 代码 |
| WP-4.5 | **产出浮动 ±20%** + 观星台改写上下限 | `C6`、`RES-02` | 结算器 | `ProductionVariance` + 2 个修正 target | 中 | 代码+填表 |
| WP-4.6 | **驻扎系统**（学者/化学家，影响范围、产出加成、死亡消失） | `E16/E17`、`UNIT-13` | `Unit`、`Building` | `GarrisonBinding`、`InfluenceRadius` | 中~大 | 代码 |
| WP-4.7 | **单位合并**（同模板、HP 之和 ≤ MaxHP、同格） | `E15` | `Unit`（`MaxHP`） | 合并用例 | 中 | 代码 |
| WP-4.8 | **建筑 HP + 易主**（住房/军事有 HP；归零成为敌方建筑；修正器/迷雾/人口迁移） | `D7/D15/B12`、`MAP-16` | `Building`、`ConstructionAppService`、Fog/Modifier 归属 | `HP/MaxHP/HasHP/IsVictoryCritical` | 大 | 代码+填表 |
| WP-4.9 | **区域建筑 + 附属建筑嵌套** | `B4/D15`、`MAP-12` | `MapCell` 建筑集合 + `Building.HostUId` | — | 大 | 代码 |
| WP-4.10 | **迷雾**：服务/状态分离（每玩家）+ 永久清除语义 + 视野列 | `B5/B6/B9/H1`、`FOG-01/05` | `FogAppService` → `FogState` + 无状态服务 | `IFogState`、`FogRegistry` | 中 | 代码+填表 |
| WP-4.11 | **地形通行解锁**（水域/山地由科技解锁） | `B2/F8`、`FOG-06` | 通行判定接解锁查询 | 每玩家解锁集合 | 中 | 代码 |
| WP-4.12 | **事件暂停 + 玩家决策** | `A6/G6`、`EVT-06` | `GameClock.Paused` + 挂起队列 + 流程 | 事件流程模型 | 中 | 代码 |
| WP-4.13 | **产出归因查询**（"查看资源加减项"）+ SourceId 语义规范（建筑 uid） | `C4`、`MOD-07` | `ModifierAppService` | `ModifierQueryService` | 中 | 代码 |
| WP-4.14 | **UI 门控**（资源面板、研究功能、升级按钮按解锁状态显示） | `F7`、`DEP-11` | 表现层查询契约 | — | 中 | 代码 |

## 批次 5 · 表现层与优化（P2 · 里程碑 M1/M2，6 个 WP）

| WP | 内容 | 覆盖 | 涉及文件/模块 | 新增类型/字段 | 量 | 性质 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| WP-5.1 | **M1 极简调试层**：文本状态面板（资源/人口/队列/研究/事件）+ 按钮（建造/训练/研究/推进 1 月）+ 纯色六边形 | `F7` | 替换 `DevMapUi`，新增 `DebugHud` | — | 中 | 代码（Godot） |
| WP-5.2 | 修现有脚手架致命问题（`MapView` 未注入、`CameraController` 孤儿） | `WIRE-05`、`MAP-09` | `MapView`、`CameraController` | — | 小 | 代码（Godot） |
| WP-5.3 | **地形外观数据驱动**（配置 `Sprite`/`Color`，缺美术用纯色占位） | `B1` | 地形表字段 + `MapCellView` | `Sprite`/`Color` | 小 | 代码+填表 |
| WP-5.4 | **正式表现层（M2）**：图层化（地形/建筑/单位/迷雾）、交互（选址建造、单位指令、事件弹窗）、意图结构化（修 `CON-02/08`） | `I2` | 新增各模块 `Presentation/` | `PlayerIntent`、`IActionHandler` | 大 | 代码（Godot） |
| WP-5.5 | 迷雾性能（半径模板缓存 + 增量更新 + 紧凑存档） | `H1`、`FOG-03/04` | `FogState`、`FogRepository` | — | 中 | 代码 |
| WP-5.6 | 地图存档性能（脏格子增量写、`DeleteMap/ListMaps` 补全） | `MAP-07/08` | `MapRepository` | — | 中 | 代码 |

### 13.x 三张专项清单

**(1) 只需填表/改资产即可完成（不改代码）**

- 资源配置重写（3 种资源的初始值/上限/月结间隔/浮动区间）；Id 规范为 `Idea` / `Food` / `BasicMinerals`
- 24 个建筑条目（含分类、前置、可建地块、建造/升级消耗与时间、产出用 Modifier Absolute 表达、人口与上限参数、视野、HP）
- 93 个科技节点（前置改为 `{TreeId,NodeId}`、成本、时长、数值效果；"解锁"类效果只在建筑表填前置）
- 20 个事件（触发概率 `%/日`、时长、消耗、前置、产量与速率修正）
- 地形 5 类（新增 JSON 表：Id/Name/MoveCost/Passable/UnlockTech/Weight/Sprite/Color）
- 15 个单位（HP/MaxHP/ATK/AttackRange/MP/视野/影响范围/维护/Tags/训练地点与时长/敌方刷新与掉落）

**(2) 必须新增 C# 类型（不触及核心签名）**

`GameClock`/`GameTime`/`TimeSpeedTier`/`ITimeDriver` · `CoreBootstrap`/`GameSession`/`PlayerContext`/`CoreDependencies` · `ConfigValidator` · `IFileSystem`/`IConfigSource`（+Godot 适配）· `TerrainConfigDto`/`ITerrainConfig`/`TerrainsConfigRepository` · `MonthlySettlementService` · `TrainingQueue` · `UpgradeTask` · `BuilderBinding` · `UnitMovementService`/`UnitCombatService` · `EnemySpawner`/`IMapPostProcessor` · `NoHostileRequirement` · `GarrisonBinding` · `DomainEventBus` · `ModifierHost`/`ModifierStage`/`ModifierQueryService` · `ProductionVariance` 求值 · `IFogState`/`FogRegistry` · 存档 DTO 与 `SaveMapper` · `PlayerIntent`

**(3) 必须动核心架构（签名/职责变更）**

1. `GodotTimeService` → 拆为"纯 C# 推进器 + Godot 适配器"，时间单位由秒改日（`A1/A2/A3/A5`）
2. `TaskSnapshot`/`ITaskRepository`/`IProgressTask` → UId 键 + OwnerScope + 自动回收（`A7/A8`）
3. `MapAppService` → 内存聚合（`MapSession`）+ 存档边界 + 查询/命令分离（`B10/B11`）
4. `MapCell`/`Map` → 占用模型（嵌套/权威一致）（`B3/B4`）
5. `FogAppService` → 服务与状态分离（每玩家）（`I2/H1`）
6. `ModifierManager`/`IModifierRepository` → 多 target 累加 + 宿主作用域 + 阶段 + 查询（`A5/C5/C6/D13/E13/G5`）
7. `Building`/`Unit` → 可序列化状态 + HP/MaxHP + 易主（`D7/E15/B12`）
8. `ITechNodeConfig.Prerequisites` → `{TreeId,NodeId}`（`F2`，P0 最急）

### 13.y 关键依赖链（v0.2 版；完整的依赖边与批次重排见 §13.z）

`WP-0.x → WP-1.1 → WP-1.3 → WP-1.4 → WP-2.x → WP-3.1 → WP-3.2 → WP-4.1`

- `Modifier` 宿主化（`WP-4.1`）必须在 `Map` 内存化（`WP-3.1`）之后，否则修正器没有稳定的宿主对象
- `建筑升级`（`WP-2.6`）依赖 `跨树前置`（`WP-2.1`）与 `任务键修正`（`WP-2.2`）
- `同格战斗`（`WP-3.6`）依赖 `占用权威一致`（`WP-3.4`）与 `移动模型`（`WP-3.5`）
- `月度经济结算器`（`WP-3.9`）依赖 `月结节拍`（`WP-1.1`）与 `建筑产出接线`（`WP-2.7`）

---

---

## 13.z 工作包级别 / 依赖 / 风险 / 降级（v0.3）

> 本节是 §13 的补充，并为 §13.y 的依赖关系补全全部依赖边。级别口径：
> **P0** = 不做就无法通过 M0-1 或 M0-2 验收；**P1** = M0-3 验收或玩法正确性必需；**P2** = 深度机制与表现层。
> 「风险点 / 降级方案」只为 **大**（含按"大"处理的"中~大"）与 **P0** 项填写，其余为 `—`。
> 说明：**WP 级别与问题级别不一一对应** —— 例如 WP-2.5 / WP-2.7 对应的问题（`UNIT-11` / `C3`）并非 P0，但它们被 M0-2 的验收项直接需要，故为 P0。

### 批次 0 · 工程基座（4 个 WP）

| WP ID | 新级别 | 依赖 | 风险点 | 降级方案 |
| :--- | :--- | :--- | :--- | :--- |
| WP-0.1 清理死代码与失效配置 | P2 | — | — | — |
| WP-0.2 测试工程 + 内存替身 | **P0** | WP-0.3（替身实现的接口）；`ManualTimeDriver` 部分 ← WP-1.1 | 测试项目若引用 `Godot.NET.Sdk` 主工程，会把 Godot 依赖带进 headless 测试（`GodotSharp` 不是传递引用） | 优先把纯 C# 部分（Domain+Application+Core）拆成独立类库供测试引用；工期紧时可让测试项目引用主工程并**跳过**涉及 Godot 类型的用例 |
| WP-0.3 IO/配置抽象（`IFileSystem` / `IConfigSource` + Godot 适配） | **P0** | — | — | — |
| WP-0.4 地形配置转 JSON | **P0** | WP-0.3 | ① 替换 `VoronoiMapGenerator` 与 `GodotMapRepository` 的地形加载路径会同时牵动 `.tres` 读取；② 必须同步补 `MAP-05/06` 的 Id 与 `MoveCost`（数据），否则 M0-1 ② 的校验仍失败 | 过渡期"双读"（JSON 优先、`.tres` 兜底）；数据先只填 5 类地形的最小字段（Id/Name/MoveCost/Passable/UnlockTech） |

### 批次 1 · 时间与装配通电（5 个 WP）+ 前移的 WP-3.1

| WP ID | 新级别 | 依赖 | 风险点 | 降级方案 |
| :--- | :--- | :--- | :--- | :--- |
| WP-1.1 `GameClock` | **P0** | —（纯 C#） | 6 日/秒档位 + 掉帧时单帧可能跨上百日 → 逐日派发退化为长时间循环 | 按需订阅（无监听者的日边界直接跳过派发）+ 单帧最大推进日数上限（如 30 日），超出部分记为欠账 |
| WP-1.2 `GodotTimeDriver` | P1 | WP-1.1、WP-1.3 | — | — |
| WP-1.3 `CoreBootstrap` + `GameSession` | **P0** | WP-0.3、WP-1.1 | ① 服务构造顺序（现有单类 8 个构造参数且互相依赖）极易撞**构造循环依赖**；② `GameSession` 若同时持"状态 + 服务"，测试时无法替换实现 | ① 用"扁平注册表 + 延迟解析（Lazy）"打破循环；② 把"状态"（GameSession）与"服务"（CoreServices）**分开构造**，会话只持状态 |
| WP-1.4 7 张配置表通电 + 启动期校验 | **P0** | WP-0.3、WP-0.4、WP-1.3 | 原型数据（3 建筑 / 3 单位 / 5 科技节点）与设计规模差距大，一上来就启用"引用完整性校验"必然全表报错 → M0-1 ② 无法通过 | 校验**分级**（error / warning）且不阻断启动；M0-1 ② 只要求"解析成功 + 无 error"；引用完整性先只校验**同表内闭合** |
| WP-1.5 口径重标定（秒 → 游戏日） | **P0** | WP-1.1、WP-1.4 | 同时触及 5 张配置表 + 5 处硬编码节拍，漏改一处的表现是"节奏慢 N 倍"，不易察觉 | 用 `TimeConstants` 单点定义；加"配置单位自检"（如 `GrowInterval` 落在 [1,365] 才视为合法日值） |
| **WP-3.1** Map 常驻内存（由批次 3 前移） | **P0** | WP-1.3 | ① `MapAppService` 现有 14 个方法全是 Load→改→Save，改为内存态需逐个改写并保证脏标记正确；② "谁负责落盘"若不明确，会出现"测试通过、实机丢档" | ① 两步走：先加**会话级 Map 缓存**（读走缓存、写更新缓存 + 脏标记），暂不动方法签名；② 落盘统一交给 WP-3.3 的存档点，测试期用 InMemory 实现 |

### 批次 2 · 单玩家可玩（10 个 WP）

| WP ID | 新级别 | 依赖 | 风险点 | 降级方案 |
| :--- | :--- | :--- | :--- | :--- |
| WP-2.1 跨树前置修复 | **P0** | WP-1.4 | 前置结构变更会同时影响**建筑表与事件表**的 `TechRequirements` 解析（现为 `Dictionary<treeId, nodeIds>`） | 兼容层：`Prerequisites` 同时接受 `"treeId:nodeId"` 字符串与结构体两种写法，旧表不破 |
| WP-2.2 任务键 + 完成即回收 | P1 | WP-1.1、WP-1.3 | — | — |
| WP-2.3 人口任务修 bug | P1 | WP-2.2、**WP-3.1** | — | — |
| WP-2.4 建造者校验 | P1 | WP-2.2、**WP-3.1**、WP-3.4（占用视图） | — | — |
| WP-2.5 训练绑定建筑 + 队列 | **P0** | WP-2.2、**WP-3.1** | "校验建筑等级"需要等级字段，而等级依赖已降级的升级体系（`CON-09`），会被依赖链拖住 | 本轮只做「建筑 uid 关联 + 训练完成时人口 −1 + 落位」；配置缺等级字段时**跳过**等级校验 |
| WP-2.6 建筑升级 | P1 | WP-2.1、WP-2.2、**WP-3.1** | — | — |
| WP-2.7 建筑产出接线 | **P0** | WP-1.4、WP-1.5 | Modifier 目标名与配置表不一致会**静默失效**（`MOD-05` 的静默降级） | 先只支持 3 个 target（`IdeaGrowth` / `FoodGrowth` / `MineralGrowth`）+ 启动期 target 名白名单校验 |
| WP-2.8 事件按日 + 表结构 | **P0** | WP-1.1、WP-1.4、WP-1.5 | 与"20 条事件全量重写"耦合，填表工作量大 | 先修**结构**（根对象 + `TriggerChancePerDay`）并保留 3 条原型事件；引擎侧沿用「`IntervalTask` target = 1 日」即等价"每日判定"，无需改架构 |
| WP-2.9 科技串行 + 三树并行 | P1 | WP-2.1、WP-2.2 | — | — |
| WP-2.10 最小领域事件总线 | P1 | WP-1.3 | — | — |

### 批次 3 · 世界不丢 + 真实移动战斗（10 个 WP，含前移的 WP-3.1）

| WP ID | 新级别 | 依赖 | 风险点 | 降级方案 |
| :--- | :--- | :--- | :--- | :--- |
| WP-3.1 Map 常驻内存 | **P0** | WP-1.3（见批次 1） | 见批次 1 | 见批次 1 |
| WP-3.2 实体持久化 | P1 | WP-3.1、WP-0.3 | ① `Building` / `Unit` 是只读字段且无 `[JsonConstructor]`，**直接序列化会得到不可反序列化的结构**；② 实体与任务快照不同事务写，会出现"有建筑无任务 / 有任务无建筑" | ① 独立 `*SaveDto` + Mapper（既定方案）；工期紧时只存最小集（建筑：uid/Id/等级/位置/Owner/HP；单位：uid/HP/位置/MP），**不存任务**（任务快照本已独立落盘） |
| WP-3.3 统一存档单元 | P1 | WP-3.2 | — | — |
| WP-3.4 占用权威一致 | P1 | WP-3.1 | 改动会同时影响建造、移动、迷雾与 A*（`IsClear` / `GetOccupantByUId` 语义） | 先只做「进入/离开统一 API + 修僵尸索引」，`MapCell` 槽位结构不动；嵌套（WP-4.9）延后 |
| WP-3.5 移动模型 R1~R7 | P1 | WP-3.4、WP-0.4、WP-1.5、WP-2.2 | — | — |
| WP-3.6 同格战斗 | P1 | WP-3.4、WP-3.5、WP-2.10 | ① "一格一对交战单位"与现有"格子单个 `Occupant`"冲突，需要**成对占用**表示；② 按日结算 + 近战/远程混合易出"隔格也判同格"的边界 bug | ① 先不动 `MapCell` 结构，用"攻击者持目标 uid + 双方共格"的软表示；② 先只实现「近战同格 + 远程隔格」两条最小路径，范围/阻挡留空 |
| WP-3.7 死亡 / 掉落 | P1 | WP-3.6、WP-2.10 | — | — |
| WP-3.8 敌方生成与封锁 | P1 | WP-3.4、WP-3.1、WP-0.4 | — | — |
| WP-3.9 月度结算器 | P1 | WP-1.1、WP-2.7、WP-3.1、WP-2.5 | — | — |
| WP-3.10 赤字减员 | P1 | WP-3.9、WP-3.1 | — | — |

### 批次 4 · 深度机制（14 个 WP）

| WP ID | 新级别 | 依赖 | 风险点 | 降级方案 |
| :--- | :--- | :--- | :--- | :--- |
| WP-4.1 修正器宿主化 | P1 | WP-3.1、WP-2.10 | ① 触及**所有** Modifier 读写点（资源增长、建筑挂载、事件、科技），漏改即静默失效；② 存储由"每玩家一文件"改为"按宿主分桶"会**破坏旧存档** | ① 保持 `ModifierManager.GetValue` 对外签名不变，只在内部加宿主键路由；② 旧档不兼容（Q7 已确认可接受），用 `saveVersion` 显式拒绝 |
| WP-4.2 范围效果 | P1 | WP-4.1、WP-3.1、WP-3.4 | ① 依赖"建筑分类标签"，数据未填时效果静默不生效；② 半径扫描在地图大时是 O(r²) | ① 标签缺失时按"不过滤"处理并输出 warning；② 缓存半径模板（与迷雾共用一套空间工具） |
| WP-4.3 单位标签 + 条件修正 | P1 | WP-3.6、WP-4.1 | "近战 = `AttackRange == 0`"的派生约定与"额外标签"可能歧义（如既近战又带标签） | 条件求值改为「标签优先、派生兜底」；暂不支持复合条件（AND/OR） |
| WP-4.4 修正作用面扩展 | P1 | WP-4.1、WP-2.2、WP-1.5 | 六类作用面（上限/时长/视野/消耗/伤害/速率）逐个接线，容易出现"某个消费点漏读修正" | 分批接线，每批配一条断言测试（例：+10% 上限 → 上限值可观测变化） |
| WP-4.5 产出浮动 | P1 | WP-3.9、WP-4.1、WP-4.2 | — | — |
| WP-4.6 驻扎系统 | P1 | WP-4.1、WP-3.1、WP-3.4 | ① 单位生命周期与建筑绑定，死亡/拆除/易主三条路径都要清理；② 影响范围与"范围修正"耦合易重复计算 | 先只做「产出加成」一条路径，影响范围的其它效果留空 |
| WP-4.7 单位合并 | P1 | WP-3.6、WP-3.4 | — | — |
| WP-4.8 建筑 HP + 易主 | P1 | WP-4.1、WP-3.1、WP-3.4、WP-2.10 | ① 易主要同时迁移「修正器 + 迷雾 + 人口 + 任务」，漏一条即产生"幽灵产出"；② 夺取前提是能攻击建筑（WP-3.6 / E18） | 先只做「HP 字段 + 摧毁移除」，易主用配置开关**默认关闭**，待"可攻击建筑"落地后再启用 |
| WP-4.9 建筑嵌套 | P2 | WP-3.4、WP-2.6 | ① "附属建筑建在区域建筑当中"要求"格内多建筑 + 父子引用"，与"一格一建筑"既约冲突；② 升级保留附属要求升级不销毁实例 | 先只做「数据层父子引用（`HostUId`）+ UI 分组显示」，不改变占用判定 |
| WP-4.10 迷雾服务/状态分离 | P1 | WP-1.3、WP-3.1 | — | — |
| WP-4.11 地形通行解锁 | P1 | WP-3.5、WP-2.1 | — | — |
| WP-4.12 事件暂停 + 决策 | P1 | WP-1.1、WP-2.8、WP-2.10 | — | — |
| WP-4.13 产出归因查询 | P1 | WP-4.1 | — | — |
| WP-4.14 UI 门控 | P2 | WP-4.13、WP-2.1、WP-5.1 | — | — |

### 批次 5 · 表现层与优化（6 个 WP）

| WP ID | 新级别 | 依赖 | 风险点 | 降级方案 |
| :--- | :--- | :--- | :--- | :--- |
| WP-5.1 M1 极简调试层 | P2 | WP-3.9、WP-4.13、WP-2.10 | — | — |
| WP-5.2 修脚手架致命问题 | P2 | WP-5.1 | — | — |
| WP-5.3 地形外观数据驱动 | P2 | WP-0.4、WP-5.1 | — | — |
| WP-5.4 正式表现层（M2） | P2 | WP-5.1、WP-5.3、WP-4.14 | ① 美术资源缺失，"图层化渲染"没有可用的贴图标准；② 交互需要稳定的"意图契约"，而 `PlayerIntent` / `IActionHandler` 尚未定义 | 先做「占位图形 + 最小交互（点选/确认）」，美术与表现风格解耦；意图契约在 WP-5.1 里先落地一版 |
| WP-5.5 迷雾性能 | P2 | WP-4.10 | — | — |
| WP-5.6 地图存档性能 | P2 | WP-3.2、WP-3.3 | — | — |

### 13.z.1 依赖主干图（补全全部依赖边）

```
WP-0.3 ─┬─→ WP-0.2 ─┬─→ （全部验收测试）
        └─→ WP-0.4 ─┴─→ WP-1.4 ─→ WP-1.5 ─→ WP-2.7 ─→ WP-3.9 ─→ WP-3.10
WP-1.1 ─┬─→ WP-1.3 ─┬─→ WP-1.4
        │            ├─→ WP-2.2 ─→ WP-2.3 / WP-2.5 / WP-2.6 / WP-2.9
        │            ├─→ WP-2.10 ─→ WP-3.6 / WP-3.7 / WP-4.8 / WP-4.12 / WP-5.1
        │            └─→ WP-3.1 ─┬─→ WP-2.3 / WP-2.4 / WP-2.5 / WP-2.6 / WP-3.4 / WP-3.9 / WP-4.1 / WP-4.10
        │                        └─→ WP-3.2 ─→ WP-3.3 ─→ WP-5.6
        └─→ WP-1.2（→ M1）
WP-3.4 ─┬─→ WP-3.5 ─→ WP-3.6 ─→ WP-3.7 / WP-4.3 / WP-4.7
        ├─→ WP-3.8
        └─→ WP-4.2 / WP-4.6 / WP-4.9 / WP-4.11
WP-4.1 ─┬─→ WP-4.2 / WP-4.3 / WP-4.4 / WP-4.5 / WP-4.6 / WP-4.8 / WP-4.13
        └─→ WP-4.13 ─→ WP-4.14 / WP-5.1 ─→ WP-5.2 / WP-5.3 / WP-5.4
WP-2.1 ─→ WP-2.6 / WP-2.9 / WP-4.11 / WP-4.14
WP-2.8 ─→ WP-4.12
WP-0.4 ─→ WP-3.5 / WP-3.8 / WP-5.3
```

### 13.z.2 批次重排建议（依赖图暴露的两处）

1. **`WP-3.1`（Map 常驻内存）从批次 3 前移到批次 1 末尾。** 原因：`WP-2.3 / 2.4 / 2.5 / 2.6` 全部依赖它，而 **M0-2 ③④ 没有它必然失败** —— 代码复核：每次操作 `LoadMap` 而存档不存占据物 → 建造/训练完成回调 `_map.GetOccupantByUId(mapId, uid)`（`ConstructionAppService.cs:83`、`UnitsAppService.cs:85`）必抛 `KeyNotFoundException`；`RegisterHousingTask` 中 `cell.AddPopulation(1)` 改的是刚读出的副本且不落盘 → **人口永远为 0**。它又依赖 `WP-1.3`，故不能晚于批次 1。
2. **`WP-3.4`（占用权威一致）应提前到批次 2 之前**，若要让 `WP-2.4`（建造者校验）与 `WP-2.5`（训练落位）具备正确的占位判断；否则这两项只能做"弱校验"。

### 13.z.3 降级后的关键路径

`WP-0.3 ✅ → WP-0.4 ✅ → WP-1.1 ✅ → WP-1.3 ✅ → WP-3.1 ✅ → WP-1.4 ✅ → WP-1.5 ✅ → WP-2.1 / WP-2.7 / WP-2.8 → WP-2.5`（`WP-1.2 ✅` 已并行完成，是 M1 的前置）

（该路径决定"最早能通过 M0-1 与 M0-2 验收"的时间；其余 P1/P2 项均可并行或延后。）

# 14. 实施里程碑与验收标准

| 里程碑 | 组成 | **退出条件（可自动验证）** |
| :--- | :--- | :--- |
| **M0-1 · 无头骨架** | 批次 0 + 批次 1（`WP-0.1~0.4`、`WP-1.1~1.5`） | ① xUnit 中 `clock.Advance(1080)` 后 `CurrentDay == 1080` 且 `DayElapsed` **恰好派发 1080 次**；② 7 张配置表全部解析成功并通过引用完整性校验；③ 三档流速切换后，"每 10 日 / 每 30 日"节拍在**游戏日维度**保持不变 |
| **M0-2 · 单玩家可玩** | 批次 2（`WP-2.1~2.10`） | ① **物理树根节点（简单机械直觉）可研究** → 证明 `TECH-07` 死锁解除；② 建成 School 后 6 个月内存出 250 idea/月；③ 营地 90 日建成 → 人口上限与视野生效；④ 工坊训练工人 30 日后单位出现且人口 −1；⑤ 固定种子下 3 年事件触发次数落在期望区间 | **① ✅ WP-2.1；② ✅ WP-2.7（检查 52/52）；③④ ⏳ 待做；⑤ ✅ `WP-2.8`** |
| **M0-3 · 世界可存续** | 批次 3（`WP-3.1~3.10`） | ① 存档 → 读档 → `Advance(360)` 后，建筑/单位/人口/任务/迷雾/时间**逐项等价**；② 工人（M=10）跨平原（5）10 日走 2 格、跨山地（25）需 30 日（与设计示例一致）；③ 同格交战 N 日后一方 HP 归零、单位移除、掉落进池；④ 敌方单位封锁格不可建造 |
| **M1 · 可感知调试版** | `WP-5.1/5.2/5.3` | 人能在地图上看到地形/建筑/单位/资源变化，并手动推进 1 月，用于手感与数值校验 |
| **M2 · 正式表现层** | `WP-5.4` + 美术资源 | 正式 UI 与美术；表现层只消费"查询 / 事件 / 意图"三契约，不改核心 |
| **P1 深度** | 批次 4 全部 | 范围效果、条件修正、经济循环、事件暂停、多玩家隔离全部可用 |

> **v0.3 批次重排（依赖图修正）**：`WP-3.1`（Map 常驻内存）前移至**批次 1 末尾**（它是 M0-2 ③④ 的前置：不做则建造/训练完成回调 `GetOccupantByUId` 抛异常、人口永远为 0）；`WP-3.4`（占用权威一致）前移至**批次 2 之前**（`WP-2.4` / `WP-2.5` 需要正确的占位视图）。详见 §13.z.2。

**M0 与 M2 之间不存在返工**：M0 定的是数据契约（查询/事件/意图），M2 只是消费者；反之若先做 M2，M0 的 8 处核心签名变更会推翻表现层。

> **M0-1 验收状态（v0.3.4）**：①②③ **已全部由无头检查覆盖并通过**（43/43、退出码 0）——
> ① `Advance(1080)` 逐日派发 1080 次且 `CurrentDay == 1080`；
> ② 7 张配置表全部解析成功、启动期校验 **0 error / 0 warning**；
> ③ 「每 10 日 / 每 30 日」节拍在三档流速下**用真实配置值**验证一致（移动 10 日 → 108 次/1080 日、月结 30 日 → 36 次/1080 日）。
> 复现命令见 §18.2。

---

# 15. 配置表字段增量规格（策划写表指南）

| 表 | 现有字段 | **需新增 / 修改** |
| :--- | :--- | :--- |
| `Resources` | Name / Description / GrowInterval / BaseGrowth / BaseValue / BaseLimit / DependentModifiers | `GrowInterval = 30`（日）；Id 规范（`Idea` / `Food` / `BasicMinerals`，显示名可中文）；新增 `ProductionVariance`（默认 0.2）；预留 `ShareLimitGroup`（矿石分级） |
| `Buildings` | BuildingId / Name / ResourceCost / TerrainRequirements / TechRequirements / Modifiers / Duration / Actions / VisionRadius / IsHousing / Population* | 新增 `Category`、`PrerequisiteBuildings`、`UpgradeTo`、`UpgradeTechRequirements`、`UpgradeCost`、`UpgradeDuration`、`TrainableUnits`、`ModifierScope{Radius,FilterTags}`、`HasHP`、`MaxHP`、`IsVictoryCritical`；`Duration` 改"日"；`Modifiers` 支持宿主/条件/口径 |
| `Units` | UnitId / ResourceCost / TerrainRequirements / TechRequirements / Duration / HP / Attack / Movement / Actions / VisionRadius / MoveRechargePerTick / AttackRadius / AttackDamage / PopulationCost | 重命名为设计口径：`HP` / `MaxHP` / `ATK` / `AttackRange` / `MP`（每 10 日回复量）/ `VisionRadius` / `InfluenceRadius` / `Maintenance` / `Tags` / `TargetPriority` / `ConditionalModifiers` / `TrainBuildings` / `Loot`（敌方）/ `SpawnTable`（敌方）；`Duration` 改"日"；**删除** `Attack`（与 ATK 重复）与 `Movement`（与 MP 重复） |
| `TechTrees` | Id / Prerequisites(List&lt;string&gt;) / Cost(float) / Duration / Modifiers | `Prerequisites` → `[{TreeId, NodeId}]`（✅ v0.3.5 / WP-2.1 已实现：领域类型 `TechPrerequisite`，JSON 兼容 `"nodeId"` / `"treeId:nodeId"` / 结构体三种写法，旧表不改；`Concurrency`/`Effects`/`ResourceCost` 仍未做）；`Cost` → `ResourceCost`（结构统一，数值不变即全 idea）；`Duration` 改"日"；新增树级 `Concurrency`；`Effects` 区分"修正"与"解锁"（解锁仅 UI 文案，**权威源在建筑表**） |
| `Events` | EventId / Name / Description / TriggerChance / Duration / Modifiers / ResourcePrerequisites / TechPrerequisites | 根对象改为 `{"Events":[…]}`；`TriggerChance` → `TriggerChancePerDay`（✅ v0.3.7 / WP-2.8 已实现，旧名不静默迁移、校验器提示）；`Duration` 改"日"（0 = 永久）；新增 `OneTimeEffects`（一次性资源结算）；**删除**"分类"列 |
| `Terrains`（**新 JSON 表**） | — | `Id` / `Name` / `MoveCost` / `Passable` / `UnlockTech` / `Weight` / `Sprite` / `Color`；量级：平原 5 / 沙漠 8 / 森林 10 / 山地 25 / 水域 50（需解锁） |
| `MapGenerator` | Density | 新增敌方刷新相关（或直接由单位表 `SpawnTable` 与地形概率决定） |

**写表口径三条铁律**（避免再出现"填了不生效"）：
1. **时长/节拍一律写"游戏日"**（建造/研究/训练/事件时长、人口增长间隔、资源结算间隔）
2. **产量一律是"每月"**：`Modifier.Absolute` = +N/月；`Percent` = ×(1+p)；年产量先除以 12
3. **Modifier 的 `Target` 名必须来自统一登记表**（`IdeaGrowth` / `FoodGrowth` / `MineralGrowth` / `PopulationGrowth` / `BuildingSpeed` / `UnitTrainingSpeed` / `UnitAttack` / `UnitSpeed` / `VisionRadius` / `ResourceLimit` / `EnemyAttack` / `EnemyDefense` / `BuildingDamage`…），拼写不符即启动期报错（见 `WP-1.4`）

---

# 16. 装配方案（分层组合根 + 引擎耦合点收敛）

## 16.1 引擎耦合点实测（98 个 .cs 中）

| 统计 | 结果 |
| :--- | :--- |
| 引用 `using Godot` 的文件 | **仅 11 个** |
| 调用 Godot API（`FileAccess`/`ResourceLoader`/`DirAccess`/`GD.Print`）的文件 | **仅 4 个**（`GodotConfigService`、`GodotMapRepository`、`DevMapUi`、`MapView`） |
| 继承 Godot 类型的类型 | **8 个**：`ServiceContainer:Node`、`GodotTimeService:Node`、`TerrainConfigResources:Resource`、`GeneratorConfigResources:Resource`、`MapView:Node2D`、`MapCellView:Node2D`、`DevMapUi:CanvasLayer`、`CameraController:Camera2D` |

→ **`Domain` + `Application` 两层已是 100% 纯 C#**（唯一例外是 `ITaskRepository.cs:1` 的无用 `using Godot`，由 `WP-0.1` 删除）。"先写非 Godot 核心"在代码层面已具备条件。

## 16.2 分层装配（结论：**方案 1** 已采纳）

```
[Core 组装层] 纯 C#，可在 xUnit 中直接调用
  CoreBootstrap.Build(CoreDependencies) → GameSession
  CoreDependencies = { IFileSystem, IConfigSource, ITimeDriver, IRandom }
        ▲ 由 Godot 层注入
[Godot 适配层] 薄（ServiceContainer 仅持有引用 + 暴露只读属性）
  GodotFileSystem · GodotJsonConfigSource · GodotTimeDriver(:Node → clock.Advance)
        ▲ 表现层只依赖三契约
[Presentation] ① 查询契约（只读视图） ② 事件流（订阅） ③ 意图入口（结构化请求）
```

**装配顺序（依赖近乎全连通，必须显式固定）**：
1. 基础设施 `IFileSystem` → `IConfigSource`
2. 配置仓库 ×7（Resources / Buildings / Units / TechTrees / Events / **Terrains** / Generator）
3. 时间 `GameClock`（纯 C#，可手动 `Advance(days)`）
4. 静态定义 `IMapGenerator`（改依赖 `ITerrainConfigSource`）
5. 会话状态 `MapSession` + 各玩家 `ResourcesPool` / `TechTree` / `ModifierStore` / `FogState`
6. 领域服务 `BuildingFactory` / `UnitFactory` / `ModifierService` / `TaskRegistry` / `MonthlySettlementService`
7. 应用服务 Map（拆 Query/Command）/ Construction / Units / TechTrees / Events / Fog（无状态化）
8. 编排 `DomainEventBus` +（可选）`IntentRouter`
9. Godot 适配 `GodotTimeDriver` / `GodotFileSystem` / `ServiceContainer`

**Session 方案（与 `DEP-03`、`MAP-01`、`FOG-01` 同一改造工程）**：`GameSession` 持有"地图集合 + 玩家集合 + 各玩家子系统状态"，应用服务按会话/玩家作用域构造（`new ConstructionAppService(session, player)`）。它一次解决：多玩家隔离、Modifier 归属迁移（易主）、存档边界。

## 16.3 表现层策略

| 现状 | 处理 |
| :--- | :--- |
| `MapView` 依赖未注入（必 NRE） | `WP-5.2` 修一次注入，**冻结功能**，M2 重写 |
| `MapCellView` 按 `res://Texture/Terrain/{Id}.png` 拼路径 + 六边形尺寸硬编码（`MapCellView.cs:15-16,32`） | `WP-5.3` 改为数据驱动（地形表加 `Sprite`/`Color`），缺美术用纯色占位 |
| `DevMapUi` 生成器调试面板 | 保留为调试工具；M1 由 `DebugHud` 取代玩法调试 |
| `CameraController` 未被任何场景引用 | `WP-5.2` 接线或删除 |
| 美术资源（仅 `Texture/Terrain/test1~5.png`） | M2 前不投入；图层数据契约（地形/建筑/单位/迷雾 + 可见性）在 M0 阶段定好 |

---

# 17. 既有条目修订与统计变更

## 17.1 级别与内容修订

| 条目 | v0.1 | v0.2 | 修订原因 |
| :--- | :--- | :--- | :--- |
| `TIME-05` | P1 | **P0** | 口径已锁定（日为基础 / 30 日·月 / 360 日·年 / 三档 1·3·6 日每真实秒 / 暂停），是一切节拍的前提 |
| `DEP-07` | P2 | **P0（前置）** | 无测试工程则 M0 里程碑无法验收（方案 1 的前提） |
| `CON-05` | P2 | **P1** | 人口不落盘会直接阻断 M0-3 的"存档等价"验收 |
| `TECH-02` | P1 | **P2** | 设计确认科技消耗本身就是纯 idea，仅建议结构统一 |
| `TIME-06` | P2 | **并入 `TIME-13`** | 同一口径问题（秒 → 日） |
| `UNIT-02` | P1 | **并入 `UNIT-10`** | MP 上限压死是该模型未实现的一种表现 |
| `UNIT-03` | P1 | **并入 `UNIT-10`** | 移动节拍失衡同源 |
| `MAP-05` / `MAP-06` | P1 | P1（内容修订） | 地形**改为 JSON**；量级改为 5/8/10/25/50；补齐 5 类地形与水域解锁规则 |
| `MAP-12` | P2 | P2（内容修订） | 追加设计侧证据：区域建筑+附属建筑嵌套（仅"日晷"1 条附属建筑）→ `WP-4.9` |
| `MOD-07` | P2 | P2（工作量下修） | `ModifierValue.SourceId` 已具备归因能力，仅缺查询 API → `WP-4.13` |
| `UNIT-05` | P1 | P1（交叉引用） | 由 `WP-3.4` 与 `B3` 一并解决（占用权威一致） |
| `TIME-02/03/09` | — | 交叉引用 | 统一标注"由 `WP-2.2` 一并修复（UId 键 + 完成即回收 + 范围注销）" |
| 第 7 节 `A~E` 阶段路线图 | — | **已被第 13 节的改造清单取代**（保留为历史记录） | 排期按新批次重排 |
| 第 7 节设计矛盾 `C2/C4/C8` 与文明级指标 | — | **关闭**（设计确认废弃） | 见 §10.5 |
| 第 8.3 节 | — | **标记完成**（二/三/四阶段已完成，见第 10~17 节） | — |

## 17.2 统计变更

| 项 | v0.1 | v0.2 |
| :--- | :--- | :--- |
| P0 阻塞级 | 8 | **18** → v0.3 **10**（收敛，见 §17.3） |
| P1 严重级 | 29 | **35** → v0.3 **43**（+8 条由 P0 降级） |
| P2 中等级 | 35 | **37** |
| P3 轻微级 | 7 | **7** |
| **合计** | **79** | **97** |
| 其中已并入他条 | — | 3 条（`TIME-06`、`UNIT-02`、`UNIT-03`） |
| 其中未立条目 | — | 3 项（`MAP-14`、`MAP-15`、`UNIT-15`，见 §12.x） |

**v0.2 新增 21 条**（其中 8 条已在 v0.3 由 P0 降为 P1，见 §17.3）：`TIME-12`、`TIME-13`、`TIME-14`、`TIME-11`、`TECH-07`、`CON-09`、`MAP-16`、`MAP-17`、`UNIT-10`、`UNIT-11`、`UNIT-12`、`UNIT-13`、`UNIT-14`、`UNIT-18`、`UNIT-19`、`EVT-06`、`RES-01`、`RES-02`、`FOG-05`、`FOG-06`、`DEP-11`

### 17.3 P0 收敛判定（v0.3）

判定标准：**不做就无法通过 M0-1 或 M0-2 的自动验收**（M0-1 = Advance(1080) 逐日派发 1080 次 / 7 张配置表可解析并通过校验 / 档位切换后节拍在游戏日维度不变；M0-2 = 物理树根节点可研究 / School 6 个月产出 250 idea / 营地 90 日建成并生效 / 工坊训练工人 30 日且人口 −1 / 3 年事件触发次数落区间）。

**保留 P0（10 条）**

| 问题 ID | 判定依据 | 关联 WP | 状态 |
| :--- | :--- | :--- | :--- |
| TIME-12 | M0-1 ①③ 直接验收 GameClock（Advance / 逐日派发 / 档位） | WP-1.1 | ✅ 已验收 |
| TIME-05 | 与 TIME-12 同一验收面（日口径 + 档位），M0-1 ③ | WP-1.1 / 1.5 | ✅ 已验收 |
| TIME-13 | M0-1 ③「每 10 日 / 每 30 日节拍在游戏日维度不变」 | WP-1.5 | ✅ 已验收 |
| WIRE-01 | 不做则无法构造会话，M0-1 ② 与 M0-2 全项不可执行 | WP-1.3 | ✅ 已验收 |
| WIRE-03 | M0-1 ② 要求 7 张表全部可加载 | WP-0.3 / 1.4 | ✅ 已验收 |
| MAP-17 | M0-1 ② 含 Terrains 表；M0-2 ③ 建造需地形可通行 | WP-0.4 | ✅ 已验收 |
| EVT-01 | 事件表为裸数组，EventsConfigDto 解析必失败 → M0-1 ② 直接不过 | WP-2.8 | ⏳ 待做 |
| DEP-07 | M0-1 ①②③、M0-2 ①~⑤ 的**验收载具**（xUnit + 内存实现） | WP-0.2 | ✅ 已验收 |
| MAP-01 | **代码复核**：每次操作 LoadMap 而存档不存占据物/人口 → 完成回调 GetOccupantByUId 抛 KeyNotFoundException、人口永远为 0 → M0-2 ③④ 不可能通过（需前移） | WP-3.1 | ✅ 已验收 |
| TECH-07 | M0-2 ① 直接验收跨树前置 | WP-2.1 | ✅ 已修复（M0-2 ① 通过） |

**降级 P1（8 条）**：WIRE-02、MAP-02、TIME-01、TIME-02、TIME-14、CON-09、MAP-16、UNIT-10 —— 逐条理由见第 12 节各条目顶部的 v0.3 标注，回归条件见 §18.4。


---

**维护约定**：
- 新增问题沿用 `<模块前缀>-<序号>` 编号（`WIRE`/`MAP`/`TIME`/`MOD`/`CON`/`UNIT`/`TECH`/`EVT`/`FOG`/`RES`/`DEP`），并同步更新第 2 节与第 17.2 节的统计。
- 每条修复完成后，在对应条目末尾追加 `> ✅ 已修复（PR/commit 摘要）`，**不要删除原诊断内容**。
- 改造项（`WP-x.y`）完成后，在第 13 节对应行末追加状态标记；里程碑验收结果写入第 14 节。
- 设计稿相关结论放入第 10 节（规格锁定）并注明来源文档；新发现的设计矛盾追加到 §10.5 的关闭清单。
- 行号引用基于 commit `c6dda9a`；`v0.3.5` 已按**修复后的实际行号**复核 `Scripts/TechTrees/**`、`Scripts/Core/Config/ConfigValidator.cs`、`Scripts/Units/Application/UnitsAppService.cs` 与 `Config/TechTrees.json` 相关的引用（其余文件若仍有偏差，按"就近定位"理解）。

---

# 18. 实施进度（滚动更新）

## 18.1 批次进度

### M0-1（批次 1 · 时间与装配通电）

| 日期 | WP | 状态 | 验证方式 / 产物 |
| :--- | :--- | :--- | :--- |
| 2026-09-14 | WP-0.1 清理死代码与失效配置 | ✅ 完成 | 删除空目录 `Scripts/{Time,Event,Research,Tree}`；移除 `ITaskRepository.cs` 的 `using Godot`；`IRandom.Next` 签名统一为 `(min, max)`；csproj 移除 4 条失效排除项并预置 `Tests\**` 排除。`dotnet build` 通过（warning 4 → 3） |
| 2026-09-14 | WP-0.2 测试工程 + 内存替身 | ✅ 完成（离线降级形态） | `Tests/SciencePotato.HeadlessChecks`（零外部依赖可执行工程）：`Check`（断言/汇总/退出码）、`ClockChecks`、`ConfigChecks`、`TestDoubles`（`InMemoryFileSystem`/`InMemoryConfigSource`/`ManualTimeDriver`）。**12/12 通过，退出码 0** |
| 2026-09-14 | WP-0.3 IO/配置抽象 | ✅ 完成 | 新增 `IFileSystem`、`IConfigSource`（`Common/Domain`）+ `GodotFileSystem`、`GodotJsonConfigSource`（`Common/Infrastructure`）；`ServiceContainer` 完成接线 |
| 2026-09-14 | WP-0.4 地形配置转 JSON | ✅ 完成 | 新增 `ITerrainsConfig`/`TerrainConfigDto`/`TerrainsConfigDto`/`ITerrainConfigRepository`/`TerrainsConfigRepository` + `Config/Terrains.json`（平原5/沙漠8/森林10/山地25/水域50，水域 `Passable=false`+`UnlockTech=buoyancy`）；`VoronoiMapGenerator` 与 `GodotMapRepository` 改为注入；`ITerrainData` 新增 `Passable`/`UnlockTech` 并让两处通行判定改用 `Passable`（消除 `MoveCost == 0` 魔法值） |
| 2026-09-14 | WP-1.1 `GameClock` | ✅ 完成 | 新增 `Scripts/Core/Time/`（`GameClock`/`GameDate`/`TimeSpeedTier`/`ITimeDriver`）：日为基础、30 日/月、360 日/年、三档 1/3/6 日每真实秒、暂停、**逐日派发 `DayElapsed`**、单帧上限 30 日（超出记欠账）。M0-1 ①③ 断言通过 |
| 2026-09-14 | WP-1.2 `GodotTimeDriver` | ✅ 完成 | 新增 `Scripts/Autoload/GodotTimeDriver.cs`（`Node, ITimeDriver`）：**唯一**的"真实秒 → 游戏日"换算点（`_Process → Clock.Advance`），附 `SetSpeedTier`/`SetPaused`/`TogglePause`；`ServiceContainer._Ready` 自动挂为子节点（因此无需改场景与 autoload 列表）；`ITimeDriver.Advance` 改为返回派发日数（与 `GameClock.Advance` 对齐）；删除从未入场景的 `GodotTimeService`（`WIRE-02` 的秒制残源） |
| 2026-09-14 | WP-1.3 `CoreBootstrap` + `GameSession` | ✅ 完成 | 新增 `Scripts/Core/`（`CoreDependencies`/`CoreServices`/`CoreBootstrap`/`GameSession`）：唯一组合根、显式顺序装配、配置缺失快速失败；`ServiceContainer` 薄化为"Godot 适配器 + 调 Bootstrap"；`VoronoiMapGenerator` 生成器配置改为可空（无头回退 `GeneratorConfigDto`）。检查 3 项通过 |
| 2026-09-14 | WP-3.1 Map 常驻内存（前移） | ✅ 完成 | 新增 `Scripts/Map/Domain/MapSession.cs`（内存缓存 + 脏标记 + 存档点 `Flush`）；`MapAppService` 由"每方法 Load/Save"改为走会话缓存（15 处 Load→Get、2 处 Save→MarkDirty、5 处补脏标记），构造函数改为 `(IMapGenerator, MapSession)`。检查 5 项通过（含"占据物/人口跨调用不丢失"） |
| 2026-09-14 | WP-1.4 7 张配置表通电 + 启动期校验 | ✅ 完成 | **7 张表统一为 `Config/{表名}.json`**（Terrains/Resources/Buildings/Units/TechTrees/Events/Generator；`Document/*Config.json` 经 `git mv` 迁入，Events 由裸数组改为 `{ "Events": [...] }`，Generator 新增 JSON 表）；新增 `Scripts/Core/ConfigTables.cs` + `Scripts/Core/Config/`（`ConfigTableLoader`/`ConfigValidator`/`ConfigReport`/`ConfigIssue`/`ModifierTargetRegistry`）：装载"能不能解析"+ 校验"内容对不对"写入同一份报告，**error 阻断启动 / warning 仅提示**（`CoreDependencies.FailOnConfigErrors` 可关）；`ServiceContainer` 启动时把报告打到 Godot 控制台。**顺带修复两处真实缺陷**：`UnitsConfigDto` 缺 `[JsonProperty("Units")]`（单位表原本整表解析为 0 条）、`Events` 表根对象不符。检查 13 项通过（总计 **33/33**） |
| 2026-09-14 | WP-1.5 口径重标定（秒 → 日） | ✅ 完成 | ① **常量单点**：新增 `Scripts/Core/Time/TimeConstants.cs`（日历 + 事件 1 日 / 攻击 1 日 / 移动 10 日 / 月结 30 日 + 单位自检边界 360 日），`EventAppService.cs:39`、`UnitsAppService.cs:145,296` 的 `1f`/`10f` 全部改引常量；② **节拍总线换口径**：新增纯 C# `GameTimeService`（订阅 `GameClock.DayElapsed` → 逐日 `OnTick(1 日)`，倒序遍历以便任务自注销；任务快照改为**日边界**同步，写盘频率降为 1/60），`ITimeService` 删除无人设置的 `Scale`、新增 `CurrentDay`，`ITickable` 注释锚定"日"；③ **配置重标定**：Resources 5/8/0 → **30/30/30**（月结；Idea 靠 Modifier 到账）、Buildings 10/30/20 → 90/120/60 且人口 15 → 300、Units 8/12/15 → **30/35/50**（= 设计 工人/民兵/弓箭手）；④ **单位自检**：`ConfigValidator.ValidateDayUnit` 对 5 张表的时长字段判「>360 日 → warning 疑似秒口径」。检查 10 项通过（总计 **43/43**） |

### M0-2（批次 2 · 单玩家可玩）

| 日期 | WP | 状态 | 验证方式 / 产物 |
| :--- | :--- | :--- | :--- |
| 2026-09-14 | WP-2.1 **跨树前置修复**（`TECH-07` 死锁） | ✅ 完成 | ① **类型**：新增 `TechPrerequisite{TreeId,NodeId}` + `TechPrerequisiteJsonConverter`（兼容层：`"nodeId"` 旧写法 / `"treeId:nodeId"` 紧凑写法 / `{TreeId,NodeId}` 结构体），`ITechNodeConfig.Prerequisites` 由 `List<string>` 改为 `List<TechPrerequisite>`；② **判定**：`TechTree.CanResearch` 逐条走新 `IsPrerequisiteMet`（本树查本树集合、跨树走 `AttachResearchLookup` 注入的只读解析器；未挂解析器 = **fail closed**），新增 `GetPrerequisites`/`AttachResearchLookup`；③ **装配**：`TechTreesAppService.GetOrCreateTechTree` 每次挂解析器（跨树走 `_repo.GetTreeById` **只读**载入兄弟树，不用 `GetOrCreate` 以免"查询"变成"建档"），新增 `CanResearch(mapId,ownerId,treeId,nodeId)`；**顺带修**：`Research` 此前不校验前置 → 会"先扣 Idea、再在完成回调里被 `Tree.Research` 静默丢弃"，现前置未满足直接返回；④ **Hydrate**：`HydrateConfigs` 由"只 hydrate 已存在节点"改为**同时补入表里新增的节点**（否则先有存档、后加表时新科技永远看不见 —— 跨树前置正是"按表增量开放"的模式）；⑤ **填表**：`Config/TechTrees.json` 新增 `science/counting`（设计 0 idea / 0 日，根节点）+ 最小 `physics` 树（`simple_machine_intuition` ← `science:counting`，2000 idea / 30 日），93 节点规模差 → §18.4 债务项；⑥ **校验**：`ConfigValidator` 前置校验升级 —— 跨树前置的**树 Id 与节点 Id 必须存在**（error，`D13` 的升级点）、成环检测改为**跨树统一建边后的全局 DFS**（逐树 DFS 抓不到跨树环），建筑/单位/事件的 `TechRequirements` 仍保持 warning；⑦ **指南**：`ConfigTableGuide` 升 v1.2（三种写法 + 样板 + 附录 C 的 error/warning 清单同步）。检查 **+6 项**（总计 **49/49**、退出码 0），含 **M0-2 ① 门槛**：真实配置下 `physics/simple_machine_intuition` 在「计数」研究前不可研究、研究后可研究、30 日后真正完成、重开存档仍成立 |
| 2026-09-14 | WP-2.7 **建筑产出接线**（建筑 Id 收敛 + Modifier → 月结） | ✅ 完成 | ① **填表**：`Config/Buildings.json` 改为设计稿口径的 4 条 —— `camp`（营地 90 日 / 人口上限 9·半径 1·间隔 300 日 / 视野 1）、`workshop`（工坊 90 日 / 视野 2 / 无产出）、`school`（学院 240 日 / `IdeaGrowth` **+250 Absolute** / `CanResearch`）、`military_camp`（军营 60 日 / 视野 3），旧的 `house`/`library`/`barracks` 全部废弃（并用断言锁死"旧 Id 不存在"）；`Duration` 与人口三件套逐项对齐设计稿"建造时间/效果"列；② **接线**：产出链路本身已由 `ConstructionAppService` 完工回调 → `ModifierAppService` → `ResourcesAppService` 月结（`GrowInterval=30`）构成，本轮**用端到端断言把它钉住**：真实地图 + 真实仓储（临时目录）+ 真实时间总线，施工期 239 日 Idea 恒为 0 → 第 240 日完工 → 之后每 30 日 +250（6 个月恰好 +1500）→ 按 sourceId 回收修正器后立即不再产出；③ **顺带修**：`Map.GetBuildingInfo` 在**空格子**上抛 NRE（与 `GetOccupantInfo` 对齐后返回 null）—— 任何"查询任意格"的调用方都会踩到（`MAP-03` 家族）；④ **固定已知缺陷**：`MAP-04`（建造只写 `cell.Occupant`，`cell.Building` 恒空 → `RemoveBuildingByPosition` 对自建建筑整体失效：既不拆地块也不回收修正器）加了一条"缺陷仍在"的断言，`WP-3.4` 修好时它会失败并提醒改成正向断言；⑤ **指南**：`ConfigTableGuide` 升 v1.3（Id 口径 + 设计数值来源 + 产出写法）。检查 **+3 项**（总计 **52/52**、退出码 0），**M0-2 ② 通过** |
| 2026-09-14 | WP-2.8 **事件按日掷骰 + 字段改名**（`TriggerChancePerDay`） | ✅ 完成 | ① **字段**：`TriggerChance` → `TriggerChancePerDay`（口径 = design/events.md 的「%/日」，0.2%/日 = 0.002），表/接口/DTO/校验器/指南同步；**不做静默迁移** —— 旧表会静默变 0（事件永不触发），故校验器对 0 值给出"请改名"提示（并新增"旧名提示"断言）；② **引擎重写**：持续期由"再注册一个 LinearTask"改为 `ActiveEvent` 逐日倒计时（触发日起算、共 Duration 日、0 = 永久），新增 `GetActiveEvents`/`GetTriggerCounts`/`RollCount`；**生效中不再重复触发**（`G8`）；`StartEventsEngine` 幂等（`EVT-03`）；③ **验收**：**M0-2 ⑤** 固定种子 3 年（1080 日）触发次数落在 均值±3.5σ 且两次运行完全一致（实测 gold_rush=2 / plague=2）；另加"每日恰好一次判定"（`RollCount == 1080`）、"5%/日 的实际频率 ∈ [3.5%,6.5%]"、"资源/科技前置门控"、"到期回收 + 永久只触发一次"四条断言；④ **发现**：`D25`（消耗不落盘）也让**事件前置资源反复可用**（扣了等于没扣），用例中已注明并挂 §18.4。检查 **+6 项**（总计 **58/58**、退出码 0），**M0-2 ⑤ 通过** |

### 18.1.1 里程碑验收对照

| 里程碑 | 验收项 | 状态 |
| :--- | :--- | :--- |
| M0-2 | ① 物理树根节点（简单机械直觉）可研究 | ✅ `WP-2.1`（检查 49/49） |
| M0-2 | ② 建成 School 后 6 个月内存出 250 idea/月 | ✅ `WP-2.7`（检查 52/52） |
| M0-2 | ③ 营地 90 日建成 → 人口上限与视野生效 | ⏳ `WP-2.3` |
| M0-2 | ④ 工坊训练工人 30 日后单位出现且人口 −1 | ⏳ `WP-2.5` |
| M0-2 | ⑤ 固定种子下 3 年事件触发次数落在期望区间 | ✅ `WP-2.8`（检查 58/58） |

## 18.2 复现命令

```powershell
# 主工程构建（Godot 侧）
dotnet build 'Science Potato.csproj'

# M0-1 无头验收（零外部依赖；退出码 0 = 全部通过）
dotnet run --project 'Tests\SciencePotato.HeadlessChecks\SciencePotato.HeadlessChecks.csproj'
```

当前验收结果（2026-09-14，**58/58 通过、退出码 0**）：M0-1 组 43 项 + M0-2 组 15 项。

| 分组 | 数量 | 覆盖 |
| :--- | :--- | :--- |
| 时间与替身（WP-0.2 / WP-1.1） | 9 | 逐日派发 1080 次 / 长跑无漏算（`CurrentDay`/`PendingDays` 收敛）/ 三档在游戏日维度一致 / 「每 10 日」节拍三档均 6 次 / 日期换算 / 暂停冻结 / 单帧上限欠账 / `ManualTimeDriver` / `InMemoryFileSystem` |
| 配置表（WP-0.4 / WP-1.4） | 16 | Terrains 可解析·量级·未知 Id 返回 null / 7 张表文件齐全 / 7 张表全部通电（`LoadedCount=7`）/ 真实配置 **0 error** / Target 登记表覆盖派生名 / 非法 `Modifier.Type` 判 error / 科技前置同表闭合（缺失·自环·成环）/ key≠Id 判 error / 空表与缺表判 error / 跨表引用分级（未知树 error、未知节点 warning）/ 事件根对象拒绝裸数组 / `FailOnConfigErrors=false` 放行并取回报告 / 全表视图与 JSON 条目数一致 / 报告行可读 |
| 组合根（WP-1.3） | 3 | 装配后可生成地图且常驻内存 / 配置缺失快速失败（消息指明表名）/ 会话时钟可推进 |
| Map 常驻内存（WP-3.1） | 5 | 只读盘一次 / 占据物跨调用不丢失（可按 uid 取回）/ 人口不被读盘重置 / `Flush` 只写脏地图 / 落盘后新会话仍读不到占据物（WP-3.2 待补） |
| 口径重标定（WP-1.5 / WP-1.2） | 10 | 节拍常量单点（事件 1 日 / 攻击 1 日 / 移动 10 日 / 月结 30 日）/ **一帧跨多日仍逐日派发**（1080 日 → 1080 次）/ 不足一日不提前触发 / **真实 Resources 表 `GrowInterval=30` → 1080 日结算 36 次**（Idea 即使 `BaseGrowth=0` 也照常结算）/ 三档 1·3·6 日每真实秒的月结次数一致 / 暂停不派发 / 移动 108 次·攻击 1080 次 / 真实 5 张表时长全落 [1,360] 日且无口径警告 / 秒值残留（600）只 warning 不阻断 / 组合根 + `ManualTimeDriver` 推进 1 个月触发 1 次月结 |
| 跨树前置（WP-2.1） | 6 | 三种前置写法（本树 / `tree:node` / 结构体）都能解析且写出口径一致 / 跨树前置 **fail closed**（未挂解析器不可研究）且**同树前置不走跨树查询** / **M0-2 ①**：真实配置下「计数」研究前物理树根节点不可研究 → 研究后可研究 → 30 日后完成 → 重开存档仍成立 / 存档后**新增**节点可被 `HydrateConfigs` 带出 / 校验器对跨树前置缺树·缺节点·跨树成环判 error 且合法跨树不误报 / 真实配置 warning 白名单（仅 `science/counting` 的 0 日，防"白名单空转"） |
| 建筑产出（WP-2.7） | 3 | 建筑表 4 条与设计口径一致（Id/时间/人口/School 250 idea/月）/ 旧原型 Id 已不存在 / **M0-2 ②**：施工 239 日 Idea 恒 0 → 240 日完工 → 每 30 日 +250（6 个月 +1500）→ 回收修正器即停（并固定 `MAP-04` 拆除失效） |
| 事件引擎（WP-2.8） | 6 | `TriggerChancePerDay` 与设计 %/日 一致且旧名有提示 / **M0-2 ⑤**：固定种子 3 年触发次数落区间且可复现 / 每日恰好一次判定（`RollCount==1080`）/ 5%/日 频率 ∈ [3.5%,6.5%] / 资源与科技前置门控 / 到期回收·永久只触发一次·生效中不重复触发·引擎幂等 |



## 18.3 实施期决策与偏差记录

| # | 决策 / 偏差 | 说明 |
| :--- | :--- | :--- |
| D1 | **测试框架降级**：离线 NuGet 缓存无 xunit/nunit/mstest | 改用零依赖的可执行验收工程（结构化断言 + 退出码），验收职责等价；具备网络条件后可平移为 xUnit（`Check` 的断言签名与 xUnit 一致） |
| D2 | `GameClock.MaxDaysPerAdvance` 为**实例属性**（默认 30，取自 `DefaultMaxDaysPerAdvance`） | 生产保留防卡顿上限；验收测试可调高到 `int.MaxValue` 以复现"一次推进 1080 日" |
| D3 | `AdvanceDays(0)` 语义 = **只派发欠账** | 便于"超出单帧上限后补齐"的可测试性；负数不累加、仅派发 |
| D4 | 地形移出 `.tres` 后，`Config/Terrains/*.tres` 保留但**不再参与运行时** | 旧原型地形 Id（`test1~test5`）解析返回 `null` 并输出 `GD.PushWarning`；`GodotMapRepository.SaveMap` 已加空地形保护（顺带缓解 `MAP-09`） |
| D5 | `ITerrainData` 新增 `Passable`/`UnlockTech`，通行判定改用 `Passable` | 消除 `MoveCost == 0 即不可通行` 的魔法值语义；水域默认不可通行（解锁交由 `WP-4.11`） |
| D6 | 表现层未接线（`MapCellView` 仍按 `{TerrainId}.png` 找图） | 新地形 Id（`plain` 等）暂无贴图 → 该路径返回 null 贴图并输出错误，不影响核心；外观数据驱动由 `WP-5.3` 处理 |
| D7 | 组合根入参改为 **`MapRepositoryFactory`**（`Func<ITerrainConfigRepository, IMapRepository>`）而非直接注入仓库实例 | 地图仓库需要地形配置来解析地形 Id，而地形配置由组合根本身产出 → 直接注入构成构造循环；工厂是打断循环的最小手段 |
| D8 | `MapAppService` 构造函数由 `(IMapGenerator, IMapRepository)` 改为 `(IMapGenerator, MapSession)` | 会话缓存承担读写边界，应用服务不再直接触仓储（更贴近"Map 是聚合根"的既定设计） |
| D9 | `VoronoiMapGenerator` 的生成器配置**可空**（`IConfigLoader` 传 null → 回退 `GeneratorConfigDto`，Density=4） | 让核心可无头运行；Godot 侧仍读 `Config/Generator/Generator.tres` |
| D10 | **待办风险**：`Config/*.json` 必须在 Godot 导出设置中加入"非资源文件过滤"（`*.json`） | 否则导出包内不含 JSON → 启动期 `CoreBootstrap` 快速失败；编辑器内运行不受影响（`WP-1.4` / M1 处理）。⚠️ 本批次复查：仓库内暂无 `export_presets.cfg`，导出前必须新建预设并勾选该过滤 |
| D11 | 配置表**位置与命名统一**为 `Config/{表名}.json`（`Terrains`/`Resources`/`Buildings`/`Units`/`TechTrees`/`Events`/`Generator`） | 原 `Document/*Config.json` 经 `git mv` 迁入（保留历史），文件名去 `Config` 后缀以与 `IConfigSource.LoadText(name)` 的 `<name>.json` 约定对齐；`Document/ConfigTableGuide.txt` 同步（文件名 + 新增附录 C 校验清单）；仍支持 `user://Config/{表名}.json` 覆写 |
| D12 | 校验**分级**：error 阻断启动、warning 只提示；`CoreDependencies.FailOnConfigErrors`（默认 true）可关闭 | §13.z 风险 3 的落地：原型数据与设计规模差距大，若一上来就全量强校验则 M0-1 ② 必然失败。error 清单/ warning 清单写入指南附录 C；测试用 `false` 取完整报告而不必断言异常消息 |
| D13 | 跨表引用完整性**先降级为 warning**，只有两处判 error：同表内前置闭合（科技树内）与"科技树 Id 不存在" | 与 §13.z「引用完整性先只校验同表内闭合」一致；WP-2.1 收敛跨树前置后再升级 |
| D14 | `Modifier.Target` 登记表**由配置派生**：固定名 ∪ `{资源名}Growth` ∪ `{单位ID}Attack`/`{单位ID}HP` | 既覆盖指南附录 B 的命名约定（`GoldGrowth`/`swordsmanAttack`），又能在填表者另造词时报警；`Type` 字面量白名单仅 `Percent`/`Absolute`，其余判 **error**（`ModifierAppService` 会把未知拼写静默当成 Absolute，即 `MOD-05` 的静默降级） |
| D15 | **发现并修复**：`UnitsConfigDto.UnitsData` 缺 `[JsonProperty("Units")]` | 属性名与 JSON 键不匹配 → 单位表**整表静默解析为 0 条**（M0-1 ② 的真实阻塞点，被"表为空"error 抓出）。同时 `Events` 表原为裸数组、与 `EventsConfigDto` 根对象不符 → 改为 `{ "Events": [...] }`；`WP-2.8` 只余 `TriggerChance`→`TriggerChancePerDay` 改名与 DTO 增字段 |
| D16 | Generator 表通电（`Config/Generator.json`），`.tres` 降级为**可选编辑器覆写** | 取值顺序：`.tres`（仅 Godot，存在资源加载器时）→ JSON 表 → 代码默认值（`Density=4`）。无头/CI 走 JSON 表，与其余 6 张表同源；`CoreDependencies.ResourceConfigLoader`/`GeneratorConfigPath` 遂变成纯可选 |
| D17 | 三个配置仓库补**全表视图**：`IBuildingConfigRepository.GetAll`、`IUnitsRepository.GetAll`、`ITechTreesConfigRepository.GetTreeIds` | 启动期校验需要"全表"而非"按 Id 单查"；同时给 `TechTreesConfigRepository.GetTechTreeConfig` 补空值保护（原来 `_techTreesConfig` 为 null 时会 NRE）。单条检索接口保持向后兼容 |
| D18 | **节拍总线由"帧"改为"日"**：`ITickable.OnTick(delta)` 的 `delta` 单位 = 游戏日，由新类 `GameTimeService` 订阅 `GameClock.DayElapsed` 逐日派发 `OnTick(1)` | §13.z 风险 4 的落地：若沿用"一帧一次 tick"，第三档（6 日/真实秒 × 60FPS）一帧跨多日会让"每日掷骰 / 月结"漏算（`A3`/`C12`）。逐日派发保证无论档位如何，**周期次数只与游戏日数有关**（M0-1 ③ 与 M0-2 ⑤ 的前提）。总线放在 `Scripts/Core/`（纯 C#），因此无头测试可精确驱动；Godot 侧只剩 `GodotTimeDriver.Advance(realDelta)` |
| D19 | `ITimeService.Scale` **删除**（新增 `CurrentDay`）；`GodotTimeService`（`Node`）**删除** | `Scale` 从无任何调用方设置（`WIRE-02`），其语义已被 `GameClock.Speed`（三档 + 暂停）取代；`GodotTimeService` 既未入场景树又是秒制残留，保留它只会让"口径回归秒"再次可能。旧类型的职责被拆成 `GameTimeService`（纯逻辑）+ `GodotTimeDriver`（引擎适配），与 `GodotFileSystem`/`GodotJsonConfigSource` 的既有分工一致 |
| D20 | 配置时长字段**是否改数值**取决于"旧秒值是否落在日的合理区间"：Resources/Buildings/Units **必须改**（5/8/0、10/30/20、8/12/15 在日口径下分别太小/与设计不符），TechTrees/Events **不改**（20/40/60/15/30 与 30/20/0 恰好落在 0~180 与 5~30 的设计区间内） | 口径重标定是**语义**变更，不是"批量乘系数"：能对齐设计稿的都按设计稿填（工人 30 日、民兵 35 日、弓箭手 50 日、营地人口 300 日），否则会引入无法解释的魔数。TechTrees/Events 的数值不改，但**由校验器的单位自检锁死口径**（>360 → warning），避免以后被再次误填成秒 |
| D21 | `ResourcesAppService.StartGrowthTasks` 的跳过条件由 `GrowInterval <= 0 || BaseGrowth <= 0` 改为**只看 `GrowInterval`** | 原条件让 `BaseGrowth=0` 的资源永不建结算任务 —— 而 Idea 的正确形态正是"基础产出 0 + 全靠建筑/科技 Modifier"（`GrowInterval=30`）。不改这一行，M0-2 ② 的"School 250 idea/月"永远无法到账（Modifier 无处结算），且 `GrowInterval=0` 仍保持"不自动结算"的旧语义 |
| D22 | 任务快照落盘口径：由"每帧每任务写 JSON"改为**游戏日边界**同步（写盘频率 ≈ 1/60），完整"统一存档点"仍留给 `WP-3.3` | `A8`/`TIME-08` 的中间形态：本批次先把口径与派发打通，不动存档架构（避免与 `WP-3.2/3.3` 的实体存档 DTO 冲突）。`GameTimeService` 的 `ITaskRepository` 参数是可选的（无头测试传 null），`WP-3.3` 接入时无需再改调用方 |
| D23 | **`UNIT-10` 记作"显式缺陷"而非"数值偏差"**：`MoveTick` 把 MP 上限写成了单次恢复量（`Scripts/Units/Application/UnitsAppService.cs:158`：`CurrentMP = Math.Min(CurrentMP + MoveRechargePerTick, MoveRechargePerTick)`），而单位表的 `MoveRechargePerTick` = 3 / 2 / 1.5 **全部小于最小地形消耗 5** → 三支部队**当前完全无法移动**（不是"走得慢"） | M0-1/M0-2 的验收项都不涉及移动（M0-2 ④ 只要求"单位出现"），故本轮**不改行为**，只在日志里显式标注缺陷等级：避免 M1 手测时把"单位不动"误判成手感问题或新引入的 bug。修复归 `WP-3.5`（R1~R7 重写 + 地形消耗/通行权限），字段收敛（删 `Attack`/`Movement`、`MovementPoint` → `MP`）见 §15。回归条件见 §18.4 |
| D24 | 跨树前置的**三层口径**：① 领域层 `TechTree` 只认 `TechPrerequisite{TreeId,NodeId}`，跨树解析靠注入的 `AttachResearchLookup`，**未注入 = fail closed**（跨树前置判未满足，宁可暂时不可研究也不错误放行）；② 应用层 `TechTreesAppService` 用 `_repo.GetTreeById` **只读**载入兄弟树（不用 `GetOrCreateTechTree`，以免"查询能否研究"变成"建档 + 写盘"）；③ 校验层升级：科技树自身的**前置闭合**（含跨树缺树 / 缺节点 / 跨树成环）判 error，而建筑/单位/事件表对科技树的 `TechRequirements` 仍保持 warning | `D13` 的升级点按计划兑现：跨树前置的树 Id 或节点 Id 任一不存在，都等于"该科技永久不可解锁"（正是 `TECH-07` 的成因），因此不能再降级放行；而"别的表引用科技树"不属于科技树内部闭合，维持既有降级口径。只读查询每次都会重载该 owner 的树文件（`GenericJsonRepository.Load` 的语义），因此"研究完成即 `SaveTree`"是正确性前提（既有行为）；树集合的缓存优化留给 `WP-2.9` |
| D25 | **待办缺陷（本轮发现，未修）**：`ResourcesConsumption.Consume()` 只改内存池、**没有回写**（`Scripts/Resources/Application/ResourcesAppService.cs:46-50` + `Scripts/Resources/Domain/ResourcesConsumption.cs:29-32`）→ 研发/建造扣掉的资源会在下次 `LoadResourcesPool` 时"复活"（无头验收里 `Research` 扣 2000 Idea 后重读池仍是原值） | 不阻断 M0-2 ①（验收只看"科技是否研究完成"），但它会让 M1 里"资源花掉了"看起来生效、实际回滚。归属 `WP-3.2`（实体持久化）/`WP-3.3`（统一存档点）；最小修法是 `Consume()` 后立即 `SaveResources`（届时顺带把 `_repo` 注入消耗契约）。已挂账于 §18.4 |
| D26 | **`Map.GetBuildingInfo` 空格子 NRE（验收时发现，已修）**：`Map.cs:115-121`（修复后）原来 `if (_cells.TryGetValue(...)) return cell.Building.GetInfo();` —— `cell.Building` 为 null 时直接抛 NRE（与同族的 `GetOccupantInfo` 不一致）。无头用例第一次调用它查"学院是否落位"就崩了 | 修复与 `GetOccupantInfo` 对齐：`&& cell.Building != null`，空格子返回 null。这是 `MAP-03` 家族（"宽容 API / 严格 API 混用"）的又一实例：任何"查询任意格"的调用方（UI 高亮、建造合法性、测试）都会踩到。仅补 null 保护，不改 `MAP-03` 的整体收口（仍由 `WP-3.4` 统一安全 API） |
| D27 | **`MAP-04` 被"固定成断言"而不是就地修复**：`Map.AddOccupant` 只写 `cell.Occupant`、`cell.Building` 恒为 null，于是 `RemoveBuildingByPosition` 对 `ConstructionAppService` 自建的建筑**整体失效**（既不拆地块、也不回收它的产出修正器） | 本轮只做 `WP-2.7`（填表 + 产出接线），不动占用模型 —— 占用模型是 `WP-3.4`「地块占用权威一致」的核心工作（要同时决定"一格一占据物 vs 多槽位"并维护 uid 索引）。为了让这条缺陷**不会被悄悄改动/遗忘**，在 `BuildingProductionChecks` 里加了一条"缺陷仍在"的断言（`WP-3.4` 修好时它会失败，提示改成正向断言）。产出侧的"可回收"已用 `RemoveModifiersBySourceId` 单独验证，不依赖失效的拆除路径 |
| D28 | **无头验收必须放开"单帧最多派发 30 日"（`D2`）**：`GameClock.MaxDaysPerAdvance` 默认 30 是给真机防卡顿用的，测试里 `AdvanceDays(239)` 只会派发 30 日、其余记入欠账 —— 症状是"用例跑完但时间只走了 60 日"（本轮 M0-2 ② 因此卡在 `day=60`） | 约定：**凡是一次推进 > 30 日的用例，先 `Session.Clock.MaxDaysPerAdvance = int.MaxValue`**（`WP-1.1` 的 `D2` 已经写明，本轮把它落到用法上）。同时给 `Check.Run` 补了失败时的"第一帧堆栈"输出，避免以后靠猜定位 |
| D29 | **字段改名不做静默迁移**：`TriggerChance` → `TriggerChancePerDay` 后，旧表里的 `TriggerChance` 会被 Newtonsoft 静默忽略 → 概率 0 → 事件永不触发 | 两种做法：① 兼容层把旧名映射到新名（静默迁移）；② 不映射，让启动期校验器报"TriggerChancePerDay=0：该事件永远不会触发（若旧表仍写 TriggerChance，请改名）"。取 ②：迁移会掩盖"表没更新"这一事实，而原型期最怕静默降级（`MOD-05` 同类教训）；改名只影响 3 条样例，代价极低 |
| D30 | **事件持续期口径 = 触发当日起算，共 `Duration` 个游戏日**：倒计时在**当日结算之后**推进（`AdvanceActiveEvents` 放在 `TickEvents` 末尾），因此 `Duration=10` 覆盖第 1~10 日、第 11 日才重新触发 | 首次实现把推进放在掷骰之前，导致"触发当天就被扣 1 天"的错位；两者在同一天里必须明确先后。另：事件的"生效中状态 + 触发计数"**只在内存**（`EventAppService` 字段）→ 读档即丢，归 `WP-3.2` |


## 18.4 降级项与未修缺陷回归看板（v0.3.5 新增）

**用途**：v0.3 把 8 条 P0 降为 P1（判定见 §17.3），本轮又新增 3 类显式待办（`D23` 不可移动 / `D25` 消耗不落盘 / 内容规模差）。这张表的唯一职责是把"**什么条件下必须回到这些项**"写死，避免"降级 = 遗忘"。

### 18.4.1 降级项（P0 → P1）回归条件

| 问题 | 降级理由（摘要） | 回归条件（何时必须做） | 归属 WP | 状态 |
| :--- | :--- | :--- | :--- | :--- |
| `WIRE-02` 引擎耦合点又被绕过 | 调用点已收敛到 `IFileSystem` / `GameTimeService`，无头断言不受影响 | 出现任何"直接 `FileAccess` 读盘 / 用秒计时"的新代码；或 M1 真实运行出现存档路径不一致 | WP-1.2 / 5.x | 部分已修（`GodotTimeDriver`） |
| `MAP-02` 每帧写盘 | 性能/IO 缺陷，不改变 headless 断言的正确性 | M1 手测卡顿/掉帧；或 `WP-3.2` / `WP-3.3` 落地统一存档点时一并收口 | WP-3.2 / 3.3 | 未修 |
| `TIME-01` / `TIME-02` 秒制残留与节拍失衡 | `WP-1.5` 口径重标定 + 校验器单位自检已覆盖断言面 | 校验器报"疑似仍是秒口径"；或 M1 手感与设计明显不符 | WP-1.5 / 2.2 | 部分已修 |
| `TIME-14` 月结未成体系 | `GrowInterval=30` 逐日派发已通过断言，差的只是"结算器汇总 → 需求 → 扣减" | M0-3 ① 月度经济结算器（`WP-3.9`） | WP-3.9 | 未修 |
| `CON-09` 没有建筑升级 | M0-2 ②③④ 只用 lv.I 建筑（School lv.I / 营地 / 工坊 lv.I） | 科技树里 16 条"解锁 XX 升级"要落地时 | WP-2.6 | 未修 |
| `MAP-16` 建筑 HP 与易主 | 城市攻防属批次 4，M0-1/2/3 验收均不涉及 | 需要"摧毁/夺取建筑"作为胜负条件时 | WP-4.8 | 未修 |
| `UNIT-10` 移动力离散累积模型 | M0-3 ② 才验收移动；**当前单位 100% 不可移动（见 `D23`）** | **`WP-3.5` 开工即修**（M0-3 ② 之前） | WP-3.5 | 未修（显式缺陷） |
| `MAP-17` 地形消耗量级（**已验收**） | Terrains 转 JSON 且量级对齐（平原 5 / 山地 25 / 水域 50） | 移动模型重写（`WP-3.5`）若发现量级不适配 | WP-3.5 | ✅ 已验收（量级可能在 WP-3.5 微调） |

### 18.4.2 新增显式待办（v0.3.5）

| 项 | 内容 | 影响面 | 归属 WP |
| :--- | :--- | :--- | :--- |
| `D23` | 单位**完全不可移动**：MP 上限被压成恢复量，且恢复量（3 / 2 / 1.5）< 最小地形消耗（5） | 移动 / 战斗 / 侦察全部；M0-3 ② 的验收面 | WP-3.5 |
| `D25` | 资源消耗**不落盘**：`ResourcesConsumption.Consume()` 后无 `SaveResources` | 研发/建造的"已花掉"会在下次读盘时回滚 | WP-3.2 / 3.3 |
| 内容规模 ①| 科技树 3 树 **93 节点** vs 当前最小样例 6 节点（military 3 / science 3 / physics 1） | 物理/化学树的 35 / 10 节点在 M1 手测里是空的（但**链路已通**：跨树前置 + 0 成本根节点均已验收） | 填表（批次 4 起，配 `WP-4.14` UI 门控） |
| 内容规模 ②| 建筑 24 条 / 单位 5 类 vs 当前样例 3 / 3 | M0-2 ②~④ 只依赖 School / 营地 / 工坊三条 | `WP-2.5` / `WP-2.6` |
| `MAP-04` 拆除失效（**已固定断言**） | 建造只写 `cell.Occupant`，`cell.Building` 恒空 → 拆除既不拆地块也不回收产出修正器（`D27`） | 玩家无法拆除建筑；建筑升级（`WP-2.6`）与易主（`WP-4.8`）都依赖同一占用模型 | WP-3.4 |
| 资源词汇不一致 | 设计稿用「基础石材 / food / basic minerals」，原型表只有 Gold / Wood / Idea（建筑造价与矿场/农田产出因此无法照抄设计值） | 所有造价与产出的**数值映射**都是近似；`resources.md` 的 4~5 种基础资源落地时需一次性重填 | 填表（批次 4 起，配 `WP-3.9` 结算器） |
| 建筑前置（附属 / 升级）未建模 | 设计稿的"前置"列既有科技也有**建筑**（如 日晷 ← School（建筑）），`IBuildingConfig` 只有 `TechRequirements`，没有"需要已有建筑"的字段 | 日晷、观星台等附属建筑无法表达；建筑升级（`WP-2.6`）也要用到 | `WP-2.6` / `WP-2.12+` |
| `PopulationGrowth` 修正器未接线 | 住房人口由 `ConstructionAppService.RegisterHousingTask` 的 `IntervalTask` 直接 `AddPopulation(1)` 驱动，`PopulationGrowth` target 填了也不会被读（设计稿把营地的人口效果记在 Modifier 列） | 科技/事件想"加快人口增长"无处生效；人口三件套（上限/半径/间隔）目前是**每格**语义，而设计稿是"半径 1 格内总计 9 人" | `WP-2.3`（人口任务）/ 批次 4 修正器宿主化 |
| 小地图只剩一个群系 | 生成器锚点数 = `Density/100 × 面积`（Voronoi），8×8 这种小图常常全图同一种地形（实测某 seed 全 water=不可通行） | M1 手测建议用 ≥24×24；真正的"地图配比"问题归生成器重做 | `WP-5.3` / 生成器 |
| 事件内容规模 | 设计稿 20 条事件 vs 当前样例 3 条（淘金热 / 瘟疫 / 启蒙时代，概率与持续期均取自设计稿） | "按日掷骰 + 前置 + 修正器"链路已通，缺的只是填表与文案 | 填表（批次 4 起，配 `WP-4.12` 决策 UI） |
| 事件状态不落盘 | `EventAppService` 的"生效中事件 + 触发计数"是内存字段，读档即丢；永久事件的修正器也没有"发生过什么"的记录 | 读档后玩家看到数值变了却不知原因（`EVT-04`） | WP-3.2 / WP-3.3 |
| 事件暂停与决策（`EVT-06`） | 事件仍是"触发即结算"的全自动流程，没有暂停 / 选项 / 确认（设计稿要求"事件发生时暂停直到确认"） | 叙事与决策体验缺失；`GameClock.Paused` 已有能力，缺的是待处理队列 + UI | WP-4.12 |

