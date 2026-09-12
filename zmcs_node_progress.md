# NodeDoc（醉梦传说 zmcs）节点编辑器接入 — 进度与任务清单

> 更新时间：2026-09-11　|　项目：TCG2（Unity, `e:\Program Files\Unity\unity projects\TCG2`）
> 本文件是跨对话交接清单。新会话请先读本文件与 `Assets/TcgEngine/Resources/NodeDoc.xml`。

---

## 一、总体目标

把 `NodeDoc.xml`（319 个节点定义，15 个分类）逐步接入 TCG2 的节点编辑器：
`GraphEditorPanel.SupportedNodeIds`（白名单）+ `NodeDocRunner`（执行/求值）双登记。
原则：**行为与文档一致**；文档含糊处向用户确认后再编码；过时/旧编号节点不实现。

## 二、当前进度快照

| 指标 | 数值 |
|---|---|
| 节点总数 | 319 |
| 已接入（SUP） | **252** |
| 可见节点 | **295**（隐藏 24：`-N` 旧编号 / 同名重复。**注**：过时节点实际未被隐藏，见第五节） |
| 可见待办 | **47**（其中 32 个按约定不做：摘要「已过时」15 + 名称「（过时）」9 + 无枚举对应 3 + 蓄力待定 5；**真正待接入 15**） |
| 编译诊断 | 0 报错（read_lints） |

**复核命令**（统计白名单数量）：
```powershell
cd "e:/Program Files/Unity/unity projects/TCG2"
$txt=[IO.File]::ReadAllText((Resolve-Path 'Assets/TcgEngine/Scripts/Workshop/UI/GraphEditorPanel.cs'),[Text.Encoding]::UTF8)
$m=[regex]::Match($txt,'SupportedNodeIds = new HashSet<string>\s*\{(.*?)\};',[Text.RegularExpressions.RegexOptions]::Singleline)
([regex]::Matches($m.Groups[1].Value,'"(-?\d+)"')).Count
```

**复核命令**（精确统计可见/待办，按 ShouldHideNodeDoc 规则镜像）：
```powershell
cd "e:/Program Files/Unity/unity projects/TCG2"
$x=New-Object System.Xml.XmlDocument
$x.LoadXml([IO.File]::ReadAllText((Resolve-Path 'Assets/TcgEngine/Resources/NodeDoc.xml'),[Text.Encoding]::UTF8))
$supTxt=[IO.File]::ReadAllText((Resolve-Path 'Assets/TcgEngine/Scripts/Workshop/UI/GraphEditorPanel.cs'),[Text.Encoding]::UTF8)
$mm=[regex]::Match($supTxt,'SupportedNodeIds = new HashSet<string>\s*\{(.*?)\};',[Text.RegularExpressions.RegexOptions]::Singleline)
$sup=@{}; foreach($q in [regex]::Matches($mm.Groups[1].Value,'"(-?\d+)"')){$sup[$q.Groups[1].Value]=$true}
$all=@(); foreach($n in $x.SelectNodes('//ActionComment')){$all+=@{id=([string]$n.defineId).Trim();name=[string]$n.editorName}}
$keep=@{}; foreach($d in $all){$c=$keep[$d.name]; $s=$sup.ContainsKey($d.id); if($c -eq $null -or ($s -and -not $sup.ContainsKey($c))){$keep[$d.name]=$d.id}}
$vis=0;$todo=0; foreach($d in $all){$hidden=($d.id.StartsWith('-')) -or ($keep[$d.name] -ne $d.id); if(-not $hidden){$vis++; if(-not $sup.ContainsKey($d.id)){$todo++}}}
"VISIBLE=$vis TODO=$todo"
```

## 三、已完成批次（累计，白名单 252）

| 批次 | 节点 |
|---|---|
| 1 基础运算层 | 112002 比较、112004 整数运算、112005 逻辑运算(多输入补全)、112006 字符串常量 |
| 2 只读查询 | 102020 相邻卡牌、101013 暂存区、101011 玩家道具、103007 目标卡牌定义列表、111035 合并多个集合 |
| 法术伤害家族 | 101016 获取法术伤害、102011 获取卡牌法术伤害、103013 获取卡牌定义法术伤害 |
| 3 数据缺口近似 | 101009/210006/202045 延迟区、201007 摧毁道具、101010/201006/103015 技能、101021/201013/201014/201015 疲劳、102033/103004/102034/103019 标签、106031/106030/202049/202048 可见性(只存不发) |
| 4 效果家族 | 107001 该效果、107003 效果属性、107004/107005/107006 获取效果、107007 合法使用目标、107009 所属定义(多输出)、207001 发动效果 |
| 6 映射 | 113001~113005、213001、213002 |
| 汉化 | 112001 布尔常量、112002 比较、112003 整数常量、112004 整数运算、112005 逻辑运算、112006 字符串常量（信达雅，NodeDocDb.CnNames） |
| 7 = T1 Pile 值通道 | 102029 获取卡牌所在牌堆、105002 获取牌堆中的牌、105003 获取牌堆名、105004 获取牌堆所属玩家、200004 创建牌堆、200005 删除牌堆、200006 从牌堆将卡牌移动到目标牌堆、202046 创建衍生卡并置入牌堆 |
| T2 事件家族 | 108004 事件类型判断、108005 本局事件、108006 本回合事件、108007 前X-Y回合事件、108008/108009 事件前/后快照、108010 重复次数、108011 发生回合、108012 父事件、108013 子事件、108014 事件链、108015/108016 引发的事件、208009 设置重复次数 |
| T2 卡牌快照家族 | 104001/104002/104004~104019（18 个；104003 与 104017 同名，按同名去重保留 104017 故跳过；104012/104013 蓄力按用户决定撤下） |
| T2 增益家族补全 | 106001 该增益、106002 获取增益定义、106003 是否具有增益、106005 单个增益定义、106006 卡牌所有增益、106007 增益属性可见、206004 设置增益属性可见性 |
| T3-a 玩家 | 101020 获取所有玩家、101022 获取某个回合的玩家（近似=当前回合玩家）、201002 设置玩家属性 |
| T3-b 选择类（复用入口选目标通道） | 201004/201016 从卡牌中选择一张、201005/201017 从卡牌定义中选择一张 |
| T3-c 卡牌动作/多目标伤害/杂项 | 202011 法术伤害、202012 随机分配法伤、202017 强制更新状态、202021/202022/202023 触发宣言/遗言/效果、202024 重置卡牌、202025 强制攻击、202026 攻击次数+1、202027 揭示卡牌、210008 移动到暂存区、202035/202042 伤害分配、202036/202043 随机目标伤害 |
| T3-d 部分查询 | 102009 获取唯一属性名、102035 获取卡牌属性名、103003 获取卡牌定义列表 |

## 四、修改/新增文件清单

| 文件 | 说明 |
|---|---|
| `Scripts/Workshop/Graph/NodeDocDb.cs` | 英文节点汉化映射 `CnNames` |
| `Scripts/Workshop/Graph/NodeDocRunner.cs` | 全部执行/求值分支与辅助方法（含 T1 Pile 通道、T2 事件/快照/增益通道） |
| `Scripts/Workshop/UI/GraphEditorPanel.cs` | 白名单、端口字段默认值（比较 A/B、整数运算 arg、法术伤害 trait_id、标签 tag、映射 key/value、Pile 区域、EventReference 事件类型） |
| `Scripts/Workshop/Graph/GraphEventContext.cs` | **T2 新增字段**：`turn`/`repeat`/`parent`/`children`/`card_before`/`card_after` |
| `Scripts/GameLogic/GameLogic.cs` | **T2**：`EmitGraphEvent` 落事件日志（父/子链/回合/前后快照）+ `GetEventLog/GetTurnEvents/GetRangeEvents` |
| `Scripts/Effects/EffectRunGraph.cs` | 5 处 `Run(...)` 透传 `ability`（效果上下文） |
| `Resources/Traits/spell_damage.asset` + `.meta` | **新增**：法术伤害 TraitData（id `spell_damage`，名称"法术伤害"，guid `b7e4c1a2d9f3405681ca2be5f0d39c74`） |
| `Resources/Effects/{add_spell_damage,damage,damage_player,damage_equal_to_atk,damage_equal_to_mana_max}.asset` | 修复 `bonus_damage` 悬空引用（旧 guid `50116f13...` → 新 guid） |

## 五、关键设计决策（用户已确认，勿反复询问）

| zmcs 概念 | TCG2 载体 | 备注 |
|---|---|---|
| 效果 Effect | `AbilityData`（内含 `EffectData[]`） | `NodeDocRunner.Run(..., ability:)` 注入 `ctx_ability` |
| 法术伤害加成 | `TraitData`（id `spell_damage`） | 与 `EffectDamage.bonus_damage` 同源；节点用 `trait_id` 字段读取 |
| 延迟区 | `Player.cards_secret`（奥秘区） | |
| 道具 | `Player.cards_equip` / 英雄 `equipped_uid` | |
| 英雄技能 | 英雄卡本身 + `exhausted` | 复原技能=清除已行动 |
| 疲劳层数 | `Player` 特性 `FATIGUE_TRAIT="疲劳层数"` | |
| 卡牌标签 | 关键词/特性 id（`keywords`/`traits`） | |
| 可见性 | `Card` 特性 `vis:{pid}[:prop]` | **只存不发** |
| 映射 GraphMap | `Dictionary<string,object>`，按 113001 节点 id 缓存，Run 结束清空 | |
| 多输出口 | `SourcePinName(graph, link)` 按起点引脚名分发 | 107009 cardDefine/buffDefine/isCardDefine 已用 |
| 过时/旧编号 | 库内隐藏逻辑见下「重要事实」 | `-N` 旧编号被隐藏；过时节点**实际未隐藏** |
| 事件日志 | `GraphEventContext` 链 + `GameLogic.event_log`（上限 512） | `turn/repeat/parent/children` 由 `EmitGraphEvent` 落账 |
| 卡牌快照 | `Card.CloneNew` 深克隆的 `Card` 对象（`card_before`/`card_after`） | 快照值即一个 Card，104 家族直接按 Card 口读 |
| 增益实例 | `CardBuff`（挂 `Card.buffs`）+ `BuffRef{卡,实例,id}` 包装 | TCG2 `CardBuff` 不含所属卡，故包装 |
| 增益可见性 | `Card` 特性 `vis:buff:{增益id}:{属性}:{玩家id}` | **只存不发** |
| 蓄力层数 | 未定义 | 用户决定**蓄力家族暂缓**：104012/104013 已撤下，102025/102026/202033 暂不做 |

**⚠ 重要事实（过时判定口径，用户已确认"维持现状不改隐藏逻辑"）**：`NodeDoc.xml` 中 **60 个节点**带 `<obsoleteMsg />`，但**全部为空占位**（含已支持的 112001/112003/112004/112006/202001 等），故 `NodeDocDb.Def.obsolete` **恒为 false**、`ShouldHideNodeDoc` 的过时分支**从未生效**——所谓"过时节点库内隐藏"并不成立，这些节点目前在库中以"未接入"灰显。真正的过时信号只有文字：`editorName` 含「（过时）」（11 个）、`summary` 含「已过时」（24 个）。用户决定**不改隐藏逻辑**，后续是否实现逐个判断；判定过时以下面两类清单为准。

**已明确跳过（不要再做，无对应枚举）**：`107002 效果具有标签`、`107008 效果类型判断`、`107010 事件效果类型判断`（EffectTag/EffectType/EventTriggerTime 无对应枚举）。

**摘要标注「已过时」→ 按用户决定一律不实现（15 个）**：`109001~109007/109009 事件记录家族`（已被 108 事件家族取代）、`208003~208008`（指向"转换事件类型+设置变量"）、`202007 设置属性`（已被 202037 取代；已支持的 `102004 获取属性` 为历史遗留例外）。

**名称含「（过时）」的 9 个未接入节点（暂不做）**：212003、201011、101001、202008、202009、202010、103009、111003、111006（多为旧版别名，其中 `105001` 已作为兼容别名接入）。

**同名重复且已被隐藏（无需实现）**：102002、103005、202002、202034、202039、202047（各有支持版本，`keep_name` 只留支持项）；`104003 卡牌快照类型判断`（与 104017 同名，保留支持版 104017）。

**蓄力家族（用户决定暂缓）**：102025/102026/202033/104012/104013 —— 载体定义清楚后再一起做。

## 六、待办任务清单（按已确认优先级）

### T1. Pile 值通道（✅ 已完成，白名单 182→190）
- [x] `ResolveValuePile`/`ResolveInputPile`/`PileEncode`/`PileDecode`/`PileList`/`PileMoveCard`：编码/解码 + 区域合法性
- [x] `102029 获取卡牌所在牌堆`（复用 `GetCardPileName`）
- [x] `105002 获取牌堆中的牌`、`105003 获取牌堆名`、`105004 获取牌堆所属玩家`
- [x] `200006 从牌堆将卡牌移动到目标牌堆`（仅数据型区域；战场/装备特殊区拒绝）、`200005 删除牌堆`（近似=清空区域）
- [x] `202046 创建衍生卡并置入牌堆`（战场走自动空位；无牌堆→暂存区 `cards_temp`）
- [x] `200004 创建牌堆`（TCG2 无自定义牌堆 → **近似=返回该玩家固定区域句柄**，等效 101017，待用户复核）
- [x] `NodePresetFromDoc` 补 `Pile` 型输入口下拉字段（手牌/牌库/墓地/战场/装备/奥秘/暂存区）
- 说明：Pile 值为 `"玩家id|区域名"` 字符串；`200006` 的 `fromPile` 仅作语义标注，实际按卡当前所在区摘出

### T2. 事件日志 + 卡牌快照基础设施（✅ 已完成，白名单 190→228）
- [x] `GraphEventContext` 增补字段：`turn`/`repeat`/`parent`/`children`/`card_before`/`card_after`
- [x] `GameLogic.EmitGraphEvent`：广播时落事件日志（父/子链、回合、前后快照，上限 512）+ `GetEventLog/GetTurnEvents/GetRangeEvents`
- [x] 卡牌快照：`Card.CloneNew` 深克隆封装为 CardSnapshot 值通道（`SnapshotInput`）；事件广播自动抓主体卡前后快照
- [x] `NodeDocRunner` 增 EventArg 值解析（`ResolveValueEvent/ResolveInputEvent/ResolveEventList/ResolveInputEvents`）
- [x] 事件家族：108004~108016、208009（白名单 +14）
- [x] 卡牌快照家族：104001/104002/104004~104019（白名单 +18；104003 与 104017 同名去重跳过；104012/104013 蓄力撤下）
- [x] 增益家族补全：106001/106002/106003/106005/106006/106007/206004（白名单 +7）
- [x] 增益值通道 `BuffRef{card,buff,buff_id}` + `ResolveValueBuff/ResolveBuffList/ResolveInputBuff/ResolveInputBuffs/ResolveValueBuffDefine`
- [x] `NodePresetFromDoc` 补 `EventReference` 下拉字段；106005/106007/206004 保留 Buff 引用口
- **修正记录**：`109002~109009` 事件记录家族的 `summary` 注明"该节点已过时，请使用获取事件变量"，按"摘要已过时一律不实现"约定**跳过**（注：其 `obsoleteMsg` 只是空占位，不能作为判定依据）
- **说明**：`EventTypeMatches` 用中文事件类型名（打出/伤害/治疗/死亡/弃牌/装备/抽卡/回合/游戏）匹配 `ctx.action` 子串

### T3. 第 7 批 行动/编辑/查询补全（🚧 进行中，白名单 228→252）
- [x] 玩家：`101020 获取所有玩家`（Player[]；单消费者取首个）、`101022 获取某个回合的玩家`（近似=当前回合玩家）、`201002 设置玩家属性`
- [x] 选择类（复用入口「目标1条件」选目标通道）：`201004/201016 从卡牌中选择一张`、`201005/201017 从卡牌定义中选择一张`
  - 语义：若入口已选目标 `target_card` 落在候选集合内 → 即所选；否则回退取首张（非交互上下文/AI）
- [x] 卡牌动作：`202011/202012 法术伤害`、`202017 强制更新游戏状态`、`202021/202022/202023 触发宣言/遗言/效果`、`202024 重置卡牌`、`202025 强制攻击目标`、`202026 攻击次数+1`、`202027 揭示卡牌`
- [x] 多目标伤害：`202035/202042 伤害并分配`（近似=均分，余数给前排）、`202036/202043 随机目标伤害`
- [x] 集合/杂项：`210008 卡牌移动到暂存区`
- [x] 部分查询：`103003 获取卡牌定义列表`（近似=全部定义）、`102009 获取唯一属性名`、`102035 获取卡牌属性名`
- [x] 跳过：`202007 设置属性`（摘要「已过时」，被 202037 取代）
- [ ] **剩余待接入 15 个**：
  - 定义动作：`202030/202031/202032 触发定义宣言/遗言/效果`、`203001 展示卡牌定义`、`203002 复制卡牌定义`、`203003 设置卡牌定义属性`、`203004 拼接卡牌定义`
  - 集合/杂项：`111036 转换集合类型`、`211001 遍历`、`201012 信仰对决`
  - 查询：`103016 定义对应角色`、`103022 定义合法使用目标`、`103025 定义所属卡包`、`102023 获取合法卡牌使用目标`、`102024 获取合法攻击目标`
  - 说明：定义动作涉及运行时改写定义（`CardData` 为共享资产，需先定策略）；`211001 遍历`/`201012 信仰对决` 属复杂控制流/专属机制，建议逐个单独确认

### T4. 收尾
- 验证清单：`zmcs_node_verification.md`（V01~V68 逐用例，含测试目标/步骤/预期/边界/异常与填写区；用户按此表逐条验证并回填）
- [ ] 每批完成后跑 `read_lints` + 白名单统计命令
- [ ] 全部完成后在 Unity 里编译 + 冒烟（拖一张卡验证连线/保存/运行）

## 七、开发约定（新会话照做）

1. **双登记**：新节点 = `NodeDocRunner` 加执行/求值分支 + `GraphEditorPanel.SupportedNodeIds` 加 id；`Value` 类节点（defineId 以 10/11 开头）走求值通道，`Action` 类（20/21）走 `ExecuteAction`。
2. **求值通道分工**：整数→`ResolveNodeInt`；布尔→`EvaluateConditionNode`；卡牌集合→`ResolveCollectionNode`；单卡→`ResolveValueCard`；定义→`ResolveValueDefine/EvaluateDefineArray`；玩家→`ResolvePlayerOutput`；效果→`ResolveEffectList`；Pile→`ResolveValuePile`（`"玩家id|区域名"`）；事件→`ResolveValueEvent/ResolveEventList`；卡牌快照→`ResolveSnapshot`（深克隆 Card）；增益→`ResolveValueBuff/ResolveBuffList`（`BuffRef`）；Object/杂项→`GetObjectInput`。
3. **注释口径**：每个 case 注释写清"近似映射的取舍"，与本文第五节一致。
4. **不动**：`NodeDoc.xml`（数据源）、过时节点、负数 id 节点。
5. **验证**：改完跑 `read_lints`（三个核心文件）+ 白名单统计；不引入新依赖。
6. 端口字段：`Object`/`CardTagName` 等无默认字段的口，需要时在 `NodePresetFromDoc` 补 `FieldDef`（参考 112002 的 A/B、102033 的 tag）。
