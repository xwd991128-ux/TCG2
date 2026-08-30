using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 【施法者属性来源】ValueCasterStat
    /// 读取"施放此能力的卡"自身的属性，乘以系数后返回。
    /// 用于"造成的伤害等于我自己的攻击力"、'"抽的牌数 = 我方的法力"'这类效果。
    ///
    /// 例：一张法术"血祭"
    ///   EffectDamage.amount = ValueCasterStat { type=HP, multiplier=1 }
    ///   => 对目标造成等于施法者剩余血量数值的伤害。
    /// </summary>
    [CreateAssetMenu(fileName = "value_caster_stat", menuName = "TcgEngine/Value/CasterStat", order = 63)]
    public class ValueCasterStat : ValueSource
    {
        [Header("读施法者哪项属性")]
        public EffectStatType type = EffectStatType.Attack;

        [Header("系数：返回值 = 属性值 × multiplier")]
        public int multiplier = 1;

        public override int GetValue(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            if (caster == null) return 0;

            int stat = 0;
            if (type == EffectStatType.Attack) stat = caster.GetAttack();
            else if (type == EffectStatType.HP) stat = caster.GetHP();
            else if (type == EffectStatType.Mana) stat = caster.GetMana();

            return stat * multiplier;
        }
    }
}