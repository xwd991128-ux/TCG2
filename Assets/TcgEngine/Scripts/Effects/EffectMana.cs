using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect to gain/lose mana (player)
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Mana", order = 10)]
    public class EffectMana : EffectData
    {
        public bool increase_value;
        public bool increase_max;
        /// <summary>增加"最大灵力值"（三套灵力体系之三：灵力上限 mana_max 的增长硬顶）。
        /// 新增字段=加法，旧资产读默认 false，行为与改动前完全一致。</summary>
        public bool increase_max_total;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            //先改硬顶（最大灵力值），后面上限/当前的钳制口径随之变化
            if (increase_max_total)
            {
                target.mana_max_total = Mathf.Max(target.mana_max_total + ability.value, 0);
                target.ClampMana();
            }

            if (increase_max)
            {
                target.mana_max += ability.value;
                //灵力上限的钳制上界 = 该玩家自己的"最大灵力值"（不再是全局 GameplayData.mana_max）
                target.mana_max = Mathf.Clamp(target.mana_max, 0, target.GetManaClampCap());
            }
            
            if(increase_value)
            {
                target.mana += ability.value;
                target.mana = Mathf.Max(target.mana, 0);
            }
        }

    }
}