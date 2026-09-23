# Phase 1 交付：内置能力 → 规则图节点 映射审计

> 生成方式：`tools/audit_ability_coverage.ps1`（类型频次）+ `tools/audit_migration_matrix.ps1`（可达性/逐卡矩阵/外部资源）
> 数据源：`Resources/{Cards,Abilities,Effects,Conditions,Status}` 资产 + `Resources/NodeDoc.xml`（319 节点）+ 实现代码
> 判定口径：**✓ 直译**（1 节点）/ **◐ 组合**（2~5 节点可表达）/ **✗ 缺口**（需新增节点或改架构）

---

## 一、规模（确定数字）

| 项 | 数量 | 说明 |
|---|---|---|
| 内置卡 | **155** | Hero 3 / Fire 28 / Forest 26 / Plain 34 / Water 29 / Swamp 24 / Summon 11 |
| ↳ 无能力 | **25** | `abilities: []` |
| ↳ 有能力 | **130** | 134 张 `deckbuilding=true`，21 张=false（含 11 召唤物） |
| AbilityData 资产 | **290** | 卡**直接引用 126**、**含链式可达 146**、**真孤儿 144**（不参与迁移） |
| 实际用到的 Effect 类 | **27 / 47** | 20 个类从未使用 |
| 实际用到的 Condition 类 | **18 / 27** | 9 个未使用 |
| 实际用到的 Filter 类 | **3 / 6** | 3 个未使用 |
| Status 引用 | **59 次 / 56 能力 / 50 卡** | 50 张卡带状态（占 1/3） |
| 能力级 FX | **75 个能力** | 全部指向 **9 个共用 prefab**（见第七节） |
| 能力级音频 | **0** | `cast_audio/target_audio` 全空 ✓ |
| `multi_target` / `target_slots` | **0 / 0** | → DTO 这两处缺口**不影响迁移** ✓ |

---

## 二、分批施工单（数据源 `tools/card_migration_matrix.tsv`）

| 批 | 内容 | 卡数 | 占比 |
|---|---|---|---|
| 1 | 白板卡（无能力） | **25** | 16% |
| 2 | 数值/直伤（无条件、无过滤） | **14** | 9% |
| 3 | 召唤/衍生 | **3** | 2% |
| 4 | **含条件/过滤** | **60** | 39% |
| 5 | **英雄/光环(Ongoing)/亡语(OnDeath)** | **52** | 34% |
| 6 | 专有逻辑 | **1** | 1% |

> **结论**：批 4 + 批 5 = **112 张（72%）**。整个迁移的成败不在"直伤/抽牌"这类通用动作，而在
> **Condition（84 张卡用到）** 与 **Ongoing 光环（49 张卡）** 的表达力。下面第三~六节围绕这两块。

---

## 三、Effect → 节点 映射表（27 类，按引用次数）

| Effect 类 | 引用 | 卡数 | 对应节点 | 判定 |
|---|---|---|---|---|
| EffectAddStat | 65 | 28 | `102027 获取卡牌属性`(攻击/生命/法力费用) + `112004` 加减 + `202037 设置卡牌属性` | ◐ 组合 |
| EffectDamage | 31 | 17 | `202001 造成伤害` / `202041 造成伤害或法伤` | ✓ |
| EffectSummon | 24 | 9 | `202003 创建衍生卡并置入战场` | ✓ |
| EffectHeal | 22 | 10 | `202013/202039/202047 治疗目标卡牌` | ✓ |
| EffectDraw | 18 | 11 | `210001 简单抽牌` / `201003 抽目标卡牌` | ✓ |
| EffectDestroy | 16 | 5 | `202016 消灭` | ✓ |
| EffectSendPile | 15 | 6 | `210002~210008`（置入战场/手牌/牌库/墓地/延迟区/道具栏/暂存区）+ `200006` | ✓ |
| EffectMana | 7 | 4 | `201008 增加当前灵力值` / `201009/201010 灵力上限` | ◐（子型 ManaMaxTotal 见缺口） |
| EffectClearStatus | 7 | 3 | `106006 获取卡牌上的所有增益` + `206002 移除增益`（逐个移除）| ◐ 组合 |
| EffectPlay | 6 | 2 | `202023 触发法术或技能卡牌效果` / `210002` | ◐ 需核语义 |
| EffectTransform | 5 | 3 | `202029 变形为卡牌定义` | ✓ |
| EffectRepeat | 4 | 1 | `212002 重复动作` / `212005 重复动作直到` | ✓ |
| EffectDiscard | 4 | 0 | `202002/202038 丢弃卡牌` | ✓ |
| EffectAddAbility | 4 | 2 | — | **✗ 缺口**（无"添加技能"节点） |
| EffectRoll | 3 | 2 | `112008 X到Y之间的随机整数`（骰值存临时变量） | ◐ 组合 |
| EffectCreate | 3 | 1 | `202003~202006/202045/202046 创建衍生卡并置入…` | ✓ |
| EffectResetStat | 3 | 2 | `202024 重置卡牌` | ◐ 需核语义 |
| EffectExhaust | 2 | 3 | — | **✗ 缺口**（无横置/疲惫节点） |
| EffectAddHeroAbility | 2 | 0 | — | **✗ 缺口**（同 AddAbility） |
| EffectSetStat | 2 | 2 | `202037 设置卡牌属性` | ✓ |
| EffectSetAtkEqualHpReal | 1 | 0 | `102008 获取当前生命值` + `202037` | ◐ 组合 |
| EffectDrawType | 1 | 0 | `210001` + `111012 筛选` + `102032 卡牌类型判断` | ◐ 组合 |
| EffectAddStatRoll | 1 | 0 | `112008` + `202037` | ◐ 组合 |
| EffectSetHeroAbility | 1 | 0 | — | **✗ 缺口** |
| EffectAddTrait | 1 | 0 | —（`202037` 只支持 攻击/生命/法力费用） | **✗ 缺口** |
| EffectAddKeyword | 1 | 1 | —（同上） | **✗ 缺口** |
| EffectClearTemp | 1 | 1 | — | **✗ 缺口**（清空暂存区） |

**缺口汇总（动作类）**：`添加/设置技能`（7 次引用）、`横置`（2）、`添加种族/关键词`（2）、`清空暂存区`（1）
→ 影响 **~13 张卡**（`spell_extinct`、`spell_submerge`、`bull_heat`、`dragon_egg`、`flame_eagle`、`kraken`、`spell_hibernate`…）

---

## 四、Condition / Filter → 节点 映射表

| Condition 类 | 引用 | 卡数 | 对应节点 | 判定 |
|---|---|---|---|---|
| ConditionCardType | 85 | 45 | `102032`/`103024`/`104017` 类型判断 | ✓ |
| ConditionOwner | 81 | 33 | `102010 获取卡牌拥有者` + `112002 Compare` | ◐ 组合 |
| ConditionSelf | 38 | 19 | `102001 这张卡牌` | ✓ |
| ConditionCardPile | 31 | 13 | `105005 卡牌所在牌堆判断` | ✓ |
| ConditionSlotEmpty | 30 | 19 | —（无槽位节点；可用 `111004 获取元素数量` 近似） | **✗ 缺口**（30 次！高频） |
| ConditionOwnerAI | 28 | 17 | —（无"是否 AI"判断节点） | **✗ 缺口** |
| ConditionTarget | 25 | 8 | `102001` + `111005 包含` | ◐ 组合 |
| ConditionOnce | 8 | 3 | `112007/212004 临时变量`（每回合一次） | ◐ 组合 |
| ConditionStatus | 7 | 2 | `106003 是否具有增益` | ◐（状态↔增益口径待定，见第九节） |
| ConditionTurn | 6 | 2 | `119001 获取回合数` + Compare | ◐ 组合 |
| ConditionStat | 6 | 1 | `102006/102007/102008 获取攻击/生命` + Compare | ◐ 组合 |
| ConditionCount | 4 | 4 | `111004 获取元素数量` + Compare | ◐ 组合 |
| ConditionRolled | 2 | 0 | `112007 获取临时变量` + Compare | ◐ 组合 |
| ConditionSelectedValue | 2 | 1 | `112007/113003 映射取值` | ◐ 组合 |
| ConditionSlotRange | 1 | 1 | —（槽位缺口） | **✗ 缺口** |
| ConditionOtherCardWithStatus | 1 | 1 | `111023 是否有任意元素满足条件` + `106003` | ◐ 组合 |
| ConditionDeckbuilding | 1 | 1 | `103014 是否为衍生牌` | ✓ |
| ConditionTrait | 1 | 0 | `103004/104018 具有标签` | ✓ |
| FilterRandom | 12 | 5 | `111008/111028 随机元素` | ✓ |
| FilterFirst | 6 | 1 | `111024 获取第一个元素` | ✓ |
| FilterLowestStat | 2 | 2 | `111013 排序` + `111024` | ◐ 组合 |

**缺口汇总（条件类）**：`槽位是否为空 / 槽位范围`（31 次，**最高频缺口**，19+1 张卡）、`是否 AI 玩家`（28 次，17 张卡）

---

## 五、触发（入口）与目标（取值）覆盖

### 入口事件（14 类实际使用）
| AbilityTrigger | 次数 | 图入口 | 判定 |
|---|---|---|---|
| OnPlay | 120 | `OnPlay` / `ActivateEffect` | ✓ |
| Ongoing | 45 | `AuraEffect`→Ongoing | ✓（49 张卡） |
| None | 32 | 无入口（被赋予/链式能力） | ❓ 需确认语义 |
| Activate | 24 | `ActivateAbility`→Activate | ✓ |
| OnDeath | 15 | `OnDeath` / `PassiveEffect`(亡语) | ✓ |
| StartOfTurn | 14 | `StartOfTurn` | ✓ |
| OnBeforeAttack | 9 | `OnAttack` / `"攻击时"` | ✓ |
| EndOfTurn | 7 | `EndOfTurn` | ✓ |
| **OnAfterAttack** | 7 | — | **✗ 缺入口**（4 张卡） |
| OnPlayOther | 4 | `"打出牌"` | ✓ |
| **OnKill** | 4 | — | **✗ 缺入口**（2 张卡） |
| **OnBeforeDefend** | 3 | — | **✗ 缺入口**（3 张卡） |
| **OnAfterDefend** | 2 | — | **✗ 缺入口**（0 张卡） |
| **OnDeathOther** | 2 | — | **✗ 缺入口**（2 张卡） |
| （解析失败） | 2 | — | ❓ 需定位 |

> 图已有 25 个 `OnBefore*/OnAfter*` 事件入口（`CardPoolIO.MapGraphTrigger:894-935`），**缺的正是"攻击后/被攻击时/击杀时/他人死亡时"这 4~5 个**。

### 目标（18 类实际使用）
| AbilityTarget | 次数 | 图表达 | 判定 |
|---|---|---|---|
| Self | 77 | `102001 这张卡牌` | ✓ |
| PlayerSelf | 44 | 施法者玩家（入口编译） | ◐ |
| SelectTarget | 41 | 入口 target（`CompileGraphAbilities`） | ◐ |
| PlayTarget | 33 | 入口 target | ◐ |
| AllCardsBoard | 30 | `102012/102013 获取所有仆从/角色` | ✓ |
| AbilityTriggerer | 12 | 事件源卡（`108002` 事件变量） | ◐ |
| CardSelector | 11 | `201004/201016 从卡牌中选择一张` | ✓ |
| **EquippedCard** | 10 | —（无"获取装备者/被装备者"节点） | **✗ 缺口** |
| None | 6 | — | ❓ |
| **LastTargeted** | 5 | — | **✗ 缺口** |
| **AllSlots** | 5 | —（无槽位集合） | **✗ 缺口** |
| PlayerOpponent | 4 | `101003 获取玩家对手` | ✓ |
| AllCardsHand | 3 | `101006 获取玩家的手牌` | ✓ |
| AllCardData | 3 | `103001 获取所有卡牌定义` | ✓ |
| ChoiceSelector | 2 | —（无"选项菜单"节点） | **✗ 缺口** |
| AllCardsAllPiles | 2 | `111018/111019 集合相加` | ◐ |
| AllPlayers | 1 | `101020 获取所有玩家` | ✓ |
| **LastSummoned** | 1 | — | **✗ 缺口** |

---

## 六、必须新增的节点（按优先级）

| # | 节点 | 类型 | 影响 | 优先级 |
|---|---|---|---|---|
| 1 | 槽位是否为空 / 槽位数量 | Condition/取值 | **31 次引用、20 张卡** | P0 |
| 2 | 是否 AI 玩家 | Condition | **28 次、17 张卡** | P0 |
| 3 | 添加/移除/设置 **技能(能力)** | Action | 7 次、~4 张卡 | P0 |
| 4 | **横置/解除横置** | Action | 2 次、3 张卡 | P1 |
| 5 | 入口事件 ×4~5：`OnAfterAttack`/`OnBeforeDefend`/`OnAfterDefend`/`OnKill`/`OnDeathOther` | Event | 18 次、~10 张卡 | P1 |
| 6 | 获取**装备者/被装备卡** | 取值 | 10 次 | P1 |
| 7 | 获取**最近目标 / 最近召唤** | 取值 | 6 次 | P2 |
| 8 | 槽位集合（`AllSlots`） | 取值 | 5 次 | P2 |
| 9 | 选项菜单（`ChoiceSelector`） | Action | 2 次 | P2 |
| 10 | 添加**种族/标签**、添加**关键词** | Action | 2 次、1 张卡 | P2 |
| 11 | 清空暂存区 | Action | 1 次、1 张卡 | P2 |
| 12 | 属性通道扩展：**灵力上限硬顶**(ManaMaxTotal) | 属性枚举 | 1 子型 | P2 |
| 13 | 属性通道扩展：setter 支持更多属性名 | 枚举 | 提升 ◐→✓ 覆盖率 | P2 |

> 说明：13 项里有 **7 项集中在"槽位/AI/技能/装备者"**，都与 TCG2 特有的"槽位 + 装备 + 英雄技"机制相关；
> 其余是通用扩展。**P0 三项（1/2/3）影响 ~40 张卡，是批 4/5 的前置条件。**

---

## 七、资源打包清单（"全部随池走"）

### 能力级 FX：**只需 9 个共用 prefab** ✓（`Assets/TcgEngine/Prefabs/FX/`）
| prefab | 引用次数 | 用途 |
|---|---|---|
| Flame.prefab | 27 | 伤害类 target_fx |
| PotionFX.prefab | 12 | 增益/治疗 target_fx |
| Phoenix.prefab | 11 | 消灭/复活 target_fx、caster_fx |
| Leaf.prefab | 8 | 中毒/沉默/异常 target_fx |
| WaterFX.prefab | 6 | 回手/回牌库 target_fx |
| CastFX.prefab | 4 | 通用施法 caster_fx |
| FlameProjectile.prefab | 3 | 弹道 projectile_fx |
| TrapFX.prefab | 2 | 陷阱 |
| ElectricFX.prefab | 2 | 强化 target_fx |
→ 方案：DTO 增加 `fx_id` 字段（引用"特效 id"表，项目已有 `VFXConfig`），**9 条映射**即可全量覆盖。

### 卡级资源（155 张卡）
- `art_board` / `art_full`（Sprite）×2 → 进 `art/`（`.tcgpool` 已支持 ✓）
- `spawn_audio/death_audio/attack_audio/damage_audio`（AudioClip）→ 进 `audio/`（已支持 ✓）
- `spawn_fx/death_fx/attack_fx/damage_fx/idle_fx`（GameObject）→ **DTO 无字段** ✗ 需补 5 个 `fx_id`

---

## 八、DTO 缺口（`Workshop/CardCustomData.cs`）

| 字段 | DTO 现状 | 迁移是否需要 | 处理 |
|---|---|---|---|
| `AbilityData.board_fx/caster_fx/target_fx/projectile_fx` | **缺** | 需要（75 个能力） | 补 4 个 `fx_id` |
| `AbilityData.cast_audio/target_audio` | **缺** | 不需要（全空 ✓） | 补字段但留空 |
| `AbilityData.charge_target` | **缺** | ❓ 需查使用 | 视情况补 |
| `AbilityData.multi_target/target_slots/unique_targets` | **缺** | **不需要（全 0 ✓）** | 不动 |
| `CardData.spawn_fx…idle_fx`（5 个） | **缺** | 需要 | 补 5 个 `fx_id` |
| `CardPoolData.version` 校验 | 有字段未校验 | 需要（基础池迭代） | Phase 5 加校验和 |

---

## 九、脏数据与待核实（❓ 清单）

**脏数据（均为孤儿能力，不影响迁移）**：
- 3 条**伪造 guid**：`spell_give_flying_ability.effects`、`spell_silence_buff.effects`、`chain_destroy_zero_atk.conditions_trigger`
- 1 条指向**已不存在的资产**：`[molisha]_spells.conditions_trigger`
- 2 张卡**同基名**（id 不同）：`Cards/Plain/resurrection.asset`(id=`resurrection`) 与 `Cards/swamp/resurrection.asset`(id=`resurrection_swamp`)

**待核实（Phase 2 开工前必须定）**：
1. ~~状态(StatusData) ↔ 增益(BuffDefine) 的口径~~ → **已查清，结论：保留 `status` 通道，二者不合并**（证据 `NodeDocRunner.cs:2480-2511` / `7374-7378`）：
   - 图侧"增益"= **`BuffData`（`BuffPoolIO` 池，键 `buff_`+8位 Guid）**，`206001 添加增益` → `BuffRuntime.AddBuff`，注释明确"攻击/生命加成映射原生状态"；
   - 卡侧 `status`（50 张卡）= **原生 `StatusData`**（中毒/眩晕/沉默…是引擎内置行为，`EffectAddStatus`/`AbilityData.status` 驱动），**`BuffData` 的属性加减模型表达不了**；
   - DTO 已有 `status_ids`（存 `StatusData.effect` 枚举名）✓ → **状态不需要节点化**，它是"数据字段"，规则图只管流程/条件/目标。
   - **迁移口径定案**：规则（入口/条件/目标/动作链）→ 规则图；数据（状态/数值/FX/资源）→ DTO 字段。
2. `Trigger=None` 的 32 个能力在旧系统里怎么被触发（被 `EffectAddAbility` 添加？chain？）→ 决定图里对应什么
3. 2 个解析失败的 trigger 值（enum 越界/手工写入）
4. `EffectPlay`（6 次）的语义 → 映射到"触发卡牌效果"还是"置入战场"
5. `202037 设置卡牌属性` 的「法力费用」是否等价 `EffectStatType.Mana`

---

## 十、Phase 2 起手清单

1. **定 ❓1（状态 vs 增益）** — 阻塞转换器设计
2. **新增 P0 三项节点**（槽位 / 是否 AI / 添加技能）— 阻塞批 4/5
3. **实现 `AbilityToGraphConverter`**：按第三~五节映射表生成图；◐ 项生成组合子图；✗ 项生成**显式 TODO 占位**（不静默丢弃）
4. **补 DTO 的 9 个 fx_id 字段** 与 `VFXConfig` 映射
5. 先跑**批 1（25 张白板）**打通管线，再按 2→3→4→5→6 推进

---

## 十一、P0 节点落地记录（已完成，2026-09-19）

> 验证批次：`tools/batch_overrides.tsv`（标题 `P0 nodes r2`）→ **8/8 通过**；节点库 301 → **303**。

| 项 | 实现方式 | 改动位置 | 验证 |
|---|---|---|---|
| **空槽位数量 / 是否有空位 / 是否AI** | **不新增节点**：扩到既有 `101002 获取玩家属性` 的自由文本属性名（该节点的 propName 是自由文本，故无需改编辑器/文档库） | `NodeDocRunner.cs`（101002 取值分支）；新增 `CountEmptySlots`（与 `FindEmptySlot` 同源） | `空槽位数量=6`（每侧 6 格，`Slot.x_min=1..x_max=6` 闭区间）、`是否有空位=1`、`是否AI=0`（本地玩家 `is_ai=false`）✓ |
| **添加技能** | **新增动作节点 `202050 添加技能`**（cards + abilityId → `Card.AddAbility`） | `Resources/NodeDoc.xml`（追加 ActionComment）、`NodeDocRunner.cs`（`IsSupportedAction` + `ExecuteAction`）、`GraphEditorPanel.cs`（`SupportedNodeIds`）、`tools/node_inventory.tsv` | 端到端：`施法卡 HasAbility(stealth)=True` ✓；负向对照（不存在的技能 id）→ 正确告警不崩溃 ✓ |
| **AI限定·目标与施法者同归属** | **新增取值节点 `101030`**（Boolean）：旧 `ConditionOwnerAI` 的 1:1 等价口径 | 同上四处 | 人类施法者 → 恒真（换"要求=不同"仍恒真）✓；**AI 分支未覆盖**（需 AI 拥有的施法者夹具，待补） |

### 两条口径更正（重要）
1. **`ConditionOwnerAI` 的真实语义不是"是不是 AI"**，而是：**对真人玩家恒真（不受限）；仅当施法者是 AI 时**，才要求"目标与施法者归属相同/不同"（`oper`）——它是**约束 AI 选目标的护栏**（防止 AI 把法术浪费在自己身上），不是玩法规则。→ 迁移时对真人玩家路径**无行为影响**。
2. **战场每侧 6 格**（`Slot.x_min=1, x_max=6`，`GetAll` 闭区间）——之前文档里写的 5 格是错的。

### 改动文件（可用 git 回溯）
- `Assets/TcgEngine/Resources/NodeDoc.xml`（新增 2 个 ActionComment，纯追加）
- `Assets/TcgEngine/Scripts/Workshop/Graph/NodeDocRunner.cs`（101002 扩属性名 + `CountEmptySlots` + 202050 动作 + 101030 取值 + 白名单）
- `Assets/TcgEngine/Scripts/Workshop/UI/GraphEditorPanel.cs`（SupportedNodeIds 加 202050 / 101030）
- `tools/node_inventory.tsv`（登记 2 个节点）

### 剩余缺口（下一步 P1）
`OnAfterAttack`/`OnBeforeDefend`/`OnAfterDefend`/`OnKill`/`OnDeathOther` 五个触发入口、横置/解除横置、获取装备者、最近目标/最近召唤、槽位集合、添加种族·关键词、清空暂存区。

