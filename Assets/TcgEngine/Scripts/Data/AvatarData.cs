using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Defines all avatar data
    /// </summary>

    [CreateAssetMenu(fileName = "Avatar", menuName = "TcgEngine/Avatar", order = 10)]
    public class AvatarData : ScriptableObject
    {
        public string id;
        public Sprite avatar;
        public int sort_order;

        public static List<AvatarData> avatar_list = new List<AvatarData>();
        private static Dictionary<string, AvatarData> avatar_dict = new Dictionary<string, AvatarData>();  //id → 数据（O(1) 查找）
        private static int avatar_dict_count = -1;                                                          //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (avatar_list.Count == 0)
                avatar_list.AddRange(Resources.LoadAll<AvatarData>(folder));

            avatar_list.Sort((AvatarData a, AvatarData b) => { 
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
            avatar_dict.Clear();
            foreach (AvatarData a in avatar_list)
            {
                if (a != null && !string.IsNullOrEmpty(a.id))
                    avatar_dict[a.id] = a;
            }
            avatar_dict_count = avatar_list.Count;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，而 PlayerUI.Update 每帧都会 `AvatarData.Get(player.avatar)`。</summary>
        public static AvatarData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (avatar_dict_count != avatar_list.Count)
                RebuildDict();
            return avatar_dict.TryGetValue(id, out AvatarData a) ? a : null;
        }

        public static List<AvatarData> GetAll()
        {
            return avatar_list;
        }
    }
}
