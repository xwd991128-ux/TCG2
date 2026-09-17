using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines all fixed deck data (for user custom decks, check UserData.cs)
    /// </summary>
    
    [CreateAssetMenu(fileName = "DeckData", menuName = "TcgEngine/DeckData", order = 7)]
    public class DeckData : ScriptableObject
    {
        public string id;

        [Header("Display")]
        public string title;

        [Header("Cards")]
        public CardData hero;
        public CardData[] cards;

        public static List<DeckData> deck_list = new List<DeckData>();
        private static Dictionary<string, DeckData> deck_dict = new Dictionary<string, DeckData>();  //id → 数据（O(1) 查找）
        private static int deck_dict_count = -1;                                                      //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if(deck_list.Count == 0)
                deck_list.AddRange(Resources.LoadAll<DeckData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等；列表条数变化或首次访问时自动调用）</summary>
        public static void RebuildDict()
        {
            deck_dict.Clear();
            foreach (DeckData d in deck_list)
            {
                if (d != null && !string.IsNullOrEmpty(d.id))
                    deck_dict[d.id] = d;
            }
            deck_dict_count = deck_list.Count;
        }

        public int GetQuantity()
        {
            return cards.Length;
        }

        public bool IsValid()
        {
            return true;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，开局/关卡配置里反复调用。</summary>
        public static DeckData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (deck_dict_count != deck_list.Count)
                RebuildDict();
            return deck_dict.TryGetValue(id, out DeckData d) ? d : null;
        }

        public static List<DeckData> GetAll()
        {
            return deck_list;
        }
    }
}
