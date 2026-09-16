using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect to make a player lose their next turn(s).
    /// 语义：给目标玩家加 skip_turns 层标记，GameLogic.StartNextTurn 在选下家时跳过该玩家并消耗 1 层。
    /// value &lt;= 0 时按 1 层处理；未指定目标时跳过该能力施法者的对手。
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/SkipTurn", order = 22)]
    public class EffectSkipTurn : EffectData
    {
        [Tooltip("是否跳过施法者自己（默认跳过对手）")]
        public bool skip_caster = false;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (logic == null)
                return;
            Player p = target;
            if (p == null && caster != null)
            {
                Player owner = logic.GameData.GetPlayer(caster.player_id);
                p = skip_caster ? owner : logic.GameData.GetOpponentPlayer(caster.player_id);
                if (p == null)
                    p = owner;
            }
            if (p == null)
                return;
            logic.SkipNextTurns(p, Mathf.Max(1, ability != null ? ability.value : 1));
        }
    }
}
