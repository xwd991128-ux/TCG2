using System;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 程序化构建图的辅助类：简化节点/连线创建，用于生成示例图与自检数据。
    /// </summary>
    public static class GraphBuilder
    {
        /// <summary>新建事件节点（触发入口）</summary>
        public static GraphNode Event(string id, string action, string title)
        {
            GraphNode node = Base(id, GraphNodeType.Event, action, title);
            node.pins.Add(new GraphPin { id = id + "_out", name = "out", display_name = "触发", is_output = true, type = NodeValueType.Flow });
            return node;
        }

        /// <summary>新建动作节点</summary>
        public static GraphNode Action(string id, string action, string title, int value = 0)
        {
            GraphNode node = Base(id, GraphNodeType.Action, action, title);
            node.pins.Add(new GraphPin { id = id + "_in", name = "in", display_name = "入", is_output = false, type = NodeValueType.Flow });
            node.pins.Add(new GraphPin { id = id + "_out", name = "out", display_name = "出", is_output = true, type = NodeValueType.Flow });
            if (value != 0 || action == "Damage" || action == "Draw" || action == "Heal")
                node.fields.Add(new FieldCustomData { name = "value", value = value.ToString() });
            return node;
        }

        /// <summary>新建条件节点</summary>
        public static GraphNode Condition(string id, string action, string title)
        {
            GraphNode node = Base(id, GraphNodeType.Condition, action, title);
            node.pins.Add(new GraphPin { id = id + "_in", name = "in", display_name = "入", is_output = false, type = NodeValueType.Flow });
            node.pins.Add(new GraphPin { id = id + "_true", name = "true", display_name = "真", is_output = true, type = NodeValueType.Flow });
            node.pins.Add(new GraphPin { id = id + "_false", name = "false", display_name = "假", is_output = true, type = NodeValueType.Flow });
            return node;
        }

        private static GraphNode Base(string id, GraphNodeType type, string action, string title)
        {
            GraphNode node = new GraphNode();
            node.id = id;
            node.type = type;
            node.action = action;
            node.title = string.IsNullOrEmpty(title) ? action : title;
            return node;
        }

        /// <summary>建立连线（from 节点出线 → to 节点入线）</summary>
        public static void Link(GraphData graph, GraphNode from, GraphNode to,
            string from_pin = null, string to_pin = null)
        {
            string from_pin_id = from_pin ?? FirstPin(from).id;
            string to_pin_id = to_pin ?? FirstPin(to).id;
            graph.links.Add(new GraphLink
            {
                from_node = from.id,
                from_pin = from_pin_id,
                to_node = to.id,
                to_pin = to_pin_id,
            });
        }

        /// <summary>节点首个出线端口</summary>
        public static GraphPin FirstOutput(GraphNode node)
        {
            foreach (GraphPin pin in node.pins)
                if (pin.is_output)
                    return pin;
            return node.pins.Count > 0 ? node.pins[0] : null;
        }

        /// <summary>节点首个入线端口</summary>
        public static GraphPin FirstInput(GraphNode node)
        {
            foreach (GraphPin pin in node.pins)
                if (!pin.is_output)
                    return pin;
            return node.pins.Count > 0 ? node.pins[0] : null;
        }

        private static GraphPin FirstPin(GraphNode node)
        {
            return node.pins.Count > 0 ? node.pins[0] : null;
        }
    }
}