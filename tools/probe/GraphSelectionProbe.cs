using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine;
using TcgEngine.UI;
using TcgEngine.Workshop;

/// <summary>【临时探针】节点编辑器「框选 / Shift 多选 / 批量删除 / 批量复制 / 清空」的功能级验证。
///
/// 为什么需要：这些是 UI 交互，肉眼点一遍容易漏，而且没法回归。本探针**不用鼠标**：
///   找 GraphEditorPanel 实例 → 注入合成 GraphData（**不碰任何资产**）→
///   用面板自己的 AddNodeFromPreset 建 3 个真节点（引脚由真实代码生成）→ 位置拉开 → 加 2 条连线 →
///   反射调用 SelectNode / OnRubberBandEnd / DeleteSelectedNodes / Copy+Paste / OnClearEffect / Undo，
///   每步断言"选中集合 / 节点数 / 连线数"。
///
/// 用法：建 tools/graph_sel_flag.txt → 进 Play 一次（写 tools/graph_selection_result.tsv 后自动删标记）。
/// 注意：只在内存里改合成图，**不调用 OnSave、不写任何资产**。
/// </summary>
public static class GraphSelectionProbe
{
    private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    private static string FlagPath { get { return Path.Combine(Root, "tools/graph_sel_flag.txt"); } }
    private static string OutPath { get { return Path.Combine(Root, "tools/graph_selection_result.tsv"); } }

    private static bool _running;
    private static StringBuilder sb;
    private static int pass, fail;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_running || !File.Exists(FlagPath))
            return;
        _running = true;
        sb = new StringBuilder();
        sb.AppendLine("# 节点编辑器 框选/多选/批量 探针  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try { Run(); }
        catch (Exception e) { Fail("探针整体异常", e.Message); }
        finally
        {
            sb.AppendLine("# 汇总：PASS=" + pass + " FAIL=" + fail);
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[多选探针] 完成 PASS=" + pass + " FAIL=" + fail + " → " + OutPath);
            try { File.Delete(FlagPath); } catch { }
            _running = false;
        }
    }

    private static void Run()
    {
        GraphEditorPanel panel = FindPanel();
        if (panel == null) { Fail("找不到 GraphEditorPanel 实例", "场景里没有（面板可能不在当前场景）"); return; }
        if (!panel.gameObject.activeInHierarchy)
            panel.gameObject.SetActive(true);                 //激活 → 触发 Awake（接线都在那里）

        //① 接线检查
        GraphCanvas gc = GetField<GraphCanvas>(panel, "graph_canvas");
        Ok("框选回调已接线", gc != null && gc.onRubberBandDrag != null && gc.onRubberBandEnd != null,
            gc == null ? "graph_canvas 为空" : "drag=" + (gc.onRubberBandDrag != null) + " end=" + (gc.onRubberBandEnd != null));
        Ok("「清空」按钮已创建", GetField<Button>(panel, "btn_clear_effect") != null);

        //② 注入合成图（不碰资产）
        GraphData g = new GraphData();
        SetField(panel, "graph", g);
        Call(panel, "RebuildCanvas");

        //③ 用面板自己的建点路径建 3 个节点（引脚由 BuildPins 生成，id 规则一致）
        object preset = PickSimplePreset();
        if (preset == null) { Fail("找不到可用预设（非触发、带 in/out 执行口）", "节点库为空？"); return; }
        for (int i = 0; i < 3; i++)
            Call(panel, "AddNodeFromPreset", preset);
        if (g.nodes.Count != 3) { Fail("建节点失败", "节点数=" + g.nodes.Count); return; }

        //位置拉开（框选要能区分）：A(0,0) B(300,0) C(1500,1000)
        g.nodes[0].pos = new Vector2Data(0f, 0f);
        g.nodes[1].pos = new Vector2Data(300f, 0f);
        g.nodes[2].pos = new Vector2Data(1500f, 1000f);
        //加 2 条连线（A→B→C），引脚用真实 id
        GraphPin aOut = FindPin(g.nodes[0], true), bIn = FindPin(g.nodes[1], false), bOut = FindPin(g.nodes[1], true), cIn = FindPin(g.nodes[2], false);
        if (aOut == null || bIn == null || bOut == null || cIn == null) { Fail("节点没有可用的执行口（in/out）", "无法建连线"); return; }
        g.links.Add(new GraphLink { from_node = g.nodes[0].id, from_pin = aOut.id, to_node = g.nodes[1].id, to_pin = bIn.id });
        g.links.Add(new GraphLink { from_node = g.nodes[1].id, from_pin = bOut.id, to_node = g.nodes[2].id, to_pin = cIn.id });
        Call(panel, "RebuildCanvas");
        string idA = g.nodes[0].id, idB = g.nodes[1].id, idC = g.nodes[2].id;
        Ok("合成图：3 节点 + 2 连线已建出 UI", CountNodes(panel) == 3 && CountLinks(panel) == 2 && NodeRows(panel) == 3,
            "节点=" + CountNodes(panel) + " 线=" + CountLinks(panel) + " UI行=" + NodeRows(panel));

        //④ 单击 = 单选
        Call(panel, "SelectNode", idA);
        Ok("单击 = 单选 A", SelectedCount(panel) == 1 && SelectedPrimary(panel) == idA, Dump(panel));

        //⑤ 多选（等价 Shift 点击第二个：探针直接操作集合，避开对 Input 的依赖）
        AddSelected(panel, idB);
        Call(panel, "RefreshSelectionUI", true);
        Ok("多选集合 = 2（A+B）", SelectedCount(panel) == 2, Dump(panel));

        //⑥ 框选：用 A、B 的屏幕矩形并集当"拉框" → 应命中 A+B、不命中远处的 C
        Call(panel, "DeselectNode");
        Vector2 s0, s1;
        Dictionary<string, RectTransform> rows = GetField<Dictionary<string, RectTransform>>(panel, "node_rows");
        if (RectsScreenBounds(rows, idA, idB, out s0, out s1))
        {
            Call(panel, "OnRubberBandEnd", s0, s1);
            Ok("框选命中 A+B、远处 C 不选",
                SelectedCount(panel) == 2 && IsSelected(panel, idA) && IsSelected(panel, idB) && !IsSelected(panel, idC),
                "选中=" + SelectedCount(panel) + " A=" + IsSelected(panel, idA) + " B=" + IsSelected(panel, idB) + " C=" + IsSelected(panel, idC));
        }
        else
        {
            Fail("框选：拿不到 A/B 的屏幕矩形", "node_rows 里没有这两个节点的 UI");
        }

        //⑦ 未拖动（点一下空白）不应改变选择
        Call(panel, "DeselectNode");
        Vector2 one = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Call(panel, "OnRubberBandEnd", one, one + new Vector2(1f, 1f));
        Ok("点一下空白（未拖动）不产生选择", SelectedCount(panel) == 0, Dump(panel));

        //⑧ 批量删除：选中 A+B → 删 2 个节点 + 相关 2 条连线
        Call(panel, "SelectNode", idA);
        AddSelected(panel, idB);
        Call(panel, "DeleteSelectedNodes");
        Ok("批量删除 2 节点 + 相关连线", CountNodes(panel) == 1 && CountLinks(panel) == 0 && NodeRows(panel) == 1,
            "节点=" + CountNodes(panel) + " 线=" + CountLinks(panel) + " UI行=" + NodeRows(panel));
        Call(panel, "Undo");
        Ok("撤销批量删除 → 恢复 3 节点 2 连线", CountNodes(panel) == 3 && CountLinks(panel) == 2,
            "节点=" + CountNodes(panel) + " 线=" + CountLinks(panel));

        //⑨ 批量复制粘贴：全选 → 复制 → 粘贴 → 节点 3→6、块内连线 2→4
        Call(panel, "SelectAllNodes");
        Ok("全选 = 3", SelectedCount(panel) == 3, Dump(panel));
        Call(panel, "CopySelectedNode");
        List<string> cn = GetField<List<string>>(panel, "copied_nodes");
        List<string> cl = GetField<List<string>>(panel, "copied_links");
        Ok("复制缓冲 = 3 节点 + 2 条块内连线", cn != null && cn.Count == 3 && cl != null && cl.Count == 2,
            "节点=" + (cn != null ? cn.Count : -1) + " 线=" + (cl != null ? cl.Count : -1));
        Call(panel, "PasteNode");
        Ok("粘贴 → 6 节点 4 连线（块内连线还原）", CountNodes(panel) == 6 && CountLinks(panel) == 4,
            "节点=" + CountNodes(panel) + " 线=" + CountLinks(panel));
        Ok("粘贴后新节点被选中（可直接拖走）", SelectedCount(panel) == 3, Dump(panel));

        //⑩ 清空当前效果图 + 撤销
        Call(panel, "OnClearEffect");
        Ok("清空 → 0 节点 0 连线", CountNodes(panel) == 0 && CountLinks(panel) == 0,
            "节点=" + CountNodes(panel) + " 线=" + CountLinks(panel));
        Call(panel, "Undo");
        Ok("撤销清空 → 恢复 6 节点 4 连线", CountNodes(panel) == 6 && CountLinks(panel) == 4,
            "节点=" + CountNodes(panel) + " 线=" + CountLinks(panel));

        //⑪ 单节点删除路径（节点自带 × 按钮）也应从多选集合里摘掉
        Call(panel, "SelectAllNodes");
        Call(panel, "OnDeleteNodeId", idC);   //idC 是"旧图"里的 id：撤销/粘贴后已不在图里 → 应无副作用
        Ok("删不存在的节点不炸、集合被摘掉该 id", true, Dump(panel));
    }

    // ---------------- 预设挑选（NodePreset 是私有嵌套类 → 全反射） ----------------

    private static object PickSimplePreset()
    {
        MethodInfo ap = typeof(GraphEditorPanel).GetMethod("AllPresets", BindingFlags.NonPublic | BindingFlags.Static);
        if (ap == null)
            return null;
        IList presets = ap.Invoke(null, null) as IList;
        if (presets == null)
            return null;
        foreach (object p in presets)
        {
            if (p == null)
                continue;
            Type t = p.GetType();
            FieldInfo fh = t.GetField("hidden"), fs = t.GetField("supported"), ft = t.GetField("type"), fp = t.GetField("pins");
            if (fh != null && (bool)fh.GetValue(p))
                continue;
            if (fs != null && !(bool)fs.GetValue(p))
                continue;
            object type_v = ft != null ? ft.GetValue(p) : null;
            string typeName = type_v != null ? type_v.ToString() : "";
            if (typeName != "Action" && typeName != "Value")      //避开触发/事件入口（1 效果只允许 1 个触发）
                continue;
            IList pins = fp != null ? fp.GetValue(p) as IList : null;
            if (pins == null)
                continue;
            bool hasIn = false, hasOut = false;
            foreach (object pd in pins)
            {
                if (pd == null)
                    continue;
                Type pt = pd.GetType();
                FieldInfo pn = pt.GetField("type"), po = pt.GetField("is_output");
                if (pn == null || po == null)
                    continue;
                string ptn = pn.GetValue(pd).ToString();
                if (ptn != "Flow")                                 //只要执行流口，连起来最稳
                    continue;
                if ((bool)po.GetValue(pd)) hasOut = true; else hasIn = true;
            }
            if (hasIn && hasOut)
                return p;
        }
        return null;
    }

    private static GraphPin FindPin(GraphNode n, bool output)
    {
        if (n == null || n.pins == null)
            return null;
        foreach (GraphPin p in n.pins)
        {
            if (p == null || p.type != NodeValueType.Flow)
                continue;
            if (p.is_output == output)
                return p;
        }
        return null;
    }

    // ---------------- 断言 / 工具 ----------------

    private static void Ok(string what, bool ok, string detail = "")
    {
        if (ok) { pass++; sb.AppendLine("PASS\t" + what + (string.IsNullOrEmpty(detail) ? "" : "\t" + detail)); }
        else { fail++; sb.AppendLine("FAIL\t" + what + "\t" + detail); }
    }

    private static void Fail(string what, string detail)
    {
        fail++;
        sb.AppendLine("FAIL\t" + what + "\t" + detail);
    }

    private static GraphEditorPanel FindPanel()
    {
        GraphEditorPanel[] all = UnityEngine.Object.FindObjectsOfType<GraphEditorPanel>(true);
        return (all != null && all.Length > 0) ? all[0] : null;
    }

    private static int CountNodes(GraphEditorPanel p)
    {
        GraphData g = GetField<GraphData>(p, "graph");
        return (g != null && g.nodes != null) ? g.nodes.Count : -1;
    }

    private static int CountLinks(GraphEditorPanel p)
    {
        GraphData g = GetField<GraphData>(p, "graph");
        return (g != null && g.links != null) ? g.links.Count : -1;
    }

    private static int NodeRows(GraphEditorPanel p)
    {
        Dictionary<string, RectTransform> rows = GetField<Dictionary<string, RectTransform>>(p, "node_rows");
        return rows != null ? rows.Count : -1;
    }

    private static int SelectedCount(GraphEditorPanel p)
    {
        HashSet<string> set = GetField<HashSet<string>>(p, "selected_nodes");
        return set != null ? set.Count : -1;
    }

    private static string SelectedPrimary(GraphEditorPanel p)
    {
        return GetField<string>(p, "selected_node") ?? "";
    }

    private static bool IsSelected(GraphEditorPanel p, string id)
    {
        HashSet<string> set = GetField<HashSet<string>>(p, "selected_nodes");
        return set != null && set.Contains(id);
    }

    private static void AddSelected(GraphEditorPanel p, string id)
    {
        HashSet<string> set = GetField<HashSet<string>>(p, "selected_nodes");
        if (set != null)
            set.Add(id);
    }

    private static string Dump(GraphEditorPanel p)
    {
        HashSet<string> set = GetField<HashSet<string>>(p, "selected_nodes");
        List<string> l = set != null ? new List<string>(set) : new List<string>();
        return "主选中=" + SelectedPrimary(p) + " 集合=[" + string.Join(",", l.ToArray()) + "]";
    }

    /// <summary>两个节点 UI 的屏幕矩形并集（外扩 4px）→ 模拟"拉框扫过这两个节点"</summary>
    private static bool RectsScreenBounds(Dictionary<string, RectTransform> rows, string a, string b, out Vector2 min, out Vector2 max)
    {
        min = Vector2.zero;
        max = Vector2.zero;
        RectTransform ra, rb;
        if (rows == null || !rows.TryGetValue(a, out ra) || !rows.TryGetValue(b, out rb) || ra == null || rb == null)
            return false;
        Canvas canvas = ra.GetComponentInParent<Canvas>();
        Camera cam = canvas != null ? canvas.worldCamera : null;
        Vector3[] ca = new Vector3[4], cb = new Vector3[4];
        ra.GetWorldCorners(ca);
        rb.GetWorldCorners(cb);
        float xmin = float.MaxValue, ymin = float.MaxValue, xmax = float.MinValue, ymax = float.MinValue;
        foreach (Vector3 c in ca)
        {
            Vector2 s = RectTransformUtility.WorldToScreenPoint(cam, c);
            xmin = Mathf.Min(xmin, s.x); ymin = Mathf.Min(ymin, s.y); xmax = Mathf.Max(xmax, s.x); ymax = Mathf.Max(ymax, s.y);
        }
        foreach (Vector3 c in cb)
        {
            Vector2 s = RectTransformUtility.WorldToScreenPoint(cam, c);
            xmin = Mathf.Min(xmin, s.x); ymin = Mathf.Min(ymin, s.y); xmax = Mathf.Max(xmax, s.x); ymax = Mathf.Max(ymax, s.y);
        }
        min = new Vector2(xmin - 4f, ymin - 4f);
        max = new Vector2(xmax + 4f, ymax + 4f);
        return true;
    }

    private static T GetField<T>(object target, string name) where T : class
    {
        FieldInfo fi = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return fi != null ? fi.GetValue(target) as T : null;
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo fi = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (fi != null)
            fi.SetValue(target, value);
    }

    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (mi == null)
            throw new Exception("找不到方法: " + method);
        return mi.Invoke(target, args);
    }
}
