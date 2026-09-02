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
        // ---- zmcs(NodeDoc) 扩展类型（追加于尾部，不影响旧图已存值） ----
        CardSnapshot = 14,     // 卡牌快照
        EventRecord = 15,      // 事件记录
        Effect = 16,           // 效果实例
        EffectTag = 17,        // 效果标签
        GraphMap = 18,         // 映射
        EventReference = 19,   // 事件引用
        DefineReference = 20,  // 定义引用
        CardPoolID = 21,       // 卡池 ID
        CardTagName = 22,      // 卡牌标签名
        KeywordName = 23,      // 关键词名
        CardType = 24,         // 卡牌类型
        CompareOperator = 25,  // 比较运算符（枚举）
        LogicOperator = 26,    // 逻辑运算符（枚举）
        IntegerOperator = 27,  // 整数运算符（枚举）
        CardPropertyGetterName = 28, // 卡牌属性取值器名
        CardPropertySetterName = 29, // 卡牌属性设置器名
        CardDefinePropertyGetterName = 30, // 卡牌定义属性取值器名
        PileName = 31,         // 牌堆名
        EventVarName = 32,     // 事件变量名
        EventTriggerTime = 33, // 事件触发时机
        TypeName = 34,         // 类型名
        EffectType = 35,       // 效果类型
        Pair = 36,             // 键值对
        NodeValueRef = 37,     // 值引用（泛型擦除）
        CardDefineSelect = 38, // 卡牌定义选择器
        Array = 39,            // 泛型数组（元素类型未知）
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
        public string action;                      // 具体行为（内置用 "Damage"；zmcs 节点用 defineId，如 "202001"）
        public string title;                       // 节点显示名
        public string category;                    // zmcs(NodeDoc) 主题分类名（如"卡牌"/"玩家"）；内置节点为空
        public Vector2Data pos;                    // 画布坐标
        public bool collapsed;                     // 收起节点：只显示头部，端口/描述隐藏，连线不断
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

        /// <summary>取连到某节点指定输入端口（完整引脚 id）的唯一入线，无则返回 null。
        /// 两线制取值：数据输入口有上游取值线时用上游值，否则用该口固定默认值。</summary>
        public GraphLink GetIncomingLink(string node_id, string pin_id)
        {
            foreach (GraphLink link in links)
            {
                if (link.to_node == node_id && link.to_pin == pin_id)
                    return link;
            }
            return null;
        }

        /// <summary>取某节点的指定引脚（按完整引脚 id），找不到返回 null</summary>
        public GraphPin GetPin(string node_id, string pin_id)
        {
            GraphNode node = GetNode(node_id);
            if (node == null || node.pins == null)
                return null;
            foreach (GraphPin p in node.pins)
            {
                if (p.id == pin_id)
                    return p;
            }
            return null;
        }

        /// <summary>取某节点的指定引脚（按短名），找不到返回 null</summary>
        public GraphPin GetPinByName(string node_id, string name)
        {
            GraphNode node = GetNode(node_id);
            if (node == null || node.pins == null)
                return null;
            foreach (GraphPin p in node.pins)
            {
                if (p.name == name)
                    return p;
            }
            return null;
        }
    }
}