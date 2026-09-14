using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    public enum ConditionStatType
    {
        None = 0,
        Attack = 10,
        HP = 20,
        Mana = 30,
        /// <summary>最大灵力值（三套灵力体系之三：灵力上限的增长硬顶）；仅玩家有意义，卡牌判断恒为假</summary>
        ManaMaxTotal = 40,
    }

    /// <summary>
    /// Compares basic card or player stats such as attack/hp/mana
    /// </summary>

    [CreateAssetMenu(fileName = "condition", menuName = "TcgEngine/Condition/Stat", order = 10)]
    public class ConditionStat : ConditionData
    {
        [Header("Card stat is")]
        public ConditionStatType type;
        public ConditionOperatorInt oper;
        public int value;

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Card target)
        {
            if (type == ConditionStatType.Attack)
            {
                return CompareInt(target.GetAttack(), oper, value);
            }

            if (type == ConditionStatType.HP)
            {
                return CompareInt(target.GetHP(), oper, value);
            }

            if (type == ConditionStatType.Mana)
            {
                return CompareInt(target.GetMana(), oper, value);
            }

            return false;
        }

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Player target)
        {
            if (type == ConditionStatType.HP)
            {
                return CompareInt(target.hp, oper, value);
            }

            if (type == ConditionStatType.Mana)
            {
                return CompareInt(target.mana, oper, value);
            }

            //最大灵力值（灵力上限的增长硬顶）
            if (type == ConditionStatType.ManaMaxTotal)
            {
                return CompareInt(target.GetManaMaxTotal(), oper, value);
            }

            return false;
        }
    }
}