using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 弹层统一皮肤：把"项目认可的弹层规格"固化成一个地方，所有弹层都走它。
    ///
    /// 为什么要有这个类（而不是每个弹层自己写 MakeText/MakeButton）：
    ///   之前的实现每处都是散值 —— 行高 30、颜色硬编码 (1,1,1,0.08)、文本全局 Ellipsis 截断、
    ///   底部元素靠手算 anchoredPosition 摆放 —— 结果就是「像调试界面」「长文案被切成 …看不清」「压字」。
    ///
    /// 规格来源（全部取自项目既有实现，不新造）：
    ///   · 面板底色 UITheme.BgPopup、遮罩 UITheme.MaskPopup、行常态 CtrlWeak、行选中 UISelectPopup 同款 0.2/0.55/0.85
    ///   · 列表行高 UITheme.RowH(44)、按钮高 BtnH(46)、图标热区 BtnIcon(56)、间距只用 GapXs/Sm/Md/Lg
    ///   · 字号：面板标题 FontButton(22)、区块标题 FontStatus(20)+TextTitle、正文 FontBody(16)、次要 FontSmall(15)
    ///   · 文本一律 UIFactory.CreateTmpText + UIFonts.ResolveFont()，溢出模式 Overflow（★不再用 Ellipsis 截断）
    ///   · 超宽的文本改为折行，并用 GetPreferredValues 精确撑高行高（既不裁字也不压行）
    /// </summary>
    public static class PopupSkin
    {
        /// <summary>选项行选中底色（与 UISelectPopup 完全一致）</summary>
        public static readonly Color RowSelected = new Color(0.2f, 0.55f, 0.85f, 0.95f);

        /// <summary>面板标题条高度</summary>
        public const float TitleH = 52f;

        /// <summary>区块标题行高</summary>
        public const float SectionH = 36f;

        /// <summary>底部区高度：一行提示 + 一行按钮</summary>
        public const float FooterH = 26f + UITheme.BtnH + UITheme.GapMd * 2f;

        private static TextMeshProUGUI m_measure;

        // ==================== 骨架 ====================

        /// <summary>建全屏层（含 CanvasGroup，供 UIPanel 做淡入淡出）。面板是它的子物体。</summary>
        public static RectTransform CreateRootLayer(Transform context, string name, int sibling_last)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(context, false);
            UIFactory.SetStretch(rt);
            if (sibling_last > 0)
                go.transform.SetAsLastSibling();
            return rt;
        }

        /// <summary>遮罩（全屏，可点）。返回它的 Button，调用方决定点击行为。</summary>
        public static Button CreateMask(RectTransform root)
        {
            Image img = UIFactory.CreateImage("Mask", root, UITheme.MaskPopup);
            UIFactory.SetStretch(img.rectTransform);
            img.raycastTarget = true;
            Button btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            return btn;
        }

        /// <summary>居中面板：BgPopup 底 + 一层分隔线（标题下 / 底部上），并吞掉点击避免穿透到遮罩。</summary>
        public static RectTransform CreatePanel(RectTransform root, string name, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);

            Image bg = go.AddComponent<Image>();
            bg.color = UITheme.BgPopup;
            Button swallow = go.AddComponent<Button>();
            swallow.targetGraphic = bg;
            swallow.transition = Selectable.Transition.None;

            return rt;
        }

        /// <summary>面板标题（左）+ 关闭按钮（右上，图标）。返回标题文本。</summary>
        public static TMP_Text CreateTitleRow(RectTransform panel, string title, out Button close)
        {
            TMP_Text t = NewText("Title", panel, title, UITheme.FontButton, UITheme.TextTitle, TextAlignmentOptions.MidlineLeft);
            RectTransform trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -UITheme.GapSm);
            trt.sizeDelta = new Vector2(-(UITheme.BtnIcon + UITheme.GapLg * 2f), TitleH);

            close = CreateCloseButton(panel);
            CreateHLine(panel, "TitleLine", -TitleH - UITheme.GapSm);
            return t;
        }

        /// <summary>
        /// 关闭按钮：走项目约定 —— 名字含 CloseBtn 时 UIFactory 会用 exit.png 图标替换文字；
        /// 图标取不到（例如非编辑器环境）则回退成 TMP 的「×」，绝不留下一个空按钮。
        /// </summary>
        public static Button CreateCloseButton(RectTransform panel)
        {
            Button btn = UIFactory.CreateButton("CloseBtn", panel, "", null, UITheme.FontButton, UITheme.Ctrl);
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-UITheme.GapSm, -UITheme.GapSm);
            rt.sizeDelta = new Vector2(UITheme.BtnIcon, UITheme.BtnIcon);

            Image img = btn.GetComponent<Image>();
            if (img == null || img.sprite == null)
            {
                TMP_Text t = NewText("Text", rt, "×", UITheme.FontButton, UITheme.TextBody, TextAlignmentOptions.Center);
                UIFactory.SetStretch(t.rectTransform);
            }
            return btn;
        }

        /// <summary>横向分隔线（用 UITheme.Divider，画出一条"区块边界"，避免整块糊成一片）</summary>
        public static RectTransform CreateHLine(RectTransform panel, string name, float anchored_y)
        {
            Image img = UIFactory.CreateImage(name, panel, UITheme.Divider);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, anchored_y);
            rt.sizeDelta = new Vector2(-UITheme.GapLg * 2f, 1f);
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>
        /// 滚动列表（Viewport/Content/VLG/CSF 参数与 UISelectPopup 一致，只是行高改用 UITheme.RowH）。
        /// <paramref name="footer_h"/> 为底部保留高度，列表不会压到底部元素上。
        /// </summary>
        public static RectTransform CreateScroll(RectTransform panel, float footer_h)
        {
            RectTransform srt = UIFactory.CreateRect("Scroll", panel);
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(UITheme.GapLg, footer_h);
            srt.offsetMax = new Vector2(-UITheme.GapLg, -TitleH - UITheme.GapSm * 2f);
            ScrollRect scroll = srt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            RectTransform vrt = UIFactory.CreateRect("Viewport", srt);
            UIFactory.SetStretch(vrt);
            vrt.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vrt;

            RectTransform crt = UIFactory.CreateRect("Content", vrt);
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = crt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;      //★必须 true，否则行上的 LayoutElement 高度会被忽略
            vlg.spacing = UITheme.GapSm;
            vlg.padding = UITheme.PadList();
            ContentSizeFitter csf = crt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            return crt;
        }

        /// <summary>底部区：上面一行状态提示，下面一行右对齐按钮（用 LayoutGroup 排版，不再手算坐标 → 不可能压字）</summary>
        public static RectTransform CreateFooter(RectTransform panel, float height, out TMP_Text status)
        {
            RectTransform rt = UIFactory.CreateRect("Footer", panel);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(0f, height);

            CreateHLineAtBottom(panel, "FooterLine", height + UITheme.GapMd);

            status = NewText("Status", rt, "", UITheme.FontSmall, UITheme.TextDim, TextAlignmentOptions.MidlineLeft);
            RectTransform srt = status.rectTransform;
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -UITheme.GapXs);
            srt.sizeDelta = new Vector2(-UITheme.GapLg * 2f, 24f);

            RectTransform row = UIFactory.CreateRect("Buttons", rt);
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(1f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, UITheme.GapMd);
            row.sizeDelta = new Vector2(-UITheme.GapLg * 2f, UITheme.BtnH);

            HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.spacing = UITheme.GapLg;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            return row;
        }

        private static void CreateHLineAtBottom(RectTransform panel, string name, float from_bottom)
        {
            Image img = UIFactory.CreateImage(name, panel, UITheme.Divider);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, from_bottom);
            rt.sizeDelta = new Vector2(-UITheme.GapLg * 2f, 1f);
            img.raycastTarget = false;
        }

        /// <summary>底部按钮（TMP 自绘：不用旧版 uGUI Text，避免无中文字形）</summary>
        public static Button CreateFooterButton(RectTransform row, string label, float width, Action on_click)
        {
            GameObject go = new GameObject(label + "Btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(row, false);
            rt.sizeDelta = new Vector2(width, UITheme.BtnH);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = UITheme.BtnH;

            Image img = go.GetComponent<Image>();
            img.color = UITheme.Ctrl;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);

            TMP_Text t = NewText("Text", rt, label, UITheme.FontButton, UITheme.TextBody, TextAlignmentOptions.Center);
            UIFactory.SetStretch(t.rectTransform);
            if (on_click != null)
                btn.onClick.AddListener(() => on_click());
            return btn;
        }

        // ==================== 行工厂 ====================

        /// <summary>区块标题（FontStatus + TextTitle：与选项行形成明显层级，不再和正文糊在一起）</summary>
        public static TMP_Text CreateSection(RectTransform list, string text)
        {
            RectTransform row = NewRow(list, "Section", SectionH);
            TMP_Text t = NewText("Label", row, text, UITheme.FontStatus, UITheme.TextTitle, TextAlignmentOptions.MidlineLeft);
            RectTransform trt = t.rectTransform;
            UIFactory.SetStretch(trt);
            trt.offsetMin = new Vector2(UITheme.GapXs, 0f);
            trt.offsetMax = new Vector2(-UITheme.GapXs, 0f);
            return t;
        }

        /// <summary>选项行：行高 44、常态 CtrlWeak、选中高亮、可选 √/□ 前缀。超宽文案自动折行并撑高。</summary>
        public static GameObject CreateOptionRow(RectTransform list, string label, bool on, bool multi, Action on_click)
        {
            string prefix = multi ? (on ? "√ " : "□ ") : (on ? "● " : "○ ");
            string text = prefix + label;
            float avail = RowTextWidth(list);
            bool wrap = MeasureWidth(text, UITheme.FontBody) > avail;
            float h = wrap ? MeasureHeight(text, UITheme.FontBody, avail) : UITheme.RowH;

            RectTransform row = NewRow(list, "Opt", h);
            Image bg = row.gameObject.AddComponent<Image>();
            bg.color = on ? RowSelected : UITheme.CtrlWeak;
            Button btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            UITheme.ApplyButtonColors(btn);
            btn.onClick.AddListener(() => on_click());

            TMP_Text t = NewText("Label", row, text, UITheme.FontBody, UITheme.TextBody, TextAlignmentOptions.MidlineLeft);
            RectTransform trt = t.rectTransform;
            UIFactory.SetStretch(trt);
            trt.offsetMin = new Vector2(UITheme.GapLg, 0f);
            trt.offsetMax = new Vector2(-UITheme.GapLg, 0f);
            t.enableWordWrapping = wrap;
            return row.gameObject;
        }

        /// <summary>
        /// 动作行：左边一句说明，右边一个动作按钮（如「← 主卡」）。
        /// right_label 为空时**整行**可点（如「+ 添加卡」）；不可用时文字转次要色且不可点。
        /// </summary>
        public static GameObject CreateActionRow(RectTransform list, string label, string right_label, Color bg_color,
            Action on_click, bool interactable = true)
        {
            float btn_w = UITheme.BtnWShort;
            bool has_right = !string.IsNullOrEmpty(right_label);
            float avail = RowTextWidth(list) - (has_right ? btn_w + UITheme.GapLg : 0f);
            bool wrap = MeasureWidth(label, UITheme.FontBody) > avail;
            float h = wrap ? MeasureHeight(label, UITheme.FontBody, avail) : UITheme.RowH;

            RectTransform row = NewRow(list, "Action", h);
            Image bg = row.gameObject.AddComponent<Image>();
            bg.color = bg_color;
            bg.raycastTarget = has_right;      //整行可点时由 row 自己接收射线

            if (!has_right && on_click != null)
            {
                Button rowbtn = row.gameObject.AddComponent<Button>();
                rowbtn.targetGraphic = bg;
                UITheme.ApplyButtonColors(rowbtn);
                rowbtn.interactable = interactable;
                rowbtn.onClick.AddListener(() => on_click());
            }

            TMP_Text t = NewText("Label", row, label, UITheme.FontBody,
                interactable ? UITheme.TextBody : UITheme.TextDim, TextAlignmentOptions.MidlineLeft);
            RectTransform trt = t.rectTransform;
            UIFactory.SetStretch(trt);
            trt.offsetMin = new Vector2(UITheme.GapLg, 0f);
            trt.offsetMax = new Vector2(-(UITheme.GapLg + (has_right ? btn_w + UITheme.GapLg : 0f)), 0f);
            t.enableWordWrapping = wrap;

            if (has_right)
            {
                Button b = NewTinyButton(row, right_label, btn_w, interactable, on_click);
                RectTransform brt = b.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(1f, 0.5f);
                brt.anchorMax = new Vector2(1f, 0.5f);
                brt.pivot = new Vector2(1f, 0.5f);
                brt.anchoredPosition = new Vector2(-UITheme.GapLg, 0f);
                brt.sizeDelta = new Vector2(btn_w, UITheme.BtnHSm);
            }
            return row.gameObject;
        }

        /// <summary>行内小按钮（TMP 自绘，避免旧版 uGUI Text 无中文字形）</summary>
        public static Button NewTinyButton(Transform parent, string label, float width, bool interactable, Action on_click)
        {
            GameObject go = new GameObject("Btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(width, UITheme.BtnHSm);

            Image img = go.GetComponent<Image>();
            img.color = UITheme.Ctrl;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);
            btn.interactable = interactable;

            TMP_Text t = NewText("Text", rt, label, UITheme.FontBody, UITheme.TextBody, TextAlignmentOptions.Center);
            UIFactory.SetStretch(t.rectTransform);
            if (on_click != null)
                btn.onClick.AddListener(() => on_click());
            return btn;
        }

        /// <summary>信息行（校验错误 / 提示）：自动折行并按内容撑高 —— 长文案完整可见，不裁剪不压行。</summary>
        public static TMP_Text CreateInfoRow(RectTransform list, string text, Color color, int font_size = 0)
        {
            int size = font_size > 0 ? font_size : UITheme.FontBody;
            float avail = RowTextWidth(list);
            float h = MeasureHeight(text, size, avail);

            RectTransform row = NewRow(list, "Info", h);
            TMP_Text t = NewText("Label", row, text, size, color, TextAlignmentOptions.TopLeft);
            RectTransform trt = t.rectTransform;
            UIFactory.SetStretch(trt);
            trt.offsetMin = new Vector2(UITheme.GapLg, UITheme.GapXs);
            trt.offsetMax = new Vector2(-UITheme.GapLg, -UITheme.GapXs);
            t.enableWordWrapping = true;
            return t;
        }

        /// <summary>清空列表（倒序 + 先 SetActive(false)：Destroy 延迟到帧末，否则同帧重建会出现重复区块）</summary>
        public static void ClearList(RectTransform list)
        {
            if (list == null)
                return;
            for (int i = list.childCount - 1; i >= 0; i--)
            {
                Transform child = list.GetChild(i);
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        // ==================== 文本 / 度量 ====================

        /// <summary>建 TMP 文本（统一走 UIFactory + UIFonts 字体管线；溢出模式 Overflow，不截断）</summary>
        public static TMP_Text NewText(string name, Transform parent, string text, int size, Color color, TextAlignmentOptions align)
        {
            return UIFactory.CreateTmpText(name, parent, text, size, color, align, UIFonts.ResolveFont(), false);
        }

        public static float MeasureWidth(string text, int size)
        {
            TextMeshProUGUI m = GetMeasure();
            if (m == null)
                return text != null ? text.Length * size : 0f;
            m.fontSize = size;
            m.enableWordWrapping = false;
            return m.GetPreferredValues(text != null ? text : "", 0f, 0f).x;
        }

        /// <summary>折行后的高度（含上下留白）。行高由它决定 → 长文案不会被裁掉。</summary>
        public static float MeasureHeight(string text, int size, float width)
        {
            TextMeshProUGUI m = GetMeasure();
            if (m == null)
                return UITheme.RowH;
            m.fontSize = size;
            m.enableWordWrapping = true;
            float h = m.GetPreferredValues(text != null ? text : "", Mathf.Max(40f, width), 0f).y;
            return Mathf.Max(UITheme.RowH, h + UITheme.GapXs * 2f);
        }

        /// <summary>列表可用文本宽度（去掉左右内边距）</summary>
        public static float RowTextWidth(RectTransform list)
        {
            float w = list != null ? list.rect.width : 0f;
            if (w <= 1f)
                w = 600f;      //布局还没跑过时的保守值（首帧构建时常见）
            return w - UITheme.PadList().left - UITheme.PadList().right - UITheme.GapLg * 2f;
        }

        /// <summary>
        /// 复用的测量组件（不进任何 Canvas，只用来算文本尺寸）。
        /// 标记 HideAndDontSave：**绝不**让它有机会被保存进场景（此前运行时建的 UI 泄漏进场景，教训）。
        /// </summary>
        private static TextMeshProUGUI GetMeasure()
        {
            if (m_measure != null)
                return m_measure;
            GameObject go = new GameObject("PopupSkinMeasure");
            go.hideFlags = HideFlags.HideAndDontSave;
            m_measure = go.AddComponent<TextMeshProUGUI>();
            m_measure.hideFlags = HideFlags.HideAndDontSave;
            m_measure.font = UIFonts.ResolveFont();
            m_measure.raycastTarget = false;
            return m_measure;
        }

        // ==================== 交互辅助 ====================

        /// <summary>是否按下 Esc（弹层用它关闭；项目用旧版 Input）</summary>
        public static bool EscPressed()
        {
            return Input.GetKeyDown(KeyCode.Escape);
        }

        /// <summary>出现/关闭的面板缩放过渡（淡入淡出由 UIPanel 负责，这里补一点"弹出"的动感）</summary>
        public static void AnimateScale(RectTransform panel, bool visible, float speed = 12f)
        {
            if (panel == null)
                return;
            float target = visible ? 1f : 0.97f;
            float cur = panel.localScale.x;
            float next = Mathf.MoveTowards(cur, target, speed * Time.unscaledDeltaTime);
            panel.localScale = new Vector3(next, next, 1f);
        }

        // ==================== 内部 ====================

        private static RectTransform NewRow(RectTransform list, string name, float height)
        {
            RectTransform rt = UIFactory.CreateRect(name, list);
            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return rt;
        }
    }
}
