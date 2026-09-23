using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>可被修饰规则覆盖的环境数值（新增一种 = 加一个枚举值 + 结算处一个分支）</summary>
    public enum DeckFormatField
    {
        DeckSize = 0,      //主卡张数
        MaxCopies = 10,    //同名单卡上限
        MaxLegendary = 20, //传说卡上限
    }

    /// <summary>覆盖方式：Set=直接替换（自由构筑把上限设成很大）／Add=在环境值上叠加（"上限+1"）</summary>
    public enum DeckOverrideMode
    {
        Set = 0,
        Add = 10,
    }

    /// <summary>覆盖环境数值：把 DeckFormatData 的同名字段替换或叠加（如"自由构筑"解除同名上限、"上限+1"）</summary>
    [System.Serializable]
    public class DeckOverride
    {
        public DeckFormatField field = DeckFormatField.MaxCopies;
        public DeckOverrideMode mode = DeckOverrideMode.Set;
        public int value;
    }

    /// <summary>
    /// 一套修饰规则：**带上它就生效**。三种来源共用这个形状 ——
    ///   ① 卡牌自带修饰（带这张卡就生效，卡按 id 引用）
    ///   ② 乱斗环境的"可选开关规则"（对局前勾选后才生效）
    ///   ③ （未来）其它来源，只要产出一个 DeckModifier 即可，校验与结算代码不用改。
    /// </summary>
    [System.Serializable]
    public class DeckModifier
    {
        public string id;
        public string title;
        public string desc;

        [Header("覆盖环境数值")]
        public List<DeckOverride> overrides = new List<DeckOverride>();

        [Header("追加约束（叠加在环境约束之上）")]
        public List<DeckConstraint> constraints = new List<DeckConstraint>();

        [Header("追加额外区（如 ETC 卡强制生成必填「乐队」区）")]
        public List<DeckZone> zones = new List<DeckZone>();

        [Header("追加整卡组条件")]
        public List<DeckPredicate> predicates = new List<DeckPredicate>();
    }

    /// <summary>
    /// 让"卡牌自带修饰"能从 Resources 按 id 加载（写法与 CardData / DeckData 一致：list + dict + RebuildDict）。
    /// 卡牌侧只存一个 id 字符串，不做硬引用 —— 与迁移方向（改按 id 引用）保持一致。
    /// </summary>
    [CreateAssetMenu(fileName = "deck_modifier", menuName = "TcgEngine/DeckModifierData", order = 9)]
    public class DeckModifierData : ScriptableObject
    {
        public string id;
        public DeckModifier modifier = new DeckModifier();

        public static List<DeckModifierData> modifier_list = new List<DeckModifierData>();
        private static Dictionary<string, DeckModifierData> modifier_dict = new Dictionary<string, DeckModifierData>();
        private static int modifier_dict_count = -1;   //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "DeckModifiers")
        {
            if (modifier_list.Count == 0)
                modifier_list.AddRange(Resources.LoadAll<DeckModifierData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等）</summary>
        public static void RebuildDict()
        {
            modifier_dict.Clear();
            foreach (DeckModifierData d in modifier_list)
            {
                if (d != null && !string.IsNullOrEmpty(d.id))
                    modifier_dict[d.id] = d;
            }
            modifier_dict_count = modifier_list.Count;
        }

        public static DeckModifierData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (modifier_dict_count != modifier_list.Count)
                RebuildDict();
            return modifier_dict.TryGetValue(id, out DeckModifierData d) ? d : null;
        }

        /// <summary>按 id 取修饰本体（找不到返回 null，调用方自行跳过）</summary>
        public static DeckModifier GetModifier(string id)
        {
            DeckModifierData d = Get(id);
            return d != null ? d.modifier : null;
        }

        public static List<DeckModifierData> GetAll()
        {
            return modifier_list;
        }
    }
}
