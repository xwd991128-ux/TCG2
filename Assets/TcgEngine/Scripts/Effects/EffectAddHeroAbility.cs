using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/AddHeroAbility", order = 10)]
    public class EffectAddHeroAbility : EffectData
    {
        public AbilityData gain_ability;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (target.hero != null && gain_ability != null)
            {
                target.hero.AddAbility(gain_ability);
            }
        }
    }
}
