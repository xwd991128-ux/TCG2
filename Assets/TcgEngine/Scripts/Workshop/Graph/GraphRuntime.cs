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
    /// 采用【两线制】：
    ///   动作线（Flow）→ 驱动执行顺序（从事件触发节点沿 Flow 输出线走到动作节点执行）；
    ///   取值线（数据端口）→ 只在取值时向上游求值，不驱动流程。
    /// 数据输入口的取值规则：优先取上游取值线的值；无连线时用该口的固定默认值（节点字段）。
    /// </summary>
    public static class GraphRuntime
    {
        public class ExecutionResult
        {
            public bool success = true;
            public string error = "";
            public List<string> log = new List<string>();
            public List<string> visited = new List<string>();      //已执行的节点 id
            public List<string> visited_links = new List<string>(); //走过的连线标识 "from|from_pin|to|to_pin"（供编辑器高亮走线）
        }

        /// <summary>
        /// 执行一张图。从 entry 事件节点开始，沿动作线执行所有可达的动作节点。
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

        /// <summary>
        /// 从节点开始递归执行（两线制）。
        /// 只沿【动作线（Flow 输出端口）】继续执行；取值线（数据输出端口）不驱动流程。
        /// 条件节点只走其判定的分支（真/假）输出口。
        /// </summary>
        private static void RunFrom(GraphData graph, IGraphHost host, GraphNode node, ExecutionResult result, HashSet<string> visited, int depth)
        {
            if (node == null || host == null || depth > 256)
                return;

            if (visited.Contains(node.id))
                return; //已执行过，防止回落成环
            visited.Add(node.id);
            result.visited.Add(node.id);

            string branch_pin = null;   //条件节点选中的输出端口短名（"true"/"false"）；null=沿所有动作线
            switch (node.type)
            {
                case GraphNodeType.Event:
                    host.Log("触发事件: " + node.title);
                    break;

                case GraphNodeType.Action:
                    ExecuteAction(graph, host, node, result);
                    break;

                case GraphNodeType.Condition:
                {
                    bool cond = EvaluateCondition(graph, node);
                    host.Log(node.title + " = " + (cond ? "真" : "假"));
                    branch_pin = cond ? "true" : "false";
                    break;
                }

                case GraphNodeType.Value:
                    host.Log(node.title + " = " + EvaluateValue(graph, node));
                    break;
            }

            //两线制：沿输出连线继续，但只跟动作线（Flow/None），取值线只传值不驱动执行
            List<GraphLink> outs = graph.GetOutgoing(node.id);
            foreach (GraphLink link in outs)
            {
                GraphPin out_pin = graph.GetPin(node.id, link.from_pin);
                if (out_pin != null && out_pin.type != NodeValueType.Flow && out_pin.type != NodeValueType.None)
                    continue;   //取值线：跳过（不驱动流程）
                if (branch_pin != null && (out_pin == null || out_pin.name != branch_pin))
                    continue;   //条件分支：只走选中输出

                //记录走过的动作线（编辑器高亮走线用）
                result.visited_links.Add(link.from_node + "|" + link.from_pin + "|" + link.to_node + "|" + link.to_pin);

                GraphNode next = graph.GetNode(link.to_node);
                RunFrom(graph, host, next, result, visited, depth + 1);
            }
        }

        /// <summary>执行一个动作节点（数值参数按两线制取值：接取值线用上游，否则用固定字段值）</summary>
        private static void ExecuteAction(GraphData graph, IGraphHost host, GraphNode node, ExecutionResult result)
        {
            if (node == null || string.IsNullOrEmpty(node.action))
            {
                host.Log("动作节点缺少 action: " + (node != null ? node.title : ""));
                return;
            }

            string action = node.action;
            string tgt = GetFieldString(node, "target", "");   //目标范围（可空，仅用于日志/模拟展示）
            if (action == "Damage")
            {
                int value = GetInputInt(graph, node, "value", GetFieldInt(node, "value", 1));
                int dealt = host.Damage(value);
                host.Log(node.title + "[" + (string.IsNullOrEmpty(tgt) ? "目标" : tgt) + "] 造成 " + dealt + " 点伤害");
            }
            else if (action == "Heal")
            {
                int value = GetInputInt(graph, node, "value", GetFieldInt(node, "value", 1));
                int healed = host.Heal(value);
                host.Log(node.title + "[" + (string.IsNullOrEmpty(tgt) ? "己方英雄" : tgt) + "] 治疗 " + healed + " 点");
            }
            else if (action == "Draw")
            {
                int nb = GetInputInt(graph, node, "count", GetFieldInt(node, "value", 1));
                int drawn = host.Draw(nb);
                host.Log(node.title + " 抽 " + drawn + " 张牌");
            }
            else if (action == "GainMana")
            {
                int amount = GetInputInt(graph, node, "amount", GetFieldInt(node, "value", 1));
                string mode = GetFieldString(node, "mana_mode", "增加上限(空水晶)");
                host.Log(node.title + "[" + mode + "] 数量 " + amount + "（真实对局接入法力系统）");
            }
            else if (action == "Destroy")
            {
                host.Log(node.title + "[" + (string.IsNullOrEmpty(tgt) ? "目标" : tgt) + "] 消灭目标（模拟）");
            }
            else if (action == "AddAttack" || action == "AddHP")
            {
                int value = GetInputInt(graph, node, "value", GetFieldInt(node, "value", 1));
                host.Log(node.title + "[" + (string.IsNullOrEmpty(tgt) ? "自身" : tgt) + "] +" + value);
            }
            else if (action == "ReturnHand" || action == "ShuffleDeck")
            {
                host.Log(node.title + "[" + (string.IsNullOrEmpty(tgt) ? "目标" : tgt) + "] 移入指定牌堆（真实对局按牌堆执行）");
            }
            else if (action == "Summon")
            {
                host.Log(node.title + "（待接入真实宿主）");
            }
            else
            {
                host.Log("未识别的动作: " + action);
            }
        }

        /// <summary>求值一个值/运算节点（常量/比较/运算/随机；生命/法力/攻击需真实对局上下文返回 0）</summary>
        public static int EvaluateValue(GraphData graph, GraphNode node)
        {
            if (node == null)
                return 0;
            switch (node.action)
            {
                case "IntegerConst":
                    return GetFieldInt(node, "value", 0);
                case "BooleanConst":
                    return GetFieldString(node, "value", "true") == "true" ? 1 : 0;
                case "Compare":
                {
                    int a = GetInputInt(graph, node, "a", GetFieldInt(node, "a", 0));
                    int b = GetInputInt(graph, node, "b", GetFieldInt(node, "b", 0));
                    return Compare(a, b, GetFieldString(node, "op", "==")) ? 1 : 0;
                }
                case "IntegerOperation":
                {
                    int a = GetInputInt(graph, node, "a", GetFieldInt(node, "a", 0));
                    int b = GetInputInt(graph, node, "b", GetFieldInt(node, "b", 0));
                    return Operate(a, b, GetFieldString(node, "op", "+"));
                }
                case "Random":
                    return UnityEngine.Random.Range(0, Mathf.Max(1, GetInputInt(graph, node, "max", GetFieldInt(node, "value", 10))));
                default:
                    return 0; //Health/Mana/Attack 等需要真实对局上下文
            }
        }

        /// <summary>条件判定（P1 用固定字段值近似；真实对局上下文 P3 接入）</summary>
        public static bool EvaluateCondition(GraphData graph, GraphNode node)
        {
            if (node == null)
                return false;
            switch (node.action)
            {
                case "IfRandom":
                {
                    int chance = GetInputInt(graph, node, "chance", GetFieldInt(node, "value", 50));
                    return UnityEngine.Random.Range(0, 100) < chance;
                }
                case "IfMana":
                {
                    int need = GetInputInt(graph, node, "value", GetFieldInt(node, "value", 1));
                    return need > 0; //真实对局比较当前法力，P1 用固定值近似
                }
                case "IfHealth":
                case "IfTarget":
                default:
                {
                    int value = GetInputInt(graph, node, "value", GetFieldInt(node, "value", 1));
                    return value > 0; //真实对局比较目标/场上，P1 用固定值近似
                }
            }
        }

        // ---------------- 两线制取值 ----------------

        /// <summary>数据输入口取值：接上游取值线则用上游值，否则用该口固定默认值（节点字段）</summary>
        public static int GetInputInt(GraphData graph, GraphNode node, string pin_name, int def)
        {
            if (node == null)
                return def;
            GraphPin pin = FindPin(node, pin_name);
            if (pin != null)
            {
                GraphLink in_link = (graph != null) ? graph.GetIncomingLink(node.id, pin.id) : null;
                if (in_link != null && TrySourceInt(graph, in_link.from_node, out int src_val))
                    return src_val;
            }
            //无连线 → 固定值（节点字段；部分节点端口名与字段名不同，如 count/amount/max → value）
            return GetFieldInt(node, FieldNameForPin(node, pin_name), def);
        }

        /// <summary>布尔输入口取值（同上规则）</summary>
        public static bool GetInputBool(GraphData graph, GraphNode node, string pin_name, bool def)
        {
            if (node == null)
                return def;
            GraphPin pin = FindPin(node, pin_name);
            if (pin != null)
            {
                GraphLink in_link = (graph != null) ? graph.GetIncomingLink(node.id, pin.id) : null;
                if (in_link != null)
                {
                    GraphNode src = graph.GetNode(in_link.from_node);
                    if (src != null)
                    {
                        if (src.type == GraphNodeType.Value)
                            return EvaluateValue(graph, src) != 0;
                        if (src.type == GraphNodeType.Condition)
                            return EvaluateCondition(graph, src);
                    }
                }
            }
            string s = GetFieldString(node, FieldNameForPin(node, pin_name), def ? "true" : "false");
            return s == "true" || s == "1";
        }

        /// <summary>字符串输入口取值（同上规则）</summary>
        public static string GetInputString(GraphData graph, GraphNode node, string pin_name, string def)
        {
            if (node == null)
                return def;
            GraphPin pin = FindPin(node, pin_name);
            if (pin != null)
            {
                GraphLink in_link = (graph != null) ? graph.GetIncomingLink(node.id, pin.id) : null;
                if (in_link != null)
                {
                    GraphNode src = graph.GetNode(in_link.from_node);
                    if (src != null)
                    {
                        if (src.type == GraphNodeType.Value)
                            return EvaluateValue(graph, src).ToString();
                        if (src.type == GraphNodeType.Condition)
                            return EvaluateCondition(graph, src) ? "true" : "false";
                    }
                }
            }
            return GetFieldString(node, FieldNameForPin(node, pin_name), def);
        }

        /// <summary>尝试从上游节点求出一个整数（值节点 / 条件节点）</summary>
        private static bool TrySourceInt(GraphData graph, string source_node_id, out int val)
        {
            val = 0;
            if (graph == null)
                return false;
            GraphNode src = graph.GetNode(source_node_id);
            if (src == null)
                return false;
            if (src.type == GraphNodeType.Value)
            {
                val = EvaluateValue(graph, src);
                return true;
            }
            if (src.type == GraphNodeType.Condition)
            {
                val = EvaluateCondition(graph, src) ? 1 : 0;
                return true;
            }
            return false;
        }

        /// <summary>端口短名 → 固定值字段名（部分节点端口名与字段名不同：Draw 的 count、GainMana 的 amount、Random 的 max 都存于 value 字段）</summary>
        private static string FieldNameForPin(GraphNode node, string pin_name)
        {
            if (pin_name == "count" || pin_name == "amount" || pin_name == "max")
                return "value";
            return pin_name;
        }

        /// <summary>按短名查找节点引脚</summary>
        private static GraphPin FindPin(GraphNode node, string pin_name)
        {
            if (node == null || node.pins == null)
                return null;
            foreach (GraphPin p in node.pins)
            {
                if (p.name == pin_name)
                    return p;
            }
            return null;
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
