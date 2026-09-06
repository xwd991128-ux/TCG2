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
            NodeDocRunner.Run(logic, graph, caster, null, null, trigger_action);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Debug.Log("[RunGraph] 图执行触发 action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null") + " 目标卡=" + (target != null ? target.CardData?.id : "无"));
            NodeDocRunner.Run(logic, graph, caster, target, null, trigger_action);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            NodeDocRunner.Run(logic, graph, caster, null, target, trigger_action);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Slot target)
        {
            //选目标时以"格子"结算（SelectSlot/PlayTarget 落点）：格内有卡→卡牌目标；空己方格→玩家目标；空敌方格→无目标
            if (target == null)
                return;
            Card slot_card = logic.GameData.GetSlotCard(target);
            if (slot_card != null)
                NodeDocRunner.Run(logic, graph, caster, slot_card, null, trigger_action);
            else
                NodeDocRunner.Run(logic, graph, caster, null, logic.GameData.GetPlayer(target.p), trigger_action);
        }
    }
}
