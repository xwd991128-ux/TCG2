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
    /// 上一轮教训：FindObjectOfType 拿到的第一份 CardZoomPanel 可能是"另一个实例"（挂点全屏、字段没绑），
    /// 拿它当面板量出来的结论是错的 ⇒ 本轮改成：
    ///   ① 枚举**所有** CardZoomPanel 实例（含未激活），逐个打印挂点/字段绑定/尺寸，挑出"看得见的那份"（trade_area 有绑定的）；
    ///   ② 打印 desc 的**祖先链**（每级的 anchors / sizeDelta / rect），好定位"真正的弹窗盒子"是哪一级；
    ///   ③ ShowCard 后量「相关卡牌条 / 卡图 / 描述 / 买卖区」，算包含关系与相交面积。
    /// 触发：建 tools/related_strip_flag.txt → 进 Play → 写 tools/related_strip_result.tsv。
    /// </summary>
    public class RelatedStripProbe : MonoBehaviour
    {
        private static string Root { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")); } }
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
                    CardZoomPanel[] all = UnityEngine.Object.FindObjectsOfType<CardZoomPanel>(true);
                    sb.AppendLine("A 场景里的 CardZoomPanel 实例数\t" + all.Length);
                    for (int i = 0; i < all.Length; i++)
                        sb.AppendLine("A" + i + " 实例\t" + Dump(all[i]));

                    panel = PickPanel(all);
                    if (panel == null)
                    {
                        if (frames > 900) Finish("找不到可用的 CardZoomPanel");
                        return;
                    }
                    sb.AppendLine("A★ 选用\t" + Dump(panel));

                    target = FindCardWithRelated();
                    if (target == null)
                    {
                        if (frames > 900) Finish("没有任何卡配了相关卡牌（related_card_ids 全空）");
                        return;
                    }
                    sb.AppendLine("B 测试用卡\t" + target.id + "\t" + target.title + "\trelated=" + target.related_card_ids.Count);

                    panel.ShowCard(target, VariantData.GetDefault());
                    if (!panel.gameObject.activeInHierarchy)
                    {
                        panel.gameObject.SetActive(true);
                        sb.AppendLine("C2 弹窗原本未激活\t已强制 SetActive(true) 只为量布局");
                    }
                    sb.AppendLine("C ShowCard 已调用\tactiveInHierarchy=" + panel.gameObject.activeInHierarchy);
                    phase = 1;
                    frames = 0;
                    return;
                }

                if (phase == 1)
                {
                    if (frames < 45)
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

        private static CardZoomPanel PickPanel(CardZoomPanel[] all)
        {
            if (all == null || all.Length == 0)
                return CardZoomPanel.Get();
            //优先"绑定最全"的那份（买卖区有绑定的 = 界面上真正在用、带 BUY/SELL 的那份）
            foreach (CardZoomPanel p in all)
            {
                if (p != null && GetFieldStatic(p, "trade_area") != null && GetFieldStatic(p, "desc") != null)
                    return p;
            }
            foreach (CardZoomPanel p in all)
                if (p != null && GetFieldStatic(p, "desc") != null)
                    return p;
            return all[0];
        }

        private static object GetFieldStatic(object obj, string name)
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null ? f.GetValue(obj) : null;
        }

        private static bool GetFieldStaticBool(object obj, string name)
        {
            object v = GetFieldStatic(obj, name);
            return v is bool && (bool)v;
        }

        private static float GetFieldStaticFloat(object obj, string name)
        {
            object v = GetFieldStatic(obj, name);
            return v is float ? (float)v : 0f;
        }

        private static string Dump(CardZoomPanel p)
        {
            if (p == null)
                return "(null)";
            RectTransform rt = p.transform as RectTransform;
            object card = GetFieldStatic(p, "card_ui");
            object desc = GetFieldStatic(p, "desc");
            object trade = GetFieldStatic(p, "trade_area");
            return "path=" + PathOf(p.transform)
                + "\tactiveSelf=" + p.gameObject.activeSelf
                + "\tactiveInHierarchy=" + p.gameObject.activeInHierarchy
                + "\tcard_ui=" + (card != null) + "\tdesc=" + (desc != null) + "\ttrade_area=" + (trade != null)
                + "\t" + (rt != null ? ("anchors=" + rt.anchorMin + "/" + rt.anchorMax + " sizeDelta=" + rt.sizeDelta + " rect=" + rt.rect.size) : "no-rt");
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            Transform p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 8)
            {
                s = p.name + "/" + s;
                p = p.parent;
            }
            return s;
        }

        private CardData FindCardWithRelated()
        {
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.related_card_ids != null && c.related_card_ids.Count > 0)
                    return c;
            return null;
        }

        private static RectTransform GetRT(object o)
        {
            Component comp = o as Component;
            if (comp != null)
                return comp.transform as RectTransform;
            GameObject go = o as GameObject;      //★trade_area 是 GameObject（不是 Component），上一轮就是这里漏了 ⇒ "无此控件"
            return go != null ? go.transform as RectTransform : null;
        }

        private void Measure()
        {
            RectTransform panel_rt = panel.transform as RectTransform;
            RectTransform card_rt = GetRT(GetFieldStatic(panel, "card_ui"));
            RectTransform desc_rt = GetRT(GetFieldStatic(panel, "desc"));
            RectTransform trade_rt = GetRT(GetFieldStatic(panel, "trade_area"));
            RectTransform strip_rt = GetFieldStatic(panel, "related_root") as RectTransform;

            //desc 的祖先链：定位"真正的弹窗盒子"
            sb.AppendLine("== desc 祖先链（从 desc 往上）==");
            Transform cur = desc_rt;
            int guard = 0;
            while (cur != null && guard++ < 8)
            {
                RectTransform r = cur as RectTransform;
                sb.AppendLine("chain" + guard + "\t" + cur.name
                    + (r != null ? ("\tanchors=" + r.anchorMin + "/" + r.anchorMax + "\tsizeDelta=" + r.sizeDelta + "\trect=" + r.rect.size + "\tworld=" + Fmt(WorldRect(r))) : ""));
                cur = cur.parent;
            }

            sb.AppendLine("== 条 ==");
            if (strip_rt == null)
            {
                sb.AppendLine("F related_root = null（从未创建）");
                return;
            }
            sb.AppendLine("F active=" + strip_rt.gameObject.activeSelf
                + "\tparent=" + (strip_rt.parent != null ? PathOf(strip_rt.parent) : "?")
                + "\tanchors=" + strip_rt.anchorMin + "/" + strip_rt.anchorMax
                + "\tpivot=" + strip_rt.pivot
                + "\tanchoredPos=" + strip_rt.anchoredPosition
                + "\tsizeDelta=" + strip_rt.sizeDelta);

            Rect s = WorldRect(strip_rt);
            sb.AppendLine("G 世界矩形(条)\t" + Fmt(s));
            ReportOverlap("K 与卡图(卡面)", s, card_rt);
            ReportOverlap("L 与描述框", s, desc_rt);
            ReportOverlap("M 与买卖区", s, trade_rt);

            //条的父级（我钉的宿主）在哪儿、有多大
            RectTransform host = strip_rt.parent as RectTransform;
            sb.AppendLine("H 宿主\t" + (host != null ? (PathOf(host) + "\trect=" + host.rect.size + "\tworld=" + Fmt(WorldRect(host))) : "?"));
            sb.AppendLine("I 条完全在宿主内\t" + (host != null && Contains(WorldRect(host), s)));

            int active = 0;
            for (int i = 0; i < strip_rt.childCount; i++)
            {
                Transform ch = strip_rt.GetChild(i);
                if (ch.gameObject.activeSelf && ch.name.StartsWith("Slot"))
                    active++;
            }
            sb.AppendLine("N 显示的卡位数\t" + active);

            //条有没有被屏幕边缘切掉（上一轮踩的坑：宿主向下长，条被屏幕底切了一半）
            Canvas cv = panel.GetComponentInParent<Canvas>();
            if (cv != null)
            {
                Rect cvW = WorldRect(cv.transform as RectTransform);
                sb.AppendLine("P 屏幕世界矩形\t" + Fmt(cvW));
                sb.AppendLine("Q 条完全在屏幕内\t" + Contains(cvW, s));
            }
            sb.AppendLine("R 加高方向\t" + (GetFieldStaticBool(panel, "grow_up") ? "向上长(底边不动)" : "向下长(顶边不动)"));
            sb.AppendLine("S 两侧剩余(宿主单位)\tbelow=" + GetFieldStaticFloat(panel, "room_below_u").ToString("0.#")
                + "  above=" + GetFieldStaticFloat(panel, "room_above_u").ToString("0.#"));
            float extra = 0f;
            FieldInfo fe = typeof(CardZoomPanel).GetField("host_extra", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fe != null) extra = (float)fe.GetValue(panel);
            sb.AppendLine("O host_extra(宿主加高量)\t" + extra.ToString("0.#"));
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
            sb.AppendLine(tag + "\t" + Fmt(b) + "\t相交面积=" + inter.ToString("0")
                + "\t占本条=" + (Area(a) > 0f ? (inter / Area(a)).ToString("0.00") : "0"));
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return outer.xMin <= inner.xMin && outer.xMax >= inner.xMax && outer.yMin <= inner.yMin && outer.yMax >= inner.yMax;
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
                + r.yMin.ToString("0.#") + "," + r.yMax.ToString("0.#") + "] w=" + r.width.ToString("0.#") + " h=" + r.height.ToString("0.#");
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
            if (panel != null) { try { File.Delete(FlagPath); } catch { } }
            else sb.AppendLine("Z2 保留标记\t下次 Play 自动重试（本次没找到弹窗实例）");
            Debug.Log("[相关卡牌条] 验证完成 → " + OutPath);
        }
    }
}
