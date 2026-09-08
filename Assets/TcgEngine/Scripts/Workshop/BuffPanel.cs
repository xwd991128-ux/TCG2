using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 增益管理系统面板（游戏内 Unity 组件版，与卡池管理 CardPoolPanel 平级）。
    /// 增益是全局资源（Workshop/buffs.json，与卡池文件同目录），不属于任何单个卡池，
    /// 因此独立成页：左侧增益列表 + 右侧属性表编辑（攻击/生命加成自动参与战斗，自定义属性供规则图读写）。
    /// 面板由 Editor 工具（BuffPanelBuilder）在场景中用 Unity UI 组件搭建，
    /// 所有 UI 引用在 Inspector 中手动绑定，运行时不动态创建界面。
    /// </summary>
    public class BuffPanel : UIPanel
    {
        [Header("列表")]
        public ScrollRect buff_list_scroll;          // 增益列表滚动区
        public RectTransform buff_list_content;      // 列表容器
        public GameObject buff_list_template;        // 列表项模板（隐藏）

        [Header("编辑区")]
        public InputField buff_name_input;           // 增益名称
        public Dropdown buff_category_dropdown;      // 分类（增益/减益/光环/印记）
        public InputField buff_desc_input;           // 描述
        public InputField buff_duration_input;       // 持续回合（0=永久）
        public RectTransform buff_prop_content;      // 属性表容器（攻击加成/生命加成/自定义 key-value 行）
        public GameObject buff_prop_template;        // 属性行模板（隐藏：key + value + 删除）
        public Button btn_buff_add_prop;             // 新增属性行

        [Header("工具栏")]
        public Button btn_buff_new;                  // 新增增益
        public Button btn_buff_copy;                 // 复制增益
        public Button btn_buff_del;                  // 删除增益
        public Button btn_buff_save;                 // 保存增益池（写盘 buffs.json）
        public Button btn_buff_edit_graph;           // 编辑效果：进入规则编辑器编辑当前增益的效果图（BuffData.graph）
        public Text status_text;                     // 状态提示

        private static BuffPanel instance;
        public static BuffPanel Get() { return instance; }

        private BuffData editing_buff;                          // 当前编辑的增益定义
        private string selected_buff_id;                        // 列表选中的增益 id
        private readonly List<GameObject> buff_prop_rows = new List<GameObject>();   // 属性行实例（key+value+删除）
        private readonly Dictionary<string, Image> buff_row_imgs = new Dictionary<string, Image>();  // 列表项选中高亮
        private static readonly string[] BUFF_CATEGORIES = { "增益", "减益", "光环", "印记" };

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            if (btn_buff_new != null) btn_buff_new.onClick.AddListener(OnBuffNew);
            if (btn_buff_copy != null) btn_buff_copy.onClick.AddListener(OnBuffCopy);
            if (btn_buff_del != null) btn_buff_del.onClick.AddListener(OnBuffDel);
            if (btn_buff_save != null) btn_buff_save.onClick.AddListener(OnBuffSave);
            if (btn_buff_edit_graph != null) btn_buff_edit_graph.onClick.AddListener(OnEditGraph);
            if (btn_buff_add_prop != null) btn_buff_add_prop.onClick.AddListener(OnBuffAddProp);
            if (buff_category_dropdown != null)
            {
                buff_category_dropdown.ClearOptions();
                buff_category_dropdown.AddOptions(new List<string>(BUFF_CATEGORIES));
                buff_category_dropdown.onValueChanged.AddListener((v) =>
                {
                    if (editing_buff != null && v >= 0 && v < BUFF_CATEGORIES.Length)
                        editing_buff.category = BUFF_CATEGORIES[v];
                });
            }
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            RefreshBuffList();
            List<BuffData> all = BuffPoolIO.GetAll();
            if (all.Count > 0)
            {
                SelectBuff(all[0].id);
            }
            else
            {
                editing_buff = null;
                selected_buff_id = null;
                LoadBuffEdit();
            }
            SetStatus("增益设计：攻击/生命加成自动参与战斗，自定义属性供规则图读写");
        }

        // ---------------- 列表 ----------------

        /// <summary>刷新增益列表（清空旧项，按 BuffPoolIO 池重建）</summary>
        private void RefreshBuffList()
        {
            if (buff_list_content == null || buff_list_template == null)
                return;
            buff_row_imgs.Clear();
            for (int i = buff_list_content.childCount - 1; i >= 0; i--)
            {
                GameObject go = buff_list_content.GetChild(i).gameObject;
                if (go != buff_list_template)
                    Destroy(go);
            }
            List<BuffData> all = BuffPoolIO.GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                BuffData b = all[i];
                if (b == null)
                    continue;
                GameObject inst = Instantiate(buff_list_template, buff_list_content);
                inst.name = "BuffItem_" + i;
                inst.SetActive(true);
                Text t = inst.GetComponentInChildren<Text>(true);
                if (t != null)
                {
                    string cat = string.IsNullOrEmpty(b.category) ? "" : " [" + b.category + "]";
                    t.text = (string.IsNullOrEmpty(b.title) ? b.id : b.title) + cat;
                }
                Button btn = inst.GetComponent<Button>();
                if (btn == null)
                    btn = inst.AddComponent<Button>();
                Image img = inst.GetComponent<Image>();
                if (img == null)
                    img = inst.AddComponent<Image>();
                string bid = b.id;
                btn.onClick.AddListener(() => SelectBuff(bid));
                if (!buff_row_imgs.ContainsKey(bid))
                    buff_row_imgs[bid] = img;
            }
            ApplyBuffListHighlight();
        }

        private void ApplyBuffListHighlight()
        {
            foreach (KeyValuePair<string, Image> kv in buff_row_imgs)
            {
                if (kv.Value != null)
                    kv.Value.color = (kv.Key == selected_buff_id)
                        ? new Color(0.5f, 0.78f, 1f, 0.5f)
                        : new Color(1f, 1f, 1f, 0.08f);
            }
        }

        /// <summary>选中增益：加载到右侧编辑区</summary>
        private void SelectBuff(string buff_id)
        {
            BuffData b = BuffPoolIO.Get(buff_id);
            if (b == null)
                return;
            editing_buff = b;
            selected_buff_id = b.id;
            ApplyBuffListHighlight();
            LoadBuffEdit();
        }

        /// <summary>把当前编辑的增益定义刷到右侧控件（新建/切换选中时调用）</summary>
        private void LoadBuffEdit()
        {
            if (buff_name_input == null || buff_duration_input == null)
                return;
            if (editing_buff == null)
            {
                buff_name_input.text = "";
                buff_desc_input.text = "";
                buff_duration_input.text = "0";
                if (buff_category_dropdown != null)
                    buff_category_dropdown.value = 0;
                ClearBuffPropRows();
                return;
            }
            buff_name_input.text = editing_buff.title ?? "";
            buff_desc_input.text = editing_buff.desc ?? "";
            //每次加载重绑写回（RemoveAllListeners 防止切换选中时监听器累积）
            buff_name_input.onEndEdit.RemoveAllListeners();
            buff_name_input.onEndEdit.AddListener((s) =>
            {
                if (editing_buff == null)
                    return;
                editing_buff.title = s;
                RefreshBuffList();   //改名后同步刷新左侧列表项标题（保持选中高亮）
            });
            buff_desc_input.onEndEdit.RemoveAllListeners();
            buff_desc_input.onEndEdit.AddListener((s) => { if (editing_buff != null) editing_buff.desc = s; });
            buff_duration_input.text = editing_buff.duration.ToString();
            int cat_idx = Array.IndexOf(BUFF_CATEGORIES, editing_buff.category);
            if (buff_category_dropdown != null && cat_idx >= 0)
                buff_category_dropdown.value = cat_idx;
            //属性表
            ClearBuffPropRows();
            List<BuffProp> props = editing_buff.props ?? new List<BuffProp>();
            if (props.Count == 0)
            {
                //新增益保证至少攻击/生命两行
                props.Add(new BuffProp("攻击加成", 0));
                props.Add(new BuffProp("生命加成", 0));
                editing_buff.props = props;
            }
            foreach (BuffProp p in props)
                CreateBuffPropRow(p);
        }

        /// <summary>创建一行属性编辑（key + value + 删除按钮）</summary>
        private void CreateBuffPropRow(BuffProp p)
        {
            if (buff_prop_template == null || buff_prop_content == null)
                return;
            GameObject inst = Instantiate(buff_prop_template, buff_prop_content);
            inst.name = "PropRow_" + buff_prop_rows.Count;
            inst.SetActive(true);
            buff_prop_rows.Add(inst);

            Transform key_t = inst.transform.Find("KeyInput");
            Transform val_t = inst.transform.Find("ValueInput");
            Transform del_t = inst.transform.Find("DelBtn");
            if (key_t != null)
            {
                InputField key_in = key_t.GetComponent<InputField>();
                if (key_in == null) key_in = key_t.GetComponentInChildren<InputField>(true);
                if (key_in != null)
                {
                    key_in.text = p.key ?? "";
                    key_in.onEndEdit.AddListener((s) =>
                    {
                        if (editing_buff != null && !string.IsNullOrEmpty(s))
                            p.key = s;
                    });
                }
            }
            if (val_t != null)
            {
                InputField val_in = val_t.GetComponent<InputField>();
                if (val_in == null) val_in = val_t.GetComponentInChildren<InputField>(true);
                if (val_in != null)
                {
                    val_in.text = p.value.ToString();
                    val_in.onEndEdit.AddListener((s) =>
                    {
                        if (editing_buff != null && int.TryParse(s, out int v))
                            p.value = v;
                    });
                }
            }
            if (del_t != null)
            {
                Button del_btn = del_t.GetComponent<Button>();
                if (del_btn == null) del_btn = del_t.GetComponentInChildren<Button>(true);
                if (del_btn != null)
                    del_btn.onClick.AddListener(() => RemoveBuffPropRow(inst, p));
            }
        }

        private void RemoveBuffPropRow(GameObject row, BuffProp p)
        {
            buff_prop_rows.Remove(row);
            Destroy(row);
            if (editing_buff != null && editing_buff.props != null)
                editing_buff.props.Remove(p);
        }

        private void ClearBuffPropRows()
        {
            foreach (GameObject row in buff_prop_rows)
            {
                if (row != null)
                    Destroy(row);
            }
            buff_prop_rows.Clear();
        }

        private void OnBuffAddProp()
        {
            if (editing_buff == null)
            {
                SetStatus("请先选中或新建一个增益");
                return;
            }
            if (editing_buff.props == null)
                editing_buff.props = new List<BuffProp>();
            BuffProp p = new BuffProp("自定义属性", 0);
            editing_buff.props.Add(p);
            CreateBuffPropRow(p);
        }

        private void OnBuffNew()
        {
            BuffData b = BuffPoolIO.New();
            RefreshBuffList();
            SelectBuff(b.id);
            SetStatus("已新建增益（记得点「保存」写盘）");
        }

        private void OnBuffCopy()
        {
            if (editing_buff == null)
                return;
            BuffData b = BuffPoolIO.Duplicate(editing_buff);
            RefreshBuffList();
            SelectBuff(b.id);
            SetStatus("已复制增益（记得点「保存」写盘）");
        }

        private void OnBuffDel()
        {
            if (editing_buff == null)
                return;
            string bid = editing_buff.id;
            BuffPoolIO.Remove(editing_buff);
            editing_buff = null;
            selected_buff_id = null;
            RefreshBuffList();
            List<BuffData> all = BuffPoolIO.GetAll();
            if (all.Count > 0)
                SelectBuff(all[0].id);
            else
                LoadBuffEdit();
            SetStatus("已删除增益 " + bid + "（记得点「保存」写盘）");
        }

        private void OnBuffSave()
        {
            //写回输入框当前值（onEndEdit 已写，这里兜底防止未失焦直接点保存）
            if (editing_buff != null && buff_duration_input != null)
            {
                if (int.TryParse(buff_duration_input.text, out int dur))
                    editing_buff.duration = Mathf.Max(dur, 0);
                if (buff_name_input != null && !string.IsNullOrEmpty(buff_name_input.text))
                    editing_buff.title = buff_name_input.text;
                if (buff_desc_input != null)
                    editing_buff.desc = buff_desc_input.text;
            }
            BuffPoolIO.SaveAll();
            SetStatus("增益池已保存 → Workshop/buffs.json");
        }

        /// <summary>「编辑效果」：进入全屏规则编辑器编辑当前增益的效果图（BuffData.graph，入口=增益触发事件节点）。
        /// 编辑的是引用，保存时由 GraphEditorPanel 写回 buffs.json；返回时走 NotifyGraphClosed 刷新。</summary>
        private void OnEditGraph()
        {
            if (editing_buff == null)
            {
                SetStatus("请先在列表中选择一个增益");
                return;
            }
            //兜底写回输入框当前值（未失焦直接点按钮）
            if (buff_duration_input != null)
            {
                if (int.TryParse(buff_duration_input.text, out int dur))
                    editing_buff.duration = Mathf.Max(dur, 0);
                if (buff_name_input != null && !string.IsNullOrEmpty(buff_name_input.text))
                    editing_buff.title = buff_name_input.text;
                if (buff_desc_input != null)
                    editing_buff.desc = buff_desc_input.text;
            }

            GraphEditorPanel editor = GraphEditorPanel.Get();
            if (editor == null)
                editor = FindObjectOfType<GraphEditorPanel>(true); //含失活对象
            if (editor == null)
            {
                SetStatus("未找到规则编辑器面板，请先运行「生成规则编辑器页面」工具");
                return;
            }

            editor.OpenBuff(editing_buff);
            editor.Show();
            Hide();
        }

        /// <summary>规则编辑器关闭后返回：刷新增益列表与编辑区（属性/效果图已在规则编辑器中修改并保存）</summary>
        public void NotifyGraphClosed()
        {
            RefreshBuffList();
            if (editing_buff != null)
                SelectBuff(editing_buff.id);
        }

        private void SetStatus(string msg)
        {
            if (status_text != null)
                status_text.text = msg;
        }
    }
}
