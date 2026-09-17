using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines all packs data
    /// </summary>

    [CreateAssetMenu(fileName = "PackData", menuName = "TcgEngine/PackData", order = 5)]
    public class PackData : ScriptableObject
    {
        public string id;

        [Header("Content")]
        public PackType type;
        public int cards = 5;   //Cards per pack
        public PackRarity[] rarities_1st;  //Probability of each rarity, for first card
        public PackRarity[] rarities;      //Probability of each rarity, for other cards
        public PackVariant[] variants;      //Probability of each variant, for other cards

        [Header("Display")]
        public string title;
        public Sprite pack_img;
        public Sprite cardback_img;
        [TextArea(5, 10)]
        public string desc;
        public int sort_order;

        [Header("Availability")]
        public bool available = true;
        public int cost = 100;  //Cost to buy

        public static List<PackData> pack_list = new List<PackData>();
        private static Dictionary<string, PackData> pack_dict = new Dictionary<string, PackData>();  //id → 数据（O(1) 查找）
        private static int pack_dict_count = -1;                                                      //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (pack_list.Count == 0)
                pack_list.AddRange(Resources.LoadAll<PackData>(folder));

            pack_list.Sort((PackData a, PackData b) => {
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
            pack_dict.Clear();
            foreach (PackData p in pack_list)
            {
                if (p != null && !string.IsNullOrEmpty(p.id))
                    pack_dict[p.id] = p;
            }
            pack_dict_count = pack_list.Count;
        }

        public string GetTitle()
        {
            return title;
        }

        public string GetDesc()
        {
            return desc;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，开包/商店/奖励结算会反复调用。</summary>
        public static PackData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (pack_dict_count != pack_list.Count)
                RebuildDict();
            return pack_dict.TryGetValue(id, out PackData p) ? p : null;
        }

        public static List<PackData> GetAllAvailable()
        {
            List<PackData> valid_list = new List<PackData>();
            foreach (PackData apack in GetAll())
            {
                if (apack.available)
                    valid_list.Add(apack);
            }
            return valid_list;
        }

        public static List<PackData> GetAll()
        {
            return pack_list;
        }
    }

    public enum PackType
    {
        Random = 0,
        Fixed = 10,
    }

    [System.Serializable]
    public struct PackRarity
    {
        public RarityData rarity;
        public int probability;
    }

    [System.Serializable]
    public struct PackVariant
    {
        public VariantData variant;
        public int probability;
    }
}