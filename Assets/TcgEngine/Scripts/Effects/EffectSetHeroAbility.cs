using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect that replaces hero abilities with a new ability
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/SetHeroAbility", order = 10)]
    public class EffectSetHeroAbility : EffectData
    {
        public AbilityData new_ability;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (target.hero != null && new_ability != null)
            {
                Card hero = target.hero;
                List<string> ability_ids = new List<string>(hero.abilities);
                foreach (string ab_id in ability_ids)
                {
                    AbilityData ab = AbilityData.Get(ab_id);
                    if (ab != null)
                        hero.RemoveAbility(ab);
                }
                hero.AddAbility(new_ability);
            }
        }
    }
}
