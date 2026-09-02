using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Graph
{
    /// 端口类型：执行流(Exec) / 数据流(数值、布尔、文本、卡牌)
    /// 规约(见产品文档 3.4)：实线=exec 执行流，虚线=data 数据流
    public enum NodePortType
    {
        Exec,
        Bool,
        Int,
        String,
        Card
    }

    /// 一条连线：从 源节点 sourceNodeId.sourcePort 连到 目标节点 destNodeId.destPort
    /// 对应 .diycard 里的 connect = { sourceNodeId, sourcePort, destNodeId, destPort }
    [Serializable]
    public class GraphConnectionData
    {
        public string sourceNodeId;
        public string sourcePort;
        public string destNodeId;
        public string destPort;
    }

    /// 节点上的一个固定输入参数（key=端口名，值为异构数据）
    /// 仅在"该端口未被上游连线喂数据"时作为兜底值
    [Serializable]
    public class GraphNodeInput
    {
        public string name;              // 端口名，如 "a" / "count"
        public NodePortType type = NodePortType.Int;
        public int intValue;
        public bool boolValue;
        public string stringValue;       // 文本/卡牌引用(id) 等
    }

    /// 一个节点实例
    /// 对应 .diycard 里的 node = { id, defineId, posX, posY, inputs{} }
    [Serializable]
    public class GraphNodeData
    {
        public string id;               // 实例唯一 id（图内）
        public int defineId;            // 节点类型，对齐 NodeDoc.xml 的 defineId
        public float posX;
        public float posY;
        public List<GraphNodeInput> inputs = new List<GraphNodeInput>();
    }

    /// 节点图数据
    /// 对应 .diycard 里的 graph = { nodes[], connections[] }
    [Serializable]
    public class NodeGraphData
    {
        public List<GraphNodeData> nodes = new List<GraphNodeData>();
        public List<GraphConnectionData> connections = new List<GraphConnectionData>();

        public GraphNodeData FindNode(string id)
        {
            foreach (var n in nodes)
                if (n.id == id) return n;
            return null;
        }
    }

    /// 卡牌元信息（卡面/费用/攻血）
    [Serializable]
    public class CardGraphMeta
    {
        public string id;
        public string author;
        public string title;
        public int cost;
        public int attack;
        public int hp;
        public string type;
    }

    /// .diycard 顶层结构 = { schema, meta, graph }
    [Serializable]
    public class CardGraphData
    {
        public string schema = "diycard/1.0";
        public CardGraphMeta meta = new CardGraphMeta();
        public NodeGraphData graph = new NodeGraphData();

        public string ToJson() => JsonUtility.ToJson(this, true);
        public static CardGraphData FromJson(string json) => JsonUtility.FromJson<CardGraphData>(json);
    }
}