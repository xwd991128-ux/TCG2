using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/DestroyEquip", order = 10)]
    public class EffectDestroyEquip : EffectData
    {
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            //★失败留痕：旧实现在"没有可摧毁的装备"时把 null 交给 DiscardCard，被它的 null 守卫静默吞掉
            //  —— 玩家看到"摧毁装备"完全没发生、日志一句没有（实测 EffectFailPathProbe ⑦）。
            if (target == null)
            {
                Debug.LogWarning("[效果] 摧毁装备失败：目标卡为空");
                return;
            }
            if (target.CardData != null && target.CardData.IsEquipment())
            {
                logic.DiscardCard(target);
                return;
            }

            //目标不是装备 → 摧毁它身上附着的那件装备；没有附着 = 无可摧毁
            Card etarget = logic.GameData.GetCard(target.equipped_uid);
            if (etarget == null)
            {
                Debug.LogWarning("[效果] 摧毁装备失败：" + target.card_id + " 不是装备、也没有附着装备");
                return;
            }
            logic.DiscardCard(etarget);
        }

    }
}