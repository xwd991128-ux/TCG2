using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>自定义节点的三大类别（创建时先选类型）。</summary>
    public enum CustomNodeKind
    {
        Action = 0,     //动作节点：有执行流（动作流输入/输出），可在动作流中间串联
        Function = 1,   //函数节点：无执行流，只有数据端口，作为取值/计算子节点
        Event = 2,      //事件节点：有执行流，且是"事件声明"→ 自动生成「XX时 / XX后」两个关联子节点
    }

    /// <summary>自定义节点的一个端口（玩家自己定义，可任意增删）。</summary>
    [Serializable]
    public class CustomNodePort
    {
        public string name;                                  //端口名（玩家填，蓝图块上的标签）
        public string type = nameof(NodeValueType.Object);   //NodeValueType 枚举名（数字/文本/布尔/卡牌/玩家/通用Object…）
        public bool is_array;

        public CustomNodePort() { }

        public CustomNodePort(string name, string type, bool is_array = false)
        {
            this.name = name;
            this.type = type;
            this.is_array = is_array;
        }

        public NodeValueType Type
        {
            get
            {
                NodeValueType t;
                if (!string.IsNullOrEmpty(type) && Enum.TryParse(type, out t))
                    return t;
                return NodeValueType.Object;
            }
        }

        public CustomNodePort Clone()
        {
            return new CustomNodePort(name, type, is_array);
        }
    }

    /// <summary>
    /// ★玩家自定义节点（DIY 节点）的数据定义 —— 对应 Workshop/custom_nodes.json。
    ///
    /// 三类结构（kind）：
    ///   · Action（动作）：本体图 = 蓝图块（动作流输入/输出 + 自定义端口）+ 玩家编排的内部流程
    ///   · Function（函数）：本体图 = 蓝图块（只有自定义数据端口，无执行流）
    ///   · Event（事件）：**两张图** —— graphs[0]="XX时"、graphs[1]="XX后"，各自一个事件入口节点（自动生成）
    /// 端口由玩家自由增删（含排序），最终会同步到蓝图块的 GraphNode.pins（画布是数据驱动的）。
    /// </summary>
    [Serializable]
    public class CustomNodeData
    {
        public string id;
        public string title = "新自定义节点";
        public string desc = "";
        public int kind = (int)CustomNodeKind.Action;
        public List<CustomNodePort> inputs = new List<CustomNodePort>();   //自定义数据输入端口
        public List<CustomNodePort> outputs = new List<CustomNodePort>();  //自定义数据输出端口
        public List<string> tags = new List<string>();

        /// <summary>★事件节点专用：监听哪个引擎事件（GraphEventPresets 里的 action，如 OnBeforeDamage/OnAfterDamage）。
        /// 归一成 "OnBefore..." 为基准：graphs[0]（XX时）= 基准动作、graphs[1]（XX后）= OnAfter 版本。</summary>
        public string listen_action = "";

        //编排结果：动作/函数 = 1 张；事件 = 2 张（时 / 后）。写法与 BattleButtonConfig/BuffData 的"多图"完全同规。
        public GraphData graph;                                        //兼容字段 = graphs[0]
        public List<CardEffectData> graphs = new List<CardEffectData>();

        public CustomNodeKind Kind { get { return (CustomNodeKind)kind; } }
        public string GetTitle() { return string.IsNullOrEmpty(title) ? (string.IsNullOrEmpty(id) ? "未命名节点" : id) : title; }

        /// <summary>该类别应有的图数量（动作/函数=1，事件=2）</summary>
        public int GraphCount { get { return Kind == CustomNodeKind.Event ? 2 : 1; } }

        /// <summary>第 i 张图的名字（事件：XX时 / XX后；其它：本体）</summary>
        public string GraphName(int index)
        {
            if (Kind != CustomNodeKind.Event)
                return GetTitle();
            return GetTitle() + (index <= 0 ? "时" : "后");
        }

        /// <summary>按需补齐 graphs 列表（旧数据/新建都走这里；与 BuffData.EnsureGraphs 同规）</summary>
        public List<CardEffectData> EnsureGraphs()
        {
            if (graphs == null)
                graphs = new List<CardEffectData>();
            int want = GraphCount;
            //切换类型后图数量可能变化（动作↔事件）：多的保留（切回来还在），少的补齐
            while (graphs.Count < want)
            {
                CardEffectData e = new CardEffectData();
                e.graph = new GraphData();
                graphs.Add(e);
            }
            for (int i = 0; i < graphs.Count; i++)
            {
                if (graphs[i] == null)
                    graphs[i] = new CardEffectData();
                if (graphs[i].graph == null)
                    graphs[i].graph = new GraphData();
                graphs[i].name = GraphName(i);
                if (string.IsNullOrEmpty(graphs[i].graph.name))
                    graphs[i].graph.name = "custom_" + id + "_" + i;
            }
            graph = graphs[0].graph;   //兼容字段
            return graphs;
        }

        public CustomNodePort AddPort(bool output)
        {
            List<CustomNodePort> list = output ? outputs : inputs;
            if (list == null)
            {
                list = new List<CustomNodePort>();
                if (output) outputs = list; else inputs = list;
            }
            CustomNodePort p = new CustomNodePort(output ? ("out" + (list.Count + 1)) : ("in" + (list.Count + 1)), nameof(NodeValueType.Int32));
            list.Add(p);
            return p;
        }

        public void MovePort(bool output, int index, int delta)
        {
            List<CustomNodePort> list = output ? outputs : inputs;
            if (list == null)
                return;
            int to = index + delta;
            if (index < 0 || index >= list.Count || to < 0 || to >= list.Count)
                return;
            CustomNodePort p = list[index];
            list.RemoveAt(index);
            list.Insert(to, p);
        }

        /// <summary>校验：端口名非空 + 动作/事件节点必须有动作流输入输出 + 端口类型合法。返回问题列表（空=通过）</summary>
        public List<string> Validate()
        {
            List<string> issues = new List<string>();
            if (string.IsNullOrWhiteSpace(title))
                issues.Add("节点名称不能为空");
            CheckPorts(inputs, "输入", issues);
            CheckPorts(outputs, "输出", issues);
            if (Kind == CustomNodeKind.Function && (inputs == null || inputs.Count == 0) && (outputs == null || outputs.Count == 0))
                issues.Add("函数节点至少要有一个输入或输出端口");
            return issues;
        }

        private static void CheckPorts(List<CustomNodePort> list, string label, List<string> issues)
        {
            if (list == null)
                return;
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                CustomNodePort p = list[i];
                if (p == null)
                {
                    issues.Add(label + "端口第 " + (i + 1) + " 行是空的");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(p.name))
                    issues.Add(label + "端口第 " + (i + 1) + " 行的端口名为空");
                else if (!seen.Add(p.name.Trim()))
                    issues.Add(label + "端口名重复：" + p.name);
                if (p.Type == NodeValueType.None || p.Type == NodeValueType.Flow)
                    issues.Add(label + "端口「" + p.name + "」的类型不合法（不能是 None/执行流）");
            }
        }

        /// <summary>蓝图块/事件入口节点的 action 名（节点库与运行时按它识别；也用于图内节点引用）</summary>
        public string ActionId { get { return "custom_" + id; } }

        /// <summary>事件基准动作（把 OnAfter 归一成 OnBefore；空=未配置 → 退回 custom_ 命名）</summary>
        public string ListenBase
        {
            get
            {
                if (string.IsNullOrEmpty(listen_action))
                    return "";
                return listen_action.Replace("OnAfter", "OnBefore");
            }
        }

        /// <summary>事件"后"动作（OnAfter 版本）</summary>
        public string ListenAfter
        {
            get
            {
                string b = ListenBase;
                return string.IsNullOrEmpty(b) ? "" : b.Replace("OnBefore", "OnAfter");
            }
        }

        /// <summary>事件定义第 i 张图的入口节点 action（时=基准 / 后=OnAfter；未配置监听事件时退回 custom_ 命名）</summary>
        public string EventEntryAction(int index)
        {
            string base_action = ListenBase;
            if (string.IsNullOrEmpty(base_action))
                return ActionId + (index <= 0 ? "_when" : "_after");
            return index <= 0 ? base_action : ListenAfter;
        }
    }

    /// <summary>custom_nodes.json 的根对象（与 BattleButtonConfig 同规）</summary>
    [Serializable]
    public class CustomNodeConfig
    {
        public string timestamp;
        public CustomNodeData[] nodes = new CustomNodeData[0];
    }
}
