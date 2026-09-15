# Science Potato · 性能基线（活文档 · v0.6.0）

> **用途**：把"10439 格（73×143）到底要多少时间/内存/磁盘"变成可对比的数字，用来在改生成器、
> 换渲染方式、加 AI、加迷雾时**立刻看出有没有退化**。判据与风险见 `log.md` §19.3/§19.4，验收流程见 `Document/TestPlan.md`。

## 1. 怎么测（可复现）

```powershell
# 推荐：一条命令跑完三层（构建 → 无头检查 4 路并行分片 → Godot 冒烟），结论一行
powershell -ExecutionPolicy Bypass -File Tools/verify.ps1                 # 快档：冒烟 24×24
powershell -ExecutionPolicy Bypass -File Tools/verify.ps1 -FullSmoke      # 全档：冒烟 143×73（本表数字用这一条）
powershell -ExecutionPolicy Bypass -File Tools/verify.ps1 -Serial         # 排查闪烁时串行
$exe = 'E:\Godot_v4.6-stable_mono_win64\Godot_v4.6-stable_mono_win64.exe'
& $exe --headless --path 'e:\Godot\science-potato' res://Scene/Dev/smoke_report.tscn `
       --quit-after 6000 --log-file "$env:TEMP\sp_smoke.log" -- --smoke --size=143x73 --seed=20260914
# 输出里找这一行（可直接与下表对比）：
# [SMOKE][BASE] size=143x73=10439 cells seed=20260914 generate=229ms … memStatic=138.6MB …
```

参数：`--size=WxH`（默认取自 `DevMapUi.DefaultWidth/DefaultHeight` = **143×73**，`D92`）、`--seed=N`（默认 20260914）、`--ai=N`（多势力）、`--smoke`（独立存档，且每次先删旧档保证可复现）。

## 2. 基线（Windows / `--headless` / 本机实测 2026-09-15，v0.6.7）

| 规模 | 格数 | 占位形态 | generate | start | advance90 | save | load | render | worldSave | mapFile | memStatic |
| :--- | ---: | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| **143×73（当前默认）** | 10439 | 六边形 | **229 ms** | 31 ms | 38 ms | 41 ms | 79 ms | **1100 ms** | 3.5 KB | **2.34 MB** | **138.6 MB** |
| 73×143（v0.6.2，竖幅，已弃用） | 10439 | 矩形 | 218 ms | 18 ms | 31 ms | 34 ms | 88 ms | 933 ms | 2.1 KB | 2.4 MB | 86.0 MB |
| 24×24（开发快档） | 576 | 六边形 | ~48 ms | ~17 ms | ~24 ms | ~11 ms | ~17 ms | ~65 ms | — | ~134 KB | ~25 MB |

各列含义：
- `generate` = `MapAppService.GenerateMap`（Voronoi 地形 + 敌方刷新后处理）；
- `start` = `SessionOrchestrator.StartMap`（开局布置 + 每个势力的资源池/月结/事件引擎/资源成长任务登记）；
- `advance90` = `Clock.AdvanceDays(90)`（= 3 次月结 + 90 日事件掷骰 + 资源成长到账）；
- `save` / `load` = `WorldSaveService.SaveWorld` / `LoadWorld`（含地图文件 + 统一存档的原子写/读）；
- `render` = 实例化主场景 `map_view.tscn` 后 `UpdateAllCells()` 建 **10439 个节点**；
- `worldSave` = `user://save/smoke.json`（任务/资源/科技/迷雾/事件/时钟分区）；
- `mapFile` = `user://maps/{mapId}.json`（**地图实体单独一份文件**，`D53`）；
- `memStatic` = `OS.GetStaticMemoryUsage()`（跑完 90 日 + 存档往返 + 渲染之后）。

> 注意 143×73 与 73×143 的内存差异**不是方向造成的**，而是**占位形态**：六边形占位是每格一个 `Polygon2D`
> （+≈5 KB/格），矩形占位只是缩放 1×1 纹理。**有真美术后两者都不存在**（`MapCellView` 只在缺图时才建多边形）。

## 3. 读出来的三条结论（→ 影响后续 WP）

| # | 观测 | 结论 / 动作 |
| :--- | :--- | :--- |
| `B1` | **地图存档 2.4 MB（≈ 235 B/格）远大于统一存档（2.1 KB）** | 地图是磁盘占用的绝对大头 → `WP-5.6`（脏格子增量写 / 紧凑格式）优先级不低；导出/云存档也要按"一图一文件"设计（`D53` 不变） |
| `B2` | **渲染 933~1100 ms + 86~139 MB**：10k 格 = 10k 节点，约 **6~14 KB/格**（六边形占位比矩形占位多 ≈5 KB/格） | 现在能接受（M1 只渲染地形，且真美术到位后占位节点消失）。但 `WP-5.4` 要加建筑/单位/迷雾图层时**不能**继续"一格多节点" → 到 `WP-5.5`/`WP-5.4` 做决定：图块合批 / `MultiMesh` / 只给视野内格子建节点（视口裁剪） |
| `B3` | **逻辑极便宜**：90 日推进 31 ms、生成 218 ms、月结+事件+成长全在毫秒级 | `R2`（AI 开销）的压力不在这条链路上；AI tick 只要**不是每帧全图扫描**就很安全（`WP-6.2` 已定"按游戏日 tick + 只读自己视野"） |

## 4. 护栏（自动判红）与"不设硬阈值"的理由

| 检查 | 阈值 | 位置 |
| :--- | :--- | :--- |
| 生成耗时 | `< 5000 ms` | `SmokeReport.GenerateBudgetMs` |
| 渲染耗时 | `< 15000 ms` | `SmokeReport.RenderBudgetMs` |
| 格数 | `= W×H`（精确） | `SmokeReport` |

阈值**刻意宽松**：只抓"数量级退化"（换引擎版本、生成器改坏、逐格建纹理这类）。CI 机器性能差异会让严格阈值变成假红，而假红会让人开始无视它 —— 精确对比靠本文件的数字，人来判断。

## 5. 尚未覆盖（登记制）

| # | 未覆盖 | 为什么 | 补的时机 |
| :--- | :--- | :--- | :--- |
| 1 | 迷雾矩阵在 10439 格上的耗时/内存 | 迷雾尚未按 owner 拆分 | `WP-4.10` / `WP-5.5` |
| 2 | 建筑/单位图层的渲染规模 | 目前只渲染地形 | `WP-5.4` |
| 3 | AI 决策 tick 开销 | AI 未实现 | `WP-6.2` |
| 4 | 正式贴图（非占位）下的渲染与显存 | 美术未到位 | `WP-8.2` |
| 5 | 多地图同时驻留 / 频繁切换 | `WP-5.6` 的领域 | `WP-5.6` |

## 6. 维护约定

1. **每次改到生成/渲染/存档的 WP** 都要重跑 §1 并更新 §2 的数字（旧数字不保留，避免"两个基线"）。
2. 出现"明显退化"时不要只改基线：先定位原因，把结论写进 `log.md` §20 决策记录。
3. 阈值（§4）只在"确认长期稳定"时才收紧，且收紧要在本文件写明依据。
