using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Workshop;   //GraphData（与 EffectRunGraph 同一套：规则用图承载）

namespace TcgEngine
{
    /// <summary>
    /// 整卡组条件 + 奖励：conditions **全部**满足时，开局结算 reward_graph。
    /// 典型用法："费用全偶数"达标 → 触发一份奖励效果。
    /// </summary>
    [System.Serializable]
    public class DeckPredicate
    {
        public string id;
        public string title;
        public string desc;

        [Header("条件（全部满足才算满足）")]
        public List<DeckConstraint> conditions = new List<DeckConstraint>();

        [Header("奖励效果（开局结算，规则图）")]
        public GraphData reward_graph;
    }
}
