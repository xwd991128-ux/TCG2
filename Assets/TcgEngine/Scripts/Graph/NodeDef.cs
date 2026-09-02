using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Graph
{
    /// 端口蓝图：对应 NodeDoc.xml 内的 ActionParameterComment
    [Serializable]
    public class NodePortDef
    {
        public string name;          // 端口名，如 a / return
        public string displayName;   // 显示名，如 值 / 总和
        public NodePortType type = NodePortType.Int;
        public bool isArray;
    }

    /// 节点类型蓝图：一个 defineId 配一套输入/输出端口
    /// 数量由 阶段2-节点全量映射表 决定（Tier1 首批 101 个 ✓现成优先）
    [CreateAssetMenu(fileName = "node_def", menuName = "TcgEngine/Graph/NodeDef", order = 50)]
    public class NodeDef : ScriptableObject
    {
        public int defineId;                  // 对齐 NodeDoc.xml
        public string nodeName;               // editorName
        public string category;               // 卡牌 / 效果 / 行动 ...

        [Header("Ports")]
        public List<NodePortDef> inputs = new List<NodePortDef>();
        public List<NodePortDef> outputs = new List<NodePortDef>();

        public NodePortDef GetInput(string name) => inputs.Find(p => p.name == name);
        public NodePortDef GetOutput(string name) => outputs.Find(p => p.name == name);
    }

    /// 全局节点类型注册表：运行时按 defineId 查蓝图
    /// 编辑器/导入 .diycard 时自动 Register（后续接映射表批量生成）
    public static class NodeDefs
    {
        static readonly Dictionary<int, NodeDef> defs = new Dictionary<int, NodeDef>();

        public static void Register(NodeDef def)
        {
            if (def == null) return;
            defs[def.defineId] = def;
        }

        public static bool TryGet(int defineId, out NodeDef def) => defs.TryGetValue(defineId, out def);

        public static IEnumerable<NodeDef> All() => defs.Values;
    }
}