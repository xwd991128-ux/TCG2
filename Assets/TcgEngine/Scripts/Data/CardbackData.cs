using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines all cardback data
    /// </summary>

    [CreateAssetMenu(fileName = "Cardback", menuName = "TcgEngine/Cardback", order = 10)]
    public class CardbackData : ScriptableObject
    {
        public string id;
        public Sprite cardback;
        public Sprite deck;
        public int sort_order;

        public static List<CardbackData> cardback_list = new List<CardbackData>();
        private static Dictionary<string, CardbackData> cardback_dict = new Dictionary<string, CardbackData>();  //id → 数据（O(1) 查找）
        private static int cardback_dict_count = -1;                                                              //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (cardback_list.Count == 0)
                cardback_list.AddRange(Resources.LoadAll<CardbackData>(folder));

            cardback_list.Sort((CardbackData a, CardbackData b) => {
                if (a.sort_order == b.sort_order)
                    return a.id.CompareTo(b.id);
                else
                    return a.sort_order.CompareTo(b.sort_order);
            });
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等；列表条数变化或首次访问时自动调用）</summary>
        public static void RebuildDict()
        {
            cardback_dict.Clear();
            foreach (CardbackData c in cardback_list)
            {
                if (c != null && !string.IsNullOrEmpty(c.id))
                    cardback_dict[c.id] = c;
            }
            cardback_dict_count = cardback_list.Count;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，而 BoardDeck.Update→Refresh 每帧都会 `CardbackData.Get(player.cardback)`。</summary>
        public static CardbackData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (cardback_dict_count != cardback_list.Count)
                RebuildDict();
            return cardback_dict.TryGetValue(id, out CardbackData c) ? c : null;
        }

        public static List<CardbackData> GetAll()
        {
            return cardback_list;
        }
    }
}
