using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/SetAtkEqualHp", order = 10)]
    public class EffectSetAtkEqualHpReal : EffectData
    {
        public EffectStatType type;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            target.attack = target.GetHP();
        }

        public override void DoOngoingEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            target.attack = target.GetHP();
            target.attack_ongoing = 0;
        }
    }
}
