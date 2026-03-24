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
                return;

            Game data = logic.GetGameData();
            Player player = data.GetPlayer(target.player_id);

            player.RemoveCardFromAllGroups(target);
            player.cards_all.Remove(target.uid);
            target.Clear();
        }
    }
}
