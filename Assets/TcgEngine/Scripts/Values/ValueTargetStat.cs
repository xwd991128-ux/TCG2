using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 【目标属性来源】ValueTargetStat
    /// 读取"目标卡/目标玩家"的某项属性，乘以系数后返回。
    /// 这是实现"每有 1 点攻击力抽 1 张"、"造成目标血量等值伤害"等
    /// 属性联动类效果的核心积木。
    ///
    /// 例：一张法术"战术洞察"
    ///   EffectDraw.amount = ValueTargetStat { type=Attack, multiplier=1 }
    ///   => 施放时，抽的牌数 = 目标攻击力（5攻抽5，3攻抽3）
    /// </summary>
    [CreateAssetMenu(fileName = "value_target_stat", menuName = "TcgEngine/Value/TargetStat", order = 62)]
    public class ValueTargetStat : ValueSource
    {
        [Header("读目标哪项属性")]
        public EffectStatType type = EffectStatType.Attack;

        [Header("系数：返回值 = 属性值 × multiplier；倍率太高需要小数时请把 multiplier 改成 float")]
        public int multiplier = 1;

        public override int GetValue(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            int stat = 0;

            // 目标是"卡"：读卡的属性
            if (target != null)
            {
                if (type == EffectStatType.Attack) stat = target.GetAttack();
                else if (type == EffectStatType.HP) stat = target.GetHP();
                else if (type == EffectStatType.Mana) stat = target.GetMana();
            }
            // 目标是"玩家"：读玩家自身属性（攻击用英雄代替）
            else if (target_player != null)
            {
                if (type == EffectStatType.HP) stat = target_player.hp;
                else if (type == EffectStatType.Mana) stat = target_player.mana;
                else if (type == EffectStatType.Attack)
                    stat = target_player.hero != null ? target_player.hero.GetAttack() : 0;
            }

            return stat * multiplier;
        }
    }
}