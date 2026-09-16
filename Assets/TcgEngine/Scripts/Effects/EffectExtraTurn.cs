using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect to grant extra turns to a player.
    /// 语义（与 GameLogic.HeroNewTurn 契约一致）：给目标玩家加 value 层 HeroNewTurn，
    /// 该玩家本回合结束时消耗 1 层并原地再来一个完整回合（可叠加，层数用完自动清除）。
    /// value &lt;= 0 时按 1 层处理；未指定目标时用施法者所属玩家。
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/ExtraTurn", order = 21)]
    public class EffectExtraTurn : EffectData
    {
        [Tooltip("是否给施法者所属玩家（勾选则忽略 ability 的目标）")]
        public bool caster_owner = false;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (logic == null)
                return;
            Player p = caster_owner || target == null
                ? logic.GameData.GetPlayer(caster != null ? caster.player_id : logic.GameData.current_player)
                : target;
            if (p == null)
                return;
            logic.GiveExtraTurns(p, Mathf.Max(1, ability != null ? ability.value : 1));
        }
    }
}
