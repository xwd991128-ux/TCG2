using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 连线组件：绘制一条从输出引脚到输入引脚的连线（节点编辑器画布）。
    /// 挂在画布 content 下与节点同级的连线对象上（先于节点渲染，避免遮挡节点）。
    /// line 是一条细长 Image，按两端引脚位置旋转拉伸重绘。
    /// 坐标统一使用画布 content 的局部坐标（与 GraphNode.pos / NodePin.GetCanvasPos 一致）。
    /// 【两线制】动作线（Exec）= 紫 #7c5cff 实线（粗 3px）；取值线（数据端口）= 按类型配色的半透明细线（粗 1.5px）。
    /// 【交互】线体自带透明加宽命中层（LineHit），右键点击连线可删除（取消连接）。
    /// </summary>
    public class NodeLink : MonoBehaviour, IPointerClickHandler
    {
        public RectTransform line;          // 线体 Image 的 RectTransform
        public string from_node;
        public string from_pin;
        public string to_node;
        public string to_pin;
        public System.Action<NodeLink> onDelete;   // 右键点击连线回调（编辑器绑定，删除该连线）

        private NodePin from_ref;           // 起点引脚
        private NodePin to_ref;             // 终点引脚
        private NodeValueType style_type = NodeValueType.Flow;   // 线样式类型（默认动作线，兼容旧连线）

        /// <summary>绑定数据，并把线体锚点固定在画布原点（pivot 左侧中心，便于旋转拉伸）</summary>
        public void Setup(RectTransform line)
        {
            this.line = line;
            if (line != null)
            {
                line.anchorMin = Vector2.zero;
                line.anchorMax = Vector2.zero;
                line.pivot = new Vector2(0f, 0.5f);

                //透明加宽命中层：铺满线长、上下各扩 7px，便于右键点中（视觉仍是细线）
                GameObject hit_go = new GameObject("LineHit", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                hit_go.transform.SetParent(line, false);
                RectTransform hit_rt = hit_go.GetComponent<RectTransform>();
                hit_rt.anchorMin = new Vector2(0f, 0.5f);
                hit_rt.anchorMax = new Vector2(1f, 0.5f);
                hit_rt.offsetMin = new Vector2(0f, -7f);
                hit_rt.offsetMax = new Vector2(0f, 7f);
                Image himg = hit_go.GetComponent<Image>();
                himg.color = new Color(1f, 1f, 1f, 0.01f);
                himg.raycastTarget = true;
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
            if (line == null)
                return;
            Image img = line.GetComponent<Image>();
            if (img == null)
                return;
            bool is_action = IsActionType(style_type);
            if (hl)
            {
                img.color = new Color(1f, 0.92f, 0.3f, 0.95f);
                line.sizeDelta = new Vector2(line.sizeDelta.x, 5f);
            }
            else
            {
                img.color = is_action ? ActionColor : StyleColor(style_type);
                line.sizeDelta = new Vector2(line.sizeDelta.x, is_action ? 3f : 1.5f);
            }
        }

        /// <summary>按两端引脚位置重绘（画布 content 局部坐标）</summary>
        public void Redraw()
        {
            if (line == null || from_ref == null || to_ref == null)
                return;

            Vector2 a = from_ref.GetCanvasPos();
            Vector2 b = to_ref.GetCanvasPos();
            Vector2 delta = b - a;
            float len = delta.magnitude;
            if (len < 1f)
                len = 1f;

            bool is_action = IsActionType(style_type);
            Image img = line.GetComponent<Image>();
            if (img != null)
            {
                img.color = is_action ? ActionColor : StyleColor(style_type);
                img.raycastTarget = false;
            }
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            line.anchoredPosition = a;
            line.sizeDelta = new Vector2(len, is_action ? 3f : 1.5f);
            line.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static bool IsActionType(NodeValueType type)
        {
            return type == NodeValueType.Flow || type == NodeValueType.None || type == NodeValueType.ActionNode;
        }

        //配色（方案：执行=紫 #7c5cff / 整数=蓝 #5b9dff / 卡牌=红 #e5484d / 布尔/玩家=灰 #8a8fa3 / 文本=绿 #35c28a）
        private static readonly Color ActionColor = new Color(0.486f, 0.361f, 1f, 0.9f);    // 紫 #7c5cff

        /// <summary>取值线按端口类型配色（与引脚彩点一致）</summary>
        private static Color StyleColor(NodeValueType type)
        {
            switch (type)
            {
                case NodeValueType.Int32: return new Color(0.357f, 0.616f, 1f, 0.55f);      //蓝 #5b9dff
                case NodeValueType.Boolean:
                case NodeValueType.Player:
                case NodeValueType.Object: return new Color(0.541f, 0.561f, 0.639f, 0.55f); //灰 #8a8fa3
                case NodeValueType.Card: return new Color(0.898f, 0.282f, 0.302f, 0.55f);   //红 #e5484d
                case NodeValueType.String: return new Color(0.208f, 0.761f, 0.541f, 0.55f); //绿 #35c28a
                case NodeValueType.CardDefine: return new Color(1f, 0.62f, 0.35f, 0.55f);   //橙（卡牌定义）
                case NodeValueType.Pile:
                case NodeValueType.EventArg: return new Color(1f, 0.82f, 0.4f, 0.55f);      //黄
                case NodeValueType.Buff:
                case NodeValueType.BuffDefine: return new Color(1f, 0.56f, 0.64f, 0.55f);   //粉
                default: return new Color(0.8f, 0.8f, 0.8f, 0.45f);
            }
        }
    }
}
