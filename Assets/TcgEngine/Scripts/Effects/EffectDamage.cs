using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect that damages a card or a player (lose hp)
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Damage", order = 10)]
    public class EffectDamage : EffectData
    {
        public TraitData bonus_damage;

        [Header("伤害来源：不填则用能力的基础 value（兼容旧配置）")]
        public ValueSource amount;   // 填了就在任意地方取值，空则回退到 ability.value

        private int GetAmount(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            if (amount != null)
                return amount.GetValue(data, ability, caster, target, target_player);
            return ability.value;
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            int damage = GetAmount(logic.GameData, ability, caster, null, target);
            damage += GetBonusDamage(logic.GameData, caster);
            logic.DamagePlayer(caster, target, damage);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            int damage = GetAmount(logic.GameData, ability, caster, target, null);
            damage += GetBonusDamage(logic.GameData, caster);
            logic.DamageCard(caster, target, damage, true);
        }

        private int GetBonusDamage(Game data, Card caster)
        {
            if (bonus_damage == null) return 0;
            Player player = data.GetPlayer(caster.player_id);
            return caster.GetTraitValue(bonus_damage) + player.GetTraitValue(bonus_damage);
        }
    }
}