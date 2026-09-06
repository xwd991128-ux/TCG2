using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine
{
    /// <summary>
    /// 图条件：目标合法性由规则图求值（zmcs「目标1条件」机制）。
    /// 由 CardPoolIO 编译链路在主动效果入口的「目标1条件」输入口有连线时动态创建（无 .asset），
    /// 逐候选目标调用 NodeDocRunner 求值入口连着的条件链（如 卡牌类型判断）。
    /// </summary>
    public class ConditionGraphTarget : ConditionData
    {
        [Header("规则图条件")]
        public GraphData graph;         //规则图
        public string entry_node_id;    //入口节点 id（其「目标1条件」口连着条件链）

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Card target)
        {
            return NodeDocRunner.EvaluateTargetCondition(graph, entry_node_id, caster, target);
        }

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Player target)
        {
            //玩家目标（点英雄走这条分支）：用该玩家的英雄卡代入图条件（如 卡牌类型判断=随从 会排除英雄）
            if (target == null || target.hero == null)
                return false;
            return NodeDocRunner.EvaluateTargetCondition(graph, entry_node_id, caster, target.hero);
        }

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Slot target)
        {
            //格子候选：空格子非法；格内有卡则以该卡代入图条件
            if (target == null)
                return false;
            Card slot_card = data.GetSlotCard(target);
            return slot_card != null && NodeDocRunner.EvaluateTargetCondition(graph, entry_node_id, caster, slot_card);
        }
    }
}
