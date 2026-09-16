# Science Potato · 美术规格（活文档 · v0.9.10）

> **用途**：让"换美术"变成**只改文件 + 只改清单**（不改代码）。这是 `log.md` §19.3 里 M2 判据 ⑤ 的落地依据，
> 也是与美术/外包沟通的唯一口径来源。清单见 `Document/AssetManifest.csv`（**自动生成，勿手改**），
> 逐条交付清单见 `Document/AssetList.md`，验收脚本 `Tools/check_assets.ps1`。
> **硬约束（`R5`）**：任何"美术尺寸/命名/路径"的知识都必须在**配置表或清单**里，不在 `.cs` 里。
>
> **v0.9.10（`WP-8.1`）新增**：§7 七条硬规则 · §8 尺寸与张数总表（189 个文件）· §9 授权与署名（含 AI 生成注意）·
> §10 交付分包 · §11 验收流程 · §12 常见问题 · §13 程序侧已就位的接口。**并把"每格图幅"从 352×406 更正为 366×423**（见 §2）。

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
| 每格图幅（真实美术） | **`366 × 423`**（= 列步长 × 行步长·4/3，六边形**内切满画布**、四角透明） | 本文 §8（v0.9.10 更正） | 平顶六边形：左右尖角贴左右边、上下边贴上下边；建议留 1~2px 出血防拼缝。**旧值 `352×406` 是"略小于步长留拼缝"的早期猜测，已废弃** |

> 改格子尺寸只改这张表；**任何**位置换算都必须调 `MapCellView.LayoutPosition`/`MapView.CellLayoutPosition`，不允许第二份抄写。

## 3. 资源类别与目录约定

| 类别 | 目录 | 命名规则 | 谁读它 |
| :--- | :--- | :--- | :--- |
| 地形 | `Config/Terrains.json` 的 `TerrainSpriteDir`（默认 `res://Texture/Terrain/`） | 每行 `Sprite`（空则用 `Id`）+ `.png` | `MapCellView.SetTerrain` |
| 建筑 | `res://Texture/Building/{建筑Id}.png` | 与 `Config/Buildings.json` 的 `BuildingId` 一致 | `WP-5.3` 起的建筑图层 |
| 单位 | `res://Texture/Unit/{单位Id}.png` | 与 `Config/Units.json` 的 `Id` 一致 | `WP-5.4` 的单位图层 |
| 资源图标 | `res://Texture/Resource/{资源Id}.png` | 与 `Config/Resources.json` 的 `Name` 一致 | 资源面板（`WP-5.4`） |
| 事件插图 | `res://Texture/Event/{事件Id}.png` | 与 `Config/Events.json` 的 `EventId` 一致 | 事件弹窗（`WP-4.12`/`WP-5.4`） |
| 音频 | **见 `Document/SoundSpec.md`（音频规格）与 `Document/SoundList.md`（逐条清单）** | 音频单一套文档，与图分开 | `WP-8.4` |

**命名纪律**：文件名 = 配置表的 Id，**不加前后缀、不做本地化**（中文名进 i18n 的字符串表，不进文件名）。

## 4. 缺资源时的行为（必须成立）

| 情形 | 期望行为 | 现状 |
| :--- | :--- | :--- |
| 缺地形贴图 | 只警告一次，格子按配置 `Color` 画**纯色六边形**占位（不崩、不中断整图） | ✅ 已实现（`WP-5.3` + v0.6.7 六边形；`AppearanceChecks`/`ContentBuildingChecks` + 冒烟锁住） |
| `Color` 缺失或非法 | 按地形 Id 派生**稳定**颜色（同 Id 同色、不同 Id 不同色） | ✅ 已实现（`WP-5.3`） |
| 未设置 `CellScene` | 只警告，跳过渲染 | ✅ 已实现（`WP-5.2`） |
| 缺建筑/单位图 | 用彩色小六边形占位（按 `OwnerId` 上色：己方暖/敌方红），贴图存在时自动改用贴图 | ✅ 已实现（`WP-5.4` 占位 + `WP-8.1` 贴图接线：`MapCellView.SetOccupantMarker`，只缩不放大、脚底对齐） |
| 清单与目录不一致 | 不阻断启动；用脚本报差异（缺图 / 尺寸错 / 无透明通道 / 多余文件） | ✅ 已实现（`Tools/check_assets.ps1`；清单本身由 `Tools/gen_asset_manifest.ps1` 从表生成，不会漂移） |

## 5. 占位美术（M1~M2 期间）

在正式美术到位前，一律使用**纯色/几何占位**，并把"还缺什么"登记在 `AssetManifest.csv` 的 `Status` 列（`placeholder` / `final` / `missing`）。
要求：占位也要能分辨（地形至少 4 种可区分的颜色；建筑与单位用不同形状），否则 `N1` 无法判断"能不能看清"。

## 6. 与 WP 的关系

| WP | 负责 | 完成判据 |
| :--- | :--- | :--- |
| `WP-5.3` | 外观数据驱动（配置 `Sprite`/`Color`）+ 清单落地 | 换一张地形图**不改代码**；缺图不崩；缺图时占位是**六边形**（v0.6.7） |
| `WP-8.1` | 本文件 + `AssetManifest.csv` 定稿（尺寸/锚点/授权来源） | 清单每行都有 `Status` 与来源 |
| `WP-8.2` | 按清单替换资源 | 替换后仅重跑冒烟即可，`git diff` 里**没有 `.cs`** |
| `WP-8.4` | 打包与音频 | 导出包能启动并通过冒烟 |

---

# 第二部分（v0.9.10 / `WP-8.1`）：交付方必读的硬规则与清单

## 7. 七条硬规则

| # | 规则 | 为什么 |
| ---: | :--- | :--- |
| 1 | **PNG-32（RGBA8）**，直通 alpha（不要预乘）；不透明处 alpha=255、透明处 alpha=0 | 引擎按 alpha 混合；预乘会出黑边 |
| 2 | **背景真透明**（不是白底/棋盘格）；**图里不要写文字/水印/签名** | 文字一律走 i18n（中英双语），写进图就无法切语言 |
| 3 | **画布尺寸 = §8 的规格值**；做得更大要**等比缩到规格值**，不要自己改画布 | 位置/缩放由代码按画布推算，画布一变全错位 |
| 4 | **锚点**：地形/图标/插图 = Center；**建筑/单位 = 脚底贴画布下边、水平居中** | 同类共用统一基准 ⇒ 换图不"跳位" |
| 5 | **命名 = 配置表里的 Id**（小写+下划线），**路径不得改**：`res://Texture/{类别}/{id}.png` | `AssetList.md` 里逐条给了文件名 |
| 6 | **统一视角/光源**：正俯视偏 30°、光源左上、单位与建筑朝向朝右下 | 多来源混用时，这是"看得出是一套"的关键 |
| 7 | **同类共用画布**（同一建筑 I/II/III 同一画布与同一脚底基准） | 升级是"长高"，不是"跳位" |

## 8. 尺寸与张数总表（合计 **189** 个文件）

| 类别 | 画布 | 锚点 | 张数 | 目录 |
| :--- | :--- | :--- | ---: | :--- |
| Terrain 地形 | **366 × 423** | Center | 5 | `res://Texture/Terrain/` |
| Building 建筑 | 256 × 256 | 脚底居中 | 24 | `res://Texture/Building/` |
| Unit 单位 | 128 × 128 | 脚底居中 | 15 | `res://Texture/Unit/` |
| Resource 资源图标 | 64 × 64 | Center | 3 | `res://Texture/Resource/` |
| Event 事件插图 | 512 × 288 | Center | 20 | `res://Texture/Event/` |
| TechIcon 科技图标 | 96 × 96 | Center | **93** | `res://Texture/Tech/{math\|physics\|chemistry}/` |
| UI | 见 §8.1 | Center | 28（23 必需 + 5 可选） | `res://Texture/UI/` |
| Audio BGM | **见 `Document/SoundSpec.md` / `SoundList.md`**（音频单独一套文档） | — | 1 | `res://Audio/BGM/` |

> **音效（SFX）已单列**：见 `Document/SoundSpec.md` + `Document/SoundList.md`（36 条音效 + 1 首 BGM，`wav` 一次性音效 / `ogg` 循环音乐）。

### 8.1 UI（`res://Texture/UI/`）

| 文件 | 尺寸 | 说明 |
| :--- | :--- | :--- |
| `panel_bg.png` | 96×96 九宫格（四边 24px 可拉伸） | 通用面板底（研究/建造/事件弹窗共用） |
| `button_normal.png` / `button_hover.png` / `button_pressed.png` | 96×96 九宫格（24px） | 按钮三态 |
| `button_icon.png` | 64×64 九宫格（16px） | 图标按钮底 |
| `tab_normal.png` / `tab_active.png` | 64×64 九宫格（16px） | 分类页签 |
| `icon_speed_pause/1x/3x/6x.png` | 48×48 | 时间档位 |
| `icon_population.png` | 48×48 | 人口 |
| `icon_research/build/move/attack/hold.png` | 48×48 | 功能入口与单位指令 |
| `select_hex.png` | 366×423 | 选中格高亮（与地形同几何、半透明描边） |
| `range_hex.png`（可选） | 366×423 | 攻击/视野范围覆盖 |
| `tech_node_frame.png` | 96×96 | 科技节点框（4 态靠颜色调制，不用 4 张） |
| `progress_fill.png` | 32×32 九宫格（8px） | 进度条填充（也可纯色） |
| `main_menu_bg.png` | 1920×1080 | 主菜单背景 |
| `victory_bg.png` / `defeat_bg.png` | 1920×1080 | 结局画面 |
| `logo.png`（可选） | 512×256 | 标题 |
| `cursor_default/build/attack.png`（可选） | 32×32 | 光标三态 |

## 9. 授权与署名（必填）

交付时请在 `Document/AssetManifest.csv` 对应行填 **`Owner`**（作者/来源）与 **`License`**（`CC0` / `CC-BY-4.0` / `自绘` / `AI-<平台>-<日期>`）。
用"需署名"许可的素材，我会汇总到 `Document/Credits.md` —— **许可与出处写清**，否则发布有法律风险。

**AI 生成特别注意**：① 出图尺寸/透明常不规矩 ⇒ 按 §7 规则 1~3 归一化；② 同批图用**同一提示词模板 + 参考图**，否则风格会飘；
③ 记录平台与生成日期（写进 `License` 列）；④ 不要在图里嵌文字/水印/签名。

## 10. 交付分包（每包到位即见效果，缺图不影响运行）

| 包 | 内容 | 数量 |
| :--- | :--- | ---: |
| 01 最小可玩包 | 5 地形 + `camp`/`farm`/`mine` + `worker`/`swordsman` + 3 资源图标 + `panel_bg` + 按钮三态 | 16 |
| 02 建筑全量 | 其余 21 张（含 I/II/III 各级） | 21 |
| 03 单位与敌人 | 其余 13 张 | 13 |
| 04 事件插图 | 20 张 | 20 |
| 05 科技图标 | 93 张（按树分目录） | 93 |
| 06 UI 与结局 | 其余 UI 25 张 | 25 |
| 07 BGM | 1 首（已有则跳过） | 1 |

打包：保持目录结构（`Texture/...`、`Audio/...`）整包 zip 即可。

## 11. 验收流程（我方自动跑）

```powershell
# 报告：缺哪些、哪张尺寸不对、哪张没有透明通道、目录里有没有多余文件
powershell -ExecutionPolicy Bypass -File Tools/check_assets.ps1

# 质检通过后把清单状态从 present 升级为 delivered
powershell -ExecutionPolicy Bypass -File Tools/check_assets.ps1 -Promote

# 清单本身（表里新增了建筑/科技后重生成，避免清单与表漂移）
powershell -ExecutionPolicy Bypass -File Tools/gen_asset_manifest.ps1
```

**回退保证**：缺任何图都不会崩（地形 → 纯色六边形；建筑/单位 → 彩色小六边形；面板 → 纯色），可以**边画边交**。

## 12. 常见问题

| 现象 | 原因 | 处理 |
| :--- | :--- | :--- |
| 图放进去但游戏里没变 | 文件名/目录与 id 不符 | 对照 `AssetList.md` 的"文件名"列；`check_assets.ps1` 会报"多余文件" |
| 提示尺寸不对 | 画布与 §8 不一致 | 改画布或等比缩放到规格值 |
| 图边缘有白/黑边 | 背景不是真透明，或用了预乘 alpha | 导出选"直通 alpha + 透明背景" |
| 建筑/单位互相压住、位置偏高 | 脚底基准不在画布下边 | 把"接触地面线"对齐画布下边（§7 规则 4） |
| `Texture/Terrain/test1.png`~`test5.png` | **历史测试图**（命名不是 id） | 当前**不生效**；正式图请按 `plain/desert/forest/mountain/water` 命名 |

## 13. 程序侧已就位的接口（"丢文件就行"的保证）

- **地形**：`Config/Terrains.json` 的 `Sprite`/`Color` + 步长（366 / 317.25）⇒ 丢文件即生效，不改代码。
- **建筑 / 单位**：**已接线** —— `res://Texture/Building/{id}.png`、`res://Texture/Unit/{id}.png`；有图用图、无图回退占位；
  贴图按"不超出格子"自动缩放（只缩不放大），脚底对齐格内基准线（`MapCellView`）。
- **资源 / 事件 / 科技 / UI**：路径口径已固定并被无头用例锁住（`MapLayerModel.ResourceSpritePath` / `EventSpritePath` /
  `TechSpritePath` / `UiSpritePath` + `PresentationChecks` 的"清单闭合"用例），面板接线完成后自动使用同名文件。
- **图层**：`TerrainLayer` / `OccupantsLayer` 容器已就位；**雾**由格子自身的 `Modulate` 承担（不占贴图）。

