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
            NodeDocRunner.Run(logic, graph, caster, target, null, trigger_action);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            NodeDocRunner.Run(logic, graph, caster, null, target, trigger_action);
        }
    }
}
