using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 【固定常量来源】ValueConstant
    /// 返回一个写死的整数。等价于原来的 ability.value，但能在节点连线里充当"基础色板"。
    /// 大部分情况下效果仍用 ability.value 即可，这里提供是为了连线时有一个明确取值器。
    /// </summary>
    [CreateAssetMenu(fileName = "value_constant", menuName = "TcgEngine/Value/Constant", order = 61)]
    public class ValueConstant : ValueSource
    {
        [Header("固定返回的数值")]
        public int value = 1;

        public override int GetValue(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            return value;
        }
    }
}