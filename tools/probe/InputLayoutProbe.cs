using System;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine;
using TcgEngine.UI;
using TcgEngine.Workshop;

/// <summary>【临时探针】诊断「输入框：文字不垂直居中 / 多一个空格」。
/// 打开面板 → 对"节点库搜索框"（node_search_input）与"卡名输入框"（input_name）各 dump 一份布局：
///   输入框自身：anchors / pivot / sizeDelta / offsets；文本组件：alignment / rect / margin / fontSize / lineHeight；
///   textViewport 矩形；以及当前 text（用 [] 括起来好看出占位空格）。
/// 用法：建 tools/input_probe_flag.txt → 进 Play 一次 → 写 tools/input_layout.tsv → 自动删标记。
/// </summary>
public static class InputLayoutProbe
{
    private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    private static string FlagPath { get { return Path.Combine(Root, "tools/input_probe_flag.txt"); } }
    private static string OutPath { get { return Path.Combine(Root, "tools/input_layout.tsv"); } }

    private static bool _running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_running || !File.Exists(FlagPath))
            return;
        _running = true;
        StringBuilder sb = new StringBuilder();
        try
        {
            GraphEditorPanel panel = null;
            GraphEditorPanel[] all = UnityEngine.Object.FindObjectsOfType<GraphEditorPanel>(true);
            if (all != null && all.Length > 0) panel = all[0];
            if (panel == null)
            {
                sb.AppendLine("# 找不到 GraphEditorPanel（面板不在当前场景）");
            }
            else
            {
                if (!panel.gameObject.activeInHierarchy)
                    panel.gameObject.SetActive(true);
                Call(panel, "EnsureTmpUI");      //跑一遍 TMP 迁移（搜索框就是在这里被转成 TMP 的）
                Call(panel, "GuardAllInputs");   //★真实使用时 RefreshNodeLib/RefreshNodeFields 会走到这里（占位+居中都在 Guard 里）

                TMP_InputField search = GetField<TMP_InputField>(panel, "node_search_input");
                TMP_InputField name = GetField<TMP_InputField>(panel, "input_name");
                sb.AppendLine("# 输入框布局诊断  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Dump(sb, "节点库搜索框/搜索", search);
                Dump(sb, "卡名输入框/名称", name);

                //★修复验证：单行框应垂直居中；聚焦后占位空格应被清掉、失焦兜底应补回
                CheckCentered(sb, "节点库搜索框", search);
                CheckCentered(sb, "卡名输入框", name);
                if (search != null && search.textComponent != null)
                {
                    TmpInputUtil.BeginEdit(search);
                    string raw_focus = search.text ?? "";
                    sb.AppendLine("CHECK\t聚焦后文本被清空（打字不会多空格）\t"
                        + (raw_focus.Length == 0 ? "PASS" : "FAIL") + "\t聚焦后=[" + raw_focus + "] 码点=" + Codes(raw_focus));
                    TmpInputUtil.EnsureNotEmpty(search);
                    sb.AppendLine("CHECK\t失焦兜底补回占位（防 TMP 空文本越界）\t"
                        + (search.text == TmpInputUtil.Placeholder ? "PASS" : "FAIL")
                        + "\t兜底后=[" + search.text + "] 码点=" + Codes(search.text));
                    TmpInputUtil.Write(search, "abc");
                    sb.AppendLine("CHECK\t写入 abc 后读取干净（无多余空格）\t"
                        + (TmpInputUtil.Read(search) == "abc" ? "PASS" : "FAIL")
                        + "\t读回=[" + TmpInputUtil.Read(search) + "] 码点=" + Codes(TmpInputUtil.Read(search)));
                }
            }
        }
        catch (Exception e)
        {
            sb.AppendLine("ERR\t" + e);
        }
        finally
        {
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[输入框探针] 完成 → " + OutPath);
            try { File.Delete(FlagPath); } catch { }
            _running = false;
        }
    }

    private static void Dump(StringBuilder sb, string tag, TMP_InputField inp)
    {
        sb.AppendLine("=== " + tag + " ===");
        if (inp == null)
        {
            sb.AppendLine("  （空引用：面板字段没绑上）");
            return;
        }
        RectTransform go_rt = inp.transform as RectTransform;
        Row(sb, "输入框自身", go_rt);
        sb.AppendLine("  inp.text = [" + inp.text + "]  长度=" + (inp.text != null ? inp.text.Length : -1)
            + "  码点=" + Codes(inp.text));
        TMP_Text ph = inp.placeholder as TMP_Text;
        sb.AppendLine("  placeholder = [" + (ph != null ? ph.text : "<无>") + "]");
        if (inp.textComponent != null)
            sb.AppendLine("  textComponent.text = [" + inp.textComponent.text + "]  码点=" + Codes(inp.textComponent.text)
                + "  textInfo首字符码点=" + FirstCharCode(inp.textComponent));
        sb.AppendLine("  失焦兜底占位串 = [" + TmpInputUtil.Placeholder + "] 码点=" + Codes(TmpInputUtil.Placeholder));
        sb.AppendLine("  lineType=" + inp.lineType + "  isFocused=" + inp.isFocused
            + "  fontSize(设)=" + inp.pointSize + "  caretColor=" + inp.caretColor);
        RectTransform vp = inp.textViewport;
        Row(sb, "textViewport", vp);
        TMP_Text tc = inp.textComponent;
        if (tc == null)
        {
            sb.AppendLine("  （textComponent 为空）");
            return;
        }
        Row(sb, "文本组件", tc.rectTransform);
        sb.AppendLine("  文本: alignment=" + tc.alignment + "  margin=" + tc.margin + "  fontSize=" + tc.fontSize
            + "  enableWordWrapping=" + tc.enableWordWrapping + "  overflow=" + tc.overflowMode);
        if (tc.font != null)
            sb.AppendLine("  font=" + tc.font.name + "  pointSize=" + tc.fontSize + "  fontSizeMin/Max=" + tc.fontSizeMin + "/" + tc.fontSizeMax);
        if (tc.textInfo != null)
            sb.AppendLine("  textInfo: characterCount=" + tc.textInfo.characterCount + " lineCount=" + tc.textInfo.lineCount);
        else
            sb.AppendLine("  textInfo: <尚未生成（无布局）>");
        if (tc.fontSize > 0)
            sb.AppendLine("  参考：文本 rect 高=" + tc.rectTransform.rect.height + " vs 输入框高=" + (go_rt != null ? go_rt.rect.height : 0f)
                + "（文本 rect 若=输入框高且 alignment 含 Left/Center 则应垂直居中）");
    }

    /// <summary>单行框垂直居中检查：alignment 必须是 TMP 的 Left(=MidlineLeft)，且文本 rect 上下留白对称</summary>
    private static void CheckCentered(StringBuilder sb, string tag, TMP_InputField inp)
    {
        if (inp == null || inp.textComponent == null)
        {
            sb.AppendLine("CHECK\t" + tag + " 垂直居中\tFAIL\t（字段为空）");
            return;
        }
        TMP_Text tc = inp.textComponent;
        RectTransform tr = tc.rectTransform;
        float center_off = (tr.offsetMin.y + tr.offsetMax.y) * 0.5f;   //相对盒子中心的偏移（0=正居中）
        bool align_ok = tc.alignment == TextAlignmentOptions.Left;
        bool pad_ok = Mathf.Abs(center_off) < 0.01f;
        sb.AppendLine("CHECK\t" + tag + " 文字垂直居中\t" + (align_ok && pad_ok ? "PASS" : "FAIL")
            + "\talignment=" + tc.alignment + " 上下留白=" + tr.offsetMin.y + "/" + tr.offsetMax.y
            + " 相对中心偏移=" + center_off);
    }

    /// <summary>把字符串每个字符的码点列出来（空格=32、零宽空格=8203 等，肉眼分辨"看不见的字符"）</summary>
    private static string Codes(string s)
    {
        if (s == null)
            return "<null>";
        StringBuilder b = new StringBuilder();
        foreach (char c in s)
            b.Append((int)c).Append(',');
        return b.ToString().TrimEnd(',');
    }

    private static string FirstCharCode(TMP_Text tc)
    {
        try
        {
            if (tc == null || tc.textInfo == null || tc.textInfo.characterCount <= 0)
                return "<无字符>";
            return ((int)tc.textInfo.characterInfo[0].character).ToString();
        }
        catch (Exception e) { return "ERR " + e.Message; }
    }

    private static void Row(StringBuilder sb, string label, RectTransform rt)
    {
        if (rt == null)
        {
            sb.AppendLine("  " + label + "：（空）");
            return;
        }
        sb.AppendLine("  " + label + "：" + NodePath(rt)
            + "  anchorMin=" + rt.anchorMin + " anchorMax=" + rt.anchorMax + " pivot=" + rt.pivot
            + " sizeDelta=" + rt.sizeDelta + " offsets(min/max)=" + rt.offsetMin + "/" + rt.offsetMax
            + " anchoredPos=" + rt.anchoredPosition + " localPos=" + rt.localPosition
            + " rect=" + rt.rect + " worldH=" + rt.rect.height * rt.lossyScale.y);
    }

    private static string NodePath(Transform t)
    {
        string s = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            s = t.name + "/" + s;
        }
        return s;
    }

    private static T GetField<T>(object target, string name) where T : class
    {
        FieldInfo fi = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return fi != null ? fi.GetValue(target) as T : null;
    }

    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (mi == null)
            throw new Exception("找不到方法: " + method);
        return mi.Invoke(target, args);
    }
}
