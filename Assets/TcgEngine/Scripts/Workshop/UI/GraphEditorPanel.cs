using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TcgEngine;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡牌规则编辑器面板（P2+ 全屏节点编辑器）。
    /// 从卡牌编辑器「进行」按钮进入，编辑单张卡（card）自己的规则图。
    /// 布局：左侧大面积连线画布（可拖拽/缩放节点、引脚间连线）；
    ///       右侧上=卡牌属性配置（图片/音效等）；右侧下=节点库（带筛选器，点击生成节点）。
    /// 由 Editor 工具（GraphEditorBuilder）在 Menu.unity 生成并绑定 UI 引用，
    /// 本类运行时不动态创建界面外壳，仅实例化画布内的节点/引脚/连线（数据驱动）。
    /// </summary>
    public class GraphEditorPanel : UIPanel
    {
        [Header("标题/状态")]
        public Text title_text;              // 页面标题
        public Text status_text;             // 底部状态提示

        [Header("工具栏")]
        public Button btn_save;              // 保存
        public Button btn_test;              // 模拟测试
        public Button btn_close;             // 返回卡牌编辑器
        public Button btn_delete_node;       // 删除选中节点
        public Button btn_zoom_in;           // 放大
        public Button btn_zoom_out;          // 缩小
        public Button btn_reset;             // 复位视图

        [Header("左侧画布")]
        public ScrollRect canvas_scroll;     // 画布滚动区（禁用滚动，仅作裁剪）
        public RectTransform canvas_content; // 节点/连线容器（大画布，自由 2D 空间）
        public GraphCanvas graph_canvas;     // 平移缩放控制器
        public GameObject node_template;     // 节点行模板（隐藏）
        public GameObject link_template;     // 连线模板（隐藏）
        public GameObject pin_template;      // 引脚模板（隐藏）

        [Header("右侧属性区")]
        public InputField input_name;        // 卡牌名称
        public Dropdown dropdown_type;       // 类型
        public Dropdown dropdown_team;       // 阵营
        public Dropdown dropdown_rarity;     // 稀有度
        public Dropdown dropdown_trait;      // 种族（特质）
        public InputField input_mana;        // 费用
        public InputField input_attack;      // 攻击
        public InputField input_hp;          // 生命
        public InputField input_text;        // 卡牌文本
        public InputField input_desc;        // 描述
        public Toggle toggle_deckbuilding;   // 可组卡
        public InputField input_cost;        // 购买价
        public Image art_preview;            // 卡面图片预览
        public Button btn_pick_art;          // 选择卡面图片
        public RectTransform art_full_row;   // 面板图片行（法术/奥秘隐藏）
        public Image art_full_preview;       // 面板图片预览
        public Button btn_pick_full_art;     // 选择面板图片
        public InputField input_audio_spawn; // 音效：打出
        public InputField input_audio_attack;// 音效：攻击
        public InputField input_audio_death; // 音效：死亡
        public InputField input_audio_damage;// 音效：受伤
        public Button btn_audio_spawn;       // 选择音频：打出
        public Button btn_audio_attack;      // 选择音频：攻击
        public Button btn_audio_death;       // 选择音频：死亡
        public Button btn_audio_damage;      // 选择音频：受伤

        [Header("右侧节点库")]
        public Button[] filter_buttons;      // 筛选按钮：全部/触发/条件/动作/数值
        public ScrollRect node_lib_scroll;   // 节点库滚动区
        public RectTransform node_lib_content;// 节点库容器
        public GameObject node_lib_template; // 节点库项模板（隐藏）
        public Text node_lib_count;          // 数量提示

        [Header("右侧节点参数编辑区")]
        public RectTransform node_field_area;            // 节点参数编辑区容器（选中节点后填充）
        public GameObject node_field_input_template;     // 参数行模板：输入框
        public GameObject node_field_dropdown_template;  // 参数行模板：下拉框
        public GameObject node_field_toggle_template;    // 参数行模板：开关
        public GameObject node_lib_root;                 // 节点库面板根（与参数面板同位置，互斥切换）
        public GameObject node_field_root;               // 节点参数面板根（与节点库同位置，互斥切换）
        public Text node_field_hint;                     // 参数编辑区占位提示（已由面板切换代替，保留引用兼容旧场景）

        // ---------------- 运行时数据 ----------------
        private static GraphEditorPanel instance;
        private CardPoolData pool;           // 所属卡池（引用，保存时写盘）
        private CardCustomData card;         // 当前编辑的卡
        private string save_path;            // 卡池文件路径
        private GraphData graph;             // 当前卡的规则图（= card.graph）

        private readonly Dictionary<string, RectTransform> node_rows = new Dictionary<string, RectTransform>();
        private readonly List<NodePin> all_pins = new List<NodePin>();
        private readonly List<NodeLink> links = new List<NodeLink>();

        private NodePin drag_from_pin;       // 拖拽连线的起始引脚
        private NodeLink temp_link;          // 拖拽中的临时连线
        private int filter_index = 0;        // 节点库筛选：0全部 1触发 2条件 3动作 4数值
        private string selected_node;        // 选中的节点 id
        private int node_index = 0;          // 新节点位置偏移计数

        private static readonly string[] TYPE_NAMES = { "随从", "法术", "英雄", "神器", "奥秘", "装备" };
        private static readonly string[] TYPE_ENUMS = { "Character", "Spell", "Hero", "Artifact", "Secret", "Equipment" };

        //下拉框选项对应的数据 id（显示名 ↔ id 一一对应）
        private readonly List<string> team_ids = new List<string>();
        private readonly List<string> rarity_ids = new List<string>();
        private readonly List<string> trait_ids = new List<string>();

        // ---------------- 节点库预设 ----------------

        /// <summary>字段编辑方式</summary>
        private enum FieldEditType { Input, Dropdown, Toggle }

        /// <summary>节点字段定义：决定节点参数区用哪种控件编辑（数值输入/枚举下拉/开关）</summary>
        private class FieldDef
        {
            public string name;              // 字段名（写入 node.fields）
            public string display_name;      // 显示名
            public FieldEditType edit;       // 编辑方式
            public string[] options;         // Dropdown 选项
            public string def;               // 默认值
            public FieldDef(string name, string display_name, FieldEditType edit, string[] options, string def)
            {
                this.name = name; this.display_name = display_name;
                this.edit = edit; this.options = options; this.def = def;
            }
        }

        /// <summary>端口定义：节点输入/输出引脚（参考 zmcs/NodeDoc.xml 规范）</summary>
        private class PinDef
        {
            public string name;              // 字段名（引脚 id 后缀）
            public string display_name;      // 显示名
            public NodeValueType type;       // 数据类型（Flow=执行流）
            public bool is_output;
            public bool is_array;
            public PinDef(string name, string display_name, NodeValueType type, bool is_output, bool is_array = false)
            {
                this.name = name; this.display_name = display_name;
                this.type = type; this.is_output = is_output; this.is_array = is_array;
            }
        }

        private class NodePreset
        {
            public GraphNodeType type;
            public string action;
            public string title;
            public string desc;
            public List<FieldDef> fields = new List<FieldDef>();  // 节点参数（数值/枚举）
            public List<PinDef> pins = new List<PinDef>();
        }

        /// <summary>比较/运算等节点的枚举字段定义辅助</summary>
        private static FieldDef EnumField(string name, string display_name, string[] options, string def)
        {
            return new FieldDef(name, display_name, FieldEditType.Dropdown, options, def);
        }
        private static FieldDef IntField(string name, string display_name, string def)
        {
            return new FieldDef(name, display_name, FieldEditType.Input, null, def);
        }
        private static FieldDef BoolField(string name, string display_name, string def)
        {
            return new FieldDef(name, display_name, FieldEditType.Toggle, null, def);
        }

        /// <summary>节点库预设（端口布局参考 zmcs/NodeDoc.xml：左输入 / 右输出，Flow 为执行流，其余为数据流）</summary>
        private static readonly NodePreset[] PRESETS = new NodePreset[]
        {
            // 触发（Event：右侧单个执行流输出）
            new NodePreset { type = GraphNodeType.Event, action = "OnPlay", title = "打出时", desc = "卡牌被使用/打出时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            new NodePreset { type = GraphNodeType.Event, action = "StartOfTurn", title = "回合开始", desc = "己方回合开始时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            new NodePreset { type = GraphNodeType.Event, action = "EndOfTurn", title = "回合结束", desc = "己方回合结束时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            new NodePreset { type = GraphNodeType.Event, action = "OnAttack", title = "攻击时", desc = "该卡发起攻击时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            new NodePreset { type = GraphNodeType.Event, action = "OnDamaged", title = "受到伤害", desc = "该卡受到伤害时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            new NodePreset { type = GraphNodeType.Event, action = "OnDeath", title = "死亡时", desc = "该卡死亡时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            new NodePreset { type = GraphNodeType.Event, action = "OnHeal", title = "受到治疗", desc = "该卡受到治疗时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            // 条件（Condition：左入 + 数据输入，右出真/假分支）
            new NodePreset { type = GraphNodeType.Condition, action = "IfHealth", title = "生命>值", desc = "目标生命大于设定值",
                fields = { IntField("value", "值", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("value", "值", NodeValueType.Int32, false),
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Condition, action = "IfMana", title = "法力>值", desc = "当前法力大于设定值",
                fields = { IntField("value", "值", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("value", "值", NodeValueType.Int32, false),
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Condition, action = "IfRandom", title = "概率判定", desc = "以概率决定走真/假分支",
                fields = { IntField("value", "概率%", "50") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("chance", "概率", NodeValueType.Int32, false),
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Condition, action = "IfTarget", title = "存在目标", desc = "场上存在有效目标",
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            // 动作（Action：左入执行流 + 数据输入，右出执行流）
            new NodePreset { type = GraphNodeType.Action, action = "Damage", title = "造成伤害", desc = "对目标造成 N 点伤害",
                fields = { IntField("value", "伤害值", "2") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("value", "伤害值", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Heal", title = "治疗", desc = "为目标恢复 N 点生命",
                fields = { IntField("value", "治疗量", "2") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("value", "治疗量", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Draw", title = "抽牌", desc = "抽取 N 张牌",
                fields = { IntField("value", "数量", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("count", "数量", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "GainMana", title = "获得法力", desc = "获得 N 点法力水晶",
                fields = { IntField("value", "数量", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("amount", "数量", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Summon", title = "召唤随从", desc = "召唤一个随从",
                fields = { IntField("card_id", "随从ID", "") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("card", "随从", NodeValueType.CardDefine, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Destroy", title = "消灭目标", desc = "消灭目标单位",
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false),
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            // 数值（Value：左数据输入，右数据输出）
            new NodePreset { type = GraphNodeType.Value, action = "Health", title = "目标生命", desc = "读取目标当前生命值",
                pins = {
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("val", "生命", NodeValueType.Int32, true),
                } },
            new NodePreset { type = GraphNodeType.Value, action = "Mana", title = "当前法力", desc = "读取当前法力值",
                pins = { new PinDef("val", "法力", NodeValueType.Int32, true) } },
            new NodePreset { type = GraphNodeType.Value, action = "Attack", title = "目标攻击", desc = "读取目标攻击力",
                pins = {
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("val", "攻击", NodeValueType.Int32, true),
                } },
            new NodePreset { type = GraphNodeType.Value, action = "Random", title = "随机数", desc = "返回 0~N 随机数",
                fields = { IntField("value", "上限", "10") },
                pins = {
                    new PinDef("max", "上限", NodeValueType.Int32, false),
                    new PinDef("val", "结果", NodeValueType.Int32, true),
                } },
            // 通用：常量/运算（对应 NodeDoc 的"其他"分类）
            new NodePreset { type = GraphNodeType.Value, action = "IntegerConst", title = "整数常量", desc = "一个固定的整数值",
                fields = { IntField("value", "数值", "1") },
                pins = { new PinDef("val", "值", NodeValueType.Int32, true) } },
            new NodePreset { type = GraphNodeType.Value, action = "BooleanConst", title = "布尔常量", desc = "固定的真/假值",
                fields = { BoolField("value", "真/假", "true") },
                pins = { new PinDef("val", "值", NodeValueType.Boolean, true) } },
            new NodePreset { type = GraphNodeType.Value, action = "Compare", title = "比较", desc = "比较两个整数（a op b）",
                fields = {
                    IntField("a", "左值", "0"),
                    EnumField("op", "关系", new string[] { ">", "<", ">=", "<=", "==", "!=" }, ">"),
                    IntField("b", "右值", "0"),
                },
                pins = {
                    new PinDef("a", "左值", NodeValueType.Int32, false),
                    new PinDef("b", "右值", NodeValueType.Int32, false),
                    new PinDef("val", "结果", NodeValueType.Boolean, true),
                } },
            new NodePreset { type = GraphNodeType.Value, action = "IntegerOperation", title = "整数运算", desc = "对两个整数做加减乘除",
                fields = {
                    IntField("a", "左值", "0"),
                    EnumField("op", "运算", new string[] { "+", "-", "*", "/", "%" }, "+"),
                    IntField("b", "右值", "0"),
                },
                pins = {
                    new PinDef("a", "左值", NodeValueType.Int32, false),
                    new PinDef("b", "右值", NodeValueType.Int32, false),
                    new PinDef("val", "结果", NodeValueType.Int32, true),
                } },
        };

        public static GraphEditorPanel Get() { return instance; }
        public CardCustomData CurrentCard { get { return card; } }

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            if (btn_save != null) btn_save.onClick.AddListener(OnSave);
            if (btn_test != null) btn_test.onClick.AddListener(OnTest);
            if (btn_close != null) btn_close.onClick.AddListener(OnClose);
            if (btn_delete_node != null) btn_delete_node.onClick.AddListener(OnDeleteNode);
            if (btn_zoom_in != null) btn_zoom_in.onClick.AddListener(() => { if (graph_canvas != null) graph_canvas.ZoomIn(); });
            if (btn_zoom_out != null) btn_zoom_out.onClick.AddListener(() => { if (graph_canvas != null) graph_canvas.ZoomOut(); });
            if (btn_reset != null) btn_reset.onClick.AddListener(ResetView);
            if (btn_pick_art != null) btn_pick_art.onClick.AddListener(OnPickArt);
            if (btn_pick_full_art != null) btn_pick_full_art.onClick.AddListener(OnPickFullArt);
            if (btn_audio_spawn != null) btn_audio_spawn.onClick.AddListener(() => OnPickAudio(0));
            if (btn_audio_attack != null) btn_audio_attack.onClick.AddListener(() => OnPickAudio(1));
            if (btn_audio_death != null) btn_audio_death.onClick.AddListener(() => OnPickAudio(2));
            if (btn_audio_damage != null) btn_audio_damage.onClick.AddListener(() => OnPickAudio(3));
            if (dropdown_type != null) dropdown_type.onValueChanged.AddListener((v) => RefreshPanelArtRow());
            SetupMetaDropdowns();

            if (filter_buttons != null)
            {
                for (int i = 0; i < filter_buttons.Length; i++)
                {
                    int idx = i;
                    if (filter_buttons[i] != null)
                        filter_buttons[i].onClick.AddListener(() => SetFilter(idx));
                }
                //初始「全部」高亮
                for (int i = 0; i < filter_buttons.Length; i++)
                {
                    if (filter_buttons[i] != null)
                    {
                        Text t = filter_buttons[i].GetComponentInChildren<Text>();
                        if (t != null)
                            t.color = (i == filter_index) ? new Color(1f, 0.85f, 0.5f, 1f) : Color.white;
                    }
                }
            }
        }

        // ---------------- 打开/数据 ----------------

        /// <summary>打开某张卡的规则编辑器（pool/card 为引用，修改后保存时整体写盘）</summary>
        public void Open(CardPoolData pool, CardCustomData card, string savePath)
        {
            this.pool = pool;
            this.card = card;
            this.save_path = savePath;

            graph = card != null ? card.graph : null;
            if (graph == null)
            {
                graph = new GraphData();
                if (card != null)
                    card.graph = graph;
            }
            if (graph.name == null || graph.name.Length == 0)
                graph.name = card != null ? card.title : "NewGraph";

            node_index = 0;
            //旧图端口迁移：旧引脚无类型（type=None），按预设重建端口（id 命名不变，连线保持有效）
            foreach (GraphNode n in graph.nodes)
                MigratePins(n);
            RefreshForm();
            RefreshPanelArtRow();
            RefreshArt();
            RebuildCanvas();
            RefreshNodeLib();
            ResetView();

            SetStatus("正在编辑规则图: " + (card != null && !string.IsNullOrEmpty(card.title) ? card.title : "（未命名卡）"));
        }

        // ---------------- 属性区 ----------------

        /// <summary>从 Resources 加载阵营/稀有度/种族预设并填充下拉框（显示名 title ↔ 数据 id）</summary>
        private void SetupMetaDropdowns()
        {
            TeamData.Load();
            RarityData.Load();
            TraitData.Load();
            SetupDropdownOptions(dropdown_team, TeamData.GetAll(), team_ids, t => t.title, t => t.id);
            SetupDropdownOptions(dropdown_rarity, RarityData.GetAll(), rarity_ids, r => r.title, r => r.id);
            SetupDropdownOptions(dropdown_trait, TraitData.GetAll(), trait_ids, t => t.title, t => t.id);
        }

        private void SetupDropdownOptions<T>(Dropdown dropdown, List<T> list, List<string> ids,
            Func<T, string> title, Func<T, string> id) where T : ScriptableObject
        {
            if (dropdown == null)
                return;
            ids.Clear();
            dropdown.ClearOptions();
            List<Dropdown.OptionData> options = new List<Dropdown.OptionData>();
            foreach (T data in list)
            {
                if (data == null)
                    continue;
                ids.Add(id(data));   //用字段 id（如 rarity 的 "rare"），不用 asset 名（"3-rare"）
                options.Add(new Dropdown.OptionData(title(data)));
            }
            dropdown.options = options;
            if (options.Count > 0)
                dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        /// <summary>按下拉框中选中项写回 card 字段（按 id 匹配索引）</summary>
        private static void SetDropdown(Dropdown dropdown, List<string> ids, string id)
        {
            if (dropdown == null)
                return;
            int idx = string.IsNullOrEmpty(id) ? 0 : ids.IndexOf(id);
            if (idx < 0)
                idx = 0;
            if (idx < dropdown.options.Count)
                dropdown.value = idx;
        }

        /// <summary>读取下拉框当前选中项对应的数据 id（无匹配返回原值）</summary>
        private static string GetDropdown(Dropdown dropdown, List<string> ids, string def)
        {
            if (dropdown == null)
                return def;
            int idx = dropdown.value;
            if (idx >= 0 && idx < ids.Count)
                return ids[idx];
            return def;
        }

        private void RefreshForm()
        {
            if (card == null)
                return;
            SetInput(input_name, card.title);
            SetInput(input_mana, card.mana.ToString());
            SetInput(input_attack, card.attack.ToString());
            SetInput(input_hp, card.hp.ToString());
            SetInput(input_text, card.text);
            SetInput(input_desc, card.desc);
            SetInput(input_cost, card.cost.ToString());
            SetInput(input_audio_spawn, card.spawn_audio_id);
            SetInput(input_audio_attack, card.attack_audio_id);
            SetInput(input_audio_death, card.death_audio_id);
            SetInput(input_audio_damage, card.damage_audio_id);

            if (toggle_deckbuilding != null)
                toggle_deckbuilding.isOn = card.deckbuilding;
            SetDropdown(dropdown_team, team_ids, card.team);
            SetDropdown(dropdown_rarity, rarity_ids, card.rarity);
            SetDropdown(dropdown_trait, trait_ids, card.trait);

            if (dropdown_type != null)
            {
                int idx = IndexOf(TYPE_ENUMS, card.type);
                if (idx >= 0 && idx < dropdown_type.options.Count)
                    dropdown_type.value = idx;
            }
            if (title_text != null)
                title_text.text = "规则编辑 - " + (string.IsNullOrEmpty(card.title) ? "未命名" : card.title);
        }

        private void ReadForm()
        {
            if (card == null)
                return;
            card.title = GetInput(input_name, card.title);
            card.mana = GetInputInt(input_mana, card.mana);
            card.attack = GetInputInt(input_attack, card.attack);
            card.hp = GetInputInt(input_hp, card.hp);
            card.text = GetInput(input_text, card.text);
            card.desc = GetInput(input_desc, card.desc);
            card.cost = GetInputInt(input_cost, card.cost);
            card.spawn_audio_id = GetInput(input_audio_spawn, card.spawn_audio_id);
            card.attack_audio_id = GetInput(input_audio_attack, card.attack_audio_id);
            card.death_audio_id = GetInput(input_audio_death, card.death_audio_id);
            card.damage_audio_id = GetInput(input_audio_damage, card.damage_audio_id);

            if (toggle_deckbuilding != null)
                card.deckbuilding = toggle_deckbuilding.isOn;
            card.team = GetDropdown(dropdown_team, team_ids, card.team);
            card.rarity = GetDropdown(dropdown_rarity, rarity_ids, card.rarity);
            card.trait = GetDropdown(dropdown_trait, trait_ids, card.trait);

            if (dropdown_type != null && dropdown_type.value >= 0 && dropdown_type.value < TYPE_ENUMS.Length)
                card.type = TYPE_ENUMS[dropdown_type.value];
        }

        private void RefreshArt()
        {
            if (card == null)
                return;
            if (art_preview != null)
            {
                art_preview.sprite = CardPoolIO.LoadArt(card.art_path);
                art_preview.enabled = art_preview.sprite != null;
            }
            if (art_full_preview != null)
            {
                art_full_preview.sprite = CardPoolIO.LoadArt(card.art_full_path);
                art_full_preview.enabled = art_full_preview.sprite != null;
            }
        }

        /// <summary>面板图片行：法术/奥秘不需要面板图，隐藏该行（按下拉框当前类型判断）</summary>
        private void RefreshPanelArtRow()
        {
            if (art_full_row == null)
                return;
            string t = card != null ? card.type : "";
            if (dropdown_type != null && dropdown_type.value >= 0 && dropdown_type.value < TYPE_ENUMS.Length)
                t = TYPE_ENUMS[dropdown_type.value];
            bool show = t != "Spell" && t != "Secret";
            if (art_full_row.gameObject.activeSelf != show)
                art_full_row.gameObject.SetActive(show);
        }

        private void OnPickArt()
        {
            if (card == null)
                return;
            string[] files = FileDialogTool.OpenFiles("选择卡牌图片", "图片文件 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", false);
            if (files == null || files.Length == 0)
            {
                SetStatus("未选择图片");
                return;
            }
            try
            {
                string src = files[0];
                string ext = Path.GetExtension(src);
                if (string.IsNullOrEmpty(ext))
                    ext = ".png";
                string fname = (string.IsNullOrEmpty(card.id) ? "art_" + Guid.NewGuid().ToString("N").Substring(0, 8) : card.id) + ext;
                Directory.CreateDirectory(CardPoolIO.ArtFolder);
                string dst = Path.Combine(CardPoolIO.ArtFolder, fname);
                File.Copy(src, dst, true);
                card.art_path = fname;
                CardPoolIO.UpdateCardData(card);   //立即同步运行时卡面，避免选图后卡牌上不更新
                RefreshArt();
                SetStatus("已设置卡牌图片: " + fname);
            }
            catch (Exception e)
            {
                Debug.LogError("设置卡牌图片失败: " + e.Message);
                SetStatus("设置卡牌图片失败: " + e.Message);
            }
        }

        /// <summary>选择面板（全图）图片，复制到 ArtFolder 并写入 card.art_full_path</summary>
        private void OnPickFullArt()
        {
            if (card == null)
                return;
            string[] files = FileDialogTool.OpenFiles("选择面板图片", "图片文件 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", false);
            if (files == null || files.Length == 0)
            {
                SetStatus("未选择面板图片");
                return;
            }
            try
            {
                string src = files[0];
                string ext = Path.GetExtension(src);
                if (string.IsNullOrEmpty(ext))
                    ext = ".png";
                string fname = (string.IsNullOrEmpty(card.id) ? "full_" + Guid.NewGuid().ToString("N").Substring(0, 8) : card.id + "_full") + ext;
                Directory.CreateDirectory(CardPoolIO.ArtFolder);
                string dst = Path.Combine(CardPoolIO.ArtFolder, fname);
                File.Copy(src, dst, true);
                card.art_full_path = fname;
                CardPoolIO.UpdateCardData(card);   //立即同步运行时卡面，避免选图后卡牌上不更新
                RefreshArt();
                SetStatus("已设置面板图片: " + fname);
            }
            catch (Exception e)
            {
                Debug.LogError("设置面板图片失败: " + e.Message);
                SetStatus("设置面板图片失败: " + e.Message);
            }
        }

        /// <summary>选择音频文件（slot：0打出 1攻击 2死亡 3受伤），复制到 AudioFolder 并写入对应字段</summary>
        private void OnPickAudio(int slot)
        {
            if (card == null)
                return;
            string[] files = FileDialogTool.OpenFiles("选择音频文件", "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg", false);
            if (files == null || files.Length == 0)
            {
                SetStatus("未选择音频");
                return;
            }
            try
            {
                string src = files[0];
                string ext = Path.GetExtension(src);
                if (string.IsNullOrEmpty(ext))
                    ext = ".wav";
                string fname = (string.IsNullOrEmpty(card.id) ? "audio_" + Guid.NewGuid().ToString("N").Substring(0, 8) : card.id) + "_" + slot + ext;
                Directory.CreateDirectory(CardPoolIO.AudioFolder);
                string dst = Path.Combine(CardPoolIO.AudioFolder, fname);
                File.Copy(src, dst, true);

                switch (slot)
                {
                    case 0: card.spawn_audio_id = fname; SetInput(input_audio_spawn, fname); break;
                    case 1: card.attack_audio_id = fname; SetInput(input_audio_attack, fname); break;
                    case 2: card.death_audio_id = fname; SetInput(input_audio_death, fname); break;
                    case 3: card.damage_audio_id = fname; SetInput(input_audio_damage, fname); break;
                }
                SetStatus("已设置音效: " + fname);
            }
            catch (Exception e)
            {
                Debug.LogError("设置音效失败: " + e.Message);
                SetStatus("设置音效失败: " + e.Message);
            }
        }

        // ---------------- 节点库 ----------------

        private void RefreshNodeLib()
        {
            if (node_lib_content == null)
                return;

            //清除旧项（保留模板）
            for (int i = node_lib_content.childCount - 1; i >= 0; i--)
            {
                Transform child = node_lib_content.GetChild(i);
                if (child != null && child.gameObject != node_lib_template)
                    Destroy(child.gameObject);
            }

            int shown = 0;
            for (int i = 0; i < PRESETS.Length; i++)
            {
                if (filter_index != 0 && (int)PRESETS[i].type != filter_index - 1)
                    continue;
                CreateNodeLibItem(PRESETS[i]);
                shown++;
            }
            if (node_lib_count != null)
                node_lib_count.text = "节点库（" + shown + " 个）";
        }

        private void CreateNodeLibItem(NodePreset preset)
        {
            if (node_lib_template == null)
                return;
            GameObject inst = Instantiate(node_lib_template, node_lib_content);
            inst.name = "Lib_" + preset.action;
            inst.SetActive(true);

            Text title = inst.transform.Find("TitleText")?.GetComponent<Text>();
            if (title != null)
                title.text = preset.title;

            //端口概要（▸输出 ◂输入）
            Text desc = inst.transform.Find("DescText")?.GetComponent<Text>();
            if (desc != null)
                desc.text = PortSummary(preset);

            Button btn = inst.GetComponent<Button>();
            if (btn == null)
                btn = inst.AddComponent<Button>();
            btn.onClick.AddListener(() => AddNodeFromPreset(preset));
        }

        private void SetFilter(int idx)
        {
            if (idx < 0 || idx >= filter_buttons.Length)
                return;
            filter_index = idx;
            for (int i = 0; i < filter_buttons.Length; i++)
            {
                if (filter_buttons[i] != null)
                {
                    Text t = filter_buttons[i].GetComponentInChildren<Text>();
                    if (t != null)
                        t.color = (i == idx) ? new Color(1f, 0.85f, 0.5f, 1f) : Color.white;
                }
            }
            RefreshNodeLib();
        }

        /// <summary>从预设创建节点并添加到画布（点击节点库项）</summary>
        private void AddNodeFromPreset(NodePreset preset)
        {
            if (graph == null)
                return;

            GraphNode node = new GraphNode();
            node.id = "n_" + GameTool.GenerateRandomID(6, 10);
            node.type = preset.type;
            node.action = preset.action;
            node.title = preset.title;

            //默认位置：画布中部向左下逐行排布，避免重叠
            float start_x = 260f;
            float start_y = -140f;
            float col = node_index % 3;
            float row = node_index / 3;
            node.pos = new Vector2Data(start_x + col * 220f, start_y - row * 120f);
            node_index++;

            //引脚（按预设端口定义生成：左输入/右输出，带类型）
            BuildPins(node, preset);
            //默认字段（按字段定义初始化，缺失才补默认值）
            foreach (FieldDef fd in preset.fields)
            {
                if (!HasField(node, fd.name))
                    node.fields.Add(new FieldCustomData { name = fd.name, value = fd.def ?? "" });
            }

            graph.nodes.Add(node);
            CreateNodeUI(node);
            SelectNode(node.id);   //自动选中新节点，右侧立即显示可编辑参数
            SetStatus("已添加节点: " + node.title + "（可编辑右侧参数）");
        }

        /// <summary>按预设端口定义生成引脚（PinDef → GraphPin）</summary>
        private void BuildPins(GraphNode node, NodePreset preset)
        {
            if (node == null || preset == null)
                return;
            foreach (PinDef pd in preset.pins)
            {
                node.pins.Add(new GraphPin
                {
                    id = node.id + "_" + pd.name,
                    name = pd.name,
                    display_name = pd.display_name,
                    type = pd.type,
                    is_output = pd.is_output,
                    is_array = pd.is_array,
                });
            }
        }

        /// <summary>按类型+动作查找预设</summary>
        private static NodePreset FindPreset(GraphNodeType type, string action)
        {
            foreach (NodePreset p in PRESETS)
            {
                if (p.type == type && p.action == action)
                    return p;
            }
            return null;
        }

        /// <summary>旧图数据迁移：旧节点引脚无类型（type=None），按预设重建端口（id 命名规则不变，连线保持有效）</summary>
        private static void MigratePins(GraphNode node)
        {
            if (node == null || node.pins == null || node.pins.Count == 0)
                return;
            bool need_migrate = false;
            foreach (GraphPin p in node.pins)
            {
                if (p.type == NodeValueType.None)
                {
                    need_migrate = true;
                    break;
                }
            }
            if (!need_migrate)
                return;
            NodePreset preset = FindPreset(node.type, node.action);
            if (preset == null)
                return;
            node.pins.Clear();
            foreach (PinDef pd in preset.pins)
            {
                node.pins.Add(new GraphPin
                {
                    id = node.id + "_" + pd.name,
                    name = pd.name,
                    display_name = pd.display_name,
                    type = pd.type,
                    is_output = pd.is_output,
                    is_array = pd.is_array,
                });
            }
        }

        /// <summary>端口概要（节点库/节点显示用）</summary>
        private static string PortSummary(NodePreset preset)
        {
            if (preset == null)
                return "";
            if (preset.pins.Count == 0)
                return preset.desc;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (PinDef pd in preset.pins)
            {
                if (sb.Length > 0)
                    sb.Append("  ");
                sb.Append(pd.is_output ? "▸" : "◂");
                sb.Append(pd.display_name);
            }
            return sb.ToString();
        }

        // ---------------- 画布：节点/连线 ----------------

        private void RebuildCanvas()
        {
            if (canvas_content == null)
                return;

            //清除旧节点/连线/临时线（保留模板）
            for (int i = canvas_content.childCount - 1; i >= 0; i--)
            {
                GameObject child = canvas_content.GetChild(i).gameObject;
                if (child != node_template && child != link_template && child != pin_template)
                    Destroy(child);
            }
            node_rows.Clear();
            all_pins.Clear();
            links.Clear();
            temp_link = null;
            selected_node = null;

            if (graph == null)
                return;

            //先建节点
            foreach (GraphNode node in graph.nodes)
                CreateNodeUI(node);

            //再建连线（置底，避免遮挡节点）
            foreach (GraphLink link in graph.links)
                CreateLinkUI(link);

            //重建后无选中节点，参数编辑区回到占位提示
            RefreshNodeFields(null);
        }

        private void CreateNodeUI(GraphNode node)
        {
            if (node_template == null)
                return;

            GameObject inst = Instantiate(node_template, canvas_content);
            inst.name = "Node_" + node.id;
            inst.SetActive(true);

            RectTransform rect = inst.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(node.pos.x, node.pos.y);

            Text type = inst.transform.Find("TypeText")?.GetComponent<Text>();
            if (type != null)
                type.text = NodeTypeLabel(node.type);
            Text title = inst.transform.Find("TitleText")?.GetComponent<Text>();
            if (title != null)
                title.text = node.title;
            Text desc = inst.transform.Find("DescText")?.GetComponent<Text>();
            if (desc != null)
                desc.text = NodeSummary(node);

            //引脚
            Transform pins_root = inst.transform.Find("Pins");
            if (pins_root != null)
            {
                foreach (GraphPin pin in node.pins)
                    CreatePinUI(node, pin, pins_root, rect);
            }

            //拖拽
            NodeDragger dragger = inst.GetComponent<NodeDragger>();
            if (dragger == null)
                dragger = inst.AddComponent<NodeDragger>();
            dragger.Setup(node.id, MoveNode, (id, r) => OnNodeMoved(id, r.anchoredPosition));

            //点击选中
            NodeClick click = inst.GetComponent<NodeClick>();
            if (click == null)
                click = inst.AddComponent<NodeClick>();
            click.Setup(node.id, SelectNode);

            node_rows[node.id] = rect;
            ApplySelectHighlight(node.id);
        }

        private void CreatePinUI(GraphNode node, GraphPin pin, Transform pins_root, RectTransform node_rect)
        {
            if (pin_template == null)
                return;

            GameObject inst = Instantiate(pin_template, pins_root);
            inst.name = "Pin_" + (pin.is_output ? "O" : "I") + "_" + pin.name;
            inst.SetActive(true);

            //引脚相对节点左下角的偏移：输入靠左缘，输出靠右缘，垂直居中略偏下
            float y = -12f;
            for (int i = 0; i < node.pins.Count; i++)
            {
                if (node.pins[i] == pin)
                {
                    y = -20f - i * 22f;
                    break;
                }
            }
            float x = pin.is_output ? 190f : 0f;
            RectTransform prt = inst.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;   //锚定节点左下角
            prt.anchorMax = Vector2.zero;
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(x, y);

            NodePin node_pin = inst.GetComponent<NodePin>();
            if (node_pin == null)
                node_pin = inst.AddComponent<NodePin>();
            node_pin.Setup(this, node.id, pin.id, pin.is_output, node_rect, prt.anchoredPosition);
            all_pins.Add(node_pin);

            //引脚颜色：Flow=执行流（输出青/输入橙），数据流按类型区分
            Transform dot = inst.transform.Find("Dot");
            Image pimg = dot != null ? dot.GetComponent<Image>() : inst.GetComponent<Image>();
            if (pimg != null)
                pimg.color = PinColor(pin);
        }

        /// <summary>引脚颜色：执行流用青/橙，数据流按类型着色（参考 NodeDoc 端口类型）</summary>
        private static Color PinColor(GraphPin pin)
        {
            if (pin.type == NodeValueType.Flow || pin.type == NodeValueType.None)
                return pin.is_output ? new Color(0.3f, 0.9f, 1f, 1f) : new Color(1f, 0.7f, 0.4f, 1f);
            switch (pin.type)
            {
                case NodeValueType.Int32: return new Color(0.5f, 1f, 0.55f, 1f);    //绿
                case NodeValueType.Boolean: return new Color(0.85f, 0.6f, 1f, 1f);  //紫
                case NodeValueType.Card: return new Color(1f, 0.5f, 0.5f, 1f);      //红
                case NodeValueType.CardDefine: return new Color(1f, 0.7f, 0.4f, 1f);//橙
                case NodeValueType.EventArg: return new Color(1f, 0.9f, 0.4f, 1f);  //黄
                default: return new Color(0.8f, 0.8f, 0.8f, 1f);                     //灰
            }
        }

        private void CreateLinkUI(GraphLink link)
        {
            if (link_template == null)
                return;

            GameObject inst = Instantiate(link_template, canvas_content);
            inst.name = "Link_" + link.from_node + "->" + link.to_node;
            inst.SetActive(true);
            inst.transform.SetAsFirstSibling(); //置底，避免遮挡节点

            NodeLink nl = inst.GetComponent<NodeLink>();
            if (nl == null)
                nl = inst.AddComponent<NodeLink>();
            nl.Setup(inst.GetComponent<RectTransform>());
            nl.from_node = link.from_node;
            nl.from_pin = link.from_pin;
            nl.to_node = link.to_node;
            nl.to_pin = link.to_pin;

            NodePin from = FindPin(link.from_node, link.from_pin);
            NodePin to = FindPin(link.to_node, link.to_pin);
            nl.SetEndpoints(from, to);
            nl.Redraw();
            links.Add(nl);
        }

        private NodePin FindPin(string node_id, string pin_id)
        {
            foreach (NodePin p in all_pins)
            {
                if (p.node_id == node_id && p.pin_id == pin_id)
                    return p;
            }
            return null;
        }

        /// <summary>重绘所有连线（节点移动后调用）</summary>
        private void RedrawLinks()
        {
            foreach (NodeLink nl in links)
                nl.Redraw();
        }

        /// <summary>节点拖拽中：增量移动（除以缩放，保证手感）</summary>
        public void MoveNode(string node_id, RectTransform row, PointerEventData eventData)
        {
            if (row == null)
                return;
            float scale = canvas_content != null && canvas_content.localScale.x > 0.001f ? canvas_content.localScale.x : 1f;
            row.anchoredPosition += eventData.delta / scale;
            //移动时实时重绘相连的线
            foreach (NodeLink nl in links)
            {
                if (nl.from_node == node_id || nl.to_node == node_id)
                    nl.Redraw();
            }
        }

        /// <summary>节点拖拽结束：写回位置</summary>
        public void OnNodeMoved(string node_id, Vector2 pos)
        {
            if (graph == null)
                return;
            GraphNode node = graph.GetNode(node_id);
            if (node == null)
                return;
            node.pos = new Vector2Data(pos.x, pos.y);
            SetStatus("节点已移动，记得保存");
        }

        /// <summary>选中节点并高亮（同时刷新右侧参数编辑区）</summary>
        private void SelectNode(string node_id)
        {
            selected_node = node_id;
            foreach (var kv in node_rows)
                ApplySelectHighlight(kv.Key);
            GraphNode node = graph != null ? graph.GetNode(node_id) : null;
            RefreshNodeFields(node);
            SetStatus("已选中节点，可编辑右侧参数（记得保存）");
        }

        private void ApplySelectHighlight(string node_id)
        {
            if (node_rows.TryGetValue(node_id, out RectTransform rect))
            {
                Image bg = rect.Find("LineBG")?.GetComponent<Image>();
                if (bg != null)
                    bg.color = (node_id == selected_node)
                        ? new Color(0.35f, 0.65f, 0.95f, 0.45f)
                        : new Color(1f, 1f, 1f, 0.12f);
            }
        }

        // ---------------- 节点参数编辑区 ----------------

        /// <summary>按选中节点的预设字段动态生成参数编辑控件（输入框/下拉框/开关）。
        /// 有可编辑字段时右下角显示节点参数面板，否则显示节点库面板。</summary>
        private void RefreshNodeFields(GraphNode node)
        {
            if (node_field_area == null)
                return;

            //清空旧控件（保留模板）
            for (int i = node_field_area.childCount - 1; i >= 0; i--)
            {
                GameObject child = node_field_area.GetChild(i).gameObject;
                if (child != node_field_input_template && child != node_field_dropdown_template && child != node_field_toggle_template)
                    Destroy(child);
            }

            NodePreset preset = (node != null) ? FindPreset(node.type, node.action) : null;
            bool has_fields = (preset != null && preset.fields.Count > 0);
            ShowFieldPanel(has_fields);
            if (!has_fields)
                return;

            //缺失字段补默认值（旧图/手改 JSON 可能缺字段），保证控件有值
            foreach (FieldDef fd in preset.fields)
            {
                if (!HasField(node, fd.name))
                    node.fields.Add(new FieldCustomData { name = fd.name, value = fd.def ?? "" });
            }
            foreach (FieldDef fd in preset.fields)
            {
                string current = GetFieldValue(node, fd.name, fd.def ?? "");
                switch (fd.edit)
                {
                    case FieldEditType.Input: CreateFieldInput(node, fd, current); break;
                    case FieldEditType.Dropdown: CreateFieldDropdown(node, fd, current); break;
                    case FieldEditType.Toggle: CreateFieldToggle(node, fd, current); break;
                }
            }
        }

        /// <summary>节点参数面板与节点库面板在右下角同一位置互斥切换</summary>
        private void ShowFieldPanel(bool show)
        {
            if (node_field_root != null)
                node_field_root.SetActive(show);
            if (node_lib_root != null)
                node_lib_root.SetActive(!show);
            if (node_field_hint != null)
                node_field_hint.gameObject.SetActive(false);   //占位提示由面板切换代替，不再显示
        }

        private void CreateFieldInput(GraphNode node, FieldDef fd, string current)
        {
            if (node_field_input_template == null)
                return;
            GameObject inst = Instantiate(node_field_input_template, node_field_area);
            inst.name = "Field_" + fd.name;
            inst.SetActive(true);

            Text label = inst.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.text = fd.display_name;

            InputField input = inst.GetComponentInChildren<InputField>(true);
            if (input == null)
                return;
            if (input.placeholder is Text ph && ph != null)
                ph.text = "请输入" + fd.display_name;
            input.text = current;
            input.onValueChanged.AddListener((val) =>
            {
                SetFieldValue(node, fd.name, val);
                RefreshNodeSummary(node);
            });
        }

        private void CreateFieldDropdown(GraphNode node, FieldDef fd, string current)
        {
            if (node_field_dropdown_template == null)
                return;
            GameObject inst = Instantiate(node_field_dropdown_template, node_field_area);
            inst.name = "Field_" + fd.name;
            inst.SetActive(true);

            Text label = inst.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.text = fd.display_name;

            Dropdown dd = inst.GetComponentInChildren<Dropdown>(true);
            if (dd == null)
                return;
            dd.ClearOptions();
            if (fd.options != null)
                dd.AddOptions(new List<string>(fd.options));
            int idx = fd.options != null ? Array.IndexOf(fd.options, current) : -1;
            dd.value = idx < 0 ? 0 : idx;
            dd.RefreshShownValue();
            dd.onValueChanged.AddListener((v) =>
            {
                string val = (fd.options != null && v >= 0 && v < fd.options.Length) ? fd.options[v] : "";
                SetFieldValue(node, fd.name, val);
                RefreshNodeSummary(node);
            });
        }

        private void CreateFieldToggle(GraphNode node, FieldDef fd, string current)
        {
            if (node_field_toggle_template == null)
                return;
            GameObject inst = Instantiate(node_field_toggle_template, node_field_area);
            inst.name = "Field_" + fd.name;
            inst.SetActive(true);

            Text label = inst.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.text = fd.display_name;

            Toggle tg = inst.GetComponentInChildren<Toggle>(true);
            if (tg == null)
                return;
            tg.isOn = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
            tg.onValueChanged.AddListener((val) =>
            {
                SetFieldValue(node, fd.name, val ? "true" : "false");
                RefreshNodeSummary(node);
            });
        }

        /// <summary>字段值变化后刷新画布上节点摘要文本（显示最新参数）</summary>
        private void RefreshNodeSummary(GraphNode node)
        {
            if (node == null || node_rows == null)
                return;
            if (node_rows.TryGetValue(node.id, out RectTransform rect))
            {
                Text desc = rect.Find("DescText")?.GetComponent<Text>();
                if (desc != null)
                    desc.text = NodeSummary(node);
            }
        }

        // ---------------- 字段读写 ----------------

        private static bool HasField(GraphNode node, string name)
        {
            if (node == null || node.fields == null)
                return false;
            foreach (FieldCustomData f in node.fields)
            {
                if (f.name == name)
                    return true;
            }
            return false;
        }

        private static string GetFieldValue(GraphNode node, string name, string def)
        {
            if (node == null || node.fields == null)
                return def;
            foreach (FieldCustomData f in node.fields)
            {
                if (f.name == name)
                    return f.value ?? def;
            }
            return def;
        }

        private static void SetFieldValue(GraphNode node, string name, string value)
        {
            if (node == null)
                return;
            if (node.fields == null)
                node.fields = new List<FieldCustomData>();
            foreach (FieldCustomData f in node.fields)
            {
                if (f.name == name)
                {
                    f.value = value;
                    return;
                }
            }
            node.fields.Add(new FieldCustomData { name = name, value = value });
        }

        private void OnDeleteNode()
        {
            if (graph == null || string.IsNullOrEmpty(selected_node))
            {
                SetStatus("请先在画布中选中一个节点");
                return;
            }
            graph.links.RemoveAll(l => l.from_node == selected_node || l.to_node == selected_node);
            graph.nodes.RemoveAll(n => n.id == selected_node);
            SetStatus("已删除节点及其连线（记得保存）");
            RebuildCanvas();
        }

        // ---------------- 连线交互 ----------------

        public void OnPinDragBegin(NodePin pin, PointerEventData eventData)
        {
            drag_from_pin = pin;
            ShowTempLink();
        }

        public void OnPinDrag(NodePin pin, PointerEventData eventData)
        {
            if (drag_from_pin == null || temp_link == null)
                return;
            Vector2 a = drag_from_pin.GetCanvasPos();
            if (ScreenToContent(eventData.position, out Vector2 b))
                DrawTempLink(a, b);
        }

        public void OnPinDragEnd(NodePin pin, PointerEventData eventData)
        {
            HideTempLink();
            if (drag_from_pin == null)
                return;

            NodePin start = drag_from_pin;
            drag_from_pin = null;

            if (!ScreenToContent(eventData.position, out Vector2 end_pos))
                return;

            //找距离最近的、类型相反、不同节点的引脚
            NodePin target = null;
            float best = 40f;   //命中半径（画布局部单位，与引脚命中区 44px 匹配）
            foreach (NodePin p in all_pins)
            {
                if (p == start || p.node_id == start.node_id)
                    continue;
                if (p.is_output == start.is_output)
                    continue;
                float dist = Vector2.Distance(p.GetCanvasPos(), end_pos);
                if (dist < best)
                {
                    best = dist;
                    target = p;
                }
            }

            if (target == null)
            {
                SetStatus("未连到任何引脚（拖动到另一个节点的引脚上松手）");
                return;
            }

            ConnectPins(start, target);
        }

        private void ConnectPins(NodePin from, NodePin to)
        {
            if (graph == null)
                return;

            //统一方向：输出 → 输入
            if (!from.is_output && to.is_output)
            {
                NodePin t = from;
                from = to;
                to = t;
            }
            if (from.is_output == to.is_output || from.node_id == to.node_id)
            {
                SetStatus("无法建立连接（需输出→输入且不同节点）");
                return;
            }

            //目标入线已占用则移除旧连线
            graph.links.RemoveAll(l => l.to_node == to.node_id && l.to_pin == to.pin_id);
            //起点出线到同一入线的重复连线也移除
            graph.links.RemoveAll(l => l.from_node == from.node_id && l.from_pin == from.pin_id
                && l.to_node == to.node_id && l.to_pin == to.pin_id);

            GraphLink link = new GraphLink
            {
                from_node = from.node_id,
                from_pin = from.pin_id,
                to_node = to.node_id,
                to_pin = to.pin_id,
            };
            graph.links.Add(link);
            CreateLinkUI(link);
            SetStatus("已连接: " + NodeShortName(from.node_id) + " → " + NodeShortName(to.node_id));
        }

        private void ShowTempLink()
        {
            if (link_template == null || temp_link != null)
                return;
            GameObject inst = Instantiate(link_template, canvas_content);
            inst.name = "TempLink";
            inst.SetActive(true);
            inst.transform.SetAsFirstSibling();
            NodeLink nl = inst.GetComponent<NodeLink>();
            if (nl == null)
                nl = inst.AddComponent<NodeLink>();
            nl.Setup(inst.GetComponent<RectTransform>());
            nl.SetEndpoints(drag_from_pin, drag_from_pin);
            temp_link = nl;
        }

        private void DrawTempLink(Vector2 a, Vector2 b)
        {
            if (temp_link == null || temp_link.line == null)
                return;
            Vector2 delta = b - a;
            float len = delta.magnitude;
            if (len < 1f)
                len = 1f;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            temp_link.line.anchoredPosition = a;
            temp_link.line.sizeDelta = new Vector2(len, 2.5f);
            temp_link.line.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void HideTempLink()
        {
            if (temp_link != null)
            {
                Destroy(temp_link.gameObject);
                temp_link = null;
            }
        }

        /// <summary>屏幕坐标 → 画布 content 局部坐标（按 Canvas 渲染模式取正确相机）</summary>
        private bool ScreenToContent(Vector2 screen, out Vector2 local)
        {
            local = Vector2.zero;
            if (canvas_content == null)
                return false;
            //主 Canvas 为 Screen Space - Camera 时必须传 worldCamera，传 null 会导致坐标偏移（线不跟鼠标/连不上）
            Canvas canvas = canvas_content.GetComponentInParent<Canvas>();
            Camera cam = canvas != null ? canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas_content, screen, cam, out local);
        }

        private string NodeShortName(string node_id)
        {
            if (graph != null)
            {
                GraphNode n = graph.GetNode(node_id);
                if (n != null)
                    return string.IsNullOrEmpty(n.title) ? n.action : n.title;
            }
            return node_id;
        }

        // ---------------- 视图控制 ----------------

        private void ResetView()
        {
            if (canvas_content != null)
            {
                canvas_content.anchoredPosition = Vector2.zero;
                canvas_content.localScale = Vector3.one;
            }
        }

        // ---------------- 保存/测试/关闭 ----------------

        private void OnSave()
        {
            if (card == null || pool == null)
            {
                SetStatus("没有可保存的数据");
                return;
            }

            ReadForm();
            graph.name = string.IsNullOrEmpty(card.title) ? "NewGraph" : card.title;
            card.graph = graph;

            //同步运行时自定义卡数据，使卡牌构筑/编辑器卡面立即反映最新属性
            CardPoolIO.UpdateCardData(card);

            string path = save_path;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(CardPoolIO.SaveFolder, pool.name + ".json");
                save_path = path;
            }

            try
            {
                Directory.CreateDirectory(CardPoolIO.SaveFolder);
                File.WriteAllText(path, JsonUtility.ToJson(pool, true));
                SetStatus("已保存规则图与属性: " + Path.GetFileName(path));
            }
            catch (Exception e)
            {
                Debug.LogError("保存失败: " + e.Message);
                SetStatus("保存失败: " + e.Message);
            }
        }

        /// <summary>用模拟宿主执行当前卡规则图，验证触发→动作闭环</summary>
        private void OnTest()
        {
            if (graph == null || graph.nodes.Count == 0)
            {
                SetStatus("当前卡还没有规则图，先在节点库添加节点");
                return;
            }
            SimulatedGraphHost host = new SimulatedGraphHost();
            GraphRuntime.ExecutionResult result = GraphRuntime.Execute(graph, host, "");
            if (result.success)
                SetStatus("测试完成: 执行 " + result.visited.Count + " 个节点 | HP=" + host.hp + " 手牌=" + host.hand);
            else
                SetStatus("测试失败: " + result.error);
        }

        /// <summary>关闭并返回卡牌编辑器</summary>
        private void OnClose()
        {
            Hide();
            CardEditorPanel editor = CardEditorPanel.Get();
            if (editor == null)
                editor = FindObjectOfType<CardEditorPanel>(true);
            if (editor != null)
            {
                editor.Show();
                editor.NotifyGraphClosed();
            }
        }

        // ---------------- 工具 ----------------

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
            string s = node.action;
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
                    s += (p.is_output ? "▸" : "◂") + p.display_name;
                }
                s += "]";
            }
            return s;
        }

        private static int IndexOf(string[] arr, string val)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == val)
                    return i;
            return -1;
        }

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

        private static int GetInputInt(InputField field, int def)
        {
            if (field == null)
                return def;
            if (int.TryParse(field.text, out int val))
                return val;
            return def;
        }

        private void SetStatus(string msg)
        {
            if (status_text != null)
                status_text.text = msg;
        }
    }
}
