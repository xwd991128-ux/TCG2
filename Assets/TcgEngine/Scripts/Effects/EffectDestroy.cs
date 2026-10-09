using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Destroy", order = 10)]
    public class EffectDestroy : EffectData
    {
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            //★统一入口：装备区的卡也算"在场"（KillCard 的守卫就是 IsOnBoard||IsEquipped）。
            //  旧写法只判 IsOnBoard ⇒ 消灭装备卡时死亡图事件/击杀数全丢（见 EquipDestroyProbe）。
            logic.DestroyCard(caster, target);
        }

    }
}