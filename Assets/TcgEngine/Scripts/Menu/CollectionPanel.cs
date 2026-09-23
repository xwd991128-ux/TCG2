using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// CollectionPanel is the panel where players can see all the cards they own
    /// Also the panel where they can use the deckbuilder
    /// </summary>

    public class CollectionPanel : UIPanel
    {
        [Header("Cards")]
        public ScrollRect scroll_rect;
        public RectTransform scroll_content;
        public CardGrid grid_content;
        public GameObject card_prefab;

        [Header("筛选")]
        public Button filter_button;       // 打开右侧筛选弹层
        public UIPanel filter_panel;       // 右侧筛选弹层根

        [Header("Right Side")]
        public UIPanel deck_list_panel;
        public UIPanel card_list_panel;
        public DeckLine[] deck_lines;

        [Header("Deckbuilding")]
        public InputField deck_title;
        public Text deck_quantity;
        public GameObject deck_cards_prefab;
        public RectTransform deck_content;
        public GridLayoutGroup deck_grid;
        public IconButton[] hero_powers;

        private CardFilterState filter_state = new CardFilterState();
        private List<string> pool_keys = new List<string>(); // 与卡池下拉选项一一对应

        //高级筛选：搜索框文本 → CardQuery（按文本缓存，避免逐卡重复解析）
        private CardQuery query_cache;
        private string query_cache_src;
        private TMPro.TMP_Text empty_hint;      //筛不出卡时的提示（运行时创建）
        private Button assist_button;           //「筛选助手」按钮（运行时创建）

        //筛选弹层控件引用（运行时 Find 绑定）
        private List<string> pool_labels = new List<string>();   // 与 pool_keys 一一对应的显示名
        private TMPro.TMP_Text filter_pool_select_text;          // 卡池：弹出单选按钮文本
        private TMPro.TMP_Text filter_sort_by_select_text;       // 排序字段
        private TMPro.TMP_Text filter_sort_dir_select_text;      // 排序方向
        private Dropdown filter_pool_dd;
        private Toggle[] filter_type_toggles = new Toggle[0];
        private Toggle[] filter_team_toggles = new Toggle[0];
        private Toggle[] filter_cost_toggles = new Toggle[0];
        private InputField filter_search_input;
        private Toggle filter_foil_toggle;
        private Toggle[] filter_rarity_toggles = new Toggle[0];
        private Dropdown filter_sort_by_dd;
        private Dropdown filter_sort_dir_dd;
        private Button filter_apply_btn;
        private Button filter_clear_btn;

        private List<CollectionCard> card_list = new List<CollectionCard>();
        private List<CollectionCard> all_list = new List<CollectionCard>();
        private List<DeckLine> deck_card_lines = new List<DeckLine>();

        private string current_deck_tid;
        private bool editing_deck = false;
        private bool saving = false;
        private bool spawned = false;
        private bool update_grid = false;
        private float update_grid_timer = 0f;

        private List<UserCardData> deck_cards = new List<UserCardData>();

        //构筑规则状态行（卡表列表的第一行）：合法徽标 + 主卡/额外区统计；整行可点 → 打开「额外区」弹层
        //优先绑定场景里已有的 DeckRuleStatus 槽位，没有才新建（避免重复区块；旧实现放在侧栏外会出界）
        private GameObject deck_rule_status;
        private TMPro.TMP_Text deck_rule_status_text;
        private List<UserDeckZone> deck_zones = new List<UserDeckZone>();   //额外区缓冲（与弹层共用同一个 List）
        private DeckLine deck_highlight_line;      //「返回调整」定位到的那一行
        private float deck_highlight_timer;        //>0 时该行处于高亮状态（到期自动还原）

        private const float DeckHighlightSeconds = 1.2f;
        private static readonly Color DeckHighlightColor = new Color(1f, 0.9f, 0.4f, 1f);

        private static CollectionPanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            //Delete grid content
            for (int i = 0; i < grid_content.transform.childCount; i++)
                Destroy(grid_content.transform.GetChild(i).gameObject);
            for (int i = 0; i < deck_grid.transform.childCount; i++)
                Destroy(deck_grid.transform.GetChild(i).gameObject);

            foreach (DeckLine line in deck_lines)
                line.onClick += OnClickDeckLine;
            foreach (DeckLine line in deck_lines)
                line.onClickDelete += OnClickDeckDelete;

            if (filter_button != null)
                filter_button.onClick.AddListener(OnClickFilterButton);

            BindFilterPanel();
        }

        protected override void Start()
        {
            base.Start();

            //Set power abilities hover text
            foreach (IconButton btn in hero_powers)
            {
                CardData icard = CardData.Get(btn.value);
                HoverTargetUI hover = btn.GetComponent<HoverTargetUI>();
                AbilityData iability = icard?.GetAbility(AbilityTrigger.Activate);
                if (icard != null && hover != null && iability != null)
                {
                    string color = ColorUtility.ToHtmlStringRGBA(icard.team.color);
                    hover.text = "<b><color=#" + color + ">Hero Power: </color>";
                    hover.text += icard.title + "</b>\n " + iability.GetDesc(icard);
                    if (iability.mana_cost > 0)
                        hover.text += " <size=16>Mana: " + iability.mana_cost + "</size>";
                }
            }
        }

        protected override void Update()
        {
            base.Update();

            //「返回调整」定位的高亮：到期自动还原（必须还原，否则卡图会一直蒙着高亮色）
            if (deck_highlight_timer > 0f)
            {
                deck_highlight_timer -= Time.deltaTime;
                if (deck_highlight_timer <= 0f)
                    RestoreDeckHighlight();
            }
        }

        private void LateUpdate()
        {
            //Resize grid
            update_grid_timer += Time.deltaTime;
            if (update_grid && update_grid_timer > 0.2f)
            {
                grid_content.GetColumnAndRow(out int rows, out int cols);
                if (cols > 0)
                {
                    float row_height = grid_content.GetGrid().cellSize.y + grid_content.GetGrid().spacing.y;
                    float height = rows * row_height;
                    scroll_content.sizeDelta = new Vector2(scroll_content.sizeDelta.x, height + 100);
                    update_grid = false;
                }
            }
        }

        private void OnDestroy()
        {
            foreach (DeckLine line in deck_lines)
            {
                line.onClick -= OnClickDeckLine;
                line.onClickDelete -= OnClickDeckDelete;
            }

            if (filter_button != null)
                filter_button.onClick.RemoveListener(OnClickFilterButton);
            if (filter_apply_btn != null)
                filter_apply_btn.onClick.RemoveListener(ApplyFilter);
            if (filter_clear_btn != null)
                filter_clear_btn.onClick.RemoveListener(ClearAllFilter);
            if (filter_search_input != null)
                filter_search_input.onEndEdit.RemoveListener(OnSearchEndEdit);
        }

        private void SpawnCards()
        {
            spawned = true;
            foreach (CollectionCard card in all_list)
                Destroy(card.gameObject);
            all_list.Clear();

            foreach (CardData card in CardData.GetAll())
            {
                GameObject nCard = Instantiate(card_prefab, grid_content.transform);
                CollectionCard dCard = nCard.GetComponent<CollectionCard>();
                dCard.SetCard(card, VariantData.GetDefault(), 0);
                dCard.onClick += OnClickCard;
                dCard.onClickRight += OnClickCardRight;
                all_list.Add(dCard);
                nCard.SetActive(false);
            }
        }

        //----- Reload User Data ---------------

        public async void ReloadUser()
        {
            await Authenticator.Get().LoadUserData();
            MainMenu.Get().RefreshDeckList();
            RefreshCardsQuantities();

            if (!editing_deck)
                RefreshDeckList();
        }

        public async void ReloadUserCards()
        {
            await Authenticator.Get().LoadUserData();
            RefreshCardsQuantities();
        }

        public async void ReloadUserDecks()
        {
            await Authenticator.Get().LoadUserData();
            MainMenu.Get().RefreshDeckList();
            RefreshDeckList();
        }

        //----- Refresh UI --------

        private void RefreshAll()
        {
            RefreshFilters();
            RefreshCards();
            RefreshDeckList();
            RefreshStarterDeck();
        }

        private void RefreshFilters()
        {
            filter_state = new CardFilterState();
            SetFilterToUI(); //同步到弹层控件（若已绑定）
        }

        private void ShowDeckList()
        {
            deck_list_panel.Show();
            card_list_panel.Hide();
            editing_deck = false;
        }

        private void ShowDeckCards()
        {
            deck_list_panel.Hide();
            card_list_panel.Show();
        }
        
        public void RefreshCards()
        {
            if (!spawned)
                SpawnCards();

            card_list.Clear();

            UserData udata = Authenticator.Get().UserData;
            if (udata == null)
                return;

            VariantData variant = VariantData.GetDefault();
            VariantData special = VariantData.GetSpecial();
            if (filter_state.foil && special != null)
                variant = special;

            List<CardDataQ> all_cards = new List<CardDataQ>();
            List<CardDataQ> shown_cards = new List<CardDataQ>();

            foreach (CardData icard in CardData.GetAll())
            {
                CardDataQ card = new CardDataQ();
                card.card = icard;
                card.variant = variant;
                card.quantity = udata.GetCardQuantity(icard, variant);
                all_cards.Add(card);
            }

            SortCards(all_cards);

            foreach (CardDataQ card in all_cards)
            {
                if (!card.card.deckbuilding)
                    continue;
                CardData icard = card.card;

                if (!CardPoolIO.IsCardInPool(icard, filter_state.pool))
                    continue;
                if (filter_state.types.Count > 0 && !filter_state.types.Contains(icard.type))
                    continue;
                if (filter_state.teams.Count > 0 && (icard.team == null || !filter_state.teams.Contains(icard.team)))
                    continue;
                if (filter_state.costs.Count > 0)
                {
                    //费用 7 表示「7+」：匹配 7 费及以上
                    bool cost_match = filter_state.costs.Contains(icard.mana);
                    if (!cost_match && filter_state.costs.Contains(7) && icard.mana >= 7)
                        cost_match = true;
                    if (!cost_match)
                        continue;
                }
                if (filter_state.rarities.Count > 0 && (icard.rarity == null || !filter_state.rarities.Contains(icard.rarity)))
                    continue;

                //★ 搜索框支持「高级筛选语法」（CardQuery）：trait:龙 mana<=3 kw:战吼 p:元素=火 …
                //  裸词的行为与旧搜索框完全一致（模糊匹配 id/标题/卡面文字/描述）→ 旧用法不用改。
                if (!Query.Match(icard))
                    continue;

                shown_cards.Add(card);
            }

            SetEmptyHint(shown_cards.Count == 0 && !Query.IsEmpty);

            int index = 0;
            foreach (CardDataQ qcard in shown_cards)
            {
                if (index < all_list.Count)
                {
                    CollectionCard dcard = all_list[index];
                    dcard.SetCard(qcard.card, qcard.variant, 0);
                    card_list.Add(dcard);
                    if (!dcard.gameObject.activeSelf)
                        dcard.gameObject.SetActive(true);
                    index++;
                }
            }

            for (int i = index; i < all_list.Count; i++)
                all_list[i].gameObject.SetActive(false);

            update_grid = true;
            update_grid_timer = 0f;
            scroll_rect.verticalNormalizedPosition = 1f;
            RefreshCardsQuantities();
        }

        private void RefreshCardsQuantities()
        {
            UserData udata = Authenticator.Get().UserData;
            foreach (CollectionCard card in card_list)
            {
                CardData icard = card.GetCard();
                VariantData ivariant = card.GetVariant();
                bool owned = IsCardOwned(udata, icard, ivariant, 1);
                int quantity = udata.GetCardQuantity(icard, ivariant);
                card.SetQuantity(quantity);
                card.SetGrayscale(!owned);
            }
        }

        private void RefreshDeckList()
        {
            foreach (DeckLine line in deck_lines)
                line.Hide();
            deck_cards.Clear();
            deck_zones.Clear();
            editing_deck = false;
            saving = false;

            UserData udata = Authenticator.Get().UserData;
            if (udata == null)
                return;

            int index = 0;
            foreach (UserDeckData deck in udata.decks)
            {
                if (index < deck_lines.Length)
                {
                    DeckLine line = deck_lines[index];
                    line.SetLine(udata, deck);
                }
                index++;
            }

            if (index < deck_lines.Length)
            {
                DeckLine line = deck_lines[index];
                line.SetLine("+");
            }
            RefreshCardsQuantities();
        }

        private void RefreshDeck(UserDeckData deck)
        {
            deck_title.text = "Deck Name";
            current_deck_tid = GameTool.GenerateRandomID(7);
            deck_cards.Clear();
            saving = false;
            editing_deck = true;

            foreach (IconButton btn in hero_powers)
                btn.Deactivate();

            if (deck != null)
            {
                deck_title.text = deck.title;
                current_deck_tid = deck.tid;

                foreach (IconButton btn in hero_powers)
                {
                    if (deck.hero != null && btn.value == deck.hero.tid)
                        btn.Activate();
                }
                
                for (int i = 0; i < deck.cards.Length; i++)
                {
                    CardData card = CardData.Get(deck.cards[i].tid);
                    VariantData variant = VariantData.Get(deck.cards[i].variant);
                    if (card != null && variant != null)
                    {
                        AddDeckCard(card, variant, deck.cards[i].quantity);
                    }
                }

                //额外区：与主卡分开存（UserDeckData.zones），随卡组一起载入
                deck_zones.Clear();
                if (deck.zones != null)
                {
                    foreach (UserDeckZone z in deck.zones)
                    {
                        if (z != null && !string.IsNullOrEmpty(z.zone_id))
                            deck_zones.Add(z);
                    }
                }
            }

            RefreshDeckCards();
        }

        private void RefreshDeckCards()
        {
            foreach (DeckLine line in deck_card_lines)
                line.Hide();

            List<CardDataQ> list = new List<CardDataQ>();
            foreach (UserCardData card in deck_cards)
            {
                CardDataQ acard = new CardDataQ();
                acard.card = CardData.Get(card.tid);
                acard.variant = VariantData.Get(card.variant);
                acard.quantity = card.quantity;
                list.Add(acard);
            }
            list.Sort((CardDataQ a, CardDataQ b) => { return a.card.title.CompareTo(b.card.title); });

            UserData udata = Authenticator.Get().UserData;
            int index = 0;
            int count = 0;
            foreach (CardDataQ card in list)
            {
                if (index >= deck_card_lines.Count)
                    CreateDeckCard();

                if (index < deck_card_lines.Count)
                {
                    DeckLine line = deck_card_lines[index];
                    if (line != null)
                    {
                        line.SetLine(card.card, card.variant, card.quantity, !IsCardOwned(udata, card.card, card.variant, card.quantity));
                        count += card.quantity;
                    }
                }
                index++;
            }

            RefreshDeckStatus(count);

            RefreshCardsQuantities();
        }

        /// <summary>
        /// 构筑规则实时反馈（卡组区唯一刷新点 RefreshDeckCards 调用）：
        ///   · 把"主卡 x/N"的 N 从写死的 GameplayData.deck_size 改成**当前构筑规则的 N**
        ///     （这样选了乱斗/自定义修饰后，要求的张数会跟着变）；
        ///   · 「合法 / 不合法（N 条）」徽标 + 额外区统计 + 中文错误逐条。
        /// 规则与判定全部来自 DeckRulesResolver / DeckValidator，界面不自己算。
        /// </summary>
        private void RefreshDeckStatus(int count)
        {
            UserDeckData udeck = BuildEditingDeck();
            DeckFormatData format = DeckFormatData.Get(GameClient.game_settings.deck_format_id);
            if (format == null)
                format = DeckFormatData.GetStandard();
            List<string> toggles = DeckRuleToggles.Split(GameClient.game_settings.deck_optional_rules);

            ResolvedDeckRules rules = DeckRulesResolver.Resolve(udeck, format, toggles);
            List<DeckError> errors = DeckValidator.Validate(udeck, format, toggles);

            if (deck_quantity != null)
            {
                deck_quantity.text = count + "/" + rules.deck_size;
                deck_quantity.color = count == rules.deck_size ? Color.white : Color.red;
            }

            EnsureDeckRuleStatusRow();
            if (deck_rule_status_text == null)
                return;

            //额外区最多显示 2 个，多的折成"等 N 个区" —— 状态行高度固定在约 3 行，超了会被裁掉
            List<DeckZone> valid_zones = new List<DeckZone>();
            foreach (DeckZone zone in rules.zones)
            {
                if (zone != null && !string.IsNullOrEmpty(zone.id))
                    valid_zones.Add(zone);
            }
            string zones_text = "";
            for (int i = 0; i < valid_zones.Count && i < 2; i++)
            {
                DeckZone zone = valid_zones[i];
                zones_text += (zones_text.Length > 0 ? "  " : "")
                    + "额外区「" + (string.IsNullOrEmpty(zone.title) ? zone.id : zone.title) + "」"
                    + CountZone(udeck, zone.id) + "/" + zone.max_count;
            }
            if (valid_zones.Count > 2)
                zones_text += "  等 " + valid_zones.Count + " 个区";
            if (zones_text.Length == 0)
                zones_text = "额外区：当前环境没有";

            //★ 只用字体确实覆盖的符号：▸(U+25B8) 不在覆盖表里，会渲成方块；→ 在（见 UIFonts.SymbolProbe）
            string text = (errors.Count == 0 ? "● 合法" : "● 不合法（" + errors.Count + " 条）")
                + "  主卡 " + count + "/" + rules.deck_size
                + "\n" + zones_text + "   → 点此编辑";
            if (errors.Count > 0)
            {
                string first = DeckValidator.JoinMessages(errors, "；");
                text += "\n" + (first.Length > 46 ? first.Substring(0, 46) + "…" : first);
            }

            deck_rule_status_text.text = text;
            deck_rule_status_text.color = errors.Count == 0 ? UITheme.Accent : UITheme.Danger;
        }

        /// <summary>用当前编辑中的卡组拼一个 UserDeckData（只读用途：校验/统计）</summary>
        private UserDeckData BuildEditingDeck()
        {
            UserDeckData udeck = new UserDeckData();
            udeck.tid = current_deck_tid;
            udeck.title = deck_title != null ? deck_title.text : "";
            udeck.hero = new UserCardData();
            udeck.hero.tid = GetSelectedHeroId();
            udeck.hero.variant = VariantData.GetDefault().id;
            udeck.cards = deck_cards.ToArray();
            udeck.zones = deck_zones.ToArray();
            return udeck;
        }

        private static int CountZone(UserDeckData deck, string zone_id)
        {
            if (deck == null || deck.zones == null)
                return 0;
            foreach (UserDeckZone z in deck.zones)
            {
                if (z == null || z.zone_id != zone_id || z.cards == null)
                    continue;
                int total = 0;
                foreach (UserCardData c in z.cards)
                {
                    if (c != null && c.quantity > 0)
                        total += c.quantity;
                }
                return total;
            }
            return 0;
        }

        /// <summary>
        /// 状态行：卡表列表的第一行（整行可点 → 打开「额外区」弹层）。
        /// 优先**绑定场景里已有的同名槽位**，没有才新建 —— 避免"场景一个 + 运行时又建一个"的重复区块；
        /// 同时修掉旧实现的位置错误（原来按 DeckCount 往下推，实测会超出侧栏右/下边界各 259/198 单位）。
        /// </summary>
        private void EnsureDeckRuleStatusRow()
        {
            if (deck_rule_status != null || deck_grid == null)
                return;

            //① 行容器**自己新建**：实测在场景里遗留的那个 DeckRuleStatus 上 AddComponent<Image>/<Button>()
            //   会返回 null（组件加不上 → 行点不动，第一次还会 NRE），所以容器必须是干净对象。
            GameObject row_go = new GameObject("DeckRuleRow", typeof(RectTransform));
            row_go.transform.SetParent(deck_grid.transform, false);
            row_go.transform.SetAsFirstSibling();
            SetStretchRect(row_go.GetComponent<RectTransform>());
            Image bg = row_go.AddComponent<Image>();
            bg.color = UITheme.Ctrl;   //与卡组行的浅色条同档，别用 0.10（太淡，看着像游离的文字而不是一行）
            Button btn = row_go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(OnClickDeckRuleStatus);
            deck_rule_status = row_go;

            //② 文本优先**复用**场景里已有的 DeckRuleStatus（避免出现两个徽标），没有才新建
            Transform found = deck_grid.transform.Find("DeckRuleStatus");
            if (found == null && transform != null)
                found = transform.Find("SidebarRight/EditDeck/DeckRuleStatus");
            if (found != null)
            {
                found.SetParent(row_go.transform, false);
                deck_rule_status_text = found.GetComponent<TMPro.TMP_Text>();
            }
            if (deck_rule_status_text == null)
            {
                GameObject text_go = new GameObject("DeckRuleStatus", typeof(RectTransform));
                text_go.transform.SetParent(row_go.transform, false);
                TMPro.TextMeshProUGUI t = text_go.AddComponent<TMPro.TextMeshProUGUI>();
                UIFonts.ApplyFont(t);      //统一字体管线（运行时新建文本一律 TMP）
                t.fontSize = UITheme.FontSmall;
                t.alignment = TMPro.TextAlignmentOptions.TopLeft;
                t.enableWordWrapping = true;
                t.overflowMode = TMPro.TextOverflowModes.Overflow;
                t.raycastTarget = false;
                deck_rule_status_text = t;
            }
            SetStretchRect(deck_rule_status_text.rectTransform);
            deck_rule_status_text.color = UITheme.TextDim;

            //③ 列表高度要算上这一行，否则最后一行会被裁掉
            if (deck_content != null)
            {
                float h = Mathf.Max(deck_content.sizeDelta.y, (deck_card_lines.Count + 1) * 70f + 20f);
                deck_content.sizeDelta = new Vector2(deck_content.sizeDelta.x, h);
            }
        }

        private void OnClickDeckRuleStatus()
        {
            //把**正在编辑的两个缓冲**交给弹层直接改（改动即时生效），关掉后刷新本页
            DeckZonePopupUI popup = DeckZonePopupUI.Create(transform);
            popup.Open(deck_cards, deck_zones, RefreshDeckCards);
        }

        private static void SetStretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        //-------- 「返回调整」定位（构筑规则弹框 → 跳回卡组编辑并指出出错的卡）--------

        /// <summary>打开指定卡组的编辑视图（跨面板跳转用）</summary>
        public void OpenDeckForEdit(UserDeckData deck)
        {
            if (deck == null || deck.cards == null)
                return;
            ShowDeckCards();
            RefreshDeck(deck);
        }

        /// <summary>切到卡组编辑视图，滚动并高亮指定卡；tid 为空则只切视图</summary>
        public void FocusDeckCard(string tid)
        {
            ShowDeckCards();
            if (string.IsNullOrEmpty(tid))
                return;

            int index = 0;
            int total = 0;
            DeckLine target = null;
            foreach (DeckLine line in deck_card_lines)
            {
                if (line == null || line.IsHidden())
                    continue;
                CardData card = line.GetCard();
                total++;
                if (target == null && card != null && card.id == tid)
                {
                    target = line;
                    index = total - 1;
                }
            }

            if (target == null)
            {
                Debug.Log("[构筑规则] 卡组里没有「" + tid + "」这张卡（该错误可能来自额外区或卡池），已停在卡组编辑视图。");
                return;
            }

            HighlightDeckLine(target);
            ScrollDeckTo(index, total);
        }

        private void HighlightDeckLine(DeckLine line)
        {
            if (deck_highlight_line != null && deck_highlight_line != line)
                RestoreDeckHighlight();
            deck_highlight_line = line;
            deck_highlight_timer = DeckHighlightSeconds;
            if (line.image != null)
                line.image.color = DeckHighlightColor;
        }

        /// <summary>还原占位色（不清掉会让卡图一直蒙色）</summary>
        private void RestoreDeckHighlight()
        {
            if (deck_highlight_line != null && deck_highlight_line.image != null)
                deck_highlight_line.image.color = Color.white;
            deck_highlight_line = null;
            deck_highlight_timer = 0f;
        }

        private void ScrollDeckTo(int index, int total)
        {
            if (deck_content == null)
                return;
            ScrollRect scroll = deck_content.GetComponentInParent<ScrollRect>();
            if (scroll == null)
                return;
            scroll.verticalNormalizedPosition = total > 1 ? Mathf.Clamp01(1f - (float)index / (total - 1)) : 1f;
        }

        private void RefreshStarterDeck()
        {
            UserData udata = Authenticator.Get().UserData;
            if (udata != null && (udata.cards.Length == 0 || udata.rewards.Length == 0))
            {
                if (GameplayData.Get().starter_decks.Length > 0)
                {
                    StarterDeckPanel.Get().Show();
                }
            }
        }

        //-------- Deck editing actions

        private void CreateDeckCard()
        {
            GameObject deck_line = Instantiate(deck_cards_prefab, deck_grid.transform);
            DeckLine line = deck_line.GetComponent<DeckLine>();
            deck_card_lines.Add(line);
            float height = deck_card_lines.Count * 70f + 20f;
            deck_content.sizeDelta = new Vector2(deck_content.sizeDelta.x, height);
            line.onClick += OnClickCardLine;
            line.onClickRight += OnRightClickCardLine;
        }

        private void AddDeckCard(CardData card, VariantData variant, int quantity = 1)
        {
            AddDeckCard(card.id, variant.id, quantity);
        }

        private void RemoveDeckCard(CardData card, VariantData variant)
        {
            RemoveDeckCard(card.id, variant.id);
        }

        private void AddDeckCard(string tid, string variant, int quantity = 1)
        {
            UserCardData ucard = GetDeckCard(tid, variant);
            if (ucard != null)
            {
                ucard.quantity += quantity;
            }
            else
            {
                ucard = new UserCardData(tid, variant);
                ucard.quantity = quantity;
                deck_cards.Add(ucard);
            }
        }

        private void RemoveDeckCard(string tid, string variant)
        {
            for (int i = deck_cards.Count - 1; i >= 0; i--)
            {
                UserCardData ucard = deck_cards[i];
                if (ucard.tid == tid && ucard.variant == variant)
                {
                    ucard.quantity--;

                    if(ucard.quantity <= 0)
                        deck_cards.RemoveAt(i);
                }
            }
        }

        private UserCardData GetDeckCard(string tid, string variant)
        {
            foreach (UserCardData ucard in deck_cards)
            {
                if (ucard.tid == tid && ucard.variant == variant)
                    return ucard;
            }
            return null;
        }

        private void SaveDeck()
        {
            UserData udata = Authenticator.Get().UserData;
            UserDeckData udeck = new UserDeckData();
            udeck.tid = current_deck_tid;
            udeck.title = deck_title.text;
            udeck.hero = new UserCardData();
            udeck.hero.tid = GetSelectedHeroId();
            udeck.hero.variant = VariantData.GetDefault().id;
            udeck.cards = deck_cards.ToArray();
            udeck.zones = deck_zones.ToArray();   //★额外区必须一起存档（原来只存主卡，额外区会被丢掉）
            saving = true;

            if (Authenticator.Get().IsTest())
                SaveDeckTest(udata, udeck);

            if (Authenticator.Get().IsApi())
                SaveDeckAPI(udata, udeck);

            ShowDeckList();
        }

        private async void SaveDeckTest(UserData udata, UserDeckData udeck)
        {
            udata.SetDeck(udeck);
            await Authenticator.Get().SaveUserData();
            ReloadUserDecks();
        }

        private async void SaveDeckAPI(UserData udata, UserDeckData udeck)
        {
            string url = ApiClient.ServerURL + "/users/deck/" + udeck.tid;
            string jdata = ApiTool.ToJson(udeck);
            WebResponse res = await ApiClient.Get().SendPostRequest(url, jdata);
            UserDeckData[] decks = ApiTool.JsonToArray<UserDeckData>(res.data);
            saving = res.success;

            if (res.success && decks != null)
            {
                udata.decks = decks;
                await Authenticator.Get().SaveUserData();
                ReloadUserDecks();
            }
        }

        private async void DeleteDeck(string deck_tid)
        {
            UserData udata = Authenticator.Get().UserData;
            UserDeckData udeck = udata.GetDeck(deck_tid);
            List<UserDeckData> decks = new List<UserDeckData>(udata.decks);
            decks.Remove(udeck);
            udata.decks = decks.ToArray();

            if (Authenticator.Get().IsApi())
            {
                string url = ApiClient.ServerURL + "/users/deck/" + deck_tid;
                await ApiClient.Get().SendRequest(url, "DELETE", "");
            }

            await Authenticator.Get().SaveUserData();
            ReloadUserDecks();
        }

        //---- 筛选弹层 -----------

        public void OnClickFilterButton()
        {
            RefreshPoolOptions();
            SetFilterToUI();
            if (filter_panel != null)
                filter_panel.Show();
        }

        /// <summary>运行时查找并绑定筛选弹层内控件</summary>
        private void BindFilterPanel()
        {
            if (filter_panel == null)
                return;

            Transform root = filter_panel.transform;
            filter_pool_dd = FindChild<Dropdown>(root, "FilterPoolDd");
            filter_search_input = FindChild<InputField>(root, "FilterSearchInput");
            filter_foil_toggle = FindChild<Toggle>(root, "FilterFoilToggle");
            filter_sort_by_dd = FindChild<Dropdown>(root, "FilterSortByDd");
            filter_sort_dir_dd = FindChild<Dropdown>(root, "FilterSortDirDd");
            filter_apply_btn = FindChild<Button>(root, "FilterApplyBtn");
            filter_clear_btn = FindChild<Button>(root, "FilterClearBtn");

            filter_type_toggles = FindToggles(root, "FilterTypeToggle_");
            filter_team_toggles = FindToggles(root, "FilterTeamToggle_");
            filter_cost_toggles = FindToggles(root, "FilterCostToggle_");
            filter_rarity_toggles = FindToggles(root, "FilterRarityToggle_");

            //卡池 / 排序字段 / 排序方向：旧下拉 → 弹出单选按钮（与规则编辑器、关键词管理同款交互）。
            //AttachToDropdown 会停用旧下拉并保留它的底色与布局，所以不需要重跑生成工具。
            filter_pool_select_text = UISelectPopup.AttachToDropdown(filter_pool_dd, OnClickFilterPool);
            filter_sort_by_select_text = UISelectPopup.AttachToDropdown(filter_sort_by_dd, OnClickFilterSortBy);
            filter_sort_dir_select_text = UISelectPopup.AttachToDropdown(filter_sort_dir_dd, OnClickFilterSortDir);

            if (filter_apply_btn != null)
                filter_apply_btn.onClick.AddListener(ApplyFilter);
            if (filter_clear_btn != null)
                filter_clear_btn.onClick.AddListener(ClearAllFilter);

            //搜索框：提示语法 + 回车即生效（不用再点「应用」；点应用仍然可用）
            ApplySearchPlaceholder();
            if (filter_search_input != null)
                filter_search_input.onEndEdit.AddListener(OnSearchEndEdit);

            EnsureAssistButton();
        }

        /// <summary>把搜索框的占位提示换成高级筛选语法示例（兼容 TMP / 旧版 Text 两种占位）</summary>
        private void ApplySearchPlaceholder()
        {
            if (filter_search_input == null || filter_search_input.placeholder == null)
                return;
            TMPro.TMP_Text tmp = filter_search_input.placeholder as TMPro.TMP_Text;
            if (tmp != null)
            {
                tmp.text = CardQuery.SyntaxHint;
                return;
            }
            Text legacy = filter_search_input.placeholder as Text;
            if (legacy != null)
                legacy.text = CardQuery.SyntaxHint;
        }

        private void OnSearchEndEdit(string text)
        {
            ReadFilterFromUI();
            RefreshCards();
        }

        /// <summary>创建「筛选助手」按钮（放在「筛选」按钮正下方；场景不用改，运行时自建）</summary>
        private void EnsureAssistButton()
        {
            if (assist_button != null || filter_button == null)
                return;

            Button btn = NewRuntimeButton("FilterAssistButton", transform, "筛选助手");
            RectTransform rt = btn.GetComponent<RectTransform>();
            RectTransform src = filter_button.GetComponent<RectTransform>();
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.sizeDelta = src.sizeDelta;
            rt.anchoredPosition = src.anchoredPosition + new Vector2(0f, -60f);   //正下方
            btn.onClick.AddListener(OnClickFilterAssist);
            assist_button = btn;
        }

        /// <summary>运行时自建按钮（TMP 文本 + 主题令牌：旧版 uGUI Text 会字体发糊/缺中文字形）</summary>
        private Button NewRuntimeButton(string name, Transform parent, string label)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = new Color(0.5f, 0.78f, 1f, 0.35f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);

            TMPro.TMP_Text t = UIFactory.CreateTmpText("Text", rt, label, UITheme.FontButton, UITheme.TextBody,
                TMPro.TextAlignmentOptions.Center, UIFonts.ResolveFont());
            UIFactory.SetStretch(t.rectTransform);
            t.raycastTarget = false;
            return btn;
        }

        private void OnClickFilterAssist()
        {
            CardFilterAssist.Open(transform,
                CapturePreset,                                   //保存预设：打包当前整套筛选状态
                ApplyPreset,                                     //应用预设：整套写回并刷新
                () => filter_state.search ?? "",
                OnAssistQueryChanged);
        }

        /// <summary>把当前筛选状态打包成一套"筛选方案"（预设 = 语法 + 勾选 + 卡池 + 金卡 + 排序）</summary>
        private CardFilterPreset CapturePreset()
        {
            ReadFilterFromUI();      //把弹层控件里的最新勾选/搜索读回状态，再打包

            CardFilterPreset p = new CardFilterPreset();
            p.query = filter_state.search ?? "";
            p.pool = filter_state.pool ?? "";
            p.foil = filter_state.foil;
            p.sort_by = filter_state.sort_by;
            p.sort_desc = filter_state.sort_desc;
            for (int i = 0; i < filter_state.types.Count; i++)
                p.types.Add(filter_state.types[i].ToString());          //存枚举名（"Spell"），不存枚举值
            for (int i = 0; i < filter_state.teams.Count; i++)
                if (filter_state.teams[i] != null) p.teams.Add(filter_state.teams[i].id);
            for (int i = 0; i < filter_state.rarities.Count; i++)
                if (filter_state.rarities[i] != null) p.rarities.Add(filter_state.rarities[i].id);
            for (int i = 0; i < filter_state.costs.Count; i++)
                p.costs.Add(filter_state.costs[i]);
            p.name = CardFilterPreset.AutoName(p);      //自动命名（免打字）
            return p;
        }

        /// <summary>应用一套筛选方案：状态 → 控件（SetFilterToUI 现成的回写通道）→ 刷新卡牌</summary>
        private void ApplyPreset(CardFilterPreset p)
        {
            if (p == null)
                return;

            filter_state.search = p.query ?? "";
            filter_state.pool = ResolvePoolKey(p.pool);     //卡池可能已被删 → 回退"全部卡池"
            filter_state.foil = p.foil;
            filter_state.sort_by = Mathf.Clamp(p.sort_by, 0, SORT_BY_VALUES.Length - 1);
            filter_state.sort_desc = p.sort_desc;

            filter_state.types.Clear();
            if (p.types != null)
            {
                for (int i = 0; i < p.types.Count; i++)
                {
                    CardType t = CardQuery.ParseType(p.types[i]);
                    if (t != CardType.None && !filter_state.types.Contains(t))
                        filter_state.types.Add(t);
                }
            }

            filter_state.teams.Clear();
            if (p.teams != null)
            {
                for (int i = 0; i < p.teams.Count; i++)
                {
                    TeamData t = TeamData.Get(p.teams[i]);
                    if (t != null && !filter_state.teams.Contains(t))
                        filter_state.teams.Add(t);
                }
            }

            filter_state.rarities.Clear();
            if (p.rarities != null)
            {
                for (int i = 0; i < p.rarities.Count; i++)
                {
                    RarityData r = RarityData.Get(p.rarities[i]);
                    if (r != null && !filter_state.rarities.Contains(r))
                        filter_state.rarities.Add(r);
                }
            }

            filter_state.costs.Clear();
            if (p.costs != null)
            {
                for (int i = 0; i < p.costs.Count; i++)
                {
                    if (!filter_state.costs.Contains(p.costs[i]))
                        filter_state.costs.Add(p.costs[i]);
                }
            }

            SetFilterToUI();     //勾选 / 卡池 / 排序 / 搜索框一次性同步
            RefreshCards();
        }

        /// <summary>预设里的卡池可能已不存在（本地卡池被删）→ 回退"全部卡池"，避免"筛出空列表还不知道为什么"</summary>
        private string ResolvePoolKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "";
            RefreshPoolOptions();       //重建可用卡池（含本地卡池增删）
            return pool_keys.Contains(key) ? key : "";
        }

        /// <summary>助手里改条件（追加/移除/清空/应用预设）→ 立即写回搜索框并刷新卡牌</summary>
        private void OnAssistQueryChanged(string text)
        {
            filter_state.search = text ?? "";
            if (filter_search_input != null)
                filter_search_input.text = filter_state.search;
            RefreshCards();
        }

        /// <summary>递归查找指定名称的子对象组件（弹层控件在多层嵌套内，root.Find 只查直接子级）</summary>
        private T FindChild<T>(Transform root, string name) where T : Component
        {
            foreach (Transform child in root)
            {
                if (child.name == name)
                {
                    T comp = child.GetComponent<T>();
                    if (comp != null)
                        return comp;
                }
                T found = FindChild<T>(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>按名字前缀查找一组 Toggle（如 FilterTeamToggle_xxx）</summary>
        private Toggle[] FindToggles(Transform root, string prefix)
        {
            List<Toggle> list = new List<Toggle>();
            foreach (Toggle tg in root.GetComponentsInChildren<Toggle>(true))
            {
                if (tg.name.StartsWith(prefix))
                    list.Add(tg);
            }
            return list.ToArray();
        }

        /// <summary>点击「卡池」按钮：弹出单选（显示卡池名，写回 pool key）</summary>
        private void OnClickFilterPool()
        {
            string cur = pool_keys.Count > 0 ? pool_keys[Mathf.Clamp(PoolIndex(), 0, pool_keys.Count - 1)] : "";
            UISelectPopup.OpenSingle(transform, "卡池", pool_labels, pool_keys, cur, OnPoolPicked);
        }

        private void OnPoolPicked(string key)
        {
            filter_state.pool = key ?? "";
            RefreshPoolSelect();
        }

        /// <summary>当前卡池在 pool_keys 中的下标（找不到时取 0＝全部卡池）</summary>
        private int PoolIndex()
        {
            int idx = pool_keys.IndexOf(filter_state.pool);
            return idx < 0 ? 0 : idx;
        }

        private void RefreshPoolSelect()
        {
            if (filter_pool_select_text == null)
                return;
            filter_pool_select_text.text = pool_labels.Count > 0
                ? pool_labels[Mathf.Clamp(PoolIndex(), 0, pool_labels.Count - 1)]
                : "全部卡池";
        }

        //排序字段 / 方向：选项与生成工具（CardFilterBuilder）保持一致
        private static readonly string[] SORT_BY_LABELS = { "名称", "法力值", "颜色", "稀有度" };
        private static readonly string[] SORT_BY_VALUES = { "0", "1", "2", "3" };
        private static readonly string[] SORT_DIR_LABELS = { "升序", "降序" };
        private static readonly string[] SORT_DIR_VALUES = { "0", "1" };

        /// <summary>点击「排序字段」：弹出单选</summary>
        private void OnClickFilterSortBy()
        {
            UISelectPopup.OpenSingle(transform, "排序字段", SORT_BY_LABELS, SORT_BY_VALUES,
                Mathf.Clamp(filter_state.sort_by, 0, SORT_BY_VALUES.Length - 1).ToString(), OnSortByPicked);
        }

        private void OnSortByPicked(string value)
        {
            int v;
            if (int.TryParse(value, out v))
                filter_state.sort_by = Mathf.Clamp(v, 0, SORT_BY_VALUES.Length - 1);
            RefreshSortSelect();
        }

        /// <summary>点击「排序方向」：弹出单选</summary>
        private void OnClickFilterSortDir()
        {
            UISelectPopup.OpenSingle(transform, "排序方向", SORT_DIR_LABELS, SORT_DIR_VALUES,
                filter_state.sort_desc ? "1" : "0", OnSortDirPicked);
        }

        private void OnSortDirPicked(string value)
        {
            filter_state.sort_desc = value == "1";
            RefreshSortSelect();
        }

        private void RefreshSortSelect()
        {
            if (filter_sort_by_select_text != null)
                filter_sort_by_select_text.text = SORT_BY_LABELS[Mathf.Clamp(filter_state.sort_by, 0, SORT_BY_LABELS.Length - 1)];
            if (filter_sort_dir_select_text != null)
                filter_sort_dir_select_text.text = filter_state.sort_desc ? SORT_DIR_LABELS[1] : SORT_DIR_LABELS[0];
        }

        /// <summary>重建卡池选项（全部 + 内置卡包 + 本地卡池），并刷新按钮显示</summary>
        private void RefreshPoolOptions()
        {
            if (filter_pool_dd == null && filter_pool_select_text == null)
                return;

            List<CardPoolIO.PoolOption> options = CardPoolIO.GetPoolOptions();
            pool_keys.Clear();
            pool_labels.Clear();
            foreach (CardPoolIO.PoolOption opt in options)
            {
                pool_keys.Add(opt.key);
                pool_labels.Add(opt.label);
            }
            RefreshPoolSelect();
        }

        private void ApplyFilter()
        {
            ReadFilterFromUI();
            if (filter_panel != null)
                filter_panel.Hide();
            RefreshCards();
        }

        private void ClearAllFilter()
        {
            filter_state = new CardFilterState();
            SetFilterToUI();
            RefreshCards();
        }

        /// <summary>把弹层控件当前值写入筛选状态</summary>
        private void ReadFilterFromUI()
        {
            //卡池 / 排序字段 / 排序方向：已由弹出单选的选中回调直接写入 filter_state，
            //这里不再从（已停用的）旧下拉读取，避免被它的默认值覆盖。

            filter_state.types.Clear();
            foreach (Toggle tg in filter_type_toggles)
            {
                if (tg != null && tg.isOn)
                {
                    CardType t = GetTypeById(tg.name.Replace("FilterTypeToggle_", ""));
                    if (t != CardType.None && !filter_state.types.Contains(t))
                        filter_state.types.Add(t);
                }
            }

            filter_state.teams.Clear();
            foreach (Toggle tg in filter_team_toggles)
            {
                if (tg != null && tg.isOn)
                {
                    TeamData team = TeamData.Get(tg.name.Replace("FilterTeamToggle_", ""));
                    if (team != null && !filter_state.teams.Contains(team))
                        filter_state.teams.Add(team);
                }
            }

            filter_state.costs.Clear();
            foreach (Toggle tg in filter_cost_toggles)
            {
                if (tg != null && tg.isOn)
                {
                    int.TryParse(tg.name.Replace("FilterCostToggle_", ""), out int cost);
                    if (!filter_state.costs.Contains(cost))
                        filter_state.costs.Add(cost);
                }
            }

            filter_state.rarities.Clear();
            foreach (Toggle tg in filter_rarity_toggles)
            {
                if (tg != null && tg.isOn)
                {
                    RarityData rarity = RarityData.Get(tg.name.Replace("FilterRarityToggle_", ""));
                    if (rarity != null && !filter_state.rarities.Contains(rarity))
                        filter_state.rarities.Add(rarity);
                }
            }

            filter_state.search = filter_search_input != null ? filter_search_input.text : "";
            filter_state.foil = filter_foil_toggle != null && filter_foil_toggle.isOn;
        }

        /// <summary>把筛选状态同步到弹层控件</summary>
        private void SetFilterToUI()
        {
            RefreshPoolSelect();

            foreach (Toggle tg in filter_type_toggles)
            {
                if (tg != null)
                {
                    CardType t = GetTypeById(tg.name.Replace("FilterTypeToggle_", ""));
                    tg.SetIsOnWithoutNotify(t != CardType.None && filter_state.types.Contains(t));
                }
            }

            foreach (Toggle tg in filter_team_toggles)
            {
                if (tg != null)
                {
                    TeamData team = TeamData.Get(tg.name.Replace("FilterTeamToggle_", ""));
                    tg.SetIsOnWithoutNotify(team != null && filter_state.teams.Contains(team));
                }
            }

            foreach (Toggle tg in filter_cost_toggles)
            {
                if (tg != null)
                {
                    int.TryParse(tg.name.Replace("FilterCostToggle_", ""), out int cost);
                    tg.SetIsOnWithoutNotify(filter_state.costs.Contains(cost));
                }
            }

            foreach (Toggle tg in filter_rarity_toggles)
            {
                if (tg != null)
                {
                    RarityData rarity = RarityData.Get(tg.name.Replace("FilterRarityToggle_", ""));
                    tg.SetIsOnWithoutNotify(rarity != null && filter_state.rarities.Contains(rarity));
                }
            }

            if (filter_search_input != null)
                filter_search_input.text = filter_state.search;
            if (filter_foil_toggle != null)
                filter_foil_toggle.SetIsOnWithoutNotify(filter_state.foil);
            RefreshSortSelect();
        }

        /// <summary>当前搜索框文本解析出的查询（按文本缓存；空文本=全部通过）</summary>
        private CardQuery Query
        {
            get
            {
                string src = filter_state.search ?? "";
                if (query_cache == null || query_cache_src != src)
                {
                    query_cache = CardQuery.Parse(src);
                    query_cache_src = src;
                }
                return query_cache;
            }
        }

        /// <summary>筛不出卡时的提示（运行时创建一次；带条件时才提示，避免空手点进来看见莫名文字）</summary>
        private void SetEmptyHint(bool show)
        {
            if (empty_hint == null)
            {
                empty_hint = UIFactory.CreateTmpText("FilterEmptyHint", transform,
                    "未匹配到卡牌：减少条件试试（点左侧「筛选助手」有语法速查与预设）",
                    UITheme.FontBody, UITheme.TextDim, TMPro.TextAlignmentOptions.Center, UIFonts.ResolveFont());
                RectTransform rt = empty_hint.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 120f);
                rt.sizeDelta = new Vector2(760f, 44f);
                empty_hint.raycastTarget = false;
            }
            if (empty_hint.gameObject.activeSelf != show)
                empty_hint.gameObject.SetActive(show);
        }

        private CardType GetTypeById(string id)
        {
            if (id == "hero") return CardType.Hero;
            if (id == "character") return CardType.Character;
            if (id == "spell") return CardType.Spell;
            if (id == "artifact") return CardType.Artifact;
            if (id == "secret") return CardType.Secret;
            if (id == "equipment") return CardType.Equipment;
            return CardType.None;
        }

        /// <summary>按筛选状态排序（sort_by: 0名称 1法力 2颜色 3稀有度）</summary>
        private void SortCards(List<CardDataQ> list)
        {
            int sign = filter_state.sort_desc ? -1 : 1;
            switch (filter_state.sort_by)
            {
                case 1: //法力值
                    list.Sort((a, b) => sign * (a.card.mana == b.card.mana ? a.card.title.CompareTo(b.card.title) : a.card.mana.CompareTo(b.card.mana)));
                    break;
                case 2: //颜色
                    list.Sort((a, b) =>
                    {
                        string ta = a.card.team != null ? a.card.team.id : "";
                        string tb = b.card.team != null ? b.card.team.id : "";
                        return sign * (ta == tb ? a.card.title.CompareTo(b.card.title) : ta.CompareTo(tb));
                    });
                    break;
                case 3: //稀有度
                    list.Sort((a, b) =>
                    {
                        int ra = a.card.rarity != null ? a.card.rarity.rank : 0;
                        int rb = b.card.rarity != null ? b.card.rarity.rank : 0;
                        return sign * (ra == rb ? a.card.title.CompareTo(b.card.title) : ra.CompareTo(rb));
                    });
                    break;
                default: //名称
                    list.Sort((a, b) => sign * a.card.title.CompareTo(b.card.title));
                    break;
            }
        }

        //---- Card grid clicks ----------

        public void OnClickCard(CardUI card)
        {
            if (!editing_deck)
            {
                CardZoomPanel.Get().ShowCard(card.GetCard(), card.GetVariant());
                return;
            }

            CardData icard = card.GetCard();
            VariantData variant = card.GetVariant();
            if (icard != null)
            {
                int in_deck = CountDeckCards(icard, variant);
                int in_deck_same = CountDeckCards(icard);
                UserData udata = Authenticator.Get().UserData;

                bool owner = IsCardOwned(udata, card.GetCard(), card.GetVariant(), in_deck + 1);
                int max_duplicate = GameplayData.Get().deck_duplicate_max;
                if (icard.rarity != null && icard.rarity.id == "mythic")
                {
                    max_duplicate = 1;
                }
                bool deck_limit = in_deck_same < max_duplicate;

                if (owner && deck_limit)
                {
                    AddDeckCard(icard, variant);
                    RefreshDeckCards();
                }
            }
        }

        public void OnClickCardRight(CardUI card)
        {
            CardZoomPanel.Get().ShowCard(card.GetCard(), card.GetVariant());
        }

        //---- Right Panel Click -------

        public void OnClickDeckLine(DeckLine line)
        {
            if (line.IsHidden() || saving)
                return;
            UserDeckData deck = line.GetUserDeck();
            RefreshDeck(deck);
            ShowDeckCards();
        }

        private void OnClickCardLine(DeckLine line)
        {
            CardData card = line.GetCard();
            VariantData variant = line.GetVariant();
            if (card != null)
            {
                RemoveDeckCard(card, variant);
            }

            RefreshDeckCards();
        }

        private void OnRightClickCardLine(DeckLine line)
        {
            CardData icard = line.GetCard();
            if (icard != null)
                CardZoomPanel.Get().ShowCard(icard, line.GetVariant());
        }

        // ---- Deck editing Click -----

        public void OnClickSaveDeck()
        {
            if (!saving)
            {
                SaveDeck();
            }
        }

        public void OnClickDeckBack()
        {
            ShowDeckList();
        }

        public void OnClickDeleteDeck()
        {
            if (editing_deck && !string.IsNullOrEmpty(current_deck_tid))
            {
                DeleteDeck(current_deck_tid);
            }
        }

        public void OnClickDeckDelete(DeckLine line)
        {
            if (line.IsHidden())
                return;
            UserDeckData deck = line.GetUserDeck();
            if (deck != null)
            {
                DeleteDeck(deck.tid);
            }
        }
        
        // ---- Getters -----

        public int CountDeckCards(CardData card, VariantData cvariant)
        {
            int count = 0;
            foreach (UserCardData ucard in deck_cards)
            {
                if (ucard.tid == card.id && ucard.variant == cvariant.id)
                    count += ucard.quantity;
            }
            return count;
        }

        public int CountDeckCards(CardData card)
        {
            int count = 0;
            foreach (UserCardData ucard in deck_cards)
            {
                if (ucard.tid == card.id)
                    count += ucard.quantity;
            }
            return count;
        }

        private bool IsCardOwned(UserData udata, CardData card, VariantData variant, int quantity)
        {
            return udata.GetCardQuantity(card, variant) >= quantity;
        }

        private string GetSelectedHeroId()
        {
            foreach (IconButton btn in hero_powers)
            {
                if (btn.IsActive())
                    return btn.value;
            }
            return "";
        }

        //-----

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            RefreshAll();
            ShowDeckList();
        }

        public static CollectionPanel Get()
        {
            return instance;
        }
    }

    public struct CardDataQ
    {
        public CardData card;
        public VariantData variant;
        public int quantity;
    }

    /// <summary>卡牌构筑界面的筛选状态</summary>
    [System.Serializable]
    public class CardFilterState
    {
        public string pool = "";                                    //卡池 key（""全部 / pack:xxx / file:xxx）
        public List<CardType> types = new List<CardType>();         //勾选种类（空=全部）
        public List<TeamData> teams = new List<TeamData>();         //勾选颜色（空=全部）
        public List<int> costs = new List<int>();                   //勾选费用（空=全部）
        public string search = "";                                  //搜索词（模糊）
        public bool foil = false;                                   //仅金卡
        public List<RarityData> rarities = new List<RarityData>();  //勾选稀有度（空=全部）
        public int sort_by = 0;                                     //0名称 1法力 2颜色 3稀有度
        public bool sort_desc = false;                              //倒序
    }
}