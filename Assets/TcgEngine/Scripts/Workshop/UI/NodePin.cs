using UnityEngine;
using UnityEngine.EventSystems;

namespace TcgEngine.UI
{
    /// <summary>
    /// 节点引脚组件：挂在节点上的输入/输出引脚小圆点。
    /// 从输出引脚拖拽到输入引脚建立 GraphLink；松手位置由宿主编辑器判定。
    /// 坐标以画布局部坐标（content 原点）为准，避免屏幕↔画布换算误差。
    /// </summary>
    public class NodePin : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public GraphEditorPanel editor;      // 宿主节点编辑器
        public string node_id;
        public string pin_id;
        public bool is_output;
        public RectTransform node_rect;      // 所属节点根
        public Vector2 offset;               // 引脚相对节点左下角的偏移（画布局部）

        public void Setup(GraphEditorPanel editor, string node_id, string pin_id, bool is_output,
            RectTransform node_rect, Vector2 offset)
        {
            this.editor = editor;
            this.node_id = node_id;
            this.pin_id = pin_id;
            this.is_output = is_output;
            this.node_rect = node_rect;
            this.offset = offset;
        }

        /// <summary>引脚在画布局部坐标（content 原点为基准）</summary>
        public Vector2 GetCanvasPos()
        {
            if (node_rect == null)
                return Vector2.zero;
            return node_rect.anchoredPosition + offset;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (editor != null)
                editor.OnPinDragBegin(this, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (editor != null)
                editor.OnPinDrag(this, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (editor != null)
                editor.OnPinDragEnd(this, eventData);
        }
    }
}
