using UnityEngine;
using UnityEngine.EventSystems;

namespace TcgEngine.UI
{
    /// <summary>
    /// 连线画布控制器：挂在画布背景（canvas_bg）上。
    /// 滚轮缩放节点容器 content，左键拖拽空白平移 content，供 +/- 按钮调用 ZoomIn/ZoomOut。
    /// </summary>
    public class GraphCanvas : MonoBehaviour, IScrollHandler, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        public RectTransform content;    // 节点容器（大画布）
        public float zoom_min = 0.3f;
        public float zoom_max = 2.5f;
        public float zoom_step = 0.12f;

        private bool dragging = false;

        public void OnScroll(PointerEventData eventData)
        {
            Zoom(eventData.scrollDelta.y > 0 ? zoom_step : -zoom_step);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            //仅当拖拽起点在画布空白区（content 之外）才平移；
            //从节点/引脚（content 子对象）发起的拖拽交给 NodeDragger/NodePin 处理，
            //事件会冒泡到这里，不能误平移画布。
            dragging = false;
            if (content == null || eventData == null)
                return;
            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            if (hit == null || hit.transform.IsChildOf(content))
                return;
            dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (content == null || !dragging)
                return;
            //增量平移，不做坐标原点换算（避免缩放/偏移误差）；
            //除以 localScale 使缩放后拖动手感与画面位移一致
            float scale = content.localScale.x > 0.001f ? content.localScale.x : 1f;
            content.anchoredPosition += eventData.delta / scale;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
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
