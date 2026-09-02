using System;
using System.Collections;
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
        public Button btn_undo;              // 撤销
        public Button btn_redo;              // 重做
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
        public InputField node_search_input; // 节点库搜索框（按节点名过滤）
        public RectTransform node_recent_root;// 最近使用栏（横向按钮容器）
        public Dropdown node_filter_dropdown; // 节点库分类下拉（全部/内置/收藏 + NodeDoc zmcs 分类）

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
        /// <summary>当前卡的规则图（供 NodePin 等组件读取取值）</summary>
        public GraphData Graph { get { return graph; } }

        private readonly Dictionary<string, RectTransform> node_rows = new Dictionary<string, RectTransform>();
        private readonly List<NodePin> all_pins = new List<NodePin>();
        private readonly List<NodeLink> links = new List<NodeLink>();

        private NodePin drag_from_pin;       // 拖拽连线的起始引脚
        private NodeLink temp_link;          // 拖拽中的临时连线
        private int filter_index = 0;        // 节点库筛选：0全部 1触发 2条件 3动作 4数值
        private string search_keyword = "";  // 节点库搜索关键词
        private readonly HashSet<string> favs = new HashSet<string>();          // 收藏的节点 action（持久化）
        private readonly List<string> recent_actions = new List<string>();     // 最近使用的节点 action（持久化）
        private string selected_node;        // 选中的节点 id
        private int node_index = 0;          // 新节点位置偏移计数

        //Tier2 保护/防呆：撤销重做（结构操作快照）、节点复制粘贴、空画布引导
        private readonly List<string> undo_stack = new List<string>();   // 结构操作历史（GraphData JSON 快照）
        private readonly List<string> redo_stack = new List<string>();
        private const int MAX_UNDO = 50;
        private const string FAV_KEY = "graph_editor_favs";       // 收藏节点 action 持久化 key
        private const string RECENT_KEY = "graph_editor_recent";  // 最近使用节点 action 持久化 key
        private GraphNode copied_node;       // 复制缓冲（Ctrl+C/V）
        private GameObject empty_hint;       // 空画布引导提示

        //运行走线高亮：模拟测试后标出执行路径（走过的节点+连线）
        private readonly HashSet<NodeLink> highlighted_links = new HashSet<NodeLink>();
        private readonly HashSet<RectTransform> highlighted_nodes = new HashSet<RectTransform>();
        private Coroutine run_coroutine;
        private static readonly Color run_hl_color = new Color(1f, 0.85f, 0.3f, 1f);

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
            public bool required;            // 必填输入口（未接则标红+感叹号，保存前拦截，规格第6.6节）
            public PinDef(string name, string display_name, NodeValueType type, bool is_output, bool is_array = false)
            {
                this.name = name; this.display_name = display_name;
                this.type = type; this.is_output = is_output; this.is_array = is_array;
                this.required = false;
            }
        }

        private class NodePreset
        {
            public GraphNodeType type;
            public string action;
            public string title;
            public string desc;
            public string category;                                   // zmcs 主题分类（NodeDoc 节点）；内置节点为空
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
            new NodePreset { type = GraphNodeType.Event, action = "OnDraw", title = "抽到时", desc = "该卡被抽到手中时触发",
                pins = { new PinDef("out", "触发", NodeValueType.Flow, true) } },
            // 条件（Condition：左入 + 数据输入，右出真/假分支）
            new NodePreset { type = GraphNodeType.Condition, action = "IfHealth", title = "生命>值", desc = "目标生命大于设定值",
                fields = { IntField("value", "值", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("value", "值", NodeValueType.Int32, false),
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Condition, action = "IfMana", title = "法力>值", desc = "当前法力大于设定值",
                fields = { IntField("value", "值", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("value", "值", NodeValueType.Int32, false),
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Condition, action = "IfRandom", title = "概率判定", desc = "以概率决定走真/假分支",
                fields = { IntField("value", "概率%", "50") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("chance", "概率", NodeValueType.Int32, false),
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Condition, action = "IfTarget", title = "存在目标", desc = "场上存在有效目标",
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("true", "真", NodeValueType.Flow, true),
                    new PinDef("false", "假", NodeValueType.Flow, true),
                } },
            // 动作（Action：左入执行流 + 数据输入，右出执行流）——仅保留能真实编译进对战的内置直通动作；
            // 更丰富的能力（目标选取/集合/属性修改等）请使用 NodeDoc(zmcs) 节点（下方分类下拉中选取）
            new NodePreset { type = GraphNodeType.Action, action = "Damage", title = "造成伤害", desc = "对目标造成 N 点伤害",
                fields = { IntField("value", "伤害值", "2") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("target", "目标", NodeValueType.Card, false),
                    new PinDef("value", "伤害值", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Heal", title = "治疗", desc = "为己方英雄恢复 N 点生命",
                fields = { IntField("value", "治疗量", "2") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("value", "治疗量", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Draw", title = "抽牌", desc = "己方抽取 N 张牌",
                fields = { IntField("value", "数量", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("count", "数量", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "GainMana", title = "获得法力", desc = "获得 N 点法力水晶",
                fields = { IntField("value", "数量", "1") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("amount", "数量", NodeValueType.Int32, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Summon", title = "召唤随从", desc = "召唤一个随从",
                fields = { IntField("card_id", "随从ID", "") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
                    new PinDef("card", "随从", NodeValueType.CardDefine, false),
                    new PinDef("out", "出", NodeValueType.Flow, true),
                } },
            new NodePreset { type = GraphNodeType.Action, action = "Destroy", title = "消灭目标", desc = "消灭目标单位",
                fields = { IntField("value", "伤害值", "0") },
                pins = {
                    new PinDef("in", "入", NodeValueType.Flow, false) { required = true },
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

        // ---------------- NodeDoc(zmcs) 节点照搬：319 节点数据驱动并入节点库 ----------------

        /// <summary>节点库分类下拉选项（与 filter_index 一一对应）：全部/内置/收藏 + NodeDoc zmcs 分类</summary>
        private const string CAT_ALL = "全部";
        private const string CAT_BUILTIN = "内置";
        private const string CAT_FAV = "收藏";

        private static List<string> filter_options_cache;
        private static List<string> FilterOptions()
        {
            if (filter_options_cache == null)
            {
                filter_options_cache = new List<string> { CAT_ALL, CAT_BUILTIN, CAT_FAV };
                foreach (string c in NodeDocDb.Categories)
                    filter_options_cache.Add(c);
            }
            return filter_options_cache;
        }

        /// <summary>完整节点源：内置直通节点 + NodeDoc(zmcs) 全部节点（懒加载缓存）</summary>
        private static List<NodePreset> all_presets_cache;
        private static List<NodePreset> AllPresets()
        {
            if (all_presets_cache == null)
            {
                all_presets_cache = new List<NodePreset>(PRESETS);
                foreach (NodeDocDef d in NodeDocDb.All)
                    all_presets_cache.Add(NodePresetFromDoc(d));
            }
            return all_presets_cache;
        }

        /// <summary>NodeDoc 定义 → 面板预设：端口 1:1 照搬；Int32/Boolean/String 输入转为右侧可编辑常量字段</summary>
        private static NodePreset NodePresetFromDoc(NodeDocDef d)
        {
            NodePreset p = new NodePreset();
            p.type = d.outputs.Count == 0 ? GraphNodeType.Action : GraphNodeType.Value;  //启发：无输出→动作；有输出→取值/查询
            p.action = d.define_id;
            p.title = d.editor_name;
            p.desc = d.CleanSummary();
            p.category = d.category;
            foreach (NodeDocPort ip in d.inputs)
                p.pins.Add(new PinDef(ip.name, ip.display_name, ip.type, false, ip.is_array));
            foreach (NodeDocPort op in d.outputs)
                p.pins.Add(new PinDef(op.name, op.display_name, op.type, true, op.is_array));
            //纯动作（无输出数据口）：补一对执行流口（入/出），才能从入口事件接入真实执行链（v1 解释器沿此驱动）
            if (p.type == GraphNodeType.Action)
            {
                p.pins.Insert(0, new PinDef("in", "执行", NodeValueType.Flow, false));
                p.pins.Add(new PinDef("out", "执行", NodeValueType.Flow, true));
            }
            foreach (NodeDocPort ip in d.inputs)
            {
                if (ip.is_array || ip.is_params)
                    continue;
                if (ip.type == NodeValueType.Int32)
                    p.fields.Add(IntField(ip.name, ip.display_name, "0"));
                else if (ip.type == NodeValueType.Boolean)
                    p.fields.Add(BoolField(ip.name, ip.display_name, "false"));
                else if (ip.type == NodeValueType.String)
                    p.fields.Add(new FieldDef(ip.name, ip.display_name, FieldEditType.Input, null, ""));
            }
            return p;
        }

        /// <summary>当前下拉分类是否命中某预设（全部=命中；内置/收藏/zmcs 分类按 category/收藏表过滤）</summary>
        private bool InFilter(NodePreset p)
        {
            if (filter_index <= 0)
                return true;
            List<string> opts = FilterOptions();
            if (filter_index >= opts.Count)
            {
                filter_index = 0;
                return true;
            }
            string sel = opts[filter_index];
            if (sel == CAT_BUILTIN)
                return string.IsNullOrEmpty(p.category);
            if (sel == CAT_FAV)
                return favs.Contains(p.action);
            return p.category == sel;
        }

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
            if (btn_undo != null) btn_undo.onClick.AddListener(Undo);
            if (btn_redo != null) btn_redo.onClick.AddListener(Redo);
            if (btn_zoom_in != null) btn_zoom_in.onClick.AddListener(() => { if (graph_canvas != null) graph_canvas.ZoomIn(); });
            if (btn_zoom_out != null) btn_zoom_out.onClick.AddListener(() => { if (graph_canvas != null) graph_canvas.ZoomOut(); });
            if (btn_reset != null) btn_reset.onClick.AddListener(ResetView);
            //点击画布空白处取消节点选中
            if (graph_canvas != null)
                graph_canvas.onCanvasClick = DeselectNode;
            if (btn_pick_art != null) btn_pick_art.onClick.AddListener(OnPickArt);
            if (btn_pick_full_art != null) btn_pick_full_art.onClick.AddListener(OnPickFullArt);
            if (btn_audio_spawn != null) btn_audio_spawn.onClick.AddListener(() => OnPickAudio(0));
            if (btn_audio_attack != null) btn_audio_attack.onClick.AddListener(() => OnPickAudio(1));
            if (btn_audio_death != null) btn_audio_death.onClick.AddListener(() => OnPickAudio(2));
            if (btn_audio_damage != null) btn_audio_damage.onClick.AddListener(() => OnPickAudio(3));
            if (dropdown_type != null) dropdown_type.onValueChanged.AddListener((v) => RefreshPanelArtRow());
            SetupMetaDropdowns();

            //分类下拉（新 UI）：全部/内置/收藏/NodeDoc zmcs 分类；存在下拉时隐藏旧按钮（兼容旧场景）
            if (node_filter_dropdown != null)
            {
                node_filter_dropdown.ClearOptions();
                node_filter_dropdown.AddOptions(FilterOptions());
                node_filter_dropdown.value = 0;
                node_filter_dropdown.RefreshShownValue();
                node_filter_dropdown.onValueChanged.AddListener((v) => { filter_index = v; RefreshNodeLib(); });
                if (filter_buttons != null)
                {
                    for (int i = 0; i < filter_buttons.Length; i++)
                        if (filter_buttons[i] != null)
                            filter_buttons[i].gameObject.SetActive(false);
                }
            }
            else if (filter_buttons != null)
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

            //搜索框（按节点名实时过滤）
            if (node_search_input != null)
            {
                node_search_input.onValueChanged.AddListener((val) =>
                {
                    search_keyword = val ?? "";
                    RefreshNodeLib();
                });
            }

            //收藏 + 最近使用（规格第1节）
            LoadFavs();
            LoadRecent();
            RefreshRecentBar();
        }

        protected override void Update()
        {
            base.Update();
            HandleShortcuts();
        }

        // ---------------- Tier2：撤销/重做 / 复制粘贴 / 防呆 ----------------

        /// <summary>快捷键：Ctrl+Z 撤销 / Ctrl+Y 重做 / Ctrl+C 复制选中节点 / Ctrl+V 粘贴节点</summary>
        private void HandleShortcuts()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl)
                return;
            if (Input.GetKeyDown(KeyCode.Z))
                Undo();
            else if (Input.GetKeyDown(KeyCode.Y))
                Redo();
            else if (Input.GetKeyDown(KeyCode.C))
                CopySelectedNode();
            else if (Input.GetKeyDown(KeyCode.V))
                PasteNode();
        }

        /// <summary>结构操作前记录当前图快照（添加/删除/连线/移动/粘贴）</summary>
        private void PushUndo()
        {
            if (graph == null)
                return;
            undo_stack.Add(JsonUtility.ToJson(graph));
            if (undo_stack.Count > MAX_UNDO)
                undo_stack.RemoveAt(0);
            redo_stack.Clear();
        }

        private void Undo()
        {
            if (graph == null || undo_stack.Count == 0)
            {
                SetStatus("没有可撤销的操作");
                return;
            }
            redo_stack.Add(JsonUtility.ToJson(graph));
            string snap = undo_stack[undo_stack.Count - 1];
            undo_stack.RemoveAt(undo_stack.Count - 1);
            JsonUtility.FromJsonOverwrite(snap, graph);
            RebuildCanvas();
            SetStatus("已撤销 (Ctrl+Z)，记得保存");
        }

        private void Redo()
        {
            if (graph == null || redo_stack.Count == 0)
            {
                SetStatus("没有可重做的操作");
                return;
            }
            undo_stack.Add(JsonUtility.ToJson(graph));
            string snap = redo_stack[redo_stack.Count - 1];
            redo_stack.RemoveAt(redo_stack.Count - 1);
            JsonUtility.FromJsonOverwrite(snap, graph);
            RebuildCanvas();
            SetStatus("已重做 (Ctrl+Y)，记得保存");
        }

        /// <summary>复制选中节点（含字段/引脚，不含连线）到剪贴板</summary>
        private void CopySelectedNode()
        {
            if (graph == null || string.IsNullOrEmpty(selected_node))
            {
                SetStatus("请先选中一个节点再复制 (Ctrl+C)");
                return;
            }
            GraphNode src = graph.GetNode(selected_node);
            if (src == null)
                return;
            copied_node = src;
            SetStatus("已复制节点: " + src.title + "（Ctrl+V 粘贴）");
        }

        /// <summary>粘贴复制的节点：深拷贝 + 新 id + 偏移位置</summary>
        private void PasteNode()
        {
            if (graph == null || copied_node == null)
            {
                SetStatus("剪贴板为空（先 Ctrl+C 复制一个节点）");
                return;
            }
            GraphNode copy = JsonUtility.FromJson<GraphNode>(JsonUtility.ToJson(copied_node));
            copy.id = "n_" + GameTool.GenerateRandomID(6, 10);
            foreach (GraphPin p in copy.pins)
                p.id = copy.id + "_" + p.name;   //保持 id 命名规则，粘贴后连线可用
            copy.pos = new Vector2Data(copied_node.pos.x + 60f, copied_node.pos.y - 60f);
            PushUndo();
            graph.nodes.Add(copy);
            CreateNodeUI(copy);
            SelectNode(copy.id);
            RefreshEmptyHint();
            ApplyValidationMarks();   //粘贴的节点未接动作线时标红提示
            SetStatus("已粘贴节点: " + copy.title + "（记得保存）");
        }

        /// <summary>检测新连线(from→to)是否会沿动作线形成执行环（DFS 从 to 出发能否回到 from）</summary>
        private bool WouldCreateCycle(string from_node, string to_node)
        {
            if (graph == null)
                return false;
            Stack<string> stack = new Stack<string>();
            HashSet<string> visited = new HashSet<string>();
            stack.Push(to_node);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                if (cur == from_node)
                    return true;
                if (!visited.Add(cur))
                    continue;
                foreach (GraphLink link in graph.GetOutgoing(cur))
                {
                    GraphPin op = graph.GetPin(cur, link.from_pin);
                    if (op != null && op.type != NodeValueType.Flow && op.type != NodeValueType.None)
                        continue;   //只沿动作线（取值线不会成环）
                    stack.Push(link.to_node);
                }
            }
            return false;
        }

        /// <summary>空画布时在画布中央显示半透明引导提示（有节点则移除）</summary>
        private void RefreshEmptyHint()
        {
            bool empty = (graph == null || graph.nodes.Count == 0);
            if (empty && empty_hint == null && canvas_content != null)
            {
                GameObject go = new GameObject("EmptyHint", typeof(RectTransform));
                go.transform.SetParent(canvas_content, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(760, 190);

                //主提示文字（上半，半透明）
                GameObject tgo = new GameObject("Text", typeof(RectTransform));
                tgo.transform.SetParent(go.transform, false);
                RectTransform trt = tgo.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0f, 0.5f);
                trt.anchorMax = new Vector2(1f, 1f);
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                Text text = tgo.AddComponent<Text>();
                text.font = (status_text != null) ? status_text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 28;
                text.color = new Color(1f, 1f, 1f, 0.35f);
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
                text.text = "画布为空：从右侧节点库点选节点，或一键生成示例效果";

                //一键示例按钮（下半，规格第6.5节最小可用效果引导）
                GameObject bgo = new GameObject("SampleBtn", typeof(RectTransform));
                bgo.transform.SetParent(go.transform, false);
                RectTransform brt = bgo.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0.2f, 0.02f);
                brt.anchorMax = new Vector2(0.8f, 0.4f);
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;
                Image bimg = bgo.AddComponent<Image>();
                bimg.color = new Color(0.486f, 0.361f, 1f, 0.85f);   //紫（执行流品牌色）
                Button btn = bgo.AddComponent<Button>();
                btn.targetGraphic = bimg;
                Text btext = CreateStretchTextChild(bgo.transform, 20, Color.white);
                btext.text = "一键生成示例效果：打出时对目标造成 1 点伤害";
                btn.onClick.AddListener(BuildSampleEffect);

                empty_hint = go;
            }
            else if (!empty && empty_hint != null)
            {
                Destroy(empty_hint);
                empty_hint = null;
            }
        }

        /// <summary>一键生成最小可用效果：打出时 → 对目标造成 1 点伤害（规格第6.5节新手引导）</summary>
        private void BuildSampleEffect()
        {
            if (graph == null)
                return;
            NodePreset p_event = FindPreset(GraphNodeType.Event, "OnPlay");
            NodePreset p_damage = FindPreset(GraphNodeType.Action, "Damage");
            if (p_event == null || p_damage == null)
            {
                SetStatus("示例效果所需节点缺失");
                return;
            }

            PushUndo();   //整个示例一次撤销点
            GraphNode ev = CreateNodeFromPreset(p_event, new Vector2Data(240f, -120f));
            GraphNode dm = CreateNodeFromPreset(p_damage, new Vector2Data(520f, -120f));
            if (ev == null || dm == null)
                return;

            SetFieldValue(dm, "value", "1");   //伤害 = 1
            RefreshPinValues();                //节点内联值框显示「伤害值 = 1」
            RefreshNodeSummary(dm);            //节点说明同步刷新

            //动作线：打出时 → 造成伤害（OnPlay.out → Damage.in）
            GraphLink link = new GraphLink
            {
                from_node = ev.id,
                from_pin = ev.id + "_out",
                to_node = dm.id,
                to_pin = dm.id + "_in",
            };
            graph.links.Add(link);
            CreateLinkUI(link);

            SelectNode(dm.id);
            RefreshEmptyHint();
            ApplyValidationMarks();
            SetStatus("已生成示例效果：打出时对目标造成 1 点伤害（可修改右侧参数，还差『目标』可再接）");
        }

        /// <summary>从预设创建一个节点到指定位置并刷新 UI（供示例效果等批量搭建复用，不推撤销点）</summary>
        private GraphNode CreateNodeFromPreset(NodePreset preset, Vector2Data pos)
        {
            GraphNode node = new GraphNode();
            node.id = "n_" + GameTool.GenerateRandomID(6, 10);
            node.type = preset.type;
            node.action = preset.action;
            node.title = preset.title;
            node.category = preset.category;
            node.pos = pos;
            node_index++;

            BuildPins(node, preset);
            foreach (FieldDef fd in preset.fields)
            {
                if (!HasField(node, fd.name))
                    node.fields.Add(new FieldCustomData { name = fd.name, value = fd.def ?? "" });
            }
            graph.nodes.Add(node);
            CreateNodeUI(node);
            RecordRecent(preset.action);
            return node;
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
            //换图时清空撤销历史与复制缓冲，避免跨图误撤销
            undo_stack.Clear();
            redo_stack.Clear();
            copied_node = null;
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
            bool use_dropdown = node_filter_dropdown != null;
            List<NodePreset> source = use_dropdown ? AllPresets() : new List<NodePreset>(PRESETS);
            for (int i = 0; i < source.Count; i++)
            {
                NodePreset p = source[i];
                //筛选：分类 + 搜索关键词（规格第1节）
                if (use_dropdown)
                {
                    if (!InFilter(p))
                        continue;
                }
                else if (filter_index != 0 && (int)p.type != filter_index - 1)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(search_keyword)
                    && p.title.IndexOf(search_keyword, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                CreateNodeLibItem(p);
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

            //分类色条 + 图标字符（规格第6.4节：找节点靠颜色+图标扫）
            Image cat = inst.transform.Find("CatBar")?.GetComponent<Image>();
            if (cat != null)
                cat.color = CategoryColor(preset.type);
            Text icon = inst.transform.Find("IconText")?.GetComponent<Text>();
            if (icon != null)
            {
                icon.text = CategoryIcon(preset.type);
                icon.color = CategoryColor(preset.type);
            }

            //端口概要（▸输出 ◂输入）
            Text desc = inst.transform.Find("DescText")?.GetComponent<Text>();
            if (desc != null)
                desc.text = PortSummary(preset);

            //收藏星标（右上角，点击切换收藏状态并持久化）
            bool is_fav = favs.Contains(preset.action);
            Text star = inst.transform.Find("FavBtn/Text")?.GetComponent<Text>();
            if (star != null)
            {
                star.text = is_fav ? "★" : "☆";
                star.color = is_fav ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(1f, 0.85f, 0.4f, 0.35f);
            }
            Button fav = inst.transform.Find("FavBtn")?.GetComponent<Button>();
            if (fav != null)
            {
                string act = preset.action;
                fav.onClick.AddListener(() => ToggleFav(act));
            }

            Button btn = inst.GetComponent<Button>();
            if (btn == null)
                btn = inst.AddComponent<Button>();
            btn.onClick.AddListener(() => AddNodeFromPreset(preset));
        }

        /// <summary>切换收藏状态并持久化（规格第1节）</summary>
        private void ToggleFav(string action)
        {
            if (string.IsNullOrEmpty(action))
                return;
            if (favs.Contains(action))
                favs.Remove(action);
            else
                favs.Add(action);
            SaveFavs();
            RefreshNodeLib();
            SetStatus(favs.Contains(action) ? "已收藏: " + action : "取消收藏: " + action);
        }

        private void LoadFavs()
        {
            favs.Clear();
            string data = PlayerPrefs.GetString(FAV_KEY, "");
            if (string.IsNullOrEmpty(data))
                return;
            foreach (string s in data.Split(','))
            {
                if (!string.IsNullOrEmpty(s))
                    favs.Add(s);
            }
        }

        private void SaveFavs()
        {
            PlayerPrefs.SetString(FAV_KEY, string.Join(",", new List<string>(favs).ToArray()));
            PlayerPrefs.Save();
        }

        private void LoadRecent()
        {
            recent_actions.Clear();
            string data = PlayerPrefs.GetString(RECENT_KEY, "");
            if (string.IsNullOrEmpty(data))
                return;
            foreach (string s in data.Split(','))
            {
                if (!string.IsNullOrEmpty(s) && !recent_actions.Contains(s))
                    recent_actions.Add(s);
            }
        }

        private void SaveRecent()
        {
            PlayerPrefs.SetString(RECENT_KEY, string.Join(",", recent_actions.ToArray()));
            PlayerPrefs.Save();
        }

        /// <summary>记录节点最近使用（去重置顶，最多 8 条，持久化）</summary>
        private void RecordRecent(string action)
        {
            recent_actions.Remove(action);
            recent_actions.Insert(0, action);
            if (recent_actions.Count > 8)
                recent_actions.RemoveAt(recent_actions.Count - 1);
            SaveRecent();
            RefreshRecentBar();
        }

        /// <summary>刷新最近使用栏（横向小按钮，无最近时隐藏）</summary>
        private void RefreshRecentBar()
        {
            if (node_recent_root == null)
                return;
            for (int i = node_recent_root.childCount - 1; i >= 0; i--)
                Destroy(node_recent_root.GetChild(i).gameObject);

            if (recent_actions.Count == 0)
            {
                node_recent_root.gameObject.SetActive(false);
                return;
            }
            node_recent_root.gameObject.SetActive(true);

            float x = 0f;
            float max_w = node_recent_root.rect.width;
            foreach (string action in recent_actions)
            {
                NodePreset p = FindPresetByAction(action);
                if (p == null)
                    continue;
                float w = 34 + p.title.Length * 15f;
                if (x + w > max_w)
                    break;   //横向放不下则截断，避免溢出节点库区域
                Button btn = CreateRecentChip(p);
                RectTransform rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0.5f);
                rt.anchorMax = new Vector2(0, 0.5f);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0);
                rt.sizeDelta = new Vector2(w, 26);
                x += w + 6;
                btn.onClick.AddListener(() => AddNodeFromPreset(p));
            }
        }

        /// <summary>运行时创建最近使用小按钮（规格第1节：横向小按钮）</summary>
        private Button CreateRecentChip(NodePreset p)
        {
            GameObject go = new GameObject("Recent_" + p.action, typeof(RectTransform));
            go.transform.SetParent(node_recent_root, false);
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.45f, 0.6f, 0.45f);
            Text txt = go.AddComponent<Text>();
            txt.font = status_text != null ? status_text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 13;
            txt.color = new Color(0.9f, 1f, 1f, 1f);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = p.title;
            txt.raycastTarget = false;
            return go.AddComponent<Button>();
        }

        private static NodePreset FindPresetByAction(string action)
        {
            if (string.IsNullOrEmpty(action))
                return null;
            foreach (NodePreset p in AllPresets())
            {
                if (p.action == action)
                    return p;
            }
            return null;
        }

        // ---------------- 缺输入校验（规格第6.6节：必填口未接标红+感叹号，保存前拦截/试跑时提示） ----------------

        /// <summary>单条校验问题</summary>
        private class GraphIssue
        {
            public string node_id;
            public string msg;
            public GraphIssue(string node_id, string msg) { this.node_id = node_id; this.msg = msg; }
        }

        private readonly Dictionary<string, GameObject> issue_badges = new Dictionary<string, GameObject>();   // 节点 id → 红感叹号角标
        private readonly Dictionary<string, GameObject> collapse_badges = new Dictionary<string, GameObject>(); // 节点 id → 收起角标（「×N」圆徽）
        private RectTransform hover_tooltip_root;   // 收起节点悬停细目提示根（挂在画布容器内）
        private Text hover_tooltip_text;            // 悬停细目提示文本
        private RectTransform hover_tooltip_rect;   // 悬停细目提示 RectTransform

        /// <summary>校验整张图：必填输入口未接动作线、事件节点没有连出动作线（图什么都不做）</summary>
        private List<GraphIssue> ValidateGraph()
        {
            List<GraphIssue> issues = new List<GraphIssue>();
            if (graph == null)
                return issues;
            //含 zmcs(NodeDoc) 节点的图：数据线驱动、不沿入口动作线执行，跳过"事件未连出动作线"检查（执行层后续接入）
            bool has_node_doc = false;
            foreach (GraphNode gn in graph.nodes)
            {
                if (gn != null && !string.IsNullOrEmpty(gn.category))
                {
                    has_node_doc = true;
                    break;
                }
            }
            foreach (GraphNode node in graph.nodes)
            {
                if (node == null)
                    continue;
                NodePreset preset = FindPreset(node.type, node.action);
                if (preset != null)
                {
                    foreach (PinDef pd in preset.pins)
                    {
                        if (!pd.required)
                            continue;
                        GraphPin pin = graph.GetPinByName(node.id, pd.name);
                        if (pin == null)
                            continue;
                        if (graph.GetIncomingLink(node.id, pin.id) == null)
                            issues.Add(new GraphIssue(node.id, "缺少「" + pd.display_name + "」输入（执行流）"));
                    }
                }
                //事件节点没有连出任何动作线 → 该事件触发了也不做任何事（纯 NodeDoc 图除外，见 has_node_doc）
                if (node.type == GraphNodeType.Event && !has_node_doc)
                {
                    bool has_flow_out = false;
                    foreach (GraphLink l in graph.GetOutgoing(node.id))
                    {
                        GraphPin op = graph.GetPin(node.id, l.from_pin);
                        if (op != null && (op.type == NodeValueType.Flow || op.type == NodeValueType.None))
                        {
                            has_flow_out = true;
                            break;
                        }
                    }
                    if (!has_flow_out)
                        issues.Add(new GraphIssue(node.id, "事件未连出动作线"));
                }
            }
            return issues;
        }

        /// <summary>刷新节点红感叹号角标：有问题的节点右上角显示「!」，正常节点移除</summary>
        private void ApplyValidationMarks()
        {
            if (graph == null)
            {
                ClearValidationMarks();
                return;
            }
            List<GraphIssue> issues = ValidateGraph();

            //汇总每个节点的问题
            Dictionary<string, string> node_msgs = new Dictionary<string, string>();
            foreach (GraphIssue g in issues)
            {
                if (node_msgs.ContainsKey(g.node_id))
                    node_msgs[g.node_id] += "、";
                node_msgs[g.node_id] = node_msgs.ContainsKey(g.node_id) ? node_msgs[g.node_id] + g.msg : g.msg;
            }

            //移除多余角标
            List<string> to_remove = new List<string>();
            foreach (var kv in issue_badges)
            {
                if (!node_msgs.ContainsKey(kv.Key) || kv.Value == null)
                {
                    if (kv.Value != null)
                        Destroy(kv.Value);
                    to_remove.Add(kv.Key);
                }
            }
            foreach (string k in to_remove)
                issue_badges.Remove(k);

            //为有问题节点补角标
            foreach (var kv in node_msgs)
            {
                if (issue_badges.ContainsKey(kv.Key) && issue_badges[kv.Key] != null)
                    continue;
                if (!node_rows.TryGetValue(kv.Key, out RectTransform rect) || rect == null)
                    continue;
                issue_badges[kv.Key] = CreateIssueBadge(rect);
            }

            //状态栏汇总（数量提示）
            if (issues.Count > 0)
            {
                int node_count = node_msgs.Count;
                SetStatus("规则图有 " + issues.Count + " 处缺输入（" + node_count + " 个节点），保存前请先补全");
            }

            //缺输入节点外圈变淡红（规格第6.6节）
            foreach (var kv in node_rows)
                ApplySelectHighlight(kv.Key);
        }

        /// <summary>清除全部校验角标（切图/重建前调用）</summary>
        private void ClearValidationMarks()
        {
            foreach (var kv in issue_badges)
            {
                if (kv.Value != null)
                    Destroy(kv.Value);
            }
            issue_badges.Clear();
        }

        /// <summary>在节点右上角创建红色「!」角标（红底白字圆徽）</summary>
        private GameObject CreateIssueBadge(RectTransform node_rect)
        {
            GameObject go = new GameObject("IssueBadge", typeof(RectTransform));
            go.transform.SetParent(node_rect, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-76, -8);   //放在缩小按钮左侧，避免与右上角按钮重叠
            rt.sizeDelta = new Vector2(20, 20);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.9f, 0.2f, 0.2f, 1f);
            bg.raycastTarget = false;

            Text txt = CreateStretchTextChild(go.transform, 15, Color.white);   //Text 放独立子对象（Graphic 唯一限制）
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = "!";
            return go;
        }

        /// <summary>在指定父级下创建铺满父级的 Text 子对象（Unity 限制一个 GameObject 只能有一个 Graphic，
        /// 因此背景 Image 与文字 Text 必须分属父子两个对象）</summary>
        private Text CreateStretchTextChild(Transform parent, int font_size, Color color)
        {
            GameObject tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(parent, false);
            RectTransform trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            Text txt = tgo.AddComponent<Text>();
            txt.font = status_text != null ? status_text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = font_size;
            txt.color = color;
            txt.raycastTarget = false;
            return txt;
        }

        /// <summary>取选中节点的缺输入提示（供选中时状态栏显示「还差…」）</summary>
        private string MissingInputHint(string node_id)
        {
            List<GraphIssue> issues = ValidateGraph();
            string hint = "";
            foreach (GraphIssue g in issues)
            {
                if (g.node_id == node_id)
                    hint = string.IsNullOrEmpty(hint) ? g.msg : hint + "；" + g.msg;
            }
            return hint;
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
            node.category = preset.category;

            //引脚（先建，位置计算需用端口数估算节点高度）
            BuildPins(node, preset);

            //默认位置：画布当前可视区域正中央（节点中心居中），便于玩家立刻看到并拖动
            Vector2 c = CanvasVisibleCenter();
            node.pos = new Vector2Data(c.x - EstimateNodeWidth(node) * 0.5f, c.y - EstimateNodeHeight(node) * 0.5f);
            node_index++;

            //默认字段（按字段定义初始化，缺失才补默认值）
            foreach (FieldDef fd in preset.fields)
            {
                if (!HasField(node, fd.name))
                    node.fields.Add(new FieldCustomData { name = fd.name, value = fd.def ?? "" });
            }

            PushUndo();   //结构操作：记录撤销点
            graph.nodes.Add(node);
            CreateNodeUI(node);
            SelectNode(node.id);   //自动选中新节点，右侧立即显示可编辑参数
            RefreshEmptyHint();    //有节点后移除空画布引导
            RecordRecent(preset.action);   //记录最近使用（规格第1节）
            ApplyValidationMarks();        //新节点尚未接动作线，标红提示缺输入
            SetStatus("已添加节点: " + node.title + "（可编辑右侧参数）");
        }

        /// <summary>画布当前可视区域的中心点（画布 content 局部坐标，考虑平移与缩放）</summary>
        private Vector2 CanvasVisibleCenter()
        {
            if (graph_canvas == null || canvas_content == null)
                return new Vector2(260f, -140f);
            RectTransform viewport = graph_canvas.GetComponent<RectTransform>();
            if (viewport == null)
                return new Vector2(260f, -140f);
            float scale = canvas_content.localScale.x > 0.001f ? canvas_content.localScale.x : 1f;
            return (viewport.rect.size * 0.5f - canvas_content.anchoredPosition) / scale;
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
            foreach (NodePreset p in AllPresets())
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

            ClearRunHighlight();   //重建前清理执行走线高亮，避免残留被销毁的引用
            ClearValidationMarks(); //重建前清理缺输入角标（节点将整体重建）

            //清除旧节点/连线/临时线（保留模板与空画布引导）
            for (int i = canvas_content.childCount - 1; i >= 0; i--)
            {
                GameObject child = canvas_content.GetChild(i).gameObject;
                if (child != node_template && child != link_template && child != pin_template && child != empty_hint)
                    Destroy(child);
            }
            node_rows.Clear();
            all_pins.Clear();
            links.Clear();
            temp_link = null;
            selected_node = null;
            collapse_badges.Clear();           //收起角标随节点重建
            hover_tooltip_root = null;         //悬停提示随画布重建，引用置空（EnsureHoverTooltip 会重建）
            hover_tooltip_text = null;
            hover_tooltip_rect = null;

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
            RefreshEmptyHint();
            ApplyValidationMarks();   //重建后刷新缺输入角标
            RefreshCollapseBadges();  //重建后刷新收起节点「×N」角标
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
            //节点宽/高自适应：宽度按最宽一行文字、高度按 Header+端口行数+说明区（规格第3节），
            //输出口 x 与端口行 y 依赖该尺寸，须先于 CreatePinUI 设置
            rect.sizeDelta = new Vector2(EstimateNodeWidth(node), EstimateNodeHeight(node));

            //Header：分类色条 + 类型标签 + 标题
            Transform header = inst.transform.Find("Header");
            if (header != null)
            {
                Image cat = header.Find("CatBar")?.GetComponent<Image>();
                if (cat != null)
                    cat.color = CategoryColor(node.type);
                Text type = header.Find("TypeText")?.GetComponent<Text>();
                if (type != null)
                {
                    //zmcs(NodeDoc) 节点头部显示其主题分类（如"卡牌"），内置节点沿用原四类名
                    string label = string.IsNullOrEmpty(node.category) ? NodeTypeLabel(node.type) : node.category;
                    type.text = CategoryIcon(node.type) + " " + label;   //规格第6.4节：颜色+图标辅助扫视
                    type.color = CategoryColor(node.type);
                }
            }
            Text title = inst.transform.Find("Header/TitleText")?.GetComponent<Text>();
            if (title != null)
                title.text = node.title;
            Text desc = inst.transform.Find("DescText")?.GetComponent<Text>();
            if (desc != null)
            {
                desc.text = NodeSummary(node);
                desc.gameObject.SetActive(!string.IsNullOrEmpty(desc.text));   //无说明则高度 0
            }

            //收起/删除按钮（每个节点自带，规格第4节）
            string nid = node.id;
            Button btn_del = inst.transform.Find("Header/BtnDel")?.GetComponent<Button>();
            if (btn_del != null)
                btn_del.onClick.AddListener(() => OnDeleteNodeId(nid));
            Button btn_min = inst.transform.Find("Header/BtnMin")?.GetComponent<Button>();
            if (btn_min != null)
                btn_min.onClick.AddListener(() => ToggleCollapse(nid));

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

            //点击选中 + 悬停细目（收起节点悬停显示「入N条 · 出M条」）
            NodeClick click = inst.GetComponent<NodeClick>();
            if (click == null)
                click = inst.AddComponent<NodeClick>();
            click.Setup(node.id, SelectNode, ShowNodeHover, HideNodeHover);

            node_rows[node.id] = rect;

            //应用已保存的收起状态（打开旧图时恢复折叠布局，线保持连接）
            if (node.collapsed)
            {
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, 40);   //保留自适应宽度，收起只显示头部
                if (pins_root != null)
                    pins_root.gameObject.SetActive(false);
                Transform d2 = inst.transform.Find("DescText");
                if (d2 != null)
                    d2.gameObject.SetActive(false);
                foreach (NodePin p in all_pins)
                {
                    if (p.node_id == node.id)
                    {
                        p.gameObject.SetActive(false);   //收起：端口圆点隐藏
                        //迷你锚点并到 Header 中心（收起后高 40：Header 占 8~40，中心 y=24）
                        p.SetLocalOffset(p.is_output ? new Vector2(rect.sizeDelta.x - 3f, 24f) : new Vector2(3f, 24f));
                    }
                }
            }

            ApplySelectHighlight(node.id);
        }

        /// <summary>估算节点高度：Header(33含分割线) + max(输入,输出)端口行×28 + 说明区 + 底部留白（规格第3节）</summary>
        private static float EstimateNodeHeight(GraphNode node)
        {
            int in_c = 0, out_c = 0;
            if (node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    if (p.is_output) out_c++; else in_c++;
                }
            }
            int rows = Mathf.Max(in_c, out_c);
            bool has_desc = !string.IsNullOrEmpty(NodeSummary(node));
            return 33f + rows * 28f + (has_desc ? 26f : 0f) + 6f;
        }

        /// <summary>估算节点宽度：取标题/端口标签/内联值框中最宽一行（规格第3节 min190/max320，中文全角按字号、ASCII半角按0.55字号）</summary>
        private static float EstimateNodeWidth(GraphNode node)
        {
            float w = EstimateTextWidth(node.title, 20) + 24;
            if (node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    string name = string.IsNullOrEmpty(p.display_name) ? p.name : p.display_name;
                    if (!p.is_output)
                        name += " = 10";   //内联值框追加估算
                    w = Mathf.Max(w, EstimateTextWidth(name, 12) + 44);
                }
            }
            return Mathf.Clamp(w, 190f, 320f);
        }

        /// <summary>估算一行文字宽度（px）：中文/全角按字号，ASCII/半角按 0.55 字号</summary>
        private static float EstimateTextWidth(string text, int fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            float w = 0f;
            foreach (char c in text)
                w += c > 127 ? fontSize : fontSize * 0.55f;
            return w;
        }

        private void CreatePinUI(GraphNode node, GraphPin pin, Transform pins_root, RectTransform node_rect)
        {
            if (pin_template == null)
                return;

            //端口直接挂到节点根（锚定节点左下角），坐标即「相对节点」偏移，
            //与 NodePin.GetCanvasPos（node_rect.anchoredPosition + offset）完全一致，连线端点精确
            GameObject inst = Instantiate(pin_template, node_rect);
            inst.name = "Pin_" + (pin.is_output ? "O" : "I") + "_" + pin.name;
            inst.SetActive(true);

            //两线制端口行布局：输入贴左缘、输出贴右缘，输入/输出共用同一行中线（行高28px）
            //行号 = 端口在同类（输入/输出）列表中的序号，保证第 r 个输入与第 r 个输出水平对齐
            //规格第3节：行中心 y = headerBottom(33) + row*28 + 14（相对节点底部）
            int row = 0;
            int in_i = 0, out_i = 0;
            for (int i = 0; i < node.pins.Count; i++)
            {
                GraphPin p = node.pins[i];
                if (p == pin)
                {
                    row = p.is_output ? out_i : in_i;
                    break;
                }
                if (p.is_output) out_i++; else in_i++;
            }
            float y = node_rect.sizeDelta.y - 47f - row * 28f;
            float x = pin.is_output ? Mathf.Max(0f, node_rect.sizeDelta.x - 6f) : 6f;
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

            //引脚颜色：方案配色（执行紫/整数蓝/卡牌红/布尔玩家灰/文本绿）
            Transform dot = inst.transform.Find("Dot");
            Image pimg = dot != null ? dot.GetComponent<Image>() : inst.GetComponent<Image>();
            if (pimg != null)
                pimg.color = PinColor(pin);

            //端口标签（规格第3节：端口 = 彩点 + 标签）
            //- 输出口：名称标签贴点左侧、右对齐（标签对齐→点）
            //- 执行流输入口：名称标签贴点右侧、左对齐（点→标签）
            //- 数据输入口：值框显示「端口名 = 固定值 / ← 来源」（规格第5节）
            if (!pin.is_output)
            {
                if (pin.type == NodeValueType.Flow)
                    CreatePinNameLabel(inst.transform, pin, false);
                else
                {
                    node_pin.value_label = CreatePinValueLabel(inst.transform);
                    node_pin.RefreshValueLabel();
                }
            }
            else
            {
                CreatePinNameLabel(inst.transform, pin, true);
            }
        }

        /// <summary>创建端口名称标签（挂在引脚实例上：输出口贴点左侧右对齐，输入口贴点右侧左对齐）</summary>
        private void CreatePinNameLabel(Transform pin_inst, GraphPin pin, bool is_output)
        {
            GameObject go = new GameObject("PinName", typeof(RectTransform));
            go.transform.SetParent(pin_inst, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            //约束在节点框内：左侧输入口标签贴在圆点右侧（向框内）、右侧输出口标签贴在圆点左侧（向框内），
            //中心偏移 72px（= 点中心 6px + 半宽 65px + 1px 间隙），文字与圆点完全错开、左右不超节点框
            rt.anchoredPosition = new Vector2(is_output ? -72f : 72f, 0f);
            rt.sizeDelta = new Vector2(130f, 18f);

            Text txt = go.AddComponent<Text>();
            txt.font = status_text != null ? status_text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 12;
            txt.alignment = is_output ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            txt.color = PinColor(pin);
            txt.text = string.IsNullOrEmpty(pin.display_name) ? pin.name : pin.display_name;
            txt.raycastTarget = false;
        }

        /// <summary>创建输入口的内联数值框（挂在引脚实例上，位于圆点右侧）</summary>
        private Text CreatePinValueLabel(Transform pin_inst)
        {
            GameObject go = new GameObject("ValueLabel", typeof(RectTransform));
            go.transform.SetParent(pin_inst, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(62f, 0f);   //圆点右侧向框内延伸（点中心 6px + 半宽 60px → 框内），避免文字出框
            rt.sizeDelta = new Vector2(120f, 18f);

            Text txt = go.AddComponent<Text>();
            txt.font = status_text != null ? status_text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 12;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = new Color(0.357f, 0.616f, 1f, 1f);
            txt.raycastTarget = true;
            return txt;
        }

        /// <summary>刷新所有输入口的内联数值框（连线/字段编辑后调用）</summary>
        private void RefreshPinValues()
        {
            foreach (NodePin np in all_pins)
            {
                if (np != null)
                    np.RefreshValueLabel();
            }
        }

        /// <summary>引脚颜色（方案：执行=紫 #7c5cff / 整数=蓝 #5b9dff / 卡牌=红 #e5484d / 布尔/玩家=灰 #8a8fa3 / 文本=绿 #35c28a）</summary>
        public static Color PinColor(GraphPin pin)
        {
            if (pin.type == NodeValueType.Flow || pin.type == NodeValueType.None || pin.type == NodeValueType.ActionNode)
                return new Color(0.486f, 0.361f, 1f, 1f);    //紫 #7c5cff 执行
            switch (pin.type)
            {
                case NodeValueType.Int32: return new Color(0.357f, 0.616f, 1f, 1f);      //蓝 #5b9dff
                case NodeValueType.Boolean:
                case NodeValueType.Player:
                case NodeValueType.Object: return new Color(0.541f, 0.561f, 0.639f, 1f); //灰 #8a8fa3
                case NodeValueType.Card: return new Color(0.898f, 0.282f, 0.302f, 1f);   //红 #e5484d
                case NodeValueType.String: return new Color(0.208f, 0.761f, 0.541f, 1f); //绿 #35c28a
                case NodeValueType.CardDefine: return new Color(1f, 0.62f, 0.35f, 1f);   //橙（卡牌定义）
                case NodeValueType.Pile:
                case NodeValueType.EventArg: return new Color(1f, 0.82f, 0.4f, 1f);      //黄
                case NodeValueType.Buff:
                case NodeValueType.BuffDefine: return new Color(1f, 0.56f, 0.64f, 1f);   //粉
                default: return new Color(0.8f, 0.8f, 0.8f, 1f);                          //灰
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
            //两线制：按起点引脚类型设置线样式（Flow=动作线实线，数据端口=取值线配色）
            GraphPin fp = graph != null ? graph.GetPin(link.from_node, link.from_pin) : null;
            nl.SetStyle(fp != null ? fp.type : NodeValueType.Flow);
            nl.Redraw();
            nl.onDelete = DeleteLink;   //右键点击连线 → 取消连接
            links.Add(nl);
        }

        /// <summary>删除某入线端口的全部连线（数据 + UI 实例；替换连线/取消连接共用）</summary>
        private void RemoveLinksOnPin(string node_id, string pin_id, bool refresh = true)
        {
            if (graph == null)
                return;
            List<NodeLink> old_uis = links.FindAll(l => l.to_node == node_id && l.to_pin == pin_id);
            foreach (NodeLink old in old_uis)
            {
                links.Remove(old);
                if (old != null)
                    Destroy(old.gameObject);
            }
            graph.links.RemoveAll(l => l.to_node == node_id && l.to_pin == pin_id);
            if (refresh)
            {
                RefreshPinValues();
                ApplyValidationMarks();
                RefreshCollapseBadges();
            }
        }

        /// <summary>删除一条连线（右键点击连线触发）：撤销结构操作，目标输入口回落默认值</summary>
        private void DeleteLink(NodeLink nl)
        {
            if (nl == null || graph == null)
                return;
            PushUndo();
            graph.links.RemoveAll(l => l.from_node == nl.from_node && l.from_pin == nl.from_pin
                && l.to_node == nl.to_node && l.to_pin == nl.to_pin);
            links.Remove(nl);
            if (nl != null)
                Destroy(nl.gameObject);
            RefreshPinValues();
            ApplyValidationMarks();
            RefreshCollapseBadges();
            SetStatus("已断开连线（输入口回落默认值）");
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

        /// <summary>两线制接线合法性：动作线（Flow/None）只能连动作线；取值线必须同类型数据端口</summary>
        private bool CanConnect(NodePin from, NodePin to)
        {
            GraphPin fp = FindGraphPin(from);
            GraphPin tp = FindGraphPin(to);
            if (fp == null || tp == null)
                return true;   //旧图无类型，放宽允许（兼容旧连线）
            bool from_flow = (fp.type == NodeValueType.Flow || fp.type == NodeValueType.None);
            bool to_flow = (tp.type == NodeValueType.Flow || tp.type == NodeValueType.None);
            if (from_flow || to_flow)
                return from_flow && to_flow;
            return fp.type == tp.type;
        }

        /// <summary>引脚 → 图数据中的 GraphPin（查类型用）</summary>
        private GraphPin FindGraphPin(NodePin np)
        {
            if (graph == null || np == null)
                return null;
            return graph.GetPin(np.node_id, np.pin_id);
        }

        /// <summary>取引脚视觉圆点 Image（高亮/恢复颜色用）</summary>
        private static Image GetPinDot(NodePin p)
        {
            if (p == null)
                return null;
            Transform dot = p.transform.Find("Dot");
            return dot != null ? dot.GetComponent<Image>() : p.GetComponent<Image>();
        }

        /// <summary>拖线时高亮可连引脚、压暗不可连引脚；松开后恢复原始颜色（两线制防呆）</summary>
        private void HighlightMatchingPins(NodePin from, bool active)
        {
            foreach (NodePin p in all_pins)
            {
                Image dot = GetPinDot(p);
                if (dot == null)
                    continue;
                if (!active || from == null)
                {
                    GraphPin gp = FindGraphPin(p);
                    dot.color = gp != null ? PinColor(gp) : new Color(1f, 1f, 1f, 1f);
                    continue;
                }
                bool match = (p != from && p.is_output != from.is_output && p.node_id != from.node_id && CanConnect(from, p));
                GraphPin mgp = FindGraphPin(p);
                dot.color = match
                    ? (mgp != null ? PinColor(mgp) : new Color(1f, 1f, 1f, 1f))
                    : new Color(1f, 1f, 1f, 0.15f);
            }
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
            PushUndo();   //记录移动前位置，撤销可还原
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
            string hint = MissingInputHint(node_id);
            SetStatus(string.IsNullOrEmpty(hint)
                ? "已选中节点，可编辑右侧参数（记得保存）"
                : "已选中节点，还差：" + hint + "（记得保存）");
        }

        /// <summary>取消所有节点选中（点击画布空白处触发）：恢复高亮并回到节点库面板</summary>
        private void DeselectNode()
        {
            if (string.IsNullOrEmpty(selected_node))
                return;
            selected_node = null;
            foreach (var kv in node_rows)
                ApplySelectHighlight(kv.Key);
            RefreshNodeFields(null);   //无选中节点 → 右下角切回节点库
            SetStatus("已取消选中节点");
        }

        private void ApplySelectHighlight(string node_id)
        {
            if (node_rows.TryGetValue(node_id, out RectTransform rect))
            {
                Image bg = rect.Find("LineBG")?.GetComponent<Image>();
                if (bg != null)
                {
                    //三级状态：选中蓝 > 缺输入淡红（规格第6.6节）> 默认白
                    bool issue = !string.IsNullOrEmpty(MissingInputHint(node_id));
                    if (node_id == selected_node)
                        bg.color = new Color(0.35f, 0.65f, 0.95f, 0.45f);
                    else if (issue)
                        bg.color = new Color(0.8f, 0.2f, 0.2f, 0.18f);
                    else
                        bg.color = new Color(1f, 1f, 1f, 0.12f);
                }
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
                RefreshPinValues();
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
                RefreshPinValues();
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
                RefreshPinValues();
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
                {
                    desc.text = NodeSummary(node);
                    desc.gameObject.SetActive(!string.IsNullOrEmpty(desc.text));
                }
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
            OnDeleteNodeId(selected_node);
        }

        /// <summary>删除指定节点及其所有连线（工具栏按钮与节点自带 ✕ 按钮共用；Ctrl+Z 可撤销）</summary>
        private void OnDeleteNodeId(string node_id)
        {
            if (graph == null || string.IsNullOrEmpty(node_id))
                return;
            PushUndo();   //删除前记录，Ctrl+Z 可恢复
            graph.links.RemoveAll(l => l.from_node == node_id || l.to_node == node_id);
            graph.nodes.RemoveAll(n => n.id == node_id);
            SetStatus("已删除节点及其连线（Ctrl+Z 可撤销，记得保存）");
            RebuildCanvas();
        }

        // ---------------- 节点收起/展开（规格第4节） ----------------

        private void ToggleCollapse(string node_id)
        {
            GraphNode node = graph != null ? graph.GetNode(node_id) : null;
            if (node == null)
                return;
            SetCollapsed(node_id, !node.collapsed);
        }

        /// <summary>收起节点：只显示头部，端口/描述隐藏，连线不断（端口并到 Header 中心迷你锚点）；展开时恢复</summary>
        private void SetCollapsed(string node_id, bool collapsed)
        {
            GraphNode node = graph != null ? graph.GetNode(node_id) : null;
            if (node == null)
                return;
            node.collapsed = collapsed;
            PushUndo();   //记录收起状态，Ctrl+Z 可还原

            if (!node_rows.TryGetValue(node_id, out RectTransform rect) || rect == null)
                return;

            rect.sizeDelta = new Vector2(rect.sizeDelta.x, collapsed ? 40f : EstimateNodeHeight(node));   //收起只留头部，展开恢复自适应高度
            Transform header = rect.Find("Header");
            Transform pins = rect.Find("Pins");
            Transform desc = rect.Find("DescText");
            if (pins != null)
                pins.gameObject.SetActive(!collapsed);
            if (desc != null)
                desc.gameObject.SetActive(!collapsed);
            if (header != null)
            {
                Transform btn_min = header.Find("BtnMin");
                if (btn_min != null)
                {
                    Text t = btn_min.GetComponentInChildren<Text>();
                    if (t != null)
                        t.text = collapsed ? "▾" : "–";
                }
            }

            //端口并到左右迷你锚点（Header 中心，收起后高 40：Header 占 8~40 中心 y=24），
            //输入线汇入左侧、输出线从右侧散出（规格第4节）；展开时恢复原始偏移并显示端口
            float node_w = rect.sizeDelta.x;
            foreach (NodePin p in all_pins)
            {
                if (p.node_id != node_id)
                    continue;
                p.gameObject.SetActive(!collapsed);   //收起隐藏端口圆点（连线端点仍按 offset 定位）
                p.SetLocalOffset(collapsed ? (p.is_output ? new Vector2(node_w - 3f, 24f) : new Vector2(3f, 24f)) : p.original_offset);
            }
            //重绘经过该节点的线（收到迷你锚点 / 回到端口行）
            foreach (NodeLink nl in links)
            {
                if (nl != null && (nl.from_node == node_id || nl.to_node == node_id))
                    nl.Redraw();
            }

            //收起后取消选中，回到节点库显示
            if (selected_node == node_id)
            {
                selected_node = "";
                RefreshNodeFields(null);
                foreach (var kv in node_rows)
                    ApplySelectHighlight(kv.Key);
            }
            RefreshCollapseBadges();   //收起=显示「×N」角标，展开=移除
            SetStatus(collapsed ? "节点已收起（线保持连接）" : "节点已展开");
        }

        /// <summary>刷新全部收起节点的「×N」角标：收起=创建/更新入线计数，展开=移除（规格第4节）</summary>
        private void RefreshCollapseBadges()
        {
            //移除已展开/失效的角标
            List<string> to_remove = new List<string>();
            foreach (var kv in collapse_badges)
            {
                GraphNode n = graph != null ? graph.GetNode(kv.Key) : null;
                if (n == null || !n.collapsed || kv.Value == null)
                {
                    if (kv.Value != null)
                        Destroy(kv.Value);
                    to_remove.Add(kv.Key);
                }
            }
            foreach (string k in to_remove)
                collapse_badges.Remove(k);
            if (graph == null)
                return;

            //为收起节点补角标并更新计数（连进来的线数，防止被误判为孤立）
            foreach (var kv in node_rows)
            {
                GraphNode n = graph.GetNode(kv.Key);
                if (n == null || !n.collapsed)
                    continue;
                int in_count = graph.GetIncoming(kv.Key).Count;
                if (!collapse_badges.TryGetValue(kv.Key, out GameObject badge) || badge == null)
                {
                    badge = CreateCollapseBadge(kv.Value);
                    collapse_badges[kv.Key] = badge;
                }
                Text t = badge.GetComponentInChildren<Text>();
                if (t != null)
                    t.text = "×" + in_count;
            }
        }

        /// <summary>在节点上方创建收起角标（橙黄圆徽「×N」，表示连进来的线数）</summary>
        private GameObject CreateCollapseBadge(RectTransform node_rect)
        {
            GameObject go = new GameObject("CollapseBadge", typeof(RectTransform));
            go.transform.SetParent(node_rect, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 22f);
            rt.sizeDelta = new Vector2(24, 24);

            Image bg = go.AddComponent<Image>();
            bg.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");   //圆形精灵：橙黄圆徽（非方块）
            bg.color = new Color(0.95f, 0.6f, 0.1f, 1f);   //橙黄圆徽，醒目但不喧宾夺主
            bg.raycastTarget = false;

            Text txt = CreateStretchTextChild(go.transform, 14, Color.white);   //Text 放独立子对象（Graphic 唯一限制）
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = "×0";
            return go;
        }

        // ---------------- 收起节点悬停细目（规格第4节） ----------------

        /// <summary>节点悬停进入：收起节点显示「入N条 · 出M条」细目；缺输入节点显示「还差…」（规格第4/6节）</summary>
        private void ShowNodeHover(string node_id)
        {
            GraphNode node = graph != null ? graph.GetNode(node_id) : null;
            if (node == null)
            {
                HideNodeHover(node_id);
                return;
            }
            if (!node_rows.TryGetValue(node_id, out RectTransform rect) || rect == null || canvas_content == null)
                return;

            //内容：收起细目 + 缺输入提示（可合并显示）
            string detail = node.collapsed ? BuildCollapseDetail(node_id) : "";
            string miss = MissingInputHint(node_id);
            string content = "";
            if (!string.IsNullOrEmpty(detail) && !string.IsNullOrEmpty(miss))
                content = detail + " ｜ " + miss;
            else
                content = string.IsNullOrEmpty(detail) ? miss : detail;
            if (string.IsNullOrEmpty(content))
            {
                HideNodeHover(node_id);
                return;
            }

            Text tip = EnsureHoverTooltip();
            if (tip == null)
                return;
            tip.text = content;
            //宽度随文本自适应（clamp 防太宽/太窄）
            hover_tooltip_rect.sizeDelta = new Vector2(Mathf.Clamp(70 + tip.text.Length * 12, 150, 520), 28);
            //定位到节点正上方（画布局部坐标，随画布平移缩放）
            hover_tooltip_rect.anchoredPosition = rect.anchoredPosition + new Vector2(0f, rect.sizeDelta.y * 0.5f + 18f);
            hover_tooltip_rect.SetAsLastSibling();   //置顶，避免被其他节点/连线遮挡
            hover_tooltip_root.gameObject.SetActive(true);
        }

        /// <summary>节点悬停退出：隐藏细目提示</summary>
        private void HideNodeHover(string node_id)
        {
            if (hover_tooltip_root != null)
                hover_tooltip_root.gameObject.SetActive(false);
        }

        /// <summary>创建/复用悬停细目提示（挂在画布容器内，随画布平移缩放）</summary>
        private Text EnsureHoverTooltip()
        {
            if (hover_tooltip_text != null)
                return hover_tooltip_text;
            if (canvas_content == null)
                return null;

            GameObject go = new GameObject("HoverTooltip", typeof(RectTransform));
            go.transform.SetParent(canvas_content, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200f, 28f);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.1f, 0.14f, 0.95f);
            bg.raycastTarget = false;

            //Text 需放独立子对象（Unity 限制一个 GameObject 只能有一个 Graphic）
            Text txt = CreateStretchTextChild(go.transform, 13, Color.white);
            txt.alignment = TextAnchor.MiddleCenter;

            hover_tooltip_root = rt;
            hover_tooltip_text = txt;
            hover_tooltip_rect = rt;
            return txt;
        }

        /// <summary>生成收起节点悬停细目文本：入N条 · 出M条：来自【…】、连向【…】</summary>
        private string BuildCollapseDetail(string node_id)
        {
            List<GraphLink> in_links = graph != null ? graph.GetIncoming(node_id) : new List<GraphLink>();
            List<GraphLink> out_links = graph != null ? graph.GetOutgoing(node_id) : new List<GraphLink>();
            string from = "";
            for (int i = 0; i < in_links.Count; i++)
            {
                if (i > 0) from += "、";
                from += NodeShortName(in_links[i].from_node);
            }
            string to = "";
            for (int i = 0; i < out_links.Count; i++)
            {
                if (i > 0) to += "、";
                to += NodeShortName(out_links[i].to_node);
            }
            if (in_links.Count == 0 && out_links.Count == 0)
                return "入 0 条 · 出 0 条：孤立节点（未连接任何节点）";
            string s = "入 " + in_links.Count + " 条 · 出 " + out_links.Count + " 条";
            if (from.Length > 0)
                s += "：来自【" + from + "】";
            if (to.Length > 0)
                s += (from.Length > 0 ? "、" : "：") + "连向【" + to + "】";
            return s;
        }

        /// <summary>节点分类配色：触发=青 / 条件=紫 / 动作=橙 / 数值=绿（Header 色条与类型标签）</summary>
        private static Color CategoryColor(GraphNodeType type)
        {
            switch (type)
            {
                case GraphNodeType.Event: return new Color(0.3f, 0.9f, 1f, 1f);      //青
                case GraphNodeType.Condition: return new Color(0.85f, 0.6f, 1f, 1f);  //紫
                case GraphNodeType.Action: return new Color(1f, 0.7f, 0.4f, 1f);      //橙
                case GraphNodeType.Value: return new Color(0.5f, 1f, 0.55f, 1f);      //绿
                default: return Color.gray;
            }
        }

        /// <summary>节点分类图标字符：触发=! / 条件=? / 动作=> / 数值=#（规格第6.4节，节点库列表项用）
        /// 全部用 ASCII 字符，避免部分字体不支持 emoji/特殊符号渲染成方块</summary>
        private static string CategoryIcon(GraphNodeType type)
        {
            switch (type)
            {
                case GraphNodeType.Event: return "!";
                case GraphNodeType.Condition: return "?";
                case GraphNodeType.Action: return ">";
                case GraphNodeType.Value: return "#";
                default: return "•";
            }
        }

        // ---------------- 连线交互 ----------------

        public void OnPinDragBegin(NodePin pin, PointerEventData eventData)
        {
            drag_from_pin = pin;
            ShowTempLink();
            HighlightMatchingPins(pin, true);   //两线制：匹配引脚高亮，不可连引脚压暗
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
            HighlightMatchingPins(null, false);   //恢复所有引脚颜色
            if (drag_from_pin == null)
                return;

            NodePin start = drag_from_pin;
            drag_from_pin = null;

            if (!ScreenToContent(eventData.position, out Vector2 end_pos))
                return;

            //找距离最近的、类型相反、不同节点的引脚（两线制：仅类型匹配者可作为目标）
            NodePin target = null;
            float best = 40f;   //命中半径（画布局部单位，与引脚命中区 44px 匹配）
            foreach (NodePin p in all_pins)
            {
                if (p == start || p.node_id == start.node_id)
                    continue;
                if (p.is_output == start.is_output)
                    continue;
                if (!CanConnect(start, p))
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
                SetStatus("未连到可匹配的引脚（动作线连执行流口，取值线连同类型数据口）");
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
            //两线制类型校验：动作线（Flow）只能连动作线；取值线必须同类型数据端口
            if (!CanConnect(from, to))
            {
                SetStatus("无法建立连接（两线制：执行流口连执行流口，数据口须同类型）");
                return;
            }
            //环形检测：禁止沿动作线连成环，避免执行死锁
            if (WouldCreateCycle(from.node_id, to.node_id))
            {
                SetStatus("禁止连接：该连线会沿动作线形成执行环");
                return;
            }
            //动作线入口唯一性：执行流（Flow）输入口已连一条动作线时禁止再连，避免执行顺序混乱
            GraphPin tpin = FindGraphPin(to);
            if (tpin != null && (tpin.type == NodeValueType.Flow || tpin.type == NodeValueType.None)
                && graph.links.Exists(l => l.to_node == to.node_id && l.to_pin == to.pin_id))
            {
                SetStatus("该执行流入口已连接一条动作线，请先断开旧线再连");
                return;
            }
            PushUndo();   //结构操作：记录撤销点

            //目标入线已占用则移除旧连线（数据 + UI 实例，避免替换后旧线残留画布造成"一入口多线"假象）
            RemoveLinksOnPin(to.node_id, to.pin_id, false);
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
            RefreshPinValues();   //目标输入口值框变为 ← 来源
            ApplyValidationMarks(); //连线后刷新缺输入角标（接上动作线即可消除）
            RefreshCollapseBadges(); //连线后刷新收起节点「×N」角标（入线数可能变化）
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

            //规格第6.6节：保存前拦截缺输入（必填口未接）
            List<GraphIssue> issues = ValidateGraph();
            if (issues.Count > 0)
            {
                ApplyValidationMarks();
                SetStatus("无法保存：规则图有 " + issues.Count + " 处缺输入，请先补全（红「!」节点）");
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

        /// <summary>用模拟宿主执行当前卡规则图，验证触发→动作闭环，并高亮执行路径</summary>
        private void OnTest()
        {
            if (graph == null || graph.nodes.Count == 0)
            {
                SetStatus("当前卡还没有规则图，先在节点库添加节点");
                return;
            }
            //编辑器层阶段：NodeDoc(zmcs) 节点图的真实执行（图解释器）将在后续版本接入
            foreach (GraphNode gn in graph.nodes)
            {
                if (gn != null && !string.IsNullOrEmpty(gn.category))
                {
                    SetStatus("已包含 zmcs(NodeDoc) 节点：当前支持编辑/保存/预览，真实对局执行将在后续版本接入（内置动作节点仍可模拟/编译）");
                    return;
                }
            }
            //规格第6.6节：试跑时醒目提示缺输入（不拦截，跑完仍可看高亮）
            List<GraphIssue> issues = ValidateGraph();
            if (issues.Count > 0)
                ApplyValidationMarks();
            SimulatedGraphHost host = new SimulatedGraphHost();
            GraphRuntime.ExecutionResult result = GraphRuntime.Execute(graph, host, "");
            if (result.success)
            {
                string warn = issues.Count > 0 ? " | 注意：有 " + issues.Count + " 处缺输入（红「!」节点可能不执行）" : "";
                SetStatus("测试完成: 执行 " + result.visited.Count + " 个节点 | HP=" + host.hp + " 手牌=" + host.hand + warn);
                ShowRunHighlight(result);
            }
            else
            {
                SetStatus("测试失败: " + result.error);
            }
        }

        // ---------------- 运行走线高亮 ----------------

        /// <summary>高亮本次执行走过的节点与连线（黄色），约 2.5 秒后自动恢复</summary>
        private void ShowRunHighlight(GraphRuntime.ExecutionResult result)
        {
            ClearRunHighlight();
            if (result == null)
                return;

            foreach (string nid in result.visited)
            {
                if (!node_rows.TryGetValue(nid, out RectTransform rect) || rect == null)
                    continue;
                Transform t = rect.Find("Header/TitleText");
                if (t != null)
                {
                    Text txt = t.GetComponent<Text>();
                    if (txt != null)
                        txt.color = run_hl_color;
                }
                highlighted_nodes.Add(rect);
            }

            foreach (string key in result.visited_links)
            {
                NodeLink nl = FindLink(key);
                if (nl != null)
                {
                    nl.SetHighlighted(true);
                    highlighted_links.Add(nl);
                }
            }

            run_coroutine = StartCoroutine(ClearRunHighlightDelay(2.5f));
        }

        private IEnumerator ClearRunHighlightDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearRunHighlight();
        }

        /// <summary>恢复所有高亮的节点与连线（供下次测试/重建前清理）</summary>
        private void ClearRunHighlight()
        {
            if (run_coroutine != null)
            {
                StopCoroutine(run_coroutine);
                run_coroutine = null;
            }
            foreach (NodeLink nl in highlighted_links)
            {
                if (nl != null)
                    nl.SetHighlighted(false);
            }
            highlighted_links.Clear();
            foreach (RectTransform rect in highlighted_nodes)
            {
                if (rect == null)
                    continue;
                Transform t = rect.Find("Header/TitleText");
                if (t != null)
                {
                    Text txt = t.GetComponent<Text>();
                    if (txt != null)
                        txt.color = Color.white;
                }
            }
            highlighted_nodes.Clear();
        }

        /// <summary>按 "from|from_pin|to|to_pin" 标识查找连线</summary>
        private NodeLink FindLink(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            string[] parts = key.Split('|');
            if (parts.Length != 4)
                return null;
            foreach (NodeLink nl in links)
            {
                if (nl == null)
                    continue;
                if (nl.from_node == parts[0] && nl.from_pin == parts[1]
                    && nl.to_node == parts[2] && nl.to_pin == parts[3])
                    return nl;
            }
            return null;
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
            string s = string.IsNullOrEmpty(node.title) ? node.action : node.title;
            foreach (FieldCustomData f in node.fields)
                s += "  " + f.name + "=" + f.value;
            //端口概要（▸输出 ◂输入）；zmcs(NodeDoc) 节点端口多，不再拼到摘要防节点过大
            if (string.IsNullOrEmpty(node.category) && node.pins.Count > 0)
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
