using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 运行时 UIToolkit 节点画布验证（试验）。
    /// 在游戏画面内用 UIDocument + 动态 PanelSettings 铺一个节点画布，
    /// 验证 画布平移 + 节点摆放 + 端口拖线 在 Runtime UI 可用。
    /// 配合编辑器菜单生成（需在 Play 模式调用），用完直接删对象即可。
    /// </summary>
    public class RuntimeUITKNodeTest : MonoBehaviour
    {
        private const float NODE_W = 160f;
        private const float NODE_H = 50f;

        private UIDocument doc;
        private PanelSettings settings;
        private bool ui_built;

        private VisualElement canvas;
        private VisualElement lines_root;
        private VisualElement content;
        private Vector2 offset;
        private readonly List<ProtNode> nodes = new List<ProtNode>();
        private readonly List<VisualElement> link_lines = new List<VisualElement>();
        private VisualElement temp_line;
        private ProtNode drag_node;
        private bool drag_out;
        private Vector2 drag_start;
        private bool drag_pan;
        private Vector3 pan_start_mouse;
        private Vector2 pan_start_offset;

        private class ProtNode
        {
            public string id;
            public float x, y;
            public VisualElement root;
        }

        private void OnEnable()
        {
            doc = GetComponent<UIDocument>();
            if (doc == null)
                doc = gameObject.AddComponent<UIDocument>();
            //运行时直接加载编辑器生成的 PanelSettings（含主题与排序），见编辑器菜单"生成 UITK 运行时面板资源"
            settings = Resources.Load<PanelSettings>("UITK/PrototypePanel");
            if (settings == null)
            {
                Debug.LogWarning("[UITK] 缺少 Resources/UITK/PrototypePanel.asset，请先在编辑器菜单 TcgEngine→工具→生成 UITK 运行时面板资源");
                enabled = false;
                return;
            }
            doc.panelSettings = settings;
        }

        private void Start()
        {
            BuildOnce();
        }

        private void Update()
        {
            if (!ui_built)
                BuildOnce();   //根节点就绪后再建（UIDocument 的 rootVisualElement 可能晚一帧才可用）
        }

        private void BuildOnce()
        {
            if (ui_built)
                return;
            if (doc == null || doc.rootVisualElement == null)
            {
                if (!log_wait)
                {
                    log_wait = true;
                    Debug.LogWarning("[UITK] UIDocument 根节点一直未就绪，未能构建 UI");
                }
                return;
            }
            Build();
            ui_built = true;
            Debug.Log("[UITK] UI 构建完成，根节点子元素数=" + doc.rootVisualElement.childCount);
        }

        private bool log_wait;

        private void Build()
        {
            if (doc == null || doc.rootVisualElement == null)
                return;
            VisualElement root = doc.rootVisualElement;
            root.Clear();

            VisualElement bar = new VisualElement();
            bar.style.height = 44;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.paddingLeft = 10;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = new Color(0.18f, 0.19f, 0.22f, 1f);
            root.Add(bar);
            bar.Add(MakeButton("+ Node", () => AddNode(Random.Range(-120f, 120f), Random.Range(-80f, 80f))));
            bar.Add(MakeButton("Close", () => Destroy(gameObject)));

            canvas = new VisualElement();
            canvas.style.flexGrow = 1;
            canvas.style.backgroundColor = new Color(0.09f, 0.1f, 0.13f, 1f);
            canvas.style.overflow = Overflow.Hidden;
            root.Add(canvas);

            lines_root = NewLayer(canvas);
            content = NewLayer(canvas);

            canvas.RegisterCallback<PointerDownEvent>(e =>
            {
                canvas.CapturePointer(e.pointerId);
                drag_pan = true;
                pan_start_mouse = e.localPosition;
                pan_start_offset = offset;
                e.StopPropagation();
            });
            canvas.RegisterCallback<PointerMoveEvent>(OnCanvasMove);
            canvas.RegisterCallback<PointerUpEvent>(OnCanvasUp);
            canvas.RegisterCallback<PointerCancelEvent>(e => { drag_pan = false; drag_out = false; drag_node = null; });

            AddNode(-80, -20);
            AddNode(220, 90);
            Connect(nodes[0], nodes[1]);
        }

        private static VisualElement NewLayer(VisualElement parent)
        {
            VisualElement layer = new VisualElement();
            layer.style.position = Position.Absolute;
            layer.style.width = 4000;
            layer.style.height = 4000;
            layer.pickingMode = PickingMode.Ignore;
            parent.Add(layer);
            return layer;
        }

        private static Button MakeButton(string text, System.Action click)
        {
            Button b = new Button();
            b.text = text;
            b.style.marginRight = 8;
            b.clicked += click;
            return b;
        }

        private void AddNode(float x, float y)
        {
            ProtNode n = new ProtNode { id = "n" + nodes.Count, x = x, y = y };
            VisualElement node = new VisualElement();
            node.style.position = Position.Absolute;
            node.style.width = NODE_W;
            node.style.height = NODE_H;
            node.style.left = x;
            node.style.top = y;
            node.style.backgroundColor = new Color(0.17f, 0.24f, 0.33f, 1f);
            node.style.borderTopLeftRadius = 6;
            node.style.borderTopRightRadius = 6;
            node.style.borderBottomLeftRadius = 6;
            node.style.borderBottomRightRadius = 6;
            node.pickingMode = PickingMode.Ignore;

            Label title = new Label("Node " + n.id);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.height = 28;
            title.style.color = Color.white;
            node.Add(title);

            MakePort(node, new Color(0.4f, 1f, 0.6f), -9, false);
            MakePort(node, new Color(1f, 0.7f, 0.3f), NODE_W - 9, true);

            content.Add(node);
            n.root = node;
            nodes.Add(n);
        }

        private void MakePort(VisualElement node, Color color, float left, bool is_out)
        {
            VisualElement port = new VisualElement();
            port.style.position = Position.Absolute;
            port.style.width = 18;
            port.style.height = 18;
            port.style.left = left;
            port.style.top = 16;
            port.style.backgroundColor = color;
            port.style.borderTopLeftRadius = 9;
            port.style.borderTopRightRadius = 9;
            port.style.borderBottomLeftRadius = 9;
            port.style.borderBottomRightRadius = 9;
            node.Add(port);

            port.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!is_out)
                    return;
                ProtNode owner = nodes.Find(p => p.root == node);
                if (owner == null)
                    return;
                canvas.CapturePointer(e.pointerId);
                drag_node = owner;
                drag_out = true;
                drag_start = new Vector2(owner.x + NODE_W, owner.y + 25);
                if (temp_line == null)
                {
                    temp_line = MakeLine(Color.white);
                    lines_root.Add(temp_line);
                }
                SetLine(temp_line, drag_start.x, drag_start.y, drag_start.x, drag_start.y);
                temp_line.style.display = DisplayStyle.Flex;
                e.StopPropagation();
            });
        }

        private VisualElement MakeLine(Color color)
        {
            VisualElement line = new VisualElement();
            line.style.position = Position.Absolute;
            line.style.height = 3;
            line.style.backgroundColor = color;
            line.pickingMode = PickingMode.Ignore;
            line.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(50), 0);
            return line;
        }

        private Vector2 CanvasToWorld(Vector2 local) => new Vector2(local.x + offset.x, local.y + offset.y);

        private void OnCanvasMove(PointerMoveEvent e)
        {
            if (drag_out && drag_node != null)
            {
                Vector2 end = CanvasToWorld(e.localPosition);
                SetLine(temp_line, drag_start.x, drag_start.y, end.x, end.y);
                e.StopPropagation();
                return;
            }
            if (drag_pan)
            {
                Vector3 d3 = e.localPosition - pan_start_mouse;
                offset = pan_start_offset - new Vector2(d3.x, d3.y);
                offset.x = Mathf.Clamp(offset.x, -3800, 0);
                offset.y = Mathf.Clamp(offset.y, -3800, 0);
                if (content != null) { content.style.left = -offset.x; content.style.top = -offset.y; }
                if (lines_root != null) { lines_root.style.left = -offset.x; lines_root.style.top = -offset.y; }
                e.StopPropagation();
            }
        }

        private void OnCanvasUp(PointerUpEvent e)
        {
            if (drag_node != null && drag_out)
            {
                ProtNode best = null;
                float best_dist = 55f;
                Vector2 end = CanvasToWorld(e.localPosition);
                foreach (ProtNode n in nodes)
                {
                    if (n == drag_node)
                        continue;
                    Vector2 in_pos = new Vector2(n.x, n.y + 25);
                    float d = Vector2.Distance(end, in_pos);
                    if (d < best_dist)
                    {
                        best_dist = d;
                        best = n;
                    }
                }
                if (best != null)
                    Connect(drag_node, best);
                if (temp_line != null)
                    temp_line.style.display = DisplayStyle.None;
            }
            drag_pan = false;
            drag_out = false;
            drag_node = null;
        }

        private void Connect(ProtNode from, ProtNode to)
        {
            VisualElement line = MakeLine(new Color(0.55f, 0.78f, 1f, 1f));
            SetLine(line, from.x + NODE_W, from.y + 25, to.x, to.y + 25);
            lines_root.Add(line);
            link_lines.Add(line);
        }

        private static void SetLine(VisualElement line, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            line.style.left = x1;
            line.style.top = y1;
            line.style.width = Mathf.Max(1f, len);
            float deg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            line.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
        }

        private void OnDestroy()
        {
            if (settings != null)
                Destroy(settings);
        }
    }
}
