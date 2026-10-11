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
            //★失败留痕（旧写法一个 if 包住、失败全静默；实测 EffectFailPathProbe ⑥）
            if (target == null)
            {
                Debug.LogWarning("[效果] 替换英雄能力失败：目标玩家为空");
                return;
            }
            if (target.hero == null)
            {
                Debug.LogWarning("[效果] 替换英雄能力失败：p" + target.player_id + " 没有英雄卡");
                return;
            }
            if (new_ability == null)
            {
                Debug.LogWarning("[效果] 替换英雄能力失败：没有配置新能力（new_ability 为空）");
                return;
            }

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
