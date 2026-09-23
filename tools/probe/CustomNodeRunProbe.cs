using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Workshop;

/// <summary>【临时探针】自定义节点**运行时调用**验收（不需要开一局）：
///   ① 调用方节点字段值 → 绑定成定义图输入端口 → 定义图算出的值被调用方读到
///   ② 动作类：走执行流跑定义图内部编排（无异常）
///   ③ 两个配套动作（读输入端口/设置输出端口）已进执行白名单
/// 用法：建 tools/custom_run_flag.txt → 进 Play 一次 → 写 tools/custom_run_result.tsv → 自动删标记 + 清理测试节点。
/// </summary>
public static class CustomNodeRunProbe
{
    private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    private static string FlagPath { get { return Path.Combine(Root, "tools/custom_run_flag.txt"); } }
    private static string OutPath { get { return Path.Combine(Root, "tools/custom_run_result.tsv"); } }

    private static bool _running;
    private static StringBuilder sb;
    private static int pass, fail;
    private static string created_id;
    private static readonly Type runner = typeof(NodeDocRunner);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_running || !File.Exists(FlagPath))
            return;
        _running = true;
        sb = new StringBuilder();
        sb.AppendLine("# 自定义节点运行时探针  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
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
            Debug.Log("[自定义节点运行探针] 完成 PASS=" + pass + " FAIL=" + fail + " → " + OutPath);
            try { File.Delete(FlagPath); } catch { }
            _running = false;
        }
    }

    private static void Run()
    {
        CustomNodeData def = CustomNodeIO.New(CustomNodeKind.Function);
        created_id = def.id;
        def.inputs.Clear(); def.outputs.Clear();
        def.inputs.Add(new CustomNodePort("血量", NodeValueType.Int32.ToString()));
        def.outputs.Add(new CustomNodePort("结果", NodeValueType.Int32.ToString()));
        def.outputs.Add(new CustomNodePort("结果2", NodeValueType.Int32.ToString()));   //第二个输出：验证"同一次执行内多输出只跑一遍定义图"
        def.EnsureGraphs();
        GraphData dg = def.graphs[0].graph;

        //定义图：蓝图块 + 读输入端口(血量) + 设置输出端口(结果 ← 读到的值)
        dg.nodes.Add(Node("custom_block", GraphNodeType.Value, def.ActionId, "本体",
            Pin("custom_block_血量", "血量", NodeValueType.Int32, false),
            Pin("custom_block_结果", "结果", NodeValueType.Int32, true)));
        dg.nodes.Add(Node("n_read", GraphNodeType.Value, "GetCustomInputRaw", "读输入",
            Pin("n_read_out", "out", NodeValueType.Object, true), null, "name", "血量"));
        dg.nodes.Add(Node("n_set", GraphNodeType.Action, "SetCustomOutputRaw", "设输出",
            Pin("n_set_in", "in", NodeValueType.Flow, false),
            Pin("n_set_out", "out", NodeValueType.Flow, true),
            Pin("n_set_value", "value", NodeValueType.Object, false), null, "name", "结果"));
        dg.links.Add(new GraphLink { from_node = "n_read", from_pin = "n_read_out", to_node = "n_set", to_pin = "n_set_value" });
        dg.nodes.Add(Node("n_set2", GraphNodeType.Action, "SetCustomOutputRaw", "设输出2",
            Pin("n_set2_in", "in", NodeValueType.Flow, false),
            Pin("n_set2_out", "out", NodeValueType.Flow, true),
            Pin("n_set2_value", "value", NodeValueType.Object, false), null, "name", "结果2"));
        dg.links.Add(new GraphLink { from_node = "n_read", from_pin = "n_read_out", to_node = "n_set2", to_pin = "n_set2_value" });

        //调用方图：一个自定义(函数)节点实例，字段"血量"=7
        GraphData caller = new GraphData();
        caller.nodes.Add(Node("call1", GraphNodeType.Value, def.ActionId, def.GetTitle(),
            Pin("call1_血量", "血量", NodeValueType.Int32, false),
            Pin("call1_结果", "结果", NodeValueType.Int32, true), null, "血量", "7"));
        GraphNode call = caller.nodes[0];

        object v = CallStatic("EvaluateCustomOutput", null, caller, call, "结果", null, null, null);
        Ok("① 函数类：调用方读到输出端口 = 定义图按输入端口算出的值",
            v != null && v.ToString() == "7", "返回=" + (v != null ? v.ToString() : "null") + "（期望 7：字段血量=7 → 定义图读出 → 输出端口返回）");

        //①b 输出缓存：同一输入下读第二个输出端口 → 不应再跑一遍定义图
        int runs1 = Runs();
        object v2 = CallStatic("EvaluateCustomOutput", null, caller, call, "结果2", null, null, null);
        int runs2 = Runs();
        Ok("①b 同一次执行内多输出端口只跑一遍定义图（输出缓存）",
            v2 != null && v2.ToString() == "7" && runs2 == runs1,
            "结果2=" + (v2 != null ? v2.ToString() : "null") + " 执行次数 " + runs1 + " → " + runs2);

        //①c 输入变了 → 缓存失效，重新执行
        call.fields[0].value = "9";
        object v3 = CallStatic("EvaluateCustomOutput", null, caller, call, "结果", null, null, null);
        int runs3 = Runs();
        Ok("①c 输入变化后缓存失效（重新执行定义图）",
            v3 != null && v3.ToString() == "9" && runs3 == runs2 + 1,
            "结果=" + (v3 != null ? v3.ToString() : "null") + " 执行次数 " + runs2 + " → " + runs3);
        call.fields[0].value = "7";

        //动作类：块(动作流出) → 设输出，跑一遍执行流
        def.kind = (int)CustomNodeKind.Action;
        def.EnsureGraphs();
        GraphData dg2 = def.graphs[0].graph;
        dg2.nodes.Clear(); dg2.links.Clear();
        dg2.nodes.Add(Node("custom_block", GraphNodeType.Action, def.ActionId, def.GetTitle(),
            Pin("custom_block_in", "in", NodeValueType.Flow, false),
            Pin("custom_block_out", "out", NodeValueType.Flow, true),
            Pin("custom_block_血量", "血量", NodeValueType.Int32, false),
            Pin("custom_block_结果", "结果", NodeValueType.Int32, true)));
        dg2.nodes.Add(Node("n_read", GraphNodeType.Value, "GetCustomInputRaw", "读输入",
            Pin("n_read_out", "out", NodeValueType.Object, true), null, "name", "血量"));
        dg2.nodes.Add(Node("n_set", GraphNodeType.Action, "SetCustomOutputRaw", "设输出",
            Pin("n_set_in", "in", NodeValueType.Flow, false),
            Pin("n_set_out", "out", NodeValueType.Flow, true),
            Pin("n_set_value", "value", NodeValueType.Object, false), null, "name", "结果"));
        dg2.links.Add(new GraphLink { from_node = "custom_block", from_pin = "custom_block_out", to_node = "n_set", to_pin = "n_set_in" });
        dg2.links.Add(new GraphLink { from_node = "n_read", from_pin = "n_read_out", to_node = "n_set", to_pin = "n_set_value" });
        try
        {
            CallStatic("InvokeCustomNode", null, caller, call, null, null, null);
            Ok("② 动作类：走执行流跑定义图内部编排（无异常）", true, "Console 应有「自定义节点执行 …」日志");
        }
        catch (Exception e) { Fail("② 动作类：走执行流跑定义图内部编排", e.Message); }

        object ok1 = CallStatic("IsSupportedAction", "SetCustomOutputRaw");
        object ok2 = CallStatic("IsSupportedAction", "GetCustomInputRaw");
        Ok("③ 读输入端口/设置输出端口 已进执行白名单", true.Equals(ok1) && true.Equals(ok2), "set=" + ok1 + " get=" + ok2);
    }

    // ---------------- 工具 ----------------

    private static GraphNode Node(string id, GraphNodeType type, string action, string title, params object[] rest)
    {
        GraphNode n = new GraphNode { id = id, type = type, action = action, title = title, category = "自定义" };
        n.pins = new List<GraphPin>();
        n.fields = new List<FieldCustomData>();
        for (int i = 0; i < rest.Length; i++)
        {
            GraphPin p = rest[i] as GraphPin;
            if (p != null) { n.pins.Add(p); continue; }
            if (i + 1 < rest.Length && rest[i] is string)
            {
                n.fields.Add(new FieldCustomData { name = (string)rest[i], value = rest[i + 1] as string ?? "" });
                i++;
            }
        }
        return n;
    }

    private static GraphPin Pin(string id, string name, NodeValueType type, bool output)
    {
        return new GraphPin { id = id, name = name, display_name = name, type = type, is_output = output };
    }

    private static object CallStatic(string name, params object[] args)
    {
        MethodInfo mi = runner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        if (mi == null) throw new Exception("找不到静态方法: " + name);
        return mi.Invoke(null, args);
    }

    /// <summary>读 NodeDocRunner.custom_eval_runs（函数类定义图实际执行次数）</summary>
    private static int Runs()
    {
        FieldInfo fi = runner.GetField("custom_eval_runs", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        object v = fi != null ? fi.GetValue(null) : null;
        return v is int ? (int)v : -1;
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
