using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 【四则运算来源】ValueMath
    /// 把多个参数来源（ValueSource）按运算合并成一个数值。
    /// 解决"不是简单地取一个属性，而是要多个数算一算"的需求，例如：
    ///   - 抽牌数 = 目标攻击力 ÷ 2 + 1
    ///   - 伤害   = 我方随从数 × 3 + 施法者攻击
    ///
    /// 设计：
    ///   oper     = 运算规则（Add/Sub/Mul/Div）
    ///   operands = 操作数数组。每个元素本身又是一个 ValueSource，
    ///              所以可以继续"套娃"：operands 里既能放 ValueTargetStat(攻击力)，
    ///              也能放另一个 ValueMath（再做一层加减乘除），无限嵌套。
    ///
    /// 结果 = 从左到右按 oper 依次合并：
    ///   Add -> 起点 0，累加所有操作数
    ///   Sub -> 起点 operands[0]，依次减后面
    ///   Mul -> 起点 1，连乘所有操作数
    ///   Div -> 起点 operands[0]，依次除后面（除数为 0 时跳过硬保护）
    /// </summary>
    [CreateAssetMenu(fileName = "value_math", menuName = "TcgEngine/Value/Math", order = 65)]
    public class ValueMath : ValueSource
    {
        public enum ValueMathOper
        {
            Add,   // 求和：0 + o1 + o2 + ...
            Sub,   // 求差：o1 - o2 - o3 - ...
            Mul,   // 求积：1 * o1 * o2 * ...
            Div,   // 求商：o1 / o2 / o3 / ...
            Mod,   // 取余：o1 % o2 % o3 ...（依次取余）
        }

        [Header("运算规则")]
        public ValueMathOper oper = ValueMathOper.Add;

        [Header("操作数（每个可单独接一个取值源，可套娃）")]
        public ValueSource[] operands = new ValueSource[0];

        public override int GetValue(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            if (operands == null || operands.Length == 0)
                return 0;

            if (oper == ValueMathOper.Add)
            {
                int sum = 0;
                foreach (var op in operands)
                    sum += Eval(op, data, ability, caster, target, target_player);
                return sum;
            }
            else if (oper == ValueMathOper.Sub)
            {
                int r = Eval(operands[0], data, ability, caster, target, target_player);
                for (int i = 1; i < operands.Length; i++)
                    r -= Eval(operands[i], data, ability, caster, target, target_player);
                return r;
            }
            else if (oper == ValueMathOper.Mul)
            {
                int r = 1;
                foreach (var op in operands)
                    r *= Eval(op, data, ability, caster, target, target_player);
                return r;
            }
            else // Div
            {
                int r = Eval(operands[0], data, ability, caster, target, target_player);
                for (int i = 0; i < operands.Length; i++)
                {
                    if (oper == ValueMathOper.Div && i == 0) continue;   // 起点跳过除法
                    int v = Eval(operands[i], data, ability, caster, target, target_player);
                    if (oper == ValueMathOper.Div)
                    {
                        if (v != 0) r /= v;   // 除 0 保护
                    }
                    else // Mod
                    {
                        if (v != 0) r %= v;   // 取余、除 0 保护
                    }
                }
                return r;
            }
        }

        // 取值：操作数是 ValueSource 就调它，否则当作常量 int 处理
        private int Eval(ValueSource src, Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            if (src != null) return src.GetValue(data, ability, caster, target, target_player);
            return 0;
        }
    }
}