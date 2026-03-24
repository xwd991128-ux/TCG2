using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect that adds a status to a card or player
    /// 给卡牌或玩家添加状态效果
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/AddStatus", order = 10)]
    public class EffectAddStatus : EffectData
    {
        public StatusData status;
        public int value = 1;
        public int duration = 0;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            if (status != null)
                target.AddStatus(status, value, duration);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (status != null)
                target.AddStatus(status, value, duration);
        }

        public override void DoOngoingEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            if (status != null)
                target.AddOngoingStatus(status, value);
        }

        public override void DoOngoingEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (status != null)
                target.AddOngoingStatus(status, value);
        }
    }
}
