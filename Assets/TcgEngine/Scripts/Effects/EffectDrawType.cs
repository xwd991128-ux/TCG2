using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect to draw a card of specific type from deck
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/DrawType", order = 10)]
    public class EffectDrawType : EffectData
    {
        public CardType card_type;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            DrawCardOfType(logic, target, card_type);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Player player = logic.GameData.GetPlayer(target.player_id);
            DrawCardOfType(logic, player, card_type);
        }

        private void DrawCardOfType(GameLogic logic, Player player, CardType type)
        {
            if (player.cards_deck.Count == 0)
                return;

            for (int i = 0; i < player.cards_deck.Count; i++)
            {
                Card card = player.cards_deck[i];
                if (card.CardData.type == type)
                {
                    player.cards_deck.RemoveAt(i);
                    player.cards_hand.Add(card);
                    logic.TriggerPlayerCardsAbilityType(player, AbilityTrigger.OnDraw);
                    return;
                }
            }
        }
    }
}
