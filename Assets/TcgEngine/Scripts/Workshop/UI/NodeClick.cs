using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TcgEngine.UI
{
    /// <summary>
    /// 节点点击选中 + 悬停细目组件：挂在节点根 RectTransform 上。
    /// 单击左键回调宿主选中该节点（拖拽仍由 NodeDragger 处理，二者不冲突）；
    /// 悬停进入/退出回调宿主显示/隐藏收起节点的「入N条 · 出M条」提示（规格第4节）。
    /// </summary>
    public class NodeClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private string node_id;
        private Action<string> on_click;
        private Action<string> on_enter;
        private Action<string> on_exit;

        public void Setup(string node_id, Action<string> on_click, Action<string> on_enter = null, Action<string> on_exit = null)
        {
            this.node_id = node_id;
            this.on_click = on_click;
            this.on_enter = on_enter;
            this.on_exit = on_exit;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (on_click != null)
                on_click(node_id);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (on_enter != null)
                on_enter(node_id);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (on_exit != null)
                on_exit(node_id);
        }
    }
}
