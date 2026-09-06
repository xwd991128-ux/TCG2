using System.Collections.Generic;
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

        public static void Load(string folder = "")
        {
            if (keyword_list.Count == 0)
                keyword_list.AddRange(Resources.LoadAll<KeywordData>(folder));
        }

        public static KeywordData Get(string id)
        {
            foreach (KeywordData keyword in GetAll())
            {
                if (keyword.id == id)
                    return keyword;
            }
            return null;
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
