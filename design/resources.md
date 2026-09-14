# Resources

Resources are things required to start a
[research](/designs/research_tree) or do [constructions](buildings).

### NEW

  ---------------- ---------------------------- ---------------------------------------- --------------- ------------------ ------------------
  名字             来源                         用途                                     Modifiers       Initial Amount     Resource Limit
  ------           ------                       ------                                   -----------     ----------------   ----------------
  Idea             学院、日晷、事件、科技加成   研发科技、学院升级、部分建筑升级         IdeaGrowth      500                10000
  Food             农田、事件、科技加成         维持人口、单位训练与维护、部分建筑升级   FoodGrowth      300                2000
  Basic minerals   矿场、事件、科技加成         建造建筑、建筑升级、部分科技消耗         MineralGrowth   200                1500
  ---------------- ---------------------------- ---------------------------------------- --------------- ------------------ ------------------

\*For now there are no difference in mineral level

# Ideas

用于进行科研

#### 数值

- 存储上限：10000（可通过仓库升级提升）
- 初始储备： 500

#### 获取方式

  来源                                    基础产量                  影响因素
  --------------------------------------- ------------------------- ----------
  [school lv. I](buildings)（building）   250/月                    科技加成
  [巧合中的研讨会](events)（event）       灵感年产量15%（一次性）   无

#### 消耗方式

  去向                          数量   条件
  ----------------------------- ------ ----------
  [数学科技树](research_tree)   不等   详见索引
  [物理科技树](research_tree)   不等   详见索引
  [化学科技树](research_tree)   不等   详见索引

#### 联动

- 科技（如数学节点"[记数系统](math)"：学院基础产量+100/月）
- 事件（如随机事件"[巧合中的研讨会](events)"：一次性获得15%年产量）

# Natural Resources

## Food

Produced from [farms](buildings), it is essential for maintaining the
[population](population) growth.
食物的库存由整个文明共享。当产出小于消耗时将会导致[社会稳定度](game_index)，[社会繁荣度](game_index)下降。同时，粮食长期不足有可能导致[population
decrease](population)。

#### 数值

- 存储上限：2000（可通过仓库升级提升）
- 初始储备： 300
- 基础需求：人口总和 ×
  3/月，连续三年出现食物不足的情况时按照缺口随机减少人口

#### 获取方式

  来源                            基础产量                      影响因素
  ------------------------------- ----------------------------- ------------------
  [farm](buildings)（building）   144/年 ～3%                   科技加成、随机性
  随机事件"丰收/大丰收"           额外的20%/35%产量（一次性）   无

#### 消耗方式

  去向           数量
  -------------- -------------
  人口自然消耗   人口 × 3/月
  单位维护       不等/月

#### 联动

- 科技（如数学节点"[度量](math)"：农场基础产量+20/年）
- 事件（如随机事件"[丰收](events)"：一次性获得额外的20%产量）

## Minerals

Produced from [mines](mines) and [extractor](extractor), they are
essential for construction and industrials respectively. Minerals can be
further divided into
[基础石材](ores_tier)、[工业金属](ores_tier)、[高级金属](ores_tier) and
[战略金属](ores_tier). 所有等级的石料共享储存空间。
存储上限：1500（可通过仓库升级提升）

### 基础石材

遍地都是，用来进行基础建造 初始储备： 200

#### 获取方式

  来源                                    基础产量   影响因素
  --------------------------------------- ---------- ----------
  [mine lv. I](buildings)（building）     100/月     科技加成
  [mine lv. II](buildings)（building）    200/月     科技加成
  [mine lv. III](buildings)（building）   400/月     科技加成
  [mine lv. IV](buildings)（building）    800/月     科技加成

#### 消耗方式

  去向                数量   条件
  ------------------- ------ ----------
  [建造](ores_tier)   不等   详见索引

#### 联动

- 科技

\<del\>==== 工业金属 ====

稀有度1的金属，用来发展基础工业设施

#### 数值

- 存储上限：500（可通过仓库升级提升）
- 初始储备： 0

#### 获取方式

  来源                                    基础产量   影响因素
  --------------------------------------- ---------- ------------------
  [mine lv. II](buildings)（building）    30/月      科技加成、稳定度
  [mine lv. III](buildings)（building）   50/月      科技加成、稳定度
  [mine lv. IV](buildings)（building）    80/月      科技加成、稳定度

#### 消耗方式

  去向                数量   条件
  ------------------- ------ ----------
  [建造](ores_tier)   不等   详见索引

- `某科技`

\</del\> ![](tag>[mechanisms])
