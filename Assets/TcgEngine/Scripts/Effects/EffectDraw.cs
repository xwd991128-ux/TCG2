using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect to draw cards
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Draw", order = 10)]
    public class EffectDraw : EffectData
    {
        [Header("抽牌数来源：不填则用能力的基础 value（兼容旧配置）")]
        public ValueSource amount;   // 填了就在任意地方取值，空则回退到 ability.value

        private int GetAmount(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            if (amount != null)
                return amount.GetValue(data, ability, caster, target, target_player);
            return ability.value;
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            logic.DrawCard(target, GetAmount(logic.GameData, ability, caster, null, target));
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Player player = logic.GameData.GetPlayer(target.player_id);
            logic.DrawCard(player, GetAmount(logic.GameData, ability, caster, target, null));
        }

    }
}