using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    //Effect to Summon an entirely new card (not in anyones deck)
    //And places it on the board (if target slot) or hand (if target player)
    //Unlike EffectCreate, this effect targets where the card goes, and the carddata is selected on the effect

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Summon", order = 10)]
    public class EffectAddToDeck : EffectData
    {
        public CardData summon;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            //★手牌上限闸门（统一）：目标是玩家 ⇒ 落点是手牌，属"真入手"；满手牌则不召唤（与 DrawCard/202004 同口径）
            if (!logic.HandHasRoom(target))
            {
                Debug.LogWarning("[召唤] 召唤入手牌失败：p" + (target != null ? target.player_id.ToString() : "?")
                    + " 手牌已满（" + (target != null ? target.cards_hand.Count + "/" + target.GetHandMax() : "?") + "）");
                return;
            }
            logic.SummonCardHand(target, summon, caster.VariantData); //Summon in hand instead of board when target a player
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            logic.SummonCard(player, summon, caster.VariantData, target.slot); //Assumes the target has just been killed, so the slot is empty
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Slot target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            logic.SummonCard(player, summon, caster.VariantData, target);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, CardData target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            //★手牌上限闸门（统一）：目标是卡牌定义 ⇒ 落点是手牌，属"真入手"
            if (!logic.HandHasRoom(player))
            {
                Debug.LogWarning("[召唤] 召唤入手牌失败：p" + (player != null ? player.player_id.ToString() : "?")
                    + " 手牌已满（" + (player != null ? player.cards_hand.Count + "/" + player.GetHandMax() : "?") + "）");
                return;
            }
            logic.SummonCardHand(player, target, caster.VariantData);   //Summon in hand instead of board when target a carddata
        }
    }
}