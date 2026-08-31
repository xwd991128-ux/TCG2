using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effects that heals a card or player (hp)
    /// It cannot restore more than the original hp, use AddStats to go beyond original
    /// 支持 amount 参数来源：不填则用能力的基础 value（兼容旧配置）。
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Heal", order = 10)]
    public class EffectHeal : EffectData
    {
        /// <summary>治疗量来源：填了就在任意地方取值，空则回退到 ability.value</summary>
        public ValueSource amount;

        // 治疗量 = amount 或 ability.value
        private int GetAmount(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            if (amount != null)
                return amount.GetValue(data, ability, caster, target, target_player);
            return ability.value;
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            logic.HealPlayer(target, GetAmount(logic.GameData, ability, caster, null, target));
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            logic.HealCard(target, GetAmount(logic.GameData, ability, caster, target, null));
        }

    }
}