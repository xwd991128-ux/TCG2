using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 节点文档定义（对应 NodeDoc.xml 一条 ActionComment），照搬自 zmcs 完整游戏。
    /// </summary>
    public class NodeDocDef
    {
        public string define_id;      // defineId（如 "202001"），作为 GraphNode.action 与连线兼容键
        public string editor_name;    // 节点名（editorName，中文）
        public string category;       // 主题分类（卡牌/玩家/集合运算/…）
        public string summary;        // 功能说明（可能含 <br> 等 html）
        public string example;        // 示例说明
        public List<NodeDocPort> inputs = new List<NodeDocPort>();
        public List<NodeDocPort> outputs = new List<NodeDocPort>();

        public string CleanSummary()
        {
            if (string.IsNullOrEmpty(summary))
                return "";
            string s = summary.Replace("<br>", " ").Replace("<br/>", " ").Replace("</br>", " ");
            int cut = s.IndexOf("<ul>");
            if (cut >= 0)
                s = s.Substring(0, cut);
            s = s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&quot;", "\"");
            if (s.Length > 120)
                s = s.Substring(0, 117) + "…";
            return s.Trim();
        }
    }

    /// <summary>节点端口定义（NodeDoc.xml 的 ActionParameterComment）</summary>
    public class NodeDocPort
    {
        public string name;           // 端口名（ASCII，连线 key）
        public string display_name;   // 中文显示名
        public bool is_output;        // true=输出口（右侧），false=输入口（左侧）
        public NodeValueType type;    // 映射后的类型
        public string raw_type;       // 原始类型字符串（NodeValueRef<Boolean> 等）
        public bool is_array;
        public bool is_params;
    }

    /// <summary>
    /// NodeDoc.xml 数据驱动加载器：启动后从 Resources/NodeDoc.xml 解析出 zmcs 全部节点，
    /// 供规则编辑器“照搬”成可浏览/连线/保存的节点库。XML 位于 Assets/TcgEngine/Resources/NodeDoc.xml。
    /// </summary>
    public static class NodeDocDb
    {
        private static List<NodeDocDef> defs;
        private static Dictionary<string, NodeDocDef> by_id;
        private static List<string> categories;
        private static bool tried;

        /// <summary>zmcs 主题分类的稳定展示顺序（NodeDoc.xml 中出现顺序）</summary>
        public static IReadOnlyList<string> Categories
        {
            get { Ensure(); return categories; }
        }

        /// <summary>全部节点定义（319 条）</summary>
        public static IReadOnlyList<NodeDocDef> All
        {
            get { Ensure(); return defs; }
        }

        public static NodeDocDef Get(string define_id)
        {
            Ensure();
            if (define_id != null && by_id != null && by_id.TryGetValue(define_id, out NodeDocDef d))
                return d;
            return null;
        }

        private static void Ensure()
        {
            if (defs != null || tried)
                return;
            tried = true;
            defs = new List<NodeDocDef>();
            by_id = new Dictionary<string, NodeDocDef>();
            categories = new List<string>();

            TextAsset asset = Resources.Load<TextAsset>("NodeDoc");
            if (asset == null)
            {
                Debug.LogWarning("[NodeDoc] Resources/NodeDoc.xml 未找到，zmcs 节点库不可用");
                return;
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(asset.text);
                XmlNodeList list = doc.SelectNodes("//ActionComment");
                if (list != null)
                {
                    foreach (XmlNode n in list)
                    {
                        NodeDocDef def = Parse(n);
                        if (def == null)
                            continue;
                        defs.Add(def);
                        by_id[def.define_id] = def;
                        if (!categories.Contains(def.category))
                            categories.Add(def.category);
                    }
                }
                Debug.Log("[NodeDoc] 载入 zmcs 节点 " + defs.Count + " 个，分类 " + categories.Count + " 个");
            }
            catch (Exception e)
            {
                Debug.LogError("[NodeDoc] 解析失败: " + e.Message);
            }
        }

        private static NodeDocDef Parse(XmlNode node)
        {
            string id = Text(node, "defineId");
            if (string.IsNullOrEmpty(id))
                return null;
            NodeDocDef def = new NodeDocDef();
            def.define_id = id;
            def.editor_name = Text(node, "editorName") ?? id;
            def.category = Text(node, "category") ?? "其他";
            def.summary = Inner(node, "summary");
            def.example = Inner(node, "example");
            foreach (XmlNode p in node.SelectNodes("inputs/ActionParameterComment"))
            {
                NodeDocPort port = ParsePort(p);
                if (port != null)
                    def.inputs.Add(port);
            }
            foreach (XmlNode p in node.SelectNodes("outputs/ActionParameterComment"))
            {
                NodeDocPort port = ParsePort(p);
                if (port != null)
                {
                    port.is_output = true;
                    def.outputs.Add(port);
                }
            }
            return def;
        }

        private static NodeDocPort ParsePort(XmlNode node)
        {
            string raw = Text(node, "type");
            if (string.IsNullOrEmpty(raw))
                return null;
            NodeDocPort port = new NodeDocPort();
            port.name = Text(node, "name") ?? "";
            port.display_name = Text(node, "displayName") ?? port.name;
            port.raw_type = raw;
            port.is_array = Text(node, "isArray") == "true";
            port.is_params = Text(node, "isParams") == "true";
            port.type = MapType(raw);
            return port;
        }

        /// <summary>zmcs 原始类型名 → NodeValueType；未识别的归 Object（raw_type 仍保留）</summary>
        public static NodeValueType MapType(string raw)
        {
            switch (raw)
            {
                case "Flow": return NodeValueType.Flow;
                case "Boolean": return NodeValueType.Boolean;
                case "Int32": return NodeValueType.Int32;
                case "String": return NodeValueType.String;
                case "Object": return NodeValueType.Object;
                case "Player": return NodeValueType.Player;
                case "Card": return NodeValueType.Card;
                case "CardDefine": return NodeValueType.CardDefine;
                case "Pile": return NodeValueType.Pile;
                case "Buff": return NodeValueType.Buff;
                case "BuffDefine": return NodeValueType.BuffDefine;
                case "EventArg": return NodeValueType.EventArg;
                case "ActionNode": return NodeValueType.ActionNode;
                case "CardSnapshot": return NodeValueType.CardSnapshot;
                case "EventRecord": return NodeValueType.EventRecord;
                case "Effect": return NodeValueType.Effect;
                case "EffectTag": return NodeValueType.EffectTag;
                case "GraphMap": return NodeValueType.GraphMap;
                case "EventReference": return NodeValueType.EventReference;
                case "DefineReference": return NodeValueType.DefineReference;
                case "CardPoolIDProxy": return NodeValueType.CardPoolID;
                case "CardTagName": return NodeValueType.CardTagName;
                case "KeywordName": return NodeValueType.KeywordName;
                case "CardType": return NodeValueType.CardType;
                case "CompareOperator": return NodeValueType.CompareOperator;
                case "LogicOperator": return NodeValueType.LogicOperator;
                case "IntegerOperator": return NodeValueType.IntegerOperator;
                case "CardPropertyGetterName": return NodeValueType.CardPropertyGetterName;
                case "CardPropertySetterName": return NodeValueType.CardPropertySetterName;
                case "CardDefinePropertyGetterName": return NodeValueType.CardDefinePropertyGetterName;
                case "PileName": return NodeValueType.PileName;
                case "EventVarName": return NodeValueType.EventVarName;
                case "EventTriggerTime": return NodeValueType.EventTriggerTime;
                case "TypeName": return NodeValueType.TypeName;
                case "EffectType": return NodeValueType.EffectType;
                case "Pair": return NodeValueType.Pair;
                case "NodeValueRef<Boolean>":
                case "NodeValueRef<Object>":
                    return NodeValueType.NodeValueRef;
                case "CardDefineSelect": return NodeValueType.CardDefineSelect;
                case "Array": return NodeValueType.Array;
                default: return NodeValueType.Object;
            }
        }

        private static string Text(XmlNode node, string child)
        {
            XmlNode c = node.SelectSingleNode(child);
            return c != null ? c.InnerText : "";
        }

        private static string Inner(XmlNode node, string child)
        {
            XmlNode c = node.SelectSingleNode(child);
            return c != null ? c.InnerText : "";
        }
    }
}
