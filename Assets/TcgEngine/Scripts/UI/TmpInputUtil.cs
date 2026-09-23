using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 运行时 TMP 输入框的公共保护层：统一规避 TextMeshPro 3.0.7 的两个已知问题。
    ///
    /// ① 空文本 + 失焦后仍处"选择态" → GenerateHightlight 越界崩溃
    ///    报错栈：TMP_InputField.GenerateHightlight → OnFillVBO → UpdateGeometry → Rebuild → CanvasUpdateRegistry
    ///    原因：OnDeselect 会把 m_SelectionStillActive 置 true **并一直保留** —— 之后 OnFillVBO 不再提前返回，
    ///          此时若文本为空（textInfo.characterCount == 0），GenerateHightlight 会走 else 分支访问
    ///          characterInfo[m_CaretSelectPosition - 1] 即 characterInfo[-1] → IndexOutOfRangeException。
    ///          所以**任何**空输入框，只要被点过一次再点开，就会崩（表现为"所有输入框编辑都报错"）。
    ///    规避：输入框**永不保持真正空文本** —— 空值写入单个空格占位（SetTextWithoutNotify，不触发 onValueChanged、
    ///          不污染业务数据）；读取统一走 Read()（占位/纯空白 → 空串）。
    ///
    /// ② 插入光标（Caret）不显示
    ///    原因：TMP 只在 OnEnable 里创建 Caret，且要求 m_TextComponent 已赋值；而 AddComponent&lt;TMP_InputField&gt;()
    ///          会立刻触发一次 OnEnable（那时 textComponent 还是 null）→ Caret 永远建不出来；
    ///          另外 caretColor 默认深色 (50,50,50)，在深色底上肉眼看不见。
    ///    规避：绑定完组件后重启一次 enabled（只在未聚焦时做一次），并把 caretColor 设为白色。
    ///
    /// 用法：运行时新建输入框，绑定完 textComponent / textViewport 后调用 Guard()；
    ///       场景/预制体上的输入框，在面板打开与刷新时统一调用 Guard()（见 GraphEditorPanel.GuardAllInputs）。
    /// </summary>
    public static class TmpInputUtil
    {
        /// <summary>空值占位：单个空格（不可见、可被 Trim、任何中文字体都有该字形）</summary>
        public const string Placeholder = " ";

        /// <summary>已做过一次性初始化（光标 / 失焦兜底监听）的输入框实例</summary>
        private static readonly HashSet<int> s_guarded = new HashSet<int>();

        /// <summary>读取输入框文本：占位与首尾空白视作空串（业务数据始终拿到"干净"的值）</summary>
        public static string Read(TMP_InputField inp)
        {
            if (inp == null)
                return "";
            string s = inp.text;
            return string.IsNullOrEmpty(s) ? "" : s.Trim();
        }

        /// <summary>写入输入框文本：空值写占位（不触发 onValueChanged → 不会把占位写进业务数据）</summary>
        public static void Write(TMP_InputField inp, string value)
        {
            if (inp == null)
                return;
            string shown = string.IsNullOrWhiteSpace(value) ? Placeholder : value;
            if (inp.text == shown)
                return;
            inp.SetTextWithoutNotify(shown);   //TMP 3.x 提供：只改文本、不回调
            inp.ForceLabelUpdate();
        }

        /// <summary>
        /// 一次性初始化 + 空文本兜底：
        /// 首次调用补建插入光标（重启 enabled）并挂上「失焦即兜底」监听；每次都确保文本非空（避免越界崩溃）。
        /// </summary>
        public static void Guard(TMP_InputField inp)
        {
            if (inp == null || inp.textComponent == null)
                return;

            int id = inp.GetInstanceID();
            if (s_guarded.Add(id))
            {
                inp.caretColor = Color.white;      //深色底上默认深色光标看不见
                if (!inp.isFocused)
                {
                    //重启一次让 TMP 建出 Caret（只在未聚焦时做，避免打断正在编辑的光标）
                    inp.enabled = false;
                    inp.enabled = true;
                }
                inp.onEndEdit.AddListener(_ => EnsureNotEmpty(inp));   //失焦即兜底：此刻补占位不会污染数据
                inp.onSelect.AddListener(_ => BeginEdit(inp));         //★点进来先清占位空格，否则打字会多出一个空格
            }
            NormalizeLayout(inp);    //★单行输入框统一垂直居中（旧版 UpperLeft → TMP TopLeft，文字贴顶）
            EnsureNotEmpty(inp);
        }

        /// <summary>★获得焦点时：若当前只有占位空白，先清成真空串 —— 否则用户打字后会出现"多一个空格"
        /// （占位空格留在文本里跟着一起提交）。清空只发生在**聚焦态**：TMP 的空文本越界崩溃只出现在
        /// "空文本 + 失焦后仍处选择态"，而失焦那一刻 QueueGuard 会立刻补回占位，所以这里安全。</summary>
        public static void BeginEdit(TMP_InputField inp)
        {
            if (inp == null || inp.textComponent == null)
                return;
            if (!string.IsNullOrEmpty(inp.text) && inp.text.Trim().Length > 0)
                return;                                   //有真实内容 → 不动
            if (inp.text != "")                           //只有空白（占位）→ 清掉
            {
                inp.SetTextWithoutNotify("");
                inp.ForceLabelUpdate();
            }
        }

        /// <summary>★单行输入框统一"文字垂直居中"：旧版 uGUI Text 的 UpperLeft 会被映射成 TMP 的
        /// TopLeft（贴顶），且文本 rect 常是"下留 4 / 上留 0"的不对称内边距 → 看上去字偏上。
        /// 这里对**单行**框强制 mid-left（TMP 的 Left 即中线左对齐）并把上下留白改成对称；
        /// 多行框（卡牌文本/描述）保持顶端对齐不动。</summary>
        public static void NormalizeLayout(TMP_InputField inp)
        {
            if (inp == null || inp.textComponent == null)
                return;
            if (inp.lineType == TMP_InputField.LineType.MultiLineNewline
                || inp.lineType == TMP_InputField.LineType.MultiLineSubmit)
                return;
            TMP_Text tc = inp.textComponent;
            if (tc.alignment != TextAlignmentOptions.Left)
                tc.alignment = TextAlignmentOptions.Left;      //= MidlineLeft：垂直居中 + 左对齐
            RectTransform rt = tc.rectTransform;
            Vector2 min = rt.offsetMin, max = rt.offsetMax;
            float pad = Mathf.Min(Mathf.Abs(min.y), Mathf.Abs(max.y));   //两侧取小的，保证不裁字
            if (!Mathf.Approximately(min.y, pad) || !Mathf.Approximately(max.y, -pad))
            {
                rt.offsetMin = new Vector2(min.x, pad);
                rt.offsetMax = new Vector2(max.x, -pad);
            }
        }

        /// <summary>空文本兜底：写入占位，彻底绕开 characterCount == 0 的越界路径</summary>
        public static void EnsureNotEmpty(TMP_InputField inp)
        {
            if (inp == null || inp.textComponent == null)
                return;
            if (!string.IsNullOrEmpty(inp.text))
                return;
            inp.SetTextWithoutNotify(Placeholder);
            inp.ForceLabelUpdate();
        }
    }
}
