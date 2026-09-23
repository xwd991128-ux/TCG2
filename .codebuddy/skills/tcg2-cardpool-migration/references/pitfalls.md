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
