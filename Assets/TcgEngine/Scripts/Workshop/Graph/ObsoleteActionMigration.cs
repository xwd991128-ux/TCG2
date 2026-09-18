using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>旧图自愈：把**已从 NodeDoc.xml 删除**的"过时/占位"节点 action 改写成新版节点。
    ///
    /// 背景：2026-09 清理了 NodeDoc.xml 共 22 条无实现条目（12 条负 defineId 占位 + 10 条过时），
    /// 之后又按用户确认删除 201012 信仰对决 与 5 个蓄力族（无对应机制）。
    /// 旧图若仍引用被删的 action，运行时会当作"不支持的 action"跳过（= 静默不生效），所以打开图时统一改写。
    ///
    /// 映射依据：同名新节点（如 109001→109008「获取当前回合数」），详见 tools/NodeDocMigration.md。
    /// 放在独立静态类里的原因：GraphEditorPanel.cs 有多个类，方法定义与调用点可能不在同一个类里（实测踩过 CS0103）。
    /// </summary>
    public static class ObsoleteActionMigration
    {
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "212003", "212005" },   // 重复动作直到（过时）      → 重复动作直到
            { "101001", "101017" },   // 获取牌堆（过时）          → 获取牌堆
            { "202008", "202041" },   // 造成伤害或法伤（过时）    → 造成伤害或法伤
            { "202009", "202035" },   // 伤害并分配给目标（过时）  → 造成伤害或法伤并分配给目标
            { "202010", "202036" },   // 随机目标伤害（过时）      → 对固定数量的随机目标造成伤害或法伤
            { "103009", "103020" },   // 获取卡牌定义属性（过时）  → 获取卡牌定义属性
            { "111003", "111012" },   // 筛选（过时）              → 筛选
            { "111006", "111013" },   // 排序（过时）              → 排序
            { "109001", "109008" },   // 获取当前回合数（过时）    → 获取当前回合数
        };

        /// <summary>执行旧 action → 新 action 的改写（幂等；不涉及引脚与连线）</summary>
        public static void Apply(GraphNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.action))
                return;
            string to;
            if (!Map.TryGetValue(node.action, out to))
                return;
            Debug.Log("[规则图] 旧图自愈：节点 action " + node.action + "(" + node.title + ") → " + to
                + "（该节点已从节点库移除）");
            node.action = to;
        }
    }
}
