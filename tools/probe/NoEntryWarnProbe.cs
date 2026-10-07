using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【无入口图告警验证】"有节点但没有任何 Event 入口节点"的图会被静默编译成 0 能力
    /// （卡打出去毫无反应），本轮给 CardPoolIO.CompileOneGraphAbilities 加了明确告警。
    /// 本探针用 Application.logMessageReceived 捕获告警，做**真断言**：
    /// ① 合成夹具（Action/Value 节点、无 Event）→ 编译出 0 能力 且 打出「没有任何入口」告警；
    /// ② 真实卡池 sample_pool.json 的 custom_JJw8BRifSr8「幽幽柚子」→ 同样 0 能力 + 告警；
    /// ③ 负面对照：有 Event 入口的图 → 不应该打出该告警（否则会变成噪音）。
    /// 触发：建 tools/noentry_flag.txt → 进 Play → 写 tools/noentry_result.tsv → 自动删标记。
    /// </summary>
    public class NoEntryWarnProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/noentry_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/noentry_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("NoEntryWarnProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<NoEntryWarnProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath)) { done = false; frames = 0; sb.Clear(); }
                return;
            }
            frames++;
            if (frames < 120)
                return;
            done = true;
            try { Run(); }
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e.Message); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[无入口告警] 验证完成 → " + OutPath);
        }

        private static GraphNode Node(GraphData g, string id, GraphNodeType type, string action, string title, params string[] fp)
        {
            var n = new GraphNode();
            n.id = id; n.type = type; n.action = action; n.title = title;
            n.pins = new List<GraphPin>();
            n.fields = new List<FieldCustomData>();
            for (int i = 0; i + 1 < fp.Length; i += 2)
                n.fields.Add(new FieldCustomData { name = fp[i], value = fp[i + 1] });
            foreach (NodeDocDef d in NodeDocDb.All)
                if (d != null && d.define_id == action) { n.category = string.IsNullOrEmpty(d.category) ? "其他" : d.category; break; }
            if (string.IsNullOrEmpty(n.category)) n.category = "其他";
            g.nodes.Add(n);
            return n;
        }

        private static void Pin(GraphNode n, string name, NodeValueType type, bool output)
        {
            n.pins.Add(new GraphPin { id = n.id + "_" + name, name = name, display_name = name, is_output = output, type = type });
        }

        /// <summary>捕获一次编译过程的告警：返回 (是否打出"没有任何入口"告警, 全部告警拼接)</summary>
        private static string CompileAndCatch(CardCustomData dto, out bool warned, out int abilityCount)
        {
            List<string> logs = new List<string>();
            Application.LogCallback cb = (cond, st, t) => { if (t == LogType.Warning || t == LogType.Error) logs.Add(cond); };
            Application.logMessageReceived += cb;
            abilityCount = 0;
            try
            {
                CardData built = CardPoolIO.BuildCardData(dto);
                abilityCount = built != null && built.abilities != null ? built.abilities.Length : 0;
            }
            finally { Application.logMessageReceived -= cb; }
            warned = logs.Exists(l => l != null && l.Contains("没有任何入口"));
            return string.Join(" ｜ ", logs.FindAll(l => l != null && l.Contains("[规则图]")));
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            // ---- ① 合成夹具：Action + Value，无 Event ----
            GraphData g1 = new GraphData { name = "探针无入口图", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode d1 = Node(g1, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "2");
            Pin(d1, "in", NodeValueType.Flow, false);
            Pin(d1, "out", NodeValueType.Flow, true);
            Pin(d1, "value", NodeValueType.Int32, false);
            CardCustomData c1 = new CardCustomData();
            c1.id = "probe_noentry_card";
            c1.title = "探针无入口卡";
            c1.type = "Character";
            c1.effects = new List<CardEffectData> { new CardEffectData { name = "效果1", graph = g1 } };

            bool warned1; int ab1;
            string detail1 = CompileAndCatch(c1, out warned1, out ab1);
            sb.AppendLine((warned1 && ab1 == 0 ? "PASS" : "FAIL")
                + "\t① 有节点无入口 → 0 能力 且 打出告警\t能力数=" + ab1 + " 告警=" + warned1
                + (detail1.Length > 0 ? "｜" + detail1 : ""));

            // ---- ② 真实卡池：sample_pool.json 的 custom_JJw8BRifSr8 ----
            string poolFile = Path.Combine(Application.persistentDataPath, "Workshop", "sample_pool.json");
            if (!File.Exists(poolFile))
            {
                sb.AppendLine("SKIP\t② 真实卡池样本\t找不到 " + poolFile);
            }
            else
            {
                CardCustomData real = null;
                try
                {
                    CardPoolData pool = JsonUtility.FromJson<CardPoolData>(File.ReadAllText(poolFile, Encoding.UTF8));
                    if (pool != null && pool.cards != null)
                        foreach (CardCustomData d in pool.cards)
                            if (d != null && d.id == "custom_JJw8BRifSr8") { real = d; break; }
                }
                catch (Exception e) { sb.AppendLine("FAIL\t② 真实卡池样本\t解析异常: " + e.Message); }
                if (real == null)
                {
                    sb.AppendLine("SKIP\t② 真实卡池样本\t池里没有 custom_JJw8BRifSr8（可能已被改名/删除）");
                }
                else
                {
                    bool warned2; int ab2;
                    string detail2 = CompileAndCatch(real, out warned2, out ab2);
                    sb.AppendLine((warned2 && ab2 == 0 ? "PASS" : "FAIL")
                        + "\t② 幽幽柚子（真实卡池）0 能力 且 打出告警\t能力数=" + ab2 + " 告警=" + warned2
                        + (detail2.Length > 0 ? "｜" + detail2 : ""));
                }
            }

            // ---- ③ 负面对照：有 Event 入口 → 不应打该告警 ----
            GraphData g3 = new GraphData { name = "探针有入口图", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode ev3 = Node(g3, "ev", GraphNodeType.Event, "ActivateEffect", "主动效果入口");
            Pin(ev3, "out", NodeValueType.Flow, true);
            GraphNode d3 = Node(g3, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "2");
            Pin(d3, "in", NodeValueType.Flow, false);
            Pin(d3, "out", NodeValueType.Flow, true);
            Pin(d3, "value", NodeValueType.Int32, false);
            g3.links.Add(new GraphLink { from_node = ev3.id, from_pin = ev3.id + "_out", to_node = d3.id, to_pin = d3.id + "_in" });
            CardCustomData c3 = new CardCustomData();
            c3.id = "probe_entry_ok_card";
            c3.title = "探针有入口卡";
            c3.type = "Spell";
            c3.effects = new List<CardEffectData> { new CardEffectData { name = "效果1", graph = g3 } };

            bool warned3; int ab3;
            CompileAndCatch(c3, out warned3, out ab3);
            sb.AppendLine((!warned3 && ab3 > 0 ? "PASS" : "FAIL")
                + "\t③ 负面对照：有入口 → 编译出能力 且 不误报\t能力数=" + ab3 + " 误报告警=" + warned3);
        }
    }
}
