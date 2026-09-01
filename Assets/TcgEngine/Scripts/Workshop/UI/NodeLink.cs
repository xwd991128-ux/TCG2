using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 连线组件：绘制一条从输出引脚到输入引脚的连线（节点编辑器画布）。
    /// 挂在画布 content 下与节点同级的连线对象上（先于节点渲染，避免遮挡节点）。
    /// line 是一条细长 Image（1 像素宽的圆点拉伸而成），按两端引脚位置旋转拉伸重绘。
    /// 坐标统一使用画布 content 的局部坐标（与 GraphNode.pos / NodePin.GetCanvasPos 一致）。
    /// </summary>
    public class NodeLink : MonoBehaviour
    {
        public RectTransform line;          // 线体 Image 的 RectTransform
        public string from_node;
        public string from_pin;
        public string to_node;
        public string to_pin;

        private NodePin from_ref;           // 起点引脚
        private NodePin to_ref;             // 终点引脚

        /// <summary>绑定数据，并把线体锚点固定在画布原点（pivot 左侧中心，便于旋转拉伸）</summary>
        public void Setup(RectTransform line)
        {
            this.line = line;
            if (line != null)
            {
                line.anchorMin = Vector2.zero;
                line.anchorMax = Vector2.zero;
                line.pivot = new Vector2(0f, 0.5f);
            }
        }

        /// <summary>注入两端引脚引用（用于重绘）</summary>
        public void SetEndpoints(NodePin from, NodePin to)
        {
            from_ref = from;
            to_ref = to;
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

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            line.anchoredPosition = a;
            line.sizeDelta = new Vector2(len, 2.5f);
            line.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
