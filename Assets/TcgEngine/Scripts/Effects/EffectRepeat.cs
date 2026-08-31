using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// Repeat an ability X times
    /// 支持 amount 参数来源：
    ///   - 填了 amount 时，重复次数 = amount 动态取值（最高优先级）
    ///   - 没填时，沿用原逻辑：FixedValue 用能力基础 value，SelectedValue 用对局临时变量
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/Repeat", order = 10)]
    public class EffectRepeat : EffectData
    {
        public AbilityData ability;
        public EffectRepeatType type;
        public int bonus = 0;

        /// <summary>重复次数来源：填了就用它，空则回退到 type 决定的方式</summary>
        public ValueSource amount;

        public override void DoEffect(GameLogic logic, AbilityData iability, Card caster)
        {
            int count = GetRepeatCount(logic.GameData, iability, caster, null, null);
            for (int i = 0; i < count; i++)
            {
                Card triggerer = logic.GameData.GetCard(logic.GameData.ability_triggerer);
                logic.TriggerAbilityDelayed(this.ability, caster, triggerer);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData iability, Card caster, Player target)
        {
            int count = GetRepeatCount(logic.GameData, iability, caster, null, target);
            for (int i = 0; i < count; i++)
            {
                Card triggerer = logic.GameData.GetCard(logic.GameData.ability_triggerer);
                logic.TriggerAbilityDelayed(this.ability, caster, triggerer);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData iability, Card caster, Card target)
        {
            int count = GetRepeatCount(logic.GameData, iability, caster, target, null);
            for (int i = 0; i < count; i++)
            {
                Card triggerer = logic.GameData.GetCard(logic.GameData.ability_triggerer);
                logic.TriggerAbilityDelayed(this.ability, caster, triggerer);
            }
        }

        // 重复次数：amount > 动态值 > 固定/临时变量
        public int GetRepeatCount(Game game, AbilityData iability, Card caster, Card target, Player target_player)
        {
            if (amount != null)
                return amount.GetValue(game, iability, caster, target, target_player);
            if (type == EffectRepeatType.SelectedValue)
                return game.selected_value;
            if (type == EffectRepeatType.FixedValue)
                return iability.value;
            return 0;
        }
    }


    public enum EffectRepeatType
    {
        FixedValue,
        SelectedValue
    }
}