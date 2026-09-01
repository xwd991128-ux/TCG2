using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 图执行宿主：把图中的"动作"与具体的游戏执行环境解耦。
    /// P1 阶段用一个模拟宿主来验证"环形闭环"（触发→动作→结果）；
    /// P3 阶段提供接入真实 GameLogic 的宿主实现。
    /// </summary>
    public interface IGraphHost
    {
        /// <summary>对给定目标值施加伤害，返回实际造成的伤害量</summary>
        int Damage(int value);
        /// <summary>治疗指定值，返回实际治疗量</summary>
        int Heal(int value);
        /// <summary>抽 N 张牌，返回实际抽牌数</summary>
        int Draw(int nb);
        /// <summary>记录执行日志（供 UI/调试展示）</summary>
        void Log(string msg);
    }

    /// <summary>
    /// 图执行器：解释执行 GraphData。
    /// 从 Event 触发节点出发，沿连线驱动到 Action 节点并执行动作。
    /// </summary>
    public static class GraphRuntime
    {
        public class ExecutionResult
        {
            public bool success = true;
            public string error = "";
            public List<string> log = new List<string>();
            public List<string> visited = new List<string>(); //已执行的节点 id
        }

        /// <summary>
        /// 执行一张图。从 entry 事件节点开始，执行所有可达的动作节点。
        /// </summary>
        /// <param name="graph">要执行的图</param>
        /// <param name="eventAction">事件触发器标识（如 AbilityTrigger 枚举名 "OnPlay"/"StartOfTurn"），为空则执行第一个事件节点</param>
        public static ExecutionResult Execute(GraphData graph, IGraphHost host, string eventAction = "")
        {
            ExecutionResult result = new ExecutionResult();
            if (graph == null)
            {
                result.success = false;
                result.error = "图为空";
                return result;
            }

            GraphNode entry = FindEntry(graph, eventAction);
            if (entry == null)
            {
                result.success = false;
                result.error = "未找到匹配触发器(" + (eventAction ?? "") + ")的事件节点";
                return result;
            }

            RunFrom(graph, host, entry, result, new HashSet<string>(), 0);
            return result;
        }

        /// <summary>找到事件入口节点</summary>
        private static GraphNode FindEntry(GraphData graph, string eventAction)
        {
            GraphNode first_event = null;
            foreach (GraphNode node in graph.nodes)
            {
                if (node == null || node.type != GraphNodeType.Event)
                    continue;
                if (first_event == null)
                    first_event = node;
                if (!string.IsNullOrEmpty(eventAction) && node.action == eventAction)
                    return node;
            }
            return first_event;
        }

        /// <summary>从节点开始递归执行（沿输出连线，深度优先，防止环）</summary>
        private static void RunFrom(GraphData graph, IGraphHost host, GraphNode node, ExecutionResult result, HashSet<string> visited, int depth)
        {
            if (node == null || host == null || depth > 256)
                return;

            if (visited.Contains(node.id))
                return; //已执行过，防止回落成环
            visited.Add(node.id);
            result.visited.Add(node.id);

            switch (node.type)
            {
                case GraphNodeType.Event:
                    host.Log("触发事件: " + node.title);
                    break;

                case GraphNodeType.Action:
                    ExecuteAction(host, node, result);
                    break;

                case GraphNodeType.Condition:
                    host.Log("条件节点(待 P3 实现判定): " + node.title);
                    break;

                case GraphNodeType.Value:
                    host.Log(node.title + " = " + EvaluateValue(node));
                    break;
            }

            //沿输出连线继续
            List<GraphLink> outs = graph.GetOutgoing(node.id);
            foreach (GraphLink link in outs)
            {
                GraphNode next = graph.GetNode(link.to_node);
                RunFrom(graph, host, next, result, visited, depth + 1);
            }
        }

        /// <summary>执行一个动作节点</summary>
        private static void ExecuteAction(IGraphHost host, GraphNode node, ExecutionResult result)
        {
            if (node == null || string.IsNullOrEmpty(node.action))
            {
                host.Log("动作节点缺少 action: " + node.title);
                return;
            }

            string action = node.action;
            int value = GetFieldInt(node, "value", 0);

            if (action == "Damage")
            {
                int dealt = host.Damage(value);
                host.Log(node.title + " 造成 " + dealt + " 点伤害");
            }
            else if (action == "Heal")
            {
                int healed = host.Heal(value);
                host.Log(node.title + " 治疗 " + healed + " 点");
            }
            else if (action == "Draw")
            {
                int drawn = host.Draw(value);
                host.Log(node.title + " 抽 " + drawn + " 张牌");
            }
            else if (action == "GainMana")
            {
                host.Log(node.title + " 获得 " + value + " 点法力（待接入真实宿主）");
            }
            else if (action == "Summon" || action == "Destroy")
            {
                host.Log(node.title + "（待接入真实宿主）");
            }
            else
            {
                host.Log("未识别的动作: " + action);
            }
        }

        /// <summary>求值一个值/运算节点（常量/比较/运算/随机；生命/法力/攻击需真实对局上下文返回 0）</summary>
        public static int EvaluateValue(GraphNode node)
        {
            if (node == null)
                return 0;
            switch (node.action)
            {
                case "IntegerConst":
                    return GetFieldInt(node, "value", 0);
                case "Compare":
                    return Compare(GetFieldInt(node, "a", 0), GetFieldInt(node, "b", 0), GetFieldString(node, "op", "==")) ? 1 : 0;
                case "IntegerOperation":
                    return Operate(GetFieldInt(node, "a", 0), GetFieldInt(node, "b", 0), GetFieldString(node, "op", "+"));
                case "Random":
                    return UnityEngine.Random.Range(0, Mathf.Max(1, GetFieldInt(node, "value", 10)));
                default:
                    return 0; //Health/Mana/Attack 等需要真实对局上下文
            }
        }

        /// <summary>整数比较（op：> < >= <= == !=）</summary>
        public static bool Compare(int a, int b, string op)
        {
            switch (op)
            {
                case ">": return a > b;
                case "<": return a < b;
                case ">=": return a >= b;
                case "<=": return a <= b;
                case "!=": return a != b;
                default: return a == b;
            }
        }

        /// <summary>整数运算（op：+ - * / %）</summary>
        public static int Operate(int a, int b, string op)
        {
            switch (op)
            {
                case "+": return a + b;
                case "-": return a - b;
                case "*": return a * b;
                case "/": return b != 0 ? a / b : 0;
                case "%": return b != 0 ? a % b : 0;
                default: return a;
            }
        }

        /// <summary>从节点字段表中读取 int（找不到返回默认值）</summary>
        public static int GetFieldInt(GraphNode node, string name, int def)
        {
            if (node == null || node.fields == null)
                return def;
            foreach (FieldCustomData field in node.fields)
            {
                if (field.name == name && int.TryParse(field.value, out int val))
                    return val;
            }
            return def;
        }

        /// <summary>从节点字段表中读取 string</summary>
        public static string GetFieldString(GraphNode node, string name, string def = "")
        {
            if (node == null || node.fields == null)
                return def;
            foreach (FieldCustomData field in node.fields)
            {
                if (field.name == name)
                    return field.value;
            }
            return def;
        }
    }

    /// <summary>
    /// P1 模拟宿主：不连接真实对局，仅用于验证图执行闭环（触发→动作→结果累计）
    /// </summary>
    public class SimulatedGraphHost : IGraphHost
    {
        public int hp = 30;
        public int hand = 5;
        public int log_count = 0;

        public int Damage(int value)
        {
            int dealt = Mathf.Min(value, hp);
            hp -= dealt;
            return dealt;
        }

        public int Heal(int value)
        {
            int healed = value;
            hp += healed;
            return healed;
        }

        public int Draw(int nb)
        {
            hand += nb;
            return nb;
        }

        public void Log(string msg)
        {
            log_count++;
            Debug.Log("[Graph] " + msg);
        }
    }
}