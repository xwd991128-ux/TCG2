using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;

namespace TcgEngine.UI
{
    /// <summary>
    /// 「额外区」编辑弹层：把卡在主卡区与额外区（如 ETC 的「乐队」）之间搬来搬去。
    ///
    /// 为什么是弹层：组卡侧栏（449.72 宽）已被卡表与底部 ◀/SAVE/2/30 占满，
    /// 实测底部最大空隙仅 7.8 单位、纵向也没有空条带 —— 常驻区块放不下。
    ///
    /// 设计要点（与既有弹层规格一致，不另起风格）：
    ///   · 遮罩 UITheme.MaskPopup、面板 UITheme.BgPopup、行高 30 + childControlHeight、
    ///     文字一律 TMP + UIFonts、颜色全部取 UITheme；
    ///   · 弹出/关闭走 UIPanel 的淡入淡出（含遮罩，因为同一个 CanvasGroup）；
    ///   · 层级：SetAsLastSibling；焦点：打开时 CanvasGroup 可交互、关闭时禁掉，遮罩挡住下层点击；
    ///   · Esc 关闭（移动端不响应该键，靠右上角 × 与遮罩点击）；
    ///   · 状态一致：空态 / 成功（● 合法）/ 报错（中文逐条，含区名与张数）。
    ///
    /// 编辑是**即时生效**的（直接改传入的缓冲），所以点遮罩/按 Esc 关掉不会丢内容 ——
    /// 这也符合项目里"纯编辑类弹层可点遮罩关闭"的约定。
    /// </summary>
    public class DeckZonePopupUI : UIPanel
    {
        private const float PanelWidth = 560f;
        private const float PanelHeight = 640f;
        private const float RowHeight = 30f;      //与项目"多选弹层"选项行一致
        private const float HeadHeight = 30f;
        private const float ListPadding = 12f;
        private const string Title = "额外区";

        private static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color RowDim = new Color(1f, 1f, 1f, 0.04f);

        private static DeckZonePopupUI instance;

        private bool built;
        private RectTransform list_root;
        private TMP_Text txt_footer;

        private List<UserCardData> main_cards;          //主卡区缓冲（与组卡界面共用同一个 List）
        private List<UserDeckZone> zones;               //额外区缓冲（与组卡界面共用同一个 List）
        private Action on_changed;
        private string picker_zone_id;                  //非空 = 正在为该区挑卡

        public static DeckZonePopupUI Create(Transform context)
        {
            if (instance != null)
                return instance;
            GameObject go = new GameObject("DeckZonePopup", typeof(RectTransform), typeof(CanvasGroup));
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            go.transform.SetParent(canvas != null ? canvas.transform : context, false);
            SetStretch(go.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            go.transform.SetAsLastSibling();
            instance = go.AddComponent<DeckZonePopupUI>();
            instance.EnsureBuilt();
            instance.Hide(true);
            return instance;
        }

        /// <summary>打开弹层。main_cards / zones 是**组卡界面正在编辑的那两个缓冲**，本弹层直接改它们。</summary>
        public void Open(List<UserCardData> main_cards, List<UserDeckZone> zones, Action on_changed)
        {
            EnsureBuilt();
            this.main_cards = main_cards;
            this.zones = zones;
            this.on_changed = on_changed;
            picker_zone_id = null;
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

        protected override void Update()
        {
            base.Update();
            //Esc 关闭（移动端没有这个键，用右上角 × 或点遮罩）
            if (IsVisible() && !GameTool.IsMobile() && Input.GetKeyDown(KeyCode.Escape))
                Hide();
        }

        //============================ 构建（只建一次）============================

        private void EnsureBuilt()
        {
            if (built)
                return;
            Transform stale = transform.Find("Mask");
            if (stale != null)
                Destroy(stale.gameObject);   //清掉上次构建失败留下的半成品

            BuildMask();
            RectTransform panel = BuildPanel();
            BuildScroll(panel);
            BuildBottom(panel);
            built = true;
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
            mask_btn.onClick.AddListener(() => Hide());   //编辑即时生效，点遮罩关掉不丢内容
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
            float h = Mathf.Min(PanelHeight, Mathf.Max(360f, Screen.height - 80f));   //移动端/小屏不顶出屏幕
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
            srt.offsetMin = new Vector2(ListPadding, 56f);
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
            vlg.childControlHeight = true;   //★必须 true，否则行上的 LayoutElement 高度会被忽略
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            ContentSizeFitter fitter = content_go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            list_root = crt;
        }

        private void BuildBottom(RectTransform panel)
        {
            txt_footer = MakeText("Footer", panel, "", UITheme.FontSmall, TextAlignmentOptions.MidlineLeft);
            Anchor(txt_footer.rectTransform, 0f, 0f, 1f, 0f, 0f, 0f, -110f, 34f, 14f, 12f);

            Button ok = MakeButton("Ok", panel, "完成", () => Hide());
            Anchor(ok.GetComponent<RectTransform>(), 1f, 0f, 1f, 0f, 1f, 0f, 90f, 34f, -14f, 12f);
        }

        //============================ 列表内容 ============================

        private void Rebuild()
        {
            ClearList();
            ResolvedDeckRules rules = CurrentRules();
            if (rules == null || rules.zones.Count == 0)
            {
                CreateSectionRow("当前构筑环境没有额外区");
                CreateInfoRow("在「构筑规则」里选带额外区的环境，或勾选相关可选规则后，这里会出现对应的区。", UITheme.TextDim);
            }
            else
            {
                foreach (DeckZone zone in rules.zones)
                {
                    if (zone == null || string.IsNullOrEmpty(zone.id))
                        continue;
                    BuildZone(zone);
                }
            }
            RefreshFooter();
        }

        private void BuildZone(DeckZone zone)
        {
            UserDeckZone data = GetOrCreateZone(zone.id);
            int count = CountZone(data);

            string title = "「" + ZoneTitle(zone) + "」  " + count + "/" + zone.max_count;
            List<string> limits = new List<string>();
            if (zone.min_count > 0)
                limits.Add("至少 " + zone.min_count);
            if (zone.per_card_max > 0)
                limits.Add("每卡最多 " + zone.per_card_max);
            if (limits.Count > 0)
                title += "（" + string.Join("，", limits) + "）";
            CreateSectionRow(title);

            if (count == 0)
                CreateInfoRow("（还没有卡，用下面的「+ 添加卡」放进来）", UITheme.TextDim);
            else
            {
                foreach (UserCardData uc in data.cards)
                {
                    if (uc == null || string.IsNullOrEmpty(uc.tid) || uc.quantity <= 0)
                        continue;
                    UserCardData captured = uc;
                    CreateActionRow(CardName(uc.tid) + " ×" + uc.quantity, "← 主卡", RowNormal, () =>
                    {
                        MoveOneToMain(zone, captured.tid);
                        Rebuild();
                        NotifyChanged();
                    });
                }
            }

            bool picking = picker_zone_id == zone.id;
            CreateActionRow(picking ? "▲ 收起可选卡" : "+ 添加卡到「" + ZoneTitle(zone) + "」",
                null, RowDim, () =>
                {
                    picker_zone_id = picking ? null : zone.id;
                    Rebuild();
                });
            if (picking)
                BuildPicker(zone, data);
        }

        /// <summary>可选卡：只列玩家拥有的卡，且本区还剩空间、未超每卡上限；不满足的置灰并给原因</summary>
        private void BuildPicker(DeckZone zone, UserDeckZone data)
        {
            UserData udata = Authenticator.Get().UserData;
            if (udata == null)
            {
                CreateInfoRow("读取账号数据中…", UITheme.TextDim);
                return;
            }

            VariantData variant = VariantData.GetDefault();
            int shown = 0;
            int total = CountZone(data);
            foreach (CardData card in CardData.GetAll())
            {
                if (card == null || string.IsNullOrEmpty(card.id))
                    continue;
                if (udata.GetCardQuantity(card, variant) <= 0)
                    continue;   //没拥有的卡不进选择列表（与卡池一致）

                int in_zone = CountCardInZone(data, card.id);
                bool zone_full = total >= zone.max_count;
                bool card_full = zone.per_card_max > 0 && in_zone >= zone.per_card_max;
                string blocked = zone_full ? "（本区已满）" : (card_full ? "（已达每卡上限）" : null);

                CardData captured = card;
                CreateActionRow((blocked != null ? "× " : "+ ") + CardName(card.id)
                    + (in_zone > 0 ? "（区内 " + in_zone + "）" : "") + (blocked != null ? blocked : ""),
                    null, blocked != null ? RowDim : RowNormal,
                    () =>
                    {
                        if (zone_full || card_full)
                            return;
                        AddOneToZone(zone, captured.id);
                        Rebuild();
                        NotifyChanged();
                    });
                shown++;
            }

            if (shown == 0)
                CreateInfoRow("没有可加入的卡（需要有该卡、且本区未满）。", UITheme.TextDim);
        }

        private void RefreshFooter()
        {
            if (txt_footer == null)
                return;
            List<DeckError> errors = ValidateZones();
            if (errors.Count == 0)
            {
                txt_footer.text = "● 额外区符合当前构筑规则";
                txt_footer.color = UITheme.Accent;
                return;
            }
            List<string> lines = new List<string>();
            foreach (DeckError e in errors)
                lines.Add("· " + e.message);
            txt_footer.text = "额外区有 " + errors.Count + " 处不符：" + string.Join("  ", lines);
            txt_footer.color = UITheme.Danger;
        }

        //============================ 数据操作（直接改缓冲）============================

        private void AddOneToZone(DeckZone zone, string tid)
        {
            UserDeckZone data = GetOrCreateZone(zone.id);
            UserCardData in_zone = FindCard(data.cards, tid);
            if (in_zone != null)
            {
                in_zone.quantity++;
            }
            else
            {
                UserCardData nuc = new UserCardData(tid, VariantData.GetDefault().id);
                nuc.quantity = 1;
                List<UserCardData> list = new List<UserCardData>(data.cards);
                list.Add(nuc);
                data.cards = list.ToArray();
            }
            //同一张卡从主卡区扣 1：否则会出现"同一张卡同时算在两处"，统计与校验都会翻倍
            RemoveOneFromMain(tid);
        }

        private void MoveOneToMain(DeckZone zone, string tid)
        {
            UserDeckZone data = GetOrCreateZone(zone.id);
            UserCardData in_zone = FindCard(data.cards, tid);
            if (in_zone == null || in_zone.quantity <= 0)
                return;
            in_zone.quantity--;
            if (in_zone.quantity <= 0)
            {
                List<UserCardData> list = new List<UserCardData>(data.cards);
                list.Remove(in_zone);
                data.cards = list.ToArray();
            }
            AddOneToMain(tid);
        }

        private void RemoveOneFromMain(string tid)
        {
            if (main_cards == null)
                return;
            for (int i = main_cards.Count - 1; i >= 0; i--)
            {
                UserCardData uc = main_cards[i];
                if (uc == null || uc.tid != tid)
                    continue;
                uc.quantity--;
                if (uc.quantity <= 0)
                    main_cards.RemoveAt(i);
                return;
            }
        }

        private void AddOneToMain(string tid)
        {
            if (main_cards == null)
                return;
            foreach (UserCardData uc in main_cards)
            {
                if (uc != null && uc.tid == tid)
                {
                    uc.quantity++;
                    return;
                }
            }
            UserCardData nuc = new UserCardData(tid, VariantData.GetDefault().id);
            nuc.quantity = 1;
            main_cards.Add(nuc);
        }

        private UserDeckZone GetOrCreateZone(string zone_id)
        {
            foreach (UserDeckZone z in zones)
            {
                if (z != null && z.zone_id == zone_id)
                    return z;
            }
            UserDeckZone created = new UserDeckZone();
            created.zone_id = zone_id;
            zones.Add(created);
            return created;
        }

        private static UserCardData FindCard(UserCardData[] cards, string tid)
        {
            if (cards == null)
                return null;
            foreach (UserCardData uc in cards)
            {
                if (uc != null && uc.tid == tid)
                    return uc;
            }
            return null;
        }

        private static int CountZone(UserDeckZone data)
        {
            if (data == null || data.cards == null)
                return 0;
            int total = 0;
            foreach (UserCardData uc in data.cards)
            {
                if (uc != null && uc.quantity > 0)
                    total += uc.quantity;
            }
            return total;
        }

        private static int CountCardInZone(UserDeckZone data, string tid)
        {
            UserCardData uc = FindCard(data != null ? data.cards : null, tid);
            return uc != null ? uc.quantity : 0;
        }

        /// <summary>用当前两个缓冲拼一个用于校验的对象，再取**与额外区相关**的错误（整组级错误不在这里重复报）</summary>
        private List<DeckError> ValidateZones()
        {
            ResolvedDeckRules rules = CurrentRules();
            if (rules == null)
                return new List<DeckError>();
            UserDeckData deck = new UserDeckData();
            deck.cards = main_cards != null ? main_cards.ToArray() : new UserCardData[0];
            deck.zones = zones != null ? zones.ToArray() : new UserDeckZone[0];
            List<DeckError> all = DeckValidator.Validate(deck, CurrentFormat(), Toggles());
            List<DeckError> zone_errors = new List<DeckError>();
            foreach (DeckError e in all)
            {
                if (e != null && !string.IsNullOrEmpty(e.zone_id))
                    zone_errors.Add(e);
            }
            return zone_errors;
        }

        private static ResolvedDeckRules CurrentRules()
        {
            UserDeckData deck = new UserDeckData();
            return DeckRulesResolver.Resolve(deck, CurrentFormat(), Toggles());
        }

        private static DeckFormatData CurrentFormat()
        {
            DeckFormatData format = DeckFormatData.Get(GameClient.game_settings.deck_format_id);
            return format != null ? format : DeckFormatData.GetStandard();
        }

        private static List<string> Toggles()
        {
            return DeckRuleToggles.Split(GameClient.game_settings.deck_optional_rules);
        }

        private void NotifyChanged()
        {
            if (on_changed != null)
                on_changed();
        }

        //============================ 行工厂 ============================

        private void ClearList()
        {
            for (int i = list_root.childCount - 1; i >= 0; i--)
            {
                Transform child = list_root.GetChild(i);
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private void CreateSectionRow(string text)
        {
            GameObject row = NewRow(HeadHeight);
            TMP_Text t = MakeText("Label", row.transform, text, UITheme.FontSmall, TextAlignmentOptions.MidlineLeft);
            t.color = UITheme.TextDim;
            t.raycastTarget = false;
            SetStretch(t.rectTransform, 12f, 0f, 12f, 0f);
        }

        private void CreateInfoRow(string text, Color color)
        {
            GameObject row = NewRow(RowHeight);
            TMP_Text t = MakeText("Label", row.transform, text, UITheme.FontSmall, TextAlignmentOptions.MidlineLeft);
            t.color = color;
            t.enableWordWrapping = true;      //说明/错误文案可能较长
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            SetStretch(t.rectTransform, 12f, 0f, 12f, 0f);
        }

        private void CreateActionRow(string label, string right_label, Color bg_color, Action on_click)
        {
            GameObject row = NewRow(RowHeight);
            Image bg = row.AddComponent<Image>();
            bg.color = bg_color;
            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => on_click());

            TMP_Text t = MakeText("Label", row.transform, label, UITheme.FontBody, TextAlignmentOptions.MidlineLeft);
            t.raycastTarget = false;
            SetStretch(t.rectTransform, 12f, 0f, right_label != null ? 96f : 12f, 0f);

            if (right_label != null)
            {
                TMP_Text r = MakeText("Right", row.transform, right_label, UITheme.FontSmall, TextAlignmentOptions.MidlineRight);
                r.color = UITheme.TextDim;
                r.raycastTarget = false;
                SetStretch(r.rectTransform, 12f, 0f, 12f, 0f);
            }
        }

        private GameObject NewRow(float height)
        {
            GameObject row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(list_root, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return row;
        }

        private static string ZoneTitle(DeckZone zone)
        {
            return string.IsNullOrEmpty(zone.title) ? zone.id : zone.title;
        }

        private static string CardName(string tid)
        {
            CardData card = CardData.Get(tid);
            return card != null && !string.IsNullOrEmpty(card.title) ? card.title : tid;
        }

        //============================ 小工具 ============================

        private static TMP_Text MakeText(string name, Transform parent, string txt, int size, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            SetStretch(rt, 0f, 0f, 0f, 0f);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyFont(t);      //统一字体管线（不用旧版 uGUI Text）
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
