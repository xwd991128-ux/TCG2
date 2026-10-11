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
            //★三种失败各自留痕（旧写法是一个 `if (…)` 包住、失败全静默；实测 EffectFailPathProbe ⑤）
            if (target == null)
            {
                Debug.LogWarning("[效果] 授予英雄能力失败：目标玩家为空");
                return;
            }
            if (target.hero == null)
            {
                Debug.LogWarning("[效果] 授予英雄能力失败：p" + target.player_id + " 没有英雄卡");
                return;
            }
            if (gain_ability == null)
            {
                Debug.LogWarning("[效果] 授予英雄能力失败：没有配置要授予的能力（gain_ability 为空）");
                return;
            }
            target.hero.AddAbility(gain_ability);
        }
    }
}
