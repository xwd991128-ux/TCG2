using System;
using System.Collections.Generic;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 节点类型：
    /// Event     —— 触发入口（对应 AbilityTrigger 时机）
    /// Condition —— 条件判断（判真/假分叉）
    /// Action    —— 动作（伤害/抽牌等，原子操作）
    /// Value     —— 值节点（数值/引用，供其余节点输入）
    /// </summary>
    public enum GraphNodeType
    {
        Event = 0,
        Condition = 1,
        Action = 2,
        Value = 3,
    }

    /// <summary>
    /// 端口/引脚数据类型（参考 zmcs/NodeDoc.xml 节点系统规范）：
    /// Flow 为执行流（连线驱动流程），其余为数据流（连线传递数值/对象引用）。
    /// </summary>
    public enum NodeValueType
    {
        None = 0,        // 未指定/未知
        Flow = 1,        // 执行流（触发/下一步）
        Boolean = 2,     // 布尔
        Int32 = 3,       // 整数
        String = 4,      // 字符串
        Object = 5,      // 通用对象
        Player = 6,      // 玩家
        Card = 7,        // 卡牌实例
        CardDefine = 8,  // 卡牌定义
        Pile = 9,        // 牌堆
        Buff = 10,       // 增益实例
        BuffDefine = 11, // 增益定义
        EventArg = 12,   // 事件参数（动作产生的效果事件）
        ActionNode = 13, // 控制流：下一步动作
    }

    /// <summary>
    /// 节点引脚：用于在节点之间建立连接
    /// 输入引脚（is_output=false）在节点左侧，输出引脚（is_output=true）在节点右侧。
    /// </summary>
    [Serializable]
    public class GraphPin
    {
        public string id;             // 引脚唯一标识（节点内唯一）
        public string name;           // 字段名
        public string display_name;   // 显示名（中文，UI 展示用）
        public bool is_output;        // true=出线端口，false=入线端口
        public NodeValueType type = NodeValueType.None;  // 数据类型（Flow=执行流）
        public bool is_array;         // 是否数组/批量目标
    }

    /// <summary>
    /// 一条连接（从某节点出线端口 → 某节点入线端口）
    /// </summary>
    [Serializable]
    public class GraphLink
    {
        public string from_node;
        public string from_pin;
        public string to_node;
        public string to_pin;
    }

    /// <summary>
    /// 图中的一个节点（纯数据，可 JSON 序列化）
    /// 字段复用 FieldCustomData（name/value 字符串化），字段级编辑与现有反射体系互通
    /// </summary>
    [Serializable]
    public class GraphNode
    {
        public string id;                          // 节点唯一标识
        public GraphNodeType type;                 // 节点类型
        public string action;                      // 具体行为（如 "Damage"/"Draw"/"OnPlay"）
        public string title;                       // 节点显示名
        public Vector2Data pos;                    // 画布坐标
        public List<GraphPin> pins = new List<GraphPin>();
        public List<FieldCustomData> fields = new List<FieldCustomData>();
    }

    /// <summary>
    /// 序列化友好的二维坐标（UnityEngine.Vector2 不便于 JSON 稳定落盘）
    /// </summary>
    [Serializable]
    public struct Vector2Data
    {
        public float x;
        public float y;
        public Vector2Data(float x, float y) { this.x = x; this.y = y; }
    }

    /// <summary>
    /// 一张卡牌能力/规则的图（唯一落盘格式）
    /// 图由 Event 节点作为触发入口，向下连线到条件/动作节点驱动运算
    /// </summary>
    [Serializable]
    public class GraphData
    {
        public string name = "NewGraph";
        public List<GraphNode> nodes = new List<GraphNode>();
        public List<GraphLink> links = new List<GraphLink>();

        /// <summary>按 id 查询节点</summary>
        public GraphNode GetNode(string id)
        {
            foreach (GraphNode node in nodes)
            {
                if (node.id == id)
                    return node;
            }
            return null;
        }

        /// <summary>取某节点作为输出端的所有连接</summary>
        public List<GraphLink> GetOutgoing(string node_id, string pin_id = null)
        {
            List<GraphLink> list = new List<GraphLink>();
            foreach (GraphLink link in links)
            {
                if (link.from_node != node_id)
                    continue;
                if (pin_id != null && link.from_pin != pin_id)
                    continue;
                list.Add(link);
            }
            return list;
        }

        /// <summary>取某节点作为输入端的所有连接</summary>
        public List<GraphLink> GetIncoming(string node_id, string pin_id = null)
        {
            List<GraphLink> list = new List<GraphLink>();
            foreach (GraphLink link in links)
            {
                if (link.to_node != node_id)
                    continue;
                if (pin_id != null && link.to_pin != pin_id)
                    continue;
                list.Add(link);
            }
            return list;
        }
    }
}