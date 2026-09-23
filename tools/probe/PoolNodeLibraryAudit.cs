using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine.Workshop;
using TcgEngine.UI;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 节点库一致性审计（内置卡池 → 编辑器节点库）：
    /// 要求「池里出现的每个节点，玩家/编辑者在节点库里都**能选到**」——
    ///   · NodeDoc 节点：`NodeDocDb` 里有、且 `obsolete=false`、且没被同名节点顶掉、且 `supported=true`（否则库内灰显不可拖入）
    ///   · 项目内节点：`GraphEditorPanel.AllPresets()` 里有（我加的 SendToPileRaw 等都在这里登记）
    /// 另外核对**呈现准确性**：节点标题必须等于库里的节点名（编辑器显示的就是这个名字）。
    /// 触发：建工程根 tools/library_audit_flag.txt → 进 Play 一次 → 写 tools/library_conformance.tsv → 自动删标记。
    /// </summary>
    public static class PoolNodeLibraryAudit
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string PoolPath { get { return Path.Combine(Root, "tools/base_pool_v1.json"); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/library_conformance.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/library_audit_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath))
                return;
            try { Run(); }
            catch (Exception e) { Debug.LogError("[节点库审计] 失败: " + e); }
            try { File.Delete(FlagPath); } catch { }
        }

        private class LibEntry
        {
            public string title;
            public string category;
            public bool hidden;
            public bool supported;
            public bool is_nodedoc;
            public int type;
        }

        private static void Run()
        {
            if (!File.Exists(PoolPath))
            {
                Debug.LogError("[节点库审计] 找不到 " + PoolPath);
                return;
            }
            CardPoolData pool = JsonUtility.FromJson<CardPoolData>(File.ReadAllText(PoolPath, Encoding.UTF8));

            // ---- 编辑器节点库（与 GraphEditorPanel.AllPresets 完全同源）----
            MethodInfo mi = typeof(GraphEditorPanel).GetMethod("AllPresets",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null)
            {
                Debug.LogError("[节点库审计] 反射不到 GraphEditorPanel.AllPresets（签名变了？）");
                return;
            }
            IList presets = (IList)mi.Invoke(null, null);
            Dictionary<string, LibEntry> lib = new Dictionary<string, LibEntry>();
            foreach (object p in presets)
            {
                if (p == null)
                    continue;
                Type t = p.GetType();
                FieldInfo fa = t.GetField("action");
                if (fa == null)
                    continue;
                string action = fa.GetValue(p) as string;
                if (string.IsNullOrEmpty(action))
                    continue;
                LibEntry e = new LibEntry();
                e.title = t.GetField("title") != null ? (string)t.GetField("title").GetValue(p) : "";
                e.category = t.GetField("category") != null ? (string)t.GetField("category").GetValue(p) : "";
                e.hidden = t.GetField("hidden") != null && (bool)t.GetField("hidden").GetValue(p);
                e.supported = t.GetField("supported") == null || (bool)t.GetField("supported").GetValue(p);
                e.is_nodedoc = NodeDocDb.Get(action) != null;
                e.type = t.GetField("type") != null ? (int)t.GetField("type").GetValue(p) : -1;
                //同名多个预设：优先保留"可选"的那个（与编辑器 keep_name 同思路）
                if (lib.ContainsKey(action) && !(lib[action].hidden && !e.hidden))
                    continue;
                lib[action] = e;
            }

            // ---- 逐节点审计（按 action 聚合）----
            Dictionary<string, int> count = new Dictionary<string, int>();
            Dictionary<string, HashSet<string>> titles = new Dictionary<string, HashSet<string>>();
            Dictionary<string, HashSet<string>> cards = new Dictionary<string, HashSet<string>>();
            Dictionary<string, HashSet<int>> types = new Dictionary<string, HashSet<int>>();
            int total_nodes = 0;
            foreach (CardCustomData c in pool.cards)
            {
                if (c == null)
                    continue;
                foreach (CardEffectData ef in c.EnsureEffects())
                {
                    if (ef == null || ef.graph == null || ef.graph.nodes == null)
                        continue;
                    foreach (GraphNode n in ef.graph.nodes)
                    {
                        if (n == null || string.IsNullOrEmpty(n.action))
                            continue;
                        total_nodes++;
                        string a = n.action;
                        if (!count.ContainsKey(a)) { count[a] = 0; titles[a] = new HashSet<string>(); cards[a] = new HashSet<string>(); types[a] = new HashSet<int>(); }
                        count[a]++;
                        titles[a].Add(n.title ?? "");
                        cards[a].Add(c.id);
                        types[a].Add((int)n.type);
                    }
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 内置卡池节点 → 编辑器节点库 一致性审计  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("# 判据：库里必须能选到该节点（NodeDoc 未过时/未被同名顶掉/已接入执行，或项目内预设）；且池里节点标题应等于库里的节点名");
            sb.AppendLine("# 列：action | 次数 | 库内? | 库节点名 | 分类 | 过时/隐藏 | 灰显不可选 | 池里标题 | 判定");

            int violations = 0, title_warn = 0, ok_count = 0;
            List<string> bad_actions = new List<string>();
            foreach (KeyValuePair<string, int> kv in count)
            {
                string a = kv.Key;
                LibEntry le = lib.ContainsKey(a) ? lib[a] : null;
                string order = le != null ? le.title : "";
                string verdict;
                if (le == null)
                {
                    verdict = "★不在节点库（编辑器无法呈现/不可选）";
                }
                else if (le.hidden)
                {
                    verdict = "★库内隐藏（过时或同名被顶掉，不可选）";
                }
                else if (!le.supported)
                {
                    verdict = "★库内灰显（未接入执行，不可拖入）";
                }
                else
                {
                    // 类型一致性
                    bool type_ok = types[a].Contains(le.type) || le.type < 0;
                    verdict = type_ok ? "OK" : ("★节点类型与库不一致（池=" + string.Join("/", ToStr(types[a])) + " 库=" + le.type + "）");
                }
                // 标题呈现：库名 vs 池里的标题（允许「库名：后缀」写法）
                bool title_ok = true;
                if (le != null && !string.IsNullOrEmpty(le.title))
                {
                    foreach (string t in titles[a])
                    {
                        if (t == le.title)
                            continue;
                        if (!string.IsNullOrEmpty(t) && t.StartsWith(le.title + "："))
                            continue;
                        title_ok = false;
                    }
                }
                if (verdict == "OK")
                {
                    if (title_ok) ok_count++;
                    else { title_warn++; verdict = "标题与库名不一致（呈现不准）"; }
                }
                else
                {
                    violations++;
                    bad_actions.Add(a);
                    //★给"过时/灰显"节点列出**候选替身**（同/近名且库里可选），供转换器换 id
                    if (le != null)
                    {
                        List<string> cand = new List<string>();
                        foreach (NodeDocDef d in NodeDocDb.All)
                        {
                            if (d == null || d.define_id == a)
                                continue;
                            if (!NameNear(d.editor_name, le.title))
                                continue;
                            LibEntry ce = lib.ContainsKey(d.define_id) ? lib[d.define_id] : null;
                            string st = ce == null ? "无预设"
                                : (ce.hidden ? "库内隐藏" : (ce.supported ? "可选 ✓" : "灰显未接入"));
                            cand.Add(d.define_id + "「" + d.editor_name + "」(" + st + ")");
                        }
                        if (cand.Count > 0)
                            sb.AppendLine("#   ↑ 候选替身: " + string.Join("、", cand.ToArray()));
                    }
                }

                sb.AppendLine(a + "\t" + kv.Value + "\t" + (le != null ? "是" : "否") + "\t" + order
                    + "\t" + (le != null ? le.category : "") + "\t" + (le != null && le.hidden ? "是" : "否")
                    + "\t" + (le != null && !le.supported ? "是" : "否")
                    + "\t" + string.Join(" / ", ToStr(titles[a])) + "\t" + verdict);
            }
            sb.AppendLine("# 汇总：不同 action=" + count.Count + "｜节点实例=" + total_nodes
                + "｜可选且呈现正确=" + ok_count + "｜★违规定位=" + violations + "｜标题不一致=" + title_warn);
            sb.AppendLine("# 违规 action：" + (bad_actions.Count == 0 ? "无 ✓" : string.Join("、", bad_actions.ToArray())));
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[节点库审计] 完成：action=" + count.Count + " 节点实例=" + total_nodes
                + " 违规=" + violations + " 标题不一致=" + title_warn + " → " + OutPath);
        }

        /// <summary>名字相近（互相包含，或前两字相同）——用于给隐藏节点找候选替身</summary>
        private static bool NameNear(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            if (a.Contains(b) || b.Contains(a))
                return true;
            return a.Length >= 2 && b.Length >= 2 && a.Substring(0, 2) == b.Substring(0, 2);
        }

        private static List<string> ToStr(HashSet<string> set)
        {
            List<string> l = new List<string>(set);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        private static List<string> ToStr(HashSet<int> set)
        {
            List<string> l = new List<string>();
            foreach (int i in set)
                l.Add(i.ToString());
            l.Sort(StringComparer.Ordinal);
            return l;
        }
    }
}
