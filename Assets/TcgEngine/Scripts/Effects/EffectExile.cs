using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Exile", order = 10)]
    public class EffectExile : EffectData
    {
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            if (target == null)
            {
                //★失败必须留痕（与 NodeDoc 的"[NodeDoc] …失败：…"同一约定）：旧写法静默 return，
                //  玩家侧就是"Exile 效果没反应"且无迹可寻（实测 EffectFailPathProbe ④）。
                Debug.LogWarning("[效果] Exile 失败：目标卡为空");
                return;
            }

            Game data = logic.GetGameData();
            Player player = data.GetPlayer(target.player_id);

            player.RemoveCardFromAllGroups(target);
            player.cards_all.Remove(target.uid);
            target.Clear();
        }
    }
}
