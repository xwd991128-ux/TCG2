using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{

    [CreateAssetMenu(fileName = "LevelData", menuName = "TcgEngine/LevelData", order = 7)]
    public class LevelData : ScriptableObject
    {
        public string id;
        public int level;

        [Header("Display")]
        public string title;

        [Header("Gameplay")]
        public string scene;
        public DeckData player_deck;
        public DeckData ai_deck;
        public int ai_level = 10; //From 1 to 10
        public LevelFirst first_player;
        public GameObject tuto_prefab;
        public bool mulligan = true;

        [Header("Rewards")]
        public int reward_xp = 100;
        public int reward_coins = 100;
        public PackData[] reward_packs;
        public CardData[] reward_cards;
        public DeckData[] reward_decks;

        public static List<LevelData> level_list = new List<LevelData>();
        private static Dictionary<string, LevelData> level_dict = new Dictionary<string, LevelData>();  //id → 数据（O(1) 查找）
        private static int level_dict_count = -1;                                                        //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (level_list.Count == 0)
            {
                level_list.AddRange(Resources.LoadAll<LevelData>(folder));
                level_list.Sort((LevelData a, LevelData b) => { return a.level.CompareTo(b.level); });
            }
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等；列表条数变化或首次访问时自动调用）</summary>
        public static void RebuildDict()
        {
            level_dict.Clear();
            foreach (LevelData l in level_list)
            {
                if (l != null && !string.IsNullOrEmpty(l.id))
                    level_dict[l.id] = l;
            }
            level_dict_count = level_list.Count;
        }

        public string GetTitle()
        {
            return title;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，冒险模式/关卡选择反复调用。</summary>
        public static LevelData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (level_dict_count != level_list.Count)
                RebuildDict();
            return level_dict.TryGetValue(id, out LevelData l) ? l : null;
        }

        public static List<LevelData> GetAll()
        {
            return level_list;
        }
    }

    public enum LevelFirst
    {
        Random = 0,
        Player = 10,
        AI = 20,
    }
}