using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// Condition that checks if there are other cards with a specific status on the same player's board
    /// 检查己方战场上是否有其他具有指定状态的生物
    /// </summary>
    
    [CreateAssetMenu(fileName = "condition", menuName = "TcgEngine/Condition/OtherCardWithStatus", order = 10)]
    public class ConditionOtherCardWithStatus : ConditionData
    {
        [Header("Check for other card with status")]
        public StatusType has_status;
        public ConditionOperatorInt oper;
        public int value = 1;

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Card target)
        {
            int count = 0;
            Player owner = data.GetPlayer(target.player_id);
            
            foreach (Card card in owner.cards_board)
            {
                if (card.uid != target.uid && card.HasStatus(has_status))
                {
                    count++;
                }
            }
            
            return CompareInt(count, oper, value);
        }
    }
}
