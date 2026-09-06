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
        public Dropdown dropdown_status;     // 原生机制状态（StatusType 枚举名）

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
        }

        protected override void Start()
        {
            base.Start();
            if (btn_new != null) btn_new.onClick.AddListener(OnClickNew);
            if (btn_save != null) btn_save.onClick.AddListener(OnClickSave);
            if (btn_close != null) btn_close.onClick.AddListener(() => Hide());
            if (btn_add_rule != null) btn_add_rule.onClick.AddListener(OnClickAddRule);
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            KeywordData.Load();
            RefreshList();
            SelectKeyword(selected);
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
                    string tag = keywords[i].HasMechanic ? "" : "（纯展示）";
                    txt.text = $"{keywords[i].title}  [{keywords[i].id}]{tag}";
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
            if (dropdown_status != null)
            {
                int value = kw != null ? (int)kw.status_type : 0;
                int idx = STATUS_VALUES.IndexOf(value);
                dropdown_status.interactable = kw != null;
                dropdown_status.SetValueWithoutNotify(idx < 0 ? 0 : idx);
            }
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
            editor.OpenForKeyword(kw, rule);
            editor.Show();
            Hide();
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
            if (dropdown_status != null && dropdown_status.options.Count > 0)
            {
                int idx = Mathf.Clamp(dropdown_status.value, 0, STATUS_VALUES.Count - 1);
                kw.status_type = (StatusType)STATUS_VALUES[idx];
            }
            Workshop.KeywordAssetIO.SaveAsset(kw);
            RefreshList();
            SetStatus("已保存: " + kw.title);
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
