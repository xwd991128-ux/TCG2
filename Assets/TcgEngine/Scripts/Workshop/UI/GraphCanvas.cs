using UnityEngine;
using UnityEngine.EventSystems;

namespace TcgEngine.UI
{
    /// <summary>
    /// 连线画布控制器：挂在画布背景（canvas_bg）上。
    /// 滚轮缩放节点容器 content；**左键在空白处拖拽=框选节点**（编辑器接 onRubberBand*），
    /// **中键/右键拖拽=平移 content**；供 +/- 按钮调用 ZoomIn/ZoomOut。
    /// </summary>
    public class GraphCanvas : MonoBehaviour, IScrollHandler, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public RectTransform content;    // 节点容器（大画布）
        public float zoom_min = 0.3f;
        public float zoom_max = 2.5f;
        public float zoom_step = 0.12f;

        /// <summary>点击画布空白处回调（编辑器绑定：取消节点选中）</summary>
        public System.Action onCanvasClick;

        /// <summary>★框选：拖拽中回调（起点/当前 均为屏幕坐标），编辑器据此绘制选框</summary>
        public System.Action<Vector2, Vector2> onRubberBandDrag;
        /// <summary>★框选：松手回调（起点/终点 均为屏幕坐标），编辑器据此选中相交节点</summary>
        public System.Action<Vector2, Vector2> onRubberBandEnd;

        private bool dragging = false;
        private bool selecting = false;
        private Vector2 select_start;

        /// <summary>点击空白（content 之外）→ 通知编辑器取消节点选中；点在节点/引脚/连线上不处理</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (content == null || eventData == null)
                return;
            if (eventData.button != PointerEventData.InputButton.Left)   //右键已用于平移，不再当"取消选中"
                return;
            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            if (hit != null && hit.transform.IsChildOf(content))
                return;
            if (onCanvasClick != null)
                onCanvasClick();
        }

        public void OnScroll(PointerEventData eventData)
        {
            Zoom(eventData.scrollDelta.y > 0 ? zoom_step : -zoom_step);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            //分工：
            //  · 中键/右键拖 → 平移画布（左键腾出来给"框选"，旧版"拖空白平移"的手感保留在这里）
            //  · 左键拖在节点/引脚上 → 交给 NodeDragger/NodePin（事件会冒泡到这里，不能误框选/误平移）
            //  · 左键拖在空白上 → **框选**（编辑器接了 onRubberBand* 时）；未接则退回原来的平移
            dragging = false;
            selecting = false;
            if (content == null || eventData == null)
                return;

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                dragging = true;
                return;
            }

            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            if (hit != null && hit.transform.IsChildOf(content))
                return;

            if (onRubberBandDrag != null)
            {
                selecting = true;
                select_start = eventData.position;
                onRubberBandDrag(select_start, select_start);
            }
            else
            {
                dragging = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null)
                return;
            if (selecting)
            {
                if (onRubberBandDrag != null)
                    onRubberBandDrag(select_start, eventData.position);
                return;
            }
            if (content == null || !dragging)
                return;
            //增量平移，不做坐标原点换算（避免缩放/偏移误差）；
            //除以 localScale 使缩放后拖动手感与画面位移一致
            float scale = content.localScale.x > 0.001f ? content.localScale.x : 1f;
            content.anchoredPosition += eventData.delta / scale;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (selecting && onRubberBandEnd != null && eventData != null)
                onRubberBandEnd(select_start, eventData.position);
            selecting = false;
            dragging = false;
        }

        public void ZoomIn()
        {
            Zoom(zoom_step);
        }

        public void ZoomOut()
        {
            Zoom(-zoom_step);
        }

        private void Zoom(float delta)
        {
            if (content == null)
                return;
            float scale = Mathf.Clamp(content.localScale.x + delta, zoom_min, zoom_max);
            content.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
