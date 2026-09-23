using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 变量选择弹框（增益 / 种族 / 关键词 / 按钮 四类共用一套）：
    ///   标题 = 类别；主体 = 该类**当前所有已存在值**的列表（单击选中并高亮，卡牌已配置项默认预选中）；
    ///   底部三个按钮 = 删除（二次确认） / 编辑（进入对应编辑器页面） / 新增（建空项→写盘→进编辑页）。
    /// 交互：遮罩点击关闭、右上角 × 关闭、Esc 关闭。
    ///
    /// 设计：弹框只认"数据源 + 编辑器入口 + 选中回调"三件事，四类的差异集中在 Kind 与下列三处：
    ///   LoadEntries()（列数据） / CreateItem()（新增） / DeleteItem()（删除+持久化）；
    ///   进入哪个编辑器页面由宿主（CardEditorPanel）通过 open_editor 注入，弹框不反向依赖面板内部方法。
    /// UI 为运行时构建（与 CardEditorPanel.ApplyEditorLayout 同规），配色/字号统一取 UITheme。
    /// </summary>
    public class VariableSelectPopup : MonoBehaviour
    {
        public enum Kind
        {
            Buff = 0,      //增益（BuffPoolIO / Workshop/buffs.json；编辑页 = BuffPanel）
            Trait = 1,     //种族（TraitData 资产；暂无独立编辑页 → 弹框内改名）
            Keyword = 2,   //关键词（KeywordData 资产；编辑页 = KeywordPanel）
            Button = 3,    //按钮（BattleButtonIO / Workshop/buttons.json；编辑页 = 卡牌编辑器内嵌按钮编辑器）
            CustomNode = 4,//★自定义节点（CustomNodeIO / Workshop/custom_nodes.json；编辑页 = GraphEditorPanel 自定义节点模式）
        }

        // ---------------- 宿主注入（避免弹框反向依赖面板） ----------------

        /// <summary>进入对应编辑器页面（kind, id）：新增/编辑后调用，让玩家在目标编辑器里完善内容</summary>
        public static System.Action<Kind, string> open_editor;

        /// <summary>卡牌当前已配置的该项 id（用于列表默认预选中；null/空 = 未配置）</summary>
        public static System.Func<Kind, string> current_value_of_card;

        /// <summary>单击选中 → 回写卡牌（id 为空 = 清除该配置）</summary>
        public static System.Action<Kind, string> on_picked;

        /// <summary>列表发生增删后的通知（宿主可刷新状态栏/卡面）</summary>
        public static System.Action after_changed;

        private static VariableSelectPopup instance;

        private Kind kind;
        private string selected_id;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<RowRef> rows = new List<RowRef>();
        private RectTransform list_content;
        private GameObject row_template;
        private TMP_Text hint_text;
        private Button btn_del;
        private GameObject confirm_root;      //二次确认层（懒建）
        private GameObject input_root;        //改名层（懒建，仅种族用）

        private class Entry
        {
            public string id;
            public string title;
        }

        private class RowRef
        {
            public string id;
            public Image bg;
            public Button btn;
            public TMP_Text label;
        }

        //选中/常态行配色：直接对齐规则编辑器「多选弹层」的选项行（CreateSelectOptionRowEx）
        private static readonly Color ColSelect = new Color(0.2f, 0.55f, 0.85f, 0.95f);
        private static readonly Color ColRowIdle = new Color(1f, 1f, 1f, 0.08f);

        public static VariableSelectPopup Get() { return instance; }

        /// <summary>关闭当前弹框（切页/打开其它编辑器前调用）：它是挂在 Canvas 上的全屏模态，
        /// 若残留会变成隐形遮罩，把后面页面的按钮与输入框全部吃掉（"界面点不动"）。</summary>
        public static void CloseAll()
        {
            if (instance != null)
            {
                Destroy(instance.gameObject);
                instance = null;
            }
        }

        // ---------------- 打开 / 关闭 ----------------

        /// <summary>打开某类别的选择弹框（同一时刻只保留一个）</summary>
        public static VariableSelectPopup Open(Kind kind)
        {
            CardEditorPanel host = CardEditorPanel.Get();
            Canvas canvas = host != null ? host.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                Debug.LogWarning("[变量弹框] 找不到 Canvas，无法打开选择弹框");
                return null;
            }

            if (instance != null)
                Destroy(instance.gameObject);

            GameObject go = new GameObject("VariableSelectPopup", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();     //盖在所有页面之上（modal）

            VariableSelectPopup popup = go.AddComponent<VariableSelectPopup>();
            popup.kind = kind;
            popup.Build();
            instance = popup;
            return popup;
        }

        public void Close()
        {
            if (instance == this)
                instance = null;
            Destroy(gameObject);
        }

        private void Update()
        {
            //Esc：优先关内层弹层（确认/改名），否则关本弹框
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (confirm_root != null && confirm_root.activeSelf)
                    confirm_root.SetActive(false);
                else if (input_root != null && input_root.activeSelf)
                    SetHint("改名层已打开：请点「确定」或「取消」（Esc 不丢弃已填内容）");
                else
                    Close();
            }
        }

        /// <summary>点遮罩：内层弹层（二次确认 / 改名输入）打开时**不关闭**，避免把正在输入的内容丢掉；
        /// 其它情况（纯列表选择，没有未保存内容）按既有习惯关闭弹框。</summary>
        private void OnMaskClick()
        {
            if (HasInnerLayerOpen())
            {
                SetHint("请先点「确定」或「取消」结束当前编辑（点空白处不会丢弃已填内容）");
                return;
            }
            Close();
        }

        private bool HasInnerLayerOpen()
        {
            return (confirm_root != null && confirm_root.activeSelf)
                || (input_root != null && input_root.activeSelf);
        }

        // ---------------- UI 构建 ----------------

        private string KindName
        {
            get
            {
                switch (kind)
                {
                    case Kind.Buff: return "增益";
                    case Kind.Trait: return "种族";
                    case Kind.Keyword: return "关键词";
                    case Kind.CustomNode: return "自定义节点";
                    default: return "按钮";
                }
            }
        }

        private Color KindColor
        {
            get
            {
                switch (kind)
                {
                    case Kind.Buff: return UITheme.CatPurple;
                    case Kind.Trait: return UITheme.CatGreen;
                    case Kind.Keyword: return UITheme.CatBlue;
                    case Kind.CustomNode: return UITheme.CatGold;
                    default: return UITheme.CatPink;
                }
            }
        }

        /// <summary>自定义节点类别的中文名（列表标题里标注"动作/函数/事件"）</summary>
        private static string CustomNodeKindName(CustomNodeKind k)
        {
            switch (k)
            {
                case CustomNodeKind.Function: return "函数";
                case CustomNodeKind.Event: return "事件";
                default: return "动作";
            }
        }

        private void Build()
        {
            //遮罩（RaycastTarget=true：拦住下层点击；点遮罩=关闭）
            Image mask = gameObject.AddComponent<Image>();
            mask.color = UITheme.MaskPopup;
            mask.raycastTarget = true;
            Button mask_btn = gameObject.AddComponent<Button>();
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(OnMaskClick);   //内层弹层打开时不关闭（避免丢掉正在输入的内容）

            //弹框主体
            RectTransform box = MakeRect("Box", transform);
            box.anchorMin = new Vector2(0.5f, 0.5f);
            box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = new Vector2(440f, 520f);   //与规则编辑器「多选弹层」(EnsureFieldSelectPopup) 同尺寸
            Image box_bg = box.gameObject.AddComponent<Image>();
            box_bg.color = UITheme.BgPopup;
            box_bg.raycastTarget = true;   //吃掉落在弹框内的点击，避免穿透到遮罩直接关闭

            //标题（22 号、上边 34 高、右侧留 60 给 ×：与多选弹层一致）
            TMP_Text title = MakeText("Title", box, KindName, 22, UITheme.TextTitle, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -6f), new Vector2(-52f, 34f));

            //右上角 ×（34×34，比按钮列高一层的层级 → 保证可点；样式与多选弹层一致）
            Button close = MakeButton("CloseBtn", box, "×", 22, UITheme.CtrlStrong, UITheme.TextBody,
                Vector2.zero, new Vector2(34f, 34f), TextAnchor.MiddleCenter);
            RectTransform close_rt = close.GetComponent<RectTransform>();
            close_rt.anchorMin = new Vector2(1f, 1f);       //MakeButton 默认底部锚点 → 这里改成右上角
            close_rt.anchorMax = new Vector2(1f, 1f);
            close_rt.pivot = new Vector2(1f, 1f);
            close_rt.anchoredPosition = new Vector2(-6f, -6f);
            close.onClick.AddListener(Close);

            //列表滚动区（内边距与多选弹层一致：左右 12、上 46、下留给操作栏）
            RectTransform scroll_rt = MakeRect("List", box);
            SetRect(scroll_rt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 96f), new Vector2(-12f, -46f));
            Image view_bg = scroll_rt.gameObject.AddComponent<Image>();
            view_bg.color = UITheme.BgViewport;
            view_bg.raycastTarget = true;
            ScrollRect scroll = scroll_rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            RectTransform viewport = MakeRect("Viewport", scroll_rt);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(4f, 4f);
            viewport.offsetMax = new Vector2(-4f, -4f);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            list_content = MakeRect("Content", viewport);
            list_content.anchorMin = new Vector2(0f, 1f);
            list_content.anchorMax = new Vector2(1f, 1f);
            list_content.pivot = new Vector2(0.5f, 1f);
            list_content.anchoredPosition = Vector2.zero;
            list_content.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup layout = list_content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;                            //与多选弹层一致
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            //★ childControlHeight 必须为 true：否则子物体的 LayoutElement(30) 被忽略，行高会退化成
            //  RectTransform 默认的 100 → 一页只显示 3 条（与规则编辑器多选弹层不一致的根因）。
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = list_content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = list_content;

            //行模板（隐藏，运行时复制）
            row_template = BuildRowTemplate(list_content);

            //底部提示
            hint_text = MakeText("Hint", box, "", 18, UITheme.Hint, TextAnchor.MiddleLeft);
            SetRect(hint_text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 56f), new Vector2(-12f, 82f));

            //底部三个按钮：删除 / 编辑 / 新增（居中排布；按钮高 34 与多选弹层的「使用」按钮同规）
            RectTransform bar = MakeRect("Actions", box);
            SetRect(bar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 12f), new Vector2(-12f, 48f));
            HorizontalLayoutGroup barg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barg.spacing = 10f;
            barg.childAlignment = TextAnchor.MiddleCenter;
            barg.childControlWidth = false;
            barg.childControlHeight = false;
            barg.childForceExpandWidth = false;
            barg.childForceExpandHeight = false;

            Button del = MakeBarButton(bar, "Del", "删除", UITheme.Danger);
            del.onClick.AddListener(OnDeleteClick);
            btn_del = del;
            Button edit = MakeBarButton(bar, "Edit", "编辑", UITheme.Accent);
            edit.onClick.AddListener(OnEditClick);
            Button add = MakeBarButton(bar, "New", "新增", KindColor);
            add.onClick.AddListener(OnNewClick);

            //弹框整体构建完毕 → 用全项目字体管线兜一遍（含后续懒建的内层确认/改名层）
            UIFonts.ApplyResolved(gameObject);

            RefreshList();
        }

        private GameObject BuildRowTemplate(RectTransform parent)
        {
            GameObject row = MakeRect("RowTemplate", parent).gameObject;
            //行高 30：与多选弹层的选项行一致（LayoutElement + sizeDelta 双保险：布局组若没开
            //childControlHeight，就会用 sizeDelta —— 默认 100 会让一页只剩 3 条）
            RectTransform row_rt = row.GetComponent<RectTransform>();
            row_rt.sizeDelta = new Vector2(0f, 30f);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 30f;
            le.preferredHeight = 30f;
            Image bg = row.AddComponent<Image>();
            bg.color = ColRowIdle;
            bg.raycastTarget = true;
            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = cb;

            TMP_Text txt = MakeText("Text", row.transform as RectTransform, "", 18, UITheme.TextBody, TextAnchor.MiddleLeft);
            SetRect(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));

            row.SetActive(false);
            return row;
        }

        // ---------------- 列表 ----------------

        /// <summary>重新拉取数据源并重建列表（选中项尽量保持）</summary>
        private void RefreshList()
        {
            entries.Clear();
            entries.AddRange(LoadEntries());

            //默认选中：卡牌已配置项 → 否则保持原选中 → 否则第一项
            string prefer = current_value_of_card != null ? current_value_of_card.Invoke(kind) : null;
            if (!string.IsNullOrEmpty(prefer) && FindEntry(prefer) != null)
                selected_id = prefer;
            else if (FindEntry(selected_id) == null)
                selected_id = entries.Count > 0 ? entries[0].id : null;

            rows.Clear();
            if (list_content != null)
            {
                for (int i = list_content.childCount - 1; i >= 0; i--)
                {
                    Transform child = list_content.GetChild(i);
                    if (child != null && child.gameObject != row_template)
                        Destroy(child.gameObject);
                }
            }

            foreach (Entry e in entries)
            {
                if (row_template == null)
                    break;
                GameObject row = Instantiate(row_template, list_content);
                row.name = "Row_" + e.id;
                row.SetActive(true);
                TMP_Text txt = row.transform.Find("Text")?.GetComponent<TMP_Text>();
                Button btn = row.GetComponent<Button>();
                string id = e.id;
                if (btn != null)
                    btn.onClick.AddListener(() => Select(id));
                rows.Add(new RowRef { id = e.id, bg = row.GetComponent<Image>(), btn = btn, label = txt });
            }

            ApplyHighlight();
            UpdateButtons();
            SetHint(entries.Count == 0 ? ("暂无" + KindName + "，点「新增」创建一个") : ("共 " + entries.Count + " 项；单击选中，点「编辑」进入" + KindName + "编辑器"));
        }

        private List<Entry> LoadEntries()
        {
            List<Entry> list = new List<Entry>();
            switch (kind)
            {
                case Kind.Buff:
                    foreach (BuffData b in BuffPoolIO.GetAll())
                    {
                        if (b != null && !string.IsNullOrEmpty(b.id))
                            list.Add(new Entry { id = b.id, title = b.GetTitle() });
                    }
                    break;
                case Kind.Trait:
                    foreach (TraitData t in TraitAssetIO.All())
                    {
                        if (t != null && !string.IsNullOrEmpty(t.id))
                            list.Add(new Entry { id = t.id, title = t.GetTitle() });
                    }
                    break;
                case Kind.Keyword:
                    foreach (KeywordData k in KeywordData.GetAll())
                    {
                        if (k != null && !string.IsNullOrEmpty(k.id))
                            list.Add(new Entry { id = k.id, title = string.IsNullOrEmpty(k.title) ? k.id : k.title });
                    }
                    break;
                case Kind.Button:
                    foreach (BattleButtonData b in BattleButtonIO.GetAll())
                    {
                        if (b != null && !string.IsNullOrEmpty(b.id))
                            list.Add(new Entry { id = b.id, title = string.IsNullOrEmpty(b.title) ? b.id : b.title });
                    }
                    break;
                case Kind.CustomNode:
                    foreach (CustomNodeData n in CustomNodeIO.GetAll())
                    {
                        if (n != null && !string.IsNullOrEmpty(n.id))
                            list.Add(new Entry { id = n.id, title = n.GetTitle() + "（" + CustomNodeKindName(n.Kind) + "）" });
                    }
                    break;
            }
            return list;
        }

        private Entry FindEntry(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            foreach (Entry e in entries)
            {
                if (e.id == id)
                    return e;
            }
            return null;
        }

        private void Select(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;
            selected_id = id;
            ApplyHighlight();
            UpdateButtons();
            SetHint("已选中：" + (FindEntry(id) != null ? FindEntry(id).title : id));
            if (on_picked != null)
                on_picked.Invoke(kind, id);   //回写卡牌（种族/关键词有对应字段；增益/按钮为全局资源，宿主自行取舍）
        }

        /// <summary>应用选中态：选中行蓝底 + 行首「√」，未选中行「□」（勾选框样式与多选弹层一致）</summary>
        private void ApplyHighlight()
        {
            foreach (RowRef r in rows)
            {
                bool on = r.id == selected_id;
                if (r.bg != null)
                    r.bg.color = on ? ColSelect : ColRowIdle;
                if (r.label != null)
                {
                    Entry e = FindEntry(r.id);
                    string title = e != null ? (string.IsNullOrEmpty(e.title) ? e.id : e.title) : r.id;
                    r.label.text = (on ? "√ " : "□ ") + title;
                }
            }
        }

        private void UpdateButtons()
        {
            bool has = !string.IsNullOrEmpty(selected_id);
            if (btn_del != null)
            {
                btn_del.interactable = has;
                Image img = btn_del.targetGraphic as Image;
                if (img != null)
                    img.color = has ? UITheme.Danger : new Color(0.5f, 0.4f, 0.4f, 0.35f);   //无选中 → 视觉置灰
            }
        }

        private void SetHint(string msg)
        {
            if (hint_text != null)
                hint_text.text = msg;
        }

        // ---------------- 三个操作 ----------------

        private void OnNewClick()
        {
            string id = CreateItem();
            if (string.IsNullOrEmpty(id))
                return;
            selected_id = id;
            RefreshList();
            Select(id);
            NotifyChanged();
            //新增后**留在当前弹框**（不跳转到编辑器页面）：空项已加入数据源/列表并默认选中，
            //需要完善内容时再点「编辑」进入对应编辑器（增益管理页等旧页面不再被"新增"拉起）。
            SetHint("已在当前列表新增" + KindName + "：" + id + "（需要完善内容时点「编辑」）");
            //★自定义节点例外：新建后必须进去配类型/端口/编排才有意义 → 直接跳进编辑器
            if (kind == Kind.CustomNode)
            {
                Close();
                if (open_editor != null)
                    open_editor.Invoke(kind, id);
            }
        }

        private void OnEditClick()
        {
            if (string.IsNullOrEmpty(selected_id))
                return;
            if (kind == Kind.Trait)
            {
                //种族暂无独立编辑页：弹框内改名（改完写盘）
                TraitData t = TraitData.Get(selected_id);
                ShowInputDialog("编辑种族标题", t != null ? t.title : "", text =>
                {
                    TraitAssetIO.Rename(t, text);
                    RefreshList();
                    Select(selected_id);
                    NotifyChanged();
                    SetHint("已保存种族标题：" + text);
                });
                return;
            }
            Close();    //同上：进入编辑器页面前先收起弹框
            if (open_editor != null)
                open_editor.Invoke(kind, selected_id);
        }

        private void OnDeleteClick()
        {
            if (string.IsNullOrEmpty(selected_id))
                return;
            string id = selected_id;
            Entry e = FindEntry(id);
            string label = e != null ? e.title : id;
            ShowConfirm("确定删除该" + KindName + "「" + label + "」？\n该操作不可恢复。", () =>
            {
                DeleteItem(id);
                if (id == selected_id)
                    selected_id = null;
                if (current_value_of_card != null && current_value_of_card.Invoke(kind) == id && on_picked != null)
                    on_picked.Invoke(kind, null);   //卡牌上配置的就是它 → 同步清除
                RefreshList();
                NotifyChanged();
                SetHint("已删除" + KindName + "：" + label);
            });
        }

        private void NotifyChanged()
        {
            after_changed?.Invoke();
        }

        /// <summary>新增空项（含持久化），返回新 id；失败返回空</summary>
        private string CreateItem()
        {
            switch (kind)
            {
                case Kind.Buff:
                {
                    BuffData b = BuffPoolIO.New();
                    BuffPoolIO.SaveAll();
                    return b != null ? b.id : null;
                }
                case Kind.Trait:
                {
                    TraitData t = TraitAssetIO.New();
                    return t != null ? t.id : null;
                }
                case Kind.Keyword:
                {
                    int seed = 1;
                    while (KeywordData.Get("keyword_" + seed) != null)
                        seed++;
                    KeywordData kw = ScriptableObject.CreateInstance<KeywordData>();
                    kw.id = "keyword_" + seed;
                    kw.title = "新关键词";
                    KeywordData.keyword_list.Add(kw);
                    if (!KeywordAssetIO.CreateAsset(kw, "Assets/TcgEngine/Resources/Keywords/" + kw.id + ".asset"))
                        SetHint("提示：新关键词暂存于内存（资产落盘不可用）");
                    return kw.id;
                }
                case Kind.Button:
                {
                    BattleButtonData b = BattleButtonIO.New();
                    BattleButtonIO.SaveAll();
                    return b != null ? b.id : null;
                }
                case Kind.CustomNode:
                {
                    CustomNodeData n = CustomNodeIO.New(CustomNodeKind.Action);   //默认动作节点（进去后可切类型）
                    CustomNodeIO.SaveAll();
                    return n != null ? n.id : null;
                }
            }
            return null;
        }

        /// <summary>删除项（含持久化）</summary>
        private void DeleteItem(string id)
        {
            switch (kind)
            {
                case Kind.Buff:
                    BuffPoolIO.Remove(BuffPoolIO.Get(id));
                    BuffPoolIO.SaveAll();
                    break;
                case Kind.Trait:
                    TraitAssetIO.Remove(TraitData.Get(id));
                    break;
                case Kind.Keyword:
                {
                    KeywordData kw = KeywordData.Get(id);
                    if (kw != null)
                    {
                        KeywordAssetIO.DeleteAsset(kw);
                        KeywordData.keyword_list.Remove(kw);
                    }
                    break;
                }
                case Kind.Button:
                    BattleButtonIO.Remove(BattleButtonIO.Get(id));
                    BattleButtonIO.SaveAll();
                    break;
                case Kind.CustomNode:
                    CustomNodeIO.Remove(CustomNodeIO.Get(id));
                    CustomNodeIO.SaveAll();
                    break;
            }
        }

        // ---------------- 内层：二次确认 / 文本输入 ----------------

        /// <summary>二次确认框（确认才执行；取消/遮罩/×/Esc 均不做事）</summary>
        public void ShowConfirm(string message, System.Action on_ok)
        {
            RectTransform box;
            Transform root = EnsureLayer("ConfirmLayer", out box);
            box.sizeDelta = new Vector2(460f, 240f);

            TMP_Text msg = MakeText("Msg", box, message, 22, UITheme.TextBody, TextAnchor.MiddleCenter);
            SetRect(msg.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20f, 76f), new Vector2(-20f, -20f));

            Button cancel = MakeButton("Cancel", box, "取消", 22, UITheme.Ctrl, UITheme.TextBody,
                new Vector2(-130f, 18f), new Vector2(120f, 44f), TextAnchor.MiddleCenter);
            cancel.onClick.AddListener(() => root.gameObject.SetActive(false));

            Button ok = MakeButton("OK", box, "确定删除", 22, UITheme.Danger, UITheme.TextBody,
                new Vector2(130f, 18f), new Vector2(150f, 44f), TextAnchor.MiddleCenter);
            ok.onClick.AddListener(() =>
            {
                root.gameObject.SetActive(false);
                on_ok?.Invoke();
            });

            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
        }

        /// <summary>单行文本输入框（种族改名用）</summary>
        public void ShowInputDialog(string title, string init, System.Action<string> on_ok)
        {
            RectTransform box;
            Transform root = EnsureLayer("InputLayer", out box);
            box.sizeDelta = new Vector2(480f, 240f);

            TMP_Text t = MakeText("Title", box, title, 22, UITheme.TextTitle, TextAnchor.MiddleLeft);
            SetRect(t.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -12f), new Vector2(-60f, 44f));

            RectTransform field = MakeRect("Field", box);
            SetRect(field, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(24f, -20f), new Vector2(-24f, 44f));
            Image fbg = field.gameObject.AddComponent<Image>();
            fbg.color = UITheme.FieldBg;
            //TMP 输入框（项目规范）：必须给 textViewport（"Text Area"）→ 否则拖动/选字会 NullReferenceException；
            //绑定完组件后重启一次 enabled，TMP 才会建出插入光标；再交给 TmpInputUtil.Guard 做空文本兜底。
            TMP_InputField input = field.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = fbg;
            RectTransform area = MakeRect("Text Area", field);
            SetRect(area, Vector2.zero, Vector2.one, new Vector2(10f, 1f), new Vector2(-10f, -1f));
            area.gameObject.AddComponent<RectMask2D>();
            TMP_Text itext = MakeText("Text", area, init, 22, UITheme.TextBody, TextAnchor.MiddleLeft);
            SetRect(itext.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            itext.richText = false;
            input.textViewport = area;
            input.textComponent = itext;
            input.caretColor = Color.white;
            input.enabled = false;
            input.enabled = true;
            TmpInputUtil.Write(input, init);
            TmpInputUtil.Guard(input);

            Button cancel = MakeButton("Cancel", box, "取消", 22, UITheme.Ctrl, UITheme.TextBody,
                new Vector2(-130f, 18f), new Vector2(120f, 44f), TextAnchor.MiddleCenter);
            cancel.onClick.AddListener(() => root.gameObject.SetActive(false));

            Button ok = MakeButton("OK", box, "确定", 22, UITheme.CtrlStrong, UITheme.TextBody,
                new Vector2(130f, 18f), new Vector2(120f, 44f), TextAnchor.MiddleCenter);
            ok.onClick.AddListener(() =>
            {
                string v = TmpInputUtil.Read(input);
                root.gameObject.SetActive(false);
                on_ok?.Invoke(v);
            });

            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
        }

        private Transform EnsureLayer(string name, out RectTransform box)
        {
            GameObject layer = transform.Find(name)?.gameObject;
            if (layer == null)
            {
                RectTransform lrt = MakeRect(name, transform);
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
                Image m = lrt.gameObject.AddComponent<Image>();
                m.color = UITheme.MaskPopup;
                m.raycastTarget = true;
                layer = lrt.gameObject;
            }
            layer.transform.SetAsLastSibling();
            Transform box_t = layer.transform.Find("Box");
            if (box_t == null)
            {
                RectTransform b = MakeRect("Box", layer.transform);
                b.anchorMin = new Vector2(0.5f, 0.5f);
                b.anchorMax = new Vector2(0.5f, 0.5f);
                b.pivot = new Vector2(0.5f, 0.5f);
                b.anchoredPosition = Vector2.zero;
                Image bimg = b.gameObject.AddComponent<Image>();
                bimg.color = UITheme.BgPopup;
                bimg.raycastTarget = true;
                box_t = b;
            }
            box = box_t as RectTransform;

            if (name == "ConfirmLayer")
                confirm_root = layer;
            else
                input_root = layer;
            layer.SetActive(false);
            return layer.transform;
        }

        // ---------------- UI 小工具（与 CardEditorPanel 同风格） ----------------

        private static RectTransform MakeRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static void SetRect(RectTransform rt, Vector2 amin, Vector2 amax, Vector2 off_min, Vector2 off_max)
        {
            rt.anchorMin = amin;
            rt.anchorMax = amax;
            rt.offsetMin = off_min;
            rt.offsetMax = off_max;
        }

        /// <summary>新建文本：**统一用 TMP + 全项目字体管线**（旧版 uGUI Text 与页面其它文字字体不一致且发糊；
        /// 这是项目硬规则，见技能 unity-workshop-ui-pitfalls）。字体由 UIFonts 解析，构建完还会 ApplyResolved 兜一遍。</summary>
        private static TMP_Text MakeText(string name, Transform parent, string text, int size, Color color, TextAnchor anchor)
        {
            RectTransform rt = MakeRect(name, parent);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = ToTmpAlignment(anchor);
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            UIFonts.ApplyFont(t);
            return t;
        }

        /// <summary>TextAnchor → TMP 对齐（TMP 用 TextAlignmentOptions 枚举）</summary>
        private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                default: return TextAlignmentOptions.BottomRight;
            }
        }

        private static Button MakeButton(string name, Transform parent, string label, int size, Color bg, Color fg,
            Vector2 pos, Vector2 size_delta, TextAnchor anchor)
        {
            RectTransform rt = MakeRect(name, parent);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size_delta;

            Image img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            Button btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = cb;

            if (!string.IsNullOrEmpty(label))
            {
                TMP_Text t = MakeText("Text", rt, label, size, fg, anchor);
                SetRect(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
            return btn;
        }

        private static Button MakeBarButton(Transform bar, string name, string label, Color bg)
        {
            RectTransform rt = MakeRect(name, bar);
            rt.sizeDelta = new Vector2(96f, 34f);     //与多选弹层底部按钮（76×34）同高
            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 96f;
            le.preferredHeight = 34f;

            Image img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            Button btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = cb;

            TMP_Text t = MakeText("Text", rt, label, 18, UITheme.TextBody, TextAnchor.MiddleCenter);
            SetRect(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return btn;
        }
    }
}
