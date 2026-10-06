using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;              //★TMP_Text/TMP_InputField（漏了这行 → 拷进工程后整包编译失败，Play 起不来）
using TcgEngine.UI;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【规则编辑器「卡牌文本 / 描述」行诊断】
    /// ① 属性表单里是否存在这两行（按 PropLabel 文本匹配）？行内是否有 Field？
    /// ② 富文本按钮（RichTextText / RichTextDesc）是否建出来了？原输入框是否已被隐藏？
    /// ③ 若按钮在：它是否被别的可点击图形盖住（射线命中最顶层是谁）？
    /// 触发：建 tools/richtextrow_flag.txt → 进 Play → 写 tools/richtextrow_result.txt → 自动删标记。
    /// </summary>
    public class RichTextRowProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/richtextrow_result.txt"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/richtextrow_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("RichTextRowProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<RichTextRowProbe>();
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
            if (frames < 150)
                return;
            done = true;
            try { Run(); }
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[富文本行] 诊断完成 → " + OutPath);
        }

        private static string LabelOf(RectTransform row)
        {
            Transform lb = row != null ? row.Find("PropLabel") : null;
            if (lb == null) return "(无PropLabel)";
            TMP_Text tmp = lb.GetComponent<TMP_Text>();
            if (tmp != null) return tmp.text;
            Text legacy = lb.GetComponent<Text>();
            return legacy != null ? legacy.text : "(空)";
        }

        private static string PathOf(Transform t)
        {
            if (t == null) return "null";
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private void Run()
        {
            GraphEditorPanel[] panels = Resources.FindObjectsOfTypeAll<GraphEditorPanel>();
            sb.AppendLine("① GraphEditorPanel 实例数 = " + panels.Length);
            if (panels.Length == 0)
                return;
            GraphEditorPanel panel = panels[0];
            sb.AppendLine("   面板=" + panel.name + " 激活=" + panel.gameObject.activeInHierarchy);

            Transform prop = panel.transform.Find("PropArea");
            Transform content = prop != null ? prop.Find("PropScroll/Viewport/Content") : null;
            if (content == null)
            {
                sb.AppendLine("❌ 找不到 PropArea/PropScroll/Viewport/Content（属性表单容器）");
                return;
            }
            sb.AppendLine("② 属性表单行数=" + content.childCount);
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform row = content.GetChild(i) as RectTransform;
                if (row == null) continue;
                sb.AppendLine("   行[" + i + "] name=" + row.name + " 标签=\"" + LabelOf(row) + "\" 激活=" + row.gameObject.activeSelf);
            }

            string[] want = { "卡牌文本", "描述" };
            for (int w = 0; w < want.Length; w++)
            {
                RectTransform row = null;
                for (int i = 0; i < content.childCount; i++)
                {
                    RectTransform r = content.GetChild(i) as RectTransform;
                    if (r != null && LabelOf(r) == want[w]) { row = r; break; }
                }
                if (row == null)
                {
                    sb.AppendLine("❌ 找不到行「" + want[w] + "」（按 PropLabel 文本匹配失败 → SetupRichTextRow 会直接 return，点了当然没反应）");
                    continue;
                }
                Transform field = row.Find("Field");
                sb.AppendLine("③ 行「" + want[w] + "」找到：Field=" + (field != null ? "有" : "无"));
                if (field == null) continue;

                for (int c = 0; c < field.childCount; c++)
                {
                    Transform ch = field.GetChild(c);
                    if (ch == null) continue;
                    Graphic g = ch.GetComponent<Graphic>();
                    TMP_InputField ti = ch.GetComponent<TMP_InputField>();
                    InputField le = ch.GetComponent<InputField>();
                    sb.AppendLine("     Field 子[" + c + "] " + ch.name + " 激活=" + ch.gameObject.activeSelf
                        + " graphic=" + (g != null ? g.GetType().Name + "(raycast=" + g.raycastTarget + ")" : "无")
                        + (ti != null ? " TMP_InputField(可输入=" + ti.interactable + ")" : "")
                        + (le != null ? " 旧InputField(可输入=" + le.interactable + ")" : ""));
                }

                // 富文本按钮是否在（名字 RichTextText / RichTextDesc）
                string btn_name = want[w] == "卡牌文本" ? "RichTextText" : "RichTextDesc";
                Transform btn = field.Find(btn_name);
                sb.AppendLine("     富文本按钮[" + btn_name + "]=" + (btn != null ? "有" : "❌ 无（行没被执行 SetupRichTextRow）")
                    + (btn != null ? (" 激活=" + btn.gameObject.activeSelf + " raycast="
                        + (btn.GetComponent<Graphic>() != null ? btn.GetComponent<Graphic>().raycastTarget.ToString() : "无Graphic")) : ""));

                // 该字段区中心的射线命中最顶层是谁
                if (btn != null)
                {
                    RectTransform brt = btn as RectTransform;
                    Vector3 world = brt.TransformPoint(brt.rect.center);
                    Canvas cv = panel.GetComponentInParent<Canvas>(true);
                    Camera cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null)
                    {
                        var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screen };
                        List<UnityEngine.EventSystems.RaycastResult> hits = new List<UnityEngine.EventSystems.RaycastResult>();
                        es.RaycastAll(ped, hits);
                        sb.AppendLine("     射线采样(屏幕 " + screen.ToString("F0") + ") 命中 " + hits.Count + " 个，最顶层="
                            + (hits.Count > 0 ? PathOf(hits[0].gameObject.transform) : "<无>"));
                    }
                    else sb.AppendLine("     ⚠ 没有 EventSystem（全场景都点不动）");
                }
            }
        }
    }
}
