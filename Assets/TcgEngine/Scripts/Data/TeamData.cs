using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines all factions data
    /// </summary>
    
    [CreateAssetMenu(fileName = "TeamData", menuName = "TcgEngine/TeamData", order = 1)]
    public class TeamData : ScriptableObject
    {
        public string id;
        public string title;
        public Sprite icon;
        public Color color;

        public static List<TeamData> team_list = new List<TeamData>();
        private static Dictionary<string, TeamData> team_dict = new Dictionary<string, TeamData>();  //id → 数据（O(1) 查找）
        private static int team_dict_count = -1;                                                     //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (team_list.Count == 0)
                team_list.AddRange(Resources.LoadAll<TeamData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等；列表条数变化或首次访问时自动调用）</summary>
        public static void RebuildDict()
        {
            team_dict.Clear();
            foreach (TeamData t in team_list)
            {
                if (t != null && !string.IsNullOrEmpty(t.id))
                    team_dict[t.id] = t;
            }
            team_dict_count = team_list.Count;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描 GetAll()，而建卡/卡池导入（CardPoolIO.BuildCardData）
        /// 会按卡逐次调用 → 卡数 × 阵营数 的重复扫描。</summary>
        public static TeamData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (team_dict_count != team_list.Count)
                RebuildDict();
            return team_dict.TryGetValue(id, out TeamData t) ? t : null;
        }

        public static List<TeamData> GetAll()
        {
            return team_list;
        }
    }
}