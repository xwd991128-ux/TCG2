using System;

namespace TcgEngine.UI
{
    /// <summary>
    /// 富文本格式类型。参数型标签（高亮/颜色/字号）与无参标签（粗体/斜体…）统一由配置表驱动，
    /// 按钮条不写死任何标签名，新增格式只需在此枚举 + RichTextTags.defs 里加一行。
    /// </summary>
    public enum RichTextFormat
    {
        Bold = 0,       // <b>
        Italic = 1,     // <i>
        Underline = 2,  // <u>
        Strike = 3,     // <s>
        Mark = 4,       // <mark=#FFFF00>
        Color = 5,      // <color=#RRGGBB>
        Size = 6,       // <size=30>
    }

    /// <summary>一种富文本格式的配置：TMP 标签名 + 默认参数 + 按钮文字</summary>
    public class RichTextFormatDef
    {
        public RichTextFormat format;
        public string tag;          // TMP 标签名（不含尖括号）
        public string default_attr; // 默认参数，null/空 表示无参标签
        public string display;      // 按钮显示文字

        public RichTextFormatDef(RichTextFormat format, string tag, string default_attr, string display)
        {
            this.format = format;
            this.tag = tag;
            this.default_attr = default_attr;
            this.display = display;
        }

        /// <summary>是否为带参数标签（颜色/字号/高亮）</summary>
        public bool IsParametric { get { return !string.IsNullOrEmpty(default_attr); } }
    }

    /// <summary>
    /// 一次富文本编辑操作的结果：新文本 + 新光标/选区。
    /// 索引为 C# string 的 char 下标，与 TMP_InputField 的 position 语义一致。
    /// </summary>
    public struct RichTextOp
    {
        public string text;     // 编辑后的新文本
        public int selStart;    // 新选区起点（selLength==0 时即光标位置）
        public int selLength;   // 新选区长度（0 = 仅光标）
    }

    /// <summary>
    /// TMP 富文本标签的包裹 / 插入 / 开关纯字符串工具，完全不依赖 UI，可直接单测。
    ///
    /// 设计取舍（对应任务书 3.4）：
    /// - 不解析整棵标签树，只保证「点一次加一层、再点一次减一层」严格可逆；
    /// - 包裹前先把选区边界「对齐到标签 token 边界」，避免把 &lt;b&gt; 切成 &lt;b 这种半截标签；
    /// - 再剔掉跨出选区的半截标签对，避免出现 &lt;b&gt;&lt;i&gt;x&lt;/b&gt;&lt;/i&gt; 这类错位；
    /// - 第一版不做完整 HTML Range 平衡，TMP 的 richText 渲染本身容错，非法/未闭合标签不会崩溃。
    /// </summary>
    public static class RichTextTags
    {
        // ---- 标签名常量 ----
        public const string TagBold = "b";
        public const string TagItalic = "i";
        public const string TagUnderline = "u";
        public const string TagStrike = "s";
        public const string TagMark = "mark";
        public const string TagColor = "color";
        public const string TagSize = "size";

        // ---- 默认参数 ----
        public const string DefaultMarkColor = "#FFFF00";
        public const string DefaultColor = "#FF0000";
        public const string DefaultSize = "30";

        /// <summary>预设色板（红/橙/黄/绿/青/蓝/紫/白），供「颜色」按钮弹出</summary>
        public static readonly string[] PaletteColors =
        {
            "#FF3333", "#FF9933", "#FFFF33", "#33FF33",
            "#33FFFF", "#3399FF", "#CC66FF", "#FFFFFF",
        };

        /// <summary>字号档位（20/24/28/32/40），供「字号」按钮弹出</summary>
        public static readonly int[] FontSizes = { 20, 24, 28, 32, 40 };

        // ---- 配置表 ----
        private static readonly RichTextFormatDef[] defs =
        {
            new RichTextFormatDef(RichTextFormat.Bold,      TagBold,      null,                "B"),
            new RichTextFormatDef(RichTextFormat.Italic,    TagItalic,    null,                "I"),
            new RichTextFormatDef(RichTextFormat.Underline, TagUnderline, null,                "U"),
            new RichTextFormatDef(RichTextFormat.Strike,    TagStrike,    null,                "S"),
            new RichTextFormatDef(RichTextFormat.Mark,      TagMark,      DefaultMarkColor,    "高亮"),
            new RichTextFormatDef(RichTextFormat.Color,     TagColor,     DefaultColor,        "颜色"),
            new RichTextFormatDef(RichTextFormat.Size,      TagSize,      DefaultSize,         "字号"),
        };

        /// <summary>全部格式定义（按钮条按此顺序生成）</summary>
        public static RichTextFormatDef[] All { get { return defs; } }

        /// <summary>取某格式的配置</summary>
        public static RichTextFormatDef GetDef(RichTextFormat format)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].format == format)
                    return defs[i];
            }
            return null;
        }

        /// <summary>构造开标签：attr 为空返回 &lt;tag&gt;，否则返回 &lt;tag=attr&gt;</summary>
        public static string OpenTag(string tag, string attr = null)
        {
            return string.IsNullOrEmpty(attr) ? "<" + tag + ">" : "<" + tag + "=" + attr + ">";
        }

        /// <summary>构造闭标签 &lt;/tag&gt;</summary>
        public static string CloseTag(string tag)
        {
            return "</" + tag + ">";
        }

        /// <summary>按格式做开关式包裹；attr 为空时用配置表默认参数（如 color 默认 #FF0000）</summary>
        public static RichTextOp ToggleFormat(string text, int selStart, int selLength, RichTextFormat format, string attr = null)
        {
            RichTextFormatDef def = GetDef(format);
            if (def == null)
                return new RichTextOp { text = text ?? "", selStart = selStart, selLength = selLength };

            string use_attr = string.IsNullOrEmpty(attr) ? def.default_attr : attr;
            return ToggleWrap(text, selStart, selLength, def.tag, use_attr);
        }

        /// <summary>
        /// 开关式包裹：
        /// - 选区已被同名标签恰好完整包裹 且 参数相同（无参标签则都为 null，如 b/i/u/s）→ 剥离这一层；
        /// - 已被同名标签包裹但参数不同（改颜色/改字号）→ 只替换开标签属性，保持包裹不脱落；
        /// - 否则有选区 → 套一层（选区平移到包裹内的文字上，保持选中）；
        /// - 无选区 → 在光标处插入一对空标签，光标落在两个标签中间。
        /// </summary>
        public static RichTextOp ToggleWrap(string text, int selStart, int selLength, string tag, string attr = null)
        {
            if (text == null)
                text = "";
            if (string.IsNullOrEmpty(tag))
                tag = TagBold;

            selStart = ClampInt(selStart, 0, text.Length);
            selLength = ClampInt(selLength, 0, text.Length - selStart);

            int start = selStart;
            int end = selStart + selLength;

            // 选区不切断标签 token，并剔除跨界的半截标签对
            if (end > start)
            {
                BalanceRange(text, ref start, ref end);
            }
            else if (EnclosingTagStart(text, start) >= 0)
            {
                // 光标停在某个标签 token 内部：先移到标签之后，避免插出 <b><b></b>b> 这类碎片
                int after = EnclosingTagEnd(text, start);
                if (after >= 0)
                    start = end = after;
            }

            RichTextOp op;

            // 已被同名标签恰好包裹时的处理：
            // - 参数相同（无参标签则都为 null，如 b/i/u/s）→ 取消这一层（开关式）
            // - 参数不同（改颜色/改字号）→ 只替换开标签属性，保持包裹
            //   否则「先点红色再点自定义绿」会被当成取消包裹，表现为自定义色「点了没反应」
            int openStart, openEnd, closeStart;
            string existing_attr;
            if (end > start && TryFindWrap(text, start, end, tag, out openStart, out openEnd, out closeStart, out existing_attr))
            {
                if (string.Equals(NormalizeAttr(existing_attr), NormalizeAttr(attr), StringComparison.OrdinalIgnoreCase))
                {
                    string close_tag = CloseTag(tag);
                    op.text = text.Remove(closeStart, close_tag.Length).Remove(openStart, openEnd - openStart);
                    op.selStart = openStart;
                    op.selLength = end - start;
                    return op;
                }

                string replaced_open = OpenTag(tag, attr);
                op.text = text.Substring(0, openStart) + replaced_open
                        + text.Substring(openEnd, closeStart - openEnd)
                        + text.Substring(closeStart);
                op.selStart = openStart + replaced_open.Length;
                op.selLength = end - start;
                return op;
            }

            string open = OpenTag(tag, attr);
            string close = CloseTag(tag);

            if (end > start)
            {
                op.text = text.Substring(0, start) + open + text.Substring(start, end - start) + close + text.Substring(end);
                op.selStart = start + open.Length;
                op.selLength = end - start;
            }
            else
            {
                int pos = ClampInt(start, 0, text.Length);
                op.text = text.Substring(0, pos) + open + close + text.Substring(pos);
                op.selStart = pos + open.Length;
                op.selLength = 0;
            }
            return op;
        }

        /// <summary>
        /// 把六位十六进制色规整成 #RRGGBB。
        /// 接受 RRGGBB / #RRGGBB / #RRGGBBAA（截断前六位）；非法返回 null。
        /// </summary>
        public static string NormalizeHexColor(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;
            string s = input.Trim();
            if (s.StartsWith("#"))
                s = s.Substring(1);
            if (s.Length == 8)
                s = s.Substring(0, 6);
            if (s.Length == 3)
            {
                // #RGB → #RRGGBB（常见简写：F00 → FF0000）
                s = new string(new char[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            }
            if (s.Length != 6)
                return null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                    return null;
            }
            return "#" + s.ToUpperInvariant();
        }

        /// <summary>整串文本里是否含任意富文本标签（供 UI 判断是否要提示「源码模式」等）</summary>
        public static bool HasAnyTag(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<' && IsTagTokenStart(text, i))
                    return true;
            }
            return false;
        }

        // ================= 内部：选区平衡 =================

        /// <summary>
        /// 把选区收缩/扩张到「不含半截标签」的安全范围：
        /// 1) 边界若落在某个标签 token 内部，先移到整个 token 之外；
        /// 2) 再反复剔除开始处无配对的开标签、结束处无配对的闭标签。
        /// </summary>
        private static void BalanceRange(string text, ref int start, ref int end)
        {
            int lt = EnclosingTagStart(text, start);
            if (lt >= 0)
                start = lt;
            int gt = EnclosingTagEnd(text, end);
            if (gt >= 0)
                end = gt;

            for (int guard = 0; guard < 16; guard++)
            {
                bool changed = false;

                // 起点是「没有配对闭标签」的开标签 → 起点推到该标签之后
                string name;
                bool closing;
                int te;
                if (ReadTagToken(text, start, out name, out closing, out te)
                    && !closing && !HasCloseTag(text, te, end, name))
                {
                    start = te;
                    changed = true;
                }

                // 终点前是「没有配对开标签」的闭标签 → 终点退到该标签之前
                int ts;
                string cname;
                if (ReadCloseTagEndingAt(text, end, out cname, out ts)
                    && !HasOpenTag(text, start, ts, cname))
                {
                    end = ts;
                    changed = true;
                }

                if (!changed)
                    break;
            }

            if (end < start)
            {
                int tmp = start;
                start = end;
                end = tmp;
            }
        }

        /// <summary>位置 pos 落在某个（合法）标签 token 内部时，返回该 token 的 '&lt;' 下标；否则 -1</summary>
        private static int EnclosingTagStart(string text, int pos)
        {
            for (int i = pos - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '>')
                    return -1;
                if (c == '<')
                    return IsTagTokenStart(text, i) ? i : -1;
            }
            return -1;
        }

        /// <summary>位置 pos 落在某个（合法）标签 token 内部时，返回该 token 的 '&gt;' 之后下标；否则 -1</summary>
        private static int EnclosingTagEnd(string text, int pos)
        {
            bool inside = false;
            for (int i = pos - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '>')
                    break;
                if (c == '<')
                {
                    inside = IsTagTokenStart(text, i);
                    break;
                }
            }
            if (!inside)
                return -1;

            int j = pos;
            while (j < text.Length && text[j] != '>' && text[j] != '<')
                j++;
            if (j < text.Length && text[j] == '>')
                return j + 1;
            return -1;
        }

        /// <summary>
        /// 选区（[start, end)）是否被 &lt;tag...&gt;…&lt;/tag&gt; 恰好完整包裹。
        /// 是则输出开标签范围（openStart..openEnd）、闭标签起点（closeStart）与开标签属性（attr，可空）。
        /// </summary>
        private static bool TryFindWrap(string text, int start, int end, string tag,
            out int openStart, out int openEnd, out int closeStart, out string attr)
        {
            openStart = -1;
            openEnd = -1;
            closeStart = -1;
            attr = null;

            // 开标签必须紧贴选区头：从 start-1 向左找最近的 '<'，并要求该 token 恰好结束于 start
            if (start <= 0 || text[start - 1] != '>')
                return false;
            int lt = start - 1;
            while (lt >= 0 && text[lt] != '<')
                lt--;
            if (lt < 0)
                return false;

            string oname;
            bool oclosing;
            int oend;
            if (!ReadTagToken(text, lt, out oname, out oclosing, out oend) || oclosing || oend != start)
                return false;
            if (!string.Equals(oname, tag, StringComparison.OrdinalIgnoreCase))
                return false;

            // 闭标签必须紧贴选区尾
            string close = CloseTag(tag);
            if (end + close.Length > text.Length)
                return false;
            if (string.Compare(text, end, close, 0, close.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            openStart = lt;
            openEnd = oend;
            closeStart = end;
            attr = ReadAttr(text, lt, oend, tag);
            return true;
        }

        /// <summary>读取 &lt;tag=VALUE&gt; 里的 VALUE（无属性返回 null）</summary>
        private static string ReadAttr(string text, int tagStart, int tagEnd, string tag)
        {
            int p = tagStart + 1 + tag.Length;
            int gt = tagEnd - 1;                 // '&gt;' 的下标
            if (p < gt && text[p] == '=')
                return text.Substring(p + 1, gt - p - 1);
            return null;
        }

        /// <summary>属性归一化（仅用于比较是否同一参数）：空 → null，其余去空白</summary>
        private static string NormalizeAttr(string attr)
        {
            return string.IsNullOrEmpty(attr) ? null : attr.Trim();
        }

        // ================= 内部：标签 token 解析 =================

        /// <summary>text[i] 是否为合法标签 token 的起点（'&lt;' 开头、以 '&gt;' 结束、中间无 '&lt;'）</summary>
        private static bool IsTagTokenStart(string text, int i)
        {
            if (i < 0 || i >= text.Length || text[i] != '<')
                return false;

            int p = i + 1;
            if (p < text.Length && text[p] == '/')
                p++;
            if (p >= text.Length || !IsAsciiLetter(text[p]))
                return false;
            p++;

            while (p < text.Length && text[p] != '>')
            {
                if (!IsTagChar(text[p]))
                    return false;
                p++;
            }
            return p < text.Length && text[p] == '>';
        }

        /// <summary>
        /// 从 i 处读取一个完整标签 token。
        /// name = 标签名（小写，闭标签也返回名字）；closing = 是否 &lt;/name&gt;；end = '&gt;' 之后的下标。
        /// </summary>
        private static bool ReadTagToken(string text, int i, out string name, out bool closing, out int end)
        {
            name = null;
            closing = false;
            end = i;

            if (!IsTagTokenStart(text, i))
                return false;

            int p = i + 1;
            if (text[p] == '/')
            {
                closing = true;
                p++;
            }
            int ns = p;
            while (p < text.Length && IsAsciiLetter(text[p]))
                p++;
            name = text.Substring(ns, p - ns).ToLowerInvariant();

            while (p < text.Length && text[p] != '>')
                p++;
            end = p + 1;
            return true;
        }

        /// <summary>[from, to) 内是否含 &lt;/name&gt;</summary>
        private static bool HasCloseTag(string text, int from, int to, string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string needle = "</" + name + ">";
            return IndexOfIgnoreCase(text, needle, from, to) >= 0;
        }

        /// <summary>[from, to) 内是否含 &lt;name&gt; 或 &lt;name=...&gt;</summary>
        private static bool HasOpenTag(string text, int from, int to, string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            int i = from;
            while (i < to)
            {
                int at = IndexOfIgnoreCase(text, "<" + name, i, to);
                if (at < 0)
                    return false;
                int p = at + 1 + name.Length;
                if (p < text.Length && (text[p] == '>' || text[p] == '='))
                    return true;
                i = at + 1;
            }
            return false;
        }

        /// <summary>读取紧贴 end 之前的闭标签 &lt;/name&gt;，返回其名字与 '&lt;' 下标</summary>
        private static bool ReadCloseTagEndingAt(string text, int end, out string name, out int start)
        {
            name = null;
            start = end;
            if (end <= 0 || end > text.Length || text[end - 1] != '>')
                return false;

            int i = end - 1;
            while (i >= 0 && text[i] != '<')
                i--;
            if (i < 0)
                return false;

            string nm;
            bool closing;
            int te;
            if (!ReadTagToken(text, i, out nm, out closing, out te) || !closing || te != end)
                return false;

            name = nm;
            start = i;
            return true;
        }

        private static int IndexOfIgnoreCase(string text, string needle, int from, int to)
        {
            if (string.IsNullOrEmpty(needle) || from < 0 || to > text.Length)
                return -1;
            int last = to - needle.Length;
            for (int i = from; i <= last; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (char.ToLowerInvariant(text[i + j]) != char.ToLowerInvariant(needle[j]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return i;
            }
            return -1;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>标签 token 内允许出现的字符（属性值、空格、引号等）</summary>
        private static bool IsTagChar(char c)
        {
            if (IsAsciiLetter(c) || (c >= '0' && c <= '9'))
                return true;
            switch (c)
            {
                case '=':
                case '#':
                case '.':
                case '-':
                case '_':
                case ',':
                case '%':
                case '"':
                case '\'':
                case ' ':
                case '/':
                    return true;
                default:
                    return false;
            }
        }

        private static int ClampInt(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
