using System.Collections;
using System.Collections.Generic;
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

            Show();
        }

        public void RefreshCard()
        {
            ShowCard(card, variant);
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