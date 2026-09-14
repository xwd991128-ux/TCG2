using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect that adds or removes basic card/player stats such as hp, attack, mana
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/AddStat", order = 10)]
    public class EffectAddStat : EffectData
    {
        public EffectStatType type;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (type == EffectStatType.HP)
            {
                target.hp += ability.value;
                target.hp_max += ability.value;
            }

            if (type == EffectStatType.Mana)
            {
                target.mana += ability.value;
                target.mana_max += ability.value;
                target.mana = Mathf.Max(target.mana, 0);
                //灵力上限的钳制上界 = 该玩家自己的"最大灵力值"（不再是全局 GameplayData.mana_max）
                target.mana_max = Mathf.Clamp(target.mana_max, 0, target.GetManaClampCap());
            }

            //最大灵力值（灵力上限的增长硬顶）：加减后让上限/当前按新硬顶收敛
            if (type == EffectStatType.ManaMaxTotal)
            {
                target.mana_max_total = Mathf.Max(target.mana_max_total + ability.value, 0);
                target.ClampMana();
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            if (type == EffectStatType.Attack)
                target.attack += ability.value;
            if (type == EffectStatType.HP)
                target.hp += ability.value;
            if (type == EffectStatType.Mana)
                target.mana += ability.value;
        }

        public override void DoOngoingEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            if (type == EffectStatType.Attack)
                target.attack_ongoing += ability.value;
            if (type == EffectStatType.HP)
                target.hp_ongoing += ability.value;
            if (type == EffectStatType.Mana)
                target.mana_ongoing += ability.value;
        }

    }

    public enum EffectStatType
    {
        None = 0,
        Attack = 10,
        HP = 20,
        Mana = 30,
        /// <summary>最大灵力值（三套灵力体系之三：灵力上限 mana_max 的增长硬顶）。
        /// 加在末尾且值=40，不影响既有资产的序列化枚举值（Mana=30 保持不变）。</summary>
        ManaMaxTotal = 40,
    }
}