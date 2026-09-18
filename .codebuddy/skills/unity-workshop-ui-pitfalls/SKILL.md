---
name: unity-workshop-ui-pitfalls
description: Unity 项目（TCG2）工作台/卡牌编辑器类界面的高频坑与硬规则：运行时新建文本必须用 TMP + UIFonts（不能用旧版 uGUI Text，会"字体没改/发糊"）、列表行高必须 childControlHeight=true（否则 LayoutElement 被忽略、一页只剩 3 条）、运行时自建 uGUI 与场景遗留控件重叠（两个 ×、按钮点不到）、按钮锚点写错跑到框外、弹框遮罩盖住目标页面、数据类 API 不统一导致编译报错（KeywordData 没有 GetTitle）、弹框风格对齐既有"多选弹层"规格、旧页面弃用如何"不再出现在入口"且不报 missing script。另含同类高频问题：同名区块重复（Destroy 延迟 + Find 命中待销毁对象）、同行控件叠字（父级不同 / 按名字找控件失败）、TMP 与旧 uGUI 枚举混用（CS0266）、占位色未还原导致图片"蒙灰"、丢 sprite 的 Image 变白方块、LayoutElement 在未开 childControlHeight 的容器里失效、弹框遮罩"误触关闭/穿透点击"、同一参数在节点上出现两行（字段名≠端口名）、参数框不弹选择器（新增 FieldEditType 漏渲染分支）、C# 字符串嵌半角引号导致 CS1002。当出现字体不一致/发糊、列表一页只显示 3 条、控件重叠点不到、控件重复出现、同一参数重复出现、弹框风格不一致，或新增弹框/选择器/编辑器入口时使用。
---

# 工作台/卡牌编辑器 UI 高频坑与修法（TCG2）

## ⚠️ 硬规则（每次动手前先过一遍，这两条已反复被漏）

1. **字体：运行时新建文本一律 TMP + 全项目字体管线**，不用旧版 uGUI `Text`
   - 单个文本：`UIFonts.ApplyFont(t)`；整个自建界面：构建完 `UIFonts.ApplyResolved(root)`。
   - 创建文本：`TextMeshProUGUI` + `alignment` 用 `TextAlignmentOptions`（别传 `TextAnchor`，
     要写个 `ToTmpAlignment` 转换）。
   - **输入框**：`TMP_InputField` + 必须给 `textViewport`（"Text Area"）+ 绑定后
     `input.enabled=false/true` 建光标 + `caretColor = 白` + `TmpInputUtil.Write/Guard/Read`。
   - 反面：`AddComponent<Text>()` + `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
     → 与页面其它文字字体不一致、发糊、中文可能缺字（就是"字体又没改"那个问题）。
2. **列表行高必须锁死**：`VerticalLayoutGroup.childControlHeight = true`
   - 否则子物体上的 `LayoutElement(minHeight/preferredHeight = 30)` **完全被忽略**，行高退化成
     RectTransform 默认的 **100** → 一页只能看到 3 条。
   - 双保险：行模板同时设 `rectTransform.sizeDelta = (0, 30)`。
   - 对齐基准：规则编辑器多选弹层的选项行 = **30**。

## 🔎 症状速查（遇到问题先看这里，再往下翻对应坑）

| 你看到的现象 | 直接看 |
|---|---|
| 字体不一致/发糊、中文缺字；空输入框点一次再点开报 `IndexOutOfRangeException`；输入框没有光标 | 硬规则 1 + 技能 `unity-tmp-pitfalls` |
| 列表一页只显示 3 条；自建区块高度塌陷/"没显示" | 硬规则 2、坑 16、坑 21 |
| 控件重叠点不到（两个 ×、按钮被压住）、同一行文字叠字 | 坑 1、坑 2、坑 13 |
| **同一参数在节点上出现两行**（如两个「按钮」） | 坑 22 |
| 参数框不弹选择器、静默退化成输入框 | 坑 23 |
| 区块/控件重复出现（两个「自定义属性」、重复节点） | 坑 12、坑 20 |
| 点按钮**当场没反应**，要等下一步操作才生效 | 坑 26 |
| 鼠标悬浮**什么都不弹** | 坑 27 |
| **界面全都点不动**（只剩某个面板自己的按钮能点） | 坑 30（全屏 UIPanel 常显）+ 坑 31（射线采样定位遮挡） |
| 客户端永远停在 **Connecting to server…** | 坑 29（服务端流程被异常打断，异步任务静默中止） |
| `Update()` 里 NRE → 某个 UI 功能莫名不生效 | 坑 28 |
| 编译报 CS1061 / CS0029 / CS0266 / CS0019 / CS0246 / CS1503 / CS1002 | 坑 4、坑 9、坑 14、坑 24 |
| 卡图"蒙灰" / 界面出现白方块 | 坑 17、坑 18 |
| 弹框点空白处误关、点穿到背后 | 坑 19 |
| 数据改造后老数据丢了 / 删掉的行又复活 | 坑 15 |
| **MCP：点 ▶ 进不去 Play / `play_mode_start` 一直"排队"** | 附录 D-1（先查 CS 错误）、D-3 |
| **MCP：`get_script_errors` 报 0 但其实有错 / 日志搜不到、统计恒 0** | 附录 D-1、D-2 |
| **MCP：想验证运行时 UI 但看不到画面 / 查不到深层对象** | 附录 D-4、D-5（探针 SOP） |

## 坑 11：UI 射线结果的 API 记混 → CS1061

**真实报错**
```
GraphEditorPanel.cs(2324,33): error CS1061: 'RaycastResult' does not contain a definition for 'graphic'
```

**根因**：`graphic` 是**物理** `RaycastHit` / `RaycastHit2D` 的成员；UGUI 的
`UnityEngine.EventSystems.RaycastResult` **没有**它。它只有 `gameObject / module / distance / index /
depth / sortingLayer / sortingOrder / worldPosition / screenPosition`。

**正确写法**：`Graphic g = hits[0].gameObject.GetComponent<Graphic>();`
（`EventSystem.RaycastAll(pointerData, results)` 之后取 `results[0].gameObject` 即可。）

**易混点**：`Toggle.graphic` 是 `Selectable` 的字段（合法），别被它误导成 `RaycastResult` 也有。

**排查"界面点不动"的实用套路**：用上面的 `RaycastAll` 在关键位置（工具栏/右栏/画布）采样，
把 `results[0].gameObject` 的层级路径打出来 —— 一眼看到是谁挡在按钮上面；
若它自身与祖先都没有 `Selectable / TMP_InputField / ScrollRect / 拖拽层`，就是"无交互能力的隐形遮挡物"，
直接 `raycastTarget = false` 即可（画布平移层不算，必须排除，否则会破坏平移/缩放）。

## 坑 1：运行时自建控件与**场景遗留控件**重叠（"两个 ×，其中一个压在按钮上"）

**现象**：右侧「变量配置」列上方或按钮上多出一个 ×；按钮点不到 / 视觉重叠。

**根因**：这些页面是"Builder 生成场景 + 运行时归位"的混合体。运行时新建的区域（如
`CardEditorPanel.EnsureEditorArea()` 的 `EditorArea`）与**场景里旧的同类对象**同时存在；
旧关闭按钮锚在 `(1, 0.5)` 之类的位置，正好落在运行时新建的按钮列上。

**修法（运行时收敛，不改场景）**：
- 先把配置列右端收窄，给 × 留出固定间距（改 `sizeDelta.x`，注意 UGUI 语义是
  `sizeDelta = -(offsetMin + offsetMax)`，两者都设时**后者生效**）。
- 自己建的 × 必须"最后创建 + `SetAsLastSibling()`"，否则被按钮列压住点不到。
- 再写一个 `ParkForeignCloseButtons()`：遍历面板内所有 `Button`，凡**名字含 close 或文字是
  `×/X/✕`** 且矩形与配置列 `Rect.Overlaps` 相交者，统一 `SetParent(transform)` 后锚到面板右上角
  （保留原点击逻辑，不删对象）。
- `Awake` 里判一次 + `yield return null` 一帧后再判一次（LayoutGroup/ContentSizeFitter 首帧才定尺寸，
  Awake 时矩形可能还是 0）。

**实战案例（2026-09 主菜单）**：运行时注入的「音乐库 / 界面BGM配置 / 局域网对战」三个入口按钮被场景里已有的
`UICanvasTop/HomePanel/Logo`（151×113 的图片，还带 `Selectable`）压住 —— 用户描述"右上角一堆图标挡住后面的东西"。
用**临时探针的"区域清点"**（遍历根画布所有 RectTransform、把与右上角区域相交的 UI 按路径+屏幕矩形打出来）一次定位：
区域里 12 个 UI 的矩形一列出来，谁压着谁一目了然
（`Logo[1353,849]~[1484,946]` vs `MusicLibraryBtn[1361,905]~[1475,937]` vs `LanBtn[1345,865]~[1475,898]`）。
结论：**运行时注入的元素必须避让场景既有元素**（挪位置，或运行时隐藏场景里那个）；
"这堆东西都是谁"优先用区域清点（不依赖鼠标精度），指针采样适合"某个点到底是谁"；
定完位把探针删掉，最终解决方式是**在编辑器里把那个场景对象删掉**（本例删 `HomePanel/Logo`）。
另：`Menu.unity` 里同名 `Logo` 有 **5 个**（每个页面面板各一个）→ 按名字处理时**必须限定在目标面板下**再找。

**检查点**：`GetWorldCorners` 求世界矩形做相交判断；把自己新建的按钮排除在"外来按钮"之外，
否则会把自己的 × 挪走。

## 坑 2：运行时 `MakeButton` 锚点写死导致 × 跑到弹框外

**根因**：为了复用，`MakeButton(...)` 常写成"底部居中锚点 + anchoredPosition"，用它创建右上角 ×
就会跑到框外（或压在别的控件上）。

**修法**：复用工厂方法后**显式覆写** `anchorMin/anchorMax/pivot/anchoredPosition/sizeDelta`
（4 行，别忘 pivot）；或给工厂加锚点参数。

## 坑 3：弹框遮罩盖住目标页面（"点编辑后按钮全点不动"）

**根因**：弹框是挂在 Canvas 根上的全屏遮罩（`MaskPopup = (0,0,0,0.55)`，`SetAsLastSibling`），
`open_editor` 直接切页 → 遮罩仍在最上层。

**修法**：**先 `Close()` 弹框，再进入目标编辑器页面**；弹框内所有"跳页"分支都要走这一条。

## 坑 4：数据类 API 不统一 → 编译期 CS1061

**真实报错**：
```
VariableSelectPopup.cs(359,71): error CS1061: 'KeywordData' does not contain a definition for 'GetTitle'
```

| 数据类 | 取标题的正确写法 |
|---|---|
| `BuffData` | `GetTitle()`（有） |
| `TraitData` | `GetTitle()`（有） |
| `KeywordData` | **只有字段 `title`**（没有 GetTitle） |
| `BattleButtonData` | **只有字段 `title`** |

**修法/预防**：跨数据类写通用代码时统一用 `string.IsNullOrEmpty(x.title) ? x.id : x.title`；
新增数据源适配前先确认该类到底有字段还是方法（别按同类推）。

## 坑 5：新弹框风格必须对齐既有「多选弹层」

项目里已有一套被认可的弹层规格（`GraphEditorPanel.EnsureFieldSelectPopup` /
`CreateSelectOptionRowEx`），新弹框直接照抄：

| 项 | 规格 |
|---|---|
| 遮罩 | `(0,0,0,0.55)`（= `UITheme.MaskPopup`），点击遮罩关闭 |
| 面板 | 440×520，底 `(0.12,0.12,0.15,1)`（= `UITheme.BgPopup`），panel 自身挂 Button 吞点击 |
| 标题 | TMP/Text 22 号，上边内边距 6、高 34、`sizeDelta.x = -60`（给 × 留位） |
| 关闭 × | 34×34，右上 `(-6,-6)`，pivot(1,1) |
| 滚动区 | 左右 12、上 46、下按底部控件留白；`RectMask2D` + `VerticalLayoutGroup(spacing 3, padding 4)` + `ContentSizeFitter(PreferredSize)` |
| 选项行 | 高 30；选中 `(0.2,0.55,0.85,0.95)`，常态 `(1,1,1,0.08)`；文案前缀**选中 `√ `、未选中 `□ `**，18 号，左右内边距 12 |
| 底部按钮 | 高 34（如「使用」76×34） |

## 坑 6：旧页面弃用要"不再出现在入口"且**不能报 missing script**

**结论**：**不要**直接删掉被场景引用的 MonoBehaviour（`Menu.unity` 里仍有对象引用它 → Unity 报
"associated script can not be loaded"）。

**推荐做法**：
1. 删掉所有**代码入口**（重挂监听、删除旧的 `OnOpenXxx` 处理器），并 `RemoveAllListeners()` 清掉
   Builder 时代挂上的旧监听，避免"一个按钮两个行为"。
2. 该页若还要当"单条编辑落地页"，给它补 `EditXxx(id)` 公开入口（打开并选中），并在 `Show()` 里
   **重定向**：非编辑路径调用 → 打开新的统一弹框（`Debug.Log` 记录一次），旧页面从此只作为编辑落地页。
3. 生成工具（`XxxBuilder.cs`）可直接删除（静态类、无场景引用）。
4. 场景对象/导航标签的物理删除留给编辑器里手工做（或运行时重定向兜住），并在交付说明里点明。

## 坑 7：「新增」与「编辑」的语义边界（本次产品要求）

- **新增**：只在**当前列表**追加一个空项 + 写盘 + 默认选中，**不跳转**任何编辑器页面。
- **编辑**：才进入对应编辑器页面（并选中该项）。
- 删除：必须二次确认（确认才删、取消/遮罩/×/Esc 都不做事），无选中项时按钮置灰（`interactable=false` + 视觉置灰）。

## 坑 8：字体/行高自查（交付前必过）

- 全仓搜 `AddComponent<Text>()`：只应出现在"迁移前遗留/旧版兼容"处；**新建弹框/面板不能有**。
- 全仓搜 `LegacyRuntime.ttf`：同上。
- 每个 `VerticalLayoutGroup` 检查 `childControlHeight` 是否为 `true`（列表类容器）。
- 拿弹框列表数一下：480 高的列表区在 30 行高下应能看到 **≈12 条**；只看到 3 条 = 行高没锁住。

## 坑 9：同类控件"批量迁移"时漏改局部变量 → CS0029（本项目已踩两次）

**真实报错**
```
VariableSelectPopup.cs(784,22): error CS0029: Cannot implicitly convert type
'TMPro.TMP_Text' to 'UnityEngine.UI.Text'
```

**根因**：把 `MakeText` 从"返回 `Text`"改成"返回 `TMP_Text`"后，**调用点要逐个改**；
`MakeButton` 与 `MakeBarButton` 里有**逐字相同**的几行（`Text t = MakeText(...)`），
替换时只命中了第一处 → 第二处类型不匹配。

**同类前科**：`CS1061: 'KeywordData' does not contain a definition for 'GetTitle'`（见坑 4）；
`CS1061: 'GameObject' does not contain a definition for 'parent'`（写 `go.parent` 应为 `go.transform.parent`，
父级/层级接口都在 `Transform` 上；写遍历父链的日志/查找工具函数时最容易踩）；
`CS0019: Operator '==' cannot be applied to operands of type 'TraitStat' and '<null>'` —— **结构体不能判 null**，
加防空时要先确认类型是 class 还是 struct（本项目 `TraitStat` 是 struct：`struct TraitStat { TraitData trait; int value; }`）；
`CS1503: cannot convert from 'UserDeckData' to 'DeckData'` —— 给已有方法包 try/catch 外壳时**先看它有几个重载**
（`GameLogic.SetPlayerDeck` 就有 `DeckData` / `UserDeckData` 两个），外壳要按类型各包一层；
`CS0246: 'TMP_InputField' could not be found` —— 新文件里用到 TMP 类型要 `using TMPro;`
（`TMP_Text`/`TextMeshProUGUI`/`TMP_InputField` 都在 `TMPro`；`Image`/`Selectable`/`ScrollRect`/
`RectMask2D`/`Mask` 在 `UnityEngine.UI`；`EventSystem`/`PointerEventData`/`RaycastResult`/
`EventTrigger` 在 `UnityEngine.EventSystems`）。新建排查工具类时最容易漏 using。

**修法/预防**
- 改函数返回类型后，先全仓搜**旧类型 + 变量名**：`(^|[^A-Za-z_])Text[ >]`、`AddComponent<Text>()`、
  `GetComponent<Text>()`、`LegacyRuntime.ttf`，逐条确认。
- 重复代码块（工厂方法成对出现时）用"含上下文"的方式分别替换，别用只命中一处的短锚点。
- **本环境的静态检查（read_lints）对这类错误可能报 0 条**——必须让 Unity 编译一次/看 Console，
  才能确认真实编译结果（本次就是 lint 干净、Unity 报 CS0029）。

## 增益（Buff）编辑器：数据 / 运行时 / UI 三段口径（2026-09 重构）

- **数据**：`BuffData.mods`（`BuffPropMod`：target / mode / value / value_source / enum_id）+ `custom_props` +
  `vfx`(VFXConfig，用 `VFXEditorPopup` 选) ；旧 `props` 由 `EnsureMods()` 幂等迁移、`SyncLegacyProps()` 反向同步
  —— 老代码/旧图继续读 `props` 不会错。
- **运行时**：`BuffRuntime.ApplyNative / ReapplyNative` 把 mods 落到卡上：
  - 数值型 → 原生状态：攻击 `AddAttack`、生命 `AddHP`、护甲 `Armor`、花费 `AddManaCost`（→ `mana_ongoing`）；
    **"设置为 X" 用 (X - 当前值) 的增量实现**；"引用属性" 读目标卡另一个属性的当前值。
  - 枚举型 → `Card.buff_added_traits / buff_removed_traits / buff_added_keywords / buff_removed_keywords`
    （`HasTrait/HasKeyword/GetAllTraits` 已接入）。**不要**复用 `ongoing_traits/ongoing_status`：
    那些由回合清理，混用会互相清掉。
  - **实例覆盖**：运行时写 `CardBuff.props` → 优先级高于定义（按卡独立，节点 206003 走 `SetPropByTarget`）。
  - `ReapplyNative` 只清"自己管的"四个状态 + 四个 buff_* 列表，其它来源不受影响（叠加语义正确）。
- **UI（规则编辑器的增益模式）**：右列 Tab 文案运行时改为「增益参数」（`ApplyBuffModeUI`，判定 `IsBuffMode`），
  面板构建/刷新在 `EnsureBuffForm / RefreshBuffForm / RebuildBuffModRows`；**行重建只在点击时发生**（Update 不做事）。
- **复用清单（照抄，别另起风格）**：`OpenSingleSelectPopup` / `OpenMultiSelectPopup`（选择弹层）、
  `MakeText` / `MakeButton`（TMP + `UIFonts` 字体管线）、`MakeBuffInput`（TMP 输入 + `TmpInputUtil`）、
  `VFXEditorPopup.Create/Open`（特效选择）、`SetStretchRect`。
- **动态行列表三条铁律**：行容器 `VerticalLayoutGroup.childControlHeight = true`；行上 `LayoutElement`
  定高（面板 34 / 列表 30）；行内单元格用锚点定位时记得 `LayoutElement.ignoreLayout = true`。
- **单入口**：旧 `BuffPanel` 只保留"列表 + 跳转"，参数编辑统一走新页（`BuffPanel.EditBuff` → `GraphEditorPanel.OpenBuff`）。

## 坑 10：命名空间归属搞错 → CS0246（VFX 家族）

**真实报错**
```
BuffData.cs(124,16): error CS0246: The type or namespace name 'VFXConfig' could not be found
```

**根因**：想当然以为 VFX 类型与数据类同命名空间。实际归属：

| 类型 | 命名空间 | 文件 |
|---|---|---|
| `VFXConfig` / `VFXRuntime` / `VFXFrameLibrary` / `VFXTravel` / `VFXAudio` | **`TcgEngine.VFX`** | `Scripts/VFX/*.cs` |
| `VFXEditorPopup`（UI 弹框） | **`TcgEngine.UI`** | `Scripts/UI/VFX/VFXEditorPopup.cs` |

**修法**：数据类/运行时类里声明 `VFXConfig`、调用 `VFXRuntime` 时补 `using TcgEngine.VFX;`；
`TcgEngine.UI` 的面板里用 `VFXEditorPopup` **不需要** using（同命名空间），lambda 参数 `cfg => cfg.HasFrames`
靠类型推断也不用写类型名。
**预防**：跨命名空间引用前先看目标文件里的 `namespace`，别按目录名/同类推测。

## 坑 12：同名区块"重复出现"——`Destroy` 延迟 + `Find` 命中"待销毁"对象（本项目已踩）

**现象**：卡牌参数底部出现**两个「自定义属性」**、每个还各带一个「＋ 新建自定义属性」。

**根因**：`RefreshXxx()` 的写法是 `Find(名字) → Destroy → 重新创建`。但 `Object.Destroy` **延迟到帧末**才真正销毁，
同一帧内本方法被调用多次时，第二次的 `Find` 又拿到那个"待销毁"的旧对象（销毁一次没用），随后**再建一个** →
每多调一次就多一个区块。

**修法（幂等收敛，别用 Find+Destroy）**：
1. **倒序扫描所有子物体**，凡同名（或同前缀）的一律 `SetActive(false)` **再** `Destroy`；
   `SetActive(false)` 让它在**当前帧立刻**从渲染/查找意图里消失，多次调用也不会累积出可见的重块。
2. 更稳的做法：**复用**已有区块（就地刷新内容），而不是"销毁重建"。
3. 区块命名要带唯一前缀（如 `CardCustomProps`），并顺手清掉历史名字（`CustomPropSection` 之类）。

## 坑 13：同一行控件"叠字/文字被重写"——按**名字**找控件失败 + 父级不同

**现象**：音乐配置每行出现「选择音频」与「音效DIY」**叠在一起**（看起来像"选择DIY"，文字被重写）。

**两个叠加的根因**：
1. `FindChildRect(field, "AudioInput"/"PickAudioBtn")` **按对象名**找控件；生成工具版本不同名字会变 →
   找不到就 `SetRowAnchors(null)` 静默跳过 → 旧控件留在原位，新按钮按比例摆过去 → 两个按钮同位置。
2. 锚点比例是**相对父级**的：按钮若被建在 `row` 上、其它控件在 `row/Field` 里，同一个 `0.65~0.83`
   会落到完全不同的位置 → 照样叠字（原代码注释里其实点明了"所有重排必须相对 Field 做"）。

**修法（结构无关的自愈式布局）**：
- 搜索范围放宽到**整行**（`field.parent`），按 **控件类型 + 按钮文字**分类：
  输入框=`TMP_InputField`；▶=`PlayAudioBtn*`/文字 `▶`；DIY=名字 `DiyAudioBtn`/文字 `音效DIY|DIY`；其余=选择按钮。
- **先 `SetParent(field, false)` 归位，再设锚点**（这一步才是"消除坐标系错位"的关键）。
- 同类里只留第一个，其余 `SetActive(false)`；一个按钮内若挂了多段文字也只留第一段。
- 收尾打一行**结构日志**（名称/父级/锚点/激活/文字），让"是否真的修好"变成可核对的数据，而不是肉眼判断。

## 坑 14：TMP 与旧 uGUI 枚举混用 → CS0266

**真实报错**
```
GraphEditorPanel.cs(2681,42): error CS0266: Cannot implicitly convert type 'UnityEngine.TextAnchor'
to 'TMPro.TextAlignmentOptions'
```
- `TMP_Text.alignment` 是 `TextAlignmentOptions`（如 `MidlineLeft`）；`TextAnchor`（如 `MiddleLeft`）是旧版 `Text` 的。
- 两者**不能隐式转换**；改一个控件类型时，`alignment` 取值必须同步改。
- 同文件里 1637/8119 行的 `alignment = TextAnchor.*` 是**旧版 `Text`**（`back.font = LegacyUIFont()`），**不要一起改**。

## 坑 15：数据结构改造要"旧字段并存 + 幂等迁移 + 反向同步"

**案例**：增益/卡牌要做「自定义属性（名称/类型/是否数组/**初始值**）」，旧数据只有 `custom_props`（纯名字列表）。

**规则**：
- **旧字段保留**（`custom_props` 继续存在，老 JSON 不丢数据）；新字段（`custom_prop_defs`）由
  `EnsureXxx()` **幂等迁移**（旧名字 → 类型=整数、初始值=0），不要写"无条件迁移"（会把用户删掉的行复活）。
- 声明格式统一对齐项目既有规范：**`名称:类型[:数组]`**（`Line()` 输出该格式，便于跨页面对照/打日志），
  并复用同一套类型命名（`BuffPropType`，新增卡牌侧只加字段不另起一套）。
- 同类数据（增益 / 卡牌）**共用同一个 DTO 类与同一套 UI 构建函数**：弹框用"目标列表 + 提交后刷新回调"
  参数化（`OpenCustomPropDialogFor(list, after)`），避免复制两份后样式漂移。

## 坑 16：运行时自建区块在 `VerticalLayoutGroup` 里"看不见"

父容器没开 `childControlHeight` 时，子物体上的 `LayoutElement(min/preferredHeight)` 会被忽略 →
自建区块高度塌成默认值。**双保险**：给区块 `LayoutElement` 的同时再设 `sizeDelta.y = 计算高度`
（高度 = 标题 32 + 行数×34 + 按钮 46）。

## 坑 17：「白方块 / 方框」多半是**丢了 sprite 的 Image**，不是缺字形

- Toggle 的方框/勾选图没有 sprite → 渲染成白色实心方块（用户描述"可组卡怎么有个方框"）。
- **修法**：方框 `targetGraphic.color = (1,1,1,0)`（**透明但仍参与射线检测**，UI 射线不看 alpha），
  勾选图 `graphic.enabled = false`，状态改用**文字**表达（`[开]/[关]`，中文+方括号绝无缺字风险），
  并挂 `onValueChanged` 实时刷新；文字挂在 Toggle 下或行容器里都要能兼容（两处都搜）。
- 不要用 `√/☐/▶` 之类符号去"替代图形"，字体缺字形时会变成另一种方块。

## 坑 18：Builder 给的"占位色"忘记还原 → 一张图"蒙灰"

**现象**：选了图之后卡图看起来蒙了一层灰（有时有、有时没有）。

**根因**：生成工具把预览 `Image` 初值设成占位灰 `new Color(1,1,1,0.1f)`；刷新只换 `sprite` 没有还原 `color` →
有图时按 10% 不透明度绘制 = 蒙灰（无图时本来就不显示，所以"有时候"）。
**规则**：凡"占位色/占位图"机制，刷新函数里必须 `color = 有图 ? Color.white : 占位色`，一个不能漏。

## 坑 19：弹框遮罩的"关闭语义"要按需求区分

- 选择类弹层（多选/单选）：**点遮罩关闭**（既有规格）。
- 表单类弹框（如「新建自定义属性」）：产品要求**点遮罩不关闭**（防误触），只有「确定/取消」能关；
  且必须 `Image.raycastTarget = true` + `CanvasGroup(blocksRaycasts/interactable = true)` 挡住**穿透点击与背景滚动**，
  同时在 `HandleShortcuts()` 开头判"弹框打开则 return"，否则画布快捷键会在填表时误删节点/撤销。

**判据（2026-09 产品要求）**：**凡"有未保存内容"的中间弹框，点空白处一律不得关闭/丢弃**——
- 有内容（要改）：`RichTextPopupUI`（富文本）、`ImageClipPopupUI`（裁切位置/缩放）、`AudioClipEditorPopupUI`（剪辑/音量）、
  `VFXEditorPopup`（帧编辑）、`MusicLibraryPanel` 的改名层、`VariableSelectPopup` 的改名/确认内层、`GraphEditorPanel` 的
  「新建自定义属性」——遮罩点击一律 **no-op**（可顺带 `SetHint("点「取消」或「确定」结束编辑…")`）。
- 无内容（纯选择）：`UISelectPopup`、多选/单选弹层、`VariableSelectPopup` 主层——可保留"点遮罩关闭"（不丢东西）。
- **内层保护**：`Update()` 里的 Esc、遮罩点击，只要有内层（确认框/输入框）打开，就**只提示不关闭**，避免丢掉正在输入的内容。
- 实现要点：遮罩 `raycastTarget = true` 依旧保留（挡穿透与滚动），只是**不再挂关闭回调**。

## 约定（2026-09）：**所有触发/入口节点结构统一**

> 新增任何"触发/入口"类节点（事件入口、按钮入口、增益触发入口、zmcs 效果入口）时，
> **必须同时包含这三项**，字段名与「使用卡牌时」等既有事件入口保持一致：

| 项 | 类型 | 字段名 | 说明 |
|---|---|---|---|
| 触发条件 | 输入引脚 `cond`（Boolean，可空） | — | 连线为假 → 本入口不触发（无连线=总是触发） |
| 标签列表 | 字段 | `tags` | 供图内判断/事件标签（选项与事件入口同一套） |
| 优先级 | 字段 | `priority` | 同一图多入口同时匹配时的执行顺序（降序） |
| （可选）自定义效果属性 | 字段 | `custom_props` | 每行 `名称:类型[:数组]`，与增益/卡牌自定义参数同规格 |

**已按此约定修正**：按钮入口 `ButtonClicked`（点击按钮时）/ `ButtonClickedAfter`（点击按钮后）——补上 `cond` 引脚
+ `tags` + `priority` + `custom_props`，与「使用卡牌时」完全一致；另加「按钮」参数（下拉选 / 连线，见坑 22）。

**单入口约束（2026-09 最新，覆盖此前"已取消"的记录）**：`CanAddEntryTriggerNode` = **1 个效果只允许 1 个触发**，
**全模式生效**（卡牌图 / 增益图 / 按钮图统一，不再有 `card == null` 放行）：
- 判定要同时看 **type + category**（Event 且 category ∈ 入口/事件/增益触发/按钮）；只看 category 会误伤
  `CAT_EVENT` / `CAT_BUTTON` 里的取值节点（`EFCardOwner`、「这个按钮」）；
- 拦截点：节点库点击加节点、Ctrl+V 粘贴、示例效果批量搭建（三条加节点路径全覆盖）；
- 节点库里触发项在被拦时**灰显并改名「…（本效果已有触发）」**，点击只在状态栏说明原因；
- 历史图超标：多余触发节点在 `ValidateGraph` 里标红「!」，**保存 / 模拟测试会被拦**（提示文案要写清
  "缺输入 / 1 个效果只允许 1 个触发"）；
- `ValidateSingleEntryTrigger()` 在四条打开路径（卡牌/关键词/增益/按钮）都要调；首次打印
  `[规则图] 入口约束 v1 已启用…` 便于确认生效。
- 需要多个触发 → 点左上角「+ 新效果」新建效果页（**每页 1 个触发**）。

**按钮栏节点**（战斗页面按钮列表，位置 **1 起算**）：
- `209204 增加按钮`：`button`（下拉选 或 `button` 口连线）+ `pos`（空 → 追加到最后），后面按钮依次顺延；
- `209205 删除按钮`：**按「按钮」删**（`button` 下拉/连线）；找不到该按钮 → 打警告并跳过（**不误删第一个**）。
  旧图的 `pos` 删法已废弃：`PruneLegacyRemoveButtonPos` 随打开图清掉 `pos` 字段/端口及其连线；
- `119006 这个按钮`：取值节点，返回**当前正在编辑/触发的按钮 id**（`NodeDocRunner.CurrentButtonId`，
  在 `RunButtonClick` 期间设置/恢复，支持嵌套）。

## 坑 20：节点库里"同一节点出现两遍"——**代码预设 + NodeDoc.xml 各注册了一份**

**现象**：按钮分类里 `增加按钮/删除按钮/这个按钮` 各出现两次（其它节点只出现一次）。

**根因**：节点库有**两个数据源**：
① 代码预设（`GraphEditorPanel.BuildXxxPresets()`，带引脚/字段定义）；
② `Resources/NodeDoc.xml`（zmcs 节点文档 → `NodePresetFromDoc` 自动生成预设）。
**同一 action 两边都写 → 库里就有两条**。

**约定**：**一个节点只在一个来源注册**。
- 运行时节点（`NodeDocRunner` 里有实现、需要自定义引脚/字段的）→ 只写**代码预设**（XML 不写）；
- 纯文档型 zmcs 节点 → 只写 XML。
（本次修复：删除 XML 里的 209204/209205/119006 三条，保留代码预设。）

## 坑 21：`CreateBuffFieldRow` / `CreateBuffSectionTitle` 是**写死挂到增益面板容器**的

`CreateBuffFieldRow(label, out text, h)` 与 `CreateBuffSectionTitle(text)` 内部走
`CreateBuffRow(..., parent: null, ...)` → 落到 `buff_mods_content`。
**在按钮/卡牌表单里用它们 → 行会被建到增益面板里**（表现：该行"没显示"，去增益页反而多出来一行）。

**修法**：新增/使用**可指定父容器**的版本：`CreateFormFieldRow(content, label, out text, h)`、
`CreateFormSectionTitle(content, title)`；行容器统一 `CreateBuffRow(name, content, h)`。

---

## 坑 22：「下拉选择 + 连线取值」二合一参数**必须让 字段名 == 端口名**

**现象**：同一个参数在节点上出现**两行**（如「按钮」出现两次：一行是端口 `按钮 =`，一行是字段框），
用户的原话就是"现在同时存在 2 个按钮"。

**根因**：画布上的内联字段控件**只有在"同名字段 + 同名输入端口"时**才会被画进端口那一行
（`CreateNodeInlineFields` → `FindInputPin(node, fd.name)` → `x0 = PinFieldX(...)`、`with_label=false`）。
字段叫 `button_id`、端口叫 `button` → 控件只能另起一行 → 两行。

**正确姿势**（照抄「伤害」那种"圆点 + 值框"）：
- 字段与端口**同名**（都叫 `button` / `damage`），字段类型用对应下拉类型（`ButtonSelect` 等）；
- 效果：**点圆点 = 连线**（有连线时 `RefreshPinFieldVisibility` 自动隐藏该行内控件，值由连线提供）；
  **点框 = 从库里选**（如按钮库 `BattleButtonIO.GetAll()`）；
- 旧数据改名必须**在 `MigratePins` 之前**执行（`MigrateButtonNodeFields` → `RenameNodeField`），
  否则 `MigratePins` 先把新字段默认值补上 → 旧值被默认值盖掉；
- 运行时取值一律"**新名优先 + 旧名兜底**"（`ResolveNodeButtonId` 先读 `button`，再读旧 `button_id`），
  这样没在编辑器里打开过的旧图也照常跑。

## 坑 23：新增 `FieldEditType` 必须同时补 `CreateInlineFieldRow` 的渲染分支

`CreateInlineFieldRow`（画布内联控件）是一长串 `else if (fd.edit == ...)`：**漏加分支不报错**，
只打一条 `[规则图] 字段编辑类型未接渲染分支：xxx`，然后**静默退化成普通输入框**
（表现：本该弹选择器的地方变成一个可以随便打字的框）。`FieldEditType.ButtonSelect` 就漏了很久，
直到做"按钮参数二合一"才发现 —— 顺手补了 `CreateInlineButtonSelect`。
同时要记得补 `InlineFieldText(fd, value)` 的显示分支，否则节点宽度按原始值估算 → 控件被挤到只剩省略号。

## 坑 24：C# 字符串里别嵌半角双引号 → CS1002（一次真实的低级错误）

```csharp
//错：字符串在 "位置" 处被提前闭合
del_btn.desc = "…（旧图的"位置"删法仍兼容）";   // error CS1002: ; expected
```

文案里的引号统一用 `「」`（与项目其它文案一致），不要用半角 `""` 嵌套。
**教训**：这类错 `read_lints` 可能报 0 条（见坑 9），必须让 Unity 真编译一次才算过关。

---

## 坑 25：对战 HUD 的「按钮栏」（`GameUI.EnsureBattleBar`）——收起态与位置

**现象**：开局（收起态）时，一条**半透明黑底 + 空的一排方块区**横在屏幕左侧中部，**盖住对面玩家的卡牌和卡面数值**；
展开时整条也会压住对面那一排。

**根因**：`bar_root`（整条，820×84，`Image (0,0,0,0.35)`）只隐藏了 `bar_content`，
背景 Image 与整行尺寸**没收起** → 一条看不见内容的黑条继续占位（视觉挡卡 + 挡射线）。位置写死在
`bar_offset = (24, 40)`（竖向 0.5 锚点 + 40 = 正好落在对面卡牌行下沿）。

**修法（三条一起做，缺一条都会看起来"没修"）**：
1. **收起时把整条背景关掉**：`bar_root.GetComponent<Image>().enabled = bar_expanded;`
   （`raycastTarget=false` 已有，但"不画"才是用户要的"去掉透明背景"）。
2. **收起时整行缩到方块大小**：`sizeDelta = expanded ? (820,84) : (84,70)`，否则透明大矩形仍占着卡牌区域。
3. **位置做成"上下两排卡牌之间的空档"**：1920×1080 参考下两排卡牌之间约 90px 高，`bar_offset.y ≈ -34`
   （84 高的条刚好塞进去：上不压对面卡、下不压自己卡）；`ApplyBarExpanded()` 里统一
   `bar_root.anchoredPosition = bar_offset;` —— 避免场景/预制体里残留的旧坐标覆盖新默认值
   （运行时会自建该栏，字段留空时以代码默认为准）。
4. 结构日志里把 `bar_offset` 打出来，方便"位置对不对"变成可核对的数据而不是肉眼判断。
5. **位置用"安全线"兜住**：`y = Mathf.Min(bar_offset.y, -(bar_h*0.5f + 4f))` —— 中线两侧就是两排卡牌，
   整条必须压到中线以下；这样即使 **Inspector/场景里残留旧值**（`public Vector2` 是序列化字段，
   改代码默认值不一定生效！）也不会再遮住对面卡牌；高度从 84 降到 **64**（= 方块 60 + 内边距）才塞得进两排之间的空档。

## 坑 26：点了按钮"没反应"——**动作执行了但没结算/没同步**

**现象**：点击战斗界面自定义按钮，当场什么都不发生；等下一次操作（结束回合/出牌）才一起生效。

**根因**：`GameLogic.PressBattleButton` 里只跑了按钮图（`NodeDocRunner.RunButtonClick`），
**没有 `UpdateOngoing()` / `resolve_queue.ResolveAll()` / `RefreshData()`**。
本项目 `DamageCard` 只往 `card.damage` 累加，真正落到 hp 是 `UpdateOngoing()` 阶段；
不刷新则 `onRefresh`（→ `GameServer.RefreshAll`）不触发 → 客户端看不到任何变化。
对照：`PlayCard` / `CastAbility` / `Attack` 结尾都写了 `resolve_queue.ResolveAll()`（有的还带 `RefreshData()`）。

**规则（新增任何"玩家主动触发"入口时照抄）**：动作执行完必须
`UpdateOngoing(); resolve_queue.ResolveAll(); RefreshData();` 三连，才算"点了立刻生效"，
并打一条日志（如 `[按钮栏] 执行按钮图：xxx → 已立即结算并同步`）便于核对。

## 坑 27：悬浮提示"什么都不弹"——空文案提前 return + 高度写死

**现象**：鼠标移到按钮上没有任何提示。

**根因**：`ShowBarTip(target, text)` 里 `if (string.IsNullOrEmpty(text)) return;` ——
按钮的 `desc` 没填就直接不弹（用户以为功能坏了）；另外高度固定 40，多行描述会被裁掉。

**修法**：`ShowBarTip(target, title, desc)`：**有描述用描述，没描述退回显示按钮名**；高度按行数算
（`20 + 行数*22`），宽度 `Clamp(160+字数*13, 220, 480)`；提示挂 `raycastTarget=false`
并 `SetAsLastSibling()`，避免挡住/吃掉按钮点击。
**位置**：不要直接写 `tip_root.position = target.position + ...`（不同 Canvas 模式/父级下有偏差，
容易画到屏幕外 → 看起来"没弹"），改用
`WorldToScreenPoint(target.TransformPoint(锚点))` + `ScreenPointToLocalPointInRectangle` 换算成父级局部坐标；
父级取 **rootCanvas**（不是按钮栏父级，避免被裁剪/压层级）。命中时打一条日志
（`[按钮栏] 悬浮说明：xxx`），一眼区分"事件没触发"与"触发了没画出来"。

## 坑 28：`Update()` 里一个 NRE 会掐断"该帧后面所有逻辑"（连 UI 都不刷新）

**真实报错**
```
NullReferenceException
TcgEngine.Client.GameClient.GetPlayer () (at GameClient.cs:699)
TcgEngine.UI.GameUI.Update () (at GameUI.cs:422)
```
**根因**：`GetPlayer()` 直接 `GetGameData().GetPlayer(id)` —— 进对战**第一帧还没收到 RefreshAll** 时
`game_data == null` → NRE；而它是在 `GameUI.Update()` 里调的，异常后 **Update 里后续代码全部不执行**
（按钮栏刷新、各种面板状态都停摆），表现往往不是"报错"而是"某个功能就是不生效"，很容易误判成渲染问题。

**规则**：
1. `GameClient.GetPlayer() / GetOpponentPlayer()` 这类"取当前上下文对象"的 API 必须**自己返回 null 而不是抛异常**
   （`if (gdata == null || gdata.players == null) return null;`）；
2. `Update()` 开头先 `GameClient client = GameClient.Get(); if (client == null) return;`，并复用一个局部变量，
   别每行都 `GameClient.Get().Xxx()`；
3. 排查"某 UI 功能不生效"时**先看 Console 有没有异常**——一个异常就能让后面的 UI 代码整帧不跑。

## 坑 29：客户端永远停在「Connecting to server…」——服务端**开局建卡抛异常**被打断

**真实栈**
```
NullReferenceException
TcgEngine.Card.SetTraits (CardData icard) (at Card.cs:94)          // SetTrait(trait.id, 0) —— trait 为 null
TcgEngine.Card.SetCard (CardData, VariantData) (Card.cs:85)
TcgEngine.Card.Create (...)
TcgEngine.Gameplay.GameLogic.SetPlayerDeck (player, deck) (GameLogic.cs:1260)
TcgEngine.Server.GameServer.SetPlayerDeck (...) (GameServer.cs:522)
System.Runtime.CompilerServices.AsyncMethodBuilderCore+<>c.<ThrowAsync>...
```
**现象**：点"测试/开始对战"后画面一直停在「Connecting to server…」（背景能看到开局替换面板），Console 只有一条 NRE。

**根因**：`GameServer.SetPlayerDeck` 是 `async void`，里面 `SetPlayerDeck → Card.Create → SetCard → SetTraits`
逐张建卡。`SetTraits` 里 `foreach (TraitData trait in icard.traits) SetTrait(trait.id, 0)`——
**CardData 资源里 traits 数组可能留有"空槽"**（Missing 引用，通常是某张卡引用了已被删除的 TraitData），
`trait` 为 null → 异常抛出 → **异步任务中止**，`SendPlayerReady` 没执行 → 双方都永远连不上"开局"。

**修法（三层一起做）**：
1. **数据层逐项防空**：`SetTraits` 遍历时 `if (trait == null) { 警告并点名 (卡id, 下标); continue; }`；
   `stats`、`SetAbilities` 的数组空槽同样跳过（`SetKeywords` 本来就有判空，照它统一）；
2. **建卡入口防 null**：`Card.SetCard(icard==null)` / `Card.Create(icard==null)` → 报错并返回（不再 NRE），
   `GameLogic.SetPlayerDeck` 对返回 null 的那张**跳过并报错**（卡组少一张但整局能开）；
3. **服务端包一层 `SafeSetPlayerDeck`**（try/catch + `Debug.LogError`）：任何建卡异常都变成一条**看得见的报错**，
   而不是"客户端干等"——这条最值钱，它把"静默卡死"变成可排查。

**排查口诀**：凡是"客户端卡在 Connecting/等待中"且 Console 只有一条异常栈 → 一定是**服务端流程被异常打断**，
顺着栈把那个方法加防御 + 显式报错，不要怀疑网络。

## 坑 30：**全屏 UIPanel 常显 ⇒ 所有按钮/输入框都点不动**（含"改条件时写丢 `!`"）

**现象**：进对战（或某页面）后，画面被压暗、除该面板自己的按钮（如 ConnectionPanel 的 QUIT）外**全部控件无响应**，
Console 无报错。

**根因**：`UIPanel` 是带 `CanvasGroup` 的全屏面板，`Show()` 会 `blocksRaycasts = true`。
只要它在正常游戏时**意外保持可见**，就是一张吃全部射线的全屏遮罩。
本项目真实踩过两次：
1. 重构 `GameUI.Update` 时把
   `bool connection_lost = !is_connecting && !client.IsReady();` 写成了 `... && client.IsReady();`
   —— **漏了一个 `!`**，于是"已就绪"的正常对局反而显示断线面板，全屏挡死（只剩它自己的 QUIT）。
2. `LoadPanel.Get().SetVisible(...)` 原本写在 `if (!client.IsReady()) return;` **之后** ——
   客户端一旦未就绪，`Start()` 里 `Show(true)` 过的加载遮罩就永远停在屏幕上。

**规则**：
- 改任何 `bool xxx = A && !B;` 这类条件时，**改完立刻回读那一行**核对符号（尤其 `!`）；
- 遮罩/面板的可见性更新要放在**提前 return 之前**，并用"数据是否有效"（如 `data != null && data.HasStarted()`）
  而不是"我是否就绪"来判断，避免"未就绪 ⇒ 永远不刷新 ⇒ 遮罩永驻"；
- 排查"全都点不动"时，第一件事是**找全屏 `UIPanel`/`CanvasGroup` 谁还 visible**（坑 31 的射线采样同样适用：
  `RaycastAll` 最上层那个往往就是常显的面板）。

## 坑 31：悬浮/点击"收不到"时先做一次**射线采样自检**

在按钮栏/自建控件构建完（**等一帧**：首帧矩形可能还是 0）后，对每个控件中心做一次
`EventSystem.current.RaycastAll` 采样，把 `hits[0]` 的层级路径打出来：
- 命中的不是自己 → 有人挡在上面；若它**自身与父链都没有 `Selectable / TMP_InputField / ScrollRect / EventTrigger`**
  → 判为隐形遮挡物，直接把它的 `Graphic.raycastTarget = false`（不影响真交互），并打日志说明改了什么；
- 一次 `hits.Count == 0` → 该控件在遮罩外/被隐藏，属于布局问题而不是遮挡问题。

好处：把"悬浮没反应"从"我猜"变成 Console 里一条明确结论（谁挡住了），而不是反复试错。

**写法（临时探针，用完即删）**：新加一个静态类，两个入口就够——
① **指针采样**（F9 之类）：`EventSystem.current.RaycastAll(ped, hits)`，打印 `hits[0]` 起每条的
**完整层级路径 + 关键组件 + 锚点/pivot/anchoredPosition/sizeDelta/世界矩形**（#0=最上层，要删的一般就是它或它父级）；
② **区域清点**（F10 之类）：遍历根画布的 `GetComponentsInChildren<RectTransform>(false)`，
用 `GetWorldCorners` + `WorldToScreenPoint` 求**屏幕矩形**，把与目标区域相交的对象按路径排序打出来
（不依赖鼠标精度，专门回答"这堆东西都是谁"；本项目 2026-09 排查主菜单图标遮挡就是靠它一步定位）。
**注意**：这类探针是**排查用临时件**——定位完就删掉（含 `.meta`），别留在正式代码里。
**更别用手改 `.unity` 场景 YAML 去删对象**（fileID/父子引用/meta 极易改坏）：
要么在编辑器里删，要么由代码在运行时 `SetActive(false)` 收敛。

---

# 附录：本项目"卡牌/增益编辑器"开发复盘与问题统计（2026-09）

## A. 功能清单（本轮开发产出）

| # | 功能 | 落点 |
|---|---|---|
| 1 | 变量配置四入口统一走「选择弹框」+ 旧增益管理页弃用重定向 | `VariableSelectPopup` / `CardEditorPanel` / `BuffPanel` |
| 2 | 增益「属性修改」：六种写法并存（增加/减少/设置为/增加属性/减少属性/设置为属性） | `BuffData.BuffModMode` + `BuffRuntime` |
| 3 | 增益「自定义属性」= 名称:类型[:数组] + 初始值（类型化取值控件） | `BuffData` + `GraphEditorPanel` |
| 4 | 卡牌「自定义属性」（与增益同规格，复用同一弹框/控件） | `CardCustomData` + `GraphEditorPanel` |
| 5 | 卡图：面板图片自动跟随卡图并匹配比例；面板编辑只允许缩放/重置；无图不可编辑 | `GraphEditorPanel` + `ImageClipPopupUI/EditorUI` |
| 6 | 音乐配置：音效行四段布局自愈（不叠字）+ 音效DIY 去重 | `GraphEditorPanel.EnsureAudioDiyButtons/LayoutAudioRow` |
| 7 | 本页黑白灰（工具栏/Tab/删除按钮/音效DIY）+ 「可组卡」方框改文字标记 | `GraphEditorPanel` |
| 8 | 「按钮」参数二合一：字段名==端口名（圆点连线 / 不连线时按钮库下拉），旧 `button_id` 幂等改名 | `GraphEditorPanel` + `NodeDocRunner` |
| 9 | 「删除按钮」改为按「按钮」删（下拉/连线），旧 `pos` 字段/端口与连线随打开图清理 | `GraphEditorPanel` + `NodeDocRunner` |
| 10 | 单入口约束恢复为强制：全模式 1 效果 1 触发（拦截 + 灰显 + 标红 + 保存/测试阻断） | `GraphEditorPanel` |
| 11 | 增益图多页触发：`BuffRuntime.RunGraph` 逐页执行 `buff.graphs`（否则第 2 页触发永不触发） | `BuffRuntime` |
| 12 | 对战按钮栏：位置用"安全线"兜住（不遮对面卡牌）+ 收起时关背景/缩尺寸 + 高度 84→64 | `UI/GameUI.cs` |
| 13 | 对战按钮栏：点击立即结算+同步（`UpdateOngoing + ResolveAll + RefreshData`） | `GameLogic.PressBattleButton` |
| 14 | 对战按钮栏：悬浮提示空描述退回按钮名 + 高度按行数算 | `UI/GameUI.cs` |
| 15 | 开局卡死修复：`Card.SetTraits/SetAbilities` 逐项防空 + `SafeSetPlayerDeck`（服务端建卡异常显式报错、不再静默卡 Connecting） | `GameLogic/Card.cs`、`GameLogic/GameLogic.cs`、`GameServer/GameServer.cs` |
| 16 | 全屏面板挡死修复：`ConnectionPanel` 条件漏 `!` 导致正常对局也常显；`LoadPanel` 可见性挪到提前 return 之前；卡 Connecting 8 秒打诊断 | `UI/GameUI.cs` |

## B. 问题统计（按"根因类型"归类，含前序会话）

| 类型 | 次数 | 典型现象 | 一句话修法 |
|---|---|---|---|
| 编译期类型错（CS1061/CS0029/CS0266/CS0246） | 5 | `KeywordData.GetTitle` 不存在、`Text→TMP_Text` 漏改、`TextAnchor→TextAlignmentOptions`、`VFXConfig` 命名空间 | 改类型/改 API 后**全仓搜旧类型**，并**让 Unity 真编译**（IDE 静态检查可能 0 报错） |
| 控件重叠（运行时自建 vs 场景遗留 / 父级不同） | 4 | 两个 ×、四个入口压 ×、音效行"选择音频"被"音效DIY"压住 | 运行时收敛：归位到**同一父级**后按锚点排；同类只留一个并隐藏其余 |
| 重复对象（销毁不彻底 / 幂等判断看错名字） | 3 | 两个「自定义属性」区块、两个「音效DIY」 | 倒序清同名 + `SetActive(false)` 立即生效；幂等判断要按"名字或文字"双条件 |
| 视觉/资源问题（占位色、丢 sprite、缺字形） | 3 | 卡图蒙灰、可组卡白方块、符号方块 | 刷新时还原 `color`；无 sprite 用"透明射线层 + 文字"替代；不用生僻符号 |
| 数据兼容/迁移 | 2 | 旧 `props` 反复复活、旧 `custom_props` 只有名字 | 旧字段并存 + 幂等迁移 + 迁移标记（`*_migrated`） |
| 交互语义（误触、穿透、快捷键） | 2 | 点遮罩误关弹框、填表时快捷键删节点 | 遮罩不响应关闭但挡射线；弹框打开时禁用画布快捷键 |
| 布局参数（LayoutElement 被忽略） | 2 | 一页只显示 3 条、自建区块高度塌陷 | `childControlHeight = true` + 同时写 `sizeDelta` 双保险 |
| 参数语义（同一参数两行 / 约束反复） | 3 | 「按钮」出现两行、单入口约束"已取消"又被要求恢复、`FieldEditType` 漏渲染分支 | 字段名==端口名（坑 22）；约束按最新需求并在打开图时校验（坑 23）；新类型必须补渲染分支 |
| 低级笔误（字符串引号） | 1 | `desc = "…"位置"…"` → CS1002 | 文案引号用 `「」`；lint 干净也必须让 Unity 编译（坑 24） |

## C. 交付前自查（本项目定制，配合上面"验收清单"）

1. **编译**：Unity Console 无 CS 报错（lint 干净 ≠ 能编译）。装了 MCP 后的口径：`compile_scripts` → 等域重载 → **拉全量 Console 日志自己筛 `error CS`**（`get_script_errors` 会漏报，见附录 D-1）。
2. **结构日志**：与控件布局有关的功能，收尾打一行结构日志（名称/父级/锚点/激活/文字），核对"1 个 input、1 个 pick、1 个 diy、1 个 play"。
3. **幂等**：连续打开同一页面 3 次（或在同一帧触发两次刷新），区块数量不增加。
4. **空态**：无数据（无图 / 无自定义属性 / 无增益）时，不该可点、不该报错、不该出现空壳。
5. **重开**：改完保存 → 关闭页面 → 重开 → 数据与 UI 一致（证明落盘 + 刷新链路都对）。



0. **字体与行高**：弹框内文字与页面其余文字字体一致（TMP + UIFonts）；列表一页可见 ~12 条（行高 30）。
0.1 **能编译**：Unity Console 无 CS1061/CS0029 等类型错误（不要只看 IDE 静态检查）。
1. 配置列四个入口与右上角 × **不重叠**；× 可点，能收起/展开整列；面板里只剩一个可用的 ×。
2. 四类弹框都能打开；行选中是"蓝底 + √ "样式，与既有多选弹层一致；卡牌已配置项默认预选中。
3. 新增 → 留在弹框且新项出现并被选中；编辑 → 进入对应编辑器且改动生效；删除 → 有二次确认。
4. 无 `Missing script`/`NullReferenceException`；重新编译无 CS1061 类报错。

---

# 附录 D：MCP 直连 Unity 编辑器的实测坑与验收口径（2026-09）

已注册服务器 **`ch-unity-mcp`**（`url = http://localhost:9123/mcp`，76 个工具；配置写在 `~/.codebuddy/mcp.json` 的 `mcpServers` 里，改完**下一轮对话**会被 IDE 重新读取，日志里出现 `[MCP:TokenRefresh] Rescan completed: tracking 1 server(s), newly registered: [ch-unity-mcp]`、`[MCP:HealthPatrol] Healthy: 1 [ch-unity-mcp]` 即注册成功）。

常用工具：`get_editor_status` / `compile_scripts` / `get_script_errors` / `get_unity_logs` / `get_play_mode_status` / `play_mode_start` / `play_mode_stop` / `simulate_input` / `find_gameobject` / `get_gameobject_info` / `get_component_properties` / `list_scenes` / `list_prefabs`。

## D-1 坑：`get_script_errors` 会**漏报**真实编译错误（最坑，会把 Play 卡死）

实测：探针脚本里写了 `cfg.buttons.Count`（应是 `.Length`）→ Unity Console 明确有
`error CS1061: 'BattleButtonData[]' does not contain a definition for 'Count'`，
而 `get_script_errors` 返回 **`Errors: 0, Warnings: 0`**。

**后果**：有编译错误时 Unity **拒绝进入 Play 模式** —— 点 ▶ 没反应/报错，`play_mode_start` 也永远只回"已加入异步执行队列"。
**规则**：判定"能编译"必须**两条都查**：`compile_scripts` + 拉**全量** `get_unity_logs` 自己筛 `error CS`；
一旦"进不去 Play"或 `play_mode_start` 反复排队，**第一件事是查 CS 错误，别怀疑 MCP 坏了**。

## D-2 坑：`get_unity_logs` 的 `logLevel` / `searchText` 过滤**不可靠**

实测：`searchText:"error CS"` 搜不到那条真实存在的 CS1061；`logLevel":"error"` 返回 0 条；
`get_unity_log_stats` 永远返回 `info:0/warning:0/error:0`；而且 LogError 条目在返回里 `level` 也被标成 `"Info"`。
**规则**：只信**全量**拉取（`maxCount` 给大、`searchText:""`）+ 自己按 `message` 文本筛。

## D-3 坑：Play 相关调用会"排队 + 断连"，重试即可

- `play_mode_start` / `play_mode_stop` 常直接回 **"操作「…」已加入异步执行队列，请10秒后重试"** —— 这是**正常**的排队响应，
  等 10~20 秒再 `get_play_mode_status` 看结果；**不要连续猛调**（每次调用可能重置它的排队窗口）。
- 脚本编译会触发**域重载**，期间 MCP 连接会断：报 `fetch failed` / `Streamable HTTP error: Unexpected content type: null` / `无法连接到远程服务器`。
  实测域重载后 **20~35 秒**才恢复；可用 `curl` 探活（`POST http://localhost:9123/mcp`，`Host` 必须是 `localhost:9123`，写 `127.0.0.1` 会被 `400 Invalid host` 拒），恢复后再继续调用。

## D-4 坑：场景查询工具**只能看根对象**

`find_gameobject("Canvas")` → 0 条（根其实叫 `UICanvas`）；`get_gameobject_info("UICanvas/TopBar")` → `GameObject not found`（**不支持路径**）。
`get_gameobject_info(根名)` 只能列出**一级**子对象名。所以：
- 想数节点上的 `InlineField_*` 这类**深层**控件 → 这些工具**做不到**，必须用 D-5 的探针；
- 想快速知道"哪个画布挂了哪些面板" → `get_current_scene_info`（列根 + 子对象数量）够用，例如 `UICanvasTop` 的子对象里能看到 `GraphEditorPanel`。
- `raycast` 是**物理**射线，打不到 UGUI（验证 UI 遮挡仍用 `EventSystem.RaycastAll` 探针，见坑 31）。

## D-5 探针 SOP：**不用眼睛**验证运行时 UI（这一步很值）

目标：把"节点上到底有几个「按钮」行 / 区块重复没重复"变成 Console 里的**数据**。步骤：

1. 写临时脚本（放 `Assets/TcgEngine/Scripts/__TempProbe/`，名字带 `__TempProbe` 便于收尾删除）：
   - 入口用 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`（每次进 Play 自动跑，**不需要人点任何 UI**）；
   - 找面板：`Resources.FindObjectsOfTypeAll<GraphEditorPanel>()`（**能拿到未激活对象**；`GameObject.Find` 拿不到 inactive）；
   - 自动打开目标页：直接调 `panel.OpenButtons(BattleButtonIO.GetConfig())` 之类的公开入口，**不用调 `Show()`**
     （`RebuildCanvas` 不依赖可见性；而且 `GraphEditorPanel.Show()` 并非 public，探针里调会 CS0122）；
   - 清点：`panel.GetComponentsInChildren<Transform>(true)` 按名字前缀统计（如 `InlineField_`），**按签名去重后** `Debug.Log`（内容变化才打，避免刷屏）。
2. `compile_scripts` → 等域重载 → `play_mode_start` → 拉全量日志筛 `[探针]` 前缀读结论。
3. **用完立刻删**：删掉整个 `__TempProbe` 目录（**含 `InlineFieldProbe.cs.meta`**）→ 再 `compile_scripts`，让工程回到原样。

**写探针时踩过的两个编译坑（都会挡住 Play）**：
- `BattleButtonConfig.buttons` 是**数组** → 用 `.Length`，写 `.Count` 就是 CS1061；
- 探针要类型化调用时确认成员可见性（`OpenButtons` public ✓、`Show()` 不是 public ✗）。

**实测收益**（同一套 SOP 跑出来的）：
- 按钮「二合一」验证 → 全图**没有任何** `InlineField_button`/`InlineField_button_id`，且那个已接线的节点只剩端口一行 = 需求达成（不靠肉眼）；
- 启动日志治理前后对比 → **37 条 → 14 条**（17 条 null trait 汇总成 1 条、3 条卡池误报消失、TeamData 刷屏消失）。

## D-6 顺带沉淀：启动期日志噪音的固定套路

- **别在 `Update()` / `IsInside()` / 数据查询方法里留 `Debug.Log`**：本次一次抓到 4 处（`BoardSlotPlayer.Update` 每帧 3 条、`BSlot.IsInside` 未命中打、`CardData.HasAbility` 查询打、`TeamData.GetAll` 每次调用打），全是排查残留。
- 逐项 `LogError` 的校验（如 `DataLoader.CheckCardData`）要**先收集再汇总成 1 条**（本次 17 条 → 1 条，且保留明细），否则卡池一涨就刷屏。
- 目录里"扫全部 `*.json`"的加载器必须**先判别文件类型**（本次 `CardPoolIO.LoadCustomPools` 把 `buffs.json`/`buttons.json`/`bgm_library.json` 都当卡池，误报「MyCardPool 新增 0 张卡」；用 `json.IndexOf("\"cards\"")` 一眼区分）。

## D-7 坑：用命令行直连 MCP 时，PowerShell 会把 UTF-8 响应解成乱码

当 IDE 侧工具没注册（报 `tool does not exist or is not registered`）但服务端还在（`HTTP 200`）时，可以**直接用 HTTP 调 MCP**（等价于原生工具，本轮就是这么把 P0 验证跑完的）：

```
POST http://localhost:9123/mcp
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_unity_logs","arguments":{"maxCount":60,"logLevel":"all","includeStackTrace":false,"searchText":""}}}
```

**坑**：`Invoke-WebRequest` 默认按 ANSI(GBK) 解码 UTF-8 响应 → 返回的中文全是乱码，**按中文关键字检索（如 `[探针]`、`error CS` 之外的任何中文）会永远匹配不到**，看起来"探针没跑"。
**正确做法**：`-OutFile` 落盘**原始字节**，再 `[System.IO.File]::ReadAllText($f,[System.Text.Encoding]::UTF8)` 后检索。

**同类前科（同一个编码坑，换了个马甲）**：用 `Get-Content -Raw` 读 `buttons.json` 这类 UTF-8 JSON → GBK 双字节会把 `"` 吃掉 → `ConvertFrom-Json` 报"传入的对象无效"；
**凡读非 ASCII 文件/响应，一律显式指定 UTF-8**。

## D-8 坑：驱动游戏流程 / 读编辑器日志 / 工具注册失效（2026-09 实测补充）

**① 要驱动游戏流程（比如自动进一局对战）——直接调游戏自己的 API，别去模拟鼠标点菜单。**
探针在 Play 后 3 秒干这些事就能自动开一局人机（等价于菜单「单人 → 开始」），**全程无需人点任何 UI**：

```csharp
GameplayData g = GameplayData.Get();
GameClient.game_settings.game_type = GameType.Solo;        // Solo/Adventure = IsOffline()，本地对局
GameClient.game_settings.game_mode = GameMode.Casual;
GameClient.game_settings.scene = (g.arena_list != null && g.arena_list.Length > 0) ? g.arena_list[0] : "Game";
GameClient.game_settings.test_full_mana = true;            // 测试用：开局法力直接上限，更容易出牌
GameClient.player_settings.deck = new UserDeckData(g.test_deck != null ? g.test_deck : g.free_decks[0]);
GameClient.ai_settings.deck     = new UserDeckData(g.test_deck_ai != null ? g.test_deck_ai : g.ai_decks[0]);
GameClient.ai_settings.ai_level = g.ai_level;
MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual);  // → FadeToScene(game_settings.GetScene())
```

`GameplayData.test_deck / test_deck_ai` 就是「从 Unity 场景直接开局」的官方测试卡组；`LevelUI.OnClick()` 是另一条同构路径（`GameType.Adventure` + `level.scene`）。
实测：Play 后 3 秒调用 → 场景自动切到 `Game.unity` → `GameClient.Get().IsReady()==True` → 正常对局 80 秒无异常。
（比 `simulate_input` 盲点坐标可靠得多——UGUI 的屏幕坐标换算受 Canvas 模式/缩放影响，很容易点空。）

**② 读 `Editor.log` 必须**共享读**，`ReadAllText` 会抛 IOException。**
Unity 进程是**独占**打开 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 的：
`[IO.File]::ReadAllText($log)` → `IOException：文件正由另一进程使用，因此该进程无法访问此文件`。正确姿势：

```powershell
$fs=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
$t =(New-Object IO.StreamReader($fs,[Text.Encoding]::UTF8)).ReadToEnd(); $fs.Close()
```

这条路在 MCP 日志过滤不可靠时最有用（可直接统计 `NullReferenceException` / `error CS` 出现次数）。
实测本轮 80 秒自动对局：`NullReferenceException=0；error CS=0；IndexOutOfRangeException=0`。

**③ IDE 侧工具注册会失效，但服务端还活着 → 用 HTTP 直连顶上。**
现象：`mcp_call_tool` 报 `tool does not exist or is not registered`，而 `curl` 打 `9123` 仍是 `HTTP 200`。
处理：改走 `POST http://localhost:9123/mcp`（`{"method":"tools/call",...}`）完成全部操作（本轮就是这么跑完 P0/P1 验证的）；
**要让原生工具回来必须重载 IDE 窗口**（重载后工具才重新注册）。

**④ 性能验证的落地套路（"用数据说话"）**：在热点函数里留两个 `public static int` 计数器（调用数 / 真正执行数），
再放个探针每 2 秒报差值 —— 无需 Profiler、无需肉眼。本轮 P1 实测：**每 2 秒 ~1205 次调用（≈600 次/秒 ≈10 次/帧），
实际重建仅 15 次/80 秒 → 跳过率 99.97%**。

## D-9 坑：全站"页面能进能出"审计的实测结论（2026-09）

1. **`get_unity_logs` 不返回 `LogWarning` 条目**（实测：探针用 `Debug.LogWarning` 打的 8 条全部收不到，`Debug.Log` 的都收得到）。
   → **诊断/探针日志一律用 `Debug.Log`**，否则数据静默丢失（接 D-2）。
2. **读 UnityEvent 的"运行时监听"必须读 `InvokableCallList.m_RuntimeCalls`**：`UnityEventBase.m_Calls` 是 `InvokableCallList`
   对象，直接读它（或读它的 `m_Calls` 字段内容）**永远是空的** → 会把 `AddListener` 注册的按钮误判成"死按钮"（我踩过，导致 4 个 `HomeReturnBtn` 假报死按钮）。
   正确路径：`GetField("m_Calls")` → 结果对象的 `m_RuntimeCalls`（`IList`）→ 每项 `InvokableCall` 的私有字段 `Delegate`。
3. **`GetComponentInParent<T>()` 默认跳过未激活对象** → 判断"按钮属于哪个面板"要**手写向上遍历**；
   否则面板未激活时会把已有出口按钮判成"不存在" → 重复补建 → 页面上两个 ×（坑 12 的成因之一）。
4. **页面的 `UIPanel` 组件可能处于 disabled**（本项目确实有，见 `MainMenu.ForceHideModalPanels` 的注释）→
   在 `Show()` 里用 `StartCoroutine` 做延时自愈**不会执行**；自愈要**同步**做。
5. **`TabButton.ui_panel == null` 会黑屏**：`Activate()` 原本"先 `SetAll(group,false)` 再 Show 自己的页"，
   ui_panel 为空就变成"隐藏整组、什么都不显示"（场景实测 4 个，如 `TabFriend`）。
   修法：`ui_panel == null` 时**保持现状 + 报错**，别先隐藏整组。
6. **"关闭按钮绑错对象"是真实存在的**：实测 `PackZoomPanel.CloseArea` 的 Inspector 监听目标为 **null（对象已丢）**、`FilterCloseBtn` 无任何监听
   → 点击毫无反应（正是用户说的"点了没反应"）。通用自愈 `UIPanel.RepairExitButtons()`：对"按名字像出口"的按钮，
   若 Inspector 目标不是本页且无运行时监听 → 补 `Hide()`（跳过名字含 `del` 的行内 ✕；挂了 `HomeReturnButton` 的跳过——它的监听在 `Start()` 里注册）。
7. **判定"能不能退出"不能只看代码**：本工程大量页面的关闭按钮是**场景 Inspector 直接绑** `XxxPanel.Hide`（代码里搜不到）
   → 纯静态审计会误报"没有出口"（本轮静态审计给出 3 个假阳性：SoloPanel / AdventurePanel / StarterDeck 等其实都有 Close）。
8. **全站验收脚本（探针 SOP 的扩展）**：对每个页面 `Show(true)` → 找出口按钮（**按名字优先，再按文字**，避免节点行上的 ✕ 冒充页面出口）
   → **真的点一下** → 断言 `IsVisible()==false`；点之前用白名单过滤（监听含 `Quit/Logout/Scene/GoTo/StartGame` 的不点）。
   本轮结果：24 个页面 → 出口可用 19、死按钮 0、无出口 1（`MatchmakingPanel` 实为漏判：它唯一的按钮名为 `Quit`，已绑 `OnClickCancel`）。

## D-10 坑：NodeDoc 节点实现 / MCP 验证的 11 条硬经验（2026-09）

1. **`Compilation Status: Errors: 0` 会说谎，而"Play 永远起不来"的根因往往就是编译错误。**
   实测：`AbilityTrigger` 里写了不存在的 `OnAfterPlay`（CS0117）+ `GraphData` 写成复数 `GetIncomingLinks`（CS1061）→ 程序集加载失败 → `play_mode_start` 只回"已加入异步执行队列"、`get_play_mode_status` 恒为 Stopped、`autobattle` 永远"等不到报告"。
   **排查顺序：先 grep 源文件确认可疑符号是否真的存在，再看日志。**
2. **`clear_unity_logs` 清不掉历史编译错误** → 日志交叉验证会把**上一轮**的错误当成新的报出来（"还是失败"要先 grep 源文件复核）；
   并且 **`get_unity_logs` 偶发整段为空**（返回约 100 字符）→ **探针结论必须写盘**才可靠。
3. **探针结果写盘要写进项目内**（`Application.dataPath + "/../tools/xxx.txt"`）：写 `persistentDataPath`（`C:\Users\...\LocalLow\...`）会触发**授权弹窗**，用户不在时会超时失败。
4. **"当前事件/当前效果"上下文只在 `NodeDocRunner.Run` 期间存在**（`cur_event` / `ctx_ability`）→ 取值类节点的断言必须在**真实 Run 内**做，否则恒为空/假。
   把布尔结果变成可观测量的通用套路：`Run(OnPlay → 212001 分支动作(isTrue ← 被测节点)，thenAction→伤害7 / elseAction→伤害1)`，断言扣血即可（同帧读取，不会被 AI 行动插空）。
5. **整数口不认识"Object 通道"节点**：`GetIntInput` 走 `ResolveNodeInt` 的白名单，像「获取事件记录变量」这种 Object 节点喂 `212002.count` 会**静默回落成字段默认值 0**。修法：在 `GetIntInput` 里对特定 Object 节点加兜底 `ToInt(GetObjectInput(...))`。
6. **`Card` 的构造函数是 `(card_id, uid, player_id)`**，`Card.CardData` 是**只读属性**（不能赋值；用同一 `card_id` 构造即解析到同一定义）。做卡牌快照时踩过 CS7036/CS0200。
7. **`GameLogic` 在 `TcgEngine.Gameplay`**（不是 `TcgEngine`）；写跨程序集工具/探针时要 `using TcgEngine.Gameplay;`。
8. **PowerShell 对 `NodeDoc.xml` 做 XML 往返会重排格式** → git diff 会显示上万行变化（**不是丢节点**，回读节点数即可确认）。改 XML 前先 `Copy-Item` 备份。
9. **运行期"写入类"节点必须防污染**：改卡牌定义一律**先克隆**（`ScriptableObject.Instantiate`）、且**按节点 id 缓存**（否则"复制→改属性→再取"每次都是新副本，改动留不住）；已是副本的输入要**原地改**。对资产原件要**拒绝 + 明确警告**。
10. **编辑器 `CanConnect` 有两条放宽**：`Object` / `NodeValueRef` 是**万能多态槽**、且**不校验数组性** → 端口类型写错**也能连上**（所以"接不上"往往不是类型问题，而是运行期没读该口）；但类型声明仍应与实际返回值一致，否则误导使用者。
11. **`TabButton.ui_panel == null` 点击=黑屏**（先 `SetAll(group,false)` 隐藏整组、无页面顶上）；生成工具 `AddReturnButton` 对**已存在**的按钮只改样式、**不补 `HomeReturnButton` 组件** → 死按钮。两处都已在生成工具侧根治（补组件 + 空绑定 TabButton 补绑/移除）。

## D-10 坑：全站"返回 → 黑屏"的机制与修法（2026-09 第二轮，用户实报）

**现象**：从首页模块进入某页（如卡池管理），点页内「返回」→ 整屏变黑、点哪都没反应、只能强退。
**机制**：首页模块页由 `TabButton.Activate()` → `SetAll(group,false)` **互斥隐藏**同组页面后再显示自己；
而这些页的「返回」多是裸 `Hide()`（场景 Inspector 绑 `XxxPanel.Hide`，或代码 `() => Hide()`）→
藏完身后同组页面全是隐藏状态 → **屏幕上没有任何页面 = 黑屏**。

修法（已落地在 `UIPanel` / `TabButton`）：

1. **兜底判定放在 `AfterHide()`**（不是 `Hide()`）：`Hide()` 是渐隐，调用方常有"Hide 本页 + 立刻 Show 下一页"
   （卡池管理 → 编辑 → 卡牌编辑器），等淡出结束再判定才不会误伤。
2. 判定条件：本页是 `TabButton.ui_panel`（模块页）**且** `!TabButton.IsSwitchingGroups`（切组中不插手）
   **且** `HomePanel` 存在、不是自己、当前不可见 **且** 没有其它可见页面 → `HomePanel.ReturnHome()`。
3. **"还有没有页面"必须只认根级页面**（父物体是 `UICanvas`/`UICanvasTop`/`Canvas*`）：
   `TabButton` 也被用在**页内子块**上（`CardZoomPanel/Box/TradeArea/BuyArea`，组 `card_trade`），
   父页隐藏时子块 `visible` 仍为 true → 会把兜底整个吞掉（**实测就是这么漏的**，诊断日志抓到 `BuyArea(组=card_trade)`）。
   同理 `TabButton.GetAll()` 只收录 Awake 跑过的，**未激活**层级里的 TabButton（隐藏的旧顶部导航栏）要用
   `Resources.FindObjectsOfTypeAll<TabButton>()` 才看得到。
4. **旧导航组（menu）的页面不能只调 `ReturnHome()`**：`PlayerPanel` 属 `menu` 组，`ReturnHome()` 里的
   `SetAll("home",false)` 不覆盖它 → 它会一直盖在首页之上（实测"点了没关掉"）。正确顺序：**先 `Hide()` 自己，再 `ReturnHome()`**。
5. 出口按钮自愈顺带修掉两类"点了没反应"：Inspector 监听目标为 **null（对象已丢）**（`PackZoomPanel.CloseArea`）、
   完全没有监听（`FilterCloseBtn`）→ `UIPanel.RepairExitButtons()` 同步补 `Hide()`。
   名字判定要排除 `Cardback`/`card_back`（"卡背"含 back，实测被当成返回）。

**验收探针（升级版，必须这样测）**：

- 每个用例前**状态归一化**（回首页 + 等 0.45s），否则上一轮残留的可见页面会让"是否黑屏"失真；
- 点「返回」后**等 0.8s** 再断言（渐隐 ≈0.25s，而且兜底在 `AfterHide` 里才执行）——只等 2~3 帧会误报"黑屏"；
- 断言两条：①本页 `IsVisible()==false` ②**除本页外仍有可见的根级页面**（否则就是黑屏）；
- 结果：16 个页面 **16/16 通过**，日志实证 `[导航] CardPoolPanel 隐藏后屏幕上没有任何页面 → 自动回首页`。

**写探针时的新踩坑**：探针在**全局命名空间**里直接写 `TabButton` 会被 Packages 里的同名类型遮蔽 →
`CS0117 'TabButton' does not contain a definition for 'GetAll'` / `CS1061 ui_panel`；
用 `using TabBtn = TcgEngine.UI.TabButton;` 或全限定名即可（`TcgEngine.UI` 命名空间内的脚本不受影响）。

**已作废页面（增益管理 `BuffPanel`）的移除方式**：不要删场景物体（它仍是"单条增益"的编辑落地页、被弹框引用），
而是让 `Show()` 在 `!opened_for_edit` 时**一律重定向**（用 `FindObjectOfType<CardEditorPanel>(true)` 判定宿主，
别用 `Get()` —— 编辑器默认失活、Awake 没跑、`Get()` 返回 null，会漏拦而把作废页弹出来），宿主不存在就**保持隐藏**。

## D-11 坑：「返回上一页」原语与两类"模式串台"（2026-09 第三轮，用户实报）

**实报症状 1**：从增益编辑器（规则编辑器的「增益参数」）点 ✕ → **又回到已作废的"增益管理页"**。
链路：`CardEditorPanel.OpenVariableEditor(Buff)` → 先 `Hide()` 掉卡牌编辑器 → `BuffPanel.EditBuff()`（设 `opened_for_edit=true`
后把活交给规则编辑器）→ 用户点 ✕ → `GraphEditorPanel.OnClose` 的**增益分支写着 `BuffPanel.Show()`** → 而
`opened_for_edit` 仍是 true（粘滞）→ 绕过作废拦截 → 旧页回来了。
修法三条：①`OnClose` 增益分支改为回**卡牌编辑器**（变量配置的宿主）；②`EditBuff` 转交后 `opened_for_edit = false`
（别留粘滞标志）；③作废页只做"落地页兜底"。

**实报症状 2**：关键词编辑器点「返回」→ **黑屏**。原因：宿主（卡牌编辑器）在进入变量编辑器时就被 `Hide()` 了，
关键词页裸 `Hide()` 之后身后空无一页。

**通用修法：`UIPanel.return_to`（一级"上一页"原语）**
- 打开方在"宿主先 Hide 再打开子页"时登记：`child.return_to = this;`
- 子页**彻底隐藏**后（`AfterHide`）若 `return_to` 有值 → `return_to.Show()`（消费一次，置 null）；
- **向前导航**（再进下一层，如关键词 → 规则图）必须 `return_to = null;`，否则 `Hide()` 会把上层弹回来；
- 自己**显式**处理完返回时也要清 `return_to`，否则 `AfterHide` 会二次消费陈旧值（实测：关键词退规则图落点被陈旧的"卡牌编辑器"顶掉一层）。

**模式串台（Mode leak）**：`GraphEditorPanel` 的各个 `Open*` 必须把**其它模式的标志全部清掉**。
实测 `OpenForKeyword()` 漏了 `editing_buff = null;` → "先编辑增益、再编辑关键词"时右列显示增益参数、
保存走增益分支、退出还落到卡牌编辑器（与 1869 行那处注释记载的漏洞同类）。

**出口按钮名字匹配不能用裸 `back` / `return` 做包含判断**（都实测误判过）：
- `Cardback`（"选择卡背"）含 back；
- `Lib_OnBeforeTurnStart`（节点库按钮）含 **befo·return·start** ← 会被当成"返回"，轻则漏判"本页没出口"，
  重则把 `Hide()` 挂到节点按钮上。只认 `close` / `cancel` / `backbtn` / `_back` / `back_` / `backzone` / `exitbtn` / `returnbtn` / `home`。

**复现"用户实报链路"的探针要求（这轮教训）**：
- 走**真实入口**（私有入口方法可用反射调，如 `CardEditorPanel.OpenVariableEditor(Kind.Buff, id)`）；
- 断言**落点正确**，不能只断言"没黑屏"（黑屏修完了，落错页面照样是 bug）；
- 每步之间等够（渐隐 0.25s + `AfterHide` 才有兜底/return_to 动作，取 0.8~1.0s）；
- 本轮结果：A 增益→✕ 落点=卡牌编辑器且旧页不再出现 ✅；B 关键词→返回 落点=卡牌编辑器 ✅；C 关键词→规则图→✕ 落点=关键词编辑器 ✅。

## D-12 铁律：MCP"连不上"的归因表（2026-09 用户点名自查后固化）

**前提事实（实测取证）**：Unity 侧 `McpServer`（`Packages/com.unity.ai-mcp-trae/Editor/McpServer.cs`）与 IDE 侧健康检查**通常一直是好的**：

- Editor.log 里反复出现 `[McpServer] 启动MCP服务器，端口: 9123` / `MCP服务器启动成功，端口: 9123 (尝试 1/3)`
  —— 这是**域重载后重建监听**，不是崩溃；
- IDE 日志 `[MCP:HealthPatrol] Checked: 1, Healthy: 1 [ch-unity-mcp]`、`[MCP:TokenRefresh] Rescan completed: tracking 1 server(s)`
  —— 客户端始终认为它健康；
- 实测：`http://localhost:9123/mcp` → HTTP 200 / 15ms / **85 个工具**；Unity 进程 PID 从 9/17 起未变过。

| 现象 | 真实原因 | 正确处置（以及**别再做的事**） |
|---|---|---|
| `fetch failed` / 无法连接 / `Unexpected content type: null` | **域重载窗口**：compile / play / stop 都会重建监听，十几秒不可达 | **轮询等待到恢复**（`tools/mcp.ps1 wait`）；不要"固定 sleep 后单次尝试就判服务挂了"，**更不要让人去重启服务** |
| `HTTP 400 错误的请求` | **Host 头不匹配**：Mono `HttpListener` 只注册 `localhost:9123`；用 `127.0.0.1` 请求必须显式带 `Host: localhost:9123` | 用 `localhost` 或带 Host 头（`tools/mcp.ps1` 已默认带）；别当成"服务不可达" |
| `tool does not exist or is not registered` | **IDE 侧工具表陈旧**（与服务无关） | 立刻改用 `tools/mcp.ps1` HTTP 直连继续干活；不要怀疑服务器、不要动服务 |
| 点 ▶ 进不去 Play / `play_mode_start` 永远只回"已加入异步队列" | **存在编译错误**（Unity 拒绝进 Play）；注意 `get_script_errors` 会**漏报** | 先 `compile_scripts` + **拉全量日志筛 `error CS`**；`tools/mcp.ps1 play` 现在会轮询到真 `Is Playing: True`，超时则提示查 CS |

**工具化（已落地 `tools/mcp.ps1`）**：`probe`（进程 / 端口 / 三种请求方式对照 / IDE 健康日志 + 归因）、`wait [秒]`（轮询到恢复）、
`compile`（触发 → 轮询 → 官方状态 + 全量日志双查）、`play` / `stop`（轮询到状态真的变化，超时提示查编译错误）。

**写 .ps1 工具的坑（这两次都踩了）**：

- `Write-Output ("A" ␤ + "B")` 这种**跨行 `+` 续行**会被 PowerShell 当成新语句 → `Missing closing ')'`。**提示串一律写成单行**；
- 多段输出别混进管道（会被 `Show-Text` 的正则吃掉）→ 进度/提示用 **`Write-Host`**；
- `⚠` 等 GBK 以外字符 + UTF-8 无 BOM 会出玄学解析错 → 脚本存 **UTF-8 with BOM**，提示串尽量用 ASCII 符号（`[!]`）。

**⑤ 写 `.ps1` 脚本必须带 UTF-8 BOM —— 否则中文会"吃掉"紧跟其后的引号，整个脚本语法崩。**
Windows PowerShell 5.1 对**无 BOM 的 UTF-8** 按 ANSI(GBK) 解析：中文字节会与后一个 ASCII 字节凑成一个 GBK 字符，
于是 `"用法: call <tool> '<json>'"` 里的引号被吞 → 报 `Unexpected token ':'`、`Missing closing '}'`、`'<' operator is reserved`
一堆莫名其妙的语法错（看着像脚本写错了，其实是编码）。
**修法**：写完立刻补 BOM（`[IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true)))`），
或干脆让脚本内容全 ASCII。**每次用编辑器/工具改过 `.ps1` 后都要重补一次 BOM**（多数写文件工具默认写无 BOM）。

**⑥ 自动化脚本的"等待产物"必须**先删旧产物**。**
本轮 `mcp.ps1 autobattle` 的等待循环是"报告文件里出现 `done=1` 就认为跑完"——
上一次留下的报告里本来就有 `done=1` → 循环立刻退出，打印的是**上一次的结果**（时间戳/配置都对不上，极易误判为"验证通过"）。
**规则**：等文件出现前先删掉它；并把"产物时间戳/关键配置"一起打印出来做交叉核对。
