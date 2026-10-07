using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    //Sends the target card to a pile of your choice (deck/discard/hand)
    //Dont use to send to board since it needs a slot, use EffectPlay instead to send to board
    //Also dont send to discard from the board because it wont trigger OnKill effects, use EffectDestroy instead

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/SendPile", order = 10)]
    public class EffectSendPile : EffectData
    {
        public PileType pile;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Game data = logic.GetGameData();
            Player player = data.GetPlayer(target.player_id);

            if (pile == PileType.Deck)
            {
                player.RemoveCardFromAllGroups(target);
                player.cards_deck.Add(target);
                target.Clear();
            }

            if (pile == PileType.Hand)
            {
                //★手牌上限闸门（统一）：满手牌时不移动该卡（牌留在原处），与 DrawCard / 210003 同口径。
                //  （原先这里**不查上限**，而 210003 移回手牌会查 → 同一张卡"走哪条路"结果不同。）
                if (!logic.TryMoveCardToHand(player, target))
                {
                    Debug.LogWarning("[送牌] 送入手牌失败：p" + player.player_id + " 手牌已满（"
                        + player.cards_hand.Count + "/" + player.GetHandMax() + "），"
                        + (target.CardData != null ? target.CardData.id : "?") + " 留在原处");
                }
                else
                {
                    target.Clear();
                }
            }

            if (pile == PileType.Discard)
            {
                player.RemoveCardFromAllGroups(target);
                player.cards_discard.Add(target);
                target.Clear();
            }

            if (pile == PileType.Temp)
            {
                player.RemoveCardFromAllGroups(target);
                player.cards_temp.Add(target);
                target.Clear();
            }
        }
    }

    public enum PileType
    {
        None = 0,
        Board = 10,
        Hand = 20,
        Deck = 30,
        Discard = 40,
        Secret = 50,
        Equipped = 60,
        Temp = 90,
    }

}
