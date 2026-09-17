using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines all traits and stats data
    /// </summary>

    [CreateAssetMenu(fileName = "TraitData", menuName = "TcgEngine/TraitData", order = 1)]
    public class TraitData : ScriptableObject
    {
        public string id;
        public string title;
        public Sprite icon;

        public static List<TraitData> trait_list = new List<TraitData>();
        private static Dictionary<string, TraitData> trait_dict = new Dictionary<string, TraitData>();  //id → 数据（O(1) 查找）
        private static int trait_dict_count = -1;                                                        //建索引时的列表条数（用于检测外部增删）

        public string GetTitle()
        {
            return title;
        }

        public static void Load(string folder = "")
        {
            if (trait_list.Count == 0)
                trait_list.AddRange(Resources.LoadAll<TraitData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等）。列表条数变化或首次访问时自动调用，
        /// 兼容运行时增删（TraitAssetIO.Create/Remove、TraitAssetIO.All 的补加载等）。</summary>
        public static void RebuildDict()
        {
            trait_dict.Clear();
            foreach (TraitData t in trait_list)
            {
                if (t != null && !string.IsNullOrEmpty(t.id))
                    trait_dict[t.id] = t;
            }
            trait_dict_count = trait_list.Count;
        }

        /// <summary>按 id 取（O(1)）：原来是对 GetAll() 线性扫描 —— 建卡时每张卡的每个种族都会调，
        /// 卡池导入/战斗里 `CardTrait.TraitData` 首次访问也走这里，属高频热点。</summary>
        public static TraitData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (trait_dict_count != trait_list.Count)   //列表被增删过 → 先重建（新建种族后立刻可用、删除后立刻失效）
                RebuildDict();
            return trait_dict.TryGetValue(id, out TraitData t) ? t : null;
        }

        public static List<TraitData> GetAll()
        {
            return trait_list;
        }
    }

    [System.Serializable]
    public struct TraitStat
    {
        public TraitData trait;
        public int value;
    }
}