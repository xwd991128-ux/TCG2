using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;

namespace TcgEngine.UI
{
    /// <summary>
    /// 对局前「构筑规则」选择弹框：选构筑环境（标准/乱斗）+ 勾选可选自定义规则。
    ///
    /// 规则数据与判定全部来自数据层（DeckFormatData / DeckValidator），本类**只做界面**：
    ///   · 卡组在当前「环境 + 勾选」下不合法时，「开始」不可点，并把中文错误**逐条**列在下方；
    ///   · 点「开始」才把选择写进 GameClient.game_settings（deck_format_id / deck_optional_rules），
    ///     随开局设置一起发给服务端 —— 保证客户端与服务端用同一套规则校验。
    ///
    /// 写法与 VFXEditorPopup 保持一致：静态 instance + Create(Transform) 幂等 + EnsureBuilt 只建一次 +
    /// 常驻复用（不 Destroy、不 DontDestroyOnLoad），靠 Show/Hide 控制显隐。
    /// 配色统一取 UITheme，字体统一走 UIFonts。
    /// </summary>
    public class DeckFormatPopupUI : UIPanel
    {
        private const float PanelWidth = 520f;
        private const float PanelHeight = 620f;
        private const float OptionRowHeight = 30f;     //与项目"多选弹层"的选项行高一致
        private const float SectionRowHeight = 28f;
        private const float ErrorRowHeight = 26f;
        private const float ListPadding = 12f;
        private const string Title = "构筑规则";
        private const string NoFormatRow = "标准（默认：主卡 30 / 同名 ≤2）";

        private static readonly Color RowSelected = new Color(0.2f, 0.55f, 0.85f, 0.95f);
        private static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color BtnConfirmOn = new Color(0.45f, 0.85f, 0.5f, 0.55f);
        private static readonly Color BtnConfirmOff = new Color(1f, 1f, 1f, 0.12f);

        private static DeckFormatPopupUI instance;

        private bool built;
        private RectTransform list_root;
        private TMP_Text txt_summary;
        private Button btn_confirm;
        private Image btn_confirm_img;
        private Button btn_back;
        private Image btn_back_img;

        private UserDeckData deck;
        private UserDeckData ai_deck;      //对手（AI）卡组：也要按同一套规则校验，否则服务端会拒绝它
        private Action on_confirm;
        private DeckFormatData cur_format;                 //null = 用 GameplayData 默认（标准行为）
        private readonly List<string> toggled = new List<string>();

        /// <summary>取（首次则建）弹框实例；context 用于定位所在 Canvas（不内部 Find）。</summary>
        public static DeckFormatPopupUI Create(Transform context)
        {
            if (instance != null)
                return instance;
            GameObject go = new GameObject("DeckFormatPopup", typeof(RectTransform), typeof(CanvasGroup));
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            go.transform.SetParent(canvas != null ? canvas.transform : context, false);
            SetStretch(go.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            go.transform.SetAsLastSibling();
            instance = go.AddComponent<DeckFormatPopupUI>();
            instance.EnsureBuilt();
            instance.Hide(true);
            return instance;
        }

        /// <summary>打开弹框：deck 为要校验的玩家卡组；点「开始」时回调 on_confirm（选择已写入对局设置）。</summary>
        public void Open(UserDeckData target_deck, UserDeckData target_ai_deck, Action confirm)
        {
            EnsureBuilt();
            deck = target_deck;
            ai_deck = target_ai_deck;
            on_confirm = confirm;
            cur_format = DefaultFormat();
            toggled.Clear();
            Rebuild();
            Show();
            UIFonts.ApplyResolved(gameObject);
        }

        public override void Show(bool instant = false)
        {
            EnsureBuilt();
            base.Show(instant);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = true;
                canvas_group.interactable = true;
            }
        }

        public override void Hide(bool instant = false)
        {
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }
            base.Hide(instant);
        }

        //============================ 构建 ============================

        private void EnsureBuilt()
        {
            if (built)
                return;

            //清掉上次构建中途失败留下的半成品（它会挡住点击，且字段为 null 时永远无法重建）
            Transform stale = transform.Find("Mask");
            if (stale != null)
                Destroy(stale.gameObject);

            BuildMask();
            RectTransform panel = BuildPanel();
            BuildScroll(panel);
            BuildBottom(panel);

            built = true;   //全部构建成功后才置位
        }

        private void BuildMask()
        {
            GameObject mask_go = new GameObject("Mask", typeof(RectTransform));
            RectTransform mrt = mask_go.GetComponent<RectTransform>();
            mrt.SetParent(transform, false);
            SetStretch(mrt, 0f, 0f, 0f, 0f);
            Image mask = mask_go.AddComponent<Image>();
            mask.color = UITheme.MaskPopup;
            mask.raycastTarget = true;
            Button mask_btn = mask_go.AddComponent<Button>();
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.targetGraphic = mask;
            mask_btn.onClick.AddListener(() => Hide());   //纯选择弹层：点遮罩=取消
        }

        private RectTransform BuildPanel()
        {
            GameObject panel_go = new GameObject("Panel", typeof(RectTransform));
            RectTransform prt = panel_go.GetComponent<RectTransform>();
            prt.SetParent(transform, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            float h = Mathf.Min(PanelHeight, Mathf.Max(360f, Screen.height - 80f));
            prt.sizeDelta = new Vector2(PanelWidth, h);

            Image bg = panel_go.AddComponent<Image>();
            bg.color = UITheme.BgPopup;
            Button swallow = panel_go.AddComponent<Button>();   //吞点击，避免穿透到遮罩把弹层关掉
            swallow.transition = Selectable.Transition.None;
            swallow.targetGraphic = bg;

            TMP_Text t = MakeText("Title", prt, Title, UITheme.FontSection, TextAlignmentOptions.MidlineLeft);
            t.color = UITheme.TextTitle;
            Anchor(t.rectTransform, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 34f, 16f, -6f);

            Button close = MakeButton("Close", prt, "×", () => Hide());
            Anchor(close.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 1f, 1f, 34f, 34f, -6f, -6f);
            return prt;
        }

        private void BuildScroll(RectTransform panel)
        {
            GameObject scroll_go = new GameObject("Scroll", typeof(RectTransform));
            RectTransform srt = scroll_go.GetComponent<RectTransform>();
            srt.SetParent(panel, false);
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(ListPadding, 58f);
            srt.offsetMax = new Vector2(-ListPadding, -46f);
            ScrollRect scroll = scroll_go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            GameObject view_go = new GameObject("Viewport", typeof(RectTransform));
            RectTransform vrt = view_go.GetComponent<RectTransform>();
            vrt.SetParent(srt, false);
            SetStretch(vrt, 0f, 0f, 0f, 0f);
            view_go.AddComponent<RectMask2D>();
            scroll.viewport = vrt;

            GameObject content_go = new GameObject("Content", typeof(RectTransform));
            RectTransform crt = content_go.GetComponent<RectTransform>();
            crt.SetParent(vrt, false);
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            VerticalLayoutGroup vlg = content_go.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;   //★必须为 true，否则行上的 LayoutElement 高度会被忽略
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            ContentSizeFitter fitter = content_go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            list_root = crt;
        }

        private void BuildBottom(RectTransform panel)
        {
            txt_summary = MakeText("Summary", panel, "", UITheme.FontSmall, TextAlignmentOptions.MidlineLeft);
            txt_summary.color = UITheme.TextDim;
            Anchor(txt_summary.rectTransform, 0f, 0f, 1f, 0f, 0f, 0f, -232f, 34f, 14f, 14f);

            btn_confirm = MakeButton("Confirm", panel, "开始", OnClickConfirm);
            btn_confirm_img = btn_confirm.GetComponent<Image>();
            Anchor(btn_confirm.GetComponent<RectTransform>(), 1f, 0f, 1f, 0f, 1f, 0f, 100f, 34f, -14f, 14f);

            //不合法时的第二条出路：直接跳回组卡界面并定位到出错的那张卡（比"自己回去找"省事）
            btn_back = MakeButton("BackToEdit", panel, "返回调整", OnClickBackToEdit);
            btn_back_img = btn_back.GetComponent<Image>();
            Anchor(btn_back.GetComponent<RectTransform>(), 1f, 0f, 1f, 0f, 1f, 0f, 100f, 34f, -122f, 14f);
        }

        //============================ 刷新 ============================

        /// <summary>重建整份列表（环境行 + 可选规则行 + 校验结果行）。只在用户操作时调用，不每帧跑。</summary>
        private void Rebuild()
        {
            ClearList();
            BuildFormatRows();
            BuildRuleRows();
            BuildErrorRows();
            RefreshConfirmButton();
        }

        private void ClearList()
        {
            //倒序 + 先 SetActive(false)：Destroy 是延迟到帧末的，避免同帧再建时旧行还可见（重复区块）
            for (int i = list_root.childCount - 1; i >= 0; i--)
            {
                Transform child = list_root.GetChild(i);
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private void BuildFormatRows()
        {
            //标题里直接写"点一行切换"：选项行与背景对比度低，不提示的话很容易看不出这里能选
            CreateSectionRow("构筑环境（点一行即选中）");

            DeckFormatData std = DeckFormatData.GetStandard();

            //没有 standard 资产时，明确给一行「标准（默认）」：它对应 GameplayData 的默认 30/2
            if (std == null)
            {
                CreateOptionRow(NoFormatRow, cur_format == null, () =>
                {
                    cur_format = null;
                    toggled.Clear();
                    Rebuild();
                }, false);
            }
            else
            {
                //★ 默认选中的那一项必须排在**最前面**：按 Resources.LoadAll 的顺序它常被排在末尾，
                //  玩家一打开只看到一堆乱斗，会以为"没有标准/不知道该选哪儿"。
                CreateFormatRow(std);
            }

            foreach (DeckFormatData f in DeckFormatData.GetAll())
            {
                if (f == null || f == std)
                    continue;
                CreateFormatRow(f);
            }
        }

        /// <summary>一个环境选项行（点一行即选中；选中行用主题色高亮 + 末尾显式写「← 当前选中」）</summary>
        private void CreateFormatRow(DeckFormatData f)
        {
            DeckFormatData captured = f;
            bool on = cur_format == captured && cur_format != null;
            CreateOptionRow(FormatLabel(f) + "   " + FormatLimitText(f) + (on ? "    ← 当前选中" : ""),
                on, () =>
                {
                    cur_format = captured;
                    toggled.Clear();     //换环境后旧的勾选不再适用
                    Rebuild();
                }, false);
        }

        private void BuildRuleRows()
        {
            if (cur_format == null || cur_format.optional_modifiers == null || cur_format.optional_modifiers.Count == 0)
                return;
            CreateSectionRow("可选规则（勾选后生效）");
            foreach (DeckModifier mod in cur_format.optional_modifiers)
            {
                if (mod == null || string.IsNullOrEmpty(mod.id))
                    continue;
                DeckModifier captured = mod;
                bool on = toggled.Contains(captured.id);
                CreateOptionRow(RuleLabel(captured), on, () =>
                {
                    if (on)
                        toggled.Remove(captured.id);
                    else
                        toggled.Add(captured.id);
                    Rebuild();
                }, true);
            }
        }

        private void BuildErrorRows()
        {
            List<DeckError> errors = DeckValidator.Validate(deck, cur_format, toggled);
            List<DeckError> ai_errors = AiErrors();
            int total = errors.Count + ai_errors.Count;
            CreateSectionRow(total == 0 ? "校验结果：合法" : "校验结果：不合法（" + total + " 条）");
            if (total == 0)
            {
                CreateErrorRow("· 双方卡组都符合当前构筑规则。", UITheme.Accent);
                return;
            }
            foreach (DeckError e in errors)
                CreateErrorRow("· " + e.message, UITheme.Danger);
            foreach (DeckError e in ai_errors)
                CreateErrorRow("· （对手）" + e.message, UITheme.Danger);
        }

        /// <summary>
        /// 对手（AI）卡组也要按同一套规则校验：否则服务端会拒绝它（玩家只看到"一直连不上"）。
        /// 单机下 AI 卡组由玩家自选，挡住它比让它拖死开局更友好。
        /// </summary>
        private List<DeckError> AiErrors()
        {
            if (ai_deck == null)
            {
                //空值也算一条错误：否则"没选对手卡组"时不拦，开局会被服务端拒绝，玩家只看到连不上
                List<DeckError> none = new List<DeckError>();
                none.Add(new DeckError("没有选择对手卡组（AI）。"));
                return none;
            }
            return DeckValidator.Validate(ai_deck, cur_format, toggled);
        }

        private void RefreshConfirmButton()
        {
            bool ok = DeckValidator.Validate(deck, cur_format, toggled).Count == 0 && AiErrors().Count == 0;
            if (btn_confirm != null)
                btn_confirm.interactable = ok;
            if (btn_confirm_img != null)
                btn_confirm_img.color = ok ? BtnConfirmOn : BtnConfirmOff;
            if (btn_back != null)
                btn_back.interactable = !ok;      //合法时没有"要调整的东西"
            if (btn_back_img != null)
                btn_back_img.color = ok ? BtnConfirmOff : UITheme.Ctrl;
            if (txt_summary != null)
                txt_summary.text = ok ? "卡组合法，可以开始" : "卡组不合法，「开始」已禁用";
            if (txt_summary != null)
                txt_summary.color = ok ? UITheme.TextDim : UITheme.Danger;
        }

        //============================ 行工厂（尺寸/配色对齐既有弹层）============================

        private void CreateSectionRow(string text)
        {
            GameObject row = new GameObject("Section", typeof(RectTransform));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(list_root, false);
            SetRowHeight(row, SectionRowHeight);
            TMP_Text t = MakeText("Label", rt, text, UITheme.FontSmall, TextAlignmentOptions.MidlineLeft);
            t.color = UITheme.TextDim;
            t.raycastTarget = false;
            SetStretch(t.rectTransform, 12f, 0f, 12f, 0f);
        }

        /// <param name="multi">true=多选样式（显示 √/□）；false=单选样式</param>
        private void CreateOptionRow(string label, bool on, Action on_click, bool multi)
        {
            GameObject row = new GameObject("Opt", typeof(RectTransform));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(list_root, false);
            SetRowHeight(row, OptionRowHeight);
            Image bg = row.AddComponent<Image>();
            bg.color = on ? RowSelected : RowNormal;
            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => on_click());

            string prefix = multi ? (on ? "√ " : "□ ") : (on ? "● " : "○ ");
            TMP_Text t = MakeText("Label", rt, prefix + label, UITheme.FontBody, TextAlignmentOptions.MidlineLeft);
            SetStretch(t.rectTransform, 12f, 0f, 12f, 0f);
            t.raycastTarget = false;
        }

        private void CreateErrorRow(string text, Color color)
        {
            GameObject row = new GameObject("Err", typeof(RectTransform));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(list_root, false);
            SetRowHeight(row, ErrorRowHeight);
            TMP_Text t = MakeText("Label", rt, text, UITheme.FontSmall, TextAlignmentOptions.MidlineLeft);
            t.color = color;
            t.enableWordWrapping = true;      //错误文案较长，允许折行
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            SetStretch(t.rectTransform, 12f, 0f, 12f, 0f);
        }

        private static void SetRowHeight(GameObject row, float height)
        {
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
        }

        //============================ 操作 ============================

        private void OnClickConfirm()
        {
            if (btn_confirm != null && !btn_confirm.interactable)
                return;
            GameClient.game_settings.deck_format_id = cur_format != null ? cur_format.id : "";
            GameClient.game_settings.deck_optional_rules = DeckRuleToggles.Join(toggled);
            Hide();
            if (on_confirm != null)
                on_confirm();
        }

        /// <summary>「返回调整」：切到组卡面板 → 打开该卡组的编辑视图 → 定位到第一条"能落到具体卡"的错误</summary>
        private void OnClickBackToEdit()
        {
            UserDeckData target = deck;
            string tid = FirstErrorCardTid();
            Hide();

            CollectionPanel panel = CollectionPanel.Get();
            if (panel == null)
            {
                Debug.LogWarning("[构筑规则] 找不到组卡面板，无法跳转。");
                return;
            }
            if (!TabButton.ActivatePanel(panel))
                panel.Show();     //没绑到选项卡（或不同组）时退化为直接显示
            panel.OpenDeckForEdit(target);
            if (!string.IsNullOrEmpty(tid))
                panel.FocusDeckCard(tid);
        }

        /// <summary>第一条"能定位到具体卡"的错误（整组级错误没有 card_tid，跳过）</summary>
        private string FirstErrorCardTid()
        {
            List<DeckError> errors = DeckValidator.Validate(deck, cur_format, toggled);
            foreach (DeckError e in errors)
            {
                if (e != null && !string.IsNullOrEmpty(e.card_tid))
                    return e.card_tid;
            }
            return null;
        }

        /// <summary>
        /// 默认环境：只认「标准环境资产」，没有就返回 null（= 走 GameplayData 的默认 30/2）。
        /// ★ 这里**绝不能**退化成"取列表第一个"：那会让玩家一打开弹框就默默处在某个乱斗里
        ///   （例如"主卡 40 张"），他自己的 30 张卡组反而显示"不合法"，非常难理解。
        /// </summary>
        private static DeckFormatData DefaultFormat()
        {
            return DeckFormatData.GetStandard();
        }

        private static string FormatLabel(DeckFormatData f)
        {
            return string.IsNullOrEmpty(f.title) ? f.id : f.title;
        }

        private static string FormatLimitText(DeckFormatData f)
        {
            List<string> parts = new List<string> { "主卡 " + f.deck_size };
            if (f.max_copies > 0)
                parts.Add("同名 ≤" + f.max_copies);
            if (f.max_legendary > 0)
                parts.Add("传说 ≤" + f.max_legendary);
            return string.Join("｜", parts);
        }

        private static string RuleLabel(DeckModifier mod)
        {
            string title = string.IsNullOrEmpty(mod.title) ? mod.id : mod.title;
            return string.IsNullOrEmpty(mod.desc) ? title : title + " —— " + mod.desc;
        }

        //============================ 小工具 ============================

        private static TMP_Text MakeText(string name, Transform parent, string txt, int size, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            SetStretch(rt, 0f, 0f, 0f, 0f);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyFont(t);      //统一字体管线（不用旧版 uGUI Text，避免发糊/缺字）
            t.text = txt;
            t.fontSize = size;
            t.alignment = align;
            t.color = UITheme.TextBody;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static Button MakeButton(string name, Transform parent, string label, Action on_click)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = UITheme.Ctrl;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);
            if (!string.IsNullOrEmpty(label))
            {
                TMP_Text t = MakeText("Text", go.transform, label, UITheme.FontBody, TextAlignmentOptions.Center);
                t.raycastTarget = false;
            }
            if (on_click != null)
                btn.onClick.AddListener(() => on_click());
            return btn;
        }

        private static void SetStretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>按锚点给固定尺寸/偏移（锚点相同时 pivot 与该角对齐，便于"右上角 ×"这类摆放）</summary>
        private static void Anchor(RectTransform rt, float amin_x, float amin_y, float amax_x, float amax_y,
            float pivot_x, float pivot_y, float width, float height, float pos_x, float pos_y)
        {
            rt.anchorMin = new Vector2(amin_x, amin_y);
            rt.anchorMax = new Vector2(amax_x, amax_y);
            rt.pivot = new Vector2(pivot_x, pivot_y);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(pos_x, pos_y);
        }
    }
}
