# Science Potato · 音频资源清单（自动生成，勿手改）

> 由 `Tools/gen_sound_list.ps1` 从音效契约（`Scripts/Audio/Domain/SoundCatalog.cs`）生成 —— **id 就是文件名**，与代码同源、不会漂移。
> 规格与验收见 Document/SoundSpec.md；署名与许可见 Document/Credits.md。
> 合计 **37** 个文件（音效 36 + 音乐 1）；当前缺 **37** 个。

## 最小可玩音效包（先做这 9 条就能听出手感）

| 文件 | 说明 |
| :--- | :--- |
| `res://Audio/SFX/ui_click.wav` | 任何意图被接受（建造/训练/研究/移动/事件决策成功） |
| `res://Audio/SFX/ui_reject.wav` | 任何意图被拒绝（资源不足 / 门控未解锁 / 格位占用 / 越权） |
| `res://Audio/SFX/build_start.wav` | 施工点落位（建造意图成功） |
| `res://Audio/SFX/build_complete.wav` | 建筑完工（BuildingCompletedEvent） |
| `res://Audio/SFX/unit_trained.wav` | 单位训练完成（UnitTrainedEvent） |
| `res://Audio/SFX/unit_move_order.wav` | 移动指令下达（可随时改目的地） |
| `res://Audio/SFX/unit_attack.wav` | 攻击命中（可做 3 个变体；同帧只响一次） |
| `res://Audio/SFX/research_complete.wav` | 研究完成（ResearchCompletedEvent） |
| `res://Audio/SFX/event_trigger.wav` | 随机事件触发（GameEventTriggeredEvent，触发即暂停） |

## UI（12 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/SFX/ui_click.wav` | wav 44.1kHz 16-bit 单声道 | 0.05-0.15s | 任何意图被接受（建造/训练/研究/移动/事件决策成功） | P1 | 最常用的反馈音 |
| `Audio/SFX/ui_cancel.wav` | wav 44.1kHz 16-bit 单声道 | 0.05-0.20s | 右键 / ESC 退出建造或单位指令模式 | P1 |  |
| `Audio/SFX/ui_reject.wav` | wav 44.1kHz 16-bit 单声道 | 0.10-0.30s | 任何意图被拒绝（资源不足 / 门控未解锁 / 格位占用 / 越权） | P1 | 拒绝原因很多，提示音只一条 |
| `Audio/SFX/ui_hover.wav` | wav 44.1kHz 16-bit 单声道 | 0.05s | 鼠标悬停按钮或格子（可选，可关） | P2 | 音要极轻，避免吵 |
| `Audio/SFX/ui_panel_open.wav` | wav 44.1kHz 16-bit 单声道 | 0.10-0.30s | 打开面板（研究 / 建造 / 资源） | P2 |  |
| `Audio/SFX/ui_panel_close.wav` | wav 44.1kHz 16-bit 单声道 | 0.10-0.30s | 关闭面板 | P2 |  |
| `Audio/SFX/ui_tab.wav` | wav 44.1kHz 16-bit 单声道 | 0.05-0.15s | 分类页签切换 | P2 |  |
| `Audio/SFX/game_start.wav` | wav 44.1kHz 16-bit 单声道 | 0.5-1.5s | 新开局成功（生成地图 + 势力就位） | P1 | 可与 BGM 起播叠加 |
| `Audio/SFX/game_save.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.50s | 存档完成（自动存档点也算） | P1 |  |
| `Audio/SFX/game_load.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.80s | 读档完成 | P1 |  |
| `Audio/SFX/victory.wav` | wav 44.1kHz 16-bit 单声道 | 1-3s | 胜负判定：己方获胜 | P1 | 可带一点情绪上扬 |
| `Audio/SFX/defeat.wav` | wav 44.1kHz 16-bit 单声道 | 1-3s | 胜负判定：己方出局 | P1 |  |

## Build（8 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/SFX/build_start.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-0.80s | 施工点落位（建造意图成功） | P1 |  |
| `Audio/SFX/build_complete.wav` | wav 44.1kHz 16-bit 单声道 | 0.5-1.5s | 建筑完工（BuildingCompletedEvent） | P1 |  |
| `Audio/SFX/upgrade_start.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-0.80s | 开始升级建筑 | P1 |  |
| `Audio/SFX/upgrade_complete.wav` | wav 44.1kHz 16-bit 单声道 | 0.5-1.5s | 升级完成（BuildingUpgradedEvent） | P1 |  |
| `Audio/SFX/building_captured.wav` | wav 44.1kHz 16-bit 单声道 | 0.5-1.5s | 建筑易主（BuildingCapturedEvent） | P1 |  |
| `Audio/SFX/building_destroyed.wav` | wav 44.1kHz 16-bit 单声道 | 0.5-1.5s | 建筑被拆除/摧毁（BuildingRemovedEvent） | P1 |  |
| `Audio/SFX/train_start.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.50s | 建筑下单训练 | P1 |  |
| `Audio/SFX/unit_trained.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-0.80s | 单位训练完成（UnitTrainedEvent） | P1 |  |

## Unit（5 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/SFX/unit_move_order.wav` | wav 44.1kHz 16-bit 单声道 | 0.10-0.30s | 移动指令下达（可随时改目的地） | P1 |  |
| `Audio/SFX/unit_attack.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.60s | 攻击命中（可做 3 个变体；同帧只响一次） | P1 | 变体命名 unit_attack_01/02/03 |
| `Audio/SFX/unit_hit.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.50s | 己方单位受击 | P2 |  |
| `Audio/SFX/unit_died.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-0.80s | 单位阵亡（UnitDiedEvent，敌我共用） | P1 |  |
| `Audio/SFX/unit_loot.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.50s | 击杀掉落拾取 | P2 |  |

## Tech（3 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/SFX/research_start.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-0.80s | 研究开工 | P1 |  |
| `Audio/SFX/research_complete.wav` | wav 44.1kHz 16-bit 单声道 | 0.60-1.5s | 研究完成（ResearchCompletedEvent） | P1 |  |
| `Audio/SFX/tech_ui_unlocked.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-1.0s | 科技解锁界面能力（如收获面板首次可用） | P2 |  |

## Event（3 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/SFX/event_trigger.wav` | wav 44.1kHz 16-bit 单声道 | 1-2s | 随机事件触发（GameEventTriggeredEvent，触发即暂停） | P1 | 要"打断感"，但别盖过弹窗阅读 |
| `Audio/SFX/event_accept.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-0.80s | 事件弹窗：确定 | P1 |  |
| `Audio/SFX/event_dismiss.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.50s | 事件弹窗：忽略/跳过 | P2 |  |

## Resource（4 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/SFX/monthly_settle.wav` | wav 44.1kHz 16-bit 单声道 | 0.5-1.5s | 月结到账（产出 - 维护费） | P1 | 每月一次，别做太响 |
| `Audio/SFX/resource_low.wav` | wav 44.1kHz 16-bit 单声道 | 0.30-0.80s | 资源告警（低于阈值 / 因资源不足被拒） | P2 |  |
| `Audio/SFX/population_growth.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.50s | 人口 +1（住房节拍） | P2 |  |
| `Audio/SFX/deficit_warning.wav` | wav 44.1kHz 16-bit 单声道 | 0.50-1.2s | 赤字减员（连续赤字月结算） | P2 |  |

## Env（1 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/SFX/fog_reveal.wav` | wav 44.1kHz 16-bit 单声道 | 0.20-0.50s | 视野揭开（迷雾首次变可见） | P2 |  |

## BGM（1 条）

| 文件 | 格式 | 时长 | 触发点（什么时候响） | 优先级 | 说明 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Audio/BGM/main.ogg` | ogg 44.1kHz 立体声 无缝循环 | 60-120s（无缝循环） | 开局起循环播放；暂停菜单淡出 | P1 | N4 已有文件（44.1kHz 立体声） |

