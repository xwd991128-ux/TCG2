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

/// <summary>【临时探针】自定义**事件**节点运行时验收：
///   ① 事件定义的两张入口节点 action 按「监听事件」归一（时=OnBeforeX / 后=OnAfterX）→ 才能被事件广播命中
///   ② 未配置监听事件时退回 custom_&lt;id&gt;_when/_after（向后兼容）
///   ③ 广播入口 RunCustomEventDefs 空参/无 logic 时不炸（异常隔离的底线）
///   ④ 节点库里「事件」入口候选非空（编辑器"监听事件"轮换按钮有得选）
/// 用法：建 tools/custom_event_flag.txt → 进 Play 一次 → 写 tools/custom_event_result.tsv → 自动删标记 + 清理测试节点。
/// </summary>
public static class CustomNodeEventProbe
{
    private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    private static string FlagPath { get { return Path.Combine(Root, "tools/custom_event_flag.txt"); } }
    private static string OutPath { get { return Path.Combine(Root, "tools/custom_event_result.tsv"); } }

    private static bool _running;
    private static StringBuilder sb;
    private static int pass, fail;
    private static string created_id;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_running || !File.Exists(FlagPath))
            return;
        _running = true;
        sb = new StringBuilder();
        sb.AppendLine("# 自定义事件节点探针  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try { Run(); }
        catch (Exception e) { Fail("探针异常", e.Message); }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(created_id))
                {
                    CustomNodeIO.Remove(CustomNodeIO.Get(created_id));
                    CustomNodeIO.SaveAll();
                    sb.AppendLine("# 已清理测试节点：" + created_id);
                }
            }
            catch (Exception e) { sb.AppendLine("# 清理失败：" + e.Message); }
            sb.AppendLine("# 汇总：PASS=" + pass + " FAIL=" + fail);
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[自定义事件探针] 完成 PASS=" + pass + " FAIL=" + fail + " → " + OutPath);
            try { File.Delete(FlagPath); } catch { }
            _running = false;
        }
    }

    private static void Run()
    {
        GraphEditorPanel panel = null;
        GraphEditorPanel[] all = UnityEngine.Object.FindObjectsOfType<GraphEditorPanel>(true);
        if (all != null && all.Length > 0) panel = all[0];
        if (panel == null) { Fail("找不到 GraphEditorPanel", "面板不在当前场景"); return; }
        if (!panel.gameObject.activeInHierarchy) panel.gameObject.SetActive(true);

        //① 事件定义 + 监听事件 = OnAfterDamage
        CustomNodeData def = CustomNodeIO.New(CustomNodeKind.Event);
        created_id = def.id;
        def.listen_action = "OnAfterDamage";
        def.EnsureGraphs();
        Call(panel, "SyncCustomNodeBlueprint", def);
        GraphNode when = FindNode(def.graphs[0].graph, "OnBeforeDamage");
        GraphNode after = FindNode(def.graphs[1].graph, "OnAfterDamage");
        Ok("① 事件入口 action 按监听事件归一（时=OnBeforeDamage / 后=OnAfterDamage）",
            when != null && after != null,
            "when=" + (when != null ? when.action : "无") + " after=" + (after != null ? after.action : "无")
            + "｜定义里 listen=" + def.listen_action + " base=" + def.ListenBase + " after=" + def.ListenAfter);
        Ok("①b 两张入口都是事件类型且带动作流输出",
            when != null && after != null && when.type == GraphNodeType.Event && after.type == GraphNodeType.Event
            && HasPin(when, "out", true) && HasPin(after, "out", true), "");

        //② 未配置监听事件 → 退回 custom_<id>_when/_after（向后兼容）
        string keep = def.listen_action;
        def.listen_action = "";
        Ok("② 未配置监听事件时退回 custom_id_when/_after", def.EventEntryAction(0) == def.ActionId + "_when"
            && def.EventEntryAction(1) == def.ActionId + "_after",
            def.EventEntryAction(0) + " / " + def.EventEntryAction(1));
        def.listen_action = keep;

        //③ 广播入口空参/无 logic 不炸（异常隔离底线）
        bool safe = true;
        string err = "";
        try
        {
            object r1 = CallStatic("RunCustomEventDefs", new object[] { null, null });
            object r2 = CallStatic("RunCustomEventDefs", new object[] { null, NewCtx("OnAfterDamage") });
            safe = (r1 is int) && (int)r1 == 0 && (r2 is int) && (int)r2 == 0;
            err = "null/空上下文返回=" + r1 + "/" + r2;
        }
        catch (Exception e) { safe = false; err = e.Message; }
        Ok("③ 广播入口空参/无 logic 安全返回 0（不炸）", safe, err);

        //④ 节点库里「事件」入口候选非空（编辑器"监听事件"轮换按钮的数据源）
        int cnt = 0;
        IList presets = AllPresets();
        foreach (object p in presets)
        {
            if (GetFieldString(p, "action") != null
                && GetFieldString(p, "action").StartsWith("On")
                && GetFieldString(p, "category") == "事件")
                cnt++;
        }
        Ok("④ 节点库「事件」入口候选非空（可轮换选择监听事件）", cnt > 0, "候选数=" + cnt);
    }

    private static GraphEventContext NewCtx(string action)
    {
        GraphEventContext ctx = new GraphEventContext();
        ctx.action = action;
        ctx.phase = GraphEventPhase.After;
        return ctx;
    }

    // ---------------- 工具 ----------------

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

    private static IList AllPresets()
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

    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (mi == null) throw new Exception("找不到方法: " + method);
        return mi.Invoke(target, args);
    }

    private static object CallStatic(string name, object[] args)
    {
        MethodInfo mi = typeof(NodeDocRunner).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        if (mi == null) throw new Exception("找不到静态方法: " + name);
        return mi.Invoke(null, args);
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
}
