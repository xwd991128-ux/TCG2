using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 一套构筑环境（标准 / 乱斗xx）：卡组规模 + 额外区 + 基础约束 + 可选规则 + 整卡组条件。
    ///
    /// 设计目标：**新增一套乱斗 = 在 Resources/DeckFormats 下加一个资产**，
    /// 校验与结算代码都不用改（约束 / 额外区 / 条件全部数据驱动）。
    ///
    /// 与既有 `GameplayData.deck_size` / `deck_duplicate_max` 的关系：
    ///   环境资产存在时**覆盖**那两个全局值；资产缺失时（老流程）仍走 GameplayData 默认 → 标准模式行为不变。
    /// </summary>
    [CreateAssetMenu(fileName = "deck_format", menuName = "TcgEngine/DeckFormatData", order = 10)]
    public class DeckFormatData : ScriptableObject
    {
        /// <summary>标准环境的固定 id（没选任何环境时用它）</summary>
        public const string StandardId = "standard";

        public string id;

        [Header("Display")]
        public string title;
        public string desc;

        [Header("规模")]
        public int deck_size = 30;      //主卡张数
        public int max_copies = 2;      //同名单卡上限
        public int max_legendary = 1;   //传说卡上限

        [Header("额外区")]
        public List<DeckZone> zones = new List<DeckZone>();

        [Header("基础约束（必生效）")]
        public List<DeckConstraint> constraints = new List<DeckConstraint>();

        [Header("可选规则（对局前勾选后才生效）")]
        public List<DeckModifier> optional_modifiers = new List<DeckModifier>();

        [Header("整卡组条件与奖励")]
        public List<DeckPredicate> predicates = new List<DeckPredicate>();

        //------------------ 注册表（写法与 CardData / DeckData 一致）------------------

        public static List<DeckFormatData> format_list = new List<DeckFormatData>();
        private static Dictionary<string, DeckFormatData> format_dict = new Dictionary<string, DeckFormatData>();
        private static int format_dict_count = -1;   //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "DeckFormats")
        {
            if (format_list.Count == 0)
                format_list.AddRange(Resources.LoadAll<DeckFormatData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等）</summary>
        public static void RebuildDict()
        {
            format_dict.Clear();
            foreach (DeckFormatData f in format_list)
            {
                if (f != null && !string.IsNullOrEmpty(f.id))
                    format_dict[f.id] = f;
            }
            format_dict_count = format_list.Count;
        }

        public static DeckFormatData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (format_dict_count != format_list.Count)
                RebuildDict();
            return format_dict.TryGetValue(id, out DeckFormatData f) ? f : null;
        }

        /// <summary>标准环境；资产缺失返回 null（调用方回退到 GameplayData 的全局默认值）</summary>
        public static DeckFormatData GetStandard()
        {
            return Get(StandardId);
        }

        public static List<DeckFormatData> GetAll()
        {
            return format_list;
        }
    }
}
