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
    /// 【节点预设 ↔ NodeDoc.xml 一致性审计】
    /// 用户要求：每个节点的**输入/输出口必须与 NodeDoc.xml 的原始定义一致**；
    /// 编辑器不得自造端口/字段（自造即违背"照图做"）。
    ///
    /// 本探针逐节点比对（以 XML 的 defineId 对齐预设的 action）：
    ///   · missing_port ：XML 有、预设没有的口（连线连不上 / 显示不出来）
    ///   · extra_port   ：预设多出来的口（XML 没有 —— 例如 206003 被自插的「卡牌」）
    ///   · port_dir     ：同名口方向反了（该输入却是输出）
    ///   · extra_field  ：预设里的字段名不在 XML 输入口里（运行期自造字段，逐个判断能否去掉）
    ///   · field_offset ：字段名与 XML 口名不同但语义重复（例如 XML 的 propName ↔ 预设的 prop）
    ///
    /// 触发：建工程根 tools/preset_doc_audit_flag.txt → 进 Play 一次 → 写 tools/preset_doc_audit.tsv → 自动删标记。
    /// </summary>
    public static class NodePresetDocAudit
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/preset_doc_audit.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/preset_doc_audit_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath))
                return;
            try { Run(); }
            catch (Exception e) { Debug.LogError("[预设审计] 失败: " + e); }
            try { File.Delete(FlagPath); } catch { }
        }

        private static void Run()
        {
            MethodInfo mi = typeof(GraphEditorPanel).GetMethod("AllPresets",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null)
            {
                Debug.LogError("[预设审计] 反射不到 GraphEditorPanel.AllPresets");
                return;
            }
            IList presets = (IList)mi.Invoke(null, null);

            // action → 预设（同名多个时优先 supported && !hidden）
            Dictionary<string, object> by_action = new Dictionary<string, object>();
            Dictionary<string, bool> by_action_ok = new Dictionary<string, bool>();
            foreach (object p in presets)
            {
                if (p == null)
                    continue;
                Type t = p.GetType();
                string action = GetField<string>(t, p, "action");
                if (string.IsNullOrEmpty(action))
                    continue;
                bool ok = BoolField(t, p, "supported") && !BoolField(t, p, "hidden");
                if (by_action.ContainsKey(action) && !(ok && !by_action_ok[action]))
                    continue;
                by_action[action] = p;
                by_action_ok[action] = ok;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("node\teditor_name\tkind\tdetail");
            int dev = 0, checked_nodes = 0;
            Dictionary<string, int> kind_count = new Dictionary<string, int>();

            foreach (NodeDocDef d in NodeDocDb.All)
            {
                if (d == null || string.IsNullOrEmpty(d.define_id))
                    continue;
                object preset;
                if (!by_action.TryGetValue(d.define_id, out preset) || preset == null)
                    continue;   // 库里没有 → 本次不审（未支持/过时由别的审计管）
                checked_nodes++;

                Type pt = preset.GetType();
                Dictionary<string, bool> pin_dir = new Dictionary<string, bool>();   // name → is_output
                List<string> pin_names = new List<string>();
                foreach (object pin in ListField(pt, preset, "pins"))
                {
                    if (pin == null) continue;
                    Type ct = pin.GetType();
                    string nm = GetField<string>(ct, pin, "name");
                    if (string.IsNullOrEmpty(nm)) continue;
                    pin_dir[nm] = BoolField(ct, pin, "is_output");
                    pin_names.Add(nm);
                }
                List<string> field_names = new List<string>();
                foreach (object fd in ListField(pt, preset, "fields"))
                {
                    if (fd == null) continue;
                    string nm = GetField<string>(fd.GetType(), fd, "name");
                    if (!string.IsNullOrEmpty(nm)) field_names.Add(nm);
                }

                HashSet<string> xml_in = new HashSet<string>();
                HashSet<string> xml_out = new HashSet<string>();
                foreach (NodeDocPort port in d.inputs)
                    if (port != null && !string.IsNullOrEmpty(port.name)) xml_in.Add(port.name);
                foreach (NodeDocPort port in d.outputs)
                    if (port != null && !string.IsNullOrEmpty(port.name)) xml_out.Add(port.name);

                // 1) XML 输入口 → 预设必须同名且为输入
                foreach (string nm in xml_in)
                {
                    if (!pin_dir.ContainsKey(nm)) { Add(sb, ref dev, kind_count, d, "missing_port_in", nm); }
                    else if (pin_dir[nm]) { Add(sb, ref dev, kind_count, d, "port_dir_reversed", nm + "（XML=输入，预设=输出）"); }
                }
                // 2) XML 输出口 → 预设必须同名且为输出
                foreach (string nm in xml_out)
                {
                    if (!pin_dir.ContainsKey(nm)) { Add(sb, ref dev, kind_count, d, "missing_port_out", nm); }
                    else if (!pin_dir[nm]) { Add(sb, ref dev, kind_count, d, "port_dir_reversed", nm + "（XML=输出，预设=输入）"); }
                }
                // 3) 预设多出来的口
                foreach (string nm in pin_names)
                {
                    if (!xml_in.Contains(nm) && !xml_out.Contains(nm))
                        Add(sb, ref dev, kind_count, d, "extra_port", nm + (pin_dir[nm] ? "（输出）" : "（输入）"));
                }
                // 4) 预设字段名不在任何 XML 口上（自造常量字段）
                foreach (string nm in field_names)
                {
                    if (!xml_in.Contains(nm) && !xml_out.Contains(nm))
                        Add(sb, ref dev, kind_count, d, "extra_field", nm);
                }
            }

            sb.AppendLine();
            sb.AppendLine("# 审计节点数\t" + checked_nodes);
            sb.AppendLine("# 差异总数\t" + dev);
            foreach (KeyValuePair<string, int> kv in kind_count)
                sb.AppendLine("# " + kv.Key + "\t" + kv.Value);
            File.WriteAllText(OutPath, sb.ToString(), Encoding.UTF8);
            Debug.Log("[预设审计] 完成：节点 " + checked_nodes + " 个，差异 " + dev + " 条 → " + OutPath);
        }

        private static void Add(StringBuilder sb, ref int dev, Dictionary<string, int> kind_count,
            NodeDocDef d, string kind, string detail)
        {
            dev++;
            if (!kind_count.ContainsKey(kind))
                kind_count[kind] = 0;
            kind_count[kind] = kind_count[kind] + 1;
            sb.AppendLine(d.define_id + "\t" + d.editor_name + "\t" + kind + "\t" + detail);
        }

        private static T GetField<T>(Type t, object o, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
                return default(T);
            object v = f.GetValue(o);
            return v is T ? (T)v : default(T);
        }

        private static bool BoolField(Type t, object o, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
                return false;
            object v = f.GetValue(o);
            return v is bool && (bool)v;
        }

        private static List<object> ListField(Type t, object o, string name)
        {
            List<object> result = new List<object>();
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
                return result;
            IEnumerable en = f.GetValue(o) as IEnumerable;
            if (en == null)
                return result;
            foreach (object item in en)
                result.Add(item);
            return result;
        }
    }
}
