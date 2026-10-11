using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.Probe
{
    /// <summary>【验证：战斗 HUD 里到底有哪些"数字文本"，各自在屏幕哪个位置】
    /// 用户报"为什么有两个生命值"：需要**实测**而不是猜。
    /// 做法：运行期枚举 GameUI / HandsArea 下所有 TMP_Text / 旧 UI Text，打印：
    ///   路径 | 文本 | 世界矩形 | 是否激活 | 所属根。
    /// 触发：tools/hudtext_flag.txt → 进 Play → 写 tools/hudtext_result.tsv。
    /// </summary>
    public class HudTextProbe : MonoBehaviour
    {
        private static string Root { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return System.IO.Path.Combine(Root, "tools/hudtext_result.tsv"); } }
        private static string FlagPath { get { return System.IO.Path.Combine(Root, "tools/hudtext_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            GameObject go = new GameObject("HudTextProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<HudTextProbe>();
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
            catch (Exception e) { sb.AppendLine("EXCEPTION\t" + e.Message + "\n" + e.StackTrace); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[HUD文本] 验证完成 → " + OutPath);
        }

        private void Run()
        {
            sb.AppendLine("kind\tpath\ttext\tworld\tactive\troot");
            string[] roots = { "GameUI", "HandsArea", "BattleLogPanel" };
            foreach (string r in roots)
            {
                GameObject go = GameObject.Find(r);
                if (go == null)
                {
                    sb.AppendLine("ROOT-MISSING\t" + r + "\t\t\t\t");
                    continue;
                }
                sb.AppendLine("ROOT\t" + r + "\t\t" + Fmt(WorldRect(go.transform as RectTransform)) + "\t" + go.activeInHierarchy + "\t");
                Walk(go.transform, r, 0);
            }
        }

        private void Walk(Transform t, string root, int depth)
        {
            if (t == null || depth > 12)
                return;

            //★空的也打出来（"==" 标记）：本轮要回答的是"屏幕上有两个生命值"，位置比文字更重要；
            //  若某一刻数值还没刷出来，至少在结构上也能看到有几个 HP 文本框。
            TMP_Text tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
                sb.AppendLine("TMP\t" + PathOf(t) + "\t" + (string.IsNullOrWhiteSpace(tmp.text) ? "==" : Clean(tmp.text))
                    + "\t" + Fmt(WorldRect(t as RectTransform))
                    + "\t" + t.gameObject.activeInHierarchy + "\t" + root);

            Text uitext = t.GetComponent<Text>();
            if (uitext != null)
                sb.AppendLine("UIText\t" + PathOf(t) + "\t" + (string.IsNullOrWhiteSpace(uitext.text) ? "==" : Clean(uitext.text))
                    + "\t" + Fmt(WorldRect(t as RectTransform))
                    + "\t" + t.gameObject.activeInHierarchy + "\t" + root);

            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), root, depth + 1);
        }

        private static string Clean(string s)
        {
            return s.Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            Transform p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 10)
            {
                s = p.name + "/" + s;
                p = p.parent;
            }
            return s;
        }

        private static Rect WorldRect(RectTransform rt)
        {
            if (rt == null)
                return new Rect();
            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        private static string Fmt(Rect r)
        {
            return "x[" + r.xMin.ToString("0.#") + "," + r.xMax.ToString("0.#") + "] y["
                + r.yMin.ToString("0.#") + "," + r.yMax.ToString("0.#") + "]";
        }
    }
}
