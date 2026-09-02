using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 节点引脚组件：挂在节点上的输入/输出引脚小圆点。
    /// 从输出引脚拖拽到输入引脚建立 GraphLink；松手位置由宿主编辑器判定。
    /// 坐标以画布局部坐标（content 原点）为准，避免屏幕↔画布换算误差。
    /// 输入引脚附带可点数值框：固定值（如 = 5）或接取值线后显示 ← 来源（规格第5节）。
    /// </summary>
    public class NodePin : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public GraphEditorPanel editor;      // 宿主节点编辑器
        public string node_id;
        public string pin_id;
        public bool is_output;
        public RectTransform node_rect;      // 所属节点根
        public Vector2 offset;               // 引脚相对节点左下角的偏移（画布局部）
        public Vector2 original_offset;      // 展开时的原始偏移（收起/展开恢复用）
        public Text value_label;             // 内联数值框（仅输入口）：= 固定值 / ← 来源

        public void Setup(GraphEditorPanel editor, string node_id, string pin_id, bool is_output,
            RectTransform node_rect, Vector2 offset)
        {
            this.editor = editor;
            this.node_id = node_id;
            this.pin_id = pin_id;
            this.is_output = is_output;
            this.node_rect = node_rect;
            this.offset = offset;
            this.original_offset = offset;
        }

        /// <summary>刷新内联数值框：显示「端口名 = 固定值」或「端口名 ← 来源」（规格第3/5节）</summary>
        public void RefreshValueLabel()
        {
            if (value_label == null || editor == null || editor.Graph == null)
                return;
            GraphData graph = editor.Graph;
            GraphNode node = graph.GetNode(node_id);
            GraphPin pin = graph.GetPin(node_id, pin_id);
            if (node == null || pin == null)
                return;

            string label = string.IsNullOrEmpty(pin.display_name) ? pin.name : pin.display_name;
            GraphLink in_link = graph.GetIncomingLink(node_id, pin_id);
            if (in_link != null)
            {
                GraphNode src = graph.GetNode(in_link.from_node);
                string s = src != null ? src.title : "?";
                value_label.text = label + " ← " + (s.Length > 5 ? s.Substring(0, 5) : s);
                value_label.color = new Color(0.6f, 0.63f, 0.7f, 1f);   //灰：自动来源
            }
            else
            {
                string v = "";
                foreach (FieldCustomData f in node.fields)
                {
                    if (f.name == pin.name)
                    {
                        v = f.value;
                        break;
                    }
                }
                value_label.text = label + " = " + v;
                value_label.color = GraphEditorPanel.PinColor(pin);
            }
        }

        /// <summary>重设引脚局部偏移（节点收起时并到迷你锚点，展开时恢复原始偏移）</summary>
        public void SetLocalOffset(Vector2 off)
        {
            offset = off;
            RectTransform rt = GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = off;
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
