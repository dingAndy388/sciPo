# Science Potato · 音频规格（交付方必读）

> **本文档只讲声音**：图看 `Document/ArtSpec.md` + `Document/AssetList.md`；声音看本文 + `Document/SoundList.md`（逐条清单）。
> 两份清单**不重叠**（组内分工不同）；署名统一登记在 `Document/Credits.md`。
> 交付后我方跑 `Tools/check_assets.ps1` 自动验收（缺文件 / 扩展名 / 多余文件），另有响度与时长的人工抽查。
> **清单与代码同源**：`Scripts/Audio/Domain/SoundCatalog.cs` 是 id 的唯一出处，`Tools/gen_sound_list.ps1` 从它生成清单 —— **音频作者不需要看代码，只需要照 `SoundList.md` 的 id 出文件**。

---

## 1. 七条硬规则

| # | 规则 | 为什么 |
| ---: | :--- | :--- |
| 1 | **格式**：音效用 **`.wav`**（44.1 kHz / 16-bit / **单声道优先**）；音乐用 **`.ogg`**（44.1 kHz 立体声、**无缝循环**、60~120 s） | wav 短音效零解码延迟；ogg 体积小、可无缝循环 |
| 2 | **命名 = id**（小写+下划线），**路径不得改**：音效 `res://Audio/SFX/{id}.wav`；音乐 `res://Audio/BGM/{id}.ogg` | id 就是文件名，`SoundList.md` 逐条给了 |
| 3 | **响度**：音效峰值 **≈ -6 dBFS**；音乐 **≈ -14 LUFS**；**不要自己做母带/限幅** | 统一由游戏音量总线控制，否则"有的响有的轻" |
| 4 | **响应干净**：**首尾不留静音**（切头切尾）；UI 0.05~0.3 s · 建造 0.5~1.5 s · 战斗 0.2~0.6 s · 事件 1~2 s · 结算 0.5~1.5 s | 留静音会让操作"感觉延迟" |
| 5 | **不做空间化**：全部 2D 全屏音（无 3D/混响依赖）；同一音效可给 **3 个随机变体**（`unit_attack_01/02/03.wav`），代码随机取一个 | 我们不做 3D 音频；变体避免"复读机"感 |
| 6 | **无版权风险**：采样/素材来源与许可登记进 `Document/Credits.md`（`Owner` / `License` 列）；AI 生成写 `AI-<平台>-<日期>` | 发布合规 |
| 7 | **缺文件不崩**：任何一条缺失都只是"没声音"，不影响玩法与验收 | 可以**边做边交** |

---

## 2. 文件与目录总表（**36 音效 + 1 音乐**）

| 类别 | 目录 | 数量 | P1 必需 | 说明 |
| :--- | :--- | ---: | ---: | :--- |
| UI / 系统 | `res://Audio/SFX/` | 12 | 8 | 点击/取消/拒绝/开局/存档/读档/胜负 + 4 条可选 |
| 建造 / 生产 | `res://Audio/SFX/` | 8 | 8 | 开工/完工/升级/易主/摧毁/训练 |
| 单位 / 战斗 | `res://Audio/SFX/` | 5 | 3 | 移动令/攻击/阵亡 + 受击/掉落（可选） |
| 科技 | `res://Audio/SFX/` | 3 | 2 | 研究开工/完成 + 面板解锁（可选） |
| 事件 | `res://Audio/SFX/` | 3 | 2 | 触发/确定 + 忽略（可选） |
| 资源 / 结算 | `res://Audio/SFX/` | 4 | 1 | 月结 + 3 条可选告警 |
| 环境（可选） | `res://Audio/SFX/` | 1 | 0 | 视野揭开 |
| 音乐 | `res://Audio/BGM/` | 1 | 1 | `main.ogg`（你已有） |

> **最小可玩音效包（9 条）**：`ui_click` · `ui_reject` · `build_start` · `build_complete` · `unit_trained` · `unit_move_order` · `unit_attack` · `research_complete` · `event_trigger`
> 逐条明细（含每条的建议时长与触发点）见 **`Document/SoundList.md`**。

---

## 3. 播放点表：代码里"什么时候响"

音效**不是**音频方决定的，而是由游戏事件触发的。下表就是代码里的映射（出处：`SoundCatalog.SfxForDomainEvent` / `SfxForIntent`），音频方只需照 id 出文件：

| 代码触发点 | 音效 id | 备注 |
| :--- | :--- | :--- |
| 建造完成 `BuildingCompletedEvent` | `build_complete` | |
| 升级完成 `BuildingUpgradedEvent` | `upgrade_complete` | |
| 训练完成 `UnitTrainedEvent` | `unit_trained` | |
| 单位阵亡 `UnitDiedEvent` | `unit_died` | 敌我共用一条 |
| 研究完成 `ResearchCompletedEvent` | `research_complete` | |
| 随机事件触发 `GameEventTriggeredEvent` | `event_trigger` | 触发即暂停，弹窗出现 |
| 建筑易主 `BuildingCapturedEvent` | `building_captured` | |
| 建筑被拆/摧毁 `BuildingRemovedEvent` | `building_destroyed` | |
| 意图被接受（建造/训练/研究/移动/事件决策成功） | `ui_click` | 统一一条点击音 |
| 意图被拒绝（资源/门控/占用/越权） | `ui_reject` | 统一一条拒绝音，**原因由文案给** |
| 月结到账 | `monthly_settle` | 每月一次，别做太响 |
| 开局 / 存读档 / 胜负 | `game_start` / `game_save` / `game_load` / `victory` / `defeat` | 会话入口与胜负判定处 |
| 其余（悬停/面板开合/受击/掉落/告警/视野…） | 见 `SoundList.md` | 均为 P2，可由表现层后续接入 |

---

## 4. 交付与验收

```powershell
# 重新生成清单（音频 id 若有增删，先改代码契约再跑它）
powershell -ExecutionPolicy Bypass -File Tools/gen_sound_list.ps1

# 自动验收（图 + 音一起查）：缺文件 / 扩展名不符 / 目录里多余文件
powershell -ExecutionPolicy Bypass -File Tools/check_assets.ps1
```

**能自动查的**：文件是否存在、扩展名是否 `.wav`/`.ogg`、是否放在约定目录、是否有未登记的"多余文件"。
**需要人工抽查的**：响度（峰值/整体感）、时长是否在建议区间、首尾是否干净、风格是否统一（我们会挑几条听并反馈）。

---

## 5. 授权与署名

- 在 `Document/SoundManifest.csv` 对应行填 **`Owner`**（作者/来源）与 **`License`**（`CC0` / `CC-BY-4.0` / `自绘` / `已购买-<平台>` / `AI-<平台>-<日期>`）。
- 需要署名的素材会在 `Document/Credits.md` 汇总 —— **许可与出处写清**，否则发布有法律风险。
- AI 生成：同 §1 规则 6，并**不要**在音频里嵌人声化的平台水印/签名。

---

## 6. 常见问题

| 现象 | 原因 | 处理 |
| :--- | :--- | :--- |
| 文件放进去但游戏里没响 | 文件名/目录与 id 不符，或播放器还没接（见 §7） | 对照 `SoundList.md`；`check_assets.ps1` 会报"多余文件" |
| 声音忽大忽小 | 每条做了不同程度的后期 | 统一峰值 ≈ -6 dBFS，别自己限幅（§1 规则 3） |
| 操作"感觉慢半拍" | 音频首尾留了静音 | 切头切尾（§1 规则 4） |
| 攻击音像复读机 | 单条反复播 | 给 3 个变体 `unit_attack_01/02/03.wav`（§1 规则 5） |
| BGM 循环处有断点 | 循环点没对齐 | 导出前确认无缝循环（首尾采样连续），Godot 侧会设循环播放 |

---

## 7. 代码侧现状与分工（"适配没问题"的依据）

| 项 | 状态 |
| :--- | :--- |
| **音效契约**（id / 路径 / 事件→音效映射） | ✅ **已就位**：`Scripts/Audio/Domain/SoundCatalog.cs`，并由无头用例 `SoundChecks`（5 条）锁住：id 形态 · 路径口径 · **8 类领域事件全覆盖** · 清单与代码 id 闭合 · 缺文件静默 |
| **清单与工具** | ✅ `Tools/gen_sound_list.ps1` → `Document/SoundManifest.csv` + `Document/SoundList.md` |
| **播放器**（`AudioAppService`：AudioStreamPlayer 池 + 订阅事件 + Master/BGM/SFX 音量总线 + 同帧去重） | 🟡 **属 `WP-8.4`，等首批音频到位后接线**（约一次小改动）。不提前写的原因：没有真实音频文件时它无法在冒烟里"听得见"，写了也验不了 |
| **BGM 接入** | 🟡 同上：文件放 `res://Audio/BGM/main.ogg`，接线时设循环与暂停淡出 |

**一句话**：**对接面（id/路径/触发点）已经钉死且可自动校验**，音频组可以立刻开工；播放器会在音频到位后接一次，之后换音频文件就不再需要改代码。