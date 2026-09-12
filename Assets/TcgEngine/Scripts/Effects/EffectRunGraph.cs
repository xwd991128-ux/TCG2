using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine
{
    /// <summary>
    /// 规则图( NodeDoc )执行入口效果：当能力被触发/目标解析后，把真实对局上下文
    /// （logic + caster + 选中目标）交给 NodeDocRunner 解释执行对应 NodeDoc 动作。
    /// 由 CardPoolIO 编译链路在检测到事件节点下游含 NodeDoc 动作时挂载。
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/RunGraph", order = 10)]
    public class EffectRunGraph : EffectData
    {
        [Header("规则图")]
        public GraphData graph;            //要解释执行的图
        public string trigger_action;      //入口事件 action（OnPlay/StartOfTurn…），空=任意事件

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster)
        {
            //顺序逐槽多目标：选择结果在图槽号 → 卡的映射里（入口「目标卡牌N」输出口按槽取值）；
            //槽1同时作为 target_card 传入，兼容旧的单一「目标/目标卡牌」引用。
            Dictionary<int, Card> slots = logic != null ? logic.GetMultiTargetResults() : null;
            Card slot1 = null;
            if (slots != null)
                slots.TryGetValue(1, out slot1);
            NodeDocRunner.Run(logic, graph, caster, slot1, null, trigger_action, ability: ability, target_slots: slots);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Debug.Log("[RunGraph] 图执行触发 action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null") + " 目标卡=" + (target != null ? target.CardData?.id : "无"));
            NodeDocRunner.Run(logic, graph, caster, target, null, trigger_action, ability: ability);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            Debug.Log("[RunGraph] 图执行触发(玩家目标) action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null")
                + " 目标玩家=p" + (target != null ? target.player_id.ToString() : "null") + " 目标英雄卡=" + (target != null && target.hero != null ? target.hero.CardData?.id : "无"));
            //英雄=卡牌：选中英雄（含「英雄」类型直接指向、逐槽选英雄）时，把英雄卡同时作为 target_card 传入，
            //使「目标卡牌N/目标」引用拿到英雄卡（伤害经 DamageCard/DealDamage 英雄路由落到玩家 hp，
            //并保留 OnBefore/AfterDamage 图事件与吸血）；「玩家」口仍返回玩家本体（抽牌/属性类动作不受影响）。
            //此前这里传 null：单目标选英雄时下游 目标卡牌1 解析为空 → "打随从生效、打英雄无效"。
            Card hero = target != null ? target.hero : null;
            NodeDocRunner.Run(logic, graph, caster, hero, target, trigger_action, ability: ability);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Slot target)
        {
            //选目标时以"格子"结算（SelectSlot/PlayTarget 落点）：格内有卡→卡牌目标；空己方格→玩家目标(打脸)；空敌方格→无目标
            if (target == null)
                return;
            Card slot_card = logic.GameData.GetSlotCard(target);
            if (slot_card != null)
            {
                Debug.Log("[RunGraph] 图执行触发(格子目标) action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null")
                    + " 格内卡=" + (slot_card.CardData != null ? slot_card.CardData.id : "null"));
                NodeDocRunner.Run(logic, graph, caster, slot_card, null, trigger_action, ability: ability);
            }
            else
            {
                Player tplayer = logic.GameData.GetPlayer(target.p);
                Card hero = tplayer != null ? tplayer.hero : null;   //同上：打脸也把英雄卡作为目标卡传入
                Debug.Log("[RunGraph] 图执行触发(空格/打脸) action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null")
                    + " 目标玩家=p" + (tplayer != null ? tplayer.player_id.ToString() : "null"));
                NodeDocRunner.Run(logic, graph, caster, hero, tplayer, trigger_action, ability: ability);
            }
        }
    }
}
