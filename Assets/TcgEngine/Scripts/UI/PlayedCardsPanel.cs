using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine;
using TcgEngine.Client;

namespace TcgEngine.UI
{
    public class PlayedCardsPanel : MonoBehaviour
    {
        public RectTransform content;
        public CardLine card_line_prefab;
        public float line_height = 50f;
        public float line_spacing = 5f;

        private List<CardLine> line_objects = new List<CardLine>();
        private bool visible = false;
        private Player player;
        private int current_tab = 0;

        void Awake()
        {
            SetupCloseButton();
            SetupTabs();
            gameObject.SetActive(false);
        }

        private void SetupCloseButton()
        {
            Transform header = transform.Find("Header");
            if (header == null) return;
            Transform closeBtn = header.Find("CloseButton");
            if (closeBtn == null) return;
            Button button = closeBtn.GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(Hide);
        }

        private void SetupTabs()
        {
            Transform tabCard = transform.Find("TabBar/TabCard");
            Transform tabAbility = transform.Find("TabBar/TabAbility");
            
            if (tabCard != null)
            {
                Button btn = tabCard.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => SwitchTab(0));
            }
            
            if (tabAbility != null)
            {
                Button btn = tabAbility.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => SwitchTab(1));
            }
        }

        private void SwitchTab(int tab)
        {
            current_tab = tab;
            Refresh();
            UpdateTabStyles();
        }

        private void UpdateTabStyles()
        {
            Transform tabBar = transform.Find("TabBar");
            if (tabBar == null) return;

            Transform tabCard = tabBar.Find("TabCard");
            Transform tabAbility = tabBar.Find("TabAbility");

            if (tabCard != null)
            {
                Image img = tabCard.GetComponent<Image>();
                if (img != null)
                    img.color = current_tab == 0 ? new Color(0.3f, 0.3f, 0.8f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
            }

            if (tabAbility != null)
            {
                Image img = tabAbility.GetComponent<Image>();
                if (img != null)
                    img.color = current_tab == 1 ? new Color(0.3f, 0.3f, 0.8f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
            }
        }

        public void Show(Player p)
        {
            player = p;
            visible = true;
            gameObject.SetActive(true);
            UpdateTabStyles();
            Refresh();
        }

        public void Hide()
        {
            visible = false;
            gameObject.SetActive(false);
        }

        public bool IsVisible()
        {
            return visible;
        }

        public void Refresh()
        {
            Debug.Log("Refresh: player = " + (player != null ? player.username : "null"));
            Debug.Log("Refresh: content = " + (content != null ? "not null" : "null"));
            Debug.Log("Refresh: card_line_prefab = " + (card_line_prefab != null ? "not null" : "null"));
            
            foreach (CardLine line in line_objects)
            {
                if (line != null)
                    Destroy(line.gameObject);
            }
            line_objects.Clear();

            if (player == null)
                return;

            if (current_tab == 0)
                RefreshCards();
            else
                RefreshAbilities();
        }

        private void RefreshCards()
        {
            Debug.Log("RefreshCards: player.cards_board count = " + player.cards_board.Count);
            Debug.Log("RefreshCards: player.cards_discard count = " + player.cards_discard.Count);
            
            Dictionary<string, int> card_counts = new Dictionary<string, int>();
            Dictionary<string, CardData> card_data_map = new Dictionary<string, CardData>();

            List<Card> all_cards = new List<Card>();
            all_cards.AddRange(player.cards_board);
            all_cards.AddRange(player.cards_discard);

            foreach (Card card in all_cards)
            {
                CardData card_data = card.CardData;
                Debug.Log("RefreshCards: card = " + (card_data != null ? card_data.title : "null"));
                if (card_data != null)
                {
                    string card_id = card_data.id;
                    if (card_counts.ContainsKey(card_id))
                    {
                        card_counts[card_id]++;
                    }
                    else
                    {
                        card_counts[card_id] = 1;
                        card_data_map[card_id] = card_data;
                    }
                }
            }

            Debug.Log("RefreshCards: unique cards count = " + card_counts.Count);
            
            List<CardData> sorted_cards = new List<CardData>(card_data_map.Values);
            sorted_cards.Sort((a, b) => a.mana.CompareTo(b.mana));

            float y = 0;
            foreach (CardData card_data in sorted_cards)
            {
                CardLine line = CreateLine();
                if (line != null)
                {
                    line.SetLine(card_data, card_counts[card_data.id]);
                    RectTransform rt = line.GetComponent<RectTransform>();
                    rt.SetParent(content, false);
                    rt.anchoredPosition = new Vector2(0, y);
                    line_objects.Add(line);
                    y -= (line_height + line_spacing);
                }
            }

            UpdateContentSize(y);
            Debug.Log("RefreshCards: total lines created = " + line_objects.Count);
        }

        private void RefreshAbilities()
        {
            float y = 0;

            if (player.hero != null)
            {
                List<AbilityData> abilities = player.hero.GetAbilities();
                foreach (AbilityData ability in abilities)
                {
                    CardLine line = CreateLine();
                    if (line != null)
                    {
                        line.SetAbility(ability, 1);
                        RectTransform rt = line.GetComponent<RectTransform>();
                        rt.SetParent(content, false);
                        rt.anchoredPosition = new Vector2(0, y);
                        line_objects.Add(line);
                        y -= (line_height + line_spacing);
                    }
                }
            }

            UpdateContentSize(y);
        }

        private void UpdateContentSize(float y)
        {
            if (content != null)
            {
                float height = Mathf.Max(0, -y + line_spacing);
                content.sizeDelta = new Vector2(content.sizeDelta.x, height);
            }
        }

        private CardLine CreateLine()
        {
            if (card_line_prefab != null)
            {
                CardLine line = Instantiate(card_line_prefab);
                return line;
            }
            return null;
        }
    }
}
