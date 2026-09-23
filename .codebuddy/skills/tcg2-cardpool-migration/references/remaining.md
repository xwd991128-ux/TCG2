# 剩余待办明细（todo=3，2026-09-20 快照）与后续 Phase

数据来源：`tools/converter_report.tsv` 第 7 列（reason）。**D 类、B 类小节点批、C 批连锁、零头批（卡牌定义目标 + EffectCreate）都已清完**，只剩 3 条（2 条"不臆造" + 1 条项目既有坏引用）。

## 已完成（35→56→61→64→70→72→85→98→99→98，十二轮 Play 实测）

| 批次 | 数量 | 落地方式 |
|---|---|---|
| D 批 1 | 15 | ConditionSlotEmpty 14（卡目标=常量）+ ConditionOwnerAI 1（101030） |
| D 批 2 | 12 | 触发时机 4 种（7）+ ConditionOnce 3（入口 `once_per_turn`）+ AbilityTriggerer 2 |
| D 批 3 | 3 | 目标过滤 3（数据直通 `CardEffectData.filters_target`） |
| D 批 4 | 6 | CardSelector 4 + 条件数据直通（ghoul/woodland） |
| D 批 5 | 2 | AllSlots 2（槽位随 `Run(target_slot:)` 进图，`202003` 落该槽） |
| B 批 | 13 | 变形 3、横置 2、无钳制加灵力 2、混合 status 3、SetStat-HP 1、添加技能 2 |
| **C 批（连锁）** | 13 | 链条数据直通（15 张里 13 张落地）+ EffectRepeat 1（RepeatAbilityById）+ EffectRoll 2（RollValue）+ AddStat 白名单 1（shadow_assassin）+ ChoiceSelector 1（bear，壳能力） |
| **零头批** | 1 | dragon_egg：卡牌定义目标（AllCardData）通道 + 旧 EffectCreate → `CreateCardFromDefineRaw` |

验证：差分 **97 一致 + 1 跳过（bear＝ChoiceSelector 夹具不支持）、0 DIFF、0 warn**；编译冒烟全绿（含图 93 卡 / 效果图 98 = 编译能力 98 / 过滤器 8=8 / **连锁 15=15** / target_mode 无差异）；条件对账 **98/98**；**逐卡 trigger 对账 0 不一致**（图入口换 zmcs 入口名后新增的检查）；`read_lints=0`。

### 本轮踩到的坑（都已加固）

1. **`source`/`damageSource` 未接线 → 伤害源落到"被打的卡自己"**（`ResolveInputCard` 无连线兜底＝本次目标）：`KillCard` 因此不记击杀、不触发 OnKill、攻击者归因全错。
   证据：`dragon's_anger` 旧路径 `P0.kills:0→1`、新路径无。已把 4 处伤害动作改成显式传 `null` 让兜底落到**施法卡**。
   **教训**：NodeDoc 的"无连线兜底＝本次目标"对"源/攻击者"类口是错的语义，新增这类口一律显式给 `null`。
2. **只带连锁的"壳能力"**（bear 的选择菜单：入口无动作、菜单＝chain_abilities）会被两道闸门丢掉：转换器的"无效果"闸门 + 编译侧的 `has_node_doc` 闸门。两处都按"带连锁即放行"处理。
3. 连锁能力（`chain_*` 资产）**不在** baseline TSV 里 → 新增 `tools/chain_report.tsv`（转换器顺带 dump）才能勘察语义。
4. 差分探针**不跑链条**（两个通道都只跑 DoEffects / Run，链条是引擎 `AfterAbilityResolved` 的事）→ 链条的验证靠**编译冒烟的 chains 列 + 连锁 DTO/编译后计数断言**，不能靠差分。
5. **AllCardData（卡牌定义目标）有两处长期错误，本轮一起修掉**：
   - 引擎侧：`EffectRunGraph` 缺 `DoEffect(..., CardData)` 重载 → 落到基类空实现 → **AllCardData 目标的图能力静默不执行**（动作一个不跑、也不报错）。现在补了重载 + `Run(target_define:)` + `ctx_target_define` + `ResolveInputDefine` 无连线回退"本次定义"。
   - 夹具侧：差分探针的 AllCardData 分支原来当成"所有区域的卡"（`AllCardsAllPiles` 同款）→ 喂给旧路径的是卡对象、走的是 Card 重载 = **假夹具**（比对无意义）。现在与引擎同规：`GetCardDataTargets` 的定义集合，两条路径逐定义结算。
   实测证据：dragon_egg 两边都 `P0.n_temp:0→4`（4 个龙定义各创建一张进暂存区）。

## 剩余 3 条

| 卡 / 能力 | 原因 | 处理建议 |
|---|---|---|
| corruption_potion / spell_debuff2 | `AbilityTrigger.None`（+target=LastTargeted） | 旧引擎**没有任何**自动触发源（`TriggerCardAbilityType` 按 trigger 匹配；`chain_report` 里也没有任何卡引用它）→ 疑为被删卡/外部引用。**迁移时任意映射都会让它开始触发**（改变行为）→ 保持原状，等确认资产历史意图再定 |
| graveyard / graveyard_draw | 脏数据：`AbilityTrigger` 值 `41` 未定义（40=OnDeath / 42=OnDeathOther） | 旧引擎里它**永不触发** → 需查资产历史意图，不臆造语义 |
| lava_beast / (引用0) | **能力引用为 null**（卡资产 `abilities:[after_spell_attack2]`，磁盘 guid 有效，但运行时槽位是 null；引擎 `DataLoader.CheckCardData` 也在报 `lava_beast has null ability`） | 项目既有数据问题：在编辑器里打开 lava_beast 重新指定「热血」能力并保存（或 Reimport 该能力资产）即可；转换器已加守卫记 TODO（不再静默吞） |

## 覆盖盲区（留待 Phase 5 真对局冒烟）

- ✅ **已修（2026-09-20）：验证只覆盖"图通道"的假绿盲区。** 冒烟探针原先只反射 `CompileGraphAbilities`、
  对账脚本只取 `id -like '*_node*'` 的行 → **数据直通卡（Ongoing/装备/纯状态）的触发条件/过滤器/连锁从来没被验过**
  （`pufferfish / black_widow / trap_spike / vampire` 等在基线里都带条件）。现在：
  探针走 `CardPoolIO.BuildCardData`（两条通道都落行，图卡 121 能力 + 无图卡 41 能力），新增「触发条件对账」断言（DTO 28 = 编译后 28、21 张带条件的卡全部还原 ✓）；
  脚本改为**按能力 id 精确匹配优先**（数据直通能力 id 与基线同名）→ **checked=162（按 id 精确匹配 64）mismatch=0**。
  同一天同一原因还漏报过 TODO 条数（实际 3 条，含 `lava_beast`）。
- **bear（ChoiceSelector）**：差分跳过（夹具不支持选择菜单）。它的等价性目前只由冒烟覆盖（target=ChoiceSelector + chain_abilities=[choice_taunt, choice_fury] 正确还原）。
- `spell_submerge` 差分为"(无状态变化)"（夹具给 PlayTarget 的兜底目标不满足其"己方卡"条件，两条路径都空转；其 EffectAddAbility 由 spell_extinct 覆盖）。
- 夹具仍不覆盖 ChoiceSelector / ValueSelector / EquippedCard；PlayTarget 是"兜底挑一张"的近似。
- **全卡池真对局冒烟（导入 .tcgpool → 实际打一局）尚未做**，建议与 Phase 5 一起做：能覆盖"编译侧 + 引擎侧 + UI 交互"三层（尤其链条、选择器、UI 目标高亮）。

## Phase 5：打包与加载（架构定案已锁）

- 基础池打成 `.tcgpool`（`PoolPackageIO`：pool.json + art/ + audio/，已有 ✓）
- 新增 StreamingAssets 只读来源：启动扫 `StreamingAssets/*.tcgpool` → 基础池标记（只读、不可删、可被 Mod 覆盖冲突需显式策略）
- 版本 + 校验和 + 缺失 id 报告；`CardPoolIO.ImportFromFile` 目前只查 `"cards"` 子串、version 字段存而不用 → 补校验
- 加载顺序：基础池 → 玩家自定义池 → 增益池 → 按钮
- 资源随池：DTO 补特效字段（9 个共用 prefab：Flame×27/PotionFX×12/Phoenix×11/Leaf×8/WaterFX×6/CastFX×4/FlameProjectile×3/TrapFX×2/ElectricFX×2，用 VFXConfig id 引用）
- **被引用的能力资产必须随池打包**（三处已定案为"池内 id 引用"）：
  1. `EffectAddAbility` 的目标能力（play_other_sacrifice / defend_discard）
  2. **连锁能力**（C 批：chain_* / roll_* / activate_chain_* / end_turn_blessing_chain_* / spell_summon_dragon_red_damage_chain 等）
  3. `EffectRepeat` 的目标能力（chain_projectile1）
  它们自身仍是旧系统资产（内部还引用旧 Effect 类）→ Phase 6 前必须一起迁移或打包

## Phase 6：切本体

- `DeckData.hero/cards`（GUID 硬引用）与 `GameplayData.starter_decks` 改按 id 引用（含 fire_deck.asset L17-45 等 5 副）
- `Resources/Cards` 移出工程（先停用不删除：改后缀/挪 `_Legacy` 便于回退）；`CardData.Load` 不再扫内置卡
- `RegisterCard` 冲突策略（现在是冲突即跳过，CardPoolIO ~L1551）→ 基础池可覆盖内置的显式开关
- `CardData.Load` 的 Resources.LoadAll 目录核对
- 被引用能力（上表 3 类）的处理方式要与池打包方案一起定

## Phase 7：收尾

- 图覆盖 100% 后逐个删旧 Effect/Condition/Filter 子类（AI 仍吃编译出的 AbilityData：`AILogic.cs:457-461`、`AIPlayerRandom.cs:135/278` 只读 trigger/target/chain_abilities——`CompileGraphAbilities` 自动生成即可）
- 最终覆盖报告 + 回归

## 工具链现状

- **四件套**：转换器（`convert_flag.txt`）→ 差分（`diff_flag.txt`）→ 编译冒烟（`pool_smoke_flag.txt`）→ 条件对账（`check_condition_fidelity.ps1`）。
- 产物：`base_pool_v1.json` / `converter_report.tsv` / `chain_report.tsv`（C 批新增）/ `diff_runtime_result.tsv` / `pool_compile_smoke.tsv`。
- 差分结果列：`结果(同/DIFF/warn/err/skip)｜卡｜能力｜入口｜目标模式｜旧差分｜新差分｜说明`；说明里带动作数、**首处不同@N** 与两端日志。
- 编译侧改动 = 冒烟 + 差分双绿；改条件通道再加条件对账。

## 已知遗留脏数据（不影响迁移，记录在案）

- 3 条伪造 guid 的孤儿能力（spell_give_flying_ability / spell_silence_buff / chain_destroy_zero_atk）+ 1 条指向已删资产（[molisha]_spells）
- `wolf_alpha` 的光环引用已删除资产的 trait
- resurrection / resurrection_swamp 同文件名不同 id（155 资产 154 基名）
- graveyard 的 `AbilityTrigger` 值 41 未定义（见上）
- corruption_potion 的 `spell_debuff2`（trigger=None，无触发源，见上）
- 探针转换后 `CardCustomData.graph`（卡级旧字段）是空图，真图在 effects[]——新代码一律读 `EnsureEffects()`/effects[]
