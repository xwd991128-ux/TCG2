using System.Collections.Generic;
using TcgEngine;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 关键词管理面板（Workshop 运行时页面）：
    /// 使用者在这里新建/编辑关键词（标题/说明/原生机制状态/规则图），
    /// 规则图通过「编辑图」按钮跳转规则编辑器（GraphEditorPanel 关键词模式）。
    /// 页面 UI 由编辑器工具「生成关键词管理页面」构建到 Menu 场景。
    /// 资产落盘通过 KeywordAssetIO（Editor 程序集注册），未注册时仅改内存。
    /// </summary>
    public class KeywordPanel : UIPanel
    {
        [Header("左侧列表")]
        public RectTransform list_content;
        public GameObject row_template;      // 关键词行模板（Button+Text，隐藏）

        [Header("右侧属性")]
        public InputField input_title;
        public InputField input_desc;
        public Dropdown dropdown_status;     // 原生机制状态（旧下拉；运行时停用并换成弹出单选按钮）

        /// <summary>「数值」输入框：关键词**自带数值**（护甲=2 / 法术伤害=1 这种）。
        /// 运行时创建（不改场景），插在"原生机制"那一行右边的空白处；0 = 不带数值。</summary>
        [System.NonSerialized] public InputField input_value;

        //★列表收起/展开（从卡牌编辑器点「编辑」进来时已经选好关键词 ⇒ 不再占着地方显示列表）
        private RectTransform list_area;          //ListArea（面板根左列）
        private RectTransform form_area;          //FormArea（右侧表单区，缓存）
        private GameObject list_toggle_go;        //工具栏里的「列表」开关（运行时创建）

        private TMPro.TMP_Text status_select_text;   // 状态弹出单选按钮的文本（运行时创建）
        private int status_index;                    // 当前选中的 STATUS_VALUES 下标

        [Header("规则图列表")]
        public RectTransform rules_content;
        public GameObject rule_template;     // 规则行模板（InputField+EditBtn+DelBtn，隐藏）
        public Button btn_add_rule;

        [Header("顶部工具栏")]
        public Button btn_new;
        public Button btn_save;
        public Button btn_close;
        public Text status_text;

        private List<GameObject> row_buttons = new List<GameObject>();
        private List<GameObject> rule_rows = new List<GameObject>();
        private int selected = -1;
        private const string NEW_ID_SEED = "keyword_";

        private static KeywordPanel instance;
        public static KeywordPanel Get() { return instance; }

        protected override void Awake()
        {
            base.Awake();
            instance = this;
            RepairFormArea();      //★布局自愈：生成工具把 FormArea 写成负高度 ⇒ 右侧表单全排到画面外
            EnsureValueRow();      //★「数值」行：运行时建（不改场景），见方法注释
            EnsureListToggle();    //★工具栏「列表」开关：收起左侧列表、让表单铺满
        }

        /// <summary>★布局自愈（不改场景，只在运行时纠正）：
        /// 生成工具 `KeywordPanelBuilder` 把 FormArea 写成 anchors=(0,0)-(1,0) + sizeDelta=(-390,-130)
        /// ⇒ 算出来的**高度是 -130**（负数）⇒ 它下面所有控件（标题/说明/原生机制/规则区/「数值」行）
        /// 全被排到画面外，打开页面就是"右边一片空、只有一个『+ 添加规则』飘着"。
        /// 这里按工具本意改成上下拉伸：左 348（让开左侧列表）/ 右 42 / 下 40 / 上 90。</summary>
        private void RepairFormArea()
        {
            RectTransform area = FindFormArea();
            if (area == null)
                return;
            area.anchorMin = new Vector2(0f, 0f);
            area.anchorMax = new Vector2(1f, 1f);
            area.pivot = new Vector2(0f, 0f);
            area.offsetMin = new Vector2(348f, 40f);
            area.offsetMax = new Vector2(-42f, -90f);
        }

        private RectTransform FindFormArea()
        {
            if (form_area != null)
                return form_area;
            foreach (RectTransform rt in GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name == "FormArea")
                {
                    form_area = rt;
                    break;
                }
            }
            return form_area;
        }

        // ---------------- ★顶部「列表」开关：收起左列、表单铺满 ----------------

        /// <summary>工具栏「列表」开关（运行时建在"新建"左边，不改场景）。</summary>
        private void EnsureListToggle()
        {
            if (list_toggle_go != null || btn_new == null)
                return;
            RectTransform bar = btn_new.transform.parent as RectTransform;
            if (bar == null)
                return;

            list_toggle_go = new GameObject("ListToggleBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            list_toggle_go.transform.SetParent(bar, false);
            RectTransform rt = list_toggle_go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 44f);
            rt.anchoredPosition = new Vector2(-326f, 0f);     //"新建"在 -220，这里放它左边

            Image img = list_toggle_go.GetComponent<Image>();
            img.color = new Color(0.35f, 0.35f, 0.42f, 1f);
            Button b = list_toggle_go.GetComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => SetListVisible(!(list_area != null && list_area.gameObject.activeSelf)));

            GameObject t = new GameObject("T", typeof(RectTransform));
            t.transform.SetParent(rt, false);
            TextMeshProUGUI txt = t.AddComponent<TextMeshProUGUI>();
            txt.text = "列表";
            txt.fontSize = 20f;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
            UIFonts.ApplyFont(txt);
            RectTransform trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
        }

        /// <summary>收起 / 展开左侧关键词列表；收起时表单铺满整行。
        /// 需求（用户）：从卡牌编辑器点「编辑」进来时关键词已经选好了，进去就别再显示列表。</summary>
        public void SetListVisible(bool on)
        {
            if (list_area == null)
            {
                foreach (RectTransform rt in GetComponentsInChildren<RectTransform>(true))
                {
                    if (rt.name == "ListArea")
                    {
                        list_area = rt;
                        break;
                    }
                }
            }
            if (list_area != null)
                list_area.gameObject.SetActive(on);

            RectTransform area = FindFormArea();
            if (area != null)
                area.offsetMin = new Vector2(on ? 348f : 40f, 40f);   //列表列宽 320 + 间距 ⇒ 收起时铺满

            EnsureListToggle();
            if (list_toggle_go != null)
            {
                Text t = list_toggle_go.GetComponentInChildren<Text>();
                if (t != null)
                    t.text = on ? "收起列表" : "列表";
            }
        }

        protected override void Start()
        {
            base.Start();
            //「原生机制」旧下拉 → 弹出单选按钮（与规则编辑器的类型/阵营/稀有度同款交互）
            status_select_text = UISelectPopup.AttachToDropdown(dropdown_status, OnClickStatusSelect);
            if (btn_new != null) btn_new.onClick.AddListener(OnClickNew);
            if (btn_save != null) btn_save.onClick.AddListener(OnClickSave);
            if (btn_close != null) btn_close.onClick.AddListener(OnClickClose);   //有未保存改动先弹确认
            if (btn_add_rule != null) btn_add_rule.onClick.AddListener(OnClickAddRule);
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            KeywordData.Load();
            RefreshList();
            SelectKeyword(selected);

            //★未保存监听：本页表单（标题/描述/原生机制/规则行）都是"保存时才读"的 → 监听控件改动。
            //  挂完再清一次脏标记：上面的程序化填充会触发回调，不能算用户改动。
            UnsavedWatch.HookAll(transform, () => { form_dirty = true; });
            form_dirty = false;
        }

        private bool form_dirty;   //表单/规则被改过（用户 2026-10-01：关闭前要提示未保存）

        /// <summary>关闭：有未保存改动 → 先弹通用确认框（保存并返回 / 放弃改动 / 取消）</summary>
        private void OnClickClose()
        {
            if (!form_dirty)
            {
                Hide();
                return;
            }
            UnsavedChangesPopup.Show(transform,
                "关键词有未保存的修改",
                "直接退出会丢掉这些修改。要保存吗？",
                () => { form_dirty = false; OnClickSave(); Hide(); },   //保存并返回
                () => { form_dirty = false; Hide(); });                 //放弃改动并返回
        }

        // ---------------- 列表 ----------------

        private void RefreshList()
        {
            List<KeywordData> keywords = KeywordData.GetAll();
            EnsureRows(row_buttons, keywords.Count, row_template, list_content);

            for (int i = 0; i < row_buttons.Count; i++)
            {
                int index = i;
                Button btn = row_buttons[i].GetComponent<Button>();
                bool has = i < keywords.Count && keywords[i] != null;
                row_buttons[i].SetActive(has);
                if (!has) continue;
                Text txt = btn.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    //★左对齐：行内容比列表列还宽，原来是居中对齐 ⇒ 两头的字被挤出列外裁掉，
                    //  只剩中间的 [id]（用户实报"左边是什么鬼"）。左对齐后至少能看见完整标题。
                    txt.alignment = TextAnchor.MiddleLeft;
                    string tag = keywords[i].HasMechanic ? "" : "（纯展示）";
                    int kv = keywords[i].GetNativeValue();
                    txt.text = $"{keywords[i].title}  [{keywords[i].id}]{(kv > 0 ? "  +" + kv : "")}{tag}";
                }
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectKeyword(index));
            }
        }

        private void SelectKeyword(int index)
        {
            List<KeywordData> keywords = KeywordData.GetAll();
            if (index < 0 || index >= keywords.Count || keywords[index] == null)
            {
                selected = -1;
                SetForm(null);
                return;
            }
            selected = index;
            SetForm(keywords[index]);
        }

        // ---------------- 表单 ----------------

        private void SetForm(KeywordData kw)
        {
            if (input_title != null) input_title.text = kw != null ? kw.title : "";
            if (input_desc != null) input_desc.text = kw != null ? kw.desc : "";
            if (input_value != null)
            {
                input_value.SetTextWithoutNotify(kw != null ? kw.GetNativeValue().ToString() : "0");
                input_value.interactable = kw != null;
            }
            int status_value = kw != null ? (int)kw.status_type : (int)StatusType.None;
            int status_idx = STATUS_VALUES.IndexOf(status_value);
            status_index = status_idx < 0 ? 0 : status_idx;
            if (dropdown_status != null)
            {
                dropdown_status.interactable = kw != null;
                dropdown_status.SetValueWithoutNotify(status_index);
            }
            RefreshStatusSelect();
            RefreshRules(kw);
        }

        private void RefreshRules(KeywordData kw)
        {
            int count = kw != null && kw.rules != null ? kw.rules.Count : 0;
            EnsureRows(rule_rows, count, rule_template, rules_content);

            for (int i = 0; i < rule_rows.Count; i++)
            {
                int index = i;
                GameObject row = rule_rows[i];
                bool has = i < count;
                row.SetActive(has);
                if (!has) continue;

                KeywordRule rule = kw.rules[index];
                InputField trigger = row.GetComponentInChildren<InputField>(true);
                if (trigger != null)
                {
                    trigger.SetTextWithoutNotify(rule.trigger_action);
                    trigger.onValueChanged.RemoveAllListeners();
                    trigger.onValueChanged.AddListener(v => rule.trigger_action = v);
                }
                Button[] buttons = row.GetComponentsInChildren<Button>(true);
                foreach (Button btn in buttons)
                {
                    btn.onClick.RemoveAllListeners();
                    if (btn.name.Contains("EditBtn"))
                        btn.onClick.AddListener(() => OnEditGraph(kw, rule));
                    else if (btn.name.Contains("DelBtn"))
                        btn.onClick.AddListener(() => { kw.rules.RemoveAt(index); RefreshRules(kw); });
                }
            }
        }

        private void OnClickAddRule()
        {
            KeywordData kw = Current;
            if (kw == null)
            {
                SetStatus("请先选择一个关键词");
                return;
            }
            //新规则默认一张「OnPlay → 造成伤害1」最简图，进图编辑器再改
            kw.rules.Add(new KeywordRule { trigger_action = "OnPlay", graph = Workshop.GraphBuilder.BuildSimpleGraph("OnPlay", "202001", 1) });
            RefreshRules(kw);
        }

        private void OnEditGraph(KeywordData kw, KeywordRule rule)
        {
            GraphEditorPanel editor = FindObjectOfType<GraphEditorPanel>(true);
            if (editor == null)
            {
                SetStatus("未找到规则编辑器面板，请先运行「生成规则编辑器页面」工具");
                return;
            }
            editor.return_to = this;      //★ 上一页 = 本页（规则编辑器被隐藏后回到关键词编辑器，而不是空白）
            return_to = null;             //★ 这是"向前导航"（进规则图），清掉本页的上一页，避免 Hide() 时把卡牌编辑器弹回来
            editor.OpenForKeyword(kw, rule);
            editor.Show();
            Hide();
        }

        // ---------------- ★「数值」行（关键词自带数值） ----------------

        /// <summary>运行时创建「数值」行（**不改场景**）：
        ///   位置 = "原生机制"那一行**右边**的空白（状态字段只占 55% 宽度，右半边本来就是空的）；
        ///   做法 = 复制「标题」行的标签 + 输入框再改名/改位置 ⇒ 字体、配色、占位符风格与整页一致
        ///          （比手搓 uGUI 更不容易出现"字体没改/发糊/方块"）。
        /// 语义 = 关键词自带的数值：护甲=2 → 拥有该关键词的卡自动 +2 护甲；法术伤害=1 → 法术伤害 +1；0 = 不带数值。</summary>
        private void EnsureValueRow()
        {
            if (input_value != null)
                return;

            RectTransform area = null;
            RectTransform label_src = null;
            RectTransform field_src = null;
            foreach (RectTransform rt in GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name == "FormArea") area = rt;
                else if (rt.name == "TitleLabel") label_src = rt;
                else if (rt.name == "TitleField") field_src = rt;
                if (area != null && label_src != null && field_src != null)
                    break;
            }
            if (area == null || label_src == null || field_src == null)
                return;                       //页面不是本工具生成的：不折腾

            //① 标签
            RectTransform label = Instantiate(label_src, area);
            label.name = "ValueLabel";
            label.anchorMin = new Vector2(0.58f, 1f);
            label.anchorMax = new Vector2(0.58f, 1f);
            label.pivot = new Vector2(0f, 1f);
            label.anchoredPosition = new Vector2(-30f, -132f);
            label.sizeDelta = new Vector2(60f, 26f);
            Text lt = label.GetComponentInChildren<Text>(true);
            if (lt != null)
                lt.text = "数值";

            //② 输入框（与"原生机制"同一行、放它右边）
            RectTransform field = Instantiate(field_src, area);
            field.name = "ValueField";
            field.anchorMin = new Vector2(0.62f, 1f);
            field.anchorMax = new Vector2(1f, 1f);
            field.pivot = new Vector2(0f, 1f);
            field.anchoredPosition = new Vector2(0f, -128f);
            field.sizeDelta = new Vector2(-8f, 34f);

            input_value = field.GetComponentInChildren<InputField>(true);
            if (input_value == null)
            {
                Destroy(field.gameObject);    //没拿到输入框就清掉克隆体，别留空壳
                return;
            }
            input_value.text = "0";
            if (input_value.placeholder is Text ph)
                ph.text = "0=不带数值";
            else if (input_value.placeholder is TMPro.TMP_Text pht)
                pht.text = "0=不带数值";
        }

        // ---------------- 新建/保存 ----------------

        private void OnClickNew()
        {
            //id 唯一：keyword_1、keyword_2…
            int seed = 1;
            while (KeywordData.Get(NEW_ID_SEED + seed) != null)
                seed++;
            string id = NEW_ID_SEED + seed;

            KeywordData keyword = ScriptableObject.CreateInstance<KeywordData>();
            keyword.id = id;
            keyword.title = "新关键词";
            KeywordData.keyword_list.Add(keyword);

            if (!Workshop.KeywordAssetIO.CreateAsset(keyword, "Assets/TcgEngine/Resources/Keywords/" + id + ".asset"))
                SetStatus("提示：新关键词暂存于内存（资产落盘不可用）");

            RefreshList();
            SelectKeyword(KeywordData.GetAll().IndexOf(keyword));
        }

        private void OnClickSave()
        {
            KeywordData kw = Current;
            if (kw == null)
            {
                SetStatus("请先选择一个关键词");
                return;
            }
            if (input_title != null) kw.title = input_title.text;
            if (input_desc != null) kw.desc = input_desc.text;
            if (input_value != null)
            {
                int v = 0;
                int.TryParse(input_value.text, out v);
                kw.value = Mathf.Max(0, v);      //0 = 不带数值；负数按 0
            }
            kw.status_type = (StatusType)STATUS_VALUES[Mathf.Clamp(status_index, 0, STATUS_VALUES.Count - 1)];
            Workshop.KeywordAssetIO.SaveAsset(kw);
            RefreshList();
            SetStatus("已保存: " + kw.title);
        }

        /// <summary>变量选择弹框的「编辑」入口：打开关键词编辑器并选中指定关键词（找不到时保持当前选中）</summary>
        public void EditKeyword(string keyword_id)
        {
            Show();
            if (string.IsNullOrEmpty(keyword_id))
                return;
            //★已指定关键词（从卡牌编辑器的「编辑」进来）⇒ 收起左侧列表、表单铺满
            //（用户口径：进去不要再看列表了）
            SetListVisible(false);
            RefreshList();
            int index = KeywordData.GetAll().IndexOf(KeywordData.Get(keyword_id));
            if (index >= 0)
                SelectKeyword(index);
        }

        private KeywordData Current => selected >= 0 && selected < KeywordData.GetAll().Count ? KeywordData.GetAll()[selected] : null;

        private void SetStatus(string msg)
        {
            if (status_text != null)
                status_text.text = msg;
        }

        // ---------------- 工具 ----------------

        private static readonly List<int> STATUS_VALUES = BuildStatusValues();
        private static List<int> BuildStatusValues()
        {
            //下拉第 0 项固定 None，其余列出常用机制状态（与 KeywordData 原生机制对应）
            return new List<int>
            {
                (int)StatusType.None,
                (int)StatusType.Haste, (int)StatusType.Fury, (int)StatusType.Protection,
                (int)StatusType.Shell, (int)StatusType.Stealth, (int)StatusType.Flying,
                (int)StatusType.Deathtouch, (int)StatusType.LifeSteal, (int)StatusType.FirstStrike,
                (int)StatusType.Trample, (int)StatusType.SpellImmunity, (int)StatusType.Armor,
                (int)StatusType.Immunity, (int)StatusType.Regenerate,
                (int)StatusType.SpellDamage,   //法术伤害（由「法术伤害」关键词绑定，数值=加成量）
            };
        }

        /// <summary>状态下拉的选项名（与 STATUS_VALUES 一一对应，供构建工具填充）</summary>
        public static List<string> GetStatusOptionNames()
        {
            List<string> names = new List<string>();
            foreach (int v in STATUS_VALUES)
                names.Add(((StatusType)v).ToString());
            return names;
        }

        /// <summary>点击状态按钮：弹出单选列表（显示枚举名，写回对应 int 值）</summary>
        private void OnClickStatusSelect()
        {
            if (Current == null)
            {
                SetStatus("请先选择一个关键词");
                return;
            }

            List<string> names = GetStatusOptionNames();
            List<string> values = new List<string>();
            for (int i = 0; i < STATUS_VALUES.Count; i++)
                values.Add(STATUS_VALUES[i].ToString());

            int idx = Mathf.Clamp(status_index, 0, STATUS_VALUES.Count - 1);
            UISelectPopup.OpenSingle(transform, "原生机制", names, values, STATUS_VALUES[idx].ToString(), OnStatusPicked);
        }

        private void OnStatusPicked(string value)
        {
            int parsed;
            if (!int.TryParse(value, out parsed))
                return;
            int idx = STATUS_VALUES.IndexOf(parsed);
            status_index = idx < 0 ? 0 : idx;
            RefreshStatusSelect();
        }

        /// <summary>刷新状态按钮文本（未选中关键词时清空占位）</summary>
        private void RefreshStatusSelect()
        {
            if (status_select_text == null)
                return;
            bool has = Current != null;
            int idx = Mathf.Clamp(status_index, 0, STATUS_VALUES.Count - 1);
            status_select_text.text = has ? ((StatusType)STATUS_VALUES[idx]).ToString() : "";
            status_select_text.color = has ? UITheme.TextBody : UITheme.Placeholder;
        }

        /// <summary>确保 rows 列表有 needed 个实例（不足则用模板复制）</summary>
        private static void EnsureRows(List<GameObject> rows, int needed, GameObject template, RectTransform parent)
        {
            if (template != null && template.activeSelf)
                template.SetActive(false);
            while (rows.Count < needed)
            {
                GameObject row = template != null ? Instantiate(template.gameObject, parent) : new GameObject("Row", typeof(RectTransform));
                row.transform.SetParent(parent, false);
                rows.Add(row);
            }
        }
    }
}
