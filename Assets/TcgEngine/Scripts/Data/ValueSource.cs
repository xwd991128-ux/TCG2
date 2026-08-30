using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 【参数来源基类】ValueSource
    /// =====================================================================
    /// 解决什么问题？
    ///   原来效果的数值是写死的 ability.value（如抽 2 张、打 3 伤）。
    ///   想"抽的牌数 = 目标攻击力"、"伤害 = 自己血量"这类动态效果时，没有插座可插。
    ///
    ///   本类就是给每个数值效果开一个"数值输入口"（amount 字段），
    ///   里面可以接任意一个 ValueSource 子类（相当于一个"取值器"/函数节点），
    ///   从而做到"参数在任意地方都能调用" —— 想从哪取、怎么算，随你接。
    ///
    /// 用法（Unity 里拖拽，免代码）：
    ///   1. 新建一个 ValueSource 子类资源（如 ValueTargetStat）
    ///   2. 把效果 EffectDraw.amount 字段拖上这个资源
    ///   3. 运行时效果会调用 amount.GetValue(...) 取回数值再执行
    ///
    /// 为什么单独成类而不是直接存 int？
    ///   因为要给"套娃"留空间：ValueSource 内部还能再引用别的 ValueSource，
    ///   一层套一层，实现任意复杂组合（对齐醉梦传说的函数节点）。
    /// </summary>
    [CreateAssetMenu(fileName = "valuesource", menuName = "TcgEngine/Value/Source", order = 60)]
    public abstract class ValueSource : ScriptableObject
    {
        /// <summary>
        /// 取这个"参数来源"当前计算出的数值。
        /// 子类各自实现不同的取值逻辑（目标属性 / 施法者属性 / 固定常量 / 读取临时变量…）。
        /// 返回值即最终用于效果的数值。
        /// </summary>
        /// <param name="data">对局数据，可访问 selected_value、GetPlayer 等</param>
        /// <param name="ability">所属能力（含基础 value 字段）</param>
        /// <param name="caster">施法者卡</param>
        /// <param name="target">目标卡（可能为 null，取决于目标类型）</param>
        /// <param name="target_player">目标玩家（可能为 null）</param>
        public abstract int GetValue(Game data, AbilityData ability, Card caster, Card target, Player target_player);
    }
}