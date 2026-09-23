---
name: tcg2-cardpool-migration
description: TCG2（Unity 2022.3）内置卡牌迁移项目——把 155 张内置卡改造成规则图（节点编辑器）+ 基础卡池导入。当出现"继续迁移/转换器/差分验证/base_pool/converter_report/卡池导入/批N迁移/Phase 5-7/新增节点"等语境，或要修改 tools/probe/ 下的转换器与探针、继续推进迁移批次时使用。含当前进度、架构定案、踩坑清单与验证工作流。
---

# TCG2 内置卡迁移（规则图 + 基础卡池）

## 项目目标

把游戏本体的 155 张内置卡（290 个 AbilityData）从 `Resources/Cards` 剥离，规则用**节点编辑器（规则图）**表达，数据/资源走**基础卡池**（`.tcgpool`）导入。用户已拍板的决策：

- **范围**：155 卡 + 5 副 DeckData + 5 个 PackData 一起进基础池
- **资源**：图片/音频/特效全部随池（特效改 id 引用，DTO 补字段）
- **节奏**：逐批切换，每批可玩可回滚
- **分发**：StreamingAssets 本体只读 + 玩家 Mod 池叠加（Workshop）
- **旧 80 个 Effect/Condition/Filter 子类先保留**，图覆盖 100% 后再清理

## 当前进度（165 个能力 / 155 卡）

| 通道 | 数量 | 说明 |
|---|---|---|
| 图（节点编辑器） | 98 | 差分验证 **97 一致 + 1 跳过（bear 的 ChoiceSelector 夹具不支持）、0 DIFF、0 warn** |
| 数据直通 | 64 | 48 个 Ongoing（光环/装备/持续）+ 16 个纯状态（status_ids） |
| TODO | 3 | corruption_potion（trigger=None 无触发源）/ graveyard（脏数据 41）/ **lava_beast（能力引用为 null，项目既有数据问题）** |

**已完成的批次（2026-09-19/20，十二轮 Play 验证，35→56→61→64→70→72→85→98→99→98）**：
- `ConditionSlotEmpty` ×14 → 112001 布尔常量。**语义要点**：旧类只有 **Slot 目标** 才真查槽位；Card/Player 目标走 `CompareBool(false, oper)` = 与目标无关的常量（本批 14 个全是卡目标）。槽位目标仍 TODO。
- `ConditionOwnerAI` ×1 → 101030。**caster/target 两个卡口必须显式接线**（不接线的口运行期 fallback 成"本次目标"，会把目标当施法者）。
- 入口触发时机：`OnPlayOther` / `OnAfterAttack` / `OnKill` / `OnDeathOther`（转换器 `TryEntryAction` **+** CardPoolIO `MapGraphTrigger` 双登记，少一处就"触发器未支持"静默跳过）。
- `ConditionOnce` ×3 → 入口字段 `once_per_turn=true` → `ApplyEntryOverrides` 挂回 `ConditionOnce`（**发动时**由引擎 `AreTriggerConditionsMet` 判 `ability_played`；图里判必恒假，因为效果期该 id 已 Add 进去）。
- 目标模式 `AbilityTriggerer`（触发者）+ 差分夹具支持（近似=敌方首个角色）。`card.GetCardTargets` 的 AbilityTriggerer 分支吃 `game_data.ability_triggerer`，引擎在 `ResolveCardAbility` 里已赋值。
- **目标过滤 ×3（reaper/spell_stomp/Plague_Rat）走"数据直通"**：过滤器要看**整个目标集合**，与"图是目标相对的"冲突，故 `CardEffectData.filters_target`（DTO）→ 编译侧还原 `ab.filters_target`，引擎在 `GetCardTargets/GetPlayerTargets/GetSlotTargets` 里逐条 `FilterTargets`（与旧能力逐行一致）。转换器不再拦 filters。
- **编译冒烟探针（新）抓出并修掉一个真 bug**：`ActivateAbility` 入口的 `target_mode` 被 `ApplyEntryOverrides` 的"无目标槽 = 无目标"兜底覆盖成 `ab.target=None`（dark_stallion/phoenix/hero_fire/forest/water 五张开起来打空）。修法：无目标槽且入口带显式 `target_mode` → 直接 return，不再走旧启发式。
- **条件一律走"数据直通"（D 批 4，架构性决定）**：`CardEffectData.conditions_trigger/conditions_target` → 编译侧还原成 `ab.conditions_*`，与图守卫**并存**。理由：条件同时决定「目标能否被选中/高亮」与「CardSelector 候选列表」（`GetCardTargets`/`IsCardSelectionValid`），图守卫只能事后挡、替代不了；原样交回又 = 与旧能力逐行一致。图里表达不了的条件（ConditionCount/SlotRange/阵营·种族/SelectedValue…）因此**不再让整条能力作废**（先按守卫转一次，失败再按"纯数据条件"转一次）。
- **目标模式 CardSelector** ×4（firefox/raccoon/necromancer/resurrection_swamp）+ **项目内动作 PlayCardFree**（= 旧 `EffectPlay` 逐行：移入施法者方手牌 → 随机空槽 → `PlayCard(skip_cost)`；NodeDoc 的 210002 是 PlaceCardOnBoard，落点/触发/last_played 都不同，不能替代）。
- 顺带修掉：**ConditionCount ×2 / 类型含阵营·种族 ×1 / ConditionSelectedValue 的"条件"部分**（ghoul/woodland/firefox/raccoon，走数据直通后直接可转）。
- **目标模式 AllSlots ×2**（wolf_alpha/fishing_rod，D 类收官）：引擎逐槽解析（槽位条件 `ConditionSlotEmpty/Range/Owner` + `FilterRandom/FilterFirst` 全走数据），槽位经 **`NodeDocRunner.Run(..., target_slot:)` → `ctx_target_slot`** 带进图；`202003 创建衍生卡并置入战场` 优先用该上下文槽（旧 `EffectSummon.DoEffect(slot)` 就是直接落传入槽），没有上下文才退回"首个空位"。差分夹具同步支持 AllSlots（条件+过滤器筛槽、旧路径走槽位重载）。

- **B 批产品缺节点（+13 张，85/18）**：新增 6 个项目内动作 + 2 个现成节点映射 + status 数据直通 ——
  `SetCardExhausted`（旧 EffectExhaust）、`AddManaNoClamp`（旧 EffectMana 加当前灵力不钳制）、
  `ClearCardStatus`（旧 EffectClearStatus，留空=清空全部）、`ResetCardToBase`（旧 EffectResetStat，≠202024）、
  `SetCardHpClearDamage`（旧 EffectSetStat(HP)：hp=value;damage=0）、`SendToPileRaw`（旧 EffectSendPile：移入+`Clear()`）、
  `202029 变形为卡牌定义`（旧 EffectTransform）、`202050 添加技能`（旧 EffectAddAbility，**池内 id 引用**）、
  以及 `CardEffectData.status_ids`（能力自带 status，编译侧还原 `ab.status`，旧引擎按目标施加）。
- **`SendToPileRaw` 是本轮最有价值的发现**：原本"移回手牌/置入墓地"直接套 `210003/210005`，差分（快照加强后）逮到不等价 ——
  旧 `EffectSendPile` = 移入目标区域 + **`card.Clear()`**（清状态/增益/常驻、复位基础值）、**不触发** OnDraw；
  而 210003 会触发 OnDraw 且不清卡、210005 走 DiscardCard（触发死亡/弃牌链）。→ 新增严格等价的项目内动作。

- **C 批 = 连锁（+13 张，98/3）**：链条**走数据直通**（`CardEffectData.chain_ability_ids` → 编译侧还原 `ab.chain_abilities`）——
  旧引擎在 `AfterAbilityResolved` 里 `foreach chain_abilities → TriggerCardAbility(chain, caster)`，是**引擎侧**行为、与图无关，
  原样带走即逐行等价；被引用的连锁能力资产=池内 id 引用（Phase 5 随池打）。同时新增：
  `RepeatAbilityById`（=旧 EffectRepeat：按 selected_value/固定值逐次 `TriggerAbilityDelayed`）、`RollValue`（=旧 EffectRoll）、
  目标模式 `选择器`→`ChoiceSelector`（bear：**只带连锁的"壳能力"**，入口无动作也要编译 → `CardPoolIO` 的 `has_chain_only`）、
  `AddStat` 目标白名单补 `AbilityTriggerer`（shadow_assassin）。
- **本轮又逮到一个真 bug（差分+快照加强才发现）**：`damageSource` / `source` **未接线**时，
  `ResolveInputCard` 的无连线兜底是"本次目标" → 伤害源变成**被打的那张卡自己**（同玩家）→ `KillCard` 不记击杀、
  不触发 OnKill、攻击者归因错。已把 4 处伤害动作（202001/202041/202034/202012/202036/202043 族）改成显式传 `null`
  让兜底落到**施法卡**。触发证据：`dragon's_anger` 旧路径 `P0.kills:0→1`、新路径无。

- **入口命名修正（2026-09-20，用户当场指出）**：转换器原来把 `AbilityTrigger.OnPlay/OnDeath/StartOfTurn/…` 直接写成了**原始 action 名**，
  但编辑器节点库里根本没有这些名字（只有 zmcs 入口：主动效果 `ActivateEffect` / 起动式 `ActivateAbility` / 光环 `AuraEffect` /
  被动 `PassiveEffect`+亡语 / 事件效果 `EventEffect`+监听事件）→ 图里显示成**裸 id**、且拿不到入口专属 UI（目标槽等）。
  已按对照表重做（详见 `references/pitfalls.md` C8），并新增**逐卡 trigger 多重集对账**（编译出的 trigger 必须等于旧能力 trigger，当前 0 不一致）。
- **零头（+1 张，99/2）：卡牌定义目标（AllCardData）通道 + 旧 EffectCreate** ——
  ①`EffectRunGraph` 补 `DoEffect(..., CardData)` 重载（**此前缺失 → AllCardData 目标的图能力静默不执行**，基类空实现）；
  ②`Run(..., target_define:)` → `ctx_target_define`，`ResolveInputDefine` 无连线时回退"本次定义"；
  ③项目内动作 `CreateCardFromDefineRaw`（=旧 EffectCreate：`Card.Create(定义, caster.VariantData, 玩家)` + `last_summoned`，只处理 牌库/手牌/墓地/暂存区 4 个区域）。
  ④差分夹具的 AllCardData 分支原来是**错的**（当成"所有区域的卡"）→ 改成与引擎同规的**定义集合**（`GetCardDataTargets`），两条路径各自按定义结算；dragon_egg 实测 `P0.n_temp:0→4` 两边一致。

TODO 分类（**3，之前误报为 2**）：corruption_potion（`AbilityTrigger.None`：旧引擎**没有**任何自动触发源，疑为被删/外部引用 → 迁移任何映射都会让它开始触发，**不臆造**）、graveyard（脏数据 41）、**lava_beast（`abilities[0]` 是空引用 = 旧资产脏数据，之前漏报）**。**D 类、B 类、C 批全部清零。**

后续 Phase：**5** 打包 .tcgpool + StreamingAssets 只读来源 + 版本校验；**6** DeckData/GameplayData 改按 id 引用、移除 Resources/Cards；**7** 按覆盖率清理旧子类。

## 核心工作流

源码都在 `tools/probe/`（不进 Assets），用时拷到 `Assets/TcgEngine/Scripts/__TempProbe/` 编译（编译验证流程见 pitfalls）。

1. **转换器** `AbilityToGraphConverter.cs`：建 `tools/convert_flag.txt` → 进 Play 一次（AfterSceneLoad 自触发，无需对局）→ 自动写 `tools/base_pool_v1.json`（卡池）+ `tools/converter_report.tsv`（逐能力 ok/data/todo + 原因）→ 退出 Play 后 flag 自动删除。
2. **差分验证** `AbilityDiffProbe.cs`：建 `tools/diff_flag.txt` → 进 Play → 自动起夹具、旧路径（DoEffects 逐目标）vs 新路径（NodeDocRunner.Run）对跑 → 写 `tools/diff_runtime_result.tsv`（列：结果|卡|能力|入口|目标模式|旧差分|新差分|说明）→ 跑完自动删 flag。
3. **编译冒烟** `PoolCompileProbe.cs`（★编译侧改动的必跑项）：建 `tools/pool_smoke_flag.txt` → 进 Play → 走 `CardPoolIO.BuildCardData`（=真实导入同一函数：**数据直通能力 + 图编译能力**，两条通道都落行）→ 写 `tools/pool_compile_smoke.tsv`（列含 condTrigger/condTarget/filters/status/effects/**chains**）。自动断言：效果图数=编译能力数、过滤器 DTO 数=编译后、**连锁 DTO 数=编译后（99 时 15=15）**、**触发条件对账（DTO 28 = 编译后 28、21 张带条件的卡全部还原）**、**入口 target_mode 期望 vs 编译 ab.target（多重集比对）**、每卡"ok 行数 vs 效果图数"。**它抓过真 bug（"target_mode 被兜底覆盖"），编译侧改动别只看差分全绿。**
3b. **条件对账** `tools/check_condition_fidelity.ps1`（改条件通道后必跑）：把冒烟 TSV 里编译出的 `condTrigger/condTarget` 与迁移前基线 `tools/card_baseline.tsv` 逐能力比对（类名多重集；字段值人工抽查）。**先按能力 id 精确匹配**（数据直通能力 id 与基线同名），剩下的图编译能力（`graph_*_node*`）再按顺序配 `ok/data` 行。当前 **checked=162（按 id 精确匹配 64）mismatch=0**。
   ⚠️ **2026-09-20 的教训**：这两步原先**只覆盖"图编译能力"**（脚本过滤 `id -like '*_node*'`、探针只反射 `CompileGraphAbilities`）→
   **数据直通卡（Ongoing/装备/纯状态）的触发条件/过滤器/连锁从来没被验过**（`pufferfish / black_widow / trap_spike / vampire` 都在基线里带条件）。
   现已两边都扩到两条通道；同一天同一原因还漏报过 `lava_beast`（TODO 实际 3 条不是 2 条）。
   **凡"某条通道的产物"必须单独确认验证覆盖到它，否则就是"绿而没验"。**
3c. **连锁勘察** `tools/chain_report.tsv`（转换器顺带产出）：逐能力 dump 连锁/被引用能力（`chain_*` 资产**不在** baseline TSV 里，只能运行时取）。
3d. **节点库一致性审计** `PoolNodeLibraryAudit.cs`（★用户硬要求：池里每个节点必须是节点库里可选的节点）：
   建 `tools/library_audit_flag.txt` → 进 Play → 写 `tools/library_conformance.tsv`。
   判据=编辑器同一套数据（`NodeDocDb.All` + 反射 `GraphEditorPanel.AllPresets()`）：节点必须在库里、非过时、未被同名顶掉、`supported=true`，且**类型与名称与库一致**。
   当前：**38 种 action / 501 实例，0 违规、0 标题不一致**。详见 pitfalls C10。
4. **节点测试** `NodeBatchProbe.cs`（历史遗留，仍可用）：单节点级测试台。
5. 改动产品代码后必须走编译验证 + 全量日志 grep `error CS`（见 pitfalls 第 1 条）。

★ **探针分工（务必知道）**：差分探针跑「图 → `NodeDocRunner.Run`」（效果等价），**不覆盖** `CardPoolIO.CompileGraphAbilities`（图 → AbilityData：target/trigger/条件/过滤器/状态/白名单）。
所以编译侧的改动 = **冒烟探针 + 差分探针都要绿**；只有动作/条件求值语义的改动才只看差分。两轮 Play 标记可以同时写（冒烟秒级、差分 ~20s），但**必须先进 Play 再等待**：探针只在进入 Play（AfterSceneLoad）时读标记，Play 已开着写标记不会重跑。

★ **差分的三类"假绿"（都已在本项目实测踩到，探针已加固）**：
1. **新路径有未支持的 NodeDoc 动作** → 两边都没干活却判 same。探针现在把它判成 `warn`（不能混进"全绿"）。
2. **快照看不到的维度** → 状态/能力数量原来只在快照里记一个"签名非零计数"，于是"沉默/清状态/隐身/添加技能"全是"(无状态变化)"的 same。现在**逐条状态**（type+value+duration）与**能力数量**都进快照。
3. **效果本身幂等/无前置** → 例如"解除横置"在未横置的卡上 false→false 无差分。探针现在会**前置准备**（EffectExhaust 先把施法卡横置、EffectClearStatus 先挂一个会被清的状态），两条路径同等准备。
   排查 DIFF 时用结果里的「首处不同@N」窗口，不要靠加长截断猜。

★ **改 .cs 时不要并行下发同一文件的多个 replace**（实测丢改动：改动报告成功但内容没落盘，随后被"绿"掩盖）。改完关键文件后 grep 关键字确认落盘。

## 架构定案（改代码前必读）

- **引擎逐目标调用图**：`CardPoolIO.CompileGraphAbilities` 把入口 `target_mode` 原样编译为 `ab.target`（CardPoolIO.cs ~L848-853）→ 引擎解析目标集合后**每个目标调一次** `EffectRunGraph.DoEffect`。因此**图是"目标相对"的**：条件/动作的 card 口不接线 → fallback target_card；**图内不得自枚举目标集合**（筛选+遍历会重复施加，实测 +2 变 +6）。只有入口 `target=None` 的图才允许自定位。
- **状态不节点化**：status（嘲讽/潜行等 StatusData）走 DTO 的 `status_ids`（CardCustomData L170），编译侧 CardPoolIO L1484-1491 还原。规则进图、数据进 DTO。
- **条件"数据直通 + 图守卫"并存**（D 批 4 定案，重要）：`CardEffectData.conditions_trigger/target` 一律把原始条件数组带回 `ab.conditions_*`（引擎在发动前/解析目标集合时判定），图守卫（能表达时）作为图内可读表达保留。**为什么不能只靠守卫**：条件同时决定「目标能否被选中/高亮」与「CardSelector 候选列表」（`GetCardTargets`/`IsCardSelectionValid`），守卫是"事后挡"，替代不了；而原样交回 = 与旧能力逐行一致。副产品：图里表达不了的条件不再让能力作废（`TryConvert` 先守卫、失败再纯数据）。**动条件通道后必须跑 `tools/check_condition_fidelity.ps1` 对账基线。**
- **Ongoing 不走图**：Ongoing 管线不执行 Flow（CardPoolIO L757 警告在案）→ 光环/装备/持续能力整体直通 `abilities[]`（全保真 SerializeComponents/DeserializeComponents）。产品侧 AuraEffect 图路径（BuildAuraAbility）硬编码 value=1、只生成归属条件，表达不了 value=-1/种族条件，暂不可用。
- **旧引擎触发期语义**：`conditions_trigger` 里只重写了 `IsTargetConditionMet` 的条件类（ConditionOwner/ConditionCardType/Self/SlotEmpty/CardPile/Target 等）在触发期**空转**（基类恒真）；只有 5 个类是真活条件：ConditionCount/ConditionOnce/ConditionRolled/ConditionSelectedValue/ConditionTurn。转换器用 `IsLiveTriggerCondition()` 反射判定，空转条件跳过（与旧行为一致）。
- **多目标与条件**：目标条件 → `212001 分支动作` 守卫（每次 Run 守自己的 target_card）；多条条件 → `112005 逻辑运算(且)` 串接；取反 → `112005 operator=非`。
- **槽位随 Run 带进图（AllSlots/落点）**：图内没有"槽位取值通道"，所以 `EffectRunGraph.DoEffect(..., Slot)` 把引擎选好的槽经 `Run(target_slot:)` 放进 `ctx_target_slot`，由落位类动作（现仅 `202003`）消费；槽位的**筛选**依旧全交给引擎（条件+过滤器走数据）。新增"按槽位落位"的动作时记得同样优先读 `ctx_target_slot`。
- **整局不可复现**：洗牌走 System.Random、AI 在后台线程 → 差分判据 = 受控夹具 + 逐例重置 + 状态差分比对（不能整局字节比对）。

## 交付物 / 数据文件（tools/）

| 文件 | 内容 |
|---|---|
| `base_pool_v1.json` | 转换器产出的卡池（effects[]=规则图，abilities[]=直通能力） |
| `converter_report.tsv` | 逐能力：card/ability/trigger/target/result(ok\|data\|todo)/effects/reason |
| `diff_runtime_result.tsv` | 差分结果（64 例，与 ok 行数一一对应） |
| `pool_compile_smoke.tsv` | 编译冒烟：逐卡"两条通道 → AbilityData"结果（图卡 121 能力 + 无图卡 41 能力）+ 自动断言（效果图数/过滤器/连锁/触发条件/target_mode/ok 行数） |
| `check_condition_fidelity.ps1` | 条件对账脚本：冒烟编译出的 condTrigger/condTarget vs `card_baseline.tsv`（按 id 精确匹配优先；当前 **checked=162 mismatch=0**） |
| `card_baseline.tsv` | 155 卡结构基线（迁移前病历本，逐能力一行） |
| `ability_node_mapping.md` | Phase 1 审计报告：映射表、缺口清单、批次施工单 |
| `card_migration_matrix.tsv` | 逐卡迁移矩阵 |
| `probe/` | 全部探针/转换器源码（真源码在此） |

## 已做产品修复（NodeDocRunner.cs / ReflectionUtil.cs，勿回退）

1. `GetObjectInput`（Object 通道）补 `case "102001": return caster;`——否则 112002 比较里 B 兜底成 target_card，"目标==这张卡牌"恒真。
2. `CompareValues` 卡牌禁止走 `ToString()` 兜底（Card 未重写 ToString，所有卡都是 "TcgEngine.Card" → 任意两卡判 == 恒真）；卡的等值只认 uid。
3. `ReflectionUtil.StringToField` 补 `KeywordData.Get(str)`——缺失时带关键词引用的组件导入静默丢字段。
4. `CardPoolIO`：入口 `target_mode` 显式目标模式 + `ParseTargetMode`（无/自身/施法者玩家/对手玩家/全体玩家/所有角色/双方手牌/所有牌堆/卡牌定义/选择目标/打出目标/触发者；未知→None；旧图无该字段→行为不变）。
5. `CardPoolIO`：入口 `once_per_turn=true` → 编译出的能力挂 `ConditionOnce`（发动时判定，不是效果期分支）。
6. `CardPoolIO.MapGraphTrigger`：补 `OnPlayOther`/`OnAfterAttack`/`OnKill`/`OnDeathOther`（与转换器 `TryEntryAction` 必须同步）。
6.5 `CardCustomData.CardEffectData.filters_target`（新 DTO 字段）+ `CardPoolIO` 编译侧还原 `ab.filters_target`（数据型过滤器：引擎解析目标集合时应用）；`ApplyEntryOverrides` 的 `once_per_turn` 提到早退之前（与入口类型无关）+ **无目标槽且有 `target_mode` 时不再按旧启发式覆盖 `ab.target`**。
7.5 `CardCustomData.CardEffectData.conditions_target/conditions_trigger`（新 DTO 字段）+ 编译侧还原（`AppendConditions`），转换器**一律**把原始条件数组带走（见架构性决定）；目标模式 `卡牌选择`→`CardSelector`、`所有槽位`→`AllSlots`；项目内动作 `PlayCardFree`（`NodeDocRunner` 执行通道 + `IsSupportedAction` 白名单 + `GraphEditorPanel.BuildProjectCardPresets` 编辑器预设）。
7.6 槽位上下文：`NodeDocRunner.Run(..., Slot target_slot)` → `ctx_target_slot`（Run 结束还原）；`EffectRunGraph.DoEffect(..., Slot)` 两个分支都带上 `target_slot`；`202003` 有上下文槽就落该槽、否则退回"首个空位"。
7.7 项目内动作（**非 NodeDoc 定义**，`NodeDocRunner` 执行通道 + `IsSupportedAction` 白名单 + `GraphEditorPanel.BuildProjectCardPresets` 预设三处同步）：`PlayCardFree`、`SetCardExhausted`、`AddManaNoClamp`、`ClearCardStatus`、`ResetCardToBase`、`SetCardHpClearDamage`、`SendToPileRaw`。新增动作时**三处都要登记**，否则"未支持的 NodeDoc 动作"静默跳过。
7.8 `CardPoolIO`：数据型状态 `ab.status = ResolveStatusList(effdto.status_ids)` + 入口 `status_ability_value` → `ab.value`（旧引擎用 `ability.value` 施加状态）。
7.9 `CardPoolIO`：数据型连锁 `ab.chain_abilities = ResolveAbilityList(effdto.chain_ability_ids)`；`has_chain_only`（入口无动作但带连锁时也要编译出能力，bear 的"选择菜单"）。
7.10 `NodeDocRunner`：项目内动作再补 `RepeatAbilityById`（=旧 EffectRepeat）、`RollValue`（=旧 EffectRoll）；**伤害源兜底修复**（`source`/`damageSource` 未接线时必须取施法卡，不能落到"本次目标"，否则击杀归属/OnKill/吸血全错 —— 4 处伤害动作已统一）。
7.11 `CardPoolIO.ParseTargetMode` 补 `选择器`→`ChoiceSelector`；`EffectRunGraph` 槽位上下文（D 批）。
7.12 **卡牌定义目标通道**（零头批）：`EffectRunGraph.DoEffect(..., CardData)` 重载（**此前缺失 → AllCardData 目标的图能力静默不执行**）+ `NodeDocRunner.Run(..., target_define:)`→`ctx_target_define` + `ResolveInputDefine` 无连线回退"本次定义" + 项目内动作 `CreateCardFromDefineRaw`（=旧 EffectCreate）。差分夹具的 AllCardData 分支同步改成"定义集合"（原来错当"所有区域的卡"）。
7. `NodeDocRunner`（**工作区未提交，勿回退**）：`202050 添加技能`、`101030 AI限定·目标与施法者同归属`、玩家属性通道 `是否AI`/`空槽位数量`/`是否有空位`、`102027` 的 `基础攻击/基础生命/基础费用`、`202007 设置属性` 的**动作形态**。

详细踩坑清单见 `references/pitfalls.md`（改图/改转换器/排查 DIFF 前先读）。剩余待办明细见 `references/remaining.md`。
