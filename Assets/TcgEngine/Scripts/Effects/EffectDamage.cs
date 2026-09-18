using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Effect that damages a card or a player (lose hp)
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Damage", order = 10)]
    public class EffectDamage : EffectData
    {
        //★ 2026-09 迁移：法术伤害加成由**特性(TraitData bonus_damage)**改为**关键词(KeywordData)**承载。
        //  概念=关键词（默认 id=spell_damage），数值=该关键词绑定的状态值（StatusType.SpellDamage）。
        //  留空时**不加成**（与旧字段留空=加 0 的口径一致）；旧资源里的 bonus_damage 悬空引用已在本轮重指到关键词。
        public KeywordData bonus_keyword;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            int damage = GetDamage(logic.GameData, caster, ability.value);
            logic.DamagePlayer(caster, target, damage);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            int damage = GetDamage(logic.GameData, caster, ability.value);
            logic.DamageCard(caster, target, damage, true);
        }

        private int GetDamage(Game data, Card caster, int value)
        {
            Player player = data.GetPlayer(caster.player_id);
            //法术伤害加成 = 「法术伤害」关键词绑定状态值（卡 + 玩家，见 KeywordData.GetSpellDamageValue）
            int bonus = bonus_keyword != null ? KeywordData.GetSpellDamageValue(bonus_keyword.id, caster, player) : 0;
            return value + bonus;
        }

    }
}