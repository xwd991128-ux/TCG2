using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/SummonMultiple", order = 10)]
    public class EffectSummonMultiple : EffectData
    {
        public CardData summon;
        public int count;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            for (int i = 0; i < count; i++)
            {
                logic.SummonCardHand(target, summon, caster.VariantData);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            for (int i = 0; i < count; i++)
            {
                logic.SummonCard(player, summon, caster.VariantData);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Slot target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            for (int i = 0; i < count; i++)
            {
                logic.SummonCard(player, summon, caster.VariantData);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, CardData target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            for (int i = 0; i < count; i++)
            {
                logic.SummonCardHand(player, target, caster.VariantData);
            }
        }
    }
}
