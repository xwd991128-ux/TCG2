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
        private TextMeshProUGUI related_title;      //条标题（宽度不够时显示 "3/5" 这样的提示）
        private readonly List<GameObject> related_slots = new List<GameObject>();
        private const int RelatedMaxShow = 6;
        private const float RelatedSlotW = 86f;
        private const float RelatedSlotH = 104f;
        private const float RelatedStripH = 132f;
        private const float RelatedGap = 6f;        //与上方控件之间留的缝
        private const float RelatedPad = 12f;       //条自身左右内边距

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
                RestorePanelHeight();   //★没配相关卡牌的卡：把弹窗还原成原高（否则看过一张有相关卡的卡后，所有弹窗都被留在加高状态）
                return;
            }

            EnsureRelatedRoot();
            if (related_root == null)
                return;
            int shown = LayoutRelatedStrip(list.Count);   //★排版在"填图"之前：它决定这条放哪、能放下几个
            related_root.gameObject.SetActive(true);
            if (related_title != null)
                related_title.text = shown < list.Count
                    ? ("相关卡牌（" + shown + "/" + list.Count + "）")   //内区宽度不够 → 只列前几个，并标出来
                    : "相关卡牌";

            for (int i = 0; i < related_slots.Count; i++)
                related_slots[i].SetActive(i < shown);

            for (int i = 0; i < shown; i++)
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
            //★挂到**弹窗根**上（不是 desc 的父级）：这样"左下角"就是弹窗的左下角。
            //  挂在 desc 的父级上曾经出过事：那个容器如果不是整块内容区（只是右栏/某个局部盒子），
            //  按它算出来的位置会把这条摆到弹窗外面、被 Mask 裁掉 ⇒ 整条消失（用户实报）。
            RectTransform host = FindPanelRect();
            if (host == null)
                host = transform as RectTransform;
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
            related_title = title;
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

        // ==================== ★这条放哪（用户口径 A：放弹窗底部 + 宽度自适应，不压任何控件、不被裁） ====================
        // 旧做法是"贴在描述框 desc 的正下方"⇒ 正好压在买卖区（BUY/SELL/COST）上；宽度还写死
        //  6×86+24=540，比弹窗内区更宽 ⇒ 右侧直接被裁。现在：
        //   ① 位置 = **卡图 / 描述 / 买卖区 三者最低点的下方**（弹窗底部的空白带）⇒ 不压任何控件；
        //   ② 宽度 = 宿主实际内宽，装不下几个就只列前几个（标题标 "3/5"）⇒ 不再被裁；
        //   ③ 底部空白不够高 → 把弹窗**向下加高**（顶边不动）；加不动（有 LayoutGroup / ContentSizeFitter）就保持原样。
        private RectTransform panel_rect;
        private float base_panel_h = -1f;
        private float base_panel_pos_y;
        private float panel_extra;

        /// <summary>排版这条，返回**实际显示几个**（受宿主内宽限制，可能少于 want）。</summary>
        private int LayoutRelatedStrip(int want)
        {
            RectTransform host = related_root.parent as RectTransform;
            if (host == null)
                return 0;

            Canvas canvas = related_root.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera : null;

            //① 先给它腾地方：把面板**向下加高**（顶边不动）⇒ 面板底部多出一条只给它的空白带。
            //  最多试 3 遍：内容若是"垂直居中"锚点，面板加高会把内容也往下带（每遍净增约一半）⇒ 一遍不够，要迭代收敛。
            for (int pass = 0; pass < 3; pass++)
            {
                float band = MeasureBand(host, cam);
                if (band >= RelatedStripH + RelatedPad)
                    break;
                if (!GrowPanel(RelatedStripH + RelatedPad - band))
                    break;                    //加不了（尺寸被布局组件驱动 / 已到上限）
            }

            //② 宽度自适应：按宿主内宽收敛。宿主 rect 还没算出来（宽 <= 0，例如弹窗尚未激活）时按"最多 6 个"来，
            //  绝不能算成 0 个（那会让这条整条消失）。
            float avail_w = host.rect.width - RelatedPad * 2f;
            int fit = avail_w > RelatedSlotW
                ? Mathf.FloorToInt((avail_w - 12f) / RelatedSlotW)
                : RelatedMaxShow;
            fit = Mathf.Clamp(fit, 1, RelatedMaxShow);
            int shown = Mathf.Clamp(Mathf.Min(want, fit), 1, RelatedMaxShow);

            float need_w = 12f + shown * RelatedSlotW + RelatedPad;
            related_root.sizeDelta = new Vector2(
                Mathf.Min(need_w, Mathf.Max(avail_w, RelatedSlotW + 12f)), RelatedStripH);

            //③ 位置：**钉死在宿主左下角**（anchor/pivot 都在左下，偏移 = 内边距）。
            //  为什么不再"量现有控件的下边缘再往下摆"：那个算法要求 desc 的父级正好是整块内容区，
            //  一旦量到的值偏小或为负（父级只是局部容器 / 弹窗尚未激活 / 卡图在别的层），这条就被摆到面板**外面**、
            //  被 Mask 裁掉 ⇒ 表现就是"整条不见了"（用户实报"现在直接不显示了"）。钉左下角后，无论布局怎么变都一定在面板内。
            related_root.anchorMin = new Vector2(0f, 0f);
            related_root.anchorMax = new Vector2(0f, 0f);
            related_root.pivot = new Vector2(0f, 0f);
            related_root.anchoredPosition = new Vector2(RelatedPad, RelatedPad);
            return shown;
        }

        /// <summary>宿主底部当前空出多少高度（= 现有控件最低点的下方还剩多少）。
        /// 量到的值不可信（负数 / 超过宿主高度 ⇒ 父级不是整块内容区）时返回 0 = 当作"没有空白带"。</summary>
        private float MeasureBand(RectTransform host, Camera cam)
        {
            float band = MeasureOccupiedBottom(host, cam) - RelatedGap;
            if (band < 0f || band > host.rect.height)
                return 0f;
            return band;
        }

        /// <summary>现有控件（卡图 / 描述 / 买卖区）里最低的下边缘，返回"相对宿主底边的偏移"；
        /// 一个都量不到 → 返回宿主高度（= 整块都能用）。</summary>
        private float MeasureOccupiedBottom(RectTransform host, Camera cam)
        {
            float lowest = host.rect.height;
            bool any = false;
            lowest = TakeLowest(lowest, ref any, card_ui != null ? card_ui.transform as RectTransform : null, host, cam);
            lowest = TakeLowest(lowest, ref any, desc != null ? desc.rectTransform : null, host, cam);
            lowest = TakeLowest(lowest, ref any, trade_area != null ? trade_area.transform as RectTransform : null, host, cam);
            return any ? Mathf.Max(lowest, 0f) : host.rect.height;
        }

        private float TakeLowest(float cur, ref bool any, RectTransform child, RectTransform host, Camera cam)
        {
            //★只看"自身是否被隐藏"（activeSelf）：ShowCard 是先刷新这条、后 Show()，
            //  那一刻弹窗整体还没激活，用 activeInHierarchy 会全都量不到 ⇒ 这条会被摆到弹窗顶上。
            if (child == null || !child.gameObject.activeSelf)
                return cur;
            Vector3[] c = new Vector3[4];
            child.GetWorldCorners(c);                            //0=左下 1=左上 2=右上 3=右下
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, screen, cam, out local))
                return cur;
            any = true;
            return Mathf.Min(cur, local.y - host.rect.min.y);     //换算成"相对宿主底边"
        }

        /// <summary>把面板向下加高到"底部至少留出 want_extra 的空白"（**顶边不动**），返回是否真的改了尺寸。
        /// 只加不减（要变回原高统一走 RestorePanelHeight）；尺寸被布局组件驱动时直接放弃。</summary>
        private bool GrowPanel(float want_extra)
        {
            if (want_extra <= 0f)
                return false;
            if (panel_rect == null)
            {
                panel_rect = FindPanelRect();
                if (panel_rect == null)
                    return false;
                if (panel_rect.GetComponent<LayoutGroup>() != null || panel_rect.GetComponent<ContentSizeFitter>() != null)
                {
                    panel_rect = null;      //尺寸被别人管着：改了也会被覆盖，索性不改
                    return false;
                }
                base_panel_h = panel_rect.sizeDelta.y;
                base_panel_pos_y = panel_rect.anchoredPosition.y;
            }

            float target = Mathf.Clamp(want_extra, 0f, RelatedStripH + RelatedPad * 2f + 8f);
            if (target <= panel_extra + 0.5f)
                return false;               //已经是这么大（或更大）：不缩回去，避免"加高 ↔ 缩回"来回抖
            panel_extra = target;
            panel_rect.sizeDelta = new Vector2(panel_rect.sizeDelta.x, base_panel_h + panel_extra);
            //pivot 不在中轴上时改高度会同时带动上下两边 → 补回偏移，让**顶边留在原位**
            panel_rect.anchoredPosition = new Vector2(panel_rect.anchoredPosition.x,
                base_panel_pos_y - (1f - panel_rect.pivot.y) * panel_extra);
            return true;
        }

        /// <summary>还原弹窗原高（这张卡没有相关卡牌时）</summary>
        private void RestorePanelHeight()
        {
            if (panel_rect == null || panel_extra <= 0f)
                return;
            panel_extra = 0f;
            panel_rect.sizeDelta = new Vector2(panel_rect.sizeDelta.x, base_panel_h);
            panel_rect.anchoredPosition = new Vector2(panel_rect.anchoredPosition.x, base_panel_pos_y);
        }

        /// <summary>要加高的那个矩形：优先本组件；本组件不在面板根上时，往上找第一个"同时装得下描述与买卖区"的父级。</summary>
        private RectTransform FindPanelRect()
        {
            RectTransform self = transform as RectTransform;
            RectTransform anchor = desc != null ? desc.rectTransform : null;
            if (self == null || anchor == null || anchor == self || anchor.IsChildOf(self))
                return self;

            RectTransform other = trade_area != null ? trade_area.transform as RectTransform : null;
            RectTransform p = anchor.parent as RectTransform;
            while (p != null)
            {
                if (other == null || other == p || other.IsChildOf(p))
                    return p;
                p = p.parent as RectTransform;
            }
            return self;
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