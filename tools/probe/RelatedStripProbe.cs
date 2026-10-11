using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.UI;

namespace TcgEngine.Probe
{
    /// <summary>【验证：卡牌弹窗里「相关卡牌」条的实际排版】
    /// 要回答的问题（用户在真实界面里报过三次）：
    ///   ① 这条到底显示了没有？（曾经被摆到面板外面 ⇒ 整条消失）
    ///   ② 有没有压住买卖区（BUY/SELL/COST）？
    ///   ③ 有没有超出面板被裁？
    /// 做法：拿到真实面板（CardZoomPanel.Get）→ 找一张配了 related_card_ids 的卡 → ShowCard →
    ///   几帧后量"这条 / 卡图 / 描述 / 买卖区 / 面板"的世界矩形，算包含与相交。
    /// 触发：建 tools/related_strip_flag.txt → 进 Play（Menu 场景）→ 写 tools/related_strip_result.tsv → 自动删标记。
    /// </summary>
    public class RelatedStripProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/related_strip_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/related_strip_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            GameObject go = new GameObject("RelatedStripProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<RelatedStripProbe>();
        }

        private int frames;
        private int phase;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();
        private CardZoomPanel panel;
        private CardData target;

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath)) { done = false; frames = 0; phase = 0; sb.Clear(); }
                return;
            }
            frames++;

            try
            {
                if (phase == 0)
                {
                    if (frames < 120)
                        return;
                    //★用 includeInactive=true 找：菜单里这份弹窗一开始往往是**未激活**的，
                    //  那样 Awake 没跑、CardZoomPanel.Get() 就是 null（会误报"没有面板"）。
                    panel = UnityEngine.Object.FindObjectOfType<CardZoomPanel>(true);
                    if (panel == null)
                        panel = CardZoomPanel.Get();
                    if (panel == null)
                    {
                        if (frames > 900) { Finish("场景里找不到 CardZoomPanel 实例（Get() 与 FindObjectOfType 都是 null）"); }
                        return;
                    }
                    target = FindCardWithRelated();
                    if (target == null)
                    {
                        if (frames > 900) { Finish("没有任何卡配了相关卡牌（related_card_ids 全空）"); }
                        return;
                    }
                    sb.AppendLine("A 面板实例\t有\t" + panel.name);
                    sb.AppendLine("B 测试用卡\t" + target.id + "\t" + target.title + "\trelated=" + target.related_card_ids.Count);
                    panel.ShowCard(target, VariantData.GetDefault());
                    sb.AppendLine("C ShowCard 已调用\tShowCard 后 activeInHierarchy=" + panel.gameObject.activeInHierarchy);
                    if (!panel.gameObject.activeInHierarchy)
                    {
                        //菜单里这份弹窗通常是未激活的：为"量真实布局"强行打开（会记录在结果里，不影响结论）
                        panel.gameObject.SetActive(true);
                        sb.AppendLine("C2 弹窗原本未激活\t已强制 SetActive(true) 只为量布局");
                    }
                    phase = 1;
                    frames = 0;
                    return;
                }

                if (phase == 1)
                {
                    if (frames < 40)          //弹窗 Show 有动画，等它稳定
                        return;
                    Measure();
                    Finish(null);
                }
            }
            catch (Exception e)
            {
                Finish("EXCEPTION: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private CardData FindCardWithRelated()
        {
            foreach (CardData c in CardData.GetAll())
            {
                if (c != null && c.related_card_ids != null && c.related_card_ids.Count > 0)
                    return c;
            }
            return null;
        }

        private static RectTransform GetRT(object o)
        {
            Component comp = o as Component;
            return comp != null ? comp.transform as RectTransform : null;
        }

        private object GetField(string name)
        {
            FieldInfo f = typeof(CardZoomPanel).GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null ? f.GetValue(panel) : null;
        }

        private void Measure()
        {
            RectTransform panel_rt = panel.transform as RectTransform;
            RectTransform card_rt = GetRT(GetField("card_ui"));
            RectTransform desc_rt = GetRT(GetField("desc"));
            RectTransform trade_rt = GetRT(GetField("trade_area"));
            RectTransform strip_rt = GetField("related_root") as RectTransform;

            sb.AppendLine("D 面板\tsizeDelta=" + (panel_rt != null ? panel_rt.sizeDelta.ToString() : "?")
                + "\tanchoredPos=" + (panel_rt != null ? panel_rt.anchoredPosition.ToString() : "?")
                + "\tpivot=" + (panel_rt != null ? panel_rt.pivot.ToString() : "?"));
            float extra = -1f;
            FieldInfo fe = typeof(CardZoomPanel).GetField("panel_extra", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fe != null) extra = (float)fe.GetValue(panel);
            sb.AppendLine("E 面板加高量 panel_extra\t" + extra.ToString("0.#"));

            if (strip_rt == null)
            {
                sb.AppendLine("F 相关卡牌条\trelated_root = null（从未创建）");
                return;
            }
            sb.AppendLine("F 相关卡牌条 active\t" + strip_rt.gameObject.activeSelf
                + "\tanchors=" + strip_rt.anchorMin + "/" + strip_rt.anchorMax
                + "\tpivot=" + strip_rt.pivot
                + "\tanchoredPos=" + strip_rt.anchoredPosition
                + "\tsizeDelta=" + strip_rt.sizeDelta);

            Rect s = WorldRect(strip_rt);
            Rect p = WorldRect(panel_rt);
            sb.AppendLine("G 世界矩形(条)\t" + Fmt(s));
            sb.AppendLine("H 世界矩形(面板)\t" + Fmt(p));
            sb.AppendLine("I 条在面板内(完全)\t" + (p.xMin <= s.xMin && p.xMax >= s.xMax && p.yMin <= s.yMin && p.yMax >= s.yMax));
            sb.AppendLine("J 条与面板相交面积比\t" + (Area(s) > 0f ? (Intersect(s, p) / Area(s)).ToString("0.00") : "0"));

            ReportOverlap("K 与卡图(卡面)", s, card_rt);
            ReportOverlap("L 与描述框", s, desc_rt);
            ReportOverlap("M 与买卖区", s, trade_rt);

            int active = 0;
            for (int i = 0; i < strip_rt.childCount; i++)
            {
                Transform ch = strip_rt.GetChild(i);
                if (ch.gameObject.activeSelf && ch.name.StartsWith("Slot"))
                    active++;
            }
            sb.AppendLine("N 显示的卡位数\t" + active);
        }

        private void ReportOverlap(string tag, Rect a, RectTransform other)
        {
            if (other == null)
            {
                sb.AppendLine(tag + "\t(无此控件)");
                return;
            }
            Rect b = WorldRect(other);
            float inter = Intersect(a, b);
            sb.AppendLine(tag + "\t" + Fmt(b) + "\t相交面积=" + inter.ToString("0") +
                "\t占本条=" + (Area(a) > 0f ? (inter / Area(a)).ToString("0.00") : "0"));
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
            return "x[" + r.xMin.ToString("0") + "," + r.xMax.ToString("0") + "] y["
                + r.yMin.ToString("0") + "," + r.yMax.ToString("0") + "] w=" + r.width.ToString("0") + " h=" + r.height.ToString("0");
        }

        private static float Area(Rect r) { return Mathf.Max(0f, r.width) * Mathf.Max(0f, r.height); }

        private static float Intersect(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        private void Finish(string note)
        {
            done = true;
            if (!string.IsNullOrEmpty(note))
                sb.AppendLine("Z 结束原因\t" + note);
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            //★只有"真的量到了面板"才删标记：否则（例如这次 Play 起的场景里没有这个弹窗）
            //  保留标记，让下次 Play 自动重试，不用人来记着重新放标记。
            if (panel != null)
            {
                try { File.Delete(FlagPath); } catch { }
            }
            else
            {
                sb.AppendLine("Z2 保留标记\t下次 Play 自动重试（本次没找到弹窗实例）");
            }
            Debug.Log("[相关卡牌条] 验证完成 → " + OutPath);
        }
    }
}
