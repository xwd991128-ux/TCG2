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
                //★手牌上限闸门（统一）：落点是手牌 ⇒ 放不下就停（与 DrawCard(nb) 的逐张判定同口径：填到上限为止）
                if (!logic.HandHasRoom(target))
                    break;
                logic.SummonCardHand(target, summon, caster.VariantData);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            for (int i = 0; i < count; i++)
            {
                if (!logic.HandHasRoom(player))
                    break;
                logic.SummonCardHand(player, summon, caster.VariantData);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Slot target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);

            //指定格最多只放得下 1 张：先落目标格，剩下的依次填该玩家的其它空位。
            //（原来循环里从第 2 张起必被 SummonCard 的「格子已占用」挡掉 → 同格多召实际只出 1 张）
            int summoned = 0;
            if (logic.SummonCard(player, summon, caster.VariantData, target) != null)
                summoned++;

            if (summoned < count)
            {
                List<Slot> empties = player.GetEmptySlots();
                for (int i = 0; i < empties.Count && summoned < count; i++)
                {
                    if (logic.SummonCard(player, summon, caster.VariantData, empties[i]) != null)
                        summoned++;
                }
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, CardData target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            for (int i = 0; i < count; i++)
            {
                //★手牌上限闸门（统一）：落点是手牌 ⇒ 填到上限为止
                if (!logic.HandHasRoom(player))
                    break;
                logic.SummonCardHand(player, target, caster.VariantData);
            }
        }
    }
}
