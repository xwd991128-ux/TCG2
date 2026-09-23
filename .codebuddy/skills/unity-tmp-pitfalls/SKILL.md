---
name: unity-tmp-pitfalls
description: Unity 项目（TCG2，TextMeshPro 3.0.7）里 TextMeshPro 输入框与字体相关的三类高频问题排查与修复：空文本输入框抛 IndexOutOfRangeException（GenerateHightlight 访问 characterInfo[-1]）、界面符号显示为方块（缺字形）、输入框不显示光标。当出现 TMP 报错、UI 方块字、输入框无法编辑或光标缺失时使用。
---

# Unity TMP 3.0.7 高频坑与修法（TCG2 项目）

## 坑 1：`IndexOutOfRangeException` —— 空文本输入框

**报错栈（典型）**
```
IndexOutOfRangeException: Index was outside the bounds of the array.
TMPro.TMP_InputField.GenerateHightlight (VertexHelper vbo, Vector2 roundingOffset)   // TMP_InputField.cs:3750
TMPro.TMP_InputField.OnFillVBO (Mesh vbo)                                            // :3577
TMPro.TMP_InputField.UpdateGeometry ()                                               // :3513
TMPro.TMP_InputField.Rebuild (CanvasUpdate update)                                   // :3488
UnityEngine.UI.CanvasUpdateRegistry.PerformUpdate ()
```

**根因**
- `GenerateHightlight` 第 3750 行是 `else` 分支：`characterInfo[m_CaretSelectPosition - 1]`。
- 当文本为空（`textInfo.characterCount == 0`）时 `m_CaretSelectPosition == 0` → 访问 `characterInfo[-1]` → 越界。
- 触发条件：输入框 `isFocused` 或 `m_SelectionStillActive` 为真才会进入该代码；而 `OnDeselect` 会把
  `m_SelectionStillActive` 置真**并一直保留** —— 所以**任何空输入框，被点过一次再点开就会崩**。

**修法（项目已有现成实现，直接用）**
- `Assets/TcgEngine/Scripts/UI/TmpInputUtil.cs`
  - `Write(inp, value)`：空值写单空格占位（`SetTextWithoutNotify`，不触发 `onValueChanged`、不污染业务数据）。
  - `Read(inp)`：读值并 `Trim`（占位/纯空白 → 空串）。
  - `Guard(inp)`：一次性补建光标 + 挂「失焦即兜底」监听 + 每次确保文本非空。
  - `EnsureNotEmpty(inp)`：空文本兜底（写占位）。
- 所有运行时新建输入框：绑定 `textComponent`/`textViewport` 后调用 `Write` + `Guard`。
- 场景/预制体上的输入框：面板打开/刷新时调用 `GraphEditorPanel.GuardAllInputs()`（已挂在
  `RefreshForm` / `RefreshNodeLib` / `RefreshNodeFields`），必要时可自行加调用点。

**检查点**：`SetInput`/`GetInput`/`GetInputInt` 是否已改走 `TmpInputUtil.Write/Read`；面板内是否还有
「直接 `field.text = ...`」或「读 `field.text` 不 Trim」的漏网点。

## 坑 2：符号显示为方块（缺字形）

**根因**：用了 GB2312 符号区之外的字符，而 SimHei / MSYH 这类中文字体没有这些字形。

| 缺字形（会变方块） | 改用（GB2312 内，中文字体必有） |
|---|---|
| `▾` U+25BE、`▾▸◂` 小三角 | `▼` `▲` `←` `→` |
| `☑`/`☐` U+2610/2611 | `√` / `□` |
| `▶` U+25B6、`✕` U+2715 | `▲`、`×`（`■` 本身在 GB2312 内，可用） |

**修法**：全局统一用右列表格字符；分类图标等一律用 ASCII（见 `CategoryIcon` 注释：`! ? > #`）。

**检查点**：全仓搜 `▾ ☑ ☐ ▸ ◂ ▶ ✕` 是否清零（含 `Editor/*Builder.cs` 这类"生成场景"的代码，
场景里的旧字符需重跑生成工具或由运行时归一，如 `SetButtonText(btn_del.transform, "×")`）。

## 坑 3：输入框不显示光标（Caret）

**根因**：TMP 只在 `OnEnable` 里创建 Caret，且要求 `m_TextComponent` 已赋值；而
`AddComponent<TMP_InputField>()` 会**立刻**触发一次 `OnEnable`（那时 `textComponent` 还是 null）
→ Caret 永远建不出来。另：`caretColor` 默认深色 (50,50,50)，深色底上肉眼看不见。

**修法**：绑定完组件后（未聚焦时）重启一次 `enabled`，并设 `caretColor = Color.white` —— 已在
`TmpInputUtil.Guard` 内实现。

## 坑 4：单行输入框文字**贴顶**（不垂直居中）+ 打字后**多一个空格**（2026-09 用户实报）

两个症状一个共同来源：**旧版 uGUI `InputField` → `TMP_InputField` 的转换**（`GraphEditorPanel.ConvertInputToTMP`）。

**① 文字贴顶**：旧版 Text 的对齐常是 `UpperLeft`，`ToTmpAlignment` 会映射成 TMP 的 `TopLeft`（贴顶）；
而 TMP 里"垂直居中"的枚举是 **`TextAlignmentOptions.Left`（= MidlineLeft）**，不是 `TopLeft`。
另外旧框的文本 rect 常是"下留 4 / 上留 0"的不对称内边距（实测 `offsetMin=(10,4) offsetMax=(0,0)`、高 32 vs 框 36）→ 叠加起来看着明显偏上。
**修法**：`TmpInputUtil.NormalizeLayout(inp)` —— **单行**框强制 `alignment = Left`，并把文本 rect 上下留白改成对称
（`pad = min(|min.y|,|max.y|)`，保证不裁字）；多行框（卡牌文本/描述）保持顶端对齐**不动**。已挂在 `Guard()` 里 → 全项目输入框统一生效。

**② 多一个空格**：`TmpInputUtil` 为了绕开坑 1（空文本越界崩溃）会给**空输入框写一个空格占位**，
用户接着打字时这个空格会留在文本里（"abc" 变成 " abc"/"abc "）；如果这个值再被当成**搜索关键词**，
筛选就永远搜不到东西。
**修法**：`Guard()` 里给 `onSelect` 挂 `BeginEdit(inp)` —— **聚焦瞬间**把"只有占位空白"的文本清成真空串
（此时处于聚焦态，不会触发坑 1 的崩溃路径；失焦时 `onEndEdit→EnsureNotEmpty` 立刻补回占位兜底）。
业务侧读取一律用 `TmpInputUtil.Read()`（占位/首尾空白 → 空串），例如节点库搜索框。

**诊断手法（不要靠眼睛）**：`tools/probe/InputLayoutProbe.cs`（临时探针，建 `tools/input_probe_flag.txt` → 进 Play）
打开面板 → 调 `EnsureTmpUI` + **`GuardAllInputs`**（不跑这个，修复不会生效，会误判"没修好"）→
dump 每个输入框的 `alignment` / 文本 rect 的 anchors+offsets / **字符码点**（空格=32、零宽空格=8203 一眼看穿）
→ 写 `tools/input_layout.tsv`。修复后自检 5 项全 PASS：两个框 `alignment=Left` 且相对中心偏移=0、
聚焦后文本清空、失焦兜底补回占位、写入 `abc` 读回仍是 `abc`（无多余空格）。

## 字体解析的两级探测（`UIFonts`）

- `FontProbe`（核心）：中文 + `√×`；**不合格即弃用**（避免半屏方块）。
- `SymbolProbe`（符号）：`▼▲□■●○◆◇←→√×`；**缺符号只降级为次选**，绝不因符号把能显示中文的字体全否掉。
- 候选优先级：外部显式设置的 `font_asset` → 现做动态中文字体（48pt / 2048 图盘 / 多页）→ 项目已有中文 TMP 资产；
  优先挑「符号也齐」的那个；全都不齐时取第一个中文可用者并打印警告。

**排查提示**：若仍有方块，先看 Console 是否出现 `UIFonts：所有候选字体都缺界面符号…`；
再确认用的是**动态**字体（能按需补字）而不是只烤了部分字的静态资产。

## 相关技能

- 运行时自建 uGUI 与场景遗留控件重叠、弹框风格对齐、数据类 API 不统一（如 `KeywordData` 无
  `GetTitle`）、旧页面弃用：见技能 `unity-workshop-ui-pitfalls`。

## 验收清单

1. 控制台不再出现 `GenerateHightlight` 越界异常；空输入框反复点击、点击别处再点回均正常。
2. 节点、弹层、卡牌参数、节点库筛选按钮等处**均无方块字符**（含下拉箭头与勾选框）。
3. 输入框点击后能看到白色插入光标；输入内容即时保存，读回值不带占位空格（`Trim` 生效）。
4. 旧数据（如 `整数:true` 这类脏值）打开与编辑不报错。
