using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines card variants
    /// </summary>

    [CreateAssetMenu(fileName = "VariantData", menuName = "TcgEngine/VariantData", order = 5)]
    public class VariantData : ScriptableObject
    {
        public string id;
        public string title;
        public Sprite frame;
        public Sprite frame_board;
        public Color color = Color.white;
        public int cost_factor = 1;
        public bool is_default;

        public static List<VariantData> variant_list = new List<VariantData>();
        private static Dictionary<string, VariantData> variant_dict = new Dictionary<string, VariantData>();  //id → 数据（O(1) 查找）
        private static int variant_dict_count = -1;                                                           //建索引时的列表条数（用于检测外部增删）

        public string GetSuffix()
        {
            return "_" + id;
        }

        public static void Load(string folder = "")
        {
            if (variant_list.Count == 0)
                variant_list.AddRange(Resources.LoadAll<VariantData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等；列表条数变化或首次访问时自动调用）</summary>
        public static void RebuildDict()
        {
            variant_dict.Clear();
            foreach (VariantData v in variant_list)
            {
                if (v != null && !string.IsNullOrEmpty(v.id))
                    variant_dict[v.id] = v;
            }
            variant_dict_count = variant_list.Count;
        }

        public static VariantData GetDefault()
        {
            foreach (VariantData variant in GetAll())
            {
                if (variant.is_default)
                    return variant;
            }
            return null;
        }

        public static VariantData GetSpecial()
        {
            foreach (VariantData variant in GetAll())
            {
                if (!variant.is_default)
                    return variant;
            }
            return null;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，而建卡时每张卡都会 `VariantData.Get(card.variant)`
        /// （GameLogic.SetPlayerDeck / CollectionPanel / DeckDisplay 等）→ 整副卡组重复扫描。</summary>
        public static VariantData Get(string id)
        {
            if (variant_dict_count != variant_list.Count)
                RebuildDict();
            if (!string.IsNullOrEmpty(id) && variant_dict.TryGetValue(id, out VariantData v) && v != null)
                return v;
            return GetDefault();   //与原语义一致：查不到 → 回落到默认变体
        }

        public static List<VariantData> GetAll()
        {
            return variant_list;
        }
    }
}