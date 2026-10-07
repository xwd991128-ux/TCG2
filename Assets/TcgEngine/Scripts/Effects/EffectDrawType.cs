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
                    //★手牌上限闸门（统一）：以前这里**不查上限**，而 EffectDraw 走 DrawCard 会查
                    //  → 同一个上限"抽指定类型能超、普通抽牌不能超"。满手牌时与 DrawCard 同口径：**牌留在牌库**。
                    if (!logic.HandHasRoom(player))
                    {
                        Debug.LogWarning("[抽牌] 抽指定类型失败：p" + player.player_id + " 手牌已满（"
                            + player.cards_hand.Count + "/" + player.GetHandMax() + "），"
                            + card.CardData.id + " 留在牌库");
                        return;
                    }
                    player.cards_deck.RemoveAt(i);
                    player.cards_hand.Add(card);
                    logic.TriggerPlayerCardsAbilityType(player, AbilityTrigger.OnDraw);
                    return;
                }
            }
        }
    }
}
