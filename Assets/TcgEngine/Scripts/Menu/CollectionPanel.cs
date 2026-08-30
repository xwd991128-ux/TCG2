using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

        //筛选弹层控件引用（运行时 Find 绑定）
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

        private void SpawnCards()
        {
            spawned = true;
            foreach (CollectionCard card in all_list)
                Destroy(card.gameObject);
            all_list.Clear();

            foreach (VariantData variant in VariantData.GetAll())
            {
                foreach (CardData card in CardData.GetAll())
                {
                    GameObject nCard = Instantiate(card_prefab, grid_content.transform);
                    CollectionCard dCard = nCard.GetComponent<CollectionCard>();
                    dCard.SetCard(card, variant, 0);
                    dCard.onClick += OnClickCard;
                    dCard.onClickRight += OnClickCardRight;
                    all_list.Add(dCard);
                    nCard.SetActive(false);
                }
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

                string search = filter_state.search != null ? filter_state.search.ToLower() : "";
                if (!string.IsNullOrWhiteSpace(search)
                    && !icard.id.Contains(search)
                    && !icard.title.ToLower().Contains(search)
                    && !icard.GetText().ToLower().Contains(search))
                    continue;

                shown_cards.Add(card);
            }

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

            deck_quantity.text = count + "/" + GameplayData.Get().deck_size;
            deck_quantity.color = count >= GameplayData.Get().deck_size ? Color.white : Color.red;

            RefreshCardsQuantities();
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

            if (filter_pool_dd != null)
                filter_pool_dd.onValueChanged.AddListener((v) => OnChangeFilterPool());
            if (filter_apply_btn != null)
                filter_apply_btn.onClick.AddListener(ApplyFilter);
            if (filter_clear_btn != null)
                filter_clear_btn.onClick.AddListener(ClearAllFilter);
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

        private void OnChangeFilterPool()
        {
            if (filter_pool_dd != null && filter_pool_dd.value < pool_keys.Count)
                filter_state.pool = pool_keys[filter_pool_dd.value];
        }

        /// <summary>重建卡池下拉选项（全部 + 内置卡包 + 本地卡池），并恢复当前选择</summary>
        private void RefreshPoolOptions()
        {
            if (filter_pool_dd == null)
                return;

            List<CardPoolIO.PoolOption> options = CardPoolIO.GetPoolOptions();
            filter_pool_dd.ClearOptions();
            pool_keys.Clear();

            List<string> labels = new List<string>();
            foreach (CardPoolIO.PoolOption opt in options)
            {
                pool_keys.Add(opt.key);
                labels.Add(opt.label);
            }
            filter_pool_dd.AddOptions(labels);

            int idx = pool_keys.IndexOf(filter_state.pool);
            filter_pool_dd.SetValueWithoutNotify(idx < 0 ? 0 : idx);
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
            filter_state.pool = "";
            if (filter_pool_dd != null && filter_pool_dd.value < pool_keys.Count)
                filter_state.pool = pool_keys[filter_pool_dd.value];

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
            filter_state.sort_by = filter_sort_by_dd != null ? filter_sort_by_dd.value : 0;
            filter_state.sort_desc = filter_sort_dir_dd != null && filter_sort_dir_dd.value == 1;
        }

        /// <summary>把筛选状态同步到弹层控件</summary>
        private void SetFilterToUI()
        {
            if (filter_pool_dd != null)
            {
                int idx = pool_keys.IndexOf(filter_state.pool);
                filter_pool_dd.SetValueWithoutNotify(idx < 0 ? 0 : idx);
            }

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
            if (filter_sort_by_dd != null)
                filter_sort_by_dd.SetValueWithoutNotify(filter_state.sort_by);
            if (filter_sort_dir_dd != null)
                filter_sort_dir_dd.SetValueWithoutNotify(filter_state.sort_desc ? 1 : 0);
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