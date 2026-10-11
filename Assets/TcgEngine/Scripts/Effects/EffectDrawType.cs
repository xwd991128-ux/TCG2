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
            //★失败必须留痕（与同文件"满手牌"告警同一口径）：旧实现这两种失败都是**静默 return**，
            //  玩家侧表现是"检索抽牌技能没反应"，日志里一句都没有（实测 EffectFailPathProbe ② ③）。
            if (player == null)
            {
                Debug.LogWarning("[抽牌] 抽指定类型失败：目标玩家为空");
                return;
            }
            if (player.cards_deck.Count == 0)
            {
                Debug.LogWarning("[抽牌] 抽指定类型失败：p" + player.player_id + " 牌库为空");
                return;
            }

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

            Debug.LogWarning("[抽牌] 抽指定类型失败：p" + player.player_id + " 牌库里没有 " + type
                + " 类型的卡（牌库共 " + player.cards_deck.Count + " 张）");
        }
    }
}
