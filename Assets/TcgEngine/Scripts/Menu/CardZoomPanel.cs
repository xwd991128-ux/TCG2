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

        //★"返回上一张"的历史：点相关卡牌跳过去时把当前这张压栈；非空时才显示「◀ 返回」按钮
        private readonly List<KeyValuePair<CardData, VariantData>> nav_history = new List<KeyValuePair<CardData, VariantData>>();
        private GameObject back_btn_go;

        private static CardZoomPanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            TabButton.onClickAny += OnClickTab;

            ApplyPanelTitle();        //★左上角标题：场景里是英文占位 "CARD" ⇒ 统一改成「卡牌介绍」（所有卡共用这个弹窗）
            ApplyDescOverflowFix();   //★描述文字溢出边框：强制按框宽换行 + 超高截断
            EnsureBackButton();       //★「◀ 返回」按钮（点相关卡牌进去后能回到原卡）
        }

        private void OnDestroy()
        {
            TabButton.onClickAny -= OnClickTab;
        }

        /// <summary>左上角标题文字。工程场景里那个对象叫 Box/Text、内容是英文占位 "CARD"，
        /// 而且它**没有被任何序列化字段引用** ⇒ 这里按名字找（好处：不用改场景/prefab，
        /// 也就不存在"动到场景"的风险）。</summary>
        private void ApplyPanelTitle()
        {
            Transform t = transform.Find("Box/Text");
            if (t == null)
                return;
            Text txt = t.GetComponent<Text>();
            if (txt != null)
            {
                txt.text = "卡牌介绍";
                return;
            }
            TextMeshProUGUI tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = "卡牌介绍";
        }

        /// <summary>描述文字溢出边框：工程这里用的是旧版 Text，默认不换行 ⇒ 长描述会直接画到框外。
        /// 统一成"按框宽换行 + 超出高度截断"，任何卡的长描述都不会再越界。</summary>
        private void ApplyDescOverflowFix()
        {
            if (desc == null)
                return;
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ==================== ★「◀ 返回」：点相关卡牌进去后回到原卡 ====================
        // 需求（用户）：点相关卡牌后怎么回到原来那张卡？之前是直接替换、没有回头路 ⇒ 加一个返回栈 + 返回按钮。
        // 按钮运行时创建（不碰场景/prefab）；位置与「卡牌介绍」同一行、放在它右边。
        // 锚点用 (0, 0.5)（与标题同款"垂直居中"锚）⇒ 加高弹窗时会被 CompensateCenterChildren 一起补回原位，不会漂。

        private void EnsureBackButton()
        {
            if (back_btn_go != null)
                return;
            Transform host = transform.Find("Box");
            if (host == null)
                return;

            back_btn_go = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button));
            back_btn_go.transform.SetParent(host, false);
            RectTransform rt = back_btn_go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(132f, 46f);
            rt.anchoredPosition = new Vector2(390f, 319f);      //与标题同一行、放它右边

            Image img = back_btn_go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.10f);
            Button b = back_btn_go.GetComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(OnClickBack);

            GameObject t = new GameObject("T", typeof(RectTransform));
            t.transform.SetParent(rt, false);
            TextMeshProUGUI txt = t.AddComponent<TextMeshProUGUI>();
            txt.text = "◀ 返回";
            txt.fontSize = 22f;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
            UIFonts.ApplyFont(txt);
            RectTransform trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            back_btn_go.SetActive(false);
        }

        /// <summary>回到上一张（栈顶）</summary>
        private void OnClickBack()
        {
            if (nav_history.Count == 0)
                return;
            int last = nav_history.Count - 1;
            KeyValuePair<CardData, VariantData> prev = nav_history[last];
            nav_history.RemoveAt(last);
            ShowCardInternal(prev.Key, prev.Value);
        }

        /// <summary>★关弹窗时把"为相关卡牌条临时加高的宿主"还原。
        /// 为什么必须还原：本工程关掉了 Play 的"重载域/重载场景"（AutoBattleRunner 注释里也记了这点），
        /// 运行期对场景对象的改动会**留在编辑器场景上** ⇒ 下次进 Play 弹窗就是错的（实测：Box 从 729.25
        /// 被留在 902.58，中心锚点的标题因此下漂 86.7 ⇒ 用户看到的"左上角 CARD 偏移"）。</summary>
        public override void Hide(bool instant = false)
        {
            base.Hide(instant);
            RestorePanelHeight();
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

        /// <summary>外部入口（卡牌浏览 / 卡组里点开一张卡）：先清空"返回上一张"的历史再展示。</summary>
        public void ShowCard(CardData card, VariantData variant)
        {
            nav_history.Clear();
            ShowCardInternal(card, variant);
        }

        /// <summary>真正的展示逻辑。返回栈由调用方维护：
        ///   · 列表里点开一张卡 → 清空（见 ShowCard）
        ///   · 点「相关卡牌」跳过去 → 先把当前这张压栈（见 RefreshRelatedCards 里的点击回调）
        ///   · RefreshCard（数值刷新）→ 不动历史</summary>
        private void ShowCardInternal(CardData card, VariantData variant)
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

            //★有"上一张"可回才显示「◀ 返回」（从列表直接点开时不显示）
            if (back_btn_go != null)
                back_btn_go.SetActive(nav_history.Count > 0);

            Show();
        }

        public void RefreshCard()
        {
            ShowCardInternal(card, variant);   //数值刷新：不打乱"返回上一张"的历史
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
                    b.onClick.AddListener(() =>
                    {
                        //★跳过去看这张相关卡牌之前，先把"当前这张"压栈 ⇒ 左上角会出现「◀ 返回」点回来
                        if (this.card != null)
                            nav_history.Add(new KeyValuePair<CardData, VariantData>(this.card, this.variant));
                        ShowCardInternal(target, tv);
                    });
                }
            }
        }

        /// <summary>建一次：标题 + 最多 6 个卡位（每个 = 卡图 + 卡名，可点）。</summary>
        private void EnsureRelatedRoot()
        {
            if (related_root != null)
                return;
            //★宿主 = **卡图 / 描述 / 买卖区 的最低共同祖先**（探针实测 = CardZoomPanel/Box，那个 1248×729 的弹窗内盒）。
            //  两条用真机数据换来的教训：
            //   · 挂"面板根 CardZoomPanel"→ 它是**全屏拉伸**的 1728×1080 ⇒ 条会被钉到**屏幕左下角** ✗；
            //   · 挂"desc 的父级"在别的层级下也可能不是整块内盒 ⇒ 曾被摆到面板外、被裁掉，整条消失 ✗。
            //  LCA 一定是"看得见的那个盒子"，而且它有固定尺寸 ⇒ 加高它才有意义（拉伸的全屏根加高等于没加）。
            RectTransform host = FindStripHost();
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
        private RectTransform host_rect;        //这条的宿主（弹窗内盒）：加高它，给这条腾出底部空白带
        private float base_host_h;
        private float base_host_pos_y;
        private float host_extra;
        private bool grow_up;                   //加高方向：底下不够放这条 ⇒ 改"底边不动、向上长"
        private float room_below_u;             //宿主底边到屏幕底的距离（宿主单位）
        private float room_above_u;             //宿主顶边到屏幕顶的距离（宿主单位）
        //"锚在中心"的子控件（标题/卡图/描述/买卖区）加高前的 anchoredPosition：加高时补回、还原时写回
        private readonly Dictionary<RectTransform, Vector2> child_base_pos = new Dictionary<RectTransform, Vector2>();

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
            for (int pass = 0; pass < 4; pass++)
            {
                float band = MeasureBand(host, cam);
                if (band >= RelatedStripH + RelatedPad)
                    break;
                if (!GrowPanel(host, RelatedStripH + RelatedPad - band))
                    break;                    //加不了（尺寸被布局组件驱动 / 拉伸容器 / 已到上限）
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

        /// <summary>把宿主（弹窗内盒）向下加高 want_extra，让底部至少留出这么多空白（**顶边不动**）。
        /// 只加不减（要变回原高统一走 RestorePanelHeight）；以下情况直接放弃加高：
        ///   全屏拉伸的容器（anchorMin != anchorMax，改 sizeDelta 会把布局搞坏）、尺寸被 LayoutGroup / ContentSizeFitter 驱动。</summary>
        private bool GrowPanel(RectTransform host, float want_extra)
        {
            if (host == null || want_extra <= 0f)
                return false;
            if (host_rect != null && host_rect != host)
                return false;               //换了宿主：先 Restore 再重来，避免把上一个盒子的高度留下
            if (host_rect == null)
            {
                if (host.anchorMin != host.anchorMax)
                    return false;           //拉伸容器
                if (host.GetComponent<LayoutGroup>() != null || host.GetComponent<ContentSizeFitter>() != null)
                    return false;           //尺寸被别人管着
                host_rect = host;
                base_host_h = host.sizeDelta.y;
                base_host_pos_y = host.anchoredPosition.y;
                PickGrowDirection(host);
            }

            //★累加式（每次把"当前还差的量"加到已加的量上）：宿主里是"居中锚点"的子控件时，
            //  盒子加高 e 只会让底部空白净增约 e/2（子控件跟着走一半）⇒ 一次算不准，靠外面那个循环迭代收敛。
            float cap = Mathf.Min(RelatedStripH * 2f + 40f,
                Mathf.Max(0f, (grow_up ? room_above_u : room_below_u) - 2f));   //别把盒子长出屏幕
            float target = Mathf.Clamp(host_extra + want_extra, 0f, cap);
            if (target <= host_extra + 0.5f)
                return false;
            float delta = target - host_extra;
            host_extra = target;
            host.sizeDelta = new Vector2(host.sizeDelta.x, base_host_h + host_extra);
            //改高度会带动上下两边 → 补回偏移：向下长时**顶边**不动，向上长时**底边**不动
            host.anchoredPosition = new Vector2(host.anchoredPosition.x,
                base_host_pos_y + (grow_up ? host.pivot.y : -(1f - host.pivot.y)) * host_extra);
            //★再把"锚在中心"的子控件反向补回（它们会随中心移动）⇒ 视觉上它们原地不动，只有盒子底边多出一条空白
            CompensateCenterChildren(host, delta);
            return true;
        }

        /// <summary>还原宿主原高（这张卡没有相关卡牌 / 关弹窗时）—— 连同下面那些被补偿过的子控件一起还原</summary>
        private void RestorePanelHeight()
        {
            if (host_rect == null || host_extra <= 0f)
                return;
            host_extra = 0f;
            host_rect.sizeDelta = new Vector2(host_rect.sizeDelta.x, base_host_h);
            host_rect.anchoredPosition = new Vector2(host_rect.anchoredPosition.x, base_host_pos_y);
            foreach (KeyValuePair<RectTransform, Vector2> kv in child_base_pos)
            {
                if (kv.Key != null)
                    kv.Key.anchoredPosition = kv.Value;
            }
            child_base_pos.Clear();
        }

        /// <summary>★把"垂直锚在中心"的子控件（标题 / 卡图 / 描述 / 买卖区…）反向补回原位。
        /// 为什么必须补：盒子加高 e，它的**中心**就会移动 e/2（向下长↓）⇒ 锚在中心的子控件跟着漂，
        /// 表现就是"这张卡打开时左上角标题偏移了"（用户实测：无相关卡牌的卡正常、有相关卡牌的卡标题下漂 ✗）。
        /// 补偿后这些子控件在屏幕上**一动不动**，只有盒子底边多出一条只给"相关卡牌"用的空白。
        /// 原值记进 child_base_pos，RestorePanelHeight 会精确写回（否则这份补偿也会残留到编辑器场景里）。</summary>
        private void CompensateCenterChildren(RectTransform host, float delta)
        {
            if (host == null || Mathf.Approximately(delta, 0f))
                return;
            float shift = (grow_up ? -0.5f : 0.5f) * delta;   //中心下移 delta/2（向下长）⇒ 子控件 +delta/2 抵消
            for (int i = 0; i < host.childCount; i++)
            {
                RectTransform c = host.GetChild(i) as RectTransform;
                if (c == null || c == related_root)
                    continue;                                  //这条自己不算
                if (!Mathf.Approximately(c.anchorMin.y, c.anchorMax.y) || !Mathf.Approximately(c.anchorMin.y, 0.5f))
                    continue;                                  //只处理"垂直锚在中心"的（其余不随高度移动）
                if (c.GetComponent<LayoutGroup>() != null)
                    continue;                                  //被布局组件管的别动
                if (!child_base_pos.ContainsKey(c))
                    child_base_pos[c] = c.anchoredPosition;
                Vector2 v = c.anchoredPosition;
                c.anchoredPosition = new Vector2(v.x, v.y + shift);
            }
        }

        /// <summary>加高方向：宿主下方剩的空间够放这条 ⇒ 向下长（顶边不动）；不够 ⇒ 向上长（底边不动）。
        /// 为什么必须判断：探针实测"向下长"会把 Box 底边推到世界 y=-5.7，而屏幕底是 -5 ⇒ 这条下半截被屏幕边缘切掉。
        /// 顺手把两侧剩余空间记下来，当作加高上限（别把盒子长出屏幕）。</summary>
        private void PickGrowDirection(RectTransform host)
        {
            grow_up = false;
            room_below_u = RelatedStripH * 2f;
            room_above_u = RelatedStripH * 2f;

            Canvas cv = host.GetComponentInParent<Canvas>();
            if (cv == null)
                return;
            Rect hostW = WorldRectOf(host);
            Rect cvW = WorldRectOf(cv.transform as RectTransform);
            float per_unit = (host.rect.height > 0.01f && hostW.height > 0.01f)
                ? hostW.height / host.rect.height : 1f;       //1 个宿主单位 = 多少世界单位
            if (per_unit <= 0.0001f)
                per_unit = 1f;

            room_below_u = (hostW.yMin - cvW.yMin) / per_unit;
            room_above_u = (cvW.yMax - hostW.yMax) / per_unit;
            float need = RelatedStripH + RelatedPad;
            grow_up = room_below_u < need && room_above_u > room_below_u;
        }

        private static Rect WorldRectOf(RectTransform rt)
        {
            if (rt == null)
                return new Rect();
            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        /// <summary>这条的宿主：卡图 / 描述 / 买卖区 的**最低共同祖先**（= 看得见的弹窗内盒）。
        /// 探针实测：本工程 = CardZoomPanel/Box（1248×729，非拉伸）；面板根是全屏拉伸(1728×1080)，不能当宿主。</summary>
        private RectTransform FindStripHost()
        {
            RectTransform lca = null;
            lca = Lca(lca, card_ui != null ? card_ui.transform as RectTransform : null);
            lca = Lca(lca, desc != null ? desc.rectTransform : null);
            lca = Lca(lca, trade_area != null ? trade_area.transform as RectTransform : null);
            if (lca != null)
                return lca;
            if (desc != null && desc.transform.parent != null)
                return desc.transform.parent as RectTransform;
            return transform as RectTransform;
        }

        private static RectTransform Lca(RectTransform x, RectTransform y)
        {
            if (x == null) return y;
            if (y == null) return x;
            HashSet<Transform> chain = new HashSet<Transform>();
            for (Transform t = x; t != null; t = t.parent)
                chain.Add(t);
            for (Transform t = y; t != null; t = t.parent)
            {
                if (chain.Contains(t))
                    return t as RectTransform;
            }
            return x;
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