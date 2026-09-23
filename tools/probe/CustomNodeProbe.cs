using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.UI;
using TcgEngine.Workshop;

/// <summary>【临时探针】自定义节点编辑器验收：类型切换 / 端口增删移 / 事件自动生成 XX时·XX后 /
/// 蓝图块引脚随端口更新 / 保存落盘并重载 / 注册进节点库。
/// 用法：建 tools/custom_node_flag.txt → 进 Play 一次 → 写 tools/custom_node_result.tsv → 自动删标记。
/// 注意：会创建一条测试用自定义节点，跑完自动删除（不留脏数据）。
/// </summary>
public static class CustomNodeProbe
{
    private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    private static string FlagPath { get { return Path.Combine(Root, "tools/custom_node_flag.txt"); } }
    private static string OutPath { get { return Path.Combine(Root, "tools/custom_node_result.tsv"); } }

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
        sb.AppendLine("# 自定义节点编辑器验收探针  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        string test_id = null;
        try
        {
            GraphEditorPanel panel = null;
            GraphEditorPanel[] all = UnityEngine.Object.FindObjectsOfType<GraphEditorPanel>(true);
            if (all != null && all.Length > 0) panel = all[0];
            if (panel == null) { Fail("找不到 GraphEditorPanel", "面板不在当前场景"); }
            else
            {
                if (!panel.gameObject.activeInHierarchy) panel.gameObject.SetActive(true);
                test_id = Run(panel);
            }
        }
        catch (Exception e) { Fail("探针异常", e.ToString()); }
        finally
        {
            //清理测试节点（不留脏数据）
            if (!string.IsNullOrEmpty(test_id))
            {
                try
                {
                    CustomNodeIO.Remove(CustomNodeIO.Get(test_id));
                    CustomNodeIO.SaveAll();
                    sb.AppendLine("# 已清理测试节点：" + test_id);
                }
                catch (Exception e) { sb.AppendLine("# 清理失败：" + e.Message); }
            }
            sb.AppendLine("# 汇总：PASS=" + pass + " FAIL=" + fail);
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[自定义节点探针] 完成 PASS=" + pass + " FAIL=" + fail + " → " + OutPath);
            try { File.Delete(FlagPath); } catch { }
            _running = false;
        }
    }

    private static string Run(GraphEditorPanel panel)
    {
        //① 新建"动作节点"（弹框「新增」走的就是 CustomNodeIO.New）
        CustomNodeData n = CustomNodeIO.New(CustomNodeKind.Action);
        CustomNodeIO.SaveAll();
        Ok("① 新建动作节点（CustomNodeIO.New）", n != null && n.Kind == CustomNodeKind.Action, n != null ? n.id : "null");
        if (n == null) return null;

        //② 端口增删移（数据层）
        n.inputs.Clear(); n.outputs.Clear();
        n.inputs.Add(new CustomNodePort("血量", NodeValueType.Int32.ToString()));
        n.inputs.Add(new CustomNodePort("目标", NodeValueType.Card.ToString()));
        n.outputs.Add(new CustomNodePort("结果", NodeValueType.Boolean.ToString()));
        n.MovePort(false, 1, -1);   //"目标"上移到首位
        Ok("② 端口增删+上移（inputs[0]=目标）", n.inputs.Count == 2 && n.inputs[0].name == "目标", Dump(n));

        //③ 打开自定义节点编辑器 → 蓝图块引脚随端口生成
        Call(panel, "OpenCustomNode", n);
        GraphData g0 = n.EnsureGraphs()[0].graph;
        GraphNode block = FindNode(g0, n.ActionId);
        bool has_in = HasPin(block, "in", false), has_out = HasPin(block, "out", true);
        bool has_hp = HasPin(block, "血量", false), has_tgt = HasPin(block, "目标", false), has_res = HasPin(block, "结果", true);
        Ok("③ 动作节点蓝图块：动作流输入/输出 + 自定义端口齐全",
            block != null && has_in && has_out && has_hp && has_tgt && has_res,
            "block=" + (block != null) + " in=" + has_in + " out=" + has_out + " 血量=" + has_hp + " 目标=" + has_tgt + " 结果=" + has_res);
        Ok("③b 动作节点只有 1 张图（无时/后）", n.GraphCount == 1 && n.graphs.Count >= 1, "GraphCount=" + n.GraphCount);

        //④ 加端口 → 蓝图块立即多一个引脚
        int before = block != null ? block.pins.Count : -1;
        Call(panel, "AddCustomPort", false);          //加一个输入端口
        block = FindNode(g0, n.ActionId);
        Ok("④ 加端口后蓝图块引脚 +1", block != null && block.pins.Count == before + 1,
            "before=" + before + " after=" + (block != null ? block.pins.Count : -1));

        //⑤ 切到"事件节点" → 两张图 + XX时/XX后 两个入口（各带动作流输出）
        Call(panel, "CycleCustomKind");               //Action → Function
        Call(panel, "CycleCustomKind");               //Function → Event
        Ok("⑤ 切成事件节点", n.Kind == CustomNodeKind.Event, "kind=" + n.Kind);
        n.EnsureGraphs();
        Ok("⑤b 事件节点 = 2 张图（XX时 / XX后）", n.graphs.Count >= 2 && n.GraphCount == 2, "graphs=" + n.graphs.Count);
        GraphNode when = FindNode(n.graphs[0].graph, n.ActionId + "_when");
        GraphNode after = FindNode(n.graphs[1].graph, n.ActionId + "_after");
        Ok("⑤c XX时/XX后 各自生成入口节点（事件类型 + 动作流输出 + 自定义端口）",
            when != null && after != null
            && when.type == GraphNodeType.Event && after.type == GraphNodeType.Event
            && HasPin(when, "out", true) && HasPin(after, "out", true)
            && HasPin(when, "结果", true) && HasPin(after, "结果", true),
            "when=" + (when != null) + " after=" + (after != null));
        Ok("⑤d 两段各自独立成图（节点不串）",
            when != null && after != null && when.id != after.id
            && !n.graphs[0].graph.nodes.Contains(after) && !n.graphs[1].graph.nodes.Contains(when), "");

        //⑥ 校验：端口名为空 → 报问题
        n.inputs.Add(new CustomNodePort("", NodeValueType.Int32.ToString()));
        List<string> issues = n.Validate();
        Ok("⑥ 校验能抓出空端口名", issues.Count > 0, string.Join("；", issues.ToArray()));
        n.inputs.RemoveAt(n.inputs.Count - 1);

        //⑦ 保存落盘 + 重载（重启编辑器仍能读）
        Call(panel, "SaveCustomNode");
        bool file_ok = File.Exists(CustomNodeIO.NodeFile);
        CustomNodeIO.LoadAll();                        //模拟"重启后加载"
        CustomNodeData reloaded = CustomNodeIO.Get(n.id);
        Ok("⑦ 保存到 custom_nodes.json 且重载可读", file_ok && reloaded != null && reloaded.Kind == CustomNodeKind.Event
            && reloaded.inputs.Count == n.inputs.Count && reloaded.outputs.Count == n.outputs.Count,
            "file=" + file_ok + " reloaded=" + (reloaded != null)
            + (reloaded != null ? (" kind=" + reloaded.Kind + " in=" + reloaded.inputs.Count + " out=" + reloaded.outputs.Count) : ""));

        //⑧ 注册进节点库（其它图可引用调用）：NodePreset 是面板私有嵌套类 → 反射读 action
        IList presets = AllPresetsList();
        bool found_when = false, found_after = false;
        foreach (object po in presets)
        {
            string action = GetFieldString(po, "action");
            if (action == n.ActionId + "_when") found_when = true;
            if (action == n.ActionId + "_after") found_after = true;
        }
        Ok("⑧ 节点库出现该自定义节点（XX时 / XX后 两个可用节点）", found_when && found_after,
            "when=" + found_when + " after=" + found_after + " 库总数=" + presets.Count);
        return n.id;
    }

    // ---------------- 工具 ----------------

    private static IList AllPresetsList()
    {
        MethodInfo mi = typeof(GraphEditorPanel).GetMethod("AllPresets", BindingFlags.NonPublic | BindingFlags.Static);
        object r = mi != null ? mi.Invoke(null, null) : null;
        return r as IList ?? new ArrayList();
    }

    private static string GetFieldString(object target, string name)
    {
        if (target == null) return null;
        FieldInfo fi = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        object v = fi != null ? fi.GetValue(target) : null;
        return v != null ? v.ToString() : null;
    }

    private static GraphNode FindNode(GraphData g, string action)
    {
        if (g == null || g.nodes == null) return null;
        foreach (GraphNode x in g.nodes)
            if (x != null && x.action == action) return x;
        return null;
    }

    private static bool HasPin(GraphNode n, string pin_name, bool output)
    {
        if (n == null || n.pins == null) return false;
        foreach (GraphPin p in n.pins)
            if (p != null && p.name == pin_name && p.is_output == output) return true;
        return false;
    }

    private static string Dump(CustomNodeData n)
    {
        StringBuilder b = new StringBuilder();
        b.Append("in=[");
        foreach (CustomNodePort p in n.inputs) b.Append(p.name).Append(':').Append(p.type).Append(' ');
        b.Append("] out=[");
        foreach (CustomNodePort p in n.outputs) b.Append(p.name).Append(':').Append(p.type).Append(' ');
        b.Append(']');
        return b.ToString();
    }

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

    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (mi == null) throw new Exception("找不到方法: " + method);
        return mi.Invoke(target, args);
    }

    private static object CallStatic(string method, params object[] args)
    {
        MethodInfo mi = typeof(GraphEditorPanel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        if (mi == null) throw new Exception("找不到静态方法: " + method);
        return mi.Invoke(null, args);
    }
}

