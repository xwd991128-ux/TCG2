using System.Collections.Generic;
using TcgEngine.Gameplay;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// NodeDoc(zmcs) 图执行器 v1：在真实对局（GameLogic）里，从匹配的入口事件(Event)出发，
    /// 沿动作线(Flow)收集可达的 NodeDoc 动作节点，并按其 defineId 执行到 TCG2 游戏逻辑上。
    /// 第一批支持（垂直闭环）：202001 造成伤害、202016 消灭、治疗目标卡牌类动作。
    /// 说明：NodeDoc 数据来源/集合/属性查询等节点尚未接入（后续版本逐步实现），
    /// 未支持的 NodeDoc 动作会被静默跳过，不影响图保存与其余动作执行。
    /// </summary>
    public static class NodeDocRunner
    {
        /// <summary>
        /// 执行当前触发事件下的 NodeDoc 动作。
        /// </summary>
        /// <param name="logic">真实对局逻辑</param>
        /// <param name="graph">规则图</param>
        /// <param name="caster">施法/触发卡（图上下文：自身）</param>
        /// <param name="target_card">法术选中的卡牌目标（PlayTarget），可能为空</param>
        /// <param name="target_player">选中的玩家目标，可能为空</param>
        /// <param name="trigger_action">入口事件 action（如 OnPlay/StartOfTurn），空=执行第一个事件节点</param>
        /// <returns>实际执行的 NodeDoc 动作数</returns>
        public static int Run(GameLogic logic, GraphData graph, Card caster,
            Card target_card, Player target_player, string trigger_action)
        {
            int count = 0;
            if (logic == null || graph == null || caster == null)
                return 0;

            foreach (GraphNode ev in graph.nodes)
            {
                if (ev == null || ev.type != GraphNodeType.Event)
                    continue;
                if (!string.IsNullOrEmpty(trigger_action) && ev.action != trigger_action)
                    continue;

                List<GraphNode> acts = ReachableActions(graph, ev);
                foreach (GraphNode act in acts)
                {
                    if (act == null || act.type != GraphNodeType.Action)
                        continue;
                    if (string.IsNullOrEmpty(act.category))
                        continue;   //内置动作由 CardPoolIO 编译链路单独处理
                    ExecuteAction(logic, act, caster, target_card, target_player);
                    count++;
                }
            }
            return count;
        }

        /// <summary>沿 Flow 输出收集可达节点（跳过取值线），与 CardPoolIO 编译逻辑同规</summary>
        private static List<GraphNode> ReachableActions(GraphData graph, GraphNode from)
        {
            List<GraphNode> result = new List<GraphNode>();
            if (graph == null || from == null)
                return result;
            Stack<GraphNode> stack = new Stack<GraphNode>();
            HashSet<string> visited = new HashSet<string>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                GraphNode node = stack.Pop();
                if (node == null || !visited.Add(node.id))
                    continue;
                if (node.type == GraphNodeType.Action)
                    result.Add(node);   //记录动作并继续沿执行流下行（动作节点可能链式接下一动作）
                foreach (GraphLink link in graph.GetOutgoing(node.id))
                {
                    GraphPin out_pin = graph.GetPin(node.id, link.from_pin);
                    if (out_pin != null && out_pin.type != NodeValueType.Flow && out_pin.type != NodeValueType.None)
                        continue;
                    GraphNode next = graph.GetNode(link.to_node);
                    if (next != null)
                        stack.Push(next);
                }
            }
            return result;
        }

        /// <summary>按 defineId 把 NodeDoc 动作落到 TCG2 游戏逻辑（v1 白名单）</summary>
        private static void ExecuteAction(GameLogic logic, GraphNode act, Card caster,
            Card target_card, Player target_player)
        {
            switch (act.action)
            {
                case "202001":   //造成伤害（对传入目标/玩家）
                {
                    int value = GraphRuntime.GetFieldInt(act, "value", 1);
                    if (target_card != null)
                        logic.DamageCard(caster, target_card, value, true);
                    else if (target_player != null)
                        logic.DamagePlayer(caster, target_player, value);
                    break;
                }
                case "202016":   //消灭（对传入目标卡）
                {
                    if (target_card != null)
                    {
                        if (logic.GameData.IsOnBoard(target_card))
                            logic.KillCard(caster, target_card);
                        else
                            logic.DiscardCard(target_card);
                    }
                    break;
                }
                case "202013":   //治疗目标卡牌
                case "202039":
                case "202047":
                {
                    int value = GraphRuntime.GetFieldInt(act, "value", 1);
                    if (target_card != null)
                        logic.HealCard(target_card, value);
                    else if (target_player != null)
                        logic.HealPlayer(target_player, value);
                    break;
                }
                default:
                    //未支持动作静默跳过（后续版本扩充）
                    break;
            }
        }
    }
}
