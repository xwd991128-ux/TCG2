using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;                 //★TMP_Text（手牌上限显示）；漏了这行会整包编译失败
using TcgEngine.Client;
using TcgEngine.UI;

namespace TcgEngine.Client
{
    /// <summary>
    /// Area where all the hand cards are
    /// Will take card of spawning/despawning hand cards based on the refresh data received from server
    /// </summary>

    public class HandCardArea : MonoBehaviour
    {
        public GameObject card_prefab;
        public RectTransform card_area;
        public float card_spacing = 100f;
        public float card_angle = 10f;
        public float card_offset_y = 10f;

        [Header("手牌上限显示（可留空：运行时会自动建一个，也可手动拖一个 TMP 文本进来）")]
        public TMP_Text hand_limit_label;

        private List<HandCard> cards = new List<HandCard>();

        private bool is_dragging;

        private string last_destroyed;
        private float last_destroyed_timer = 0f;

        private static HandCardArea _instance;

        void Awake()
        {
            _instance = this;
        }

        void Update()
        {
            if (!GameClient.Get().IsReady())
                return;

            int player_id = GameClient.Get().GetPlayerID();
            Game data = GameClient.Get().GetGameData();
            Player player = data.GetPlayer(player_id);

            last_destroyed_timer += Time.deltaTime;

            //Add new cards
            foreach (Card card in player.cards_hand)
            {
                if (!HasCard(card.uid))
                    SpawnNewCard(card);
            }

            //Remove destroyed cards
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                HandCard card = cards[i];
                if (card == null || player.GetHandCard(card.GetCard().uid) == null)
                {
                    cards.RemoveAt(i);
                    if(card != null)
                        card.Kill();
                }
            }

            //Set card index
            int index = 0;
            float count_half = cards.Count / 2f;
            foreach (HandCard card in cards)
            {
                card.deck_position = new Vector2((index - count_half) * card_spacing, (index - count_half) * (index - count_half) * -card_offset_y);
                card.deck_angle = (index - count_half) * -card_angle;
                index++;
            }

            //Set target forcus
            HandCard drag_card = HandCard.GetDrag();
            is_dragging = drag_card != null;

            //手牌上限显示（张数/上限）
            UpdateHandLimitLabel(player);
        }

        /// <summary>刷新"手牌 n/上限"显示。上限 = Player.GetHandMax()（配置值 + 局内节点改动）。
        /// 满了变红，让"抽不动"这件事在界面上看得见（此前全项目没有任何地方显示手牌上限）。</summary>
        private void UpdateHandLimitLabel(Player player)
        {
            EnsureHandLimitLabel();
            if (hand_limit_label == null || player == null)
                return;

            int cur = player.cards_hand.Count;
            int max = player.GetHandMax();
            string txt = "手牌 " + cur + "/" + max;

            //脏检查：值没变就不拼字符串、不写 TMP（与本项目 PlayerUI 的灵力文本同一套写法）
            if (hand_limit_label.text != txt)
                hand_limit_label.text = txt;

            Color want = cur >= max ? new Color(1f, 0.45f, 0.35f, 1f) : UITheme.TextDim;   //满=红，常态=次要文字色
            if (hand_limit_label.color != want)
                hand_limit_label.color = want;
        }

        /// <summary>惰性创建显示文本（场景里已手动指定则直接用）。运行时新建文本必须走项目字体管线
        /// UIFonts.ApplyFont，否则会出现"字体不一致/发糊/缺字方块"。</summary>
        private void EnsureHandLimitLabel()
        {
            if (hand_limit_label != null)
                return;

            Transform parent = card_area != null ? card_area.parent : transform;   //挂到手牌区所在层，避免随卡片布局移动
            if (parent == null)
                parent = transform;

            GameObject go = new GameObject("HandLimitLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = 24f;
            t.alignment = TextAlignmentOptions.BottomRight;
            t.raycastTarget = false;
            t.text = "";
            UIFonts.ApplyFont(t);   //★统一字体管线

            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-24f, 16f);
            rt.sizeDelta = new Vector2(260f, 40f);

            hand_limit_label = t;
        }

        public void SpawnNewCard(Card card)
        {
            GameObject card_obj = Instantiate(card_prefab, card_area.transform);
            card_obj.GetComponent<HandCard>().SetCard(card);
            card_obj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);
            cards.Add(card_obj.GetComponent<HandCard>());
        }

        public void DelayRefresh(Card card)
        {
            last_destroyed_timer = 0f;
            last_destroyed = card.uid;
        }

		public void SortCards()
        {
            cards.Sort(SortFunc);

            int i = 0;
            foreach (HandCard acard in cards)
            {
                acard.transform.SetSiblingIndex(i);
                i++;
            }
        }

        private int SortFunc(HandCard a, HandCard b)
        {
            return a.transform.position.x.CompareTo(b.transform.position.x);
        }

        public bool HasCard(string card_uid)
        {
            HandCard card = HandCard.Get(card_uid);
            bool just_destroyed = card_uid == last_destroyed && last_destroyed_timer < 0.7f;
            return card != null || just_destroyed;
        }

        public bool IsDragging()
        {
            return is_dragging;
        }


        public static HandCardArea Get()
        {
            return _instance;
        }
    }
}