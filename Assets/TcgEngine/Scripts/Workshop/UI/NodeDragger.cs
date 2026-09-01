using UnityEngine;
using UnityEngine.EventSystems;
using System;

namespace TcgEngine.UI
{
    /// <summary>
    /// 节点行拖拽组件（P2 画布交互）。
    /// 挂在每个节点行的根 RectTransform 上，按屏幕增量驱动节点移动（IDragHandler.delta），
    /// 拖拽结束回调写回 GraphNode.pos。
    /// 通过委托回调与宿主面板解耦（CardEditorPanel / GraphEditorPanel 均可用）。
    /// </summary>
    public class NodeDragger : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        private string node_id;                          // 所属 GraphNode.id
        private Action<string, RectTransform, PointerEventData> on_drag;
        private Action<string, RectTransform> on_end;
        private RectTransform rect;

        public void Setup(string node_id, Action<string, RectTransform, PointerEventData> on_drag,
            Action<string, RectTransform> on_end)
        {
            this.node_id = node_id;
            this.on_drag = on_drag;
            this.on_end = on_end;
            rect = GetComponent<RectTransform>();
        }

        /// <summary>兼容旧宿主（CardEditorPanel）</summary>
        public void Setup(string node_id, CardEditorPanel editor)
        {
            Setup(node_id, editor.MoveNode, (id, r) => editor.OnNodeMoved(id, r.anchoredPosition));
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (on_drag != null && rect != null)
                on_drag(node_id, rect, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (on_end != null && rect != null)
                on_end(node_id, rect);
        }
    }
}
