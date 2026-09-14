# NodeDoc（醉梦传说 zmcs）节点编辑器接入 — 进度与任务清单

> 更新时间：2026-09-13　|　项目：TCG2（Unity, `e:\Program Files\Unity\unity projects\TCG2`）
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
| 已接入（SUP） | **265** |
| 可见节点 | **297**（隐藏 `-N` 旧编号 / 同名重复。**注**：过时节点实际未被隐藏，见第五节） |
| 可见待办 | **36**（其中 32 个按约定不做：摘要「已过时」15 + 名称「（过时）」9 + 无枚举对应 3 + 蓄力待定 5；**真正待接入 4**：201012 + 203002/203003/203004） |
| 编译诊断 | 0 报错（read_lints） |

**本轮新增（2026-09-13）**
1. **全量引脚名对齐审计 + 修复**（见 §四之二）——历史"连线看似接上、运行时无效"类 bug 的系统性清理。
2. **T3 剩余 15 个节点中的 11 个已接入**（查询类 5 + 集合/杂项 2 + 定义动作 4），白名单 252→265。
3. **AI 推演线程安全修复**：表现层（VFX/BGM）在主线程判定，杜绝后台线程碰 Unity API 导致 AI 计算被终止。
4. **编辑器字段可用性修复**：`209101 设置战斗BGM` 的 `bgm` 口原为自由文本框（无法选曲）→ 改为 `FieldEditType.BgmSelect`「音乐库下拉」（显示「标题（官方/导入）」、存 `BgmEntry.id`，首项=「留空：停止当前战斗BGM」；弹层底部仍可手填标识/显示名）。渲染/宽度/回显与既有 关键词/增益/卡牌 下拉同一套（`GraphEditorPanel` 的 `InlineFieldText` + `CreateInlineFieldRow` + `OpenFieldSelectPopup`）。

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
| T3-e 第 7 批收尾（+11） | `102023 获取合法卡牌使用目标`、`102024 获取合法攻击目标`、`103016 定义对应角色`(近似)、`103022 定义合法使用目标`、`103025 定义所属卡包`(近似)、`111036 转换集合类型`、`211001 遍历`、`202030/202031/202032 触发定义宣言/遗言/效果`、`203001 展示卡牌定义`(v1 仅日志) |
| 引脚对齐修复 | `103017/103018`(card→cardDefine)、`111007`(集合口 array)、`207001/107003/107007`(effect 口接线失效)、`106004/206001/206002/206003`(Buff/BuffDefine 口守卫) |

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
| `Scripts/Tools/MainThreadUtil.cs` | **新增**：主线程判定（`RuntimeInitializeOnLoadMethod` 捕获主线程 id）。AI 推演跑在后台线程（`AILogic.Execute → ThreadStart`）会执行同一张图，表现层必须先判主线程再碰 Unity API |
| `Scripts/VFX/VFXRuntime.cs` | `Trigger`/`IsClientView` 增主线程守卫（非主线程直接返回）——修复 AI 推演线程上 `transform` 跨线程访问导致的 `UnityException` 与「[AI] 推演线程异常，已终止本次计算」 |
| `Scripts/VFX/VFXConfig.cs` | **新增本地序列图字段**（`sheet_path`/`sheet_cols`/`sheet_rows`/`sheet_row`/`sheet_start`/`sheet_count`/`sheet_scale`）+ `HasSheet`/`EffectiveSheetCount`（纯计算，AI 线程可安全调用），`HasFrames`/`Duration`/`Summary` 兼容两种素材模式 |
| `Scripts/VFX/VFXFrameLibrary.cs` | **新增整图切片通道**：`ResolveSheet/ProbeSheet/ResolveSheetFile` + 贴图/切片双层缓存（按路径+修改时间自动重载）；`ResolveFrames` 序列图优先 |
| `Scripts/UI/VFX/VFXEditorPopup.cs` | 素材弹层新增「本地序列图」区（选文件/RM 5×5 预设/切片参数/帧预览网格），顶栏新增「序列图…」入口；未选帧提示文案更新 |
| `Scripts/Audio/BgmManager.cs` / `BgmBattleRuntime.cs` | 同上守卫 + 表现层 `try/catch` 隔离（特效/音频异常不外溢到对局逻辑与 AI 推演） |
| `Scripts/Workshop/Graph/NodeDocRunner.cs` | 引脚对齐修复 + T3-e 11 个节点 + 新增辅助：`ValidPlayTargets`/`ValidAttackTargets`（合法目标）、`ExecuteForEachNode`+`ProbeElements`（211001 遍历）、`ConvertToCards/Defines/Player/Buffs/Events`（111036 转换）、`ResolveInputEffect`/`ResolveInputBuffDefine`/`HasInputPin` |
| `Scripts/Workshop/UI/GraphEditorPanel.cs` | 白名单 +11；`111036` 补「转换类型」下拉字段；`CreateRecentChip` 的 Image+TMP 同挂 NRE 修复 |
| `Scripts/GameLogic/GameLogic.cs` | 组卡静默跳过留痕（无效卡/空卡组警告）+ `ResolveCardAbilityPlayTarget` 诊断日志 |
| `Scripts/Workshop/CardPoolIO.cs` | 入口目标槽兜底（编号槽为空时回退旧 `target_type`）+ 无目标槽编译警告 + 入口编译诊断日志 |

## 四之二、引脚名对齐审计（2026-09-13，全量）

判定口径：已接入节点的**每个输入引脚名**，与运行时代码实际读取的引脚名（`ResolveInputCard/Cards/Define/Player/Event/Buff`、`GetIntInput`、`GetObjectInput`、`ResolveValue*` 等）逐字比对。原因是历史 bug（202013 治疗）就属于"口名对不上 → 取值线静默失效"。

**已修复的真实错位**

| 节点 | 现象 | 根因 | 修复 |
|---|---|---|---|
| `103017/103018` 卡牌定义具有宣言/遗言 | 条件恒假 | 代码读 `card`，XML 口是 `cardDefine`（同族 103014/103021 用 `cardDefine`） | 改读 `cardDefine`（元素绑定路径保留） |
| `111007` 获取第X个元素 | 集合口连线无效 | `ResolveValueCards` 未传口名 → 默认 `collection`，XML 口是 `array` | 传 `array`，为空回退默认口 |
| `207001 发动效果` | effect 口连线无效、发动恒失败 | 代码对节点自身调 `ResolveValueEffect`，而效果通道没有 207001 分支 | 新增 `ResolveInputEffect`（effect 口来源按效果通道求值；未连线回退"当前效果"） |
| `107003 获取效果属性` | 效果口恒空 | 同上 | 同上 |
| `107007 获取效果合法使用目标` | 整体返回空 | 同上 | 同上 |
| `106004/206003/206002/206001` 增益家族 | （预防性）Buff/BuffDefine 口 | 编辑器**有意剥离**这些口改用 `buff_id` 下拉字段（`GraphEditorPanel.cs:904-915`） | 新增 `HasInputPin` 守卫：口存在才读，否则走原字段路径（保持既有语义不变） |

**审计结论：其余已接入节点的输入引脚名与 XML 逐字一致**（含动作家族 202001~202047、求值家族 101xxx/102xxx/103xxx/104xxx/105xxx/106xxx/107xxx/108xxx/111xxx/112xxx/113xxx）。
**兼容回退（非 bug）**：202013/202039/202047 治疗（`targets`→回退 `card`）、202016 消灭（`cards`→回退 `card`）。

## 五、关键设计决策（用户已确认，勿反复询问）

| zmcs 概念 | TCG2 载体 | 备注 |
|---|---|---|
| 效果 Effect | `AbilityData`（内含 `EffectData[]`） | `NodeDocRunner.Run(..., ability:)` 注入 `ctx_ability` |
| 法术伤害加成 | `TraitData`（id `spell_damage`） | 与 `EffectDamage.bonus_damage` 同源；节点用 `trait_id` 字段读取 |
| 延迟区 | `Player.cards_secret`（奥秘区） | |
| 道具 | `Player.cards_equip` / 英雄 `equipped_uid` | |
| 英雄技能 | 英雄卡本身 + `exhausted` + `Game.ability_played` | 复原技能（201006）=清除英雄已行动 **且清空该玩家卡上「本回合已发动」记录**：`once_per_turn` 靠 `ability_played`+`ConditionOnce` 判定，只清 `exhausted` 会出现"看着已刷新、点了没反应" |
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
- [x] `NodePresetFromDoc` 补 `Pile` 型输入口下拉字段（战场/手牌/牌库/墓地/装备区/奥秘区/英雄/暂存区）
- 说明：Pile 值为 `"玩家id|区域名"` 字符串；`200006` 的 `fromPile` 仅作语义标注，实际按卡当前所在区摘出

### T1b. 区域（牌堆）7 区统一口径（✅ 已完成）
- **7 个玩家区域**（与事件入口「生效区域」多选同一套名字）：战场 / 手牌 / 牌库 / 墓地 / 装备区 / 奥秘区 / 英雄；另有内部 `暂存区`（无 UI，衍生卡未归区时的落地处）。
- `PileNormalize` 同时认显示名与内部名（装备区→装备、奥秘区→奥秘、英雄/英雄区→英雄、暂存区/临时区），旧图不必重拖。
- 新增 `ZoneCards(player, zone)`：**只读**取区域卡牌，支持「英雄」（= `player.hero` 单卡，不是列表）；`PileList` 仍是可写列表引用（英雄返回 null → 移动/清空类节点自然拒绝）。
- `GetCardPileName` 补 英雄 / 暂存区 → `102029 获取卡牌所在牌堆`、`105001/105005 卡牌所在牌堆判断` 覆盖全部区域。
- `101017 获取牌堆` 不再"未知区域一律当牌库"：未知 → 空集合 + 警告；下拉同时放开到 8 项（7 区 + 暂存区）。
- `PileMoveCard` 对 战场/装备区/英雄 明确拒绝并给可操作警告（请用召唤/装备类节点）。
- 编辑器侧统一 `ZONE_NAMES` / `ZONE_NAMES_WITH_TEMP` 两个数组，所有区域下拉共用（避免"装备区 vs 装备"叫法不一致导致勾了不生效）。

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
- [x] **第 7 批已接入 11 个（2026-09-13）**：
  - 查询：`102023 获取合法卡牌使用目标`、`102024 获取合法攻击目标`、`103022 定义合法使用目标`（三者共用 `ValidPlayTargets/ValidAttackTargets` + `AbilityData.CanTarget`/`Game.CanAttackTarget`）
  - 查询：`103025 定义所属卡包`（近似=CardData.packs 首个卡包）、`103016 定义对应角色`（近似：定义本身为英雄/随从时返回自身，否则空+警告）
  - 集合：`111036 转换集合类型`（卡牌/定义/玩家/增益/事件；不可转换元素抛弃）、`211001 遍历`（`action` 口循环体 + `element` 口当前元素；元素上限 1000）
  - 定义动作：`202030/202031/202032 触发定义宣言/遗言/效果`（以 `cards` 为载体触发 `cardDefine` 的 OnPlay/OnDeath 能力，`targets` 作触发者）
  - `203001 展示卡牌定义`（v1=日志记录，表现层未接入界面）
- [ ] **剩余真正待接入 4 个（均需先定策略，未编码）**：
  - `203002 复制卡牌定义` / `203003 设置卡牌定义属性` / `203004 拼接卡牌定义`：需引入"运行时可编辑定义副本"机制（`CardData` 是共享 ScriptableObject 资产，运行时改写会影响卡池/存档/UI，且 `CardData.Get` 需识别副本）——**待用户确认策略后实现**
  - `201012 信仰对决`：TCG2 全库无「信仰/Faith/对决」任何数据结构与逻辑（已全库确认），属新增机制（双方从牌库按 filter 揭示、比灵力消耗、产出事件与胜负）——**待用户确认是否新增机制**

### T4. 收尾
- 验证清单：`zmcs_node_verification.md`（V01~V68 逐用例，含测试目标/步骤/预期/边界/异常与填写区；用户按此表逐条验证并回填）
- [ ] 每批完成后跑 `read_lints` + 白名单统计命令
- [ ] 全部完成后在 Unity 里编译 + 冒烟（拖一张卡验证连线/保存/运行）

## 六之二、表现层接入点（动作/事件/增益特效挂点 + 音效/弹道；代码已就绪，素材由用户配置）

现有挂点（本轮已加线程安全守卫，可直接用）：

| 场景 | 挂点 | 说明 |
|---|---|---|
| 动作节点特效（如 202035 造成伤害 → 弹道） | `GraphNode.vfx`（`VFXConfig`），`NodeDocRunner.TriggerNodeVfx(node, caster, target_card, target_player, false)` 在 `ExecuteAction` 之后触发 | `trigger=OnAction` 立即播、`AfterActionDelay` 用 delay、`OnEvent` 动作时不播；`bind=TargetCard` 即伤害目标（含英雄卡→自动落英雄锚点），`bind=Caster` 是施法卡 |
| 事件节点特效（如 OnBeforeDamage / OnAfterDamage / 增益添加） | 同类 `GraphNode.vfx`，在 `NodeDocRunner.Run` 的事件分发处触发（`is_event_node=true`） | 需 `trigger=OnEvent`；伤害/治疗/抽卡等事件经 `GameLogic.EmitGraphEvent` 广播，事件主体卡在 `target_card` |
| 持续增益（buff）特效 | 增益图自身的「增益触发入口」节点上配 `vfx`；触发链：`BuffRuntime.RunGraph(..., "OnBuffAdding"/"OnBuffAdded"/"OnBuffRemoving"/"OnBuffRemoved", ...)` → `NodeDocRunner.Run` → 事件节点 VFX | 增益定义（`BuffData`）**当前没有** vfx 字段——若想"每个增益配一个特效资产"，需新增 `BuffData.vfx` 并在 `BuffRuntime.AddBuff/RemoveBuff` 处触发 |
| 音效（**已实现**，2026-09-13） | `VFXConfig.audio_path/audio_volume/audio_loop`（+`HasAudio`）；`VFXRuntime.SpawnRoutine` 在动画开始时调用 `VFXAudio.Play(...)`；编辑器入口见 `VFXEditorPopup`「音效行」 | 素材与解码复用 `AudioPicker`（绝对路径→`LoadPath`、否则 Workshop→`LoadWorkshop`、再退 Resources）；内部 4 路音效源池，**不随特效销毁**（避免动画结束掐断音效）；解码异步→首次可能晚几十毫秒，成功后进缓存；`VFXAudio.Play` 内含主线程判定（AI 推演阶段静默）；`VFXAudio.StopAll()` 已挂到 `VFXRuntime.ClearAll()` |
| 弹道飞行（**已实现**，2026-09-13） | `VFXConfig.travel/bind_to/travel_duration/travel_curve`（+`TravelPreset`）；`VFXRuntime.ResolveBindContext` 解析起点/终点；新文件 `Scripts/VFX/VFXTravel.cs`；编辑器入口见 `VFXEditorPopup`「弹道行」 | 开弹道后 `bind`=起点、`bind_to`=终点，`VFXTravel` 每帧按缓动曲线在两端**活 Transform** 的世界坐标间 Lerp（卡/地块移动轨迹跟随），`unscaledDeltaTime` 推进；`life=max(动画时长, 飞行时长)`；终点解析不到→原地播放+日志；`travel_duration=0` 表示"与动画等长" |

**本地序列图（帧动画）支持（2026-09-13 新增，可直接用于弹道/增益特效）**

| 位置 | 内容 |
|---|---|
| `VFXConfig` 新字段 | `sheet_path`（绝对路径 / `Assets/...` 相对 / 工程 `Sprites/{FX,UI,Icons,Cards}` 内文件名）、**`sheet_cell`（单帧边长，0=自动识别）**、`sheet_cols`/`sheet_rows`（由单帧尺寸×图片尺寸推算）、`sheet_row`（只播第 N 行，-1=全部）、`sheet_start`、`sheet_count`（0=全部）、`sheet_scale`（单帧像素偏大时用 PPU 反向补偿，如 0.5 = 缩到一半） |
| `VFXFrameLibrary` | **统一分割逻辑**：`SheetCellCandidates`（取 w、h 的公因数作候选，保证任何网格都整除整图）+ `AutoSheetCell`（优先 RPG Maker 标准 192，否则最接近 192 的公因数）+ `AutoGrid`；`GetSheetVariant` 对"不能整除整图"的配置**自校正**后再切片。RPG Maker 动画实测尺寸规律：960×960=5×5、960×768=5×4、960×576=5×3、960×384=5×2、960×192=5×1、960×1152=5×6、768×192=4×1、576×192=3×1（单帧恒 192×192）→ **一律按 5 行切是错的**（这是"只有 5×5 正常"的根因）。读盘复用 `ImagePicker.TryLoadTextureFromFile`（体积/边长护栏 + 中文报错）→ 行优先、**y 翻转**切片；贴图按"路径+修改时间"缓存（改图自动重载） |
| `VFXEditorPopup` | 顶栏：「选择序列图…」+「切片设置…」；**已按要求移除「选择内置图片/序列帧列表」入口与内置帧列表**（旧配置里已存的逐张帧列表仍可播放，并提供一个"清除旧序列帧列表"按钮）。切片设置面板：单帧尺寸 ±（只在整除候选间切换）、自动识别、播放行、范围复位、起始帧/帧数/缩放、**切片帧预览网格**（最多 25 格，核对"第几帧是哪一格"） |
| 优先级 | 设置了 `sheet_path` 时 `ResolveFrames` 走整图切片；清空即回到原「序列帧列表」逐张模式（原模式完全不受影响） |
| 播放 | 预览与战斗共用 `SpriteAnimationPlayer`（`frame_rate`/循环/缩放曲线/透明曲线/颜色渐变/混合模式全部照旧生效），因此动作节点（如 202035 造成伤害）与事件节点（OnAfterDamage 等）都能直接出序列图动画 |

**特效不显示时怎么定位（运行时诊断日志，同一条最多 3 次）**

| 日志 | 含义 |
|---|---|
| `[VFX] 节点 202042 触发特效（动作节点 绑定=TargetCard 素材=序列图 …）` | 图执行到了该节点，且运行时确实读到了特效配置（**这条不出现 = 图没执行 / 节点类型不对 / 配置没落盘**） |
| `[VFX] 不出特效：该节点未配置特效或未选素材` | 配置丢了或素材为空 |
| `[VFX] 不出特效：当前不在主线程（AI 推演阶段）` | AI 预测路径不产生特效（设计如此；玩家真实出牌在主线程） |
| `[VFX] 不出特效：当前不在对战表现场景（GameBoard 不存在）` | 服务器/无头端 |
| `[VFX] 不出特效：找不到绑定对象（bind=… 目标卡=…）` | 绑定目标既不在战场、其拥有者地块也不存在 |
| `[VFX] 播放特效：25 帧 / 12fps · 时长 2.08s · 挂点=PlayerZone2 · 绑定=TargetCard · 排序=…(层…) · 父级缩放=7.5,2.2 · 单帧世界尺寸≈1.54单位 · 世界坐标=…` | 已生成，含实际挂点与**排序/父级缩放/世界尺寸/世界坐标**（据此判断"是否被遮挡/被缩放/跑到屏幕外"） |

**挂点是"玩家地块"时的渲染注意（2026-09-13 修复）**：`BoardSlotPlayer`（继承 `BSlot`）**自带 SpriteRenderer**，那是"可攻击高亮背景"，排序序很低（`BSlot.Update` 只驱动它的透明度），且地块常带缩放（用 1 单位贴图铺满整块区域）。因此：
- 旧的"`parent.sortingOrder + 3` + 直接挂为子物体"会让特效**被棋盘背景/卡面挡住**、或被父级缩放**放大/缩小到看不见**；
- 现在：排序改为**与场上卡同层、卡序 +30**（无卡时用父级层 + 保底 100 序）；并用 `parent.lossyScale` **反向补偿**（`SpriteAnimationPlayer.base_scale`），使特效**世界尺寸只由素材（单帧像素/PPU × 缩放倍率）决定**，与挂在卡上还是地块上无关；
- 位置是**地块原点**（不是英雄头像处）：需要贴合英雄时用「X/Y 偏移」，或把绑定改成「施法者 / 目标卡」。
| `[NodeDoc] 节点 202042(…) 是动作节点，特效「播放时机=事件触发时」不会生效；已按「动作时」播放` | 动作节点没有"事件触发"时机：旧实现会静默不播（这是"伤害节点特效无效"的常见原因），现在照播并提示改配置 |
| `[NodeDoc] 节点 202042(…) 配了特效但没有可用素材…` | 配置还在但素材为空/被禁用 |
| `[NodeDoc] 节点 202042(…) 缺少分类信息（旧版保存的节点），已跳过执行 → 既不结算也不出特效` | 旧图残留节点：必须删掉后从节点库重拖 |
| `[NodeDoc] 图被触发（action=ActivateEffect）但没有任何入口节点匹配 → 图内动作与特效都不会执行` | 图完全没执行：查入口的 目标类型/触发条件/优先级/标签 或能力是否挂上本图 |

**特效缩放怎么调（"显示过小"）**

| 手段 | 位置/参数 | 说明 |
|---|---|---|
| 整图缩放倍率 | `VFXConfig.sheet_scale`；界面「缩放 -/+（步长 0.1，范围 0.1–8）」+「×2 / ÷2」 | 实现为 PPU 反向补偿：`PPU = 100 / sheet_scale`，故 192px 单帧在 1.0 时是 1.92 个棋盘单位、0.8 时约 1.5 单位 |
| 逐帧缩放曲线 | `VFXConfig.scale_curve`；界面「缩放」预设（不缩放/弹出/脉冲/渐大/渐小） | 每帧 `localScale = curve(进度)`，与上面的倍率**相乘**（最终尺寸 = 单帧像素/PPU × curve(t)） |
| 播放速度 | `VFXConfig.frame_rate`；界面「帧率 -/+」 | 也决定单次时长 `Duration = 帧数/帧率` |
| 运行时动态调整 | 改 `cfg.sheet_scale`（下次播放生效）；一次性叠加可在 `VFXRuntime.SpawnRoutine` 里对 `go.transform.localScale *= k`；`SpriteAnimationPlayer.SetTimeScale(x)` 控速 | 选图时自动默认 `150/单帧像素`（192px → 0.8，约 1.5 单位） |

约束（务必遵守，否则会重现本次修复的故障）：
1. **只能在主线程**执行表现层代码：AI 推演（`AILogic.Execute`）在后台线程上执行同一张图，任何 `transform`/`Instantiate`/`AudioSource` 访问都必须先判 `MainThreadUtil.IsMainThread`；
2. 表现层异常不得外溢：现有 `TriggerNodeVfx` 与 `BgmBattleRuntime.Request*` 已包 `try/catch`，新增挂点请照做——异常会被 `AILogic` 当成"推演线程异常"终止 AI 计算；
3. 未配置（`!IsConfigured`）必须零开销，且不得影响执行顺序与对局状态（AI 预测阶段不应产生特效）。

## 七、开发约定（新会话照做）

1. **双登记**：新节点 = `NodeDocRunner` 加执行/求值分支 + `GraphEditorPanel.SupportedNodeIds` 加 id；`Value` 类节点（defineId 以 10/11 开头）走求值通道，`Action` 类（20/21）走 `ExecuteAction`。
2. **求值通道分工**：整数→`ResolveNodeInt`；布尔→`EvaluateConditionNode`；卡牌集合→`ResolveCollectionNode`；单卡→`ResolveValueCard`；定义→`ResolveValueDefine/EvaluateDefineArray`；玩家→`ResolvePlayerOutput`；效果→`ResolveEffectList`；Pile→`ResolveValuePile`（`"玩家id|区域名"`）；事件→`ResolveValueEvent/ResolveEventList`；卡牌快照→`ResolveSnapshot`（深克隆 Card）；增益→`ResolveValueBuff/ResolveBuffList`（`BuffRef`）；Object/杂项→`GetObjectInput`。
3. **注释口径**：每个 case 注释写清"近似映射的取舍"，与本文第五节一致。
4. **不动**：`NodeDoc.xml`（数据源）、过时节点、负数 id 节点。
5. **验证**：改完跑 `read_lints`（三个核心文件）+ 白名单统计；不引入新依赖。
6. 端口字段：`Object`/`CardTagName` 等无默认字段的口，需要时在 `NodePresetFromDoc` 补 `FieldDef`（参考 112002 的 A/B、102033 的 tag）。

## 八、特效系统（VFX）现状与待办（2026-09-13，跨会话交接 ★新会话从这里继续）

### 8.1 已完成（代码可用，lint 全绿）

| 能力 | 落点 | 说明 |
|---|---|---|
| 本地序列图（整张 PNG 切片） | `VFXConfig.sheet_*` / `VFXFrameLibrary.ResolveSheet/ProbeSheet/SheetCellCandidates/AutoSheetCell/AutoGrid` | **统一分割**：单帧取 w、h 公因数（自动=最接近 192）；运行时对"不能整除"的配置自校正。RPG Maker 动画单帧恒 192×192（960×960=5×5、960×768=5×4、960×576=5×3、960×384=5×2、960×192=5×1、960×1152=5×6、768×192=4×1、576×192=3×1）。读盘复用 `ImagePicker.TryLoadTextureFromFile`；切片**行优先 + y 翻转**；贴图按"路径+修改时间"缓存 |
| 编辑器（序列图部分） | `VFXEditorPopup` | 顶栏：选择序列图…/试播/停止/来回播/**切片设置…**（「选择内置图片」与内置帧列表**已按要求移除**，旧 frame_names 仍可播+提供清除按钮）；面板：单帧尺寸±（整除候选间切换）/自动识别/播放行/范围复位/起始帧/帧数/缩放（0.1 步长，上限 8，含 ×2/÷2）/切片帧预览网格 |
| 线程安全 | `Tools/MainThreadUtil.cs` + `VFXRuntime`/`BgmManager`/`BgmBattleRuntime` 守卫 | AI 推演（后台线程）执行同一张图：表现层必须先判主线程；`TriggerNodeVfx`/`BgmBattleRuntime.Request*` 有 try/catch，异常不外溢终止 AI 计算 |
| 排序/缩放补偿 | `VFXRuntime.SpawnRoutine` + `SpriteAnimationPlayer.base_scale` | 排序=与场上卡同层、卡序+30（无卡退父级层+保底 100）；按父级 lossyScale 反向补偿 → 世界尺寸只由素材决定（修复"挂在玩家地块看不见/大小失控"） |
| 锚点符号修正 | `SpawnRoutine` 的 pivot_shift | **Bottom/Top/Left/Right 原来符号全反**（注释语义="特效中心放到绑定对象的下缘/上缘/左缘/右缘"），已修正；弹道模式下两端用锚点原点、不叠加 pivot 偏移 |
| 诊断日志 | `NodeDocRunner.TriggerNodeVfx`/`Run`/`WalkNode` + `VFXRuntime.Diag` | 触发/未配置/时机门控/旧节点跳过/图无入口匹配/播放详情（排序、父级缩放、世界尺寸、世界坐标）全部有日志，同条限次防刷屏 |
| **弹道飞行（运行时已接好）** | `VFXConfig.travel/bind_to/travel_duration/travel_curve`（+`TravelPreset("linear"/"ease_out"/"ease_in_out")`）、`VFXRuntime.ResolveBindContext`、新文件 `VFX/VFXTravel.cs` | 开弹道后 bind=起点、bind_to=终点；`VFXTravel` 每帧用两端**活 Transform** 的世界坐标 Lerp（卡/地块移动轨迹跟随），unscaledDeltaTime 推进；`life = max(动画时长, 飞行时长)`；终点解析不到→原地播放+日志 |
| **音效（运行时已接好）** | `VFXConfig.audio_path/audio_volume/audio_loop`（+`HasAudio`）、新文件 `VFX/VFXAudio.cs` | 播放复用 `AudioPicker`（本地绝对路径→`LoadPath`；否则 Workshop 图库→`LoadWorkshop`；再退 Resources）。解码异步（首次可能晚几十毫秒），成功进缓存即点即响；内部 4 路音频源池、不随特效销毁；`VFXAudio.Play` 内含主线程判定；`StopAll()` 由 `VFXRuntime.ClearAll()` 调用 |
| 编辑器（弹道/音效行） | `VFXEditorPopup` | `RowTravel`（弹道开关 + 终点 cycle + 飞行 ±0.1 + 缓动 cycle，未开弹道时右侧隐藏）、`RowAudio`（选择音效…/清除音效/音量 ±0.1 + 文件名）；`PivotLabel` 文案改为「锚点：绑定对象中心/下缘/上缘/左缘/右缘」 |

### 8.2 待办（新会话继续做）

1. **[x] `VFXEditorPopup` 补两行 UI**（运行时已就绪，编辑入口已补齐）：
   - 弹道行（`RowTravel`）：`弹道：关/开`（toggle `cfg.travel`）→ 开时右侧三项整体显示：`终点：目标卡/施法者/场景固定`（cycle `cfg.bind_to`）+ `飞行 x.xs`（±0.1，0=跟动画等长，显示为"等长"）+ `缓动：线性/缓出/缓入缓出`（cycle `cfg.travel_curve`，用 `VFXConfig.TravelPreset(TRAVEL_PRESETS[i])` 重建）；
   - 音效行（`RowAudio`）：`选择音效…`（`TcgEngine.UI.AudioPicker.OpenLocalFile()` → `cfg.audio_path`，平台限制同图片）、`清除音效`、`音量 0.x`（±0.1）、右侧显示当前音效文件名；
   - 插入位置：`EnsureBuilt()` 里 r3（位置行）之后 r3b/r3c；`PivotLabel` 文案已改为无歧义的「锚点：绑定对象中心/下缘/上缘/左缘/右缘」（r3 的「绑定/锚点」按钮同步加宽 150，X/Y 偏移控件右移，避免长文案被截断）。
2. **[ ] 验证**（需在 Unity 里由用户执行）：编译后 → 节点特效编辑器配 Darkness2.png（应自动 5×5/缩放 0.8）→ 弹道开、终点=目标卡、时长 0.5s、选一个音效 → 由玩家出牌（AI 回合不出特效是设计）→ Console 看 `[VFX] 播放特效… 弹道→… 音效=…`；音效首次播放可能略滞后（异步解码）属正常。
3. **[x] 文档**：§六之二 的音效行已改"已实现"，并新增弹道行（本次交接已列在 §8.1）。
4. **[x] `VFXAudio.StopAll()` 已挂到 `VFXRuntime.ClearAll()`**（切场景/结束战斗时一并静音，避免残留尾音）。

### 8.3 关键文件速查

`Scripts/VFX/`：`VFXConfig.cs`（配置+缓动预设）、`VFXFrameLibrary.cs`（素材解析/切片/缓存）、`VFXRuntime.cs`（触发/生命周期/排序/缩放补偿/弹道接线）、`SpriteAnimationPlayer.cs`（帧播放/曲线/base_scale）、`VFXAudio.cs`（音效）、`VFXTravel.cs`（弹道）、`Tools/MainThreadUtil.cs`；编辑器 `Scripts/UI/VFX/VFXEditorPopup.cs`；诊断口径见 §六之二。

## 九、起动式（activated）两个触发节点：起动时 / 起动后（2026-09-13 落地）

> 注意区分：**起动（Activate/activated）**= 玩家**点击发动**的能力（`AbilityTrigger.Activate`=5，资产 `Resources/Abilities/activated/activate_*.asset`）；
> **启动（GameStart）**= 对局开局通知（`OnBeforeGameStart/OnAfterGameStart`）。本节讲前者。

### 9.1 两个节点与引擎广播点

| 节点（编辑器名） | action（=AbilityTrigger 名） | 引擎广播点 | 语义 | value |
|---|---|---|---|---|
| **起动时** | `OnBeforeActivate`（新增枚举 88） | `GameLogic.CastAbility` 内、`CanCastAbility` 通过之后、真正结算之前 | **任意玩家发动起动式能力之前**；**可「阻止本事件」→ 本次发动取消**（不记历史、不横置、不扣灵力、不结算） | 本次灵力费用 `mana_cost` |
| **起动后** | `OnAfterActivate`（新增枚举 89） | `GameLogic.AfterAbilityResolved` 内，仅当 `trigger==Activate`，且**扣费/横置之后、UpdateOngoing 之前** | 发动**结算后**（灵力已扣、exhausted 已生效、效果已结算）；纯通知不可阻止 | 本次灵力费用 |

顺序：同一张卡的起动永远 **起动时 → 结算 → 起动后**；同一次广播内按入口 `priority` 降序，同值按"先手方→后手方；英雄→战场→装备区→奥秘区→手牌→牌库→墓地"。
（`EmitActivateBefore/After` 都把**发动卡作为额外宿主**传入 → 它自己的图无论所在区域都能响应。）

### 9.2 两个节点的输入/输出（编辑器设置）

**连线引脚**（与其它「X时/X后」事件入口完全一致）：
- 输入：`cond`「触发条件」(Boolean，连线为假=本次不触发)
- 输出：`out`「触发」(Flow)、`card`「卡牌」(Card=发动该能力的卡；英雄技能=英雄卡)、`player`「玩家」(Player=发动者)、`pos`「位置」(Int32)

**节点字段**：

| 字段 | 适用 | 默认 | 说明 |
|---|---|---|---|
| `zones`「生效区域」(MultiOptions) | 两个节点 | `英雄;战场;装备区`（起动载体=英雄/场上卡/装备卡） | 普通宿主必须在该区域才响应；`EventZoneDefault` 已把这两个 action 并入该默认 |
| `tags`「标签列表」/ `priority`「优先级」/ `custom_props`「自定义效果属性」 | 两个节点 | 无 / 0 / 空 | 与其它事件入口一致 |
| `delay_ms`「延迟(毫秒)」(Int) | **仅起动后** | `0` | `>0` → 入队，`GameLogic.Update` 按真实时间累计到点后执行一次 |
| `wait_event`「等待事件」(Dropdown) | **仅起动后** | `无` | 无/打出牌/伤害后/治疗后/死亡后/装备后/抽卡后/回合开始后/回合结束后/**自己起动后** → 映射 `OnBeforePlay/OnAfterDamage/OnAfterHeal/OnAfterDeath/OnAfterEquip/OnAfterDraw/OnAfterTurnStart/OnAfterTurnEnd/OnAfterActivate` |

**时机组合语义（或关系）**：`delay_ms>0` 与 `wait_event≠无` 配了哪些就等哪些，**任一满足即执行一次**（执行后出队）；都留空 = 发动结算后立即同步执行。

### 9.3 数据传递

- **同一次广播内**：`GraphEventContext`（`card`=发动卡、`player`=发动者、`value`=灵力费用、`turn`）→ 入口 `card`/`player` 输出口读取；写/读变量用 `208001 设置变量` / `108002 获取变量`（槽位仅 数值/来源/玩家/卡牌）。
- **跨广播（起动时 → 起动后 / 跨回合）**：`temp_vars` 每次 `Run` 结束即清空，**不能跨事件传值**；跨事件状态必须落 **卡/玩家的特性、状态(StatusType)、增益(Buff)** 再回读。
- **延后（延迟/等待）触发时**：`RunPendingTrigger` 会**新合成独立事件上下文**（`action=OnAfterActivate`、`card=宿主`、`player=宿主拥有者`、`turn=当前回合`），不携带"触发它的那个事件"的属性，也不参与该事件的阻止/改值。

### 9.4 异常处理与稳定性

| 机制 | 落点 |
|---|---|
| 起动类图异常隔离 | `GameLogic.FireEventHost`：`ctx.action` 为 `OnBeforeActivate/OnAfterActivate` 时 `NodeDocRunner.RunEvent` 包 `try/catch` → `LogError` 后继续（**坏图不会中断能力发动或整次广播**） |
| 延后执行异常隔离 | `GameLogic.RunPendingTrigger` 内 `try/catch`（同上） |
| 阻止语义 | 仅 `OnBeforeActivate`（phase=Before）可被「阻止本事件」置 `cancelled` → `CastAbility` 检测后 `return`；`OnAfterActivate` 为 After，阻止动作只会打警告 |
| AI 预测零副作用 | `EnqueuePendingTrigger` 对 `is_ai_predict` 直接返回；`EmitGraphEvent` 在预测实例上也不广播 |
| 生命周期 | `StartGame`/`EndGame` → `ResetPendingTriggers()` 清空延后队列；宿主为 null 的待执行项自动丢弃 |
| 队列与回合解耦 | 队列**跨回合保留**（不受 `ClearTurnData/resolve_queue.Clear()` 影响），延迟秒数用 `GameLogic.Update(delta)` 累计 |
| 递归 | `EmitGraphEvent` 的 `event_depth` 上限 16 兜住"起动后→再发动→再起动后"的循环 |

### 9.5 可独立测试（只读、不改状态）

| API | 用途 |
|---|---|
| `GameLogic.PreviewEventTriggers(action)` | 传入 `OnBeforeActivate`/`OnAfterActivate`：列出**会命中哪些宿主入口、按什么顺序、参数（priority/生效区域/延迟/等待事件）**；不执行动作、不入队。命中 0 时直接给出排查提示 |
| Console 日志 | `[起动触发] 已入队：OnAfterActivate card=x 条件=延迟500ms`、`等待的事件已到达 ← OnAfterDamage`、`延迟结束`、`执行 …（延迟到点/等待的事件已到达）`、`被「阻止本事件」取消`、`图执行异常，已隔离` |

### 9.6 ⚠ 语义注意（配置前必读）

1. **起动触发是全场广播**：宿主=双方所有区域（英雄/战场/装备区/奥秘区/手牌/牌库/墓地）。任何玩家发动任何起动式能力，都会广播给所有匹配入口的卡 → 入口条件（`cond`）要写清"只对谁响应"。

### 9.7 起动式效果入口（`ActivateAbility`）——"点击发动"的配置路径

> 为什么需要它：`ActivateEffect`（主动效果入口）编译成 `AbilityTrigger.OnPlay`＝**打出时**触发；`MapGraphTrigger` 原先没有 `Activate` 映射，所以**节点编辑器画不出"点击发动"的能力**。现在新增第 5 个入口补齐。

**节点规格**（分类「入口」，紧跟在「主动效果入口」之后）：

| 输入（连线） | 说明 |
|---|---|
| `cond`「发动条件」(Boolean) | 编译进 `conditions_trigger` → **假时按钮灰、且发动被拒绝** |
| `targetConditionN`「目标N条件」(Boolean) | 由「＋ 新增目标」生成；为编译为槽私有 `ConditionGraphTarget`（如"只能选随从"） |

| 输出（连线） | 说明 |
|---|---|
| `out`「发动」(Flow) | 接动作链 |
| `player`「玩家」(Player) | 发动者（卡拥有者） |
| `card`「卡牌」(Card) | 本卡自身（英雄技能=英雄卡） |
| `targetCardN`「目标卡牌N」(Card) | 第 N 个目标槽选中的卡（单槽时回退"能力选中目标"） |

| 字段 | 默认 | 说明 |
|---|---|---|
| `mana_cost`「灵力费用」 | 0 | 发动后扣（`AfterAbilityResolved` 里对 `trigger==Activate` 扣费） |
| `exhaust`「消耗行动」 | 开 | 开=发动后本卡横置（每回合一次）；关=只受灵力限制 |
| `once_per_turn`「每回合一次」 | 关 | 开 → 追加 `ConditionOnce`（本回合已发动过就不能再发动） |
| `ability_title`「能力名」/`ability_desc`「能力描述」 | 空 | 空=自动生成；`desc` 支持 `<value>/<name>`，显示在技能按钮悬浮说明 |
| `target_typeN`「目标N类型」/`target_errorN`「目标N提示」 | 由「＋ 新增目标」生成 | `角色`=弹选（含英雄）/`英雄`=直接指玩家/`无`=该槽不参与 |
| `unique_targets`「目标去重」 | 关 | ≥2 槽时显示 |

**编译链**：`MapGraphTrigger("ActivateAbility") → AbilityTrigger.Activate`；`ApplyEntryOverrides` 读费用/横置/每回合一次/名称描述，目标槽复用主动效果入口那套（单槽→`SelectTarget`；≥2 槽→逐槽顺序选择）。
**UI 零改动**：`HeroUI`（英雄技能按钮）与 `BoardCard`（场上卡/装备卡技能按钮）本来就按 `trigger == Activate` 出按钮，并显示 `mana_cost`。

**⚠ 注意**：随从刚入场当回合受 `SummonDisorder` 限制不能发动（除非有"急速"）；`Activate` 能力只在**按钮可用**时能发动。

**备选（不画图）**：`Resources/Abilities/activated/activate_damage1(1).asset` 就是"1 费造成 1 点伤害"（`trigger:5, target:30, mana_cost:1, effects=damage.asset, value:1`），可直接挂到**资产卡**（`CardData.asset`）的 `Abilities` 数组上；但卡池（Workshop JSON）驱动的自定义卡没有配 `AbilityCustomData` 的 UI，只能在节点编辑器里画。

2. **`cond` 里的「卡牌归属(己方/敌方)」基准 = 宿主自己**（`cur_event.player` 为空时回退 `caster.player_id`，而 caster 就是宿主）→ 对"发动者是谁"没有区分度。要按发动方筛选请用 `入口.玩家` 与 `101019 玩家是否是先手`、或 `EFPlayerOwner` 接**玩家口**。
3. **`起动时` 早于目标选择**：`CastAbility` 在点击瞬间即广播，`SelectTarget` 类能力此时还没选目标；`起动后` 在选完目标、结算完之后才广播。
4. **`起动时` 的 value 只读**：改 `value` 不会改变实际扣费（扣费按 `AbilityData.mana_cost`）；"阻止"是唯一生效的干涉方式。
5. **手写 `AbilityTrigger` 的能力不会因此生效**：`OnBeforeActivate/OnAfterActivate` 只作为**图入口**（事件监听）使用，不会出现在 `activate_*.asset` 这类能力上。
6. 改的是**编辑器预设 + 引擎行为**，不是场景对象；无需重跑 Builder，打开规则编辑器重新拖入口即可（已有节点的字段会保留）。

