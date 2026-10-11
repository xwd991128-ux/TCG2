using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// When clicking on a card in menu, a box will appear with additional game info
    /// You can also buy cards in this panel
    /// </summary>

    public class CardZoomPanel : UIPanel
    {
        public CardUI card_ui;
        public Text desc;
        public Image quantity_bar;
        public Text quantity_txt;

        public GameObject trade_area;
        public InputField trade_quantity;
        public Text buy_cost;
        public Text sell_cost;
        public Text trade_error;

        private CardData card;
        private VariantData variant;

        private static CardZoomPanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            TabButton.onClickAny += OnClickTab;
        }

        private void OnDestroy()
        {
            TabButton.onClickAny -= OnClickTab;
        }

        protected override void Update()
        {
            base.Update();

            if (card != null && buy_cost != null && sell_cost != null)
            {
                if (!IsTradeable())
                {
                    //价格缺失时不能显示 "0"（会被读成"免费买"）→ 显示占位符，按钮已由 ShowCard 置灰
                    buy_cost.text = "—";
                    sell_cost.text = "—";
                    return;
                }
                int quantity = GetBuyQuantity();
                int cost = quantity * card.cost * variant.cost_factor;
                buy_cost.text = cost.ToString();
                sell_cost.text = Mathf.RoundToInt(cost * GameplayData.Get().sell_ratio).ToString();
            }
        }

        /// <summary>本卡是否可买卖（有价格才可交易）。价格=卡牌编辑器 → 卡牌参数 → 购买价。</summary>
        private bool IsTradeable()
        {
            return card != null && variant != null && card.deckbuilding && card.cost > 0;
        }

        /// <summary>买卖区整体可交互性切换（价格缺失时置灰，而不是把整块藏起来）</summary>
        private void SetTradeInteractable(bool on)
        {
            if (trade_area == null)
                return;
            foreach (Button b in trade_area.GetComponentsInChildren<Button>(true))
            {
                if (b != null)
                    b.interactable = on;
            }
            if (trade_quantity != null)
                trade_quantity.interactable = on;
        }

        public void ShowCard(CardData card, VariantData variant)
        {
            this.card = card;
            this.variant = variant;

            UserData udata = Authenticator.Get().UserData;
            int quantity = udata != null ? udata.GetCardQuantity(card, variant) : 0;
            quantity_txt.text = quantity.ToString();
            quantity_txt.enabled = quantity > 0;
            quantity_bar.enabled = quantity > 0;
            trade_quantity.text = "1";

            //★买卖区不再"购买价=0 就整块隐藏"：
            //  工坊自定义卡默认购买价=0，整块隐藏会让用户以为"这个界面没有买卖功能"（用户实报：
            //  "现在可以选了，但是没有买卖画面"）。改为**始终显示**，价格缺失时把数量/买卖按钮置灰，
            //  并在 trade_error 里写清怎么让它可用。
            bool tradeable = IsTradeable();
            if (trade_area != null)
                trade_area.SetActive(card != null && card.deckbuilding);
            SetTradeInteractable(tradeable);
            if (trade_error != null)
                trade_error.text = tradeable ? ""
                    : (card != null && card.deckbuilding ? "该卡未设置购买价（卡牌参数 → 购买价，填 > 0 即可买卖）" : "");

            card_ui.SetCard(card, variant);
            string desc = card.GetDesc();
            string adesc = card.GetAbilitiesDesc();
            if(!string.IsNullOrWhiteSpace(desc))
                this.desc.text = desc + "\n\n" + adesc;
            else
                this.desc.text = adesc;

            //★相关卡牌：与本卡一起展示（卡牌属性里配的 related_card_ids；没配则整条隐藏）
            RefreshRelatedCards(card, variant);

            Show();
        }

        public void RefreshCard()
        {
            ShowCard(card, variant);
        }

        // ==================== ★相关卡牌（卡牌浏览里与本体一起展示、可点进去看） ====================
        // 数据来源：卡牌属性「相关卡牌」= CardData.related_card_ids（卡池 JSON 字段 related_card_ids）。
        // 纯展示：不影响任何对局规则与数值。

        private RectTransform related_root;
        private readonly List<GameObject> related_slots = new List<GameObject>();
        private const int RelatedMaxShow = 6;
        private const float RelatedSlotW = 86f;
        private const float RelatedSlotH = 104f;
        private const float RelatedStripH = 132f;

        /// <summary>按当前卡的相关卡牌刷新这条；无配置/无有效卡 → 整条隐藏。</summary>
        private void RefreshRelatedCards(CardData c, VariantData v)
        {
            List<CardData> list = new List<CardData>();
            if (c != null && c.related_card_ids != null)
            {
                foreach (string id in c.related_card_ids)
                {
                    if (string.IsNullOrEmpty(id))
                        continue;
                    CardData cd = CardData.Get(id);
                    if (cd == null)
                        continue;               //无效 id（已删除/未注册）静默忽略
                    list.Add(cd);
                    if (list.Count >= RelatedMaxShow)
                        break;
                }
            }

            if (list.Count == 0)
            {
                if (related_root != null && related_root.gameObject.activeSelf)
                    related_root.gameObject.SetActive(false);
                return;
            }

            EnsureRelatedRoot();
            if (related_root == null)
                return;
            PlaceRelatedRootBelowDesc();
            related_root.gameObject.SetActive(true);

            for (int i = 0; i < related_slots.Count; i++)
                related_slots[i].SetActive(i < list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                CardData cd = list[i];
                GameObject slot = related_slots[i];
                Image img = slot.GetComponent<Image>();
                Sprite sp = cd.GetFullArt(v);
                if (sp == null)
                    sp = cd.GetFullArt(null);
                img.sprite = sp;
                img.enabled = sp != null;
                TMP_Text t = slot.GetComponentInChildren<TMP_Text>(true);
                if (t != null)
                    t.text = string.IsNullOrEmpty(cd.title) ? cd.id : cd.title;
                Button b = slot.GetComponent<Button>();
                if (b != null)
                {
                    b.onClick.RemoveAllListeners();
                    CardData target = cd;
                    VariantData tv = v != null ? v : VariantData.GetDefault();
                    b.onClick.AddListener(() => ShowCard(target, tv));   //同一弹层里跳过去看那张卡
                }
            }
        }

        /// <summary>建一次：标题 + 最多 6 个卡位（每个 = 卡图 + 卡名，可点）。</summary>
        private void EnsureRelatedRoot()
        {
            if (related_root != null)
                return;
            Transform host = desc != null ? desc.transform.parent : transform;
            if (host == null)
                return;

            GameObject root_go = new GameObject("RelatedCards", typeof(RectTransform), typeof(Image));
            related_root = root_go.GetComponent<RectTransform>();
            related_root.SetParent(host, false);
            related_root.anchorMin = new Vector2(0f, 0f);
            related_root.anchorMax = new Vector2(0f, 0f);
            related_root.pivot = new Vector2(0f, 1f);
            related_root.sizeDelta = new Vector2(RelatedSlotW * RelatedMaxShow + 24f, RelatedStripH);
            Image bg = root_go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            bg.raycastTarget = false;

            //标题
            GameObject t_go = new GameObject("Title", typeof(RectTransform));
            t_go.transform.SetParent(related_root, false);
            TextMeshProUGUI title = t_go.AddComponent<TextMeshProUGUI>();
            title.text = "相关卡牌";
            title.fontSize = 14f;
            title.color = new Color(1f, 1f, 1f, 0.75f);
            title.alignment = TextAlignmentOptions.TopLeft;
            title.raycastTarget = false;
            UIFonts.ApplyFont(title);                 //★运行时 TMP 文本必须走工程字体管线
            RectTransform trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(8f, -4f);
            trt.sizeDelta = new Vector2(-16f, 20f);

            //卡位
            for (int i = 0; i < RelatedMaxShow; i++)
            {
                GameObject slot = new GameObject("Slot" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                slot.transform.SetParent(related_root, false);
                RectTransform srt = slot.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, 0f);
                srt.anchorMax = new Vector2(0f, 0f);
                srt.pivot = new Vector2(0f, 0f);
                srt.anchoredPosition = new Vector2(12f + i * RelatedSlotW, 12f);
                srt.sizeDelta = new Vector2(RelatedSlotW - 6f, RelatedSlotH - 20f);
                Image si = slot.GetComponent<Image>();
                si.color = Color.white;
                Button sb = slot.GetComponent<Button>();
                sb.targetGraphic = si;

                //卡名（放卡位下方，跟着卡位走）
                GameObject n_go = new GameObject("Name", typeof(RectTransform));
                n_go.transform.SetParent(slot.transform, false);
                TextMeshProUGUI name = n_go.AddComponent<TextMeshProUGUI>();
                name.fontSize = 12f;
                name.color = new Color(1f, 1f, 1f, 0.9f);
                name.alignment = TextAlignmentOptions.Bottom;
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.raycastTarget = false;
                UIFonts.ApplyFont(name);
                RectTransform nrt = name.rectTransform;
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 1f);
                nrt.anchoredPosition = new Vector2(0f, -2f);
                nrt.sizeDelta = new Vector2(-4f, 16f);

                slot.SetActive(false);
                related_slots.Add(slot);
            }
            related_root.gameObject.SetActive(false);
        }

        /// <summary>把这条贴到卡牌描述的正下方（用 desc 的实际矩形换算，避免猜布局）。
        /// 支持 ScreenSpaceOverlay / ScreenSpaceCamera 两种画布。</summary>
        private void PlaceRelatedRootBelowDesc()
        {
            if (related_root == null || desc == null || desc.rectTransform == null)
                return;
            RectTransform host = related_root.parent as RectTransform;
            if (host == null)
                return;

            Canvas canvas = related_root.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera : null;

            Vector3[] dc = new Vector3[4];
            desc.rectTransform.GetWorldCorners(dc);        //0=左下 1=左上 2=右上 3=右下
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, dc[0]);
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(host, screen, cam, out local))
            {
                //localPoint 是"相对 host 轴心"的坐标 → 换算成"相对 anchor(左下角)"：减去 rect.min
                Vector2 bottom_left_relative = local - host.rect.min;
                related_root.anchoredPosition = bottom_left_relative + new Vector2(0f, -6f);
            }
        }

        private async void BuyCardTest()
        {
            int quantity = GetBuyQuantity();
            int cost = (quantity * card.cost * variant.cost_factor);
            if (quantity <= 0)
                return;

            UserData udata = Authenticator.Get().UserData;
            if (udata.coins < cost)
                return;

            udata.AddCard(card.id, variant.id, quantity);
            udata.coins -= cost;
            await Authenticator.Get().SaveUserData();
            CollectionPanel.Get().ReloadUser();
            Hide();
        }

        private async void BuyCardApi()
        {
            BuyCardRequest req = new BuyCardRequest();
            req.card = card.id;
            req.variant = variant.id;
            req.quantity = GetBuyQuantity();

            if (req.quantity <= 0)
                return;

            string url = ApiClient.ServerURL + "/users/cards/buy/";
            string jdata = ApiTool.ToJson(req);
            trade_error.text = "";

            WebResponse res = await ApiClient.Get().SendPostRequest(url, jdata);
            if (res.success)
            {
                CollectionPanel.Get().ReloadUser();
                Hide();
            }
            else
            {
                trade_error.text = res.error;
            }
        }


        private async void SellCardTest()
        {
            int quantity = GetBuyQuantity();
            int cost = Mathf.RoundToInt(quantity * card.cost * variant.cost_factor * GameplayData.Get().sell_ratio);
            if (quantity <= 0)
                return;

            UserData udata = Authenticator.Get().UserData;
            if (!udata.HasCard(card.id, variant.id, quantity))
                return;

            udata.AddCard(card.id, variant.id, -quantity);
            udata.coins += cost;
            await Authenticator.Get().SaveUserData();
            CollectionPanel.Get().ReloadUser();
            MainMenu.Get().RefreshDeckList();
            Hide();
        }

        private async void SellCardApi()
        {
            BuyCardRequest req = new BuyCardRequest();
            req.card = card.id;
            req.variant = variant.id;
            req.quantity = GetBuyQuantity();

            if (req.quantity <= 0)
                return;

            string url = ApiClient.ServerURL + "/users/cards/sell/";
            string jdata = ApiTool.ToJson(req);
            trade_error.text = "";

            WebResponse res = await ApiClient.Get().SendPostRequest(url, jdata);
            if (res.success)
            {
                CollectionPanel.Get().ReloadUser();
                Hide();
            }
            else
            {
                trade_error.text = res.error;
            }
        }

        public void OnClickBuy()
        {
            //兜住"价格 0"的路径：否则 0 费买入 = 白送卡（按购买价 0 直接 AddCard）
            if (!IsTradeable())
            {
                if (trade_error != null)
                    trade_error.text = "该卡未设置购买价，无法购买（卡牌参数 → 购买价，填 > 0）";
                return;
            }
            if (Authenticator.Get().IsTest())
            {
                BuyCardTest();
            }
            if (Authenticator.Get().IsApi())
            {
                BuyCardApi();
            }
        }

        public void OnClickSell()
        {
            //同理：价格 0 时卖出得 0 金币却白掉一张卡，直接拒绝
            if (!IsTradeable())
            {
                if (trade_error != null)
                    trade_error.text = "该卡未设置购买价，无法出售（卡牌参数 → 购买价，填 > 0）";
                return;
            }
            if (Authenticator.Get().IsTest())
            {
                SellCardTest();
            }
            if (Authenticator.Get().IsApi())
            {
                SellCardApi();
            }
        }

        private void OnClickTab(TabButton btn)
        {
            if (btn.group == "menu")
                Hide();
        }

        public int GetBuyQuantity()
        {
            bool success = int.TryParse(trade_quantity.text, out int quantity);
            if (success)
                return quantity;
            return 0;
        }

        public CardData GetCard()
        {
            return card;
        }

        public string GetCardId()
        {
            return card.id;
        }

        public string GetCardVariant()
        {
            return variant.id;
        }

        public static CardZoomPanel Get()
        {
            return instance;
        }
    }
}