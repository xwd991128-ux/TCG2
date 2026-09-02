using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Graph
{
    /// <summary>
    /// 运行时解释器（纯自研数据层）。
    /// 读 NodeGraphData → 沿 connections 做数据流传播 → 每个 defineId 走一个 handler。
    ///
    /// 现阶段先把"取数 → 运算"的传播链打通（对标 xNode 的 GetInputValue 模式，但不引第三方）；
    /// 效果真正落地(桥接到 EffectData/ValueSource，如把手牌数量×攻转成抽牌)是下一层任务。
    /// </summary>
    public class NodeGraphRunner
    {
        /// 单个节点类型的处理器：计算该节点某输出端口的值
        public interface NodeHandler
        {
            object GetOutput(NodeGraphRunner runner, GraphNodeData node, string port);
        }

        // defineId -> 处理器（由 BuildHandlers 批量注册）
        static readonly Dictionary<int, NodeHandler> handlers = new Dictionary<int, NodeHandler>();

        NodeGraphData graph;
        // 节点输出缓存：key = "nodeId:port"，避免重复递归
        readonly Dictionary<string, object> outputCache = new Dictionary<string, object>();

        public NodeGraphRunner(NodeGraphData graph)
        {
            this.graph = graph;
        }

        /// 求某节点某输出端口的值（递归 + 缓存）
        public object GetOutput(GraphNodeData node, string port)
        {
            string key = node.id + ":" + port;
            if (outputCache.TryGetValue(key, out var v)) return v;

            if (handlers.TryGetValue(node.defineId, out var h))
            {
                v = h.GetOutput(this, node, port);
                outputCache[key] = v;
                return v;
            }
            return null; // 未注册(如 Tier2/3 节点)，由调用方兜底
        }

        /// 求某节点某输入端口的值：优先取"上游连线喂来的数据"，否则取节点固定输入
        /// 返回 boxed 值；handler 侧自己强转（GetInput(node,"a") ?? 0 等）
        public object GetInput(GraphNodeData node, string port)
        {
            // 1) 数据流：被某条连线喂入 → 取上游输出
            foreach (var c in graph.connections)
            {
                if (c.destNodeId == node.id && c.destPort == port)
                {
                    GraphNodeData src = graph.FindNode(c.sourceNodeId);
                    if (src != null)
                    {
                        object o = GetOutput(src, c.sourcePort);
                        if (o != null) return o;
                    }
                }
            }

            // 2) 固定输入：端口自带兜底值
            foreach (var inp in node.inputs)
            {
                if (inp.name == port) return GetTyped(inp);
            }
            return null;
        }

        object GetTyped(GraphNodeInput inp)
        {
            switch (inp.type)
            {
                case NodePortType.Int: return inp.intValue;
                case NodePortType.Bool: return inp.boolValue;
                case NodePortType.String: return inp.stringValue;
                case NodePortType.Card: return inp.stringValue; // 卡牌引用(id)
                default: return null;
            }
        }

        /// 注册节点处理器（Key=defineId）
        public void AddHandlers(Dictionary<int, NodeHandler> extra)
        {
            if (extra == null) return;
            foreach (var kv in extra) handlers[kv.Key] = kv.Value;
        }

        /// ---- 内置示例节点（defineId 占位，正式值以300 全量映射表为准）----
        public void BuildDefaultHandlers()
        {
            // IntegerConst：输出固定整数（取节点固定输入"值"，或上游连线喂入）
            handlers[112003] = (runner, node, port) =>
                (int)(runner.GetInput(node, "value") ?? 0);

            // IntegerOperation(Add)：a + b
            handlers[112004] = (runner, node, port) =>
                (int)(runner.GetInput(node, "a") ?? 0) + (int)(runner.GetInput(node, "b") ?? 0);

            // EffectDamage：取到目标卡引用，后续桥接现有 EffectDamage(amount=ValueSource)。
            // 现在只演示取值贯穿，实际造成伤害由桥接层完成。
            handlers[101001] = (runner, node, port) =>
                runner.GetInput(node, "target") as string;
        }
    }
}