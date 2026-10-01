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
        private const float PanelWidth = 780f;
        private const float MaxPanelHeight = 860f;
        private const float MinPanelHeight = 520f;
        private const string Title = "额外区";

        //行底色一律取主题 token（原先硬编码 (1,1,1,0.08)，与弹层底色 BgPopup 几乎同色 → 整块糊成一片）
        private static readonly Color RowNormal = UITheme.CtrlWeak;
        private static readonly Color RowDim = new Color(1f, 1f, 1f, 0.04f);

        private static DeckZonePopupUI instance;

        private bool built;
        private RectTransform rt_panel;
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
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            Transform parent = canvas != null ? canvas.transform : context;
            RectTransform root = PopupSkin.CreateRootLayer(parent, "DeckZonePopup", 1);
            instance = root.gameObject.AddComponent<DeckZonePopupUI>();
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
            base.Update();      //★ 必须调用：UIPanel 的淡入淡出在这里推进

            //Esc 关闭（移动端没有这个键，用右上角 × 或点遮罩）
            if (IsVisible() && !GameTool.IsMobile() && PopupSkin.EscPressed())
                Hide();

            PopupSkin.AnimateScale(rt_panel, IsVisible());
        }

        //============================ 构建（只建一次）============================

        /// <summary>
        /// 外观全部交给 PopupSkin（面板/遮罩/标题/图标关闭按钮/滚动列表/页脚），
        /// 本类只保留数据逻辑 —— 这样两个弹层与项目既有弹层长得是同一套东西。
        /// </summary>
        private void EnsureBuilt()
        {
            if (built)
                return;
            Transform stale = transform.Find("Panel");
            if (stale != null)
                Destroy(stale.gameObject);   //清掉上次构建失败留下的半成品

            Button mask = PopupSkin.CreateMask((RectTransform)transform);
            mask.onClick.AddListener(() => Hide());   //编辑即时生效，点遮罩关掉不丢内容

            float h = Mathf.Clamp(Screen.height - 120f, MinPanelHeight, MaxPanelHeight);   //移动端/小屏不顶出屏幕
            rt_panel = PopupSkin.CreatePanel((RectTransform)transform, "Panel", PanelWidth, h);

            Button close;
            PopupSkin.CreateTitleRow(rt_panel, Title, out close);
            close.onClick.AddListener(() => Hide());

            list_root = PopupSkin.CreateScroll(rt_panel, PopupSkin.FooterH);

            RectTransform footer = PopupSkin.CreateFooter(rt_panel, PopupSkin.FooterH, out txt_footer);
            PopupSkin.CreateFooterButton(footer, "完成", UITheme.BtnWNormal, () => Hide());

            built = true;
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

            //校验结果进**列表**（原先整段塞在页脚一行里 → 长文案被裁掉，这是"看不清"的来源之一）
            List<DeckError> errors = ValidateZones();
            CreateSectionRow("校验结果");
            if (errors.Count == 0)
                CreateInfoRow("额外区符合当前构筑规则", UITheme.Accent);
            else
                foreach (DeckError e in errors)
                    CreateInfoRow("· " + e.message, UITheme.Danger);

            RefreshFooter(errors.Count);
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

        /// <summary>页脚只放一句话（细节在列表里逐条列，避免长文案挤在一行被裁掉）</summary>
        private void RefreshFooter(int error_count)
        {
            if (txt_footer == null)
                return;
            if (error_count == 0)
            {
                txt_footer.text = "额外区符合当前构筑规则；改动即时生效，点遮罩或 × 关闭";
                txt_footer.color = UITheme.Accent;
                return;
            }
            txt_footer.text = "额外区有 " + error_count + " 处不符（见列表底部红字）";
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

        //行工厂全部委托 PopupSkin（尺寸/字号/配色/折行规则只有一处定义）
        //注意：带右侧按钮的动作行，点右侧按钮才生效（原先整行可点且右侧只是文字，容易误触）

        private void ClearList()
        {
            PopupSkin.ClearList(list_root);
        }

        private void CreateSectionRow(string text)
        {
            PopupSkin.CreateSection(list_root, text);
        }

        private void CreateInfoRow(string text, Color color)
        {
            PopupSkin.CreateInfoRow(list_root, text, color);
        }

        private void CreateActionRow(string label, string right_label, Color bg_color, Action on_click)
        {
            PopupSkin.CreateActionRow(list_root, label, right_label, bg_color, on_click);
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
        //（原 MakeText/MakeButton/SetStretch/Anchor 已删除：外观统一由 PopupSkin 提供）
    }
}
