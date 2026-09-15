# Science Potato · 美术规格（活文档 · v0.6.0）

> **用途**：让"换美术"变成**只改文件 + 只改清单**（不改代码）。这是 `log.md` §19.3 里 M2 判据 ⑤ 的落地依据，
> 也是与美术/外包沟通的唯一口径来源。清单见 `Document/AssetManifest.csv`。
> **硬约束（`R5`）**：任何"美术尺寸/命名/路径"的知识都必须在**配置表或清单**里，不在 `.cs` 里。

## 1. 为什么要数据驱动（改造前的问题）

`MapCellView` 原先把六边形尺寸写死在代码里（`height = 366 / width = 423`），贴图路径按 `res://Texture/Terrain/{地形Id}.png` 拼字符串，且**缺图会抛异常**。
后果：换一套美术要改代码 + 改尺寸常量 + 猜文件名；缺一张图会让整张地图渲染中断。
（`WP-5.3` 收口外观数据驱动；`WP-5.2` 已先做"缺图不崩、只警告并占位"的兜底。）

## 2. 六边形格位（网格口径）

| 项 | 值 | 来源 | 备注 |
| :--- | :--- | :--- | :--- |
| 格位 → 世界坐标 | `x = X/2·r − X·⌈r/2⌉ + X·q`，`y = Y·r` | `MapCellView.LayoutPosition`（唯一出处）/ `MapView.CellLayoutPosition` | `X`/`Y` = 配置表的 `CellXStep`/`CellYStep` |
| `CellXStep`（列步长） | **366 px** | `Config/Terrains.json` 表根 | 代码里**没有**这个常量（只有缺配置时的兜底值） |
| `CellYStep`（行步长） | **317.25 px** | `Config/Terrains.json` 表根 | = 原型期 423×3/4，逐像素等价（有用例锁住） |
| 每格图幅（真实美术） | 建议 `352×406`（略小于步长，留 1:1 拼缝） | 待美术确认 | 缺图时占位块按 `CellXStep×CellYStep·4/3` 缩放 |

> 改格子尺寸只改这张表；**任何**位置换算都必须调 `MapCellView.LayoutPosition`/`MapView.CellLayoutPosition`，不允许第二份抄写。

## 3. 资源类别与目录约定

| 类别 | 目录 | 命名规则 | 谁读它 |
| :--- | :--- | :--- | :--- |
| 地形 | `Config/Terrains.json` 的 `TerrainSpriteDir`（默认 `res://Texture/Terrain/`） | 每行 `Sprite`（空则用 `Id`）+ `.png` | `MapCellView.SetTerrain` |
| 建筑 | `res://Texture/Building/{建筑Id}.png` | 与 `Config/Buildings.json` 的 `BuildingId` 一致 | `WP-5.3` 起的建筑图层 |
| 单位 | `res://Texture/Unit/{单位Id}.png` | 与 `Config/Units.json` 的 `Id` 一致 | `WP-5.4` 的单位图层 |
| 资源图标 | `res://Texture/Resource/{资源Id}.png` | 与 `Config/Resources.json` 的 `Name` 一致 | 资源面板（`WP-5.4`） |
| 事件插图 | `res://Texture/Event/{事件Id}.png` | 与 `Config/Events.json` 的 `EventId` 一致 | 事件弹窗（`WP-4.12`/`WP-5.4`） |
| 音频 | `res://Audio/{BGM|SFX}/{文件名}` | 文件名进清单，不参与逻辑 | `WP-8.4` |

**命名纪律**：文件名 = 配置表的 Id，**不加前后缀、不做本地化**（中文名进 i18n 的字符串表，不进文件名）。

## 4. 缺资源时的行为（必须成立）

| 情形 | 期望行为 | 现状 |
| :--- | :--- | :--- |
| 缺地形贴图 | 只警告一次，格子按配置 `Color` **画纯色占位块**（不崩、不中断整图） | ✅ 已实现（`WP-5.3`；`AppearanceChecks` + 冒烟锁住） |
| `Color` 缺失或非法 | 按地形 Id 派生**稳定**颜色（同 Id 同色、不同 Id 不同色） | ✅ 已实现（`WP-5.3`） |
| 未设置 `CellScene` | 只警告，跳过渲染 | ✅ 已实现（`WP-5.2`） |
| 缺建筑/单位图 | 用纯色占位块（按 `OwnerId` 上色），并在清单里标 `missing` | ☐ `WP-5.3` |
| 清单与目录不一致 | 启动时打一条 warning 列出差异（不阻断） | ☐ `WP-5.3` |

## 5. 占位美术（M1~M2 期间）

在正式美术到位前，一律使用**纯色/几何占位**，并把"还缺什么"登记在 `AssetManifest.csv` 的 `Status` 列（`placeholder` / `final` / `missing`）。
要求：占位也要能分辨（地形至少 4 种可区分的颜色；建筑与单位用不同形状），否则 `N1` 无法判断"能不能看清"。

## 6. 与 WP 的关系

| WP | 负责 | 完成判据 |
| :--- | :--- | :--- |
| `WP-5.3` | 外观数据驱动（配置 `Sprite`/`Color`）+ 清单落地 | 换一张地形图**不改代码**；缺图不崩 |
| `WP-8.1` | 本文件 + `AssetManifest.csv` 定稿（尺寸/锚点/授权来源） | 清单每行都有 `Status` 与来源 |
| `WP-8.2` | 按清单替换资源 | 替换后仅重跑冒烟即可，`git diff` 里**没有 `.cs`** |
| `WP-8.4` | 打包与音频 | 导出包能启动并通过冒烟 |
