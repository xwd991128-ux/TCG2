using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 【全局临时值来源】ValueSelectedValue
    /// 读取对局级临时变量 data.selected_value（就是"套娃"的中转槽）。
    /// 
    /// 配合现有机制：
    ///   - EffectSelectedValue / EffectCaptureStat / EffectRoll 会往 selected_value 写入值
    ///   - EffectRepeat(type=SelectedValue) 会用 selected_value 作为循环次数
    ///
    /// 有了本节点，任意数值效果都能"读那个临时变量"作为自己的数值，
    /// 与 EffectCaptureStat 形成完美的 先采集 → 后使用 闭环：
    ///   Ability:
    ///     effects[0] = EffectCaptureStat { type=Attack }   // 目标攻击力 -> selected_value
    ///     effects[1] = EffectDraw      { amount = ValueSelectedValue }  // 抽 selected_value 张
    /// </summary>
    [CreateAssetMenu(fileName = "value_selected", menuName = "TcgEngine/Value/SelectedValue", order = 64)]
    public class ValueSelectedValue : ValueSource
    {
        [Header("是否先从 ability.value 出发（否则读 data.selected_value）")]
        public bool use_ability_value = false;

        [Header("系数：返回值 = 基础值 × multiplier")]
        public int multiplier = 1;

        public override int GetValue(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            int base_val = use_ability_value ? ability.value : data.selected_value;
            return base_val * multiplier;
        }
    }
}