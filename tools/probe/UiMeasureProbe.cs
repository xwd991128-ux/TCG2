using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.UI;

namespace TcgEngine.Probe
{
    /// <summary>【UI 尺寸测量：为什么"战斗记录"面板在屏幕上方只出现半截/小半张】
    /// 打印：屏幕分辨率、当前场景、场景里每个 Canvas 的（renderMode/sortingOrder/scaleFactor/pixelRect）
    ///   + 它的 CanvasScaler（模式/参考分辨率/match）+ 它的 RectTransform 实际像素尺寸；
    /// 再打印 BattleLogPanel 自建画布与面板根节点的实际像素尺寸（世界角点换算成屏幕像素）。
    /// 触发：tools/uimeasure_flag.txt → 进 Play → tools/uimeasure_result.tsv → 自动删标记。</summary>
    public class UiMeasureProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/uimeasure_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/uimeasure_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("UiMeasureProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<UiMeasureProbe>();
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
            catch (Exception e) { sb.AppendLine("EXCEPTION\t" + e.Message + "\t" + e.StackTrace); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[UI测量] 完成 → " + OutPath);
        }

        private void Run()
        {
            sb.AppendLine("Screen\t" + Screen.width + " x " + Screen.height + "\t(Play 窗口/Game view 实际像素)");
            sb.AppendLine("ActiveScene\t" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
            sb.AppendLine("Canvas 总数\t" + canvases.Length);
            foreach (Canvas c in canvases)
            {
                if (c == null) continue;
                CanvasScaler sc = c.GetComponent<CanvasScaler>();
                string scaler = sc == null ? "(无 CanvasScaler)"
                    : (sc.uiScaleMode + " ref=" + sc.referenceResolution.x + "x" + sc.referenceResolution.y
                       + " match=" + sc.matchWidthOrHeight + " screenMatch=" + sc.screenMatchMode
                       + " scaleFactor=" + sc.scaleFactor);
                RectTransform rt = c.GetComponent<RectTransform>();
                string rect = "(无 RectTransform)";
                if (rt != null)
                {
                    Vector3[] corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    float h = Vector3.Distance(corners[0], corners[3]);
                    float w = Vector3.Distance(corners[0], corners[1]);
                    rect = "sizeDelta=" + rt.sizeDelta.x + "x" + rt.sizeDelta.y
                        + " 实际像素=" + Mathf.RoundToInt(w) + "x" + Mathf.RoundToInt(h);
                }
                sb.AppendLine("Canvas\t" + PathOf(c.transform) + "\tmode=" + c.renderMode + " order=" + c.sortingOrder
                    + " active=" + c.isActiveAndEnabled + " scaleFactor=" + c.scaleFactor
                    + " pixelRect=" + c.pixelRect.width + "x" + c.pixelRect.height
                    + "\n\t\t" + scaler + "\n\t\t" + rect);
            }

            DumpNamed("BattleLogCanvas");
            DumpNamed("BattleLogRoot");
            DumpNamed("BattleLogToggle");
            DumpNamed("BattleLogPanel");
            DumpNamed("MenuCanvas");
            DumpNamed("UI");
        }

        private void DumpNamed(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                sb.AppendLine("目标\t" + name + "\t(场景里找不到/未激活)");
                return;
            }
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null)
            {
                sb.AppendLine("目标\t" + name + "\t(无 RectTransform)");
                return;
            }
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            sb.AppendLine("目标\t" + PathOf(go.transform) + "\tsizeDelta=" + rt.sizeDelta.x + "x" + rt.sizeDelta.y
                + " anchorMin=" + rt.anchorMin + " anchorMax=" + rt.anchorMax + " pivot=" + rt.pivot
                + " pos=" + rt.anchoredPosition
                + "\t实际像素=" + Mathf.RoundToInt(Vector3.Distance(corners[0], corners[1])) + "x"
                + Mathf.RoundToInt(Vector3.Distance(corners[0], corners[3]))
                + "\t左下角屏幕坐标=" + corners[0].x.ToString("F0") + "," + corners[0].y.ToString("F0"));
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            Transform p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 8) { s = p.name + "/" + s; p = p.parent; }
            return s;
        }
    }
}
