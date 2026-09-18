using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TcgEngine;
using TcgEngine.Client;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡牌编辑器面板（P1：外壳 + 属性表单区 + 平铺节点展示）。
    /// 由 Editor 工具（CardEditorBuilder）在 Menu.unity 中生成并绑定 UI 引用，
    /// 本类运行时不动态创建界面，仅负责交互逻辑（符合项目工程约定）。
    /// P1 里程碑：能打开一个本地卡池 → 展示属性表单与平铺节点 → 保存写回 → 模拟执行验证。
    /// </summary>
    public class CardEditorPanel : UIPanel
    {
        [Header("标题/状态")]
        public Text title_text;              // 面板标题
        public Text status_text;             // 底部状态提示
        public Text file_text;               // 当前编辑的卡池文件路径

        [Header("卡牌列表区")]
        public ScrollRect card_scroll;       // 卡牌列表滚动区
        public RectTransform card_content;   // 卡牌列表容器
        public CardGrid card_grid;           // 卡牌网格（GridLayoutGroup）
        public Button btn_add_card;          // 新增卡按钮
        public Button btn_copy;              // 复制选中卡
        public Button btn_delete;            // 删除选中卡
        public Text editor_hint;             // 右侧编辑区占位提示
        public GameObject card_list_root;    // 卡牌列表区根（按钮模式下隐藏）
        public GameObject editor_area_root;  // 右侧属性编辑区根（按钮模式下隐藏）
        private GameObject card_prefab;      // 卡面预制体（运行时从卡组构筑界面复制）
        private bool update_grid = false;    // 待刷新网格高度
        private float update_grid_timer = 0f;

        [Header("属性表单区")]
        public InputField input_name;        // 卡池名称
        public InputField input_desc;        // 卡池描述
        public InputField input_author;      // 作者

        [Header("平铺节点列表（P2 画布）")]
        public ScrollRect node_scroll;       // 节点滚动区
        public RectTransform node_content;   // 节点容器（画布）
        public GameObject node_template;     // 节点行模板（隐藏，运行时复制）

        [Header("工具栏")]
        public Button btn_save;              // 保存
        public Button btn_test;              // 模拟运行测试
        public Button btn_go;                // （已废弃）原「进行」：功能并入左下角「编辑」，生成工具不再创建，仅兼容旧场景时会被隐藏
        public Button btn_buff;              // 增益：进入增益编辑器（BuffPanel，增益与卡池平级的全局资源）
        public Button btn_buttons;           // 按钮：切换进入内嵌的战斗按钮编辑器
        public Button btn_close;             // 关闭（返回卡池管理）
        public Button btn_save2;             // 卡牌列表底部保存按钮

        [Header("布局（运行时归位：左下操作栏 + 右侧变量配置列）")]
        public Button btn_edit_card;         // 编辑（重命名选中卡）——缺省时运行时创建
        public Button btn_trait;             // 种族——缺省时运行时创建
        public Button btn_keyword;           // 关键词——缺省时运行时创建
        public RectTransform bottom_action_bar;   // 左下角操作栏（运行时创建）
        public RectTransform side_config_bar;     // 右侧变量配置列（运行时创建）

        [Header("按钮编辑器（内嵌）")]
        public GameObject button_editor_root;    // 按钮编辑区根（默认隐藏，点「按钮」切换显示）
        public ScrollRect button_list_scroll;    // 按钮列表滚动区
        public RectTransform button_list_content;// 按钮列表容器
        public GameObject button_list_template;  // 按钮列表项模板（隐藏）
        public InputField btn_id_input;          // 按钮 ID（规则图 button_id 匹配用）
        public InputField btn_title_input;       // 按钮显示文本
        public InputField btn_desc_input;        // 按钮描述（可选）
        public Button btn_button_new;            // 新增按钮
        public Button btn_button_copy;           // 复制按钮
        public Button btn_button_del;            // 删除按钮
        public Button btn_button_save;           // 保存按钮（写盘 buttons.json）
        public Button btn_button_edit_graph;     // 编辑规则图（进入全屏规则编辑器编辑共享按钮图）
        public Button btn_button_back;           // 返回卡牌编辑模式

        private static CardEditorPanel instance;
        private string current_path;         // 当前编辑的本地卡池文件完整路径
        private CardPoolData current_pool;   // 当前编辑的卡池数据（含图）
        private CardCustomData current_card; // 当前选中的卡牌

        private readonly List<CardLine> card_lines = new List<CardLine>();   // 卡牌列表行

        private BattleButtonData editing_button;                  // 按钮编辑器：当前编辑的按钮定义
        private string selected_button_id;                        // 按钮编辑器：列表选中的按钮 id
        private readonly Dictionary<string, Image> button_row_imgs = new Dictionary<string, Image>();  // 按钮列表项高亮

        private CanvasRect canvas;           // 画布容器（放节点，内含节点映射表）
        private readonly float node_h = 40f;

        private class CardLine
        {
            public CardCustomData card;
            public RectTransform rect;
            public CollectionCard ccard;
            public Image highlight;          // 选中高亮标记
        }

        public static CardEditorPanel Get() { return instance; }
        public string FilePath { get { return current_path; } }
        public CardPoolData CurrentPool { get { return current_pool; } }

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            if (btn_save != null) btn_save.onClick.AddListener(OnSave);
            if (btn_save2 != null) btn_save2.onClick.AddListener(OnSave);
            if (btn_test != null) btn_test.onClick.AddListener(OnTest);
            if (btn_go != null) btn_go.onClick.AddListener(OnGo);
            //变量配置四入口（增益/种族/关键词/按钮）：统一走「选择弹框」，不再直接跳转旧的管理页面。
            //先清掉 Builder 时代挂上的旧监听，避免"旧入口残留"（同一按钮有两个行为）。
            if (btn_buff != null) btn_buff.onClick.RemoveAllListeners();
            if (btn_buttons != null) btn_buttons.onClick.RemoveAllListeners();
            if (btn_buff != null) btn_buff.onClick.AddListener(() => OpenVariablePopup(VariableSelectPopup.Kind.Buff));
            if (btn_buttons != null) btn_buttons.onClick.AddListener(() => OpenVariablePopup(VariableSelectPopup.Kind.Button));
            ConfigVariablePopup();
            if (btn_close != null) btn_close.onClick.AddListener(OnClose);
            if (btn_add_card != null) btn_add_card.onClick.AddListener(OnAddCard);
            if (btn_copy != null) btn_copy.onClick.AddListener(OnCopyCard);
            if (btn_delete != null) btn_delete.onClick.AddListener(OnDeleteCard);

            //按钮编辑器（内嵌）
            if (btn_button_new != null) btn_button_new.onClick.AddListener(OnButtonNew);
            if (btn_button_copy != null) btn_button_copy.onClick.AddListener(OnButtonCopy);
            if (btn_button_del != null) btn_button_del.onClick.AddListener(OnButtonDel);
            if (btn_button_save != null) btn_button_save.onClick.AddListener(OnButtonSave);
            if (btn_button_edit_graph != null) btn_button_edit_graph.onClick.AddListener(OnButtonEditGraph);
            if (btn_button_back != null) btn_button_back.onClick.AddListener(OnButtonBack);
            if (btn_id_input != null) btn_id_input.onValueChanged.AddListener(v => { if (editing_button != null) editing_button.id = v; });
            if (btn_title_input != null) btn_title_input.onValueChanged.AddListener(v => { if (editing_button != null) editing_button.title = v; });
            if (btn_desc_input != null) btn_desc_input.onValueChanged.AddListener(v => { if (editing_button != null) editing_button.desc = v; });

            ApplyEditorLayout();   //把散落的按钮归位成「左下操作栏 + 右侧变量配置列」
        }

        /// <summary>
        /// 移除变量配置列右上角的 ×（需求变更：不再收起/展开该列）。
        /// 运行时建的按钮叫 SideCloseBtn，场景里可能还存着旧副本 → 两处都按名字扫：先 SetActive(false)
        /// （立刻不可见，Destroy 要到帧末才生效），再 Destroy；最后强制把整列恢复为"展开"，避免之前被收起过。
        /// </summary>
        private void RemoveSideCloseButton()
        {
            Transform parent = editor_area_root != null ? editor_area_root.transform : transform;
            for (int pass = 0; pass < 2; pass++)
            {
                Transform root = pass == 0 ? parent : transform;
                if (root == null)
                    continue;
                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    Transform ch = root.GetChild(i);
                    if (ch == null || ch.name != "SideCloseBtn")
                        continue;
                    ch.gameObject.SetActive(false);
                    Destroy(ch.gameObject);
                    Debug.Log("[变量配置] 已移除右上角的 ×（按需求不再收起该列）");
                }
            }
            side_config_visible = true;
            ApplySideConfigVisible();   //保证列处于展开状态
        }

        // ================= 卡池编辑器 → 按钮编辑器（「按钮」弹框里点「编辑」走这里） =================
        // 注：下面这批"方块按钮行"的构建方法已弃用（按钮栏显示在**对战界面**，见 GameUI.EnsureBattleBar()），
        //     但 **OpenButtonEditor** 是当前唯一的入口方法，仍在使用（弹框「编辑」→ 按钮编辑器）。

        private Button btn_row_toggle;            //左侧方块（文字随状态：按钮 →  /  按钮 ←）
        private RectTransform btn_row_root;       //整行（方块 + 可横向滚动的按钮列表）
        private RectTransform btn_row_content;    //方形按钮容器（HorizontalLayoutGroup）
        private readonly List<Button> btn_row_squares = new List<Button>();
        private readonly List<string> btn_row_ids = new List<string>();
        private string btn_row_selected;          //当前选中的按钮 id（高亮用）
        private bool btn_row_expanded;            //是否展开

        /// <summary>
        /// 构建「按钮」栏（幂等）：左侧方块 = 展开/收起开关（箭头方向随状态变），
        /// 右侧一排**正方形**按钮 = buttons.json 的全部按钮（按序平铺、可横向滚动、选中高亮），
        /// 末尾一个「＋」= 新建按钮。默认收起，点方块展开。
        /// </summary>
        private void EnsureButtonRow(Font font)
        {
            Transform parent = editor_area_root != null ? editor_area_root.transform : transform;
            if (btn_row_root == null)
            {
                GameObject row_go = new GameObject("ButtonRow", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
                btn_row_root = row_go.GetComponent<RectTransform>();
                btn_row_root.SetParent(parent, false);
                //位置：左下角的空白带（避开上方 4 个变量配置按钮占用的区域，也避开底部状态栏）
                btn_row_root.anchorMin = new Vector2(0f, 0f);
                btn_row_root.anchorMax = new Vector2(1f, 0f);
                btn_row_root.pivot = new Vector2(0.5f, 0f);
                btn_row_root.offsetMin = new Vector2(16f, 52f);
                btn_row_root.offsetMax = new Vector2(-88f, 100f);   //右端留 72px 给 ×
                Image row_bg = row_go.GetComponent<Image>();
                row_bg.color = new Color(1f, 1f, 1f, 0.06f);
                row_bg.raycastTarget = false;

                ScrollRect sr = row_go.GetComponent<ScrollRect>();
                sr.horizontal = true;
                sr.vertical = false;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.scrollSensitivity = 24f;

                GameObject view_go = new GameObject("Viewport", typeof(RectTransform));
                RectTransform view = view_go.GetComponent<RectTransform>();
                view.SetParent(btn_row_root, false);
                view.anchorMin = Vector2.zero;
                view.anchorMax = Vector2.one;
                view.offsetMin = new Vector2(66f, 4f);   //左端 66 留给方块
                view.offsetMax = new Vector2(-4f, -4f);
                view_go.AddComponent<RectMask2D>();
                sr.viewport = view;

                GameObject content_go = new GameObject("Content", typeof(RectTransform));
                btn_row_content = content_go.GetComponent<RectTransform>();
                btn_row_content.SetParent(view, false);
                btn_row_content.anchorMin = new Vector2(0f, 0f);
                btn_row_content.anchorMax = new Vector2(0f, 1f);
                btn_row_content.pivot = new Vector2(0f, 0.5f);
                btn_row_content.anchoredPosition = Vector2.zero;
                btn_row_content.sizeDelta = Vector2.zero;
                HorizontalLayoutGroup hlg = content_go.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6f;
                hlg.padding = new RectOffset(2, 2, 2, 2);
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;    //★ 否则方块上的 LayoutElement 被忽略
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
                ContentSizeFitter csf = content_go.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                sr.content = btn_row_content;

                //左侧方块（展开/收起开关，始终可见）
                btn_row_toggle = CreateLayoutButton("BtnRowToggle", "按钮 →", ColPurple, font, 20);
                if (btn_row_toggle != null)
                {
                    RectTransform trt = btn_row_toggle.GetComponent<RectTransform>();
                    trt.SetParent(btn_row_root, false);
                    trt.anchorMin = new Vector2(0f, 0.5f);
                    trt.anchorMax = new Vector2(0f, 0.5f);
                    trt.pivot = new Vector2(0f, 0.5f);
                    trt.anchoredPosition = new Vector2(2f, 0f);
                    trt.sizeDelta = new Vector2(58f, 36f);
                    btn_row_toggle.onClick.RemoveAllListeners();
                    btn_row_toggle.onClick.AddListener(ToggleButtonRow);
                }
                Debug.Log("[按钮栏] 构建完成：方块(展开/收起) + 一排方形按钮（buttons.json）");
            }

            if (btn_row_root != null)
                btn_row_root.gameObject.SetActive(true);
            ApplyButtonRowExpanded();
            RefreshButtonRowSquares();
        }

        private void ToggleButtonRow()
        {
            btn_row_expanded = !btn_row_expanded;
            ApplyButtonRowExpanded();
            SetStatus(btn_row_expanded ? "按钮栏：已展开（点某个方块进入按钮编辑器）" : "按钮栏：已收起");
        }

        /// <summary>箭头与可见性随状态变：收起=「按钮 →」（点它会向右展开），展开=「按钮 ←」（点击收起）</summary>
        private void ApplyButtonRowExpanded()
        {
            if (btn_row_content != null)
                btn_row_content.gameObject.SetActive(btn_row_expanded);
            if (btn_row_toggle != null)
            {
                TMPro.TMP_Text t = btn_row_toggle.GetComponentInChildren<TMPro.TMP_Text>(true);   //本文件没有 using TMPro，按既有写法用全限定名
                if (t != null)
                    t.text = btn_row_expanded ? "按钮 ←" : "按钮 →";
            }
        }

        /// <summary>重建方形按钮（幂等；先脱层再 Destroy，避免重复/延迟销毁导致的重影）</summary>
        private void RefreshButtonRowSquares()
        {
            if (btn_row_content == null)
                return;
            for (int i = btn_row_content.childCount - 1; i >= 0; i--)
            {
                Transform c = btn_row_content.GetChild(i);
                if (c == null)
                    continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
            btn_row_squares.Clear();
            btn_row_ids.Clear();

            List<BattleButtonData> list = BattleButtonIO.GetAll();
            for (int i = 0; i < list.Count; i++)
            {
                BattleButtonData b = list[i];
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                CreateButtonSquare(b, false);
            }
            CreateButtonSquare(null, true);      //末尾「＋」= 新建按钮
            LayoutRebuilder.ForceRebuildLayoutImmediate(btn_row_content);
            RefreshButtonRowSelected();
        }

        private void CreateButtonSquare(BattleButtonData b, bool is_add)
        {
            string label = is_add ? "＋" : ShortButtonLabel(b);
            Button btn = CreateLayoutButton(is_add ? "BtnSquareAdd" : ("BtnSquare_" + b.id), label,
                is_add ? ColGreen : ColBlue, UiFont(), 18);
            if (btn == null)
                return;
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.SetParent(btn_row_content, false);
            rt.sizeDelta = new Vector2(38f, 38f);          //正方形
            LayoutElement le = rt.GetComponent<LayoutElement>();
            if (le == null)
                le = rt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 38f;
            le.preferredWidth = 38f;
            le.minHeight = 38f;
            le.preferredHeight = 38f;

            btn.onClick.RemoveAllListeners();
            if (is_add)
            {
                btn.onClick.AddListener(() =>
                {
                    BattleButtonData nb = BattleButtonIO.New();
                    BattleButtonIO.SaveAll();
                    btn_row_selected = nb != null ? nb.id : null;
                    btn_row_expanded = true;
                    ApplyButtonRowExpanded();
                    RefreshButtonRowSquares();
                    SetStatus("已新建按钮：" + (nb != null ? nb.GetTitle() : "") + "（点它进入按钮编辑器完善名称/背景/描述）");
                });
            }
            else
            {
                string id = b.id;
                btn.onClick.AddListener(() => OpenButtonEditor(id));
                btn_row_squares.Add(btn);
                btn_row_ids.Add(id);
            }
        }

        private string ShortButtonLabel(BattleButtonData b)
        {
            string t = b != null ? b.GetTitle() : "";
            if (string.IsNullOrEmpty(t))
                return "按";
            return t.Length > 2 ? t.Substring(0, 2) : t;    //方块只有 38px：取前两个汉字做标识
        }

        private void RefreshButtonRowSelected()
        {
            for (int i = 0; i < btn_row_squares.Count && i < btn_row_ids.Count; i++)
            {
                Button sq = btn_row_squares[i];
                if (sq == null)
                    continue;
                Image img = sq.GetComponent<Image>();
                if (img == null)
                    continue;
                bool on = !string.IsNullOrEmpty(btn_row_selected) && btn_row_ids[i] == btn_row_selected;
                img.color = on ? new Color(0.55f, 0.55f, 0.55f, 0.95f) : UITheme.Ctrl;   //黑白灰：选中=中灰高亮
            }
        }

        /// <summary>
        /// 进入**按钮编辑器**（规则编辑器面板的按钮模式：右列第一个 Tab = 按钮参数），并记住选中项。
        /// 入口：①「按钮」选择弹框里的「编辑」；② 对战界面按钮栏（GameUI）走的是同一套 GameClient 触发，不经这里。
        /// 返回路径：按钮编辑器点「×/返回」→ GraphEditorPanel.OnClose（按钮模式分支）→ NotifyButtonGraphClosed
        ///          → 重新显示卡池编辑器并刷新按钮列表/状态。
        /// </summary>
        private void OpenButtonEditor(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                SetStatus("请先在列表里选中一个按钮再点「编辑」");
                return;
            }
            BattleButtonData b = BattleButtonIO.Get(id);
            if (b == null)
            {
                BattleButtonIO.LoadAll();      //缓存里没有就重新加载一次（可能刚由别的入口新增）
                b = BattleButtonIO.Get(id);
            }
            if (b == null)
            {
                SetStatus("找不到按钮：" + id);
                return;
            }

            GraphEditorPanel panel = GraphEditorPanel.Get();
            if (panel == null)
                panel = FindObjectOfType<GraphEditorPanel>(true);
            if (panel == null)
            {
                SetStatus("未找到按钮编辑器（规则编辑器面板），请先运行「生成规则编辑器页面」工具");
                return;
            }

            //状态同步：记住当前按钮（弹框/旧列表/按钮栏三处选中态一致）
            selected_button_id = id;
            btn_row_selected = id;
            RefreshButtonRowSelected();

            //页面切换：先收干净浮层（选择弹框等），再隐藏卡池编辑器，最后打开按钮编辑器（它内部会 Show 自己）
            VariableSelectPopup.CloseAll();
            Hide();
            panel.OpenButton(b);
            SetStatus("已进入按钮编辑器：" + b.GetTitle() + "（右列「按钮参数」改名称/背景/描述/自定义参数；点 × 返回卡池编辑器）");
        }

        // ================= 变量配置：统一走「选择弹框」（旧式直接跳管理页面已弃用） =================

        /// <summary>把四类入口的宿主回调注入弹框：谁打开编辑器、卡牌当前值、选中回写、变更通知、字体</summary>
        private void ConfigVariablePopup()
        {
            VariableSelectPopup.open_editor = OpenVariableEditor;
            VariableSelectPopup.current_value_of_card = CurrentCardValue;
            VariableSelectPopup.on_picked = PickCardValue;
            VariableSelectPopup.after_changed = AfterVariableChanged;
        }

        /// <summary>打开某类别的选择弹框（增益/种族/关键词/按钮共用一套弹框）</summary>
        private void OpenVariablePopup(VariableSelectPopup.Kind kind)
        {
            if (current_pool == null)
            {
                SetStatus("请先打开一个卡池");
                return;
            }
            VariableSelectPopup.Open(kind);
        }

        /// <summary>入口按钮重挂监听：清掉 Builder 时代的旧行为，只保留"打开弹框"</summary>
        private void RewireVariableButton(Button btn, VariableSelectPopup.Kind kind)
        {
            if (btn == null)
                return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OpenVariablePopup(kind));
        }

        /// <summary>弹框「编辑 / 新增」→ 进入对应编辑器页面并选中该项（四类的差异都在这里收口）</summary>
        private void OpenVariableEditor(VariableSelectPopup.Kind kind, string id)
        {
            switch (kind)
            {
                case VariableSelectPopup.Kind.Buff:
                {
                    BuffPanel panel = BuffPanel.Get();
                    if (panel == null)
                        panel = FindObjectOfType<BuffPanel>(true);
                    if (panel == null)
                    {
                        SetStatus("未找到增益编辑器（BuffPanel）");
                        return;
                    }
                    Hide();
                    panel.return_to = this;    //★ 上一页 = 卡牌编辑器（本页刚被 Hide，编辑器隐藏后要回到这里，否则黑屏）
                    panel.EditBuff(id);        //打开并选中该增益（BuffPanel 仍是"单条增益"的编辑落地页）
                    return;
                }
                case VariableSelectPopup.Kind.Keyword:
                {
                    KeywordPanel panel = KeywordPanel.Get();
                    if (panel == null)
                        panel = FindObjectOfType<KeywordPanel>(true);
                    if (panel == null)
                    {
                        SetStatus("未找到关键词编辑器（KeywordPanel）");
                        return;
                    }
                    Hide();
                    panel.return_to = this;    //★ 同上：关键词编辑器隐藏后回到卡牌编辑器（原来会直接黑屏）
                    panel.EditKeyword(id);
                    return;
                }
                case VariableSelectPopup.Kind.Button:
                    //★ 「编辑」= 进入**按钮编辑器**（规则编辑器面板的按钮模式：右列第一个 Tab = 按钮参数），
                    //  而不是旧的内嵌按钮列表页；返回路径由 GraphEditorPanel.OnClose → NotifyButtonGraphClosed 负责。
                    OpenButtonEditor(id);
                    return;
                case VariableSelectPopup.Kind.Trait:
                    //种族暂无独立编辑页：弹框内直接改标题（见 VariableSelectPopup.OnEditClick）
                    SetStatus("种族没有独立编辑页：可在弹框内改名");
                    return;
            }
        }

        /// <summary>卡牌当前已配置的该项 id（弹框列表默认预选中用；增益/按钮是全局资源 → 无当前项）</summary>
        private string CurrentCardValue(VariableSelectPopup.Kind kind)
        {
            if (current_card == null)
                return null;
            switch (kind)
            {
                case VariableSelectPopup.Kind.Trait:
                {
                    List<string> traits = current_card.EnsureTraits();
                    return traits.Count > 0 ? traits[0] : null;
                }
                case VariableSelectPopup.Kind.Keyword:
                    return current_card.keywords != null && current_card.keywords.Count > 0 ? current_card.keywords[0] : null;
                default:
                    return null;
            }
        }

        /// <summary>弹框单击选中 → 回写卡牌：种族=traits[0]（单选），关键词=移到首位保留其余（卡牌可带多个关键词）</summary>
        private void PickCardValue(VariableSelectPopup.Kind kind, string id)
        {
            if (current_card == null)
            {
                if (!string.IsNullOrEmpty(id))
                    SetStatus("提示：未选中卡牌，本次仅在弹框里选中 " + id + "（选中卡牌后再点可写入卡面）");
                return;
            }
            switch (kind)
            {
                case VariableSelectPopup.Kind.Trait:
                {
                    List<string> traits = current_card.EnsureTraits();
                    if (string.IsNullOrEmpty(id))
                    {
                        traits.Clear();
                        current_card.trait = "";
                        SetStatus("已清除种族（记得保存卡池）");
                        break;
                    }
                    if (traits.Count == 0)
                        traits.Add(id);
                    else
                        traits[0] = id;          //首位=当前种族（双写 trait 由 EnsureTraits 兜底）
                    SetStatus("已设置种族：" + id + "（记得保存卡池）");
                    break;
                }
                case VariableSelectPopup.Kind.Keyword:
                {
                    if (string.IsNullOrEmpty(id))
                        break;
                    if (current_card.keywords == null)
                        current_card.keywords = new List<string>();
                    current_card.keywords.Remove(id);
                    current_card.keywords.Insert(0, id);
                    SetStatus("已设置关键词：" + id + "（共 " + current_card.keywords.Count + " 个，记得保存卡池）");
                    break;
                }
                default:
                    SetStatus("已选中：" + id + "（增益/按钮是全局资源，保存后对所有卡牌生效）");
                    return;
            }
            RefreshCardList();
            OnSelectCardId(current_card.id);
        }

        /// <summary>弹框里增删项之后的刷新（卡面重绘 + 选中态保持）</summary>
        private void AfterVariableChanged()
        {
            if (current_card == null)
                return;
            RefreshCardList();
            OnSelectCardId(current_card.id);
        }

        // ---------------- 右侧列的收起/展开（× 按钮） ----------------

        private Button side_close_btn;
        private bool side_config_visible = true;

        /// <summary>
        /// 变量配置列右上角的 × ：收起/展开整列。
        /// 关键点：① 放在 editor_area_root 内、最后创建并 SetAsLastSibling → 层级最高，不会被四组按钮遮挡（保证可点）；
        ///        ② 按钮列已右收 72px（见 ApplyEditorLayout 的 sizeDelta），与 × 之间留出间距，不再重叠。
        /// </summary>
        private void EnsureSideCloseButton(Font font)
        {
            Transform parent = editor_area_root != null ? editor_area_root.transform : transform;
            Transform exist = parent.Find("SideCloseBtn");
            if (exist != null)
                side_close_btn = exist.GetComponent<Button>();
            if (side_close_btn == null)
            {
                side_close_btn = CreateLayoutButton("SideCloseBtn", "×", ColRed, font, 30);
                if (side_close_btn == null)
                    return;
            }
            RectTransform rt = side_close_btn.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-12f, -12f);
            rt.sizeDelta = new Vector2(44f, 44f);
            side_close_btn.onClick.RemoveAllListeners();
            side_close_btn.onClick.AddListener(ToggleSideConfig);
            if (!side_close_btn.gameObject.activeSelf)
                side_close_btn.gameObject.SetActive(true);
            side_close_btn.transform.SetAsLastSibling();
        }

        /// <summary>
        /// 把"别人的 ×"从变量配置列里挪走：
        /// 场景里的旧关闭按钮（btn_close 等）锚在面板右侧中部，正好压在四个入口按钮上（会出现两个 × 且其中一个重叠）。
        /// 这里把**任何与配置列矩形相交、且看起来是关闭按钮**的 Button 统一停到面板右上角：
        ///   · 保留它的原功能（点了还是关面板/返回），不删对象；
        ///   · 判定：名字含 close（忽略大小写）或按钮文字是 ×/X/✕。
        /// </summary>
        private void ParkForeignCloseButtons()
        {
            if (side_config_bar == null)
                return;
            Rect col = WorldRect(side_config_bar);
            col.xMin -= 8f;
            col.xMax += 8f;
            foreach (Button b in GetComponentsInChildren<Button>(true))
            {
                if (b == null || b == side_close_btn)
                    continue;
                if (!LooksLikeCloseButton(b))
                    continue;
                RectTransform rt = b.GetComponent<RectTransform>();
                if (rt == null || !col.Overlaps(WorldRect(rt)))
                    continue;
                rt.SetParent(transform, false);      //移到面板根部（脱离原容器，避免被原布局再次拉回去）
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-16f, -16f);
                rt.sizeDelta = new Vector2(44f, 44f);
                rt.SetAsLastSibling();
                Debug.Log("[卡牌编辑器] 旧的关闭按钮与「变量配置」列重叠 → 已挪到面板右上角：" + b.name);
            }
        }

        /// <summary>名字或文字看起来像关闭按钮（×/X/✕ 或含 close）。TMP 与旧版 Text 都要看
        /// （页面里两种文本控件并存，只查一种会漏掉旧按钮）。</summary>
        private static bool LooksLikeCloseButton(Button b)
        {
            if (b.name != null && b.name.ToLower().Contains("close"))
                return true;
            TMPro.TMP_Text tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
            string s = tmp != null ? (tmp.text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(s))
            {
                Text legacy = b.GetComponentInChildren<Text>(true);
                s = legacy != null ? (legacy.text ?? "").Trim() : "";
            }
            return s == "×" || s == "X" || s == "x" || s == "✕";
        }

        private static Rect WorldRect(RectTransform rt)
        {
            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        private System.Collections.IEnumerator ParkForeignCloseNextFrame()
        {
            yield return null;
            ParkForeignCloseButtons();
        }

        private void ToggleSideConfig()
        {
            side_config_visible = !side_config_visible;
            ApplySideConfigVisible();
            SetStatus(side_config_visible ? "变量配置：已展开" : "变量配置：已收起（点右上角按钮展开）");
        }

        private void ApplySideConfigVisible()
        {
            Transform parent = editor_area_root != null ? editor_area_root.transform : transform;
            if (side_config_bar != null)
                side_config_bar.gameObject.SetActive(side_config_visible);
            Transform caption = parent.Find("SideCaption");
            if (caption != null)
                caption.gameObject.SetActive(side_config_visible);
            if (editor_hint != null)
                editor_hint.gameObject.SetActive(side_config_visible);
            if (side_close_btn != null)
            {
                Text t = side_close_btn.GetComponentInChildren<Text>(true);
                if (t != null)
                    t.text = side_config_visible ? "×" : "≡";   //收起后图标变成"展开"提示，仍可点
            }
        }

        /// <summary>切换进入内嵌战斗按钮编辑器：隐藏卡牌编辑内容，显示按钮编辑区。
        /// 按钮为全局资源（Workshop/buttons.json），一图多按钮分支组织效果。</summary>
        private void OnOpenButtonEditor()
        {
            if (button_editor_root == null)
            {
                SetStatus("未找到按钮编辑器，请先运行「TcgEngine → 战斗按钮 → 在卡牌编辑器页面添加按钮编辑器」工具");
                return;
            }

            BattleButtonIO.LoadAll();
            SetCardEditVisible(false);
            button_editor_root.SetActive(true);
            RefreshButtonList();

            List<BattleButtonData> all = BattleButtonIO.GetAll();
            if (all.Count > 0)
                SelectButton(all[0].id);
            else
                SelectButton(null);
            SetStatus("按钮编辑器：共 " + all.Count + " 个按钮（点击「保存」写回 buttons.json）");
        }

        /// <summary>隐藏/显示卡牌编辑内容（卡牌列表区 + 右侧属性编辑区）</summary>
        private void SetCardEditVisible(bool visible)
        {
            if (card_list_root != null) card_list_root.SetActive(visible);
            if (editor_area_root != null) editor_area_root.SetActive(visible);
        }

        /// <summary>返回卡牌编辑模式</summary>
        private void OnButtonBack()
        {
            if (button_editor_root != null)
                button_editor_root.SetActive(false);
            SetCardEditVisible(true);
            SetStatus("");
        }

        // ---------------- 按钮编辑器：列表 ----------------

        private void RefreshButtonList()
        {
            if (button_list_content == null)
                return;

            //清除旧列表（保留模板）
            for (int i = button_list_content.childCount - 1; i >= 0; i--)
            {
                Transform child = button_list_content.GetChild(i);
                if (child != null && child.gameObject != button_list_template)
                    Destroy(child.gameObject);
            }
            button_row_imgs.Clear();

            foreach (BattleButtonData b in BattleButtonIO.GetAll())
            {
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                CreateButtonRow(b);
            }
        }

        private void CreateButtonRow(BattleButtonData b)
        {
            if (button_list_template == null)
                return;

            GameObject line = Instantiate(button_list_template, button_list_content);
            line.name = "Btn_" + b.id;
            line.SetActive(true);

            Text txt = line.transform.Find("Text")?.GetComponent<Text>();
            if (txt != null)
                txt.text = string.IsNullOrEmpty(b.title) ? b.id : b.title;

            Image img = line.GetComponent<Image>();
            if (img != null)
                img.color = new Color(1, 1, 1, 0.08f);
            button_row_imgs[b.id] = img;

            Button btn = line.GetComponent<Button>();
            if (btn != null)
            {
                string bid = b.id;
                btn.onClick.AddListener(() => SelectButton(bid));
            }
        }

        private void SelectButton(string id)
        {
            selected_button_id = id;
            editing_button = string.IsNullOrEmpty(id) ? null : BattleButtonIO.Get(id);

            foreach (var kv in button_row_imgs)
            {
                if (kv.Value == null)
                    continue;
                kv.Value.color = kv.Key == id ? new Color(0.4f, 0.7f, 1f, 0.35f) : new Color(1, 1, 1, 0.08f);
            }

            SetInput(btn_id_input, editing_button != null ? editing_button.id : "");
            SetInput(btn_title_input, editing_button != null ? editing_button.title : "");
            SetInput(btn_desc_input, editing_button != null ? editing_button.desc : "");
        }

        // ---------------- 按钮编辑器：增删改存 ----------------

        private void OnButtonNew()
        {
            BattleButtonData b = BattleButtonIO.New();
            RefreshButtonList();
            SelectButton(b.id);
            SetStatus("已新增按钮: " + b.id + "（点击「保存」写回 buttons.json）");
        }

        private void OnButtonCopy()
        {
            if (editing_button == null)
            {
                SetStatus("请先选择一个按钮再复制");
                return;
            }
            BattleButtonData copy = BattleButtonIO.Duplicate(editing_button);
            RefreshButtonList();
            SelectButton(copy.id);
            SetStatus("已复制按钮: " + copy.id + "（点击「保存」写回 buttons.json）");
        }

        private void OnButtonDel()
        {
            if (editing_button == null)
            {
                SetStatus("请先选择一个按钮再删除");
                return;
            }
            string id = editing_button.id;
            BattleButtonIO.Remove(editing_button);
            RefreshButtonList();
            List<BattleButtonData> all = BattleButtonIO.GetAll();
            if (all.Count > 0)
                SelectButton(all[0].id);
            else
                SelectButton(null);
            SetStatus("已删除按钮: " + id + "（点击「保存」写回 buttons.json）");
        }

        private void OnButtonSave()
        {
            BattleButtonIO.SaveAll();
            SetStatus("已保存按钮配置（buttons.json）");
        }

        /// <summary>「编辑规则图」：进入全屏规则编辑器编辑全局按钮图（一图多按钮）</summary>
        private void OnButtonEditGraph()
        {
            GraphEditorPanel editor = GraphEditorPanel.Get();
            if (editor == null)
                editor = FindObjectOfType<GraphEditorPanel>(true);
            if (editor == null)
            {
                SetStatus("未找到规则编辑器面板，请先运行「生成规则编辑器页面」工具");
                return;
            }
            editor.OpenButtons(BattleButtonIO.GetConfig());
            editor.Show();
            Hide();
        }

        /// <summary>按钮规则图关闭后返回：重新显示按钮编辑器并刷新列表</summary>
        public void NotifyButtonGraphClosed()
        {
            Show();
            //旧的内嵌「按钮管理页」已弃用（入口改为卡池界面的「按钮」栏 → 按钮编辑器），这里不再显示它
            if (button_editor_root != null)
                button_editor_root.SetActive(false);
            SetCardEditVisible(true);    //★ 返回卡池时**恢复**卡牌列表 + 右侧属性/变量配置列（原来这里是 false → 整列都不见了）
            RefreshButtonList();
            if (!string.IsNullOrEmpty(selected_button_id))
                SelectButton(selected_button_id);
            if (btn_row_root != null)
            {
                btn_row_expanded = true;       //回到卡池界面时保持展开，方便直接看到刚改的按钮
                ApplyButtonRowExpanded();
                RefreshButtonRowSquares();
            }
            SetStatus("按钮已更新（名称/背景/描述/自定义参数 + 按钮图 → buttons.json）");
        }

        protected override void Update()
        {
            base.Update();
            //复刻卡组构筑器：延迟刷新滚动内容高度（待网格布局生效）
            update_grid_timer += Time.deltaTime;
            if (update_grid && update_grid_timer > 0.2f)
            {
                UpdateGridHeight();
                update_grid = false;
            }
        }

        /// <summary>打开指定本地卡池文件进行编辑</summary>
        public void Open(string filePath)
        {
            current_path = filePath;
            current_pool = LoadPool(filePath);

            if (current_pool == null)
            {
                current_pool = new CardPoolData();
                current_pool.name = Path.GetFileNameWithoutExtension(filePath);
            }

            RefreshForm();
            RefreshCardList();
            ConfigNodes();
            SetStatus("已打开: " + Path.GetFileName(filePath));
        }

        // ---------------- 加载 ----------------

        private CardPoolData LoadPool(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                return JsonUtility.FromJson<CardPoolData>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("读取卡池失败: " + path + " " + e.Message);
                return null;
            }
        }

        // ---------------- 表单 ----------------

        private void RefreshForm()
        {
            if (current_pool == null)
                return;
            SetInput(input_name, current_pool.name);
            SetInput(input_desc, current_pool.description);
            SetInput(input_author, current_pool.author);
            if (file_text != null)
                file_text.text = string.IsNullOrEmpty(current_path) ? "（未保存）" : current_path;
        }

        private void ReadForm()
        {
            if (current_pool == null)
                return;
            current_pool.name = GetInput(input_name, current_pool.name);
            current_pool.description = GetInput(input_desc, current_pool.description);
            current_pool.author = GetInput(input_author, current_pool.author);
        }

        // ---------------- 卡牌列表 ----------------

        /// <summary>渲染卡牌列表（从 current_pool.cards）</summary>
        private void RefreshCardList()
        {
            if (card_content == null)
                return;

            EnsureCardPrefab();

            //清除旧网格卡
            for (int i = card_content.childCount - 1; i >= 0; i--)
                Destroy(card_content.GetChild(i).gameObject);
            card_lines.Clear();

            if (current_pool == null)
                return;

            foreach (CardCustomData card in current_pool.cards)
                CreateCardThumb(card);

            //默认选中第一张（若有），否则清空选中
            if (card_lines.Count > 0)
                OnSelectCard(card_lines[0].card);
            else
                OnSelectCard(null);

            //触发网格高度刷新（仿 CollectionPanel 延迟计算）
            update_grid = true;
            update_grid_timer = 0f;
        }

        /// <summary>运行时从卡组构筑界面复制卡面预制体与网格参数，保证每排张数与卡牌界面一致</summary>
        private void EnsureCardPrefab()
        {
            CollectionPanel collection = FindObjectOfType<CollectionPanel>(true);
            if (collection == null || collection.grid_content == null)
                return;

            // 卡面预制体：仅当未绑定时复制
            if (card_prefab == null && collection.card_prefab != null)
                card_prefab = collection.card_prefab;

            // 网格参数完全复制（含 constraint），保证与卡牌界面每排张数一致
            GridLayoutGroup src = collection.grid_content.GetGrid();
            GridLayoutGroup dst = card_grid != null ? card_grid.GetGrid() : null;
            if (src != null && dst != null)
            {
                if (src.cellSize.x > 0 && src.cellSize.y > 0)
                    dst.cellSize = src.cellSize;
                dst.spacing = src.spacing;
                dst.constraint = src.constraint;
                dst.constraintCount = src.constraintCount;
            }
            if (dst != null)
            {
                dst.padding = new RectOffset(10, 10, 10, 10);
                dst.childAlignment = TextAnchor.UpperLeft;
                dst.startAxis = GridLayoutGroup.Axis.Horizontal;
            }
        }

        /// <summary>按行数更新滚动内容高度（仿 CollectionPanel.LateUpdate，列数计算更稳健）</summary>
        private void UpdateGridHeight()
        {
            if (card_content == null || card_grid == null)
                return;
            if (card_content.childCount == 0)
                return;

            GridLayoutGroup grid = card_grid.GetGrid();
            if (grid == null)
                return;

            //列数：固定列则直接用 constraintCount，否则按内容宽度估算
            int cols = grid.constraintCount;
            if (grid.constraint == GridLayoutGroup.Constraint.Flexible || cols < 1)
            {
                float cell_w = grid.cellSize.x + grid.spacing.x;
                float content_w = card_content.rect.width - grid.padding.horizontal;
                cols = Mathf.Max(1, Mathf.FloorToInt((content_w + grid.spacing.x) / cell_w));
            }
            if (cols < 1)
                cols = 1;

            int rows = Mathf.CeilToInt(card_content.childCount / (float)cols);
            float row_height = grid.cellSize.y + grid.spacing.y;
            float height = rows * row_height;
            Vector2 sd = card_content.sizeDelta;
            card_content.sizeDelta = new Vector2(sd.x, height + 100);
        }

        private void CreateCardThumb(CardCustomData cdata)
        {
            if (card_prefab == null || card_grid == null)
                return;

            //本地卡 CardData（已由 CardPoolIO 注入静态字典；新增未导出的卡则运行时构建注册）
            CardData card = CardData.Get(cdata.id);
            if (card == null)
            {
                card = CardPoolIO.BuildCardData(cdata);
                if (card != null)
                    CardPoolIO.RegisterCard(card);
            }
            if (card == null)
                return;

            GameObject inst = Instantiate(card_prefab, card_content);
            CollectionCard ccard = inst.GetComponent<CollectionCard>();
            if (ccard == null)
            {
                Destroy(inst);
                return;
            }

            VariantData variant = VariantData.GetDefault();
            ccard.SetCard(card, variant, 0);

            //高亮标记（放在选中时叠加一层，简化：用卡面的翻转角度/透明度区分）
            Image highlight = inst.transform.Find("Selected")?.GetComponent<Image>();

            CardLine entry = new CardLine();
            entry.card = cdata;
            entry.rect = inst.GetComponent<RectTransform>();
            entry.ccard = ccard;
            entry.highlight = highlight;
            if (highlight != null)
                highlight.enabled = false;

            //点击选中
            ccard.onClick += _ => OnSelectCardId(cdata.id);
            ccard.onClickRight += _ => CardZoomPanel.Get().ShowCard(card, variant);

            card_lines.Add(entry);
        }

        /// <summary>选中某张卡，高亮对应卡面并刷新右侧提示</summary>
        private void OnSelectCard(CardCustomData card)
        {
            current_card = card;
            foreach (CardLine entry in card_lines)
            {
                bool sel = entry.card == card;
                if (entry.ccard != null)
                    entry.ccard.SetGrayscale(!sel);
                if (entry.rect != null)
                    entry.rect.localScale = sel ? Vector3.one * 1.06f : Vector3.one;
            }

            if (editor_hint != null)
            {
                if (card == null)
                    editor_hint.text = "（暂无卡牌，点左下「新增卡」添加）";
                else
                    editor_hint.text = "已选中：" + (string.IsNullOrEmpty(card.title) ? "（未命名）" : card.title);
            }
        }

        // ================= 布局归位：左下操作栏 + 右侧变量配置列 =================

        private bool layout_applied;

        //与 CardEditorBuilder 一致的按钮配色：取值来自 UITheme 分类色（全项目只定义一次）
        private static readonly Color ColBlue = UITheme.CatBlue;
        private static readonly Color ColGreen = UITheme.CatGreen;
        private static readonly Color ColRed = UITheme.CatRed;
        private static readonly Color ColGold = UITheme.CatGold;
        private static readonly Color ColPurple = UITheme.CatPurple;
        private static readonly Color ColPink = UITheme.CatPink;

        /// <summary>
        /// 把原先散落在标题栏 / 卡牌列表区 / 其它生成工具里的按钮统一归位成两个稳定区域：
        ///   左下角：复制 / 删除 / 保存 / 新增 / 编辑（进入规则编辑器） / 模拟测试
        ///   右侧列：变量配置（增益 / 种族 / 关键词 / 按钮），纵向排列，后续追加只需往列表里加一行
        ///
        /// 为什么放在运行时归位、而不是逐个改生成工具：本页的控件由 CardEditorBuilder、
        /// BattleButtonBuilder 等多处生成，运行时按引用收集最稳，
        /// 也不会因为以后重跑某一个工具又被打散；重复调用有 layout_applied 保护。
        /// </summary>
        private void ApplyEditorLayout()
        {
            if (layout_applied)
                return;
            layout_applied = true;

            Font font = UiFont();

            //1) 列表区底边上移，给左下角操作栏让位
            if (card_list_root != null)
            {
                RectTransform list_area = card_list_root.GetComponent<RectTransform>();
                if (list_area != null)
                {
                    Vector2 min = list_area.anchorMin;
                    list_area.anchorMin = new Vector2(min.x, Mathf.Max(min.y, 0.155f));
                }
            }

            //2) 原「列表底部操作栏」（复制/删除/保存）内容已搬走，隐藏空壳
            Transform old_ops = card_list_root != null ? card_list_root.transform.Find("CardListOps") : null;
            if (old_ops == null)
                old_ops = FindDeep(transform, "CardListOps");
            if (old_ops != null)
                old_ops.gameObject.SetActive(false);

            //3) 左下角操作栏（横向等距）
            Button b_copy = btn_copy != null ? btn_copy : FindButtonDeep("CopyCardBtn");
            Button b_del = btn_delete != null ? btn_delete : FindButtonDeep("DeleteCardBtn");
            Button b_add = btn_add_card != null ? btn_add_card : FindButtonDeep("AddCardBtn");
            Button b_test = btn_test != null ? btn_test : FindButtonDeep("TestBtn");
            Button b_save = btn_save2 != null ? btn_save2 : btn_save;

            bottom_action_bar = CreateBar("BottomActionBar", transform, true, 12f);
            bottom_action_bar.anchorMin = new Vector2(0f, 0f);
            bottom_action_bar.anchorMax = new Vector2(0f, 0f);
            bottom_action_bar.pivot = new Vector2(0f, 0f);
            bottom_action_bar.anchoredPosition = new Vector2(24f, 58f);
            bottom_action_bar.sizeDelta = new Vector2(760f, 52f);

            MoveToBar(bottom_action_bar, b_copy, new Vector2(110f, 46f));
            MoveToBar(bottom_action_bar, b_del, new Vector2(110f, 46f));
            MoveToBar(bottom_action_bar, b_save, new Vector2(110f, 46f));
            MoveToBar(bottom_action_bar, b_add, new Vector2(110f, 46f));

            if (btn_edit_card == null)
            {
                btn_edit_card = CreateLayoutButton("EditCardBtn", "编辑", ColGold, font, 22);
                btn_edit_card.onClick.AddListener(OnGo);   //「编辑」= 原「进行」：进入规则编辑器做详细编辑（含改名）
            }
            MoveToBar(bottom_action_bar, btn_edit_card, new Vector2(110f, 46f));
            MoveToBar(bottom_action_bar, b_test, new Vector2(130f, 46f));

            //保存已统一到左下角，隐藏标题栏里的重复按钮（标题栏只保留「返回」）
            if (btn_save != null && btn_save != b_save)
                btn_save.gameObject.SetActive(false);

            //「进行」的功能已并入「编辑」，按钮不再保留（场景里若还有旧按钮则一并隐藏）
            if (btn_go != null)
                btn_go.gameObject.SetActive(false);

            //4) 右侧变量配置列
            EnsureEditorArea();
            Transform side_parent = editor_area_root != null ? editor_area_root.transform : transform;

            Text caption = CreateTextNode("SideCaption", side_parent, "变量配置", font, 24, new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            RectTransform crt = caption.rectTransform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            //右端留出 ×（收起变量配置）的位置：标题与按钮列都收窄到 -72，避免与 × 重叠
            crt.offsetMin = new Vector2(16f, crt.offsetMin.y);
            crt.offsetMax = new Vector2(-16f, crt.offsetMax.y);
            crt.sizeDelta = new Vector2(-88f, 40f);
            crt.anchoredPosition = new Vector2(0f, -10f);

            side_config_bar = CreateBar("SideConfigBar", side_parent, false, 10f);
            side_config_bar.anchorMin = new Vector2(0f, 1f);
            side_config_bar.anchorMax = new Vector2(1f, 1f);
            side_config_bar.pivot = new Vector2(0.5f, 1f);
            side_config_bar.offsetMin = new Vector2(16f, side_config_bar.offsetMin.y);
            side_config_bar.offsetMax = new Vector2(-16f, side_config_bar.offsetMax.y);
            side_config_bar.sizeDelta = new Vector2(-88f, 10f);   //右侧留 72px 给 ×，四组按钮整体左移
            side_config_bar.anchoredPosition = new Vector2(0f, -56f);

            if (btn_buff == null)
            {
                btn_buff = CreateLayoutButton("BuffBtn", "增益", ColPurple, font, 22);
            }
            if (btn_trait == null)
            {
                btn_trait = CreateLayoutButton("TraitBtn", "种族", ColGreen, font, 22);
            }
            if (btn_keyword == null)
            {
                btn_keyword = CreateLayoutButton("KeywordBtn", "关键词", ColBlue, font, 22);
            }
            if (btn_buttons == null)
            {
                btn_buttons = CreateLayoutButton("ButtonsBtn", "按钮", ColPink, font, 22);
            }
            //四个入口一律走选择弹框（旧式"直接跳管理页面"已弃用）
            RewireVariableButton(btn_buff, VariableSelectPopup.Kind.Buff);
            RewireVariableButton(btn_trait, VariableSelectPopup.Kind.Trait);
            RewireVariableButton(btn_keyword, VariableSelectPopup.Kind.Keyword);
            RewireVariableButton(btn_buttons, VariableSelectPopup.Kind.Button);   //「按钮」入口保持原样（按钮栏在对战界面）

            MoveToBar(side_config_bar, btn_buff, new Vector2(0f, 46f));
            MoveToBar(side_config_bar, btn_trait, new Vector2(0f, 46f));
            MoveToBar(side_config_bar, btn_keyword, new Vector2(0f, 46f));
            MoveToBar(side_config_bar, btn_buttons, new Vector2(0f, 46f));

            //注：卡池界面的「按钮」入口保持原样（编辑器不做按钮栏）；
            //    "左侧方块展开/收起 + 一排方形按钮"是**对战界面**的功能，见 GameUI.EnsureBattleBar()。
            //变量配置列右上角的 × 已按要求移除（不再提供收起/展开整列），并把可能残留的旧按钮清掉
            RemoveSideCloseButton();
            //场景里可能还留着旧的关闭按钮（锚在面板右侧中部）→ 正好压在四组按钮上：统一挪到面板右上角
            ParkForeignCloseButtons();
            StartCoroutine(ParkForeignCloseNextFrame());   //等一帧布局稳定后再判一次（LayoutGroup 首帧才定尺寸）

            //5) 选中提示移到右列底部，避免和配置按钮抢位置
            if (editor_hint != null)
            {
                RectTransform hrt = editor_hint.rectTransform;
                hrt.anchorMin = new Vector2(0f, 0f);
                hrt.anchorMax = new Vector2(1f, 0f);
                hrt.pivot = new Vector2(0.5f, 0f);
                hrt.offsetMin = new Vector2(16f, 14f);
                hrt.offsetMax = new Vector2(-16f, 14f);
                hrt.sizeDelta = new Vector2(-32f, 80f);
                hrt.anchoredPosition = new Vector2(0f, 14f);
                editor_hint.fontSize = 20;
                editor_hint.alignment = TextAnchor.LowerLeft;
                editor_hint.color = new Color(1f, 1f, 1f, 0.65f);
            }
        }

        /// <summary>确保右侧编辑区存在（BattleButtonBuilder 未跑过时兜底创建）</summary>
        private void EnsureEditorArea()
        {
            if (editor_area_root != null)
                return;

            RectTransform area = CreateRectNode("EditorArea", transform);
            area.anchorMin = new Vector2(0.845f, 0.155f);
            area.anchorMax = new Vector2(0.995f, 0.9f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            bg.raycastTarget = false;
            editor_area_root = area.gameObject;
        }

        // ---------------- 配置入口（已统一到弹框，见上方「变量配置」区） ----------------
        //
        // 旧入口已弃用并删除：
        //   · OnOpenBuffEditor()        —— 原来点「增益」直接跳增益管理页 → 现在走选择弹框，编辑才进 BuffPanel.EditBuff(id)
        //   · OnOpenTraitEditor()       —— 原来弹提示/走外部钩子           → 现在走选择弹框（种族弹框内改名）
        //   · OnOpenKeywordEditor()     —— 原来点「关键词」直接跳关键词页   → 现在走选择弹框，编辑才进 KeywordPanel.EditKeyword(id)
        // 已弃用：OnOpenButtonEditor()（旧的内嵌按钮列表页）
        //   现在「按钮」类的编辑落地页 = **按钮编辑器**（规则编辑器面板的按钮模式，右列「按钮参数」），
        //   入口：选择弹框「编辑」→ OpenButtonEditor(id)；返回：按钮编辑器 × → NotifyButtonGraphClosed()。

        // ---------------- 布局/控件小工具 ----------------

        private Font UiFont()
        {
            if (title_text != null && title_text.font != null)
                return title_text.font;
            if (status_text != null && status_text.font != null)
                return status_text.font;
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (System.Exception) { return null; }
        }

        /// <summary>操作栏容器：横向用 HorizontalLayoutGroup，纵向用 VerticalLayoutGroup，间距统一</summary>
        private static RectTransform CreateBar(string name, Transform parent, bool horizontal, float spacing)
        {
            RectTransform rt = CreateRectNode(name, parent);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            if (horizontal)
            {
                HorizontalLayoutGroup g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
                g.spacing = spacing;
                g.padding = new RectOffset(0, 0, 0, 0);
                g.childAlignment = TextAnchor.MiddleLeft;
                g.childControlWidth = false;
                g.childControlHeight = false;
                g.childForceExpandWidth = false;
                g.childForceExpandHeight = false;
            }
            else
            {
                VerticalLayoutGroup g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
                g.spacing = spacing;
                g.padding = new RectOffset(0, 0, 0, 0);
                g.childAlignment = TextAnchor.UpperCenter;
                g.childControlWidth = true;
                g.childControlHeight = false;
                g.childForceExpandWidth = true;
                g.childForceExpandHeight = false;
            }
            return rt;
        }

        /// <summary>把按钮搬到操作栏里：清掉旧锚点/位置，交给 LayoutGroup 排布，尺寸由 sizeDelta 决定</summary>
        private static void MoveToBar(RectTransform bar, Button btn, Vector2 size)
        {
            if (bar == null || btn == null)
                return;
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.SetParent(bar, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            if (!btn.gameObject.activeSelf)
                btn.gameObject.SetActive(true);
        }

        /// <summary>与生成工具同款的按钮样式（半透明底色 + 白色文字 + 悬停/按下过渡）——样式统一走 UIFactory</summary>
        private Button CreateLayoutButton(string name, string label, Color bg, Font font, int size)
        {
            return UIFactory.CreateButton(name, transform, label, font, size, bg);
        }

        private static Text CreateTextNode(string name, Transform parent, string text, Font font, int size, Color color, TextAnchor align)
        {
            Text txt = UIFactory.CreateText(name, parent, text, font, size, color, align);
            UIFactory.SetStretch(txt.rectTransform);   //与本页既有行为一致：文字自动铺满父级
            return txt;
        }

        private static RectTransform CreateRectNode(string name, Transform parent)
            => UIFactory.CreateRect(name, parent);

        private static void StretchNode(RectTransform rt)
            => UIFactory.SetStretch(rt);

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name)
                    return child;
                Transform deep = FindDeep(child, name);
                if (deep != null)
                    return deep;
            }
            return null;
        }

        private Button FindButtonDeep(string name)
        {
            Transform t = FindDeep(transform, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        /// <summary>按 id 选中卡牌（来自 CollectionCard 点击）</summary>
        private void OnSelectCardId(string id)
        {
            if (current_pool == null)
                return;
            foreach (CardCustomData card in current_pool.cards)
            {
                if (card.id == id)
                {
                    OnSelectCard(card);
                    return;
                }
            }
        }

        /// <summary>新增一张默认卡牌（0费/中立/随从）并选中</summary>
        private void OnAddCard()
        {
            if (current_pool == null)
                return;

            CardCustomData card = new CardCustomData();
            card.id = "custom_" + GameTool.GenerateRandomID(8, 12);
            card.title = "新卡 " + (current_pool.cards.Count + 1);
            card.type = "Character";   // 随从
            card.team = "";
            card.rarity = "";
            card.mana = 0;             // 默认 0 费
            card.attack = 0;
            card.hp = 0;
            card.deckbuilding = true;
            card.abilities = new List<AbilityCustomData>();

            current_pool.cards.Add(card);
            CreateCardThumb(card);
            OnSelectCard(card);
            update_grid = true;
            update_grid_timer = 0f;
            SetStatus("已新增卡牌: " + card.title + "（0 费）");
        }

        /// <summary>复制当前选中卡，插入其后并选中</summary>
        private void OnCopyCard()
        {
            if (current_pool == null || current_card == null)
            {
                SetStatus("请先选择一张卡牌再复制");
                return;
            }

            CardCustomData copy = CloneCard(current_card);
            copy.id = "custom_" + GameTool.GenerateRandomID(8, 12);
            copy.title = current_card.title + " 复制";

            int index = current_pool.cards.IndexOf(current_card);
            if (index < 0)
                index = current_pool.cards.Count - 1;
            current_pool.cards.Insert(index + 1, copy);

            RefreshCardList();
            OnSelectCardId(copy.id);
            SetStatus("已复制卡牌: " + copy.title + "（记得保存）");
        }

        /// <summary>删除当前选中卡</summary>
        private void OnDeleteCard()
        {
            if (current_pool == null || current_card == null)
            {
                SetStatus("请先选择一张卡牌再删除");
                return;
            }

            string title = current_card.title;
            current_pool.cards.Remove(current_card);
            current_card = null;

            RefreshCardList();
            SetStatus("已删除卡牌: " + title + "（记得保存）");
        }

        /// <summary>深拷贝卡牌数据（JSON 往返，避免引用同一对象）</summary>
        private static CardCustomData CloneCard(CardCustomData src)
        {
            return JsonUtility.FromJson<CardCustomData>(JsonUtility.ToJson(src));
        }

        /// <summary>把卡牌列表写回（当前仅需保证列表字段已绑定在 current_pool 上，保存时直接序列化）</summary>
        private void ReadCardList()
        {
            //此行占位：current_pool.cards 保存时整体写回 JSON，无需额外读回逻辑
        }

        /// <summary>CardType 枚举名 → 中文显示</summary>
        private static string ToChinese(string type)
        {
            switch (type)
            {
                case "Hero": return "英雄";
                case "Character": return "随从";
                case "Spell": return "法术";
                case "Artifact": return "神器";
                case "Secret": return "奥秘";
                case "Equipment": return "装备";
                default: return string.IsNullOrEmpty(type) ? "随从" : type;
            }
        }

        // ---------------- 画布节点展示（P2 可拖拽） ----------------

        /// <summary>
        /// 画布容器：承载所有节点行，维护「运行时行 RectTransform → GraphNode」映射，
        /// 提供屏幕坐标到画布局部坐标的换算与节点移动逻辑。
        /// </summary>
        public class CanvasRect
        {
            public RectTransform root;                       // 画布容器 RectTransform
            public Dictionary<string, RectTransform> rows;   // 节点id → 行

            public CanvasRect(RectTransform root)
            {
                this.root = root;
                rows = new Dictionary<string, RectTransform>();
            }

            /// <summary>记录某个节点的行引用</summary>
            public void Register(string node_id, RectTransform row)
            {
                if (rows.ContainsKey(node_id))
                    rows[node_id] = row;
                else
                    rows.Add(node_id, row);
            }

            /// <summary>把某行按屏幕像素增量移动（delta 为屏幕增量，直接累加到画布局部坐标）</summary>
            public void MoveRow(RectTransform row, PointerEventData eventData)
            {
                row.anchoredPosition += eventData.delta;
            }
        }

        private void ConfigNodes()
        {
            if (node_content == null)
                return;

            //清除旧节点（保留模板）
            for (int i = node_content.childCount - 1; i >= 0; i--)
            {
                Transform child = node_content.GetChild(i);
                if (child != null && child.gameObject != node_template)
                    Destroy(child.gameObject);
            }

            //创建画布容器：大尺寸、无自动布局，节点自由定位
            if (canvas == null)
                canvas = new CanvasRect(node_content);

            GraphData graph = current_pool != null ? current_pool.graph : null;
            if (graph == null || graph.nodes.Count == 0)
            {
                SetStatus("该卡池没有规则图，可在编辑器中新建节点。");
                return;
            }

            foreach (GraphNode node in graph.nodes)
                CreateNodeLine(node, graph);
        }

        private void CreateNodeLine(GraphNode node, GraphData graph)
        {
            if (node_template == null)
                return;

            GameObject line = Instantiate(node_template, node_content);
            line.name = "Node_" + node.id;
            line.SetActive(true);

            RectTransform rect = line.GetComponent<RectTransform>();
            //节点在画布内自由定位：anchor+pivot 固定左下角，位置由 node.pos 决定（与模板一致）
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(node.pos.x, node.pos.y);

            Text type_text = line.transform.Find("TypeText")?.GetComponent<Text>();
            if (type_text != null)
                type_text.text = NodeTypeLabel(node.type);

            Text title_text = line.transform.Find("TitleText")?.GetComponent<Text>();
            if (title_text != null)
                title_text.text = node.title;

            Text desc_text = line.transform.Find("DescText")?.GetComponent<Text>();
            if (desc_text != null)
                desc_text.text = NodeSummary(node);

            //挂拖拽组件并登记到画布
            if (canvas != null)
                canvas.Register(node.id, rect);
            NodeDragger dragger = line.GetComponent<NodeDragger>();
            if (dragger == null)
                dragger = line.AddComponent<NodeDragger>();
            dragger.Setup(node.id, this);
        }

        /// <summary>拖拽中：把行按屏幕增量移动</summary>
        public void MoveNode(string node_id, RectTransform row, PointerEventData eventData)
        {
            if (canvas != null)
                canvas.MoveRow(row, eventData);
        }

        /// <summary>拖拽结束：把行位置写回对应 GraphNode.pos</summary>
        public void OnNodeMoved(string node_id, Vector2 pos)
        {
            if (current_pool == null || current_pool.graph == null)
                return;
            GraphNode node = current_pool.graph.GetNode(node_id);
            if (node == null)
                return;
            node.pos = new Vector2Data(pos.x, pos.y);
            SetStatus("节点已移动 (" + Mathf.RoundToInt(pos.x) + "," + Mathf.RoundToInt(pos.y) + ")，记得保存");
        }

        private static string NodeTypeLabel(GraphNodeType type)
        {
            switch (type)
            {
                case GraphNodeType.Event: return "触发";
                case GraphNodeType.Condition: return "条件";
                case GraphNodeType.Action: return "动作";
                case GraphNodeType.Value: return "数值";
                default: return "节点";
            }
        }

        private static string NodeSummary(GraphNode node)
        {
            string s = "<color=#9FD5FF>" + node.action + "</color>";
            foreach (FieldCustomData f in node.fields)
                s += "  " + f.name + "=" + f.value;
            //端口概要（▸输出 ◂输入）
            if (node.pins.Count > 0)
            {
                s += "  [";
                for (int i = 0; i < node.pins.Count; i++)
                {
                    GraphPin p = node.pins[i];
                    if (i > 0)
                        s += " ";
                    s += (p.is_output ? "→" : "←") + p.display_name;
                }
                s += "]";
            }
            return s;
        }

        // ---------------- 保存/测试 ----------------

        private void OnSave()
        {
            if (current_pool == null)
            {
                SetStatus("没有可保存的卡池");
                return;
            }

            ReadForm();

            //确保保存前图非空（无图则生成一张空图，保证结构完整）
            if (current_pool.graph == null)
                current_pool.graph = new GraphData();
            current_pool.graph.name = current_pool.name;

            string path = current_path;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(CardPoolIO.SaveFolder, current_pool.name + ".json");
                current_path = path;
            }

            try
            {
                Directory.CreateDirectory(CardPoolIO.SaveFolder);
                File.WriteAllText(path, JsonUtility.ToJson(current_pool, true));
                if (file_text != null)
                    file_text.text = path;
                SetStatus("已保存: " + Path.GetFileName(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("保存失败: " + e.Message);
                SetStatus("保存失败: " + e.Message);
            }
        }

        /// <summary>关闭编辑器并返回上一层页面（卡池管理页；兜底回首页）</summary>
        private void OnClose()
        {
            Hide();

            CardPoolPanel pool = CardPoolPanel.Get();
            if (pool == null)
                pool = FindObjectOfType<CardPoolPanel>(true);
            if (pool != null)
                pool.Show();
            else if (HomePanel.Get() != null)
                HomePanel.Get().ReturnHome();
        }

        /// <summary>「模拟测试」：跳转人机战斗。我方卡组由当前选中卡组成（一整套全是这张卡），
        /// AI 用随机初始卡池，开局双方法力直接为上限。</summary>
        private void OnTest()
        {
            if (current_card == null)
            {
                SetStatus("请先在卡牌列表中选择一张卡牌进行模拟测试");
                return;
            }

            //确保该卡运行时数据已注册且为最新（含规则图编译的能力）
            CardData card = CardData.Get(current_card.id);
            if (card == null)
            {
                card = CardPoolIO.BuildCardData(current_card);
                if (card != null)
                    CardPoolIO.RegisterCard(card);
            }
            else
            {
                CardPoolIO.UpdateCardData(current_card); //同步最新属性与规则图能力
            }
            if (card == null)
            {
                SetStatus("无法构建测试卡牌数据，请先保存卡池");
                return;
            }

            //我方卡组：一整套全是这张卡
            int deck_size = GameplayData.Get().deck_size;
            UserDeckData test_deck = new UserDeckData();
            test_deck.tid = "test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            test_deck.title = "测试 - " + (string.IsNullOrEmpty(current_card.title) ? card.title : current_card.title);
            test_deck.hero = GetDefaultHero();
            test_deck.cards = new UserCardData[]
            {
                new UserCardData { tid = card.id, variant = VariantData.GetDefault().id, quantity = deck_size }
            };

            //AI：随机初始卡池
            DeckData ai_data = GameplayData.Get().GetRandomAIDeck();
            if (ai_data == null)
            {
                SetStatus("没有可用的 AI 初始卡池");
                return;
            }
            UserDeckData ai_deck = new UserDeckData(ai_data);
            //校验 AI 卡池引用的卡牌在运行时注册表中都能解析（被删除/未注册的自定义卡会导致 AI 开局空卡组直接判负）
            foreach (UserCardData uc in ai_deck.cards)
            {
                if (CardData.Get(uc.tid) == null)
                {
                    SetStatus("无法测试：AI 初始卡池「" + ai_data.title + "」引用了无效卡牌 " + uc.tid + "（已删除或未注册），请修正 GameplayData.ai_decks");
                    return;
                }
            }

            //设置对战参数并跳转人机战斗
            GameClient.player_settings.deck = test_deck;
            GameClient.ai_settings.deck = ai_deck;
            GameClient.ai_settings.ai_level = GameplayData.Get().ai_level;
            GameClient.game_settings.test_full_mana = true;

            MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual);
        }

        /// <summary>取一张默认英雄卡作为测试卡组的英雄</summary>
        private UserCardData GetDefaultHero()
        {
            foreach (CardData c in CardData.GetAll())
            {
                if (c.type == CardType.Hero)
                    return new UserCardData(c, VariantData.GetDefault());
            }
            Debug.LogWarning("[模拟测试] 卡池中不存在「英雄」类型卡牌 → 测试双方将没有英雄（「获取玩家英雄」类节点与英雄伤害/治疗都会失效）");
            return new UserCardData();
        }

        /// <summary>「进行」：进入全屏卡牌规则编辑器，编辑当前选中卡自己的规则图</summary>
        private void OnGo()
        {
            if (current_card == null)
            {
                SetStatus("请先在卡牌列表中选择一张卡牌");
                return;
            }

            GraphEditorPanel editor = GraphEditorPanel.Get();
            if (editor == null)
                editor = FindObjectOfType<GraphEditorPanel>(true); //含失活对象
            if (editor == null)
            {
                SetStatus("未找到规则编辑器面板，请先运行「生成规则编辑器页面」工具");
                return;
            }

            editor.Open(current_pool, current_card, current_path);
            editor.Show();
            Hide();
        }

        /// <summary>规则编辑器关闭后返回：重新渲染卡面（属性已在规则编辑器中修改并同步到运行时 CardData）</summary>
        public void NotifyGraphClosed()
        {
            string keep_id = current_card != null ? current_card.id : null;
            RefreshCardList();
            //重新选中之前编辑的卡
            if (!string.IsNullOrEmpty(keep_id))
                OnSelectCardId(keep_id);
            if (current_card != null)
                SetStatus("规则编辑完成: " + current_card.title + "（记得保存卡池）");
        }

        // ---------------- UI 辅助 ----------------

        private static void SetInput(InputField field, string val)
        {
            if (field != null)
                field.text = val ?? "";
        }

        private static string GetInput(InputField field, string def)
        {
            if (field == null)
                return def;
            return string.IsNullOrEmpty(field.text) ? def : field.text;
        }

        private void SetStatus(string msg)
        {
            if (status_text != null)
                status_text.text = msg;
        }
    }
}