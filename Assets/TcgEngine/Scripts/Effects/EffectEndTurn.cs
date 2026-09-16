using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect to end the current turn (player).
    /// 逻辑层唯一实现是 GameLogic.RequestEndTurn()：走 resolve_queue，等当前结算链跑完再切回合，
    /// 不会在效果结算中间重入回合流程。仅主阶段生效（其它阶段会被拒绝并打日志）。
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/EndTurn", order = 20)]
    public class EffectEndTurn : EffectData
    {
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (logic == null)
                return;
            logic.RequestEndTurn();
        }
    }
}
