using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect to play a card from your hand for free
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Play", order = 10)]
    public class EffectPlay : EffectData
    {
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Game game = logic.GetGameData();
            Player player = game.GetPlayer(caster.player_id);
            Slot slot = player.GetRandomEmptySlot(logic.GetRandom());

            //★这里**故意不查手牌上限**（统一的闸门见 GameLogic.TryMoveCardToHand 注释）：
            //  本效果是"把目标卡移入手牌 → 立刻免费打出"，进手牌只是**中转**，不是入手。
            //  若在此处查上限，会出现"手牌满时这个效果直接什么都不做"——那是行为回归，不是统一。
            player.RemoveCardFromAllGroups(target);
            player.cards_hand.Add(target);

            if (slot != Slot.None)
            {
                logic.PlayCard(target, slot, true);
            }
        }
    }
}