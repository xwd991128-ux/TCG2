using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Define reward to upload easily on the api
    /// </summary>

    [CreateAssetMenu(fileName = "RewardData", menuName = "TcgEngine/RewardData", order = 5)]
    public class RewardData : ScriptableObject
    {
        public string id;
        public string group;
        public int coins;
        public int xp;

        public PackData[] packs;
        public CardData[] cards;
        public DeckData[] decks;

        public bool repeat = true;

        public static List<RewardData> reward_list = new List<RewardData>();
        private static Dictionary<string, RewardData> reward_dict = new Dictionary<string, RewardData>();  //id → 数据（O(1) 查找）
        private static int reward_dict_count = -1;                                                          //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (reward_list.Count == 0)
                reward_list.AddRange(Resources.LoadAll<RewardData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等；列表条数变化或首次访问时自动调用）</summary>
        public static void RebuildDict()
        {
            reward_dict.Clear();
            foreach (RewardData r in reward_list)
            {
                if (r != null && !string.IsNullOrEmpty(r.id))
                    reward_dict[r.id] = r;
            }
            reward_dict_count = reward_list.Count;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，结算/领奖流程反复调用。</summary>
        public static RewardData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (reward_dict_count != reward_list.Count)
                RebuildDict();
            return reward_dict.TryGetValue(id, out RewardData r) ? r : null;
        }

        public static List<RewardData> GetAll()
        {
            return reward_list;
        }
    }

}