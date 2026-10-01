# 踩坑清单（每次排查 DIFF / 改图 / 改转换器前先读）

按"症状 → 根因 → 修法/铁律"组织。全部为实测踩到，非理论。

## A. 图能编译但静默不执行（最危险的一类，零报错零日志）

### A1. 流程输出口类型必须是 Flow
- **症状**：分支执行了但 then 不走（动作数=1）；遍历循环体一次不跑（动作数=0）。
- **根因**：`WalkFlowOutputs`（NodeDocRunner.cs ~L547）对每条出边校验 `out_pin.type == Flow || None`，**`NodeValueType.ActionNode(13)` 会被静默跳过**。
- **铁律**：转换器里 `thenAction`/`elseAction`/遍历的 `action` 等一切流程输出口一律 `AddPin(n, ..., NodeValueType.Flow, true)`。参照已验证写法：tools/probe/NodeBatchProbe.cs `AddLoopBody`（~L1064）有血泪注释。

### A2. 探针必须带"动作数"信号
- **症状**：看不出图没执行还是执行了没效果。
- **铁律**：差分探针把 `ran`（NodeDocRunner.Run 返回值）**无条件**写进结果说明（`新路径动作数=N`），且写在 note 里**不能被后续赋值覆盖**（踩过两次）。
- 注意：`ExecuteForEachNode`（遍历）自身不计数，只有循环体执行才计数。

### A3. GameLog 默认静默——"新日志为空"是假象
- **根因**：`GameLog.Log` 被 `GameLog.Verbose` 开关拦（默认 false，GameLog.cs），而差分探针的 cap 只挂 `Application.logMessageReceived`（Debug 流）。分支/动作结算日志全走 GameLog → 永远采不到。
- **修法**：探针挂载对局后 `GameLog.Verbose = true;`（已在 AbilityDiffProbe 中）。
- FX 警告（"配了特效但没有可用素材"）走 WarnThrottled→Debug ✓ 能采到。

### A4. "Clear on Play" + Play 退出清日志 → 日志要在 Play 内查
- **症状**：差分跑完后查 Unity 日志，只剩编辑器场景日志（96 条卡池导入之类）。
- **铁律**：**在 `mcp.ps1 stop` 之前**、Play 仍在运行时调 get_unity_logs。
- 另：get_unity_logs 的 searchText 对**中文失效**（JSON \u 转义）→ 用 ASCII 关键词（节点 id 如 "cond2"、"112002"）。

### A5. 客户端 FX 回调 NRE（假异常）
- **症状**：差分 6 例报 `Object reference not set...`，堆栈在 `BoardCardFX.OnPlayed → BoardCard.GetEquipCard`（BoardCard.cs:469）。
- **根因**：探针挂载的是真实对局 GameLogic，GameServer 的 `onRefresh += RefreshAll` → 客户端刷新 → 场景 UI 回调炸。与对局状态无关。
- **修法**：探针挂载后置空通知链（`logic.onRefresh/onCardPlayed/onCardSummoned/onCardMoved/onCardTransformed/onCardDiscarded/onCardDrawn/onRollValue/onCardDamaged/onCardHealed = null`，都是普通 UnityAction 字段，GameLogic.cs L24-51）。且 NRE 中断用例会污染后续用例 → 修复后连带 DIFF 消失。

## B. 语义坑（图"能跑"但结果错）

### B1. 112009 不是布尔取反
- `112009 是否不存在` = `GetObjectInput("value") == null`（null 检查）。喂它布尔 false 会被判"存在"→返回 false → 取反静默失效 → 分支不走。
- **铁律**：布尔取反用 `112005 逻辑运算 operator=非`（value1 槽，NodeDocRunner ResolveBoolParamSlots 读编号槽）。

### B2. Card 未重写 ToString → 任意两卡判 == 恒真（产品 bug，已修）
- `CompareValues` 的 == 分支有 `A.ToString() == B.ToString()` 兜底；Card 是普通类，所有实例 ToString 都是 "TcgEngine.Card" → 任意两张不同卡判相等 → 条件链全废。
- **已修**（NodeDocRunner CompareValues）：卡的等值只认 uid，`!(A is Card)` 才走 ToString 兜底。勿回退。
- 定位手段：给 112002 加临时诊断打印 `ca.CardData?.id`（打印 ToString 只会给类型名，没用）。

### B3. Object 通道不认识 102001（产品 bug，已修）
- 112002 的 A/B 走 `GetObjectInput`（Object 通道），原实现对 `102001 这张卡牌` 无分支 → 兜底返回 target_card → "目标==这张卡牌"恒真。
- **已修**：GetObjectInput category 分派链补 `case "102001": return caster;`。勿回退。

### B4. 图是"目标相对"的（引擎逐目标调用）
- 编译链把入口 target_mode 编译为 ab.target（CardPoolIO L848-853）→ 引擎逐目标解析、每目标 Run 一次图。图内用 筛选+遍历 自定位目标集合会**重复施加**（spell_growth +2 变 +6：3 目标 × 每次 Run 又遍历全集合）。
- **铁律**：多目标能力的图 = 目标条件走 212001 分支守卫（card 口不接 → fallback target_card）；动作 card 口不接 → fallback target_card。目标集合归引擎+conditions_target 管。
- 复合加属性（读基础值+加常量+写回）在多目标下同理：getter/setter card 口都不接，靠 per-target Run 的 target_card。

### B5. 触发条件在旧引擎的空转语义
- `conditions_trigger` 里只重写了 `IsTargetConditionMet` 的条件类（Owner/CardType/Self/SlotEmpty/CardPile/Target…）在触发期走基类**恒真**（空转）。真活条件只有 5 个：ConditionCount/ConditionOnce/ConditionRolled/ConditionSelectedValue/ConditionTurn。
- **铁律**：转换器 ② 块用 `IsLiveTriggerCondition()`（反射查 IsTriggerConditionMet 3 参/4 参重载的 DeclaringType）过滤，空转条件跳过——否则照搬进图（还拿 caster 当判定对象）会恒假挡死分支（trap_damage_all2 踩到）。

### B6. 旧效果 ≠ 最像的节点（两处实测不等价）
- `EffectMana`（加当前灵力）**无上限钳制**；`201008 增加当前灵力值` 会 `Clamp(0, mana_max)` → 满灵力时 coin 不等价。缺"无钳制加灵力"节点 → TODO。
- `EffectSetStat(HP)` 会清 `damage` 计数（=满血到该值）；`202037 设置卡牌属性` 不清 → 有伤目标不等价 → TODO（Attack/Mana 已映射 ✓）。

### B7. 条件节点字段口径
- `102032 卡牌类型判断` 的 type 字段要**中文类型名**（随从/法术/英雄/神器/装备/奥秘），写英文枚举名恒假（实测踩到）。
- `105005 卡牌所在牌堆判断` 的 pileName 也是中文内部名（手牌/牌库/墓地/战场/装备/奥秘/暂存区）。
- `EFCardOwner` 的 side=己方/敌方。
- **只声明会接线的 card 口**：声明了却不接 → 运行期解析为 null（条件恒假）；不声明 → fallback target_card（正确）。EmitCondition 里所有条件类都遵守这个模式。

### B8. 集合通道只支持 5 个来源
- `ResolveCollectionNode` 只支持 102013 全体角色 / 102014 友方角色 / 102015 友方随从 / 102016 敌方随从 / 102017 敌方角色。**102012 不在列表里（实测返回空）**。

## C. 工具链 / 环境坑

### C1. 编译验证必须 grep 全量日志
- `get_script_errors` 会**误报 PASS**（踩过多次）。流程：`clear_unity_logs → refresh_editor → sleep 6 → compile_scripts → sleep 34 → get_unity_logs(maxCount≥400)` grep `error CS` 必须为 0。

### C2. PowerShell 5.1 编码
- 无 BOM 的 .ps1 按 ANSI 读 → 中文全乱、语法崩。写完脚本统一加 UTF-8 BOM：
  `$t=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8); [IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true)))`
- `Get-Content -Raw` 在某些管道写法下报参数错误 → 用 `[IO.File]::ReadAllText` 兜底。

### C3. 长命令会被超时取消
- "起 Play → 等待产出"拆成两步：①建 flag/转换 + ②查询。轮询循环 ≤20 次 × 8s。

### C4. mcp 工具调用走 tools/mcp.ps1
- 用法：`powershell -NoProfile -File tools/mcp.ps1 call <tool> '<json>'`；`stop` 退出 Play。
- JSON 参数内的引号转义按现有命令照抄。

### C5. 转换器的往返校验是防线
- TryPassthrough 对直通组件做**不动点校验**（序列化→DeserializeComponents→再序列化逐字段比对）。曾逮到 ReflectionUtil 缺 KeywordData 还原（已修）。新增直通类别时务必保持这套校验。

### C6. 差分探针要点（AbilityDiffProbe.cs）
- 挂载方式：场景对象 Dive 找到真实对局 GameLogic（不是 new 的）→ 必须断客户端链（A5）+ 开 GameLog.Verbose（A3）。
- 逐例：SeedRng → ResetFixture → MakeCaster → ResolveTargets → SimulateDeath(亡语) → 旧路径 ApplyLegacy（含 AreTargetConditionsMet）→ Snapshot/Diff；新路径逐目标 NodeDocRunner.Run。
- ok 行（result=="ok"）按序对应卡池 effects[]；`data` 行（直通）不参与差分配对。
- 报告/结果文件都是 TSV；排查时别只看汇总行，DIFF 行的第 6/7 列（旧/新差分）才是内容。

### C7. "进 Play 要等半分钟"的实测分解（2026-09-19 测：共 ~50s）
- 工具：`tools/probe/StartupTimingProbe.cs`（拷到 `Assets/.../__TempProbe/` 后进一次 Play → 写 `tools/startup_timing.tsv`，
  用 `Time.realtimeSinceStartup` 打点：BeforeSceneLoad / AfterSceneLoad / +1s / +3s）。**测完记得删 Assets 里的副本**（否则每次 Play 都跑）。
- 实测：**BeforeSceneLoad 35.4s**（= 进 Play 的域重载/程序集重载阶段，最大头）/ AfterSceneLoad 38.8s（**+3.4s**：场景加载 + `DataLoader.Awake` 全量数据 + 导入 Workshop 下的池 json）/ 远到第一次 Update 已 50.2s（**+11.4s** 首帧/首屏构建）。
- 根因与建议：
  1. `ProjectSettings/EditorSettings.asset` 里 **`m_EnterPlayModeOptionsEnabled: 0`** → 每次 Play 都做完整 Domain+Scene Reload。
     打开 Enter Play Mode Options（可关 Domain Reload）是**唯一能把这个 35s 砍到几秒级**的开关；代价=静态状态跨 Play 残留，
     项目已部分适配（GameLog 用 `SubsystemRegistration` 复位；探针自己的静态 ctx 也有 Run 的 finally 复位），改前先跑差分+冒烟回归。
  2. Editor.log 已到 **4.25 GB / 6280 万行**（历史刷屏：规则编辑器自检、NodeDoc、探针日志）→ 编辑器写/查日志都变慢，
     排查前先清（Unity 控制台 Clear / 关掉编辑器删 Editor.log）。Unity 运行时占着该文件，外部读不了。
  3. `DataLoader.LoadData()` 每次 Play 都 `Resources.LoadAll` 全部数据 + `CardPoolIO.LoadCustomPools()` 导入
     `persistentDataPath/Workshop/*.json`（本机 sample_pool 994 KB + Elite Pack 161 KB + Dlc1 Pack 28 KB，且每卡还要读 `Workshop/Art` 的 png）。
     这是"进 Play 必付"的 3~4s；Phase 5 做 StreamingAssets 只读池时顺手把它改成"按需/带缓存"。
- 通过 MCP 触发时还有工具链自身等待（`mcp.ps1 play` 日志里的"已加入异步执行队列，请10秒后重试"，实测一次请求→返回 55s，其中编辑器不可用 ~50s）。
- ★ **2026-09-20 已正式开启**（用户要求"修掉点 Play 等 1 分钟"）：`ProjectSettings/EditorSettings.asset`
  `m_EnterPlayModeOptionsEnabled: 1` + `m_EnterPlayModeOptions: 1`（**只关 Domain Reload、保留 Scene Reload**，最稳的一档）。
  实测（`StartupTimingProbe` 打点）：**进 Play 50.2s → 8.7s**；BeforeSceneLoad 35.4s→2.5s、AfterSceneLoad 38.8s→5.7s、首次 Update 50.2s→7.0s；
  `mcp.ps1 play` 往返 55s→10s（不再出现"MCP 可用（等待 50 秒后恢复）"）。
- **回归结论（必须做，已做）**：关域重载后静态状态跨 Play 残留是真风险 →
  跑**连续多局**的差分+冒烟+节点库审计：第 1 局出现 1 处 DIFF（`dragon_egg` 的 AllCardData 逐定义：旧/新路径抽到不同定义，
  疑为"切换开关那一次的过渡残留"），**第 2/4 局全部 0 DIFF**（97 一致+1 跳过）、冒烟 98=98、审计 0 违规 → 判定为过渡现象，非常态。
  以后再改这类开关，**必须跑至少两局连续回归**才算过。
- 静态表安全性已核：`CardData.Load()` 等有 `if (list.Count == 0)` 守卫（不会跨局累加）；`ListSwap.Get()` 每次 `Clear()`（不会返旧数据）。
- 剩余 8.7s 的构成：域切换 2.5s / 场景+`DataLoader` 全量数据+**池导入（每次 Play 重建 155 张池卡 + 编译 98 张图）3.2s** / 首帧 1.3s。
  想再快就给池导入加"文件 mtime 未变则跳过"的缓存（可省 2~3s）——留作可选。
- 回退：`m_EnterPlayModeOptionsEnabled: 0`（或 菜单 Edit→Project Settings→Editor→Enter Play Mode Options 关掉）。

### C8. 图入口节点**必须用编辑器认识的 zmcs 入口名**（原始 action 名会显示成裸 id）
- **症状**：用户在编辑器里看到入口节点写着 `OnPlay` / `OnDeath` / `StartOfTurn` 这种裸 id，会问"明明有主动效果，为什么做了个 onplay"；
  且这些入口**拿不到入口专属 UI**（目标槽「＋新增目标」、触发条件、标签、目标去重只对 `ActivateEffect`/`ActivateAbility` 开放）。
- **根因**：编辑器节点库（`GraphEditorPanel.BuildZmcsEntryPresets` + 事件类入口表）里根本没有 `OnPlay`/`OnDeath`/`StartOfTurn`/`OnAttack`/`OnDraw`/`OnKill` 这些名字；
  那是 `AbilityTrigger` 的枚举名/编译侧映射名。**节点库里只有 5 个 zmcs 入口 + 事件类入口**。
- **对照表（转换器 `TryEntryAction` 已按此实现）**：
  | 旧 AbilityTrigger | 入口 action | 入口显示名 / 必填字段 |
  |---|---|---|
  | OnPlay | `ActivateEffect` | 主动效果入口（战吼/法术） |
  | Activate | `ActivateAbility` | 起动式效果入口（点击发动；可配灵力费用/横置/每回合一次） |
  | OnDeath | `PassiveEffect` | 被动效果入口（**必写 tag_list=亡语**，否则判 None 静默跳过） |
  | StartOfTurn / EndOfTurn / OnDraw / OnBeforeAttack / OnPlayOther | `EventEffect` | 事件效果入口（**必写 event_name** = 回合开始/回合结束/抽到时/攻击时/打出牌） |
  | OnAfterAttack / OnKill / OnDeathOther | 原始名（无等价入口） | 编译侧认识，编辑器显示裸 id |
- **编译侧对应**（改入口名后必须逐卡对账 trigger，不能只看"能编译"）：`MapGraphTrigger(ActivateEffect→OnPlay、ActivateAbility→Activate)`、
  `ResolveEventTrigger(PassiveEffect+亡语→OnDeath、EventEffect→MapEventName)`、`MapEventName(回合开始→StartOfTurn、回合结束→EndOfTurn、攻击时→OnBeforeAttack、抽到时→OnDraw、打出牌→OnPlayOther)`。
- **验证方法**：冒烟 TSV 里编译能力的 `trigger` 与 `converter_report.tsv` 的 ok 行 trigger **逐卡多重集对账**（当前 0 不一致）。
- 另：`ApplyEntryOverrides` 里"无目标槽 + 有 target_mode → 直接 return"的早退对 `ActivateEffect`/`ActivateAbility` 共用，所以换入口**不会**覆盖迁移写出的 target_mode（冒烟会验）。

### C9. 卡的能力引用为空会被**静默吞掉**（"155 卡全覆盖"里凭空少一张）
- **症状**：`converter_report.tsv` 里整张卡一行都没有（不是 todo、不是 data），卡池 JSON 里该卡 `abilities=0/effects=0`。
  实测 `lava_beast`：卡资产 `abilities: [after_spell_attack2]`（guid 在磁盘上完全有效），但运行时该槽位是 **null**
  → 引擎自己的 `DataLoader.CheckCardData` 也在报 `lava_beast has null ability`（项目既有数据问题）。
- **根因**：`foreach (AbilityData ab in card.abilities) { if (ab == null) continue; }` —— 静默跳过 = 报告里看不出来。
- **修法（已加固）**：转换器对 `abilities[i] == null` **记 TODO**（原因"能力引用丢失"）+ `Debug.LogWarning`；
  现在总计 **ok 98 + data 64 + todo 3 = 165**（tdo 三条：corruption_potion/graveyard/lava_beast）。
- **顺带发现（Phase 5 必须处理）**：`persistentDataPath/Workshop/` 下躺着 **`base_pool_v1.json`（3.98 MB）+ `Elite Pack.json`（含内置卡 id）**，
  `DataLoader.Awake → CardPoolIO.LoadCustomPools()` **每次进 Play 都会导入它们** → 运行时可能与内置卡互相覆盖/干扰（lava_beast 要盯这个方向）。

### C10. 池里的每个节点**必须**是节点库里可选的节点（否则又显示裸 id / 呈现不准）
- **要求（用户定案）**：内置卡池用的节点必须全部来自现有节点库；最终卡池界面只能用库里已有的节点，且呈现（名称/类型）要与库一致。
- **工具**：`tools/probe/PoolNodeLibraryAudit.cs`（建 `tools/library_audit_flag.txt` → 进 Play → 写 `tools/library_conformance.tsv`）。
  用**编辑器同一套数据**判：`NodeDocDb.All` + 反射 `GraphEditorPanel.AllPresets()`（含 `hidden`/`supported`/`title`/`type`）。
- **"不可选"的三种状态**（都判违规）：① 完全不在库里；② `NodeDocDef.obsolete` 或**被同名节点顶掉**（`hidden=true`）；③ `supported=false`（库内灰显、不可拖入）。
- **实测抓到的 7 处（501 个节点实例）**：
  1. `202047 治疗目标卡牌` 与 `202013 治疗目标卡牌` **同名**，但 202047 是隐藏的那个 → 必须用 202013（10 处）。
  2. `102004 获取属性` / `112003 整数常量` / `112004 整数运算`：库里是 **Value(3)**，转换器用 `ChainNode/Chain2` 建成了 **Action(2)**（90 处）→ 类型不符=呈现不准（已加 `IsLibraryValueAction`）。
  3. `OnKill` / `OnAfterAttack` / `OnDeathOther`：库里**没有**这三个入口节点 → 已在 `GraphEditorPanel.BuildZmcsEntryPresets` 末尾正式登记（击杀入口/攻击后入口/他人死亡入口，分类「入口」），否则只能写裸 id（7 处）。
  4. 标题必须等于**库里的节点名**：`EFCardOwner`「卡牌归属」→「卡牌归属判断」；`SendToPileRaw` 的自定义标题 →「移入区域(清空状态)」（具体区域放节点字段里）。
- **铁律**：写任何 `action`/`title` 前先确认它在库里（NodeDoc id 或项目预设）且 title 与库名一致（允许「库名：后缀」）；新增项目内节点必须**三处登记**（预设=进库 / 执行 case / `IsSupportedAction` 白名单）。
- **当前状态**：审计 **38 种 action / 501 实例，0 违规、0 标题不一致**。
- 边界：审计只覆盖「节点是否存在 / 是否可选 / 类型 / 名称」，**未覆盖**「引脚与字段是否都在预设里」——要更严再加一层。

## D. 光环（具名增益）链路：排查顺序 + 已修的真 bug（2026-10-01 实战，"每失去1点生命费用-1"那张卡）

### D0. 排查顺序（照这个顺序读日志，一次定位；**先自己跑探针，不要让人反复试**）
1. `[光环编译] 卡=… 增益=… 生效区域=… 作用区域=… 动作线=有 可重算=True` → 光环能力**有没有被编译出来**（没有 = 冷启动解析失败，见 D2）
2. `[光环] 本次应有 N 个目标` → 目标收集到几个（没有 = 来源卡没扫到 / 生效条件挡掉，见 D4/D5）
3. `[光环] 光环施加：buff_x → 目标卡 y` → 增益真的施加了（差分扫描在干活）
4. `[光环] 动作线执行完毕：… 实例变量=[k=v]｜目标费用=… mana_ongoing=…` → **写进了什么变量 + 费用有没有合入**
   - 变量为空（`实例变量=[]`）→ 动作线没写（见 D1/D3）
   - 变量对但费用不变 → 变量→费用的读值/合入环节（`GetSourceValue` → `AddManaCost` → `UpdateOngoingCards` 合进 `mana_ongoing`）

### D1. 英雄卡的生命读取（本轮最后一环）
- 引擎里**玩家血量在 `Player.hp/hp_max`**（开局 `player.hp = player.hp_max`；伤害走 `DamagePlayer` → `player.hp -= …`）；**英雄卡自身的 `hp/damage` 不随伤害变化**。
- 所以图里对英雄卡读「生命 / 最大生命」必须回退到所属玩家：`NodeDocRunner.CardHpForRead(logic,c)`（原有）+ 本轮新增 `HeroMaxHpForRead(logic,c)`。
- 已修：`102007 获取卡牌最大生命值` / `102008 获取卡牌当前生命值`（原来直接 `c.GetHPMax()/GetHP()`）、以及「读取卡牌属性」的 `最大生命/最大生命值` 分支。
- 症状：`英雄最大生命 − 英雄当前生命` **恒为 0** → 变量写成 0 → 费用永不变化。

### D2. 冷启动加载顺序（光环被静默丢掉）
- `DataLoader` 原顺序是「先 `CardPoolIO.LoadCustomPools()`，后 `BuffPoolIO.LoadAll()`」；卡池导入会**立刻编译规则图**，而光环的「增益定义」要按 id 在增益池里解析 → 查不到 → 落到旧 StatusType 分支 → **整条光环被静默丢弃**。
  （"编辑器里是好的"是假象：那次编译发生在增益池已加载之后。）
- 已修：① `DataLoader` 顺序改为**增益池先、卡池后**；② `CardPoolIO.BuildAuraAbility` 兜底：值以 `buff_` 开头时即使查不到也按具名增益编译（运行期 `BuffPoolIO.Get` 再取真定义）。
- 铁律：任何"编译期按 id 查另一张池表"的地方，都要检查 `DataLoader` 顺序。

### D3. 光环「目标增益」输出口：**单数/复数两条通道都要接**
- `206003 设置增益属性` 走的是**复数** `buffs` 口（`ResolveInputBuffs`），而光环分支当初只加在**单数** `ResolveInputBuff` → 复数口返回空 → 206003 退回"本次目标卡" → 报"无目标卡" → **动作线跑了却什么都没写**。
- 已修：抽出 `AuraGrantBuffRef(logic,target_card)`，单数/复数共用。

### D4. 光环来源卡：手牌也算（`GameLogic.CollectAuraGrants`）
- 原先只扫 `hero / cards_board / cards_equip` → 手牌里的卡当光环源（"手牌费用随英雄掉血减少"这类）**永远不生效**。已补 `cards_hand`。

### D5. 光环动作线的"重算"与"生效条件"
- **重算**：动作线原本只在"目标进入范围"时跑一次 → 依赖当前状态的数值（已损失生命 / 手牌数）不跟随。已加 `AbilityData.aura_repeat`（编译期 `CardPoolIO.AllAuraActionsAreRepeatable` 判定：动作线**只含幂等动作**——设置属性 / 设置增益属性 / 控制流——才为 true），为 true 时 `SyncAuraEffects` 在每次收敛点重跑；含伤害/抽牌等一次性动作的线仍只跑一次。
- **生效条件**（cond 口）：`IsEntryConditionMet` 逐候选目标求值，**为假 = 该目标不施加**（不报错）。典型误配：`比较(目标卡牌 > 这张卡牌)` = 两张卡比大小 → 退化成字符串比较 → **恒假** → 光环整体不施加。已加"非数值操作数用 `> < >= <=` 时告警一次"。
- 「只对自己」的正确写法：`比较(A=目标卡牌, B=这张卡牌, 等于)` —— 卡牌**按 uid 判等**（`CompareValues`）。
- 运算符：面板存**中文**（等于/不等于/大于/大于等于/小于/小于等于；与逻辑运算符 且/或/非 同口径）；引擎 `NormalizeCompareOp` 中文与符号都认（旧数据 `==`/`>` 不用改图）。

### D6. 增益属性修改的第三格 = **变量**（不是数值）
- 语义：填数字 = 固定值（兼容旧数据）；填中文 = **变量名** → 存 `BuffPropMod.value_source` → 运行时 `BuffRuntime.GetSourceValue(card, 变量名)` 取当前值（内置属性名 → 该属性当前值；其它名 → 增益实例属性）。
- 「花费 / 减少属性 / ＜变量＞」= 花费 -= 变量当前值；变量由图的 `206003 设置增益属性` 写入；变量一变，`SetBuffPropWithEvent → BuffRuntime.Reapply` 会立刻重算。
- 坑：保存前必须按**界面文本**回写模型（`GraphEditorPanel.CommitBuffModValuesFromUI` + 全角数字归一），否则"填完直接点保存"会丢值（存成 0）；变量来源不进旧 `props`（`SyncLegacyProps` 跳过），也不被 `NormalizeMod` 改写成"设置为"。

### D7. 面板"未保存确认"弹框的假脏
- 症状：什么都没改，点 ✕ 也弹框。根因：编辑会话基线取在**表单程序化填充之前**，填充触发的控件回调被记成"有改动"。
- 修：基线**延后一帧**取（`session_pending` → 在 `Update()` 里取 JSON 快照 + 清脏 + 挂控件监听）。

### D8. 光环费用探针（遇"对局不生效"必须先跑，别让人反复试）
- 源码归档 `tools/probe/AuraCostProbe.cs`：拷到 `Assets/TcgEngine/Scripts/__TempProbe/` → 建 `tools/aura_cost_flag.txt` → **进 Play**（探针只在 AfterSceneLoad 读标记）→ 写 `tools/aura_cost_result.tsv` → 跑完自动删标记；跑完把临时文件删掉、重新编译。
- 它按**真实对局口径**建夹具：手牌光环源 + **英雄类型卡**（`CardData.type == Hero`）+ 血量在 `Player.hp/hp_max`（英雄卡字段不动）+ 直接加载用户的真实卡（`custom_ZJixIZiYOx4` / `buff_90f3ca8b`），断言 `20→19→18`。
- 铁律：**先写/跑探针把"哪一环断了"钉死再改代码**；"改完让用户进对局看"是最后一步，不是第一步。

## E. "对敌人分配 N 点伤害"（复仇之怒类）+ 卡面样板行（2026-10-01 实战）

### E1. `202012 造成固定法术伤害并分配给随机目标` 会**丢失伤害**
- 原实现：`targets` 只解析一次 → `for (i=0;i<value;i++) DamageCard(caster, targets[RandInt(0,count)], 1, true);`
  → 目标被打死后**仍会被随机挑中**，伤害落到死卡/已离场卡上 = 白打（8 点只打出 5~6 点）。
  客户端表现就是"伤害数字/血量变化少于卡面"。
- 修法（已修）：① 每次只从**存活**目标里挑；② 命中后若已死**立刻移出候选池**；
  ③ 候选池空 → 剩余点数**打脸**（"敌人"含英雄；连敌方玩家都没有才告警丢弃）；
  ④ 存活判定必须用 `NodeDocRunner.CardHpForRead(logic, card)` —— 英雄卡的血在所属玩家身上，
     直接 `GetHP()` 恒为 0，会把英雄当"已经死了"整段跳过（见 D1）。
- 探针：`tools/probe/WrathProbe.cs`（读卡池 JSON 直接跑用户那张卡的图）。
  实测：3 个敌方随从（血 3/5/2）+ 敌方英雄 30/30 → 随从受伤 4（死 1）+ 打脸 4 = **总计 8** ✓。

### E2. 卡面/悬浮提示里多出一行 "XX入口：规则图执行"（显示异常）
- 根因：`CardPoolIO` 编译图能力时写 `ab.desc = ab.title`（样板文字），而 `CardData.GetAbilitiesDesc()`
  会把"**有 desc** 的能力"渲染成一行 → 卡面多出 `主动效果入口：规则图执行: 主动效果入口：规则图执行`。
- 修法（已修）：① 图编译能力的 **desc 默认留空**（4 处：主入口 / 旧式光环 / 具名增益光环 / 被动生效失效线）；
  ② `ability_desc` 字段提升为**所有入口**生效（原来只有"起动式入口"读它，主动效果入口填了也没用）；
  ③ 「主动效果入口」预设补上「能力名 / 能力描述」两个字段（空=卡面不显示这行）。
- 回归检查：`base_pool_v1.json` / `sample_pool.json` 里非空 `ability_desc` **条数=0**（转换器写空）
  → 清样板行对内置迁移卡无影响。
- 铁律：**任何"往 AbilityData.desc 塞兜底文字"的地方都会被渲染到卡面上**；兜底请用空串，让数据决定显示。

## F. 全量逐节点回归：怎么跑 + 已硬化（2026-10-01）

### 跑法（现在可以无人值守）
1. 用例：`tools/gen_all_cases.ps1`（`node_inventory.tsv` 304 节点自动生成 + `curated.tsv` 人工覆盖）→ `tools/all_cases.tsv`（312）→ `tools/gen_batch.ps1 -Overrides tools/all_cases.tsv` → `tools/node_batch.json`。
2. 探针 `tools/probe/NodeBatchProbe.cs` 拷到 `Assets/TcgEngine/Scripts/__TempProbe/` → 编译 → 进 Play → 自动写 `tools/node_batch_result.txt`（首行有批次名才算本次结果）。

### 两处已硬化（不硬化就每跑必挂）
1. **探针自己开局**：原实现要求"已有进行中的对局"，纯菜单启动会 `❌ 没找到 GameLogic`。
   现在会自己拼测试卡组 + `MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual)`（与编辑器「模拟测试」同一条路径）。
2. **测试增益自给自足**：用例写死 `buff_8c5cfda5`，原实现要求**玩家增益池里有这个 id**（池一改 `106002/206001` 全挂）。
   现在探针自己 `BuffPoolIO.New()` + 反射把 `buff_dict` 的键改成固定 id，并在每条用例前确保"已施加"。
   （另：探针里 `BuffRuntime.AddBuff(caster, def, 99)` 是**过时签名**，现为 `AddBuff(logic, card, def, duration)`。）

### 本轮扫出的真 bug
- `103025 获取卡牌定义所属卡池`：池卡导入时 `CardData.packs` 被置成空数组（`CardPoolIO.BuildCardData`），
  而该节点只读 `packs[0]` → 对**所有池卡**恒返回空。
  修法：`NodeDocRunner` 的 103025 分支在 `packs` 为空时回退 `CardPoolIO.PoolIdOf(card_id)`（返回所属卡池文件名）。
- 当前结果：**312/312 通过**（304 节点 + 回归族）。

## G. 语义断言探针（冒烟 ≠ 语义）+ 自建小图的三个必填项（2026-10-01）

### G0. 为什么必须再做一层
`node_batch` 的 312 条里 **289 条是冒烟级**（`value_notnull` 162 / `exec` 92 / `value_any` 35）——只证明"不崩、返回非空"，
**不能证明语义**。真正带期望值的只有约 23 条。所以"312/312 通过"不等于节点正确。
→ 新增 `tools/probe/SemanticProbe.cs`：把待测节点接进「202001 造成伤害」的 `value` 口，
用**目标玩家掉血量**当观测量（精确可测），于是"取值对不对"变成**会失败**的断言。

### G1. 自建小图跑不通的三个必填项（都实测踩过：伤害/取值全 0）
1. **必须给节点填 `category`**：引擎按 `category` 分发（`NodeDocDb` 里按 `define_id` 查），空 category 的节点会被整段跳过。
2. **合成对局必须 `game.state = GameState.Play`**：非 Play 状态下伤害/增益等入口直接返回。
3. **必须传目标卡**：`NodeDocRunner.Run(logic, graph, host, target_card, …)` —— 伤害类动作没有目标上下文时不造成伤害。
   （另有：合成夹具里 `p0/p1.hero` 都要造出来，玩家血在 `Player.hp/hp_max` 上。）

### G2. 用例本身写错的三种坑（会被误判成"产品 bug"）
- `112004 整数运算`：字段是 `operator`，操作数在 **`arg`（params 口，多条入线都要给）**，输出口叫 **`result`**（不是 `return`）。
- `102013` 是「**获取所有角色**」（返回卡牌数组），不是"获取手牌数"。
- 观测量要用**增量**：夹具可能不是满血（p0=29/30），用绝对值会凭空多算。

### G4. 第二批语义断言又抓到的两个**真 bug**（2026-10-01，都已修）
1. **未知比较运算符被静默当成「等于」**（`NodeDocRunner.NormalizeCompareOp` 只认中文与 `=`，`≠ ≥ ≤ ＞ ＜ ＝ ！＝` 全漏 ✗，
   且数值分支兜底是 `default: return na == nb`）→ 症状：接线看着对、条件结果静默错（`5 ≠ 5` 判成**真**）。
   修：补齐符号/全角变体 + 未知运算符改为**打告警**（不再静默）。
2. **增益属性双通道重复应用 + 实例值被当绝对值**（`BuffRuntime.ApplyNative` / `ApplyModRule`）：
   · `ApplyNative` 先把旧 `props` 落一遍（`GetProp(ATK_KEY)` → AddStatus），再把 `mods` 落一遍；
     而编辑器保存会 `SyncLegacyProps()`（mods→props 单向回写）→ **同一份修改落两遍**（实测「花费-2」掉 4）。
   · `ApplyModRule` 里"实例覆盖"分支把实例值当**绝对目标值**（`want - 当前值`）→ 规则「攻击/增加属性/1」
     被算成 `1 - 当前攻击(1) = 0` → **增益完全不生效**；规则「花费/减少属性/2」被算成 `-2 - 当前花费(4) = -6` → 花费砸到 0。
   修：① 有 `mods` 时不再走 `props`（二选一）；② 实例值就是**这条规则的数值**，加减方向由 `mode` 决定。
   → 断言（现在全 PASS）：`206001 + mods{攻击+1,花费-2}` → 攻击 3→4、费用 4→2。
- **教训**：只写旧 `props` 或只写 `mods` 都会得到"看着像 bug"的假象；测试增益必须写 `mods` **并** `SyncLegacyProps()`，
  且宿主卡要**放战场**、跑完要调 `logic.UpdateOngoing()`（收敛点），否则攻击/费用都不会变（会误判成产品 bug）。

### G5. 卡池接线审计工具 + 真实卡端到端（2026-10-01）
**工具**：`tools/audit_pool.ps1`（默认审 `LocalLow/.../Workshop/sample_pool.json`）—— 扫"接到不存在的**端口/字段/节点**"这类
**静默失败**（引擎对未知端口/字段是直接忽略，图看着对、运行没反应）。报告写 `tools/audit_pool_report.txt`（UTF8）。
- 跑法：`powershell -NoProfile -File tools/audit_pool.ps1`
- **必须给脚本加 BOM**：PowerShell 5.1 按 ANSI 读无 BOM 的 UTF-8 脚本 → 中文注释变乱码 → 报语法错（实测踩到）。
- 已知**误报**：① `in`/`out` 是引擎自动生成的流口，NodeDoc 里没有；② 入口/内部动作是英文名（OnPlay/ActivateEffect/…）；
  ③ `112004 整数运算` 的操作数是**编号槽** `arg`/`arg2`/`arg3`（引擎 `ResolveIntParamSlots` 认），NodeDoc 只声明 `arg`(params)。

**真实卡端到端**：`SemanticProbe` 里有"真实卡-熔岩人"用例（标题含「熔岩」的卡）：手牌光环 → 英雄损失生命 → 费用下降。
实测结果 **费用 20 → 15（掉 5 血）PASS** —— 手牌光环源、动作线重算、变量、增益落费用这条链现在是通的。

**夹具必填（否则会把"没能力的测试卡"误判成产品 bug）**：
- `Card.GetAbilities()` 读的是**卡实例自己的** `abilities` / `abilities_ongoing`（**不是**定义！`Card.Create` 不会填）
  → 手搓测试卡必须按定义灌能力（`trigger == Ongoing` 的进 `abilities_ongoing`，其余进 `abilities`）。
- `CardData` **没有** `GetAbilities()`，只有 `abilities`（数组）。
- 区域查找（`IsOnBoard`/`IsInHand`…）**按 uid 查** → 手搓卡必须给显式 uid，否则 `KillCard`/`DiscardCard` 之类直接早退。

### G6. 打到**英雄**的伤害绕过护甲/免疫/圣盾（2026-10-01，真 bug，已修）
- `GameLogic.DamageCard` 里"英雄=卡牌 最小路由"原本放在**函数最前面**：
  `if (IsHeroCard(target)) { DamagePlayer(...); return; }` → 后面的 免疫 / 圣盾 / 护甲 判定**全被跳过**。
  而规则图里"目标=英雄"极其常见（`102017 获取敌方角色` 就含英雄卡、法术打脸…）→ **护甲卡对脸完全没用**。
- 同一条根还有第二个面：`DamagePlayer` **自身不判护甲/免疫** → 直接以「玩家」为目标的伤害（节点 target=玩家）也白给。
- 修法：① `DamageCard` 的英雄路由**提前到函数前部但把 `spell_damage` 传下去**（减伤统一在 DamagePlayer 里做，避免两处重复扣护甲）；
  ② `DamagePlayer(attacker, target, value, bool spell_damage = false)` 加护甲/免疫判定（法伤不吃）。
- 断言：`202041` 普通伤害 4 vs 英雄护甲 5 → **掉血 0**；法伤 → **掉血 4**；`202001` 玩家直伤 4 vs 护甲 5 → **掉血 0**（修复前两条都掉满血）。
- 顺带确认：**护甲是"减伤"不是"消耗"**（`value -= 护甲`，护甲值不变）——写期望值别写成"护甲被扣掉"。

### G7. 三份卡池接线审计结论（`tools/audit_pool.ps1`，2026-10-01）
`sample_pool` / `base_pool_v1` / `Elite Pack` / `Dlc1 Pack` / `buffs.json` / `custom_nodes.json` 全部扫过：
命中项**全是误报**（`112004` 的编号槽 `arg` / `arg1` / `arg2` —— 引擎 `ParamSlotName` 槽 1 无旧名时就用 `arg1`，
`ResolveIntParamSlots` 逐槽取值）。→ 卡池侧**没有**接到不存在端口/字段的问题。

### G8. 全卡池端到端冒烟（`tools/probe/PoolSmokeProbe.cs`，2026-10-01）
把**每张卡的每张规则图**都真跑一遍（引擎入口 `NodeDocRunner.Run`，卡分别放**战场**与**手牌**各一次），
记录抛异常 / 找不到入口 / 执行告警。结果：**卡 280 张、图 289 张、执行 270 次、异常 0** ✓。
- `CardPoolIO.GetCustomCards()` 只给**自定义池**；内置池（`base_pool_v1.json`）要**直接读磁盘 JSON**
  （`Application.persistentDataPath/Workshop/*.json` + `JsonUtility.FromJson<CardPoolData>`）才扫得到。
- 内置 155 张卡的图**是空的**（仍走旧 Effect 系统：文件里是 `EffectAddStat`/`ConditionOwnerAI` 这类旧类型）
  → 冒烟里那 154 条"没有入口(Event)节点"**不是 bug**，是那些卡本来就没图（别再当问题修）。

### G9. 「使用卡牌后」入口（OnAfterPlay）新增 + 未保存改动被"洗白"的回归（2026-10-01）
**① 入口原本不存在**（不是"丢了"）：入口预设表 `BuildGraphEventPresets()` 只有 `OnBeforePlay`="使用卡牌时"，
`AbilityTrigger` 枚举也没有 `OnAfterPlay`。要加一个图事件入口，**必须同时改 5 处**（漏一处就是"节点在库里但永不触发"）：
1. `AbilityData.AbilityTrigger` 枚举（图事件段 60 起；本次取 `OnAfterPlay = 64`）
2. `CardPoolIO.MapGraphTrigger`（action → 触发器）
3. `NodeDocRunner.IsEventTrigger` 白名单（漏了 → 事件图不按事件执行）
4. `GraphEditorPanel.BuildGraphEventPresets`（节点库预设；`pins` 由循环统一给 cond/out/card/player/pos）
5. **广播点**：`GameLogic.PlayCard` 成功分支末尾 `EmitGraphEvent(new GraphEventContext{action="OnAfterPlay",...})`
   （另需 `GameLogic.EventZoneDefault` 补默认生效区域，旧图 zones 为空时才有合理默认）
验证：`tools/probe/AfterPlayProbe.cs` → ① 直接跑入口=伤害生效 ✓；② 真 `PlayCard` 不被打断、卡正常进战场 ✓。

**② 未保存改动被"洗白"（我引入的回归，已修）**：`RefreshEffectTabs()` 每次刷新都排基线 + `Update()` 取基线时无条件
`edit_dirty = false` → 加完节点后任何一次面板刷新都把改动"洗白" → 关页不弹确认 → **节点静默丢失**（用户实测）。
修：基线只在**进入会话时**取一次（`edit_entry_snapshot == null` 才排）；取基线时不再清脏标记。

**③ 夹具坑（会误判成产品 bug）**：`Slot` 坐标**从 1 起**（`x_min=1/y_min=1`），`new Slot(0,0,pid)` 的 `IsValid()=false`
→ `CanPlayCard` 直接 false → 以为"打出流程坏了"。`Slot` 构造：`Slot(int pid)` / `Slot(int x,int y,int pid)` / `Slot(SlotXY,int pid)`。

### G10. 新增卡牌类型 `CardType.Skill`（2026-10-01）
用户要"技能"作为卡牌类型（引擎原本只有 随从/法术/英雄/神器/奥秘/装备 ✓）。加新类型**必须同步的所有位置**：
1. `CardData.CardType` 枚举：**追加在末尾**（`Skill = 60`），绝不改旧编号（存档/网络兼容）
2. `CardData`：`GetTypeId()`（"skill"）、`IsRequireTargetSpell()`（技能带 PlayTarget 时也要选目标）
3. 类型**中文名表 ×4**：`GraphEditorPanel.TYPE_NAMES/TYPE_ENUMS`（=截图那个"选择：卡牌类型"弹框 ✓）、
   `GraphEditorBuilder.TYPE_NAMES`（生成的编辑器 UI）、`CardEditorPanel.ToChinese`、`NodeDocRunner.CardTypeName`
4. 类型**解析表 ×3**：`NodeDocRunner` 卡牌类型判断的 `switch(type_name)`、`CardQuery.ParseType/TypeName`、
   `Menu/CollectionPanel.GetTypeById`（收藏筛选开关按名字解析）
5. 生命周期：不加任何分支 → `PlayCard` 的 else 分支自然落**墓地**（=法术同款 ✓，实测 ✓）
- 已验证：type="Skill" 编译 ✓ / 打出进墓地不进战场 ✓ / 类型判断与搜索可识别 ✓。
- **未在探针里打通**："打出 → 效果图触发"——探针只调 `BuildCardData/BuildPool/ImportToGame` 仍不触发，
  因为**效果图运行期查找走 `CardPoolIO.GetCustomData(card_id)`**，探针没注册 CardCustomData（真实导入路径会注册 ✓，
  证据：实机日志里 小恶魔 的 ActivateEffect 触发过 ✓）。→ 新类型卡的"打出触发"要**实机**测一次。
- 未做（刻意）：收藏筛选的「技能」开关（`CardFilterBuilder` 生成 UI，要重跑生成工具，影响面大收益小）。

### G11. 英雄技能卡（用户定案：技能卡挂英雄 + 英雄卡上指定，2026-10-01）
语义：`type=Skill` 的卡**不进手牌、不能打出**；在**英雄卡**上引用（新字段 `skills`，技能卡 id 列表），
开战时自动把技能卡上的**起动式能力（ActivateAbility）**挂到英雄卡实例 → 变成英雄技能按钮（与火精灵「火焰」同链）。
实现点（改动了哪些文件，加/改字段时别漏）：
- `CardCustomData.skills`（DTO ✓）/ `CardData.skills`（运行期 ✓，`List<string>`，**不是数组** ✓）
- `CardPoolIO`：`BuildCardData` / `UpdateCardData` / `CardToData` **三处都要映射**（漏 UpdateCardData → 编辑器保存后不生效）
- `GameLogic.SetPlayerDeck`：英雄创建后 `MountHeroSkills(player)` —— 把技能卡能力灌进 `player.hero.abilities / abilities_ongoing`
  （**Card 实例字段** ✓；`CardData` 上没有 abilities_ongoing ✗）
- `Game.CanPlayCard`：`type==Skill → false`（不能从手牌打出）
- 编辑器：`GraphEditorPanel` 属性区新增「技能卡」行（`EnsureCardExtraFields` 复制种族行 ✓ +
  `OnClickSkillCardSelect/ApplySkillCardSelection`：候选=卡池里 type=Skill 的卡 ✓ + `UpdateCardData` 即时生效 ✓）
验证：`tools/probe/HeroSkillMountProbe.cs` → ① SetPlayerDeck 后英雄身上出现起动式技能 ✓
② 以英雄为宿主执行技能图=伤害生效 ✓ ③ CanPlayCard=False ✓。
夹具坑：`UserDeckData.cards` 是 **`UserCardData[]` 数组**（不是 List ✓）；`AbilityData` 上**没有**图字段
（图按能力 id 约定存在卡池自定义数据里 → 探针直接跑原图对象 ✓）。

### G12. 「事件广播了但图没跑/跑 N 遍」三连根因（2026-10-01，用户实测踩到，已修）
症状：规则图事件入口（如 OnAfterPlay）**事件广播了**（事件记录 ✓）但**图不执行**，或执行了**效果翻倍**。
三个叠加根因（都在编译/注册链上）：
1. **能力 id 带随机 Guid**（`graph_<卡id>_<action>_node<guid>`，5 处编译点同款）→ 每次保存/导入都生成**新 id**
   → 同一条图的能力在注册表里**越积越多** → 广播时同一条图执行 N 遍（实测 攻击+3 变 +6）
   → 且**已发到手牌的卡实例**引用旧 id（旧图/失效）——"改了图、对局里不生效"的元凶。
   修：**稳定 id** `StableAbilityId(card, kind, entryNodeId)`（同卡同入口永久不变；5 处全改：事件/逐动作旧式/光环×2/被动）。
2. **`RegisterAbility` id 冲突时跳过** ✗ → 稳定 id 后重编译会被旧版挡住（永远用旧图）。
   修：冲突时**替换**（dict 索引器 + 列表去重，与 `RegisterCard` 同款）。
3. **事件里加的增益不立刻生效**：`PlayCard` 的 OnAfterPlay 广播放在**最后一次 UpdateOngoing 之后** ✗
   → 事件里 `AddStatus(AddAttack,…)` 要等下一次收敛才反映到 GetAttack（"打出去数值不变"）。
   修：广播后补一次 `UpdateOngoing()`。
验证：`tools/probe/AfterPlayProbe.cs` ③（真实文件导入 → PlayCard → 广播唤起图 → 206001 添加增益）
→ 攻击 1→4、AddAttack=3（修复前：图跑两遍 AddAttack=6 / 攻击=1）。
**夹具坑（会伪造/掩盖产品 bug）**：
- 探针里 `BuildCardData` + `ImportToGame` 会**双重编译**；`CardData→DTO 往返会丢图`（`CardToData` 不带 effects/graph）
  → 必须走**真实文件导入**：写临时池 json（`JsonUtility.ToJson(CardPoolData)`）→ `CardPoolIO.ImportFromFile(path, true)` ✓。
- `Card.Create→SetCard` **会**从定义拷能力列表 → 探针再手动灌一遍 = 同 id 两条 = 图执行两遍（伪翻倍 ✗）。
- `AbilityData` 上**没有** graph 字段；`CardData` 没有 `GetAbilities()`（只有 `abilities` 数组）。

### G13. 假 null 崩溃 +「技能/法术」类型区分 + 守卫链定位法（2026-10-01，用户实测踩到）

#### G13.1 点卡池「编辑」必崩：`same key has already been added`（已修）
`RegisterAbility` 用 `AbilityData.Get(id) != null` 判"是否已注册" ✗ —— `AbilityData` 是 **ScriptableObject**，
重开卡池/刷新列表时旧实例被 `Destroy`，字典里留下已销毁引用；Unity 的 `==` 重载把**已销毁对象判成 null**
→ `Get` 取到了引用却"看似 null" → 误走 `dict.Add` → 同 key 崩溃。
修：改用**纯字典 `AbilityData.ability_dict.ContainsKey(id)`** 判断（不受假 null 影响），有 key 一律走"替换"；
列表去重时先判 `a == null` 再取 `a.id`（防 MissingReferenceException）。
`CardData` 注册路径本来就靠 `ContainsKey` 兜底 ✓ 不受影响。**教训：SO 注册表判存在一律用 ContainsKey，别用 Get(...) != null。**

#### G13.2 「技能」和「法术」是两种独立卡牌类型（"效果没生效"的一大类原因）
`CardType.Skill = 60`（英雄技能/主动技卡）与 `CardType.Spell = 20`（法术）**互不相等**；
编辑器 102032 类型下拉（`TYPE_NAMES = {随从,法术,技能,英雄,神器,奥秘,装备}`）存的是中文名，引擎映射一致 ✓。
实测案例：用户卡「使用卡牌后 → 类型判断(技能) 且 拥有者==拥有者 → 加增益」"永远不生效" ——
逐段验证（`tools/probe/UserCardProbe.cs`）：102032 ✓、112002 玩家判等 ✓（按 player_id ✓）、
真实打**法术** → 守卫判假 ✓（法术≠技能）、**全部已加载池里技能类型卡 = 0 张** → 条件永假。
→ 结论：**引擎没问题，是守卫类型选错**。改法：类型判断字段「技能」→「法术」（或池里真有技能卡）。
**先查"池里到底有没有满足条件的样本"，再怀疑引擎。**

#### G13.3 守卫链逐节点定位法（探针观测的两个坑）
- `NodeDocRunner.Run` 的**返回值是执行计数**（0/1…），**不是**值节点的输出 → 别用返回值当节点结果；
  值节点用「**分支 + 伤害观测法**」：`值节点 → 212001 分支(真→202041 damage=3 / 假→damage=7) → 量 p1 掉血`。
- 观测伤害**别用 202001**：它的目标吃 `target_card` 上下文（= 喂进来的样本卡）→ 伤害落在样本卡上，
  量 p1.hp 恒 0 ✗。用 **`202041`（damage 走字段）+ `targets ← 102017 获取敌方角色`** 显式指目标 ✓。
- 定位顺序：⓪ 注册表（卡编译了没/增益注册了没）→ ① 守卫链逐节点 → ② 真实打出端到端 → ③ 直接跑编译图。
- 探针产物记得清理：探针写进 Workshop 的**临时池 json**（如 probe_onafterplay_pool.json）忘删
  会出现在用户的卡池面板里（实测被骂）；跑完立即删。

### G3. 当前语义断言清单（47 条，全 PASS）
`102001` 常量伤害对照 / `102006` 攻击 / `102005` 费用 / **`102007`/`102008` 英雄卡读生命 == 玩家 hp_max/hp** /
`112004` 整数运算(-, /, *) / `102013 → 202012.targets` / **`202012` 分配伤害总量 == damage（含大量死亡时剩余点数转打脸）**。
其中英雄卡生命与分配伤害两条，在对应修复之前**必然 FAIL** → 这两条是有回归价值的硬断言。
- 下一步：按节点族继续补（条件/分支族、移动族、光环族、费用族），每补一族就跑一遍。
