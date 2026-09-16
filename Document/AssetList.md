# Science Potato · 美术资源清单（自动生成，勿手改）

> 由 `Tools/gen_asset_manifest.ps1` 从四张配置表 + `Document/ArtSpec.md` 生成 —— **表的 id 就是文件名**，清单不会和表漂移。
> 合计 **188** 个文件；当前缺 **188** 个。规格与验收见 Document/ArtSpec.md。

| 类别 | 数量 | 缺 | 典型尺寸 | 存放目录 |
| :--- | ---: | ---: | :--- | :--- |
| Building | 24 | 24 | 256x256 | res://Texture/Building/ |
| Event | 20 | 20 | 512x288 | res://Texture/Event/ |
| Resource | 3 | 3 | 64x64 | res://Texture/Resource/ |
| TechIcon | 93 | 93 | 96x96 | res://Texture/Tech/{树}/ |
| Terrain | 5 | 5 | 366x423 | res://Texture/Terrain/ |
| UI | 28 | 28 | 96x96(九宫格 24) | res://Texture/UI/ |
| Unit | 15 | 15 | 128x128 | res://Texture/Unit/ |

## 交付分包建议（每包到位即可见效果，缺图自动回退占位）

| 包 | 内容 | 数量 |
| :--- | :--- | ---: |
| 01 最小可玩包 | 5 地形 + camp/farm/mine + worker/swordsman + 3 资源图标 + panel_bg + 按钮三态 | 16 |
| 02 建筑全量 | 其余 21 张建筑（含 I/II/III 各级） | 21 |
| 03 单位与敌人 | 其余 13 张单位 | 13 |
| 04 事件插图 | 20 张 | 20 |
| 05 科技图标 | 93 张（按树分目录） | 93 |
| 06 UI 与结局 | 其余 UI 25 张 | 25 |
| 07 BGM | 1 首（已有则跳过） | 1 |

## Building（24 个）

| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `Texture/Building/bone_flute_workshop.png` | 256x256 | BottomCenter | missing | 骨笛工坊（末级；脚底贴下边、水平居中） |
| `Texture/Building/camp.png` | 256x256 | BottomCenter | missing | 营地（可升级→camp_ii；脚底贴下边、水平居中） |
| `Texture/Building/camp_ii.png` | 256x256 | BottomCenter | missing | 营地 lv.II（可升级→camp_iii；脚底贴下边、水平居中） |
| `Texture/Building/camp_iii.png` | 256x256 | BottomCenter | missing | 营地 lv.III（末级；脚底贴下边、水平居中） |
| `Texture/Building/farm.png` | 256x256 | BottomCenter | missing | 农田 lv.I（可升级→farm_ii；脚底贴下边、水平居中） |
| `Texture/Building/farm_ii.png` | 256x256 | BottomCenter | missing | 农田 lv.II（可升级→farm_iii；脚底贴下边、水平居中） |
| `Texture/Building/farm_iii.png` | 256x256 | BottomCenter | missing | 农田 lv.III（末级；脚底贴下边、水平居中） |
| `Texture/Building/military_camp.png` | 256x256 | BottomCenter | missing | 军营（可升级→military_camp_ii；脚底贴下边、水平居中） |
| `Texture/Building/military_camp_ii.png` | 256x256 | BottomCenter | missing | 军营 lv.II（可升级→military_camp_iii；脚底贴下边、水平居中） |
| `Texture/Building/military_camp_iii.png` | 256x256 | BottomCenter | missing | 军营 lv.III（末级；脚底贴下边、水平居中） |
| `Texture/Building/mine.png` | 256x256 | BottomCenter | missing | 矿场 lv.I（可升级→mine_ii；脚底贴下边、水平居中） |
| `Texture/Building/mine_ii.png` | 256x256 | BottomCenter | missing | 矿场 lv.II（可升级→mine_iii；脚底贴下边、水平居中） |
| `Texture/Building/mine_iii.png` | 256x256 | BottomCenter | missing | 矿场 lv.III（末级；脚底贴下边、水平居中） |
| `Texture/Building/observatory.png` | 256x256 | BottomCenter | missing | 观星台（末级；脚底贴下边、水平居中） |
| `Texture/Building/school.png` | 256x256 | BottomCenter | missing | 学院（可升级→school_ii；脚底贴下边、水平居中） |
| `Texture/Building/school_ii.png` | 256x256 | BottomCenter | missing | 学院 lv.II（可升级→school_iii；脚底贴下边、水平居中） |
| `Texture/Building/school_iii.png` | 256x256 | BottomCenter | missing | 学院 lv.III（末级；脚底贴下边、水平居中） |
| `Texture/Building/sundial.png` | 256x256 | BottomCenter | missing | 日晷（末级；脚底贴下边、水平居中） |
| `Texture/Building/warehouse.png` | 256x256 | BottomCenter | missing | 仓库 lv.I（可升级→warehouse_ii；脚底贴下边、水平居中） |
| `Texture/Building/warehouse_ii.png` | 256x256 | BottomCenter | missing | 仓库 lv.II（可升级→warehouse_iii；脚底贴下边、水平居中） |
| `Texture/Building/warehouse_iii.png` | 256x256 | BottomCenter | missing | 仓库 lv.III（末级；脚底贴下边、水平居中） |
| `Texture/Building/workshop.png` | 256x256 | BottomCenter | missing | 工坊（可升级→workshop_ii；脚底贴下边、水平居中） |
| `Texture/Building/workshop_ii.png` | 256x256 | BottomCenter | missing | 工坊 lv.II（可升级→workshop_iii；脚底贴下边、水平居中） |
| `Texture/Building/workshop_iii.png` | 256x256 | BottomCenter | missing | 工坊 lv.III（末级；脚底贴下边、水平居中） |

## Event（20 个）

| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `Texture/Event/alchemy_breakthrough.png` | 512x288 | Center | missing | 炼金术突破（事件弹窗插图，16:9） |
| `Texture/Event/cold_wave.png` | 512x288 | Center | missing | 低温寒潮（事件弹窗插图，16:9） |
| `Texture/Event/cultural_renaissance.png` | 512x288 | Center | missing | 文化复兴（事件弹窗插图，16:9） |
| `Texture/Event/drought.png` | 512x288 | Center | missing | 干旱（事件弹窗插图，16:9） |
| `Texture/Event/earthquake.png` | 512x288 | Center | missing | 地震（事件弹窗插图，16:9） |
| `Texture/Event/enlightenment.png` | 512x288 | Center | missing | 启蒙时代（事件弹窗插图，16:9） |
| `Texture/Event/flash_of_inspiration.png` | 512x288 | Center | missing | 灵感迸发（事件弹窗插图，16:9） |
| `Texture/Event/gold_rush.png` | 512x288 | Center | missing | 淘金热（事件弹窗插图，16:9） |
| `Texture/Event/harvest_festival.png` | 512x288 | Center | missing | 丰收庆典（事件弹窗插图，16:9） |
| `Texture/Event/locust_swarm.png` | 512x288 | Center | missing | 蝗虫灾害（事件弹窗插图，16:9） |
| `Texture/Event/master_builder.png` | 512x288 | Center | missing | 建造大师到来（事件弹窗插图，16:9） |
| `Texture/Event/math_contest.png` | 512x288 | Center | missing | 数学竞赛（事件弹窗插图，16:9） |
| `Texture/Event/mine_collapse.png` | 512x288 | Center | missing | 矿脉塌方（事件弹窗插图，16:9） |
| `Texture/Event/mysterious_stele.png` | 512x288 | Center | missing | 神秘石碑（事件弹窗插图，16:9） |
| `Texture/Event/plague.png` | 512x288 | Center | missing | 瘟疫（事件弹窗插图，16:9） |
| `Texture/Event/pottery_spread.png` | 512x288 | Center | missing | 制陶技术传播（事件弹窗插图，16:9） |
| `Texture/Event/sunspot_observation.png` | 512x288 | Center | missing | 太阳黑子观测（事件弹窗插图，16:9） |
| `Texture/Event/trade_caravan.png` | 512x288 | Center | missing | 贸易商队到来（事件弹窗插图，16:9） |
| `Texture/Event/visiting_scholar.png` | 512x288 | Center | missing | 游学学者来访（事件弹窗插图，16:9） |
| `Texture/Event/volcanic_ash.png` | 512x288 | Center | missing | 火山灰笼罩（事件弹窗插图，16:9） |

## Resource（3 个）

| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `Texture/Resource/BasicMinerals.png` | 64x64 | Center | missing | 顶栏 / 面板小图标 |
| `Texture/Resource/Food.png` | 64x64 | Center | missing | 顶栏 / 面板小图标 |
| `Texture/Resource/Idea.png` | 64x64 | Center | missing | 顶栏 / 面板小图标 |

## TechIcon（93 个）

| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `Texture/Tech/chemistry/acid_base_concepts.png` | 96x96 | Center | missing | 酸碱概念（chemistry 树） |
| `Texture/Tech/chemistry/acid_base_theory.png` | 96x96 | Center | missing | 酸碱理论深化（chemistry 树） |
| `Texture/Tech/chemistry/alchemical_symbols.png` | 96x96 | Center | missing | 炼金术符号体系（chemistry 树） |
| `Texture/Tech/chemistry/alchemy_rise.png` | 96x96 | Center | missing | 炼金术兴起（chemistry 树） |
| `Texture/Tech/chemistry/alcohol_purification.png` | 96x96 | Center | missing | 酒精提纯（chemistry 树） |
| `Texture/Tech/chemistry/alloying.png` | 96x96 | Center | missing | 合金尝试（chemistry 树） |
| `Texture/Tech/chemistry/atomism.png` | 96x96 | Center | missing | 原子论（chemistry 树） |
| `Texture/Tech/chemistry/calcination_and_roasting.png` | 96x96 | Center | missing | 煅烧与焙烧（chemistry 树） |
| `Texture/Tech/chemistry/combustion_and_air.png` | 96x96 | Center | missing | 燃烧与空气（chemistry 树） |
| `Texture/Tech/chemistry/compounds_and_mixtures.png` | 96x96 | Center | missing | 化合与混合（chemistry 树） |
| `Texture/Tech/chemistry/distillation.png` | 96x96 | Center | missing | 蒸馏技术（chemistry 树） |
| `Texture/Tech/chemistry/early_glassmaking.png` | 96x96 | Center | missing | 玻璃初制（chemistry 树） |
| `Texture/Tech/chemistry/early_metallurgy.png` | 96x96 | Center | missing | 冶金萌芽（chemistry 树） |
| `Texture/Tech/chemistry/fermentation.png` | 96x96 | Center | missing | 酿造发酵（chemistry 树） |
| `Texture/Tech/chemistry/four_elements.png` | 96x96 | Center | missing | 四元素说（chemistry 树） |
| `Texture/Tech/chemistry/gas_collection.png` | 96x96 | Center | missing | 气体收集（chemistry 树） |
| `Texture/Tech/chemistry/metal_displacement.png` | 96x96 | Center | missing | 金属置换（chemistry 树） |
| `Texture/Tech/chemistry/metal_refining.png` | 96x96 | Center | missing | 金属精炼（chemistry 树） |
| `Texture/Tech/chemistry/metallurgy_innovation.png` | 96x96 | Center | missing | 金属冶炼革新（chemistry 树） |
| `Texture/Tech/chemistry/mineral_acids.png` | 96x96 | Center | missing | 矿物酸制备（chemistry 树） |
| `Texture/Tech/chemistry/mineral_classification.png` | 96x96 | Center | missing | 矿物分类（chemistry 树） |
| `Texture/Tech/chemistry/natural_dyes.png` | 96x96 | Center | missing | 天然染料（chemistry 树） |
| `Texture/Tech/chemistry/pharmaceutical_chemistry.png` | 96x96 | Center | missing | 制药化学（chemistry 树） |
| `Texture/Tech/chemistry/potash_alkali.png` | 96x96 | Center | missing | 草木灰制碱（chemistry 树） |
| `Texture/Tech/chemistry/pottery.png` | 96x96 | Center | missing | 制陶术（chemistry 树） |
| `Texture/Tech/chemistry/saltpeter_and_gunpowder.png` | 96x96 | Center | missing | 硝石与火药（chemistry 树） |
| `Texture/Tech/chemistry/salts_research.png` | 96x96 | Center | missing | 盐类研究（chemistry 树） |
| `Texture/Tech/chemistry/taming_of_fire.png` | 96x96 | Center | missing | 火的驯服（chemistry 树） |
| `Texture/Tech/math/algebra.png` | 96x96 | Center | missing | 代数学（math 树） |
| `Texture/Tech/math/algorithms.png` | 96x96 | Center | missing | 算法（math 树） |
| `Texture/Tech/math/arabic_geometry.png` | 96x96 | Center | missing | 阿拉伯几何（math 树） |
| `Texture/Tech/math/arithmetic.png` | 96x96 | Center | missing | 算术（math 树） |
| `Texture/Tech/math/basic_geometry.png` | 96x96 | Center | missing | 基础几何（math 树） |
| `Texture/Tech/math/combinatorics.png` | 96x96 | Center | missing | 组合数学（math 树） |
| `Texture/Tech/math/conic_sections.png` | 96x96 | Center | missing | 圆锥曲线（math 树） |
| `Texture/Tech/math/counting.png` | 96x96 | Center | missing | 计数（math 树） |
| `Texture/Tech/math/diophantine_equations.png` | 96x96 | Center | missing | 丢番图方程（math 树） |
| `Texture/Tech/math/early_algebraic_geometry.png` | 96x96 | Center | missing | 代数几何萌芽（math 树） |
| `Texture/Tech/math/early_probability.png` | 96x96 | Center | missing | 概率论萌芽（math 树） |
| `Texture/Tech/math/early_trigonometry.png` | 96x96 | Center | missing | 三角学萌芽（math 树） |
| `Texture/Tech/math/elementary_algebra.png` | 96x96 | Center | missing | 初等代数（math 树） |
| `Texture/Tech/math/elements.png` | 96x96 | Center | missing | 几何原本（math 树） |
| `Texture/Tech/math/equations_and_curves.png` | 96x96 | Center | missing | 方程与曲线（math 树） |
| `Texture/Tech/math/fibonacci_sequence.png` | 96x96 | Center | missing | 斐波那契数列（math 树） |
| `Texture/Tech/math/hindu_arabic_numerals.png` | 96x96 | Center | missing | 印度-阿拉伯数字（math 树） |
| `Texture/Tech/math/infinity_and_limits.png` | 96x96 | Center | missing | 无限与极限（math 树） |
| `Texture/Tech/math/measurement.png` | 96x96 | Center | missing | 度量（math 树） |
| `Texture/Tech/math/mechanical_geometry.png` | 96x96 | Center | missing | 机械几何（math 树） |
| `Texture/Tech/math/method_of_exhaustion.png` | 96x96 | Center | missing | 穷竭法（math 树） |
| `Texture/Tech/math/number_theory.png` | 96x96 | Center | missing | 数论（math 树） |
| `Texture/Tech/math/numeral_system.png` | 96x96 | Center | missing | 记数系统（math 树） |
| `Texture/Tech/math/perspective_geometry.png` | 96x96 | Center | missing | 透视几何（math 树） |
| `Texture/Tech/math/preliminary_survey.png` | 96x96 | Center | missing | 初步测量（math 树） |
| `Texture/Tech/math/pythagorean_school.png` | 96x96 | Center | missing | 毕达哥拉斯学派（math 树） |
| `Texture/Tech/math/quadratic_formula.png` | 96x96 | Center | missing | 二次方程求根（math 树） |
| `Texture/Tech/math/simple_sequences.png` | 96x96 | Center | missing | 简单数列（math 树） |
| `Texture/Tech/math/spherical_geometry.png` | 96x96 | Center | missing | 球面几何（math 树） |
| `Texture/Tech/math/spherical_trigonometry.png` | 96x96 | Center | missing | 球面三角学（math 树） |
| `Texture/Tech/physics/acoustics.png` | 96x96 | Center | missing | 声学初探（physics 树） |
| `Texture/Tech/physics/alhazen_optics.png` | 96x96 | Center | missing | 阿尔哈曾光学（physics 树） |
| `Texture/Tech/physics/aristotelian_motion.png` | 96x96 | Center | missing | 亚里士多德运动论（physics 树） |
| `Texture/Tech/physics/atomism.png` | 96x96 | Center | missing | 原子论（physics 树） |
| `Texture/Tech/physics/buoyancy_law.png` | 96x96 | Center | missing | 浮力定律（physics 树） |
| `Texture/Tech/physics/buoyancy_observation.png` | 96x96 | Center | missing | 浮沉观察（physics 树） |
| `Texture/Tech/physics/center_of_gravity.png` | 96x96 | Center | missing | 重心理论（physics 树） |
| `Texture/Tech/physics/compound_machines.png` | 96x96 | Center | missing | 简单机械组合（physics 树） |
| `Texture/Tech/physics/early_combustion_oxidation.png` | 96x96 | Center | missing | 燃烧与氧化萌芽（physics 树） |
| `Texture/Tech/physics/four_elements_combustion.png` | 96x96 | Center | missing | 燃烧四元素说（physics 树） |
| `Texture/Tech/physics/hydrostatics.png` | 96x96 | Center | missing | 流体静力学（physics 树） |
| `Texture/Tech/physics/impetus_and_angular_momentum.png` | 96x96 | Center | missing | 冲力与角动量（physics 树） |
| `Texture/Tech/physics/impetus_theory.png` | 96x96 | Center | missing | 冲力理论（physics 树） |
| `Texture/Tech/physics/inclined_plane.png` | 96x96 | Center | missing | 斜面原理（physics 树） |
| `Texture/Tech/physics/latent_heat.png` | 96x96 | Center | missing | 潜热概念雏形（physics 树） |
| `Texture/Tech/physics/law_of_reflection.png` | 96x96 | Center | missing | 反射定律（physics 树） |
| `Texture/Tech/physics/lever_balance.png` | 96x96 | Center | missing | 杠杆平衡（physics 树） |
| `Texture/Tech/physics/magnetism.png` | 96x96 | Center | missing | 磁石研究（physics 树） |
| `Texture/Tech/physics/magnifying_lens.png` | 96x96 | Center | missing | 放大透镜（physics 树） |
| `Texture/Tech/physics/material_hardness.png` | 96x96 | Center | missing | 材料硬度（physics 树） |
| `Texture/Tech/physics/mirror_reflection.png` | 96x96 | Center | missing | 镜面反射（physics 树） |
| `Texture/Tech/physics/perspective_and_vision.png` | 96x96 | Center | missing | 透视学与视觉（physics 树） |
| `Texture/Tech/physics/projectile_analysis.png` | 96x96 | Center | missing | 抛体运动分析（physics 树） |
| `Texture/Tech/physics/projectile_quantification.png` | 96x96 | Center | missing | 抛体运动定量化（physics 树） |
| `Texture/Tech/physics/pulley_systems.png` | 96x96 | Center | missing | 滑轮组（physics 树） |
| `Texture/Tech/physics/rainbow_formation.png` | 96x96 | Center | missing | 彩虹成因（physics 树） |
| `Texture/Tech/physics/refraction_observation.png` | 96x96 | Center | missing | 折射观察（physics 树） |
| `Texture/Tech/physics/shadow_observation.png` | 96x96 | Center | missing | 影子观测（physics 树） |
| `Texture/Tech/physics/simple_machine_intuition.png` | 96x96 | Center | missing | 简单机械直觉（physics 树） |
| `Texture/Tech/physics/speed_and_acceleration.png` | 96x96 | Center | missing | 速度与加速度（physics 树） |
| `Texture/Tech/physics/static_electricity.png` | 96x96 | Center | missing | 静电观察（physics 树） |
| `Texture/Tech/physics/statics_analysis.png` | 96x96 | Center | missing | 静力学分析（physics 树） |
| `Texture/Tech/physics/time_measurement.png` | 96x96 | Center | missing | 时间测量革新（physics 树） |
| `Texture/Tech/physics/vibration_and_waves.png` | 96x96 | Center | missing | 振动与波（physics 树） |
| `Texture/Tech/physics/wheel_and_rolling.png` | 96x96 | Center | missing | 轮子与滚动（physics 树） |

## Terrain（5 个）

| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `Texture/Terrain/desert.png` | 366x423 | Center | missing | 沙漠（平顶六边形：左右尖角贴边、上下边水平、四角透明） |
| `Texture/Terrain/forest.png` | 366x423 | Center | missing | 森林（平顶六边形：左右尖角贴边、上下边水平、四角透明） |
| `Texture/Terrain/mountain.png` | 366x423 | Center | missing | 山地（平顶六边形：左右尖角贴边、上下边水平、四角透明） |
| `Texture/Terrain/plain.png` | 366x423 | Center | missing | 平原（平顶六边形：左右尖角贴边、上下边水平、四角透明） |
| `Texture/Terrain/water.png` | 366x423 | Center | missing | 水域（平顶六边形：左右尖角贴边、上下边水平、四角透明） |

## UI（28 个）

| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `Texture/UI/button_hover.png` | 96x96(九宫格 24) | Center | missing | 按钮：悬停 |
| `Texture/UI/button_icon.png` | 64x64(九宫格 16) | Center | missing | 图标按钮底 |
| `Texture/UI/button_normal.png` | 96x96(九宫格 24) | Center | missing | 按钮：常态 |
| `Texture/UI/button_pressed.png` | 96x96(九宫格 24) | Center | missing | 按钮：按下 |
| `Texture/UI/cursor_attack.png` | 32x32 | Center | missing | 光标：攻击（可选）【P2 可选】 |
| `Texture/UI/cursor_build.png` | 32x32 | Center | missing | 光标：建造（可选）【P2 可选】 |
| `Texture/UI/cursor_default.png` | 32x32 | Center | missing | 光标：默认（可选）【P2 可选】 |
| `Texture/UI/defeat_bg.png` | 1920x1080 | Center | missing | 结局：失败 |
| `Texture/UI/icon_attack.png` | 48x48 | Center | missing | 指令：攻击 |
| `Texture/UI/icon_build.png` | 48x48 | Center | missing | 建造入口 |
| `Texture/UI/icon_hold.png` | 48x48 | Center | missing | 指令：待命 |
| `Texture/UI/icon_move.png` | 48x48 | Center | missing | 指令：移动 |
| `Texture/UI/icon_population.png` | 48x48 | Center | missing | 人口 |
| `Texture/UI/icon_research.png` | 48x48 | Center | missing | 研究面板入口 |
| `Texture/UI/icon_speed_1x.png` | 48x48 | Center | missing | 时间：1 档 |
| `Texture/UI/icon_speed_3x.png` | 48x48 | Center | missing | 时间：3 档 |
| `Texture/UI/icon_speed_6x.png` | 48x48 | Center | missing | 时间：6 档 |
| `Texture/UI/icon_speed_pause.png` | 48x48 | Center | missing | 时间：暂停 |
| `Texture/UI/logo.png` | 512x256 | Center | missing | 标题 logo（可选）【P2 可选】 |
| `Texture/UI/main_menu_bg.png` | 1920x1080 | Center | missing | 主菜单背景 |
| `Texture/UI/panel_bg.png` | 96x96(九宫格 24) | Center | missing | 通用面板底（研究/建造/事件弹窗共用） |
| `Texture/UI/progress_fill.png` | 32x32(九宫格 8) | Center | missing | 进度条填充（也可纯色，代码兜底） |
| `Texture/UI/range_hex.png` | 366x423 | Center | missing | 攻击/视野范围覆盖（可选）【P2 可选】 |
| `Texture/UI/select_hex.png` | 366x423 | Center | missing | 选中格高亮（与地形同几何） |
| `Texture/UI/tab_active.png` | 64x64(九宫格 16) | Center | missing | 分类页签：选中 |
| `Texture/UI/tab_normal.png` | 64x64(九宫格 16) | Center | missing | 分类页签：常态 |
| `Texture/UI/tech_node_frame.png` | 96x96 | Center | missing | 科技节点框（4 态用 modulate，不用 4 张） |
| `Texture/UI/victory_bg.png` | 1920x1080 | Center | missing | 结局：胜利 |

## Unit（15 个）

| 文件名 | 尺寸 | 锚点 | 状态 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `Texture/Unit/archer.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/ballista.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/boar.png` | 128x128 | BottomCenter | missing | 敌方 单位（脚底贴下边、朝右下） |
| `Texture/Unit/chemist.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/crocodile.png` | 128x128 | BottomCenter | missing | 敌方 单位（脚底贴下边、朝右下） |
| `Texture/Unit/eagle.png` | 128x128 | BottomCenter | missing | 敌方 单位（脚底贴下边、朝右下） |
| `Texture/Unit/engineer.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/explorer.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/heavy_guard.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/ibex.png` | 128x128 | BottomCenter | missing | 敌方 单位（脚底贴下边、朝右下） |
| `Texture/Unit/scholar.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/spearman.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/swordsman.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |
| `Texture/Unit/wolf.png` | 128x128 | BottomCenter | missing | 敌方 单位（脚底贴下边、朝右下） |
| `Texture/Unit/worker.png` | 128x128 | BottomCenter | missing | 玩家 单位（脚底贴下边、朝右下） |

