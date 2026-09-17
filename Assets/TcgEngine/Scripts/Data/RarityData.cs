using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines all rarities data (common, uncommon, rare, mythic)
    /// </summary>

    [CreateAssetMenu(fileName = "RarityData", menuName = "TcgEngine/RarityData", order = 1)]
    public class RarityData : ScriptableObject
    {
        public string id;
        public string title;
        public Sprite icon;
        public int rank;        //Index of the rarity, should start at 1 (common) and increase sequentially

        public static List<RarityData> rarity_list = new List<RarityData>();
        private static Dictionary<string, RarityData> rarity_dict = new Dictionary<string, RarityData>();  //id → 数据（O(1) 查找）
        private static int rarity_dict_count = -1;                                                          //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (rarity_list.Count == 0)
                rarity_list.AddRange(Resources.LoadAll<RarityData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等；列表条数变化或首次访问时自动调用）</summary>
        public static void RebuildDict()
        {
            rarity_dict.Clear();
            foreach (RarityData r in rarity_list)
            {
                if (r != null && !string.IsNullOrEmpty(r.id))
                    rarity_dict[r.id] = r;
            }
            rarity_dict_count = rarity_list.Count;
        }

        public static RarityData GetFirst()
        {
            int lowest = 99999;
            RarityData first = null;
            foreach (RarityData rarity in GetAll())
            {
                if (rarity.rank < lowest)
                {
                    first = rarity;
                    lowest = rarity.rank;
                }
            }
            return first;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，而卡池导入/合集页筛选/开包展示都会按卡反复调用。</summary>
        public static RarityData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (rarity_dict_count != rarity_list.Count)
                RebuildDict();
            return rarity_dict.TryGetValue(id, out RarityData r) ? r : null;
        }

        public static List<RarityData> GetAll()
        {
            return rarity_list;
        }
    }
}