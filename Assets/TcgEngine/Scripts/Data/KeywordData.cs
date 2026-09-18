using System.Collections.Generic;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 关键词（机制词条）：风怒/冲锋/圣盾这类机制型关键词，与种族/特性（TraitData）区分。
    /// 两种形态（可同时具备）：
    /// ①原生机制关键词：status_type 引用 StatusType（Fury=风怒、Haste=冲锋…），
    ///   卡牌拥有该关键词时运行时自动获得对应状态，机制由 GameLogic 原有逻辑驱动；
    /// ②自定义机制关键词：rules 配置规则图（NodeDoc），在 trigger_action 时机由 NodeDocRunner 执行。
    /// rules 为空且 status_type=None 时是纯展示词条（只有说明文本）。
    /// </summary>

    [CreateAssetMenu(fileName = "KeywordData", menuName = "TcgEngine/KeywordData", order = 2)]
    public class KeywordData : ScriptableObject
    {
        public string id;
        public string title;
        public Sprite icon;

        [TextArea(2, 6)]
        public string desc;             //关键词说明（悬浮提示/合集页用）

        [Header("原生机制")]
        public StatusType status_type = StatusType.None;    //None=不挂原生状态

        [Header("自定义机制（规则图）")]
        public List<KeywordRule> rules = new List<KeywordRule>();

        public bool HasRules => rules != null && rules.Count > 0;
        public bool HasMechanic => status_type != StatusType.None || HasRules;

        // ==================== 法术伤害（2026-09：由 TraitData 特性迁移到关键词） ====================
        // 迁移口径：**概念**是关键词（本类，id=spell_damage），**数值**由该关键词绑定的状态值承载
        //（StatusType.SpellDamage）—— 与工程既有约定一致（armor→Armor、taunt→Protection 同理）。
        // 旧承载 TraitData("spell_damage") 已废弃删除；旧规则图里的 trait_id 字段由节点读取时兼容回退。

        /// <summary>「法术伤害」关键词的默认 id（与 Resources/Keywords/spell_damage.asset 一致）</summary>
        public const string SPELL_DAMAGE_ID = "spell_damage";

        /// <summary>取法术伤害加成的**统一入口**（EffectDamage 与 NodeDoc 节点共用）：
        /// 关键词绑定的状态值之和（卡 + 玩家的普通/持续状态都算，与迁移前 GetTraitValue 的口径一致）。
        /// keyword_id 为空/未配置时按默认 spell_damage；关键词不存在或未绑状态时回退 StatusType.SpellDamage。</summary>
        public static int GetSpellDamageValue(string keyword_id, Card c, Player p)
        {
            KeywordData kw = Get(string.IsNullOrEmpty(keyword_id) ? SPELL_DAMAGE_ID : keyword_id);
            StatusType st = (kw != null && kw.status_type != StatusType.None) ? kw.status_type : StatusType.SpellDamage;
            int v = 0;
            if (c != null)
            {
                CardStatus cs = c.GetStatus(st);
                if (cs != null) v += cs.value;
                CardStatus co = c.GetOngoingStatus(st);
                if (co != null) v += co.value;
            }
            if (p != null)
            {
                CardStatus ps = p.GetStatus(st);
                if (ps != null) v += ps.value;
                CardStatus po = p.GetOngoingStatus(st);
                if (po != null) v += po.value;
            }
            return v;
        }

        /// <summary>卡牌**定义**是否声明了某关键词：定义层没有状态值，只判"有/无"
        /// （供「获取卡牌定义法术伤害」节点用：声明了该关键词返回 1，否则 0）。</summary>
        public static bool DefineHasKeyword(CardData d, string keyword_id)
        {
            if (d == null || d.keywords == null)
                return false;
            string id = string.IsNullOrEmpty(keyword_id) ? SPELL_DAMAGE_ID : keyword_id;
            foreach (KeywordData k in d.keywords)
            {
                if (k != null && k.id == id)
                    return true;
            }
            return false;
        }

        /// <summary>按触发时机取规则（trigger_action 为空 = 匹配任意时机）</summary>
        public KeywordRule GetRule(string trigger_action)
        {
            if (rules == null)
                return null;
            foreach (KeywordRule rule in rules)
            {
                if (rule == null || rule.graph == null)
                    continue;
                if (string.IsNullOrEmpty(rule.trigger_action) || rule.trigger_action == trigger_action)
                    return rule;
            }
            return null;
        }

        public static List<KeywordData> keyword_list = new List<KeywordData>();
        private static Dictionary<string, KeywordData> keyword_dict = new Dictionary<string, KeywordData>();  //id → 数据（O(1) 查找）
        private static int keyword_dict_count = -1;                                                           //建索引时的列表条数（用于检测外部增删）

        public static void Load(string folder = "")
        {
            if (keyword_list.Count == 0)
                keyword_list.AddRange(Resources.LoadAll<KeywordData>(folder));
            RebuildDict();
        }

        /// <summary>重建 id 索引（幂等）。列表条数变化或首次访问时自动调用，
        /// 兼容运行时增删（KeywordPanel/VariableSelectPopup 的新建与删除）。</summary>
        public static void RebuildDict()
        {
            keyword_dict.Clear();
            foreach (KeywordData k in keyword_list)
            {
                if (k != null && !string.IsNullOrEmpty(k.id))
                    keyword_dict[k.id] = k;
            }
            keyword_dict_count = keyword_list.Count;
        }

        /// <summary>按 id 取（O(1)）：原来线性扫描，而卡池导入（每张卡每个关键词）、
        /// 战斗里 BuffRuntime / GameLogic.TriggerKeywords 会按关键词 id 反复调用 → O(N×M)。</summary>
        public static KeywordData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (keyword_dict_count != keyword_list.Count)   //列表被增删过 → 先重建（新建后立刻可用、删除后立刻失效）
                RebuildDict();
            return keyword_dict.TryGetValue(id, out KeywordData k) ? k : null;
        }

        public static List<KeywordData> GetAll()
        {
            return keyword_list;
        }
    }

    /// <summary>
    /// 自定义关键词的一条规则：在 trigger_action 时机执行 graph 规则图。
    /// trigger_action 与 AbilityTrigger 枚举名一致（OnPlay/StartOfTurn/OnDeath…），空=任意时机。
    /// </summary>
    [System.Serializable]
    public class KeywordRule
    {
        public string trigger_action;
        public GraphData graph;
    }
}
