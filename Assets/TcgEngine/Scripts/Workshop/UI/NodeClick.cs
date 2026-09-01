using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TcgEngine.UI
{
    /// <summary>
    /// 节点点击选中组件：挂在节点根 RectTransform 上，
    /// 单击左键回调宿主选中该节点（拖拽仍由 NodeDragger 处理，二者不冲突）。
    /// </summary>
    public class NodeClick : MonoBehaviour, IPointerClickHandler
    {
        private string node_id;
        private Action<string> on_click;

        public void Setup(string node_id, Action<string> on_click)
        {
            this.node_id = node_id;
            this.on_click = on_click;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (on_click != null)
                on_click(node_id);
        }
    }
}
