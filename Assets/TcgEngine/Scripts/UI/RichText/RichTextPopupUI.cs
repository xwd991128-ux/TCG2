using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace TcgEngine.UI
{
    /// <summary>
    /// TMP 富文本编辑弹框（复用 UIPanel 的 Show/Hide 淡入淡出）。
    ///
    /// 视觉与应用内「多选框」弹层保持同一套风格（见 Workshop/UI/GraphEditorPanel.EnsureFieldSelectPopup）：
    /// 黑色半透明全屏遮罩 + 深灰居中面板 + 半透明白按钮 + 蓝色高亮，故界面同样在运行时自建，
    /// 场景里只需一个挂本组件的空物体（可留空由 RichTextEditorUI 自动创建），无需手动拖拽任何控件引用。
    ///
    /// 结构：标题 / 按钮条（B I U S 高亮 颜色 字号）/ 源码编辑区(richText=false) / 预览行(richText=true) / 确定·取消，
    /// 颜色与字号各自弹出一个多选框风格的小面板。
    /// </summary>
    public class RichTextPopupUI : UIPanel
    {
        [Header("可选：场景预绑定（留空则运行时自动创建并在 Open 时使用）")]
        public TMP_InputField edit_field;          // 源码编辑区（多行，richText=false）
        public TextMeshProUGUI preview_text;       // 预览行（只读，richText=true）

        [Header("文案与字体")]
        public string title = "编辑卡牌描述";
        public int preview_font_size = 24;
        public TMP_FontAsset font;                  // 本弹框字体；留空走 UIFonts 全局字体（推荐全局统一）

        /// <summary>外部字体解析器（全局兜底）：UIFonts 未设置时用它取「当前界面已渲染成功」的字体，
        /// 典型用法是宿主面板（如规则编辑器）把画布字体交出来，避免弹框退回无中文字形的 TMP 默认字体。</summary>
        public static Func<TMP_FontAsset> font_resolver;

        // ---- 多选框同款配色 ----
        private static readonly Color MaskColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color PanelColor = new Color(0.12f, 0.12f, 0.15f, 1f);
        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.06f);

        private const float PanelWidth = 820f;
        private const float PanelHeight = 660f;

        private bool built;
        private RectTransform root_rect;
        private RectTransform panel_rect;
        private RectTransform palette_panel;
        private RectTransform size_panel;
        private TMP_InputField hex_field;
        private TextMeshProUGUI hex_hint;

        private RichTextEditorUI owner;         // 展示框回写目标（可空）
        private Action<string> m_callback;      // 通用确认回调（卡牌编辑器等外部调用方）

        // 输入框持焦期间持续缓存选区：点格式按钮时输入框会先失焦并把选区收缩成光标
        private int m_last_anchor, m_last_focus;
        private bool has_selection_cache;

        // ================= 生命周期 =================

        protected override void Awake()
        {
            base.Awake();
            EnsureBuilt();
        }

        protected override void Update()
        {
            base.Update();
            // 缓存用「原始字符串下标」：与 RichTextTags 的 string 索引语义一致，
            // 不能用 selectionAnchorPosition/selectionFocusPosition（TMP 的字符索引空间，可能因 textInfo 过期而错位）
            if (edit_field != null && edit_field.isFocused)
            {
                m_last_anchor = edit_field.selectionStringAnchorPosition;
                m_last_focus = edit_field.selectionStringFocusPosition;
                has_selection_cache = true;
            }
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = true;
                canvas_group.interactable = true;
            }
        }

        public override void Hide(bool instant = false)
        {
            // 关闭前结束输入状态：TMP 的鼠标拖拽选区协程（MouseDragOutsideRect）依赖 pointer 状态，
            // 在对象被隐藏时继续跑容易踩到空引用/过期下标
            if (edit_field != null && edit_field.isFocused)
            {
                try { edit_field.DeactivateInputField(); }
                catch (System.Exception) { }
            }
            HideSubPanels();
            base.Hide(instant);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }
        }

        // ================= 对外 API =================

        /// <summary>
        /// 运行时创建富文本弹框：优先挂到 context 所在 Canvas 下（占满屏幕），
        /// 控件由 Awake 自建，调用方无需绑定任何引用。
        /// </summary>
        public static RichTextPopupUI Create(Transform context)
        {
            Transform parent = context;
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>(true) : null;
            if (canvas != null)
                parent = canvas.transform;
            if (parent == null)
                return null;

            GameObject go = new GameObject("RichTextPopup", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            return go.AddComponent<RichTextPopupUI>();
        }

        /// <summary>打开并载入富文本源码；owner 为「确定」时的回写目标</summary>
        public void Open(string rich_text, RichTextEditorUI editor)
        {
            // 先走通用入口（会把 owner 清空），再记录回写目标
            Open(rich_text, editor != null ? (Action<string>)editor.ApplyResult : null);
            owner = editor;
        }

        /// <summary>通用打开入口：确认时把新富文本交给 on_confirm</summary>
        public void Open(string rich_text, Action<string> on_confirm)
        {
            EnsureBuilt();
            owner = null;               // 通用入口不写回展示框
            m_callback = on_confirm;

            if (edit_field != null)
            {
                edit_field.richText = false;
                edit_field.text = rich_text ?? "";
            }

            RefreshPreview();
            HideSubPanels();
            Show();
            ApplyFonts();   // 字体统一走全局入口覆盖

            // 打开后把光标放到文本末尾并激活输入框：TMP 输入框未获焦时不绘制光标，
            // 否则用户会以为「打开就丢失光标」，必须先点一下才能输入
            if (edit_field != null)
            {
                int end = edit_field.text != null ? edit_field.text.Length : 0;
                m_last_anchor = end;
                m_last_focus = end;
                has_selection_cache = true;
                edit_field.ActivateInputField();
                ApplyCaret(end, 0);
                if (gameObject.activeInHierarchy)
                    StartCoroutine(CoRestoreSelection(edit_field.text, end, 0));
            }
        }

        /// <summary>按格式做开关式包裹（无选区则插入空标签对）</summary>
        public void ApplyFormat(RichTextFormat format, string attr = null)
        {
            EnsureBuilt();
            if (edit_field == null)
                return;

            int start, len;
            GetSelection(out start, out len);
            RichTextOp op = RichTextTags.ToggleFormat(edit_field.text, start, len, format, attr);
            ApplyOp(op);
        }

        /// <summary>兼容按标签名调用的入口（tag 如 b/i/color，attr 可空）</summary>
        public void ApplyTag(string tag, string attr = null)
        {
            EnsureBuilt();
            if (edit_field == null)
                return;

            int start, len;
            GetSelection(out start, out len);
            RichTextOp op = RichTextTags.ToggleWrap(edit_field.text, start, len, tag, attr);
            ApplyOp(op);
        }

        /// <summary>确定：回写 owner 并触发确认回调</summary>
        public void OnClickConfirm()
        {
            EnsureBuilt();
            string result = edit_field != null ? edit_field.text : "";
            Action<string> callback = m_callback;
            RichTextEditorUI ow = owner;
            m_callback = null;
            owner = null;

            Hide();
            if (ow != null)
                ow.ApplyResult(result);      // 规格路径：展示框回写 + onTextChanged
            if (callback != null)
                callback.Invoke(result);     // 通用路径：外部回调
        }

        /// <summary>取消：关闭并丢弃修改</summary>
        public void OnClickCancel()
        {
            owner = null;
            m_callback = null;
            Hide();
        }

        /// <summary>色板 / 自定义色统一入口（hex 形如 #RRGGBB，也接受 #RGB / #RRGGBBAA）</summary>
        public void OnPickColor(string hex)
        {
            string norm = RichTextTags.NormalizeHexColor(hex);
            if (norm == null)
            {
                // 面板内直接给出可见提示，避免「点了没反应」的错觉
                if (hex_hint != null)
                    hex_hint.text = "颜色格式应为 #RRGGBB";
                Debug.LogWarning("富文本编辑器：无效的颜色值「" + hex + "」，请输入 #RRGGBB");
                return;
            }
            if (hex_hint != null)
                hex_hint.text = "";
            HideSubPanels();
            ApplyFormat(RichTextFormat.Color, norm);
        }

        /// <summary>字号选择统一入口</summary>
        public void OnPickSize(int size)
        {
            HideSubPanels();
            ApplyFormat(RichTextFormat.Size, size.ToString());
        }

        // ================= 界面构建 =================

        /// <summary>幂等构建整套弹框（场景只放一个空物体，控件全部运行时生成）</summary>
        private void EnsureBuilt()
        {
            if (built)
                return;
            built = true;

            if (root_rect == null)
                root_rect = GetComponent<RectTransform>();
            if (canvas_group == null)
                canvas_group = GetComponent<CanvasGroup>();
            if (root_rect != null)
                Stretch(root_rect, 0f);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }

            // 全屏遮罩：点击空白处关闭
            Image mask = GetComponent<Image>();
            if (mask == null)
                mask = gameObject.AddComponent<Image>();
            mask.color = MaskColor;
            Button mask_btn = GetComponent<Button>();
            if (mask_btn == null)
                mask_btn = gameObject.AddComponent<Button>();
            mask_btn.targetGraphic = mask;
            mask_btn.transition = Selectable.Transition.None;
            //★ 点空白处**不关闭**：正在编辑的富文本属于"未保存内容"，只认「取消 / 确定 / ×」。
            //   遮罩仍 raycastTarget=true → 仍然拦住穿透点击与背景滚动。
            mask_btn.onClick.AddListener(() => { });

            BuildPanel();
            ApplyFonts();
        }

        private void BuildPanel()
        {
            panel_rect = MakeRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            Image panel_img = AddImage(panel_rect, PanelColor);
            AddClickBlocker(panel_rect, panel_img);

            // 标题 + 关闭
            MakeTextTop("Title", panel_rect, title, 24, TextAlignmentOptions.MidlineLeft, Color.white, 24f, 10f, 74f, 40f);
            Button close = MakeButton("Close", panel_rect, "×", 24, OnClickCancel);
            SetTopRight(close.GetComponent<RectTransform>(), 12f, 10f, 40f, 40f);

            // 分隔线
            RectTransform line = MakeRect("Divider", panel_rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PanelWidth - 48f, 2f));
            SetTopStretch(line, 24f, 54f, 24f, 2f);
            AddImage(line, DividerColor);

            // 按钮条
            RectTransform bar = MakeRect("FormatBar", panel_rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PanelWidth - 48f, 48f));
            SetTopStretch(bar, 24f, 64f, 24f, 48f);
            HorizontalLayoutGroup hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(2, 2, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            BuildFormatBar(bar);

            // 源码编辑区
            MakeTextTop("EditLabel", panel_rect, "源码（可直接手改标签，预览实时生效）", 19, TextAlignmentOptions.MidlineLeft, new Color(0.80f, 0.85f, 0.90f, 1f), 28f, 122f, 28f, 24f);
            RectTransform edit_rt = MakeRect("EditField", panel_rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            SetTopStretch(edit_rt, 24f, 152f, 24f, 216f);
            edit_field = MakeInputField(edit_rt, true, "输入富文本源码，或用上方按钮插入标签…", 22);
            edit_field.onValueChanged.AddListener(OnEditChanged);

            // 预览
            MakeTextTop("PreviewLabel", panel_rect, "预览", 19, TextAlignmentOptions.MidlineLeft, new Color(0.80f, 0.85f, 0.90f, 1f), 28f, 382f, 28f, 24f);
            RectTransform preview_box = MakeRect("PreviewBox", panel_rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            SetTopStretch(preview_box, 24f, 410f, 24f, 130f);
            AddImage(preview_box, new Color(0f, 0f, 0f, 0.25f));
            preview_text = MakeText("PreviewText", preview_box, "", preview_font_size, TextAlignmentOptions.TopLeft, Color.white);
            Stretch(preview_text.rectTransform, 12f);
            preview_text.enableWordWrapping = true;
            preview_text.richText = true;
            preview_text.overflowMode = TextOverflowModes.Ellipsis;

            // 确定 / 取消
            Button confirm = MakeButton("BtnConfirm", panel_rect, "确定", 20, OnClickConfirm);
            SetBottomRight(confirm.GetComponent<RectTransform>(), 24f, 20f, 130f, 46f);
            Button cancel = MakeButton("BtnCancel", panel_rect, "取消", 20, OnClickCancel);
            SetBottomRight(cancel.GetComponent<RectTransform>(), 170f, 20f, 130f, 46f);

            BuildPalettePanel();
            BuildSizePanel();
            HideSubPanels();
        }

        /// <summary>格式按钮条：完全由 RichTextTags 配置表驱动</summary>
        private void BuildFormatBar(RectTransform bar)
        {
            RichTextFormatDef[] defs = RichTextTags.All;
            for (int i = 0; i < defs.Length; i++)
            {
                RichTextFormatDef def = defs[i];
                float w = def.IsParametric ? 78f : 48f;
                Button btn = MakeButton("Btn_" + def.tag, bar, def.display, 20, null);
                btn.GetComponent<RectTransform>().sizeDelta = new Vector2(w, 40f);   // childControlWidth=false 时布局组按子物体自身尺寸排布

                RichTextFormat fmt = def.format;
                btn.onClick.AddListener(() => OnClickFormat(fmt));
            }
        }

        /// <summary>「颜色」弹色板，「字号」弹档位，其余直接开关式包裹</summary>
        private void OnClickFormat(RichTextFormat format)
        {
            if (format == RichTextFormat.Color)
            {
                bool open = palette_panel != null && !palette_panel.gameObject.activeSelf;
                SetSubPanelVisible(palette_panel, open);
                if (open && hex_hint != null)
                    hex_hint.text = "";
                return;
            }
            if (format == RichTextFormat.Size)
            {
                bool open = size_panel != null && !size_panel.gameObject.activeSelf;
                SetSubPanelVisible(size_panel, open);
                return;
            }
            ApplyFormat(format, null);
        }

        /// <summary>色板面板：8 预设色 + 自定义十六进制色</summary>
        private void BuildPalettePanel()
        {
            palette_panel = MakeRect("PalettePanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(520f, 268f));
            Image bg = AddImage(palette_panel, PanelColor);
            AddClickBlocker(palette_panel, bg);

            MakeTextTop("Title", palette_panel, "选择颜色", 22, TextAlignmentOptions.MidlineLeft, Color.white, 20f, 8f, 64f, 36f);
            Button close = MakeButton("Close", palette_panel, "×", 22, HideSubPanels);
            SetTopRight(close.GetComponent<RectTransform>(), 10f, 8f, 36f, 36f);

            string[] colors = RichTextTags.PaletteColors;
            for (int i = 0; i < colors.Length; i++)
            {
                int col = i % 4;
                int row = i / 4;
                Color c;
                ColorUtility.TryParseHtmlString(colors[i], out c);

                RectTransform swatch = MakeRect("Color" + colors[i], palette_panel, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(112f, 56f));
                SetTopLeft(swatch, 20f + col * 122f, 54f + row * 64f, 112f, 56f);
                Image img = AddImage(swatch, c);
                Button btn = swatch.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;

                string captured = colors[i];
                btn.onClick.AddListener(() => OnPickColor(captured));
            }

            // 自定义色
            MakeTextTop("HexLabel", palette_panel, "自定义 #RRGGBB", 18, TextAlignmentOptions.MidlineLeft, new Color(0.80f, 0.85f, 0.90f, 1f), 20f, 188f, 320f, 28f);
            RectTransform hex_rt = MakeRect("HexField", palette_panel, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(180f, 36f));
            SetTopLeft(hex_rt, 20f, 222f, 180f, 36f);
            hex_field = MakeInputField(hex_rt, false, "#RRGGBB", 18);
            hex_field.text = RichTextTags.DefaultColor;

            Button use = MakeButton("Use", palette_panel, "使用", 18, () => OnPickColor(hex_field != null ? hex_field.text : ""));
            SetTopLeft(use.GetComponent<RectTransform>(), 212f, 222f, 84f, 36f);

            // 非法颜色值的可见提示（如输入了 3 位以外的非法字符）
            hex_hint = MakeTextTop("HexHint", palette_panel, "", 16, TextAlignmentOptions.MidlineLeft, new Color(1f, 0.5f, 0.5f, 1f), 304f, 222f, 200f, 36f);
        }

        /// <summary>字号面板：20/24/28/32/40</summary>
        private void BuildSizePanel()
        {
            size_panel = MakeRect("SizePanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(460f, 132f));
            Image bg = AddImage(size_panel, PanelColor);
            AddClickBlocker(size_panel, bg);

            MakeTextTop("Title", size_panel, "选择字号", 22, TextAlignmentOptions.MidlineLeft, Color.white, 20f, 8f, 64f, 36f);
            Button close = MakeButton("Close", size_panel, "×", 22, HideSubPanels);
            SetTopRight(close.GetComponent<RectTransform>(), 10f, 8f, 36f, 36f);

            int[] sizes = RichTextTags.FontSizes;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                Button btn = MakeButton("Size" + size, size_panel, size.ToString(), 20, null);
                SetTopLeft(btn.GetComponent<RectTransform>(), 20f + i * 88f, 62f, 76f, 46f);
                btn.onClick.AddListener(() => OnPickSize(size));
            }
        }

        // ================= 编辑逻辑 =================

        private void OnEditChanged(string _)
        {
            RefreshPreview();
        }

        /// <summary>取当前选区：持焦时读实时选区，点按钮失焦收缩时回退到缓存，都没有则落在文本末尾</summary>
        private void GetSelection(out int start, out int len)
        {
            int anchor, focus;
            if (edit_field.isFocused)
            {
                anchor = edit_field.selectionStringAnchorPosition;
                focus = edit_field.selectionStringFocusPosition;
            }
            else if (has_selection_cache)
            {
                anchor = m_last_anchor;
                focus = m_last_focus;
            }
            else
            {
                anchor = focus = edit_field.text != null ? edit_field.text.Length : 0;
            }

            start = Mathf.Min(anchor, focus);
            len = Mathf.Abs(focus - anchor);
            if (start < 0)
                start = 0;
        }

        /// <summary>写回编辑区并恢复光标/选区，预览同步</summary>
        private void ApplyOp(RichTextOp op)
        {
            if (edit_field == null)
                return;

            edit_field.text = op.text;
            RefreshPreview();

            string now = edit_field.text ?? "";
            int s = Mathf.Clamp(op.selStart, 0, now.Length);
            int e = Mathf.Clamp(op.selStart + op.selLength, 0, now.Length);

            m_last_anchor = s;
            m_last_focus = e;
            has_selection_cache = true;

            edit_field.ActivateInputField();
            ApplyCaret(s, e - s);
            if (gameObject.activeInHierarchy)
                StartCoroutine(CoRestoreSelection(now, s, e - s));   // TMP 激活流程可能推迟一帧，再补一次
        }

        /// <summary>
        /// 恢复光标/选区。必须用「原始字符串下标」API（stringPosition/selectionString*），
        /// 不能用 caretPosition/selectionAnchorPosition（那是 TMP 的字符索引空间，转换依赖 textInfo，
        /// 而 text 刚被替换时 textInfo 还是旧的，会算出越界下标，下一次输入就在 Append 里抛
        /// ArgumentOutOfRangeException）。同时先 ForceLabelUpdate 让 textInfo 与当前文本一致。
        /// </summary>
        private void ApplyCaret(int start, int len)
        {
            if (edit_field == null)
                return;
            string cur = edit_field.text ?? "";
            int s = Mathf.Clamp(start, 0, cur.Length);
            int e = Mathf.Clamp(start + len, 0, cur.Length);

            edit_field.ForceLabelUpdate();                    // 让输入框内部 processed 文本跟上新值
            TMP_Text tc = edit_field.textComponent;
            if (tc != null && tc.gameObject.activeInHierarchy)
            {
                try { tc.ForceMeshUpdate(); }                 // 立刻重建 textInfo（否则换算会用到上一帧的字符表）
                catch (System.Exception) { }
            }

            edit_field.stringPosition = s;                    // 原始字符串下标：锚点+焦点归位并清选区
            edit_field.selectionStringAnchorPosition = s;      // 锚点 = 起点
            edit_field.selectionStringFocusPosition = e;       // 焦点 = 终点（e>s 时形成选区）
            edit_field.ForceLabelUpdate();
        }

        private IEnumerator CoRestoreSelection(string expect_text, int start, int len)
        {
            yield return null;
            if (edit_field == null)
                yield break;
            if (edit_field.text != expect_text)
                yield break;   // 期间用户已改过文本，不再覆盖其光标位置
            ApplyCaret(start, len);
        }

        /// <summary>预览行实时渲染（TMP 自身容错，未闭合/非法标签不会崩溃、不弹窗）</summary>
        private void RefreshPreview()
        {
            if (preview_text != null && edit_field != null)
                preview_text.text = edit_field.text;
        }

        // ================= 子面板 =================

        private void SetSubPanelVisible(RectTransform panel, bool visible)
        {
            if (palette_panel != null)
                palette_panel.gameObject.SetActive(visible && panel == palette_panel);
            if (size_panel != null)
                size_panel.gameObject.SetActive(visible && panel == size_panel);
            if (visible && panel != null)
                panel.SetAsLastSibling();
        }

        private void HideSubPanels()
        {
            if (palette_panel != null)
                palette_panel.gameObject.SetActive(false);
            if (size_panel != null)
                size_panel.gameObject.SetActive(false);
        }

        // ================= UI 构建辅助 =================

        /// <summary>字体解析优先级：本组件指定 → UIFonts 全局 → font_resolver 外部解析器 → TMP 默认。
        /// 绝不主动使用 TMP_Settings.defaultFontAsset（LiberationSans 无中文字形，会显示成方块）。</summary>
        public TMP_FontAsset ResolveFont()
        {
            if (font != null)
                return font;
            if (UIFonts.font_asset != null)
                return UIFonts.font_asset;
            if (font_resolver != null)
            {
                TMP_FontAsset f = null;
                try { f = font_resolver(); }
                catch { f = null; }
                if (f != null)
                {
                    UIFonts.font_asset = f;   // 首次解析到就固化为全局字体，后续新界面直接复用
                    return f;
                }
            }

            TMP_FontAsset cn = UIFonts.GetChineseFont();   // 兜底：现做动态中文字体，避免默认字体渲染成方块
            if (cn != null)
                return cn;

            try { return TMP_Settings.defaultFontAsset; }
            catch { return null; }
        }

        /// <summary>把解析到的字体重套到弹框内所有 TMP 文本与输入框（含未激活子物体）</summary>
        public void ApplyFonts()
        {
            TMP_FontAsset f = ResolveFont();
            if (f == null)
                return;
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
                UIFonts.SafeSetFont(texts[i], f);
        }

        private TextMeshProUGUI MakeText(string name, Transform parent, string content, int size, TextAlignmentOptions align, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt, 0f);

            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            UIFonts.SafeSetFont(t, ResolveFont());
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.richText = false;
            t.raycastTarget = false;
            return t;
        }

        private TextMeshProUGUI MakeTextTop(string name, RectTransform parent, string content, int size, TextAlignmentOptions align, Color color,
            float left, float top, float right, float height)
        {
            TextMeshProUGUI t = MakeText(name, parent, content, size, align, color);
            SetTopStretch(t.rectTransform, left, top, right, height);
            return t;
        }

        private Button MakeButton(string name, Transform parent, string label, int font_size, Action onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 40f);

            Image img = go.GetComponent<Image>();
            img.color = ButtonColor;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.05f;
            btn.colors = cb;

            TextMeshProUGUI t = MakeText("Text", rt, label, font_size, TextAlignmentOptions.Center, Color.white);
            Stretch(t.rectTransform, 4f);
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>建一个多行/单行 TMP_InputField（richText=false，标签以源码形式编辑）</summary>
        private TMP_InputField MakeInputField(RectTransform root, bool multiline, string placeholder_text, int font_size)
        {
            Image bg = AddImage(root, FieldColor);

            RectTransform area = MakeRect("Text Area", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(area, 8f);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = MakeText("Text", area, "", font_size, TextAlignmentOptions.TopLeft, Color.white);
            Stretch(text.rectTransform, 0f);
            text.enableWordWrapping = true;
            text.richText = false;

            TextMeshProUGUI ph = MakeText("Placeholder", area, placeholder_text, font_size, TextAlignmentOptions.TopLeft, new Color(1f, 1f, 1f, 0.35f));
            Stretch(ph.rectTransform, 0f);
            ph.enableWordWrapping = true;
            ph.richText = false;
            ph.fontStyle = FontStyles.Italic;

            TMP_InputField field = root.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = bg;
            field.richText = false;                  // 源码模式：标签按普通文本显示、可手改
            field.isRichTextEditingAllowed = false;  // 光标/选区按字符索引换算，与 richText=false 保持一致
            // 必须显式指定 viewport：运行时创建的 TMP_InputField 不会自动解析 m_TextViewport，
            // 为空时拖动选区会在 MouseDragOutsideRect 里的 RectTransformUtility 抛 NullReferenceException
            field.textViewport = area;
            // 关闭「获焦即全选」：默认 true 会在点格式按钮后重新激活输入框时把整段文本全选中，
            // 冲掉我们刚恢复的选区；也会让点击文本框时直接全选，不适合源码编辑
            field.onFocusSelectAll = false;
            field.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            field.text = "";
            return field;
        }

        /// <summary>面板/子面板加一个空 Button 吞掉点击，避免穿透到遮罩触发关闭</summary>
        private static void AddClickBlocker(RectTransform rt, Image img)
        {
            Button btn = rt.gameObject.GetComponent<Button>();
            if (btn == null)
                btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
        }

        private static Image AddImage(RectTransform rt, Color color)
        {
            Image img = rt.gameObject.GetComponent<Image>();
            if (img == null)
                img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static RectTransform MakeRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static void Stretch(RectTransform rt, float margin)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }

        private static void SetTopStretch(RectTransform rt, float left, float top, float right, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-(left + right), height);
            rt.anchoredPosition = new Vector2((left - right) * 0.5f, -top);
        }

        private static void SetTopLeft(RectTransform rt, float left, float top, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(left, -top);
        }

        private static void SetTopRight(RectTransform rt, float right, float top, float width, float height)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-right, -top);
        }

        private static void SetBottomRight(RectTransform rt, float right, float bottom, float width, float height)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-right, bottom);
        }
    }
}
