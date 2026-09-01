using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 图数据的 JSON 读写（JsonUtility 封装）
    /// 运行时与编辑器均可使用。
    /// </summary>
    public static class GraphIO
    {
        /// <summary>图 → JSON 字符串</summary>
        public static string ToJson(GraphData graph)
        {
            return JsonUtility.ToJson(graph, true);
        }

        /// <summary>JSON 字符串 → 图</summary>
        public static GraphData FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonUtility.FromJson<GraphData>(json);
        }

        /// <summary>图中是否存在某节点且字段值匹配</summary>
        public static bool HasFieldValue(GraphData graph, string node_id, string field_name, string value)
        {
            if (graph == null)
                return false;
            GraphNode node = graph.GetNode(node_id);
            if (node == null)
                return false;
            foreach (FieldCustomData field in node.fields)
            {
                if (field.name == field_name && field.value == value)
                    return true;
            }
            return false;
        }

        /// <summary>图保存为 JSON 文件</summary>
        public static void SaveToFile(GraphData graph, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson(graph));
        }

        /// <summary>从 JSON 文件加载图</summary>
        public static GraphData LoadFromFile(string path)
        {
            if (!File.Exists(path))
                return null;
            return FromJson(File.ReadAllText(path));
        }
    }
}

#if UNITY_EDITOR
namespace TcgEngine.Workshop
{
    /// <summary>
    /// 编辑器测试入口：图 → JSON → 图 往返校验 + 最简示例图执行
    /// P1 里程碑验证（对应"图→JSON→图往返校验 + 跑出伤害"）
    /// </summary>
    public static class GraphBuildStress
    {
        [MenuItem("TcgEngine/卡牌编辑器/生成示例卡池JSON")]
        public static void GenerateSamplePoolFile()
        {
            //组装一张带示例图的可编辑卡池（含名称/描述/作者 + 图）
            CardPoolData pool = new CardPoolData();
            pool.name = "示例卡池";
            pool.description = "由编辑器工具生成的示例卡池（用来在卡池页直接点「编辑」测试）";
            pool.author = "Tools";
            pool.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            pool.graph = BuildSampleGraph(); //OnPlay触发→伤害3→抽1

            //给示例池加一张示例卡牌，供卡牌列表展示
            CardCustomData card = new CardCustomData();
            card.id = "sample_hero_1";
            card.title = "示例火焰随从";
            card.type = "Character";
            card.team = "";
            card.rarity = "";
            card.mana = 3;
            card.attack = 4;
            card.hp = 5;
            card.text = "示例卡牌：4/5 随从。";
            card.deckbuilding = true;
            card.abilities = new List<AbilityCustomData>();
            pool.cards.Add(card);

            string json = JsonUtility.ToJson(pool, true);
            string path = Path.Combine(CardPoolIO.SaveFolder, "sample_pool.json");
            Directory.CreateDirectory(CardPoolIO.SaveFolder);
            File.WriteAllText(path, json);

            Debug.Log("[GraphBuildStress] 已生成示例卡池: " + path);
            EditorUtility.DisplayDialog("卡牌编辑器",
                "已生成带示例图的本地卡池:\n" + path
                + "\n\n请到卡池管理页刷新，点击该卡池的「编辑」按钮即可进入编辑器体验 P1 流程。", "确定");
        }

        [MenuItem("TcgEngine/卡牌编辑器/示例图往返校验 + 执行测试")]
        public static void RunStress()
        {
            //1. 构建一张最简示例图：OnPlay 触发 → 对敌造成3点伤害 → 抽1张
            GraphData graph = BuildSampleGraph();

            //2. 图 → JSON
            string json = GraphIO.ToJson(graph);
            Debug.Log("[GraphBuildStress] 序列化JSON:\n" + json);

            //3. JSON → 图（往返）
            GraphData parsed = GraphIO.FromJson(json);
            bool roundtrip = parsed != null && CountNodes(parsed) == CountNodes(graph);
            Debug.Log("[GraphBuildStress] 往返节点数一致: " + (parsed != null ? CountNodes(parsed) : 0)
                      + " / 原图 " + CountNodes(graph) + " => " + (roundtrip ? "通过" : "失败"));

            //4. 检查字段未丢失
            bool field_ok = parsed != null && GraphIO.HasFieldValue(parsed, "damage_act", "value", "3");
            Debug.Log("[GraphBuildStress] 字段 value=3 保留: " + (field_ok ? "通过" : "失败"));

            //5. 用模拟宿主执行，验证"跑出伤害"
            SimulatedGraphHost host = new SimulatedGraphHost();
            GraphRuntime.ExecutionResult result = GraphRuntime.Execute(parsed, host, "OnPlay");
            Debug.Log("[GraphBuildStress] 执行状态: " + (result.success ? "成功" : "失败:" + result.error)
                      + " | 执行节点数: " + result.visited.Count
                      + " | 模拟HP: " + host.hp + "(应 27) 手牌: " + host.hand + "(应 6)");

            bool hp_ok = host.hp == 27;
            bool hand_ok = host.hand == 6;
            bool all_ok = roundtrip && field_ok && result.success && hp_ok && hand_ok;
            Debug.Log("[GraphBuildStress] 总体结果: " + (all_ok ? "全部通过 ✔" : "存在失败 ✘"));
            EditorUtility.DisplayDialog("卡牌编辑器 P1 校验",
                (all_ok ? "P1 环形闭环校验全部通过 ✔" : "P1 校验存在失败，详见 Console")
                + "\n节点数: " + (parsed != null ? CountNodes(parsed) : 0)
                + "  HP: " + host.hp + "  手牌: " + host.hand, "确定");
        }

        /// <summary>构建 P1 最简示例图</summary>
        public static GraphData BuildSampleGraph()
        {
            GraphData graph = new GraphData();
            graph.name = "P1_Sample";

            GraphNode ev = GraphBuilder.Event("ev_play", "OnPlay", "打出时");
            GraphNode act1 = GraphBuilder.Action("damage_act", "Damage", "对敌造成伤害", 3);
            GraphNode act2 = GraphBuilder.Action("draw_act", "Draw", "抽牌", 1);

            //画布初始坐标（P2 可拖拽），横排错开避免堆叠
            ev.pos = new Vector2Data(40, 260);
            act1.pos = new Vector2Data(520, 220);
            act2.pos = new Vector2Data(1000, 180);

            graph.nodes.Add(ev);
            graph.nodes.Add(act1);
            graph.nodes.Add(act2);

            GraphBuilder.Link(graph, ev, act1);
            GraphBuilder.Link(graph, act1, act2);
            return graph;
        }

        private static int CountNodes(GraphData g)
        {
            return g != null ? g.nodes.Count : 0;
        }
    }
}
#endif