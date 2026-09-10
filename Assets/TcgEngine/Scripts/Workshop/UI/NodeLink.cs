using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 连线组件：把一条输出→输入的贝塞尔曲线，用若干段细长 Image 拼出来（节点编辑器画布）。
    /// 之所以用「多段 Image」而不是自绘 Graphic：Image 渲染最稳（自定义 mesh 在本项目里会渲染成黑块）。
    /// 连线对象为画布 content 下与节点同级的 stretch 矩形（pivot(0,0)），局部坐标即 content 坐标；
    /// 控制点把曲线向上方拱起。
    /// 【两线制】动作线（Exec）= 紫 #7c5cff 实线；取值线（数据端口）= 按类型配色的细线。
    /// 【交互】沿曲线中段分布若干透明命中块，右键点击可删除连线。
    /// </summary>
    public class NodeLink : MonoBehaviour, IPointerClickHandler
    {
        public RectTransform line;          // 连线根（stretch 到 content，pivot(0,0)）
        public string from_node;
        public string from_pin;
        public string to_node;
        public string to_pin;
        public System.Action<NodeLink> onDelete;   // 右键点击连线回调（编辑器绑定，删除该连线）

        private const int SEG_COUNT = 24;           // 曲线分段数（越多越圆滑）
        private const int HIT_SEGMENTS = 8;         // 沿曲线中段放置的命中块数量
        private const float ARC = 0f;               // 曲线垂直拱起高度（0=水平 S 曲线，不做上拱）
        private const float HIT_SIZE = 14f;         // 命中块尺寸

        private NodePin from_ref;           // 起点引脚
        private NodePin to_ref;             // 终点引脚
        private NodeValueType style_type = NodeValueType.Flow;   // 线样式类型（默认动作线，兼容旧连线）
        private readonly List<RectTransform> segs = new List<RectTransform>();
        private readonly List<Image> seg_imgs = new List<Image>();
        private readonly List<RectTransform> hits = new List<RectTransform>();

        /// <summary>绑定数据：连线根铺满 content（pivot 左下），建立曲线分段与命中块</summary>
        public void Setup(RectTransform line)
        {
            this.line = line;
            if (line == null)
                return;

            line.anchorMin = Vector2.zero;
            line.anchorMax = Vector2.one;
            line.offsetMin = Vector2.zero;
            line.offsetMax = Vector2.zero;
            line.pivot = Vector2.zero;           // 局部原点 = content 左下角
            line.localRotation = Quaternion.identity;

            //模板自带的直线 Image 不再使用
            Image old = line.GetComponent<Image>();
            if (old != null)
                old.enabled = false;

            //曲线分段（细长 Image，pivot 在左端中点，便于按段旋转拉伸）
            segs.Clear();
            seg_imgs.Clear();
            for (int i = 0; i < SEG_COUNT; i++)
            {
                GameObject go = new GameObject("Seg", typeof(RectTransform), typeof(Image));
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.SetParent(line, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(1f, 2f);
                Image im = go.GetComponent<Image>();
                im.raycastTarget = false;
                segs.Add(rt);
                seg_imgs.Add(im);
            }

            //命中块：曲线中段的透明小方块，用于右键点中删除
            for (int i = 0; i < HIT_SEGMENTS; i++)
            {
                GameObject go = new GameObject("LineHit", typeof(RectTransform), typeof(Image));
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.SetParent(line, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(HIT_SIZE, HIT_SIZE);
                Image himg = go.GetComponent<Image>();
                himg.color = new Color(1f, 1f, 1f, 0.01f);
                himg.raycastTarget = true;
                hits.Add(rt);
            }
        }

        /// <summary>右键点击连线 → 通知编辑器删除（取消连接）</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right && onDelete != null)
                onDelete(this);
        }

        /// <summary>注入两端引脚引用（用于重绘）</summary>
        public void SetEndpoints(NodePin from, NodePin to)
        {
            from_ref = from;
            to_ref = to;
        }

        /// <summary>设置线样式：Exec=动作线（紫实线），数据端口=取值线（按类型配色）</summary>
        public void SetStyle(NodeValueType type)
        {
            style_type = type;
        }

        /// <summary>执行高亮：高亮时线体变亮黄加粗；取消时按线样式恢复（编辑器运行走线反馈）</summary>
        public void SetHighlighted(bool hl)
        {
            bool is_action = IsActionType(style_type);
            Color col = hl ? new Color(1f, 0.92f, 0.3f, 1f)
                           : LineColor(is_action);
            float w = hl ? 2.5f : LineWidth(is_action);
            for (int i = 0; i < seg_imgs.Count; i++)
            {
                if (seg_imgs[i] != null)
                    seg_imgs[i].color = col;
                if (segs[i] != null)
                    segs[i].sizeDelta = new Vector2(segs[i].sizeDelta.x, w);
            }
        }

        /// <summary>按两端引脚位置重绘（画布 content 局部坐标）</summary>
        public void Redraw()
        {
            if (line == null || from_ref == null || to_ref == null)
                return;
            Draw(from_ref.GetCanvasPos(), to_ref.GetCanvasPos());
        }

        /// <summary>按显式两端点重绘（拖拽临时线用）</summary>
        public void Draw(Vector2 a, Vector2 b)
        {
            if (line == null)
                return;

            //控制点：水平外扩 + 向上拱起（高度在节点上方）。
            //把手长度不得超过水平距离的一半，否则两端把手会互相越过 → 线会"绕一圈"打环
            float dx = Mathf.Abs(b.x - a.x);
            float h = Mathf.Clamp(dx * 0.45f, 16f, 140f);
            Vector2 c1 = a + new Vector2(h, ARC);
            Vector2 c2 = b + new Vector2(-h, ARC);

            bool is_action = IsActionType(style_type);
            Color col = LineColor(is_action);
            float w = LineWidth(is_action);

            //用分段细长 Image 拼出曲线
            int n = segs.Count;
            Vector2 prev = a;
            for (int i = 0; i < n; i++)
            {
                Vector2 cur = Bezier(a, c1, c2, b, (i + 1f) / n);
                Vector2 d = cur - prev;
                float len = d.magnitude;
                if (len < 0.01f)
                    len = 0.01f;
                RectTransform rt = segs[i];
                rt.anchoredPosition = prev;
                rt.sizeDelta = new Vector2(len + 1.5f, w);   //略长，避免折点处出现缝隙
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                if (seg_imgs[i] != null)
                    seg_imgs[i].color = col;
                prev = cur;
            }

            //命中块只放在曲线中段（避开两端节点区域，减少对节点点击的遮挡）
            int hn = hits.Count;
            for (int i = 0; i < hn; i++)
            {
                float t = hn <= 1 ? 0.5f : Mathf.Lerp(0.25f, 0.75f, (float)i / (hn - 1));
                hits[i].anchoredPosition = Bezier(a, c1, c2, b, t);
            }
        }

        private static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static bool IsActionType(NodeValueType type)
        {
            return type == NodeValueType.Flow || type == NodeValueType.None || type == NodeValueType.ActionNode;
        }

        private static float LineWidth(bool is_action)
        {
            //画布默认 2 倍缩放，故这里取较小值；实际视觉约为 3.5 / 2 px
            return is_action ? 1.75f : 1f;
        }

        private Color LineColor(bool is_action)
        {
            return is_action ? ActionColor : StyleColor(style_type);
        }

        //配色（方案：执行=紫 #7c5cff / 整数=蓝 #5b9dff / 卡牌=红 #e5484d / 布尔/玩家=灰 #8a8fa3 / 文本=绿 #35c28a）
        private static readonly Color ActionColor = new Color(0.486f, 0.361f, 1f, 0.95f);   // 紫 #7c5cff

        /// <summary>取值线按端口类型配色（与引脚彩点完全一致）</summary>
        private static Color StyleColor(NodeValueType type)
        {
            switch (type)
            {
                case NodeValueType.Int32: return new Color(0.357f, 0.616f, 1f, 0.95f);      //蓝 #5b9dff
                case NodeValueType.Boolean:
                case NodeValueType.Player:
                case NodeValueType.Object: return new Color(0.541f, 0.561f, 0.639f, 0.95f); //灰 #8a8fa3
                case NodeValueType.Card: return new Color(0.898f, 0.282f, 0.302f, 0.95f);   //红 #e5484d
                case NodeValueType.String: return new Color(0.208f, 0.761f, 0.541f, 0.95f); //绿 #35c28a
                case NodeValueType.CardDefine: return new Color(1f, 0.62f, 0.35f, 0.95f);   //橙（卡牌定义）
                case NodeValueType.Pile:
                case NodeValueType.EventArg: return new Color(1f, 0.82f, 0.4f, 0.95f);      //黄
                case NodeValueType.Buff:
                case NodeValueType.BuffDefine: return new Color(1f, 0.56f, 0.64f, 0.95f);   //粉
                default: return new Color(0.8f, 0.8f, 0.8f, 0.9f);                          //灰
            }
        }
    }
}
