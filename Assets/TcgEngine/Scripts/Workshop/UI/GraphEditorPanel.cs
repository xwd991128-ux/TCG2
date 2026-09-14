using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using TcgEngine;
using TcgEngine.Client;
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
        public TMPro.TMP_Text title_text;    // 页面标题（TMP，支持 <sprite> 富文本标签；场景里用 TMP 文本组件绑定）
        public TMP_Text status_text;         // 底部状态提示（TMP，全页统一字体）

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
        public TMP_InputField input_name;    // 卡牌名称（TMP）
        public Dropdown dropdown_type;       // 类型
        public Dropdown dropdown_team;       // 阵营
        public Dropdown dropdown_rarity;     // 稀有度
        public Dropdown dropdown_trait;      // 种族（特质）
        public Dropdown dropdown_keyword;    // 关键词（KeywordData，可选控件：场景未绑定则忽略，v1 单选）

        // ---- 卡牌属性区扩展控件（运行时创建，兼容生成工具未重建的情况）----
        private TMP_Text txt_trait_select;      // 种族多选按钮文本（显示已选摘要）
        private TMP_Text txt_keyword_select;    // 关键词多选按钮文本
        private TMP_Text txt_type_select;       // 类型选择按钮文本（TMP，替代旧下拉）
        private TMP_Text txt_team_select;       // 阵营选择按钮文本
        private TMP_Text txt_rarity_select;     // 稀有度选择按钮文本
        private TMP_Text txt_filter_select;     // 节点库分类选择按钮文本（替代 TMP 下拉）
        private int filter_select_value = 0;    // 节点库分类当前下标（与 filter_index 同步）

        // ---- 富文本编辑弹层（卡牌文本/描述；弹框为场景组件，由生成工具绑定）----
        public RichTextPopupUI rich_text_popup;       // 富文本编辑弹框（场景组件，生成工具绑定）

        // ---- 卡图裁切（点击预览图 → 裁切弹框；弹框运行时自建，无需场景绑定）----
        // 注意两行的字段对应关系（按项目约定）：
        //   「卡牌图片」行 = 卡牌正面（手牌/收藏/回合历史） → art_full_path / art_full，显示区 250×350
        //   「面板图片」行 = 战场面板                       → art_path / art_board，显示区 8.56×8.36
        private ImageClipEditorUI art_clip_card;      // 「卡牌图片」行入口（art_full_path）
        private ImageClipEditorUI art_clip_panel;     // 「面板图片」行入口（art_path）
        private bool card_extra_built = false;  // 扩展行是否已构建（每次打开面板构建一次）
        private const float MAX_AUDIO_SECONDS = 10f;   //导入音效时长上限（秒）

        // ---- 全页字体统一 + 右侧栏 Tab（布局由生成工具 GraphEditorBuilder 直接生成，运行时只做显隐切换）----
        private bool tmp_ui_done = false;                        // 旧版 Text → TMP 是否已跑过
        private bool ui_setup_pending = false;                   // 等面板激活后执行 TMP 迁移
        private readonly HashSet<GameObject> tmp_converted = new HashSet<GameObject>();
        private RectTransform prop_area_rt;                      // 卡牌参数区（占满右列）
        private RectTransform lib_area_rt;                       // 节点库区（占满右列）
        public TMP_Text tab_prop_text;                           // 「卡牌参数」Tab 文本（生成工具绑定）
        public TMP_Text tab_lib_text;                            // 「节点库」Tab 文本（生成工具绑定）
        public Button btn_tab_prop;                              // 「卡牌参数」Tab 按钮（生成工具绑定）
        public Button btn_tab_lib;                               // 「节点库」Tab 按钮（生成工具绑定）
        private bool right_tab_prop = false;                     // 当前是否在「卡牌参数」页
        public TMP_InputField input_mana;    // 费用（TMP）
        public TMP_InputField input_attack;  // 攻击（TMP）
        public TMP_InputField input_hp;      // 生命（TMP）
        public TMP_InputField input_text;    // 卡牌文本（TMP）
        public TMP_InputField input_desc;    // 描述（TMP）
        public Toggle toggle_deckbuilding;   // 可组卡
        public TMP_InputField input_cost;    // 购买价（TMP）
        public Image art_preview;            //「卡牌图片」预览（卡牌正面：手牌/收藏）→ art_full_path
        public Button btn_pick_art;          //「卡牌图片」选图按钮
        public RectTransform art_full_row;   //「面板（全图）图片」行（战场面板）→ art_path；法术/奥秘隐藏
        public Image art_full_preview;       //「面板图片」预览
        public Button btn_pick_full_art;     //「面板图片」选图按钮
        public TMP_InputField input_audio_spawn; // 音效：打出（TMP）
        public TMP_InputField input_audio_attack;// 音效：攻击（TMP）
        public TMP_InputField input_audio_death; // 音效：死亡（TMP）
        public TMP_InputField input_audio_damage;// 音效：受伤（TMP）
        public Button btn_audio_spawn;       // 选择音频：打出
        public Button btn_audio_attack;      // 选择音频：攻击
        public Button btn_audio_death;       // 选择音频：死亡
        public Button btn_audio_damage;      // 选择音频：受伤

        [Header("右侧节点库")]
        public Button[] filter_buttons;      // 筛选按钮：全部/触发/条件/动作/数值
        public ScrollRect node_lib_scroll;   // 节点库滚动区
        public RectTransform node_lib_content;// 节点库容器
        public GameObject node_lib_template; // 节点库项模板（隐藏）
        public TMP_Text node_lib_count;      // 数量提示（TMP）
        public TMP_InputField node_search_input; // 节点库搜索框（按节点名过滤，TMP）
        public RectTransform node_recent_root;// 最近使用栏（横向按钮容器）
        public TMPro.TMP_Dropdown node_filter_dropdown; // 节点库分类下拉（全部/内置/收藏 + NodeDoc zmcs 分类；场景里用 TMP Dropdown 绑定）

        [Header("右侧节点参数编辑区")]
        public RectTransform node_field_area;            // 节点参数编辑区容器（选中节点后填充）
        public GameObject node_field_input_template;     // 参数行模板：输入框
        public GameObject node_field_dropdown_template;  // 参数行模板：下拉框
        public GameObject node_field_toggle_template;    // 参数行模板：开关
        public GameObject node_lib_root;                 // 节点库面板根（与参数面板同位置，互斥切换）
        public GameObject node_field_root;               // 节点参数面板根（与节点库同位置，互斥切换）
        public TMP_Text node_field_hint;                 // 参数编辑区占位提示（TMP；面板切换已代替，保留引用兼容旧场景）

        // ---------------- 运行时数据 ----------------
        private static GraphEditorPanel instance;
        private CardPoolData pool;           // 所属卡池（引用，保存时写盘）
        private CardCustomData card;         // 当前编辑的卡
        private string save_path;            // 卡池文件路径
        private GraphData graph;             // 当前卡的规则图（= card.graph）
        private KeywordData editing_keyword;   // 关键词模式：当前编辑的关键词（card/pool 为空）
        private KeywordRule editing_rule;      // 关键词模式：当前编辑的规则条目（graph 引用其 graph）
        private BuffData editing_buff;         // 增益模式：当前编辑的增益定义（card/pool/关键词均为空，graph 引用其 graph）
        private BattleButtonConfig editing_button_config;   // 按钮模式：当前编辑的全局按钮配置（card/pool/关键词/增益均为空，graph 引用其共享图）
        /// <summary>当前卡的规则图（供 NodePin 等组件读取取值）</summary>
        public GraphData Graph { get { return graph; } }

        private int effect_index = 0;          // 当前效果图 tab 下标（卡模式：一张卡多张效果图）
        private RectTransform effect_tab_bar;  // 效果图 tab 容器（运行时创建）

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
        /// <summary>节点整体视觉缩放（画布节点按此缩放，1=原始尺寸；0.5 → 面积约 1/4）</summary>
        public const float NodeScale = 0.5f;
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
        private readonly List<string> keyword_ids = new List<string>();

        // ---------------- 节点库预设 ----------------

        /// <summary>字段编辑方式</summary>
        private enum FieldEditType { Input, Dropdown, Toggle, CardSelect, BuffSelect, ButtonSelect, MultiOptions, KeywordSelect, BgmSelect }

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
            public bool supported = true;                         // zmcs 节点是否已接入执行（未接入的灰显、不可拖入）
            public bool hidden = false;                           // 是否从节点库展示中隐藏（过时/内部/同名重复；预设仍保留以兼容旧图）
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

        // ---------------- 运算节点的编号输入槽（112004 整数运算 / 112005 逻辑运算） ----------------
        // 每个输入槽 = 一个编号端口（arg1… / value1…）+ 同名"手填值"字段：
        //   端口有连线 → 该行的手填框自动隐藏（值由连线提供）；
        //   无连线     → 显示手填框（整数=数字输入，布尔=勾选框）。
        // 槽号只增不复用；删除后自动重排编号（连线随之改写，保持有效）。

        /// <summary>运算节点的输入槽基名：112004→"arg"，112005→"value"；其它节点返回 null</summary>
        private static string ParamSlotBaseName(GraphNode node)
        {
            if (node == null)
                return null;
            if (node.action == "112004")
                return "arg";
            if (node.action == "112005")
                return "value";
            return null;
        }

        /// <summary>是否是"编号输入槽"运算节点</summary>
        private static bool IsParamSlotNode(GraphNode node)
        {
            return ParamSlotBaseName(node) != null;
        }

        /// <summary>槽字段/端口名 → 槽号：base=1、base+N=N、其它返回 0</summary>
        private static int SlotNumberOf(string name, string base_name)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(base_name) || !name.StartsWith(base_name))
                return 0;
            string num = name.Substring(base_name.Length);
            if (string.IsNullOrEmpty(num))
                return 1;
            return int.TryParse(num, out int n) && n >= 1 ? n : 0;
        }

        /// <summary>节点现存输入槽号（字段与端口都算，升序）</summary>
        private static List<int> ParamSlotsOf(GraphNode node, string base_name)
        {
            List<int> list = new List<int>();
            if (node == null || string.IsNullOrEmpty(base_name))
                return list;
            if (node.fields != null)
            {
                foreach (FieldCustomData f in node.fields)
                {
                    int s = SlotNumberOf(f != null ? f.name : null, base_name);
                    if (s > 0 && !list.Contains(s))
                        list.Add(s);
                }
            }
            if (node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    int s = SlotNumberOf(p != null ? p.name : null, base_name);
                    if (s > 0 && !list.Contains(s))
                        list.Add(s);
                }
            }
            list.Sort();
            return list;
        }

        /// <summary>给运算节点预设挂 count 个编号输入槽（端口 + 同名字段）；槽 1 用无编号名（与旧图一致）</summary>
        private static void BuildParamSlots(NodePreset p, string base_name, int count, bool is_int)
        {
            for (int i = 1; i <= count; i++)
            {
                string name = i == 1 ? base_name : base_name + i;
                p.pins.Add(new PinDef(name, "值", is_int ? NodeValueType.Int32 : NodeValueType.Boolean, false));
                p.fields.Add(is_int ? IntField(name, "值", "0") : BoolField(name, "值", "false"));
            }
        }

        /// <summary>StatusType 枚举名选项（光环入口"增益定义"下拉，剔除 None/HeroNewTurn 等内部项）</summary>
        private static string[] status_type_options;
        private static string[] StatusTypeOptions()
        {
            if (status_type_options == null)
            {
                List<string> names = new List<string>();
                foreach (string n in Enum.GetNames(typeof(TcgEngine.StatusType)))
                {
                    if (n == "None" || n == "HeroNewTurn")
                        continue;
                    names.Add(n);
                }
                status_type_options = names.ToArray();
            }
            return status_type_options;
        }

        // ---------------- NodeDoc(zmcs) 节点照搬：319 节点数据驱动并入节点库 ----------------

        /// <summary>节点库分类下拉选项（与 filter_index 一一对应）：全部/收藏/触发器/增益触发/按钮 + NodeDoc zmcs 分类（内置节点已移除）</summary>
        private const string CAT_ALL = "全部";
        private const string CAT_FAV = "收藏";
        private const string CAT_BUFF_TRIGGER = "增益触发";
        private const string CAT_BUTTON = "按钮";
        private const string CAT_EVENT = "事件";
        private const string CAT_ENTRY = "入口";

        private static List<string> filter_options_cache;
        private static List<string> FilterOptions()
        {
            if (filter_options_cache == null)
            {
                filter_options_cache = new List<string> { CAT_ALL, CAT_FAV, CAT_BUFF_TRIGGER, CAT_BUTTON, CAT_EVENT, CAT_ENTRY };
                foreach (string c in NodeDocDb.Categories)
                    filter_options_cache.Add(c);
            }
            return filter_options_cache;
        }

        /// <summary>完整节点源：NodeDoc(zmcs) 全量节点 + 入口触发器预设（未接入执行的 NodeDoc 标记 supported=false，库内灰显）</summary>
        private static List<NodePreset> all_presets_cache;
        private static List<NodePreset> AllPresets()
        {
            if (all_presets_cache == null)
            {
                all_presets_cache = new List<NodePreset>();
                //内置直通节点（原 PRESETS 数组）已整体删除：节点库只保留 NodeDoc(zmcs) 节点；
                //旧图残留的内置节点仍由 GraphRuntime 按 action 执行（无需预设数据）
                //隐藏规则：过时(obsoleteMsg)/内部负数 defineId/同名重复（同名只保留支持的那个，避免一屏同名"未接入"噪音）
                Dictionary<string, string> keep_name = new Dictionary<string, string>();
                foreach (NodeDocDef d in NodeDocDb.All)
                {
                    if (d == null || string.IsNullOrEmpty(d.define_id) || string.IsNullOrEmpty(d.editor_name))
                        continue;
                    bool sup = SupportedNodeIds.Contains(d.define_id);
                    string cur = keep_name.TryGetValue(d.editor_name, out string c) ? c : null;
                    if (cur == null || (sup && (cur == null || !SupportedNodeIds.Contains(cur))))
                        keep_name[d.editor_name] = d.define_id;
                }
                foreach (NodeDocDef d in NodeDocDb.All)
                {
                    NodePreset p = NodePresetFromDoc(d);
                    p.supported = SupportedNodeIds.Contains(d.define_id);
                    p.hidden = ShouldHideNodeDoc(d, keep_name);
                    all_presets_cache.Add(p);
                }
                //增益触发（Event 类型）：增益效果图的入口（BuffData.graph），暴露增益上下文变量（自身/施加者/增益定义/双方玩家/剩余回合）
                all_presets_cache.AddRange(BuildBuffTriggerPresets());
                //按钮节点（Event/动作）：战斗界面自定义按钮（一图多按钮），点击按钮时/点击按钮后/触发按钮效果
                all_presets_cache.AddRange(BuildButtonPresets());
                //图事件节点（Event/动作）：全场监听「X 时/后」事件入口 + 阻止本事件/修改事件值
                all_presets_cache.AddRange(BuildGraphEventPresets());
                //zmcs 风格入口节点（分类「入口」）：主动效果(战吼/法术)/光环/被动(亡语)/事件效果
                all_presets_cache.AddRange(BuildZmcsEntryPresets());
                //项目内灵力节点（分类「玩家」，与 NodeDoc 的 101014/101015 同组同风格）：
                //zmcs 只有两档灵力（当前/上限），TCG2 三套灵力体系里的"最大灵力值"没有对应 defineId → 这里补项目内节点
                all_presets_cache.AddRange(BuildManaPresets());
            }
            return all_presets_cache;
        }

        /// <summary>是否从节点库展示中隐藏：过时标注 / 内部负数 defineId / 同名重复（同名只保留 keep 里那个）</summary>
        private static bool ShouldHideNodeDoc(NodeDocDef d, Dictionary<string, string> keep_name)
        {
            if (d == null || string.IsNullOrEmpty(d.define_id))
                return true;
            if (d.define_id.StartsWith("-"))
                return true;                                   //内部/旧版负数 defineId 节点
            if (d.obsolete)
                return true;                                   //zmcs 标注过时
            if (!string.IsNullOrEmpty(d.editor_name) && keep_name != null
                && keep_name.TryGetValue(d.editor_name, out string kept))
                return kept != d.define_id;                    //同名重复，只留保留项（优先支持项）
            return false;
        }

        /// <summary>增益触发入口节点（增益效果图专用，挂在 BuffData.graph）：
        /// 动作线从 out 触发口接入执行链；self/giver/buff/player/enemy/duration 数据端口供取值线读取增益上下文。
        /// action 与 NodeDocRunner 增益触发事件名保持一致（OnBuffAdding/OnBuffAdded/OnBuffRemoving/OnBuffRemoved/OnBuffTurnStart/OnBuffTurnEnd）。</summary>
        private static List<NodePreset> BuildBuffTriggerPresets()
        {
            List<NodePreset> presets = new List<NodePreset>();
            string[][] defs = new string[][]
            {
                new string[] { "OnBuffAdding", "添加增益时", "增益正要挂上、属性生效之前触发（防递归：嵌套添加增益不再回触发本事件）" },
                new string[] { "OnBuffAdded", "添加增益后", "增益已挂上、属性已生效之后触发" },
                new string[] { "OnBuffRemoving", "移除增益时", "增益正要移除之前触发" },
                new string[] { "OnBuffRemoved", "移除增益后", "增益已移除之后触发" },
                new string[] { "OnBuffTurnStart", "每回合开始", "携带增益的卡所属玩家回合开始时触发（每回合一次，配合 duration 递减）" },
                new string[] { "OnBuffTurnEnd", "每回合结束", "携带增益的卡所属玩家回合结束时触发" },
            };
            foreach (string[] d in defs)
            {
                NodePreset p = new NodePreset();
                p.type = GraphNodeType.Event;
                p.action = d[0];
                p.title = d[1];
                p.desc = d[2];
                p.category = CAT_BUFF_TRIGGER;
                p.supported = true;
                p.pins.Add(new PinDef("out", "触发", NodeValueType.Flow, true));
                p.pins.Add(new PinDef("self", "自身", NodeValueType.Card, true));         //携带增益的卡
                p.pins.Add(new PinDef("giver", "施加者", NodeValueType.Card, true));      //谁加的增益（无则空）
                p.pins.Add(new PinDef("buff", "增益定义", NodeValueType.BuffDefine, true));//本增益定义（BuffPoolIO 查 id）
                p.pins.Add(new PinDef("player", "己方玩家", NodeValueType.Player, true));
                p.pins.Add(new PinDef("enemy", "敌方玩家", NodeValueType.Player, true));
                p.pins.Add(new PinDef("duration", "剩余回合", NodeValueType.Int32, true));//当前剩余持续回合
                presets.Add(p);
            }
            return presets;
        }

        /// <summary>战斗按钮节点预设（分类「按钮」，一图多按钮）：「点击按钮时」/「点击按钮后」是事件入口，
        /// 用 button_id 字段（按钮选择下拉）区分对应按钮；「触发按钮效果」是动作节点，主动触发指定按钮
        /// （模拟完整点击：先「时」后「后」，递归深度受限）。
        /// action 与 NodeDocRunner.RunButtonClick 的触发事件名保持一致（ButtonClicked/ButtonClickedAfter）。</summary>
        private static List<NodePreset> BuildButtonPresets()
        {
            List<NodePreset> presets = new List<NodePreset>();
            string[][] evs = new string[][]
            {
                new string[] { "ButtonClicked", "点击按钮时", "指定按钮被点击的瞬间触发（选择按钮）" },
                new string[] { "ButtonClickedAfter", "点击按钮后", "指定按钮的「点击按钮时」分支执行完毕后再触发（前后两段逻辑）" },
            };
            foreach (string[] d in evs)
            {
                NodePreset p = new NodePreset();
                p.type = GraphNodeType.Event;
                p.action = d[0];
                p.title = d[1];
                p.desc = d[2];
                p.category = CAT_BUTTON;
                p.supported = true;
                p.fields.Add(ButtonField("button_id"));
                p.pins.Add(new PinDef("out", "触发", NodeValueType.Flow, true));
                p.pins.Add(new PinDef("self", "自身", NodeValueType.Card, true));        //点击玩家英雄
                p.pins.Add(new PinDef("player", "己方玩家", NodeValueType.Player, true));
                p.pins.Add(new PinDef("enemy", "敌方玩家", NodeValueType.Player, true));
                presets.Add(p);
            }

            NodePreset act = new NodePreset();
            act.type = GraphNodeType.Action;
            act.action = "TriggerButtonEffect";
            act.title = "触发按钮效果";
            act.desc = "主动触发指定按钮（模拟点击：先「时」后「后」，可被其他节点调用）";
            act.category = CAT_BUTTON;
            act.supported = true;
            act.fields.Add(ButtonField("button_id"));
            act.pins.Add(new PinDef("in", "入", NodeValueType.Flow, false));
            act.pins.Add(new PinDef("out", "出", NodeValueType.Flow, true));
            presets.Add(act);
            return presets;
        }

        /// <summary>按钮选择字段定义（下拉选项运行时从 BattleButtonIO 动态生成，见 CreateFieldButtonSelect）</summary>
        private static FieldDef ButtonField(string name)
        {
            return new FieldDef(name, "按钮", FieldEditType.ButtonSelect, new string[] { "（无）" }, "（无）");
        }

        /// <summary>可延后入口的「等待事件」下拉：中文标签（与 GameLogic.MapWaitEventLabel 的映射一一对应）。
        /// 选「无」=不等待事件，只按延迟（延迟也为 0 时=立即执行）。</summary>
        private static readonly string[] DELAY_WAIT_EVENTS =
        {
            "无", "打出牌", "伤害后", "治疗后", "死亡后", "装备后", "抽卡后", "回合开始后", "回合结束后", "自己起动后"
        };

        /// <summary>区域（牌堆）选项——**与事件入口「生效区域」多选同一套名字**（7 个玩家区域）：
        /// 战场 / 手牌 / 牌库 / 墓地 / 装备区 / 奥秘区 / 英雄。
        /// 所有引用区域的节点字段统一用本数组，避免"装备区(编辑器) vs 装备(运行时)"这类叫法不一致导致勾了不生效。</summary>
        private static readonly string[] ZONE_NAMES =
        {
            "战场", "手牌", "牌库", "墓地", "装备区", "奥秘区", "英雄"
        };

        /// <summary>区域选项 + 内部暂存区（仅 Pile 值通道/移动类节点读写：暂存区无 UI、玩家不可见，是衍生卡未归区时的落地处）</summary>
        private static readonly string[] ZONE_NAMES_WITH_TEMP =
        {
            "战场", "手牌", "牌库", "墓地", "装备区", "奥秘区", "英雄", "暂存区"
        };

        /// <summary>图事件入口与动作节点预设（分类「事件」）：全场监听的「X 时/后」入口 + 「阻止本事件」「修改事件值」。
        /// action 名与 AbilityTrigger 枚举名 / GameLogic.EmitGraphEvent 广播名一致（OnBeforePlay/OnBeforeDamage/OnAfterDamage/OnAfterDraw）。</summary>
        private static List<NodePreset> BuildGraphEventPresets()
        {
            List<NodePreset> presets = new List<NodePreset>();
            string[][] evs = new string[][]
            {
                new string[] { "OnBeforePlay", "使用卡牌时", "任意玩家使用（打出）一张牌之前触发；可阻止该牌打出，value=费用" },
                new string[] { "OnBeforeDamage", "伤害时", "任意卡牌/玩家受伤害结算前触发；可阻止本次伤害，或把 value 改 0=免伤" },
                new string[] { "OnAfterDamage", "伤害后", "任意卡牌/玩家受伤害结算后触发（实际伤害=value，来源=source）" },
                new string[] { "OnAfterDraw", "抽卡后", "任意玩家每次抽到 1 张牌后触发（抽到的牌=subject）" },
                new string[] { "OnBeforeHeal", "治疗时", "任意卡牌/玩家被治疗结算前触发；可阻止或把 value 改 0=无效治疗" },
                new string[] { "OnAfterHeal", "治疗后", "任意卡牌/玩家被治疗结算后触发（治疗量=value；卡用 subject，玩家用 player）" },
                new string[] { "OnBeforeTransform", "变形时", "任意卡牌变形前触发；可阻止本次变形" },
                new string[] { "OnAfterTransform", "变形后", "任意卡牌变形后触发（变形后的卡=subject）" },
                new string[] { "OnBeforeEquip", "装备道具时", "任意单位/英雄装备道具前触发；可阻止（装备不上）" },
                new string[] { "OnAfterEquip", "装备道具后", "任意单位/英雄装备道具后触发（佩戴者=subject，装备=source）" },
                new string[] { "OnBeforeDeath", "死亡时", "任意卡牌死亡（进墓地）前触发；可阻止=免死（卡保持原位）" },
                new string[] { "OnAfterDeath", "死亡后", "任意卡牌死亡进墓地后触发（亡语本身由引擎触发，这里是通知/连锁时机）" },
                new string[] { "OnBeforeDiscard", "弃牌时", "任意卡牌被主动弃置进墓地前触发；可阻止" },
                new string[] { "OnAfterDiscard", "弃牌后", "任意卡牌被弃置进墓地后触发" },
                //起动式（activated）能力的「时/后」：由 GameLogic.CastAbility / AfterAbilityResolved 广播
                new string[] { "OnBeforeActivate", "起动时", "任意玩家发动「起动式能力」（英雄技能/卡牌主动技/装备主动技）之前触发；可阻止（本次发动取消、不扣灵力）；value=本次灵力费用" },
                new string[] { "OnAfterActivate", "起动后", "起动式能力发动结算后触发（灵力已扣、横置已生效、效果已结算）。可配「延迟(毫秒)」与「等待事件」——两者任一满足即执行一次；都留空=立即执行" },
                new string[] { "OnBeforeGameStart", "对战开始时", "对局初始化前通知（纯通知，不可阻止）" },
                new string[] { "OnAfterGameStart", "对战开始后", "玩家开战点（mulligan/首回合前）通知（纯通知）" },
                new string[] { "OnBeforeGameEnd", "游戏结束时", "胜负判定后、对局结束前通知（纯通知）" },
                new string[] { "OnAfterGameEnd", "游戏结束后", "对局结束收尾后通知（纯通知）" },
                new string[] { "OnBeforeTurnStart", "回合开始时", "当前回合玩家回合开始处理前通知（纯通知，不可阻止避免锁局）" },
                new string[] { "OnAfterTurnStart", "回合开始后", "回合开始处理完成后（进入主阶段前）通知" },
                new string[] { "OnBeforeTurnEnd", "回合结束时", "结束回合结算处理前通知（不可阻止，避免回合永远无法结束）" },
                new string[] { "OnAfterTurnEnd", "回合结束后", "回合结束收尾后、切换下家前通知" },
            };
            foreach (string[] d in evs)
            {
                NodePreset p = new NodePreset();
                p.type = GraphNodeType.Event;
                p.action = d[0];
                p.title = d[1];
                p.desc = d[2];
                p.category = CAT_EVENT;
                p.supported = true;
                //生效牌堆（多选）：普通宿主须在该牌堆才响应本入口；主体卡作为额外宿主不受限；选「任意」=全部牌堆
                //字段（对齐醉梦传说「XX/触发时」节点）：生效区域/标签列表/优先级/自定义效果属性
                p.fields.Add(new FieldDef("zones", "生效区域", FieldEditType.MultiOptions,
                    new string[] { "任意", "战场", "手牌", "牌库", "墓地", "装备区", "奥秘区", "英雄" },
                    "英雄;战场;装备区"));
                p.fields.Add(EnumField("tags", "标签列表", new string[] { "无", "战吼", "亡语" }, "无"));
                p.fields.Add(IntField("priority", "优先级", "0"));
                if (d[0] == "OnAfterActivate")
                {
                    //起动后的时机配置：延迟(毫秒) 与 等待事件（任一满足即执行一次；都留空=发动结算后立即执行）
                    p.fields.Add(IntField("delay_ms", "延迟(毫秒)", "0"));
                    p.fields.Add(EnumField("wait_event", "等待事件", DELAY_WAIT_EVENTS, "无"));
                }
                p.fields.Add(new FieldDef("custom_props", "自定义效果属性", FieldEditType.Input, null, ""));   //事件自定义变量（每行 名称:类型[:数组]）
                p.pins.Add(new PinDef("cond", "触发条件", NodeValueType.Boolean, false));   //事件筛选：连线布尔为真才触发（无连线=全部触发）
                p.pins.Add(new PinDef("out", "触发", NodeValueType.Flow, true));
                p.pins.Add(new PinDef("card", "卡牌", NodeValueType.Card, true));           //事件主体卡（死亡=死者 / 被打=受伤者 …）
                p.pins.Add(new PinDef("player", "玩家", NodeValueType.Player, true));       //事件主体玩家
                p.pins.Add(new PinDef("pos", "位置", NodeValueType.Int32, true));           //主体所在牌堆中的位置（slot/第几张）
                presets.Add(p);
            }

            //事件筛选取值（Bool 输出，接到事件入口的「条件」口做连线式筛选）：
            NodePreset eq = new NodePreset();
            eq.type = GraphNodeType.Value;
            eq.action = "EFCardEquals";
            eq.title = "卡牌相同判断";
            eq.desc = "两张卡口是同一张运行时卡则为真（常用于比较 宿主self 与 事件主体subject）";
            eq.category = CAT_EVENT;
            eq.supported = true;
            eq.pins.Add(new PinDef("cardA", "卡 A", NodeValueType.Card, false));
            eq.pins.Add(new PinDef("cardB", "卡 B", NodeValueType.Card, false));
            eq.pins.Add(new PinDef("out", "结果", NodeValueType.Boolean, true));
            presets.Add(eq);

            NodePreset oc = new NodePreset();
            oc.type = GraphNodeType.Value;
            oc.action = "EFCardOwner";
            oc.title = "卡牌归属判断";
            oc.desc = "卡牌属于事件主体方(己方)还是敌方 → 真假（事件条件筛选用，如：只拦自己受伤）";
            oc.category = CAT_EVENT;
            oc.supported = true;
            oc.fields.Add(new FieldDef("side", "归属", FieldEditType.Dropdown, new string[] { "己方", "敌方" }, "己方"));
            oc.pins.Add(new PinDef("card", "卡牌", NodeValueType.Card, false));
            oc.pins.Add(new PinDef("out", "结果", NodeValueType.Boolean, true));
            presets.Add(oc);

            NodePreset op = new NodePreset();
            op.type = GraphNodeType.Value;
            op.action = "EFPlayerOwner";
            op.title = "玩家归属判断";
            op.desc = "玩家属于事件主体方(己方)还是敌方 → 真假（如：是否是我方玩家受伤）";
            op.category = CAT_EVENT;
            op.supported = true;
            op.fields.Add(new FieldDef("side", "归属", FieldEditType.Dropdown, new string[] { "己方", "敌方" }, "己方"));
            op.pins.Add(new PinDef("player", "玩家", NodeValueType.Player, false));
            op.pins.Add(new PinDef("out", "结果", NodeValueType.Boolean, true));
            presets.Add(op);
            return presets;
        }

        /// <summary>项目内灵力节点（分类「玩家」）：三套灵力体系里"最大灵力值"的读/写。
        /// 为什么是项目内节点而不是 NodeDoc 节点：zmcs 只有两档灵力（101014 当前灵力值 / 101015 灵力上限），
        /// TCG2 的"最大灵力值"（灵力上限的增长硬顶 mana_max_total）在 NodeDoc.xml 里没有对应 defineId。
        /// action 用项目内保留字（GetManaMaxTotal/SetManaMaxTotal），由 NodeDocRunner 的取值/执行通道实现；
        /// 分类沿用 NodeDoc 的「玩家」，与 101014/101015/201001/201009 同组，命名与展示逻辑完全一致。</summary>
        private static List<NodePreset> BuildManaPresets()
        {
            List<NodePreset> presets = new List<NodePreset>();

            //获取最大灵力值（取值节点：玩家口 → 整数值）
            NodePreset get = new NodePreset();
            get.type = GraphNodeType.Value;
            get.action = "GetManaMaxTotal";
            get.title = "获取最大灵力值";
            get.desc = "读取玩家的最大灵力值（灵力上限每回合增长能到达的硬顶）。玩家口无连线=施法卡所属玩家。三套灵力：当前灵力 / 灵力上限 / 最大灵力值";
            get.category = "玩家";
            get.supported = true;
            get.pins.Add(new PinDef("player", "玩家", NodeValueType.Player, false));
            get.pins.Add(new PinDef("return", "值", NodeValueType.Int32, true));
            presets.Add(get);

            //设置最大灵力值（动作节点：执行流 + 玩家 + 值）
            NodePreset set = new NodePreset();
            set.type = GraphNodeType.Action;
            set.action = "SetManaMaxTotal";
            set.title = "设置最大灵力值";
            set.desc = "把玩家的最大灵力值（灵力上限的增长硬顶）设为指定值；随后灵力上限与当前灵力按新硬顶收敛（上限不超过最大值）";
            set.category = "玩家";
            set.supported = true;
            set.pins.Add(new PinDef("in", "执行", NodeValueType.Flow, false));
            set.pins.Add(new PinDef("player", "玩家", NodeValueType.Player, false));
            set.pins.Add(new PinDef("value", "值", NodeValueType.Int32, false));
            set.pins.Add(new PinDef("out", "执行", NodeValueType.Flow, true));
            set.fields.Add(IntField("value", "值", "0"));
            presets.Add(set);

            return presets;
        }

        /// <summary>zmcs 风格入口节点（分类「入口」）：主动效果(战吼/法术)/光环/被动(亡语)/事件效果 四入口。
        /// action 与 CardPoolIO 编译保持一致（ActivateEffect/PassiveEffect/AuraEffect/EventEffect），
        /// 拖入后保存卡牌即编译为能力：主动=打出时触发（战吼/法术）、被动=亡语、光环=常驻增益、事件=监听事件。</summary>
        private static List<NodePreset> BuildZmcsEntryPresets()
        {
            List<NodePreset> presets = new List<NodePreset>();

            //1) 主动效果入口（战吼/法术打出时触发；字段/端口与 CardPoolIO.ApplyEntryOverrides 对齐）
            NodePreset act = new NodePreset();
            act.type = GraphNodeType.Event;
            act.action = "ActivateEffect";
            act.title = "主动效果入口";
            act.desc = "zmcs 主动效果=打出时触发（炉石战吼/法术）。目标类型：无=直接执行 / 角色=弹选目标(含英雄) / 英雄=打脸";
            act.category = CAT_ENTRY;
            act.supported = true;
            //事件筛选与标签（与图事件入口一致；cond 连线布尔为真才触发，tags 写入当前事件上下文）
            act.pins.Add(new PinDef("cond", "触发条件", NodeValueType.Boolean, false));
            act.fields.Add(new FieldDef("tags", "标签列表", FieldEditType.Dropdown, new string[] { "无", "战吼", "亡语" }, "无"));
            act.fields.Add(IntField("priority", "优先级", "0"));
            act.fields.Add(BoolField("unique_targets", "目标去重", "false"));   //true=同一张卡只能被一个目标槽选中；默认关闭=允许多槽选同一张
            //目标槽默认为空：点「＋ 新增目标」才生成「目标N条件」输入口与「目标卡牌N」输出口
            //（目标归属不在入口内配置：需要"敌方/友方"限制时，在「目标N条件」链里用「卡牌归属」节点判断）
            act.pins.Add(new PinDef("out", "动作", NodeValueType.Flow, true));
            //数据输出：玩家=施法者所属玩家 / 卡牌=本卡自身 / 目标卡牌N=第N个目标槽选中的卡（按槽动态添加）
            act.pins.Add(new PinDef("player", "玩家", NodeValueType.Player, true));
            act.pins.Add(new PinDef("card", "卡牌", NodeValueType.Card, true));
            presets.Add(act);

            //1b) 起动式效果入口（点击发动；编译为 AbilityTrigger.Activate → 英雄技能/随从技能/装备技能按钮）
            //字段/端口与 CardPoolIO.ApplyEntryOverrides 对齐；目标槽与主动效果入口共用同一套机制
            NodePreset actv = new NodePreset();
            actv.type = GraphNodeType.Event;
            actv.action = "ActivateAbility";
            actv.title = "起动式效果入口";
            actv.desc = "zmcs 起动式效果（activated）：玩家在按钮上点击发动（灵力费用/是否消耗行动在节点上配置）。目标类型：无=直接执行 / 角色=弹选目标(含英雄) / 英雄=直接打脸";
            actv.category = CAT_ENTRY;
            actv.supported = true;
            actv.pins.Add(new PinDef("cond", "发动条件", NodeValueType.Boolean, false));   //可用性筛选：假 → 按钮灰、且发动被拒绝
            //效果标签 / 优先级（与主动效果入口对齐）：
            //  标签 = 写入当前事件上下文 tags（NodeDocRunner 读 tags→tag_list），图内用「当前事件」判断；
            //  优先级 = 同一张图上多个入口同时匹配时的执行顺序（降序）；每张效果图只允许 1 个入口，故正常无影响
            actv.fields.Add(new FieldDef("tags", "效果标签", FieldEditType.Dropdown, new string[] { "无", "战吼", "亡语" }, "无"));
            actv.fields.Add(IntField("priority", "优先级", "0"));
            actv.fields.Add(IntField("mana_cost", "灵力费用", "0"));
            actv.fields.Add(BoolField("exhaust", "消耗行动", "true"));       //true=发动后本卡横置（每回合一次）
            actv.fields.Add(BoolField("once_per_turn", "每回合一次", "false"));//true=本回合已发动过就不允许再发动
            actv.fields.Add(new FieldDef("ability_title", "能力名", FieldEditType.Input, null, ""));   //空=自动用"节点标题：规则图执行"
            actv.fields.Add(new FieldDef("ability_desc", "能力描述", FieldEditType.Input, null, ""));  //支持 <value>/<name> 占位
            actv.fields.Add(BoolField("unique_targets", "目标去重", "false"));   //≥2 个目标槽时才显示
            //目标槽默认为空：点「＋ 新增目标」才生成「目标N条件」输入口与「目标卡牌N」输出口
            actv.pins.Add(new PinDef("out", "发动", NodeValueType.Flow, true));
            actv.pins.Add(new PinDef("player", "玩家", NodeValueType.Player, true));   //发动者（卡拥有者）
            actv.pins.Add(new PinDef("card", "卡牌", NodeValueType.Card, true));       //本卡自身（英雄技能=英雄卡）
            presets.Add(actv);

            //2) 光环效果入口（常驻增益；字段与 CardPoolIO.BuildAuraAbility 对齐）
            NodePreset aura = new NodePreset();
            aura.type = GraphNodeType.Event;
            aura.action = "AuraEffect";
            aura.title = "光环效果入口";
            aura.desc = "zmcs 光环=常驻增益：给 生效区域 内符合 作用区域 的卡持续施加 增益定义（下游动作线暂不执行）";
            aura.category = CAT_ENTRY;
            aura.supported = true;
            aura.fields.Add(EnumField("buff", "增益定义", StatusTypeOptions(), "AddAttack"));
            aura.fields.Add(new FieldDef("live_area", "生效区域", FieldEditType.Dropdown, new string[] { "场上", "手牌", "全部区域" }, "场上"));
            aura.fields.Add(new FieldDef("target_area", "作用区域", FieldEditType.Dropdown, new string[] { "双方", "己方", "敌方" }, "双方"));
            aura.pins.Add(new PinDef("card", "卡牌", NodeValueType.Card, false));          //光环载体（图所在卡）
            aura.pins.Add(new PinDef("out", "动作", NodeValueType.Flow, true));
            aura.pins.Add(new PinDef("target_card", "目标卡牌", NodeValueType.Card, true));
            aura.pins.Add(new PinDef("target_player", "目标玩家", NodeValueType.Player, true));
            presets.Add(aura);

            //3) 被动效果入口（亡语；生效/失效为预留出口，执行层不驱动）
            NodePreset pas = new NodePreset();
            pas.type = GraphNodeType.Event;
            pas.action = "PassiveEffect";
            pas.title = "被动效果入口";
            pas.desc = "zmcs 被动=亡语：本卡死亡时触发，动作从「动作」出口接出（生效/失效出口预留不执行）";
            pas.category = CAT_ENTRY;
            pas.supported = true;
            pas.fields.Add(new FieldDef("tag_list", "标签列表", FieldEditType.Dropdown, new string[] { "战吼", "亡语" }, "亡语"));
            pas.fields.Add(new FieldDef("live_area", "生效区域", FieldEditType.Dropdown, new string[] { "战场", "手牌", "牌库", "墓地", "装备区", "全部区域" }, "战场"));
            pas.fields.Add(IntField("priority", "优先级", "0"));
            pas.pins.Add(new PinDef("out", "动作", NodeValueType.Flow, true));
            pas.pins.Add(new PinDef("enable", "生效动作", NodeValueType.Flow, true));     //预留：不驱动执行
            pas.pins.Add(new PinDef("disable", "失效动作", NodeValueType.Flow, true));    //预留：不驱动执行
            pas.pins.Add(new PinDef("player", "玩家", NodeValueType.Player, true));
            pas.pins.Add(new PinDef("card", "卡牌", NodeValueType.Card, true));
            presets.Add(pas);

            //4) 事件效果入口（监听事件下拉；与 CardPoolIO.MapEventName 一致）
            NodePreset evn = new NodePreset();
            evn.type = GraphNodeType.Event;
            evn.action = "EventEffect";
            evn.title = "事件效果入口";
            evn.desc = "zmcs 事件效果=监听事件触发（下拉选择监听项：回合/打出牌/攻击/死亡/抽到等），动作从「动作」出口接出";
            evn.category = CAT_ENTRY;
            evn.supported = true;
            evn.fields.Add(new FieldDef("event_name", "监听事件", FieldEditType.Dropdown,
                new string[] { "回合结束", "回合开始", "打出牌", "攻击时", "死亡时", "抽到时" }, "回合结束"));
            evn.pins.Add(new PinDef("out", "动作", NodeValueType.Flow, true));
            //对齐醉梦传说：入口不再平铺自身/目标/玩家数据口，事件数据走「当前事件→获取变量」
            presets.Add(evn);
            return presets;
        }

        /// <summary>已接入执行的 NodeDoc 节点白名单（决定库里 zmcs 节点能否拖入画布；每实现一个节点就加进来，
        /// 并清理同名的旧变体——如 102002 卡牌类型判断被 102032 取代、202008/202009/202010 已标过时）</summary>
        private static readonly HashSet<string> SupportedNodeIds = new HashSet<string>
        {
            //BGM（战斗内换背景音乐；表现层请求，不影响对局逻辑）
            "209101",   //设置战斗BGM
            "209102",   //恢复默认BGM
            //动作（执行层已实现）
            "202001",   //造成伤害（单目标）
            "202041",   //造成伤害或法伤（多目标/法伤开关，卡池主流）
            "202016",   //消灭
            "202013",   //治疗目标卡牌（执行器仍兼容 202039/202047 变体，但库内只放这一个避免同名重复）
            //第二批图事件后续补充的已接入节点（第 6 批）
            "112001",   //布尔常量（BooleanConst）
            "112002",   //比较（Compare：运算符 + A/B → 真值）
            "112003",   //整数常量（IntegerConst）
            "112004",   //整数运算（IntegerOperation：运算符 + 多整数 → 整数）
            "112006",   //字符串常量（StringConst）
            "102028",   //卡牌是否是陷阱
            "102019",   //获取护甲值
            "202014",   //禁锢（Freezing）
            "202040",   //封印（Silenced）
            "202018",   //增加护甲
            "202019",   //增加固定数量护甲
            "202020",   //失去护甲
            "201001",   //设置当前灵力值
            "201009",   //设置灵力上限
            "210007",   //卡牌装备到道具栏
            "202006",   //创建衍生卡并装备到道具栏
            "101012",   //玩家是否装备道具
            "200001",   //使玩家获胜（EndGame 收口）
            "200002",   //使玩家失败（让其余玩家获胜）
            //卡牌/卡牌定义取值与判断（对齐醉梦传说 102003/102004/103006/103014/103020/103021/103023）
            "102003",   //卡牌关键词判断（card + 关键词 → 真值）
            "102004",   //获取属性（card + 属性名 → 值）
            "103006",   //卡牌定义关键词判断
            "103014",   //是否为衍生牌（TCG2：非构筑牌）
            "103020",   //获取卡牌定义属性（攻击/生命/法力费用）
            "103021",   //卡牌定义是否是陷阱（奥秘）
            "103023",   //获取卡牌定义的类型（字符串）
            //事件家族（对齐醉梦传说）：当前事件 → 获取变量/转换类型/设置变量/阻止事件
            "108001",   //当前事件（事件上下文：广播链/能力触发链均可取）
            "108002",   //获取变量（事件→变量名→值）
            "108003",   //转换事件类型
            "208003",   //更改攻击目标
            "208004",   //更改使用目标
            "208005",   //伤害/更改受伤卡牌
            "208006",   //伤害/更改伤害源
            "208007",   //伤害/更改伤害值
            "208008",   //治疗/更改治疗值
            "208001",   //设置变量
            "208002",   //阻止事件
            "212001",   //分支动作（真值→动作/否则动作；条件用内置比较/布尔常量节点）
            "212002",   //重复动作（数量→循环执行 动作 口；repeatTime 输出第 n 次迭代供循环体数值口取值）
            "202003",   //创建衍生卡并置入战场（卡牌定义口 v1 填卡牌 id，空位自动选择）
            "202004",   //创建衍生卡并置入手牌（卡牌定义口 v1 填卡牌 id，手牌满则不创建）
            "210001",   //简单抽牌（玩家口无连线默认施法卡所属玩家；手牌满时 TCG2 不抽不爆牌）
            "201003",   //抽目标卡牌（目标卡取值线→从拥有者卡库抽到手牌，手牌满爆牌进墓地）
            //取值/数据（取值线求值已支持）
            "102001",   //这张卡牌（施法卡自身）
            "102010",   //获取卡牌拥有者（Card→Player）
            "101003",   //获取玩家对手
            "101004",   //获取玩家英雄
            "102023",   //获取合法卡牌使用目标（卡牌口 → 该卡能指向的合法目标集合，含可打脸的敌方英雄卡）
            "102024",   //获取合法攻击目标（卡牌口 → 场上随从可攻击的敌方角色集合）
            "103016",   //获取卡牌定义对应角色（v1=定义本身为英雄/随从时返回该定义）
            "103022",   //获取卡牌定义的合法使用目标（定义口 + 源卡牌口）
            "103025",   //获取卡牌定义所属卡池（v1=CardData.packs 首个卡包）
            "111036",   //转换集合类型（卡牌/定义/玩家/增益/事件 之间转换，不可转换元素丢弃）
            "211001",   //遍历（array 集合逐元素展开「动作」口循环体，element 口输出当前元素）
            "202030",   //触发卡牌定义宣言（以 cards 为载体，触发 cardDefine 的打出时能力）
            "202031",   //触发卡牌定义遗言（以 cards 为载体，触发 cardDefine 的死亡时能力）
            "202032",   //触发法术或技能卡牌定义效果
            "203001",   //展示卡牌定义（v1=日志记录，表现层待接入）
            "111004",   //获取元素数量（集合→Int32，可用作伤害值等）
            "111008",   //获取集合中的随机元素
            "111007",   //获取第X个元素（集合口+元素位置[Int32 字段/取值线]）
            "102027",   //获取卡牌属性（卡牌口+属性下拉[攻击/生命/法力费用]→数值，接动作的数值口）
            "202037",   //设置卡牌属性（卡牌口+属性下拉+数值[取值线或字段]→直接设置基础值）
            "210002",   //卡牌置入战场（卡牌口[集合/取值线]→逐张移到拥有者一侧空位，走 PlayCard 触发入场不扣费）
            "206001",   //添加增益（v1 数值增益：攻击/生命加成+持续回合字段，替代 zmcs BuffDefine 引用）
            "206002",   //移除增益（v1=移除卡牌身上加成状态：属性下拉 攻击/生命/全部）
            "206003",   //设置增益属性（v1=设置卡牌身上加成状态的值/持续：属性下拉+数值口）
            "106004",   //获取增益属性（v1=读卡牌身上加成状态的值/持续→整数）
            "112005",   //逻辑运算（且/或/非；内置无对应节点，112002 比较/112004 整数运算与内置重复不放）
            //取值/集合（取值线求值已支持）
            "111012",   //筛选
            "102013",   //获取所有角色
            "102015",   //获取友方角色
            "102017",   //获取敌方角色
            "102014",   //获取友方随从（不含英雄）
            "102016",   //获取敌方随从（不含英雄）
            "101008",   //获取玩家牌库中的卡牌
            "101006",   //获取玩家的手牌
            "101017",   //获取牌堆（牌堆名下拉：牌库/手牌/墓地）
            "102032",   //卡牌类型判断（目标1条件；同名 102002 为 CardDefineSelect 版，不支持，不放出）
            "111005",   //包含（集合口+元素口→布尔，接分支动作真值口；元素 v1 按卡牌解析）
            //控制流 / 临时变量（第二批）
            "212005",   //重复动作直到（条件口+maxRepeatTime 字段；repeatTime 输出当前迭代；硬上限 1000 次）
            "212006",   //停止重复动作（break，作用于最内层循环）
            "212007",   //跳过重复动作（continue，作用于最内层循环）
            "212004",   //设置临时变量（变量名 String 字段 + 值口；v1 全图扁平作用域）
            "112007",   //获取临时变量（整数/布尔/卡牌/集合按用途解析）
            "112010",   //根据条件选择值（isTrue 真→值口 否→否则值口）
            //集合运算（第二批，全部基于卡牌集合/卡牌属性整数集合）
            "111001",   //创建集合（elements 单卡 → 单元素集合）
            "111002",   //向集合添加元素
            "111009",   //反转集合
            "111032",   //打乱集合内元素顺序
            "111022",   //是否所有元素都满足条件（条件回调逐元素；可与 111012 同方式连条件节点）
            "111023",   //是否有任意元素满足条件（条件回调逐元素）
            "111029",   //获取集合内元素直到条件不成立（取开头连续满足段）
            "111030",   //条件不成立后获取剩余元素（跳过开头连续满足段）
            "111013",   //排序（v1 按属性下拉[攻击/生命/法力费用]+升降序；zmcs 原为条件表达式排序键，不支持）
            "111024",   //获取第一个元素
            "111025",   //获取最后一个元素
            "111026",   //获取集合内前X个元素
            "111028",   //获取集合内的随机X个元素
            "111031",   //获取集合内所有元素的某项属性（属性下拉 → 整数集合，供求和/最值/均值）
            "111014",   //求和（上游 111031 或卡牌集合×属性下拉）
            "111015",   //获取最小值
            "111016",   //获取最大值
            "111017",   //获取平均值
            //卡牌/玩家行动（第二批）
            "202015",   //沉默（v1=清空所有状态/特性/持续效果）
            "202038",   //丢弃卡牌
            "202044",   //复制卡牌（目标牌堆下拉：手牌/战场/牌库）
            "202029",   //变形为卡牌定义（isreset 忽略）
            "202028",   //获得控制权（v1 仅转移归属）
            "202005",   //创建衍生卡并洗入牌库
            "210003",   //卡牌移回手牌（手牌满则跳过）
            "210004",   //卡牌洗入牌库（top=true 置牌库顶）
            "210005",   //卡牌置入墓地（走 DiscardCard，场上卡触发死亡）
            "201008",   //增加当前灵力值（不超过上限）
            "201010",   //增加灵力上限
            //卡牌定义家族 / 玩家查询 / 杂项（第三批；zmcs CardDefine ≈ TCG2 CardData）
            "103002",   //获取卡牌定义（cardRef 口 v1 换成卡牌选择字段，填卡牌 id）
            "103008",   //获取单张卡牌的定义（Card→CardData）
            "103010",   //获取卡牌定义花费
            "103011",   //获取卡牌定义攻击力
            "103012",   //获取卡牌定义生命值
            "103024",   //卡牌定义类型判断（目标1条件可用；同名 103005 为 CardDefineSelect 版，不支持）
            "103017",   //卡牌定义具有宣言（v1=定义带 OnPlay 能力）
            "103018",   //卡牌定义具有遗言（v1=定义带 OnDeath 能力）
            "101005",   //获取当前回合的玩家
            "101014",   //获取当前灵力值
            "101015",   //获取灵力上限
            "101018",   //获取玩家的当前回合数（v1 简化为全局回合数）
            "101019",   //玩家是否是先手
            "109008",   //获取当前回合数
            "112008",   //X到Y之间的随机整数（含两端）
            "112009",   //是否不存在（值口解析为 null → 真）
            //定义集合通道（第四批；103003 定义列表的 DefineReference 入口无来源，不放出）
            "103001",   //获取所有卡牌定义（定义集合源头，配 111008 随机元素+202003 可随机召唤）
            //纯查询/取值（第五批，全部真实取值无空转）
            "102005",   //获取卡牌花费
            "102006",   //获取卡牌攻击力
            "102007",   //获取卡牌最大生命值
            "102008",   //获取卡牌当前生命值
            "101002",   //获取玩家属性（Object→v1 整数字段：生命/最大生命/灵力/灵力上限/击杀数）
            "111034",   //获取元素在集合中的位置（找不到 -1）
            "102018",   //是否濒死（GetHP()<=0）
            "102021",   //卡牌具有宣言（v1=定义带 OnPlay 能力）
            "102022",   //卡牌具有遗言（v1=定义带 OnDeath 能力）
            "105001",   //卡牌所在牌堆判断（过时版，同 105005）
            "105005",   //卡牌所在牌堆判断（手牌/牌库/墓地/战场/装备/奥秘）
            "111021",   //是否是某集合的子集
            "111033",   //集合内容是否相同（ignoreOrder）
            "101007",   //获取玩家的墓地卡牌
            "102012",   //获取所有仆从（全场战场随从）
            "111010",   //集合去重
            "111011",   //集合相减
            "111018",   //集合相加
            "111019",   //获取并集
            "111020",   //获取交集
            "111027",   //获取集合内前X个元素之外的元素
            //第二批：只读查询/集合合并（数据模型已具备）
            "102020",   //获取相邻卡牌（战场上同侧左右相邻）
            "101013",   //获取玩家暂存区（cards_temp）
            "101011",   //获取玩家道具（装备区首件）
            "103007",   //获取目标卡牌的定义列表（Card[] → CardDefine[]）
            "111035",   //合并多个集合（多个集合并成一个）
            //法术伤害家族（对齐 TCG2 EffectDamage.bonus_damage：法术伤害加成=卡牌/玩家身上的 TraitData 特性值）
            "101016",   //获取法术伤害（玩家加成 + 基础值 + 法术牌自身加成）
            "102011",   //获取卡牌法术伤害（该卡自身的法术伤害特性值）
            "103013",   //获取卡牌定义法术伤害（该定义属性表里的法术伤害特性值）
            //第三批：数据缺口近似映射（延迟区→奥秘区 / 道具→装备区 / 技能→英雄 / 疲劳→玩家特性 / 标签→关键词·特性）
            "101009",   //获取玩家延迟区（≈cards_secret）
            "210006",   //卡牌置入延迟区（≈cards_secret）
            "202045",   //创建衍生卡并置入延迟区（≈cards_secret）
            "201007",   //摧毁道具（≈移除装备）
            "101010",   //获取玩家技能（≈玩家英雄卡）
            "201006",   //复原技能（≈清除英雄已行动状态）
            "103015",   //获取英雄牌技能（≈返回英雄定义自身）
            "101021",   //获取疲劳层数（Player「疲劳层数」特性）
            "201013",   //设置疲劳层数
            "201014",   //增加疲劳层数
            "201015",   //受到疲劳伤害（伤害=疲劳层数）
            "102033",   //卡牌拥有标签（≈关键词或特性）
            "103004",   //卡牌定义是否具有标签（≈关键词或特性）
            "102034",   //获取卡牌标签列表（String[]：≈关键词/特性 id）
            "103019",   //获取卡牌定义的标签列表（String[]）
            "106031",   //卡牌是否可见（Card vis 特性，只存不发）
            "106030",   //卡牌属性是否可见（Card vis 特性，只存不发）
            "202049",   //设置卡牌可见性（只存不发）
            "202048",   //设置卡牌属性可见性（只存不发）
            //第四批：效果家族（Effect≈TCG2 AbilityData；当前效果由 Run 上下文带入）
            "107001",   //该效果（当前运行的能力）
            "107004",   //获取卡牌的所有效果（运行时 abilities）
            "107005",   //获取增益的所有效果（TCG2 增益无独立效果列表→空）
            "107006",   //获取卡牌定义的所有效果（CardData.abilities）
            "107003",   //获取效果属性（编号/名称/数值/持续/目标/触发/法力）
            "107007",   //获取效果的合法使用目标（按能力目标条件/过滤器筛选全场角色）
            "107009",   //获取效果所属卡牌定义或增益定义（多输出：cardDefine/isCardDefine 生效，buffDefine 空）
            "207001",   //发动效果（执行该能力的 EffectData 组件）
            //第六批：映射（GraphMap = Dictionary<string,object>；113001 创建 → 213001/213002 读写 → 113002~113005 查询）
            "113001",   //创建映射集合
            "113002",   //是否存在映射
            "113003",   //获取映射值
            "113004",   //获取映射的所有键
            "113005",   //获取映射的所有值
            "213001",   //设置映射
            "213002",   //移除映射
            //第七批 T1：Pile 值通道（编码 "玩家id|区域名"；区域=手牌/牌库/墓地/战场/装备/奥秘/暂存区）
            "102029",   //获取卡牌所在牌堆（Card→Pile）
            "105002",   //获取牌堆中的牌（Pile→Card[]）
            "105003",   //获取牌堆名（Pile→String）
            "105004",   //获取牌堆所属玩家（Pile→Player）
            "200004",   //创建牌堆（TCG2 无自定义牌堆 → 近似=固定区域句柄）
            "200005",   //删除牌堆（近似=清空该固定区域）
            "200006",   //从牌堆将卡牌移动到目标牌堆（仅数据型区域：手牌/牌库/墓地/奥秘/暂存区）
            "202046",   //创建衍生卡并置入牌堆（战场自动空位；未提供牌堆→暂存区）
            //第七批 T2：事件日志 + 卡牌快照基础设施（事件家族 108xxx + 事件重复次数 208009）
            "108004",   //事件类型判断（事件口 + 事件类型 → Boolean）
            "108005",   //获取本局游戏事件（按事件类型筛选 → EventArg[]）
            "108006",   //获取本回合事件（→ EventArg[]）
            "108007",   //获取前X-Y回合内的事件（farther/nearer → EventArg[]）
            "108008",   //获取卡牌在某事件前的快照（Card + EventArg → CardSnapshot）
            "108009",   //获取卡牌在某事件后的快照
            "108010",   //获取事件重复次数（→ Int32）
            "108011",   //获取事件发生的回合（→ Int32）
            "108012",   //获取事件的父事件（→ EventArg）
            "108013",   //获取事件的子事件（→ EventArg[]）
            "108014",   //获取事件发生的事件链（父→祖父… → EventArg[]）
            "108015",   //获取事件发生时引发的事件（Before 子事件）
            "108016",   //获取事件发生后引发的事件（After 子事件）
            "208009",   //设置事件重复次数（EventArg + times）
            //第七批 T2：卡牌快照家族 104xxx（快照=Card.CloneNew 深克隆；104003 已过时，不放出）
            "104001",   //获取卡牌快照对应卡牌（→ Card）
            "104002",   //获取卡牌快照卡牌定义（→ CardDefine）
            "104004",   //卡牌快照关键词判断
            "104005",   //获取卡牌快照属性（Object）
            "104006",   //获取卡牌快照花费
            "104007",   //获取卡牌快照攻击力
            "104008",   //获取卡牌快照最大生命值
            "104009",   //获取卡牌快照当前生命值
            "104010",   //获取卡牌快照拥有者（→ Player）
            "104011",   //获取卡牌快照护甲值
            "104014",   //卡牌快照具有宣言
            "104015",   //卡牌快照具有遗言
            "104016",   //卡牌快照是否是陷阱
            "104017",   //卡牌快照类型判断
            "104018",   //卡牌快照拥有标签
            "104019",   //获取卡牌快照标签列表
            //第七批 T2：增益家族补全（Buff 值通道=BuffRef{实例+所属卡}；106002 已过时，不放出）
            "106001",   //该增益（当前增益图上下文 → Buff）
            "106002",   //获取增益定义（BuffSelect 字段 → BuffDefine）
            "106003",   //是否具有增益（卡牌 + buff_id 下拉 → Boolean）
            "106005",   //获取单个增益的定义（Buff → BuffDefine）
            "106006",   //获取卡牌上的所有增益（Card → Buff[]）
            "106007",   //增益属性是否可见（Buff + 属性名 + 玩家 → Boolean，只存不发）
            "206004",   //设置增益属性可见性（只存不发）
            //第七批 T3-a：玩家查询/动作
            "101020",   //获取所有玩家（Player[]；单消费者取首个）
            "101022",   //获取某个回合的玩家（近似=当前回合玩家）
            "201002",   //设置玩家属性（生命/最大生命/灵力/灵力上限/击杀数）
            //第七批 T3-b：选择类（复用入口「目标1条件」选目标通道）
            "201004",   //从一定数量的卡牌中选择一张（返回玩家已选目标；同名 201016 为变体）
            "201005",   //从一定数量的卡牌定义中选择一张（同名 201017 为变体）
            "201016",   //从一定数量的卡牌中选择一张（带玩家口版）
            "201017",   //从一定数量的卡牌定义中选择一张（带玩家口版）
            //第七批 T3-c：卡牌动作 / 多目标伤害 / 杂项
            "202011",   //造成法术伤害（damage + targets）
            "202012",   //造成固定法术伤害并分配给随机目标（近似：逐点随机）
            "202017",   //强制更新游戏状态（RefreshData）
            "202021",   //触发卡牌宣言（≈OnPlay 能力）
            "202022",   //触发卡牌遗言（≈OnDeath 能力）
            "202023",   //触发法术或技能卡牌效果（近似：OnPlay 能力）
            "202024",   //重置卡牌（Card.Clear）
            "202025",   //强制攻击目标（AttackTarget/AttackPlayer）
            "202026",   //本回合攻击次数增加一次（清一次已攻击记录）
            "202027",   //揭示卡牌（奥秘区→墓地）
            "210008",   //卡牌移动到暂存区（cards_temp）
            "202035",   //造成伤害或法伤并分配给目标（近似：均分）
            "202042",   //同名变体（参数顺序不同）
            "202036",   //对固定数量的随机目标造成伤害或法伤
            "202043",   //同名变体（参数顺序不同）
            //第七批 T3-d：部分查询
            "102009",   //获取唯一属性名（v1 原样输出）
            "102035",   //获取卡牌属性名（属性取值器 → 中文名）
            "103003",   //获取卡牌定义列表（近似=全部定义）
        };

        /// <summary>NodeDoc 定义 → 面板预设：端口 1:1 照搬；Int32/Boolean/String 输入转为右侧可编辑常量字段</summary>
        private static NodePreset NodePresetFromDoc(NodeDocDef d)
        {
            NodePreset p = new NodePreset();
            //分类规则：zmcs defineId 前两位即节点大类——10/11=取值/数据节点（卡牌/玩家/增益/集合运算…），
            //20/21=动作/控制节点（效果/卡牌/玩家/行动/分支循环…），全量 306 个无交叉（曾经"看输出口猜类型"
            //的启发式判不对 202003/202004 这类"有返回值输出的动作节点"，已废弃）
            p.type = (d.define_id.StartsWith("20") || d.define_id.StartsWith("21"))
                ? GraphNodeType.Action : GraphNodeType.Value;
            p.action = d.define_id;
            p.title = d.editor_name;
            p.desc = d.CleanSummary();
            p.category = d.category;
            foreach (NodeDocPort ip in d.inputs)
            {
                //206001 添加增益：zmcs 的 BuffDefine 引用在 TCG2 无对应物，v1 不生成引用口，
                //改用下方 攻击加成/生命加成/持续回合 三个数值字段表达增益
                if (p.action == "206001" && ip.type == NodeValueType.BuffDefine)
                    continue;
                //增益家族（206002/106004/206003/106003）：Buff/BuffDefine 引用口不生成，改用 buff_id 下拉字段
                //引用增益定义；106004/206003 没有 card 口，下方补一个。
                //106005 获取增益定义 / 106007 / 206004 保留 Buff 引用口（Buff 值通道已落地）
                if ((p.action == "206002" || p.action == "106004" || p.action == "206003" || p.action == "106003")
                    && (ip.type == NodeValueType.Buff || ip.type == NodeValueType.BuffDefine))
                    continue;
                if ((p.action == "106004" || p.action == "206003") && ip.name == "propName")
                    continue;
                //111013 排序 / 111031 属性映射：zmcs 用条件/选择器表达式当排序键/属性选择，v1 不支持 lambda，
                //这两个口不生成，改用 prop 属性下拉字段（见下方字段区）
                if ((p.action == "111013" && ip.name == "condition") || (p.action == "111031" && ip.name == "selector"))
                    continue;
                //103002 获取卡牌定义：DefineReference 引用口不生成，换成卡牌选择字段（下方字段区）
                if (p.action == "103002" && ip.type == NodeValueType.DefineReference)
                    continue;
                //106002 获取增益定义：DefineReference 引用口不生成，换成增益选择字段（下方字段区）
                if (p.action == "106002" && ip.type == NodeValueType.DefineReference)
                    continue;
                //ActionNode 型"输入"实为分支/循环的动作出口（zmcs 控制流节点规范）→ 转成执行流输出口
                if (ip.type == NodeValueType.ActionNode)
                {
                    p.pins.Add(new PinDef(ip.name, ip.display_name, NodeValueType.Flow, true));
                    continue;
                }
                //112004 整数运算 / 112005 逻辑运算 的"值"参数口：改用**编号输入槽** arg1..argN / value1..valueN
                //（每个槽 = 端口 + 同名字段：既可连线，也可手填值，并支持动态增删），见 BuildParamSlots
                if ((p.action == "112004" || p.action == "112005") && ip.is_params)
                    continue;
                //isParams=true 是"参数列表"口：允许多条取值线接入，运行时逐线求值
                p.pins.Add(new PinDef(ip.name, ip.display_name, ip.type, false, ip.is_array || ip.is_params));
            }
            foreach (NodeDocPort op in d.outputs)
            {
                //111013/111031 的 元素 口是 lambda 表达式的输出口（v1 不支持，见上方输入口说明）
                if ((p.action == "111013" || p.action == "111031") && op.name == "element")
                    continue;
                //outputs 段的 ActionNode 同样是分支口（212001 动作/否则动作）→ 执行流输出口
                if (op.type == NodeValueType.ActionNode)
                {
                    p.pins.Add(new PinDef(op.name, op.display_name, NodeValueType.Flow, true));
                    continue;
                }
                p.pins.Add(new PinDef(op.name, op.display_name, op.type, true, op.is_array));
            }
            //动作节点一律自带 执行 in/out 一对执行流口（才能从入口事件接入执行链）
            if (p.type == GraphNodeType.Action)
            {
                //执行 in/out 固定在各自一侧最上面（输出口插到第一个输出位，分支口/事件参数口排在下面）
                p.pins.Insert(0, new PinDef("in", "执行", NodeValueType.Flow, false));
                int first_out = p.pins.FindIndex(x => x.is_output);
                p.pins.Insert(first_out < 0 ? p.pins.Count : first_out, new PinDef("out", "执行", NodeValueType.Flow, true));
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
                {
                    //209101 设置战斗BGM 的 bgm 口：zmcs 原文是"音乐库条目的标识或显示名"，用自由文本没法选歌 →
                    //改成「音乐库下拉」（显示标题+来源、存 BgmEntry.id；弹层仍可手填标识/显示名）
                    if (p.action == "209101" && ip.name == "bgm")
                        p.fields.Add(new FieldDef(ip.name, ip.display_name, FieldEditType.BgmSelect, null, ""));
                    else
                        p.fields.Add(new FieldDef(ip.name, ip.display_name, FieldEditType.Input, null, ""));
                }
                else if (ip.type == NodeValueType.CardDefine)
                    p.fields.Add(new FieldDef(ip.name, ip.display_name, FieldEditType.CardSelect, null, ""));   //下拉选当前卡池的卡牌（选项在创建控件时动态生成）
                else if (ip.type == NodeValueType.CardType)
                    p.fields.Add(EnumField(ip.name, ip.display_name, TYPE_NAMES, "随从"));  //卡牌类型枚举口 → 中文下拉（如 102032 卡牌类型判断）
                else if (ip.type == NodeValueType.CardPropertyGetterName)
                    p.fields.Add(EnumField(ip.name, ip.display_name, new string[] { "攻击", "生命", "法力费用" }, "攻击"));  //102027 获取卡牌属性
                else if (ip.type == NodeValueType.CardPropertySetterName)
                    p.fields.Add(EnumField(ip.name, ip.display_name, new string[] { "攻击", "生命", "法力费用" }, "攻击"));  //202037 设置卡牌属性
                else if (ip.type == NodeValueType.CardDefinePropertyGetterName)
                    p.fields.Add(EnumField(ip.name, ip.display_name, new string[] { "攻击", "生命", "法力费用" }, "攻击"));  //103020 获取卡牌定义属性
                else if (ip.type == NodeValueType.KeywordName)
                    p.fields.Add(new FieldDef(ip.name, "关键词", FieldEditType.KeywordSelect, null, ""));   //102003/103006 关键词下拉（列游戏自带关键词，存 KeywordData.id）
                else if (ip.type == NodeValueType.PileName)
                {
                    //105001/105005 卡牌所在牌堆判断 / 101017 获取牌堆：统一支持 7 个玩家区域（含「英雄」）
                    //装备区/奥秘区 的写法由运行时区域名归一兜底（同时兼容旧的 装备/奥秘 写法，旧图不必重拖）
                    if (p.action == "105001" || p.action == "105005")
                        p.fields.Add(EnumField(ip.name, ip.display_name, ZONE_NAMES, "战场"));
                    else
                        p.fields.Add(EnumField(ip.name, ip.display_name, ZONE_NAMES_WITH_TEMP, "牌库"));
                }
                else if (ip.type == NodeValueType.Pile)
                {
                    //Pile 型输入口（Pile 值通道 "玩家id|区域名"）：有连线时以线为准；无连线用下拉选区域，
                    //玩家取「选中玩家 / 施法卡所属玩家」
                    p.fields.Add(EnumField(ip.name, ip.display_name, ZONE_NAMES_WITH_TEMP, "牌库"));
                }
                else if (ip.type == NodeValueType.EventReference)
                {
                    //事件类型（108004 事件类型判断 / 108005 获取本局游戏事件）：中文下拉 → 匹配 ctx.action 子串
                    p.fields.Add(EnumField(ip.name, ip.display_name,
                        new string[] { "打出", "伤害", "治疗", "死亡", "弃牌", "装备", "抽卡", "回合", "游戏" }, "伤害"));
                }
                else if (p.action == "202037" && ip.name == "value" && ip.type == NodeValueType.Object)
                    p.fields.Add(IntField(ip.name, ip.display_name, "0"));   //设置属性的无连线默认值（有取值线时以线为准）
                else if (ip.type == NodeValueType.CompareOperator)
                    p.fields.Add(EnumField(ip.name, ip.display_name, new string[] { ">", "<", ">=", "<=", "==", "!=" }, ">"));
                else if (ip.type == NodeValueType.LogicOperator)
                    p.fields.Add(EnumField(ip.name, ip.display_name, new string[] { "且", "或", "非" }, "且"));
                else if (ip.type == NodeValueType.IntegerOperator)
                    p.fields.Add(EnumField(ip.name, ip.display_name, new string[] { "+", "-", "*", "/", "%" }, "+"));
            }
            //206001 添加增益：buff_id 选增益池定义（属性表增益，攻击/生命加成自动映射原生战斗）；
            //duration 覆盖持续回合（0=取定义默认/永久）；旧图 attack_add/hp_add 数值模式仍被运行时兼容
            if (p.action == "206001")
            {
                p.fields.Add(new FieldDef("buff_id", "增益定义", FieldEditType.BuffSelect, null, ""));
                p.fields.Add(IntField("duration", "持续回合(0=默认)", "0"));
            }
            //增益家族 v1：端口/字段替代表达
            if (p.action == "206002")
            {
                p.pins.Insert(0, new PinDef("card", "卡牌", NodeValueType.Card, false));   //目标卡（支持取值线）
                p.fields.Add(new FieldDef("buff_id", "增益定义", FieldEditType.BuffSelect, null, ""));
                p.fields.Add(EnumField("prop", "移除属性", new string[] { "攻击", "生命", "全部" }, "全部"));   //旧路径兼容（无 buff_id 时）
            }
            if (p.action == "106003")
            {
                //是否具有增益：card 口来自 NodeDoc 输入（Card），buff_id 选增益定义 → 布尔
                p.fields.Add(new FieldDef("buff_id", "增益定义", FieldEditType.BuffSelect, null, ""));
            }
            if (p.action == "106004" || p.action == "206003")
            {
                p.pins.Insert(0, new PinDef("card", "卡牌", NodeValueType.Card, false));   //补卡牌定位口（zmcs 原为 Buff 引用）
                p.fields.Add(new FieldDef("buff_id", "增益定义", FieldEditType.BuffSelect, null, ""));
                p.fields.Add(new FieldDef("prop", "属性名", FieldEditType.Input, null, "攻击加成"));   //增益定义里的属性 key（自定义属性名可手填）
            }
            if (p.action == "206003")
                p.fields.Add(IntField("value", "值", "0"));
            //111013 排序 / 111031 属性映射 / 111014~111017 求和最值均值：卡牌属性下拉（排序键/求值属性）
            if (p.action == "111013" || p.action == "111031" || p.action == "111014"
                || p.action == "111015" || p.action == "111016" || p.action == "111017")
                p.fields.Add(EnumField("prop", "属性", new string[] { "攻击", "生命", "法力费用" }, "攻击"));
            //111036 转换集合类型：目标元素类型下拉（TypeName 口在编辑器无内置字段，这里补一个；有连线时以线为准）
            if (p.action == "111036")
                p.fields.Add(EnumField("typeName", "转换类型", new string[] { "卡牌", "卡牌定义", "玩家", "增益", "事件" }, "卡牌"));
            //202044 复制卡牌：zmcs 的 Pile 引用口 v1 换成目标牌堆下拉
            if (p.action == "202044")
                p.fields.Add(EnumField("targetPile", "目标牌堆", new string[] { "手牌", "战场", "牌库" }, "手牌"));
            //103002 获取卡牌定义：DefineReference 引用口的替代表达——卡牌选择下拉（选当前卡池的卡牌）
            if (p.action == "103002")
                p.fields.Add(new FieldDef("cardRef", "卡牌定义", FieldEditType.CardSelect, null, ""));
            //106002 获取增益定义：DefineReference 引用口的替代表达——增益选择下拉（选增益池定义）
            if (p.action == "106002")
                p.fields.Add(new FieldDef("buffDefine", "增益定义", FieldEditType.BuffSelect, null, ""));
            //201002 设置玩家属性：Object 型「值」口的无连线默认值（有取值线时以线为准）
            if (p.action == "201002")
                p.fields.Add(IntField("value", "值", "0"));
            //事件家族（108002 获取变量 / 208001 设置变量）变量名用下拉；108003 转换事件类型用事件类型下拉
            if (p.action == "108002" || p.action == "208001")
                p.fields.Add(EnumField("varName", "变量名",
                    new string[] { "卡牌", "玩家", "来源", "数值", "目标", "标签", "目标卡牌", "伤害值", "治疗值" }, "卡牌"));
            if (p.action == "108003")
                p.fields.Add(EnumField("eventReference", "事件类型",
                    new string[] { "打出", "伤害", "治疗", "死亡", "弃牌", "装备", "抽卡", "回合", "游戏" }, "死亡"));
            //112002 比较：zmcs 的 A/B 是 Object 万能槽（常数或取值线），无连线时用文本字段填固定值
            if (p.action == "112002")
            {
                p.fields.Add(new FieldDef("A", "值A", FieldEditType.Input, null, "0"));
                p.fields.Add(new FieldDef("B", "值B", FieldEditType.Input, null, "0"));
            }
            //112004 整数运算 / 112005 逻辑运算：编号输入槽（**默认 1 个**，用「＋ 新增输入」增加、「×」删除）
            //每个槽 = 一个输入端口 + 同名字段（手填值）；端口有连线时手填框自动隐藏（见 CreateNodeInlineFields）
            if (p.action == "112004")
                BuildParamSlots(p, "arg", 1, true);
            if (p.action == "112005")
                BuildParamSlots(p, "value", 1, false);
            //法术伤害家族：法术伤害加成在 TCG2 里由卡牌/玩家身上的 TraitData 特性承载（EffectDamage.bonus_damage 同源），
            //用 trait_id 指定该特性（与 Resources/Effects/add_spell_damage 的命名一致）
            if (p.action == "101016" || p.action == "102011" || p.action == "103013")
                p.fields.Add(new FieldDef("trait_id", "法术伤害特性", FieldEditType.Input, null, "spell_damage"));
            //标签家族（102033/103004/104018）：zmcs 的 CardTagName 口在 TCG2 用文本标签（关键词/特性 id 或标题）
            if (p.action == "102033" || p.action == "103004" || p.action == "104018")
                p.fields.Add(new FieldDef("tag", "标签", FieldEditType.Input, null, ""));
            //映射家族：Object 型 键/值 口在 TCG2 用文本字段填固定值（可连取值线覆盖）
            if (p.action == "113002" || p.action == "113003" || p.action == "213001" || p.action == "213002")
                p.fields.Add(new FieldDef("key", "键", FieldEditType.Input, null, ""));
            if (p.action == "213001")
                p.fields.Add(new FieldDef("value", "值", FieldEditType.Input, null, ""));
            return p;
        }

        /// <summary>分类过滤是否命中某预设：
        /// 多选集合非空时按集合判断（集合为空=全部，不过滤）；否则退回旧的单选索引（兼容 filter_buttons 那套）。</summary>
        private bool InFilter(NodePreset p)
        {
            if (filter_cats.Count > 0)
            {
                if (filter_cats.Contains(CAT_FAV) && favs.Contains(p.action))
                    return true;
                return filter_cats.Contains(p.category);
            }
            if (filter_index <= 0)
                return true;
            List<string> opts = FilterOptions();
            if (filter_index >= opts.Count)
            {
                filter_index = 0;
                return true;
            }
            string sel = opts[filter_index];
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

            //右侧栏 Tab：生成工具在编辑器里 AddListener 的回调不会存进场景，运行时统一重挂
            if (btn_tab_prop != null)
                btn_tab_prop.onClick.AddListener(() => SelectRightTab(true));
            if (btn_tab_lib != null)
                btn_tab_lib.onClick.AddListener(() => SelectRightTab(false));

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

            //面板激活后的第一帧：执行整页 TMP 迁移（布局 Tab 已由生成工具直接生成，运行时不再重建）
            if (ui_setup_pending && gameObject.activeInHierarchy)
            {
                ui_setup_pending = false;
                EnsureTmpUI();
                if (card != null)
                {
                    RefreshForm();             //把当前卡的值填进新换上的 TMP 控件
                    RefreshCardExtraFields();
                }
                ReplaceFilterDropdown();       //分类过滤：下拉 → 选择按钮+弹层（旧下拉会渲染出空白方块）
                HideLibTitle();                //节点库标题与 Tab 上的「节点库」重复，隐藏
                RewireAudioPreviewButtons();   //试听按钮重挂监听（编辑器加的监听不存场景）
                EnsureAudioDiyButtons();       //音效DIY按钮（运行时补建，避免重跑生成工具）
                SetupRichTextRow("卡牌文本", input_text, "RichTextText");    //多行文本框 → 富文本编辑弹层
                SetupRichTextRow("描述", input_desc, "RichTextDesc");
                SetupArtClipRows();            //卡面图/面板图：点击预览图 → 卡图裁切弹框（支持本地文件导入）
            }
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
            //约束：每张效果图只允许 1 个入口/触发节点（粘贴入口节点同样受限）
            string limit_err;
            if (!CanAddEntryTriggerNode(copied_node.category, out limit_err))
            {
                SetStatus(limit_err);
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
                bool buff_mode = IsBuffMode;
                string hint = buff_mode
                    ? "增益效果图：从节点库「增益触发」分类拖入入口事件（添加增益时/后、移除增益时/后、每回合开始/结束），再连动作节点"
                    : "画布为空：从右侧节点库点选节点，或一键生成示例效果";
                string sample_text = buff_mode
                    ? "一键生成示例效果：添加增益后 → 抽一张牌"
                    : "一键生成示例效果：打出时对目标造成 1 点伤害";

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
                TMP_Text text = null;
                try
                {
                    text = tgo.AddComponent<TextMeshProUGUI>();
                    ApplyNodeFont(text, hint);
                    text.fontSize = 28;
                    text.color = new Color(1f, 1f, 1f, 0.35f);   //空画布引导属提示性文字，保留半透明；非节点文字
                    text.alignment = TextAlignmentOptions.Center;
                    text.raycastTarget = false;
                    text.text = hint;
                }
                catch (System.Exception e)
                {
                    Debug.LogError("空画布提示 TMP 化失败，已回退旧版 Text。原因：\n" + e);
                    text = null;
                    Text back = tgo.AddComponent<Text>();
                    back.font = LegacyUIFont();
                    back.fontSize = 28;
                    back.color = new Color(1f, 1f, 1f, 0.35f);
                    back.alignment = TextAnchor.MiddleCenter;
                    back.raycastTarget = false;
                    back.text = hint;
                }

                //一键示例按钮（下半）
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
                TMP_Text btext = CreateStretchTextChild(bgo.transform, 20, Color.white);
                if (btext != null)
                    btext.text = sample_text;
                btn.onClick.AddListener(buff_mode ? (UnityEngine.Events.UnityAction)BuildSampleBuffEffect : BuildSampleEffect);

                empty_hint = go;
            }
            else if (!empty && empty_hint != null)
            {
                Destroy(empty_hint);
                empty_hint = null;
            }
        }

        /// <summary>一键生成增益示例：添加增益后 → 抽一张牌（增益效果图新手引导）</summary>
        private void BuildSampleBuffEffect()
        {
            if (graph == null)
                return;
            NodePreset p_event = FindPreset(GraphNodeType.Event, "OnBuffAdded");
            NodePreset p_draw = FindPreset(GraphNodeType.Action, "Draw");
            if (p_event == null || p_draw == null)
            {
                SetStatus("示例效果所需节点缺失（增益触发节点需在节点库可拖入）");
                return;
            }

            PushUndo();   //整个示例一次撤销点
            GraphNode ev = CreateNodeFromPreset(p_event, new Vector2Data(240f, -120f));
            GraphNode dn = CreateNodeFromPreset(p_draw, new Vector2Data(520f, -120f));
            if (ev == null || dn == null)
                return;

            //动作线：添加增益后 → 抽牌（OnBuffAdded.out → Draw.in）
            GraphLink link = new GraphLink
            {
                from_node = ev.id,
                from_pin = ev.id + "_out",
                to_node = dn.id,
                to_pin = dn.id + "_in",
            };
            graph.links.Add(link);
            CreateLinkUI(link);

            SelectNode(dn.id);
            RefreshEmptyHint();
            ApplyValidationMarks();
            SetStatus("已生成增益示例：添加增益后 → 抽一张牌（可从「增益触发」分类拖入更多入口事件，如移除增益后、每回合开始）");
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
            //约束：同 AddNodeFromPreset（卡牌效果图只允许 1 个入口/触发节点）→ 不允许时返回 null
            string limit_err;
            if (!CanAddEntryTriggerNode(preset.category, out limit_err))
            {
                SetStatus(limit_err);
                return null;
            }

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
            //TMP 迁移 / Tab 构建需要 AddComponent：面板此时可能还未激活（Open 先于 Show），
            //未激活层级上 AddComponent 会失败，故推迟到激活后的第一帧（见 Update）
            if (!tmp_ui_done)
                ui_setup_pending = true;
            editing_keyword = null;   //退出关键词模式
            editing_rule = null;
            editing_button_config = null;   //退出按钮模式
            this.pool = pool;
            this.card = card;
            this.save_path = savePath;

            //效果图：一张卡可有多张效果图（每张一个入口），默认打开第 0 张
            effect_index = 0;
            graph = null;
            if (card != null)
            {
                List<CardEffectData> effs = card.EnsureEffects();
                if (effs.Count > 0 && effs[0] != null)
                {
                    if (effs[0].graph == null)
                        effs[0].graph = new GraphData();
                    graph = effs[0].graph;
                }
            }
            if (graph == null)
            {
                graph = new GraphData();
                if (card != null)
                {
                    card.EnsureEffects();
                    card.effects[0].graph = graph;
                    card.graph = graph;
                }
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
            {
                MigrateEntryTargetSlots(graph, n);   //入口目标槽：旧的无编号字段/引脚 → 编号化（含连线改写）
                MigratePins(n);
            }
            RefreshForm();
            RefreshPanelArtRow();
            RefreshArt();
            RebuildCanvas();
            RefreshNodeLib();
            ResetView();
            RefreshEffectTabs();

            SetStatus("正在编辑规则图: " + (card != null && !string.IsNullOrEmpty(card.title) ? card.title : "（未命名卡）"));
        }

        // ---------------- 效果图 tab（一张卡多张效果图，每张一个入口） ----------------

        /// <summary>当前卡的效果图列表（卡模式）；非卡模式返回 null</summary>
        private List<CardEffectData> CardEffects()
        {
            return card != null ? card.EnsureEffects() : null;
        }

        /// <summary>把兼容字段 card.graph 同步为第一张效果图（旧工具/旧编译仍读 graph）</summary>
        private void SyncLegacyGraphField()
        {
            if (card != null && card.effects != null && card.effects.Count > 0 && card.effects[0] != null)
                card.graph = card.effects[0].graph;
        }

        /// <summary>按入口节点推断效果名（对齐醉梦传说：主动效果/被动效果/光环效果/事件效果）</summary>
        private static string SuggestedEffectName(GraphData g, int index)
        {
            string prefix = "效果";
            if (g != null)
            {
                foreach (GraphNode n in g.nodes)
                {
                    if (n == null || n.type != GraphNodeType.Event)
                        continue;
                    if (n.action == "ActivateEffect") prefix = "主动效果";
                    else if (n.action == "PassiveEffect") prefix = "被动效果";
                    else if (n.action == "AuraEffect") prefix = "光环效果";
                    else if (n.action == "EventEffect") prefix = "事件效果";
                    else continue;
                    break;
                }
            }
            return prefix + (index + 1);
        }

        /// <summary>切换到第 index 张效果图（切换 graph 引用并重建画布）</summary>
        private void SelectEffect(int index)
        {
            List<CardEffectData> effs = CardEffects();
            if (effs == null || effs.Count == 0)
                return;
            effect_index = Mathf.Clamp(index, 0, effs.Count - 1);
            CardEffectData e = effs[effect_index];
            if (e.graph == null)
                e.graph = new GraphData();
            graph = e.graph;
            if (string.IsNullOrEmpty(graph.name))
                graph.name = (card != null && !string.IsNullOrEmpty(card.title) ? card.title : "NewGraph")
                    + "_" + (effect_index + 1);
            SyncLegacyGraphField();

            node_index = 0;
            undo_stack.Clear();
            redo_stack.Clear();
            copied_node = null;
            foreach (GraphNode n in graph.nodes)
            {
                MigrateEntryTargetSlots(graph, n);   //入口目标槽：旧的无编号字段/引脚 → 编号化（含连线改写）
                MigratePins(n);
            }
            RebuildCanvas();
            ResetView();
            RefreshEffectTabs();
            SetStatus("当前效果图：" + e.name);
            ValidateSingleEntryTrigger();   //每张效果图只允许 1 个入口/触发节点（历史图超标时提示）
        }

        private void OnAddEffect()
        {
            List<CardEffectData> effs = CardEffects();
            if (effs == null)
                return;
            effs.Add(new CardEffectData { name = "效果" + (effs.Count + 1), graph = new GraphData() });
            SelectEffect(effs.Count - 1);
        }

        private void OnDeleteEffect()
        {
            List<CardEffectData> effs = CardEffects();
            if (effs == null)
                return;
            if (effs.Count <= 1)
            {
                SetStatus("至少保留一张效果图");
                return;
            }
            effs.RemoveAt(effect_index);
            SyncLegacyGraphField();
            SelectEffect(Mathf.Min(effect_index, effs.Count - 1));
        }

        /// <summary>构建/刷新效果图 tab 栏（运行时创建；非卡模式隐藏）。
        /// 数量不受上限限制：tab 步距按可用宽度在 96~132 之间自适应压缩，
        /// 保证"配了几个效果就完整显示几个"（旧版固定 132 步距且被左上角标题压住 → 像"只能显示 2 个"）。</summary>
        private void RefreshEffectTabs()
        {
            List<CardEffectData> effs = CardEffects();
            if (effs == null || effs.Count == 0)
            {
                if (effect_tab_bar != null)
                    effect_tab_bar.gameObject.SetActive(false);
                return;
            }
            EnsureEffectTabBar();
            effect_tab_bar.gameObject.SetActive(true);
            effect_tab_bar.SetAsLastSibling();   //画在最上层：不再被左上角其它文字/元素盖住

            for (int i = effect_tab_bar.childCount - 1; i >= 0; i--)
            {
                Transform c = effect_tab_bar.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            //可用宽度：优先按画布视口算（tab 栏属画布区域，避免压到右上工具按钮）
            float avail = EffectTabBarWidth();
            int total = effs.Count + 2;                        //+「+ 新效果」「删除当前」
            float step = Mathf.Clamp(avail / Mathf.Max(1, total), 96f, 132f);
            float tab_w = step - 8f;
            effect_tab_bar.sizeDelta = new Vector2(avail, 26f);

            float x = 0f;
            for (int i = 0; i < effs.Count; i++)
            {
                CardEffectData e = effs[i];
                e.name = SuggestedEffectName(e.graph, i);
                CreateEffectTabButton(e.name, i, i == effect_index, x, tab_w, false);
                x += step;
            }
            CreateEffectTabButton("+ 新效果", -1, false, x, tab_w, true);
            x += step;
            CreateEffectTabButton("删除当前", -2, false, x, tab_w, true);
        }

        /// <summary>tab 栏可用宽度：画布视口宽 → 退面板宽 → 兜底 1000；
        /// 并给右上角工具按钮（撤销/重做/删除节点，从"面板右侧-820"起）留出空间，多效果时也不会压到按钮。</summary>
        private float EffectTabBarWidth()
        {
            float w = 0f;
            if (graph_canvas != null)
            {
                RectTransform vp = graph_canvas.GetComponent<RectTransform>();
                if (vp != null)
                    w = vp.rect.width;
            }

            float panel_w = 0f;
            RectTransform prt = transform as RectTransform;
            if (prt != null)
                panel_w = prt.rect.width;

            if (w < 200f)
                w = panel_w;
            if (w < 200f)
                w = 1000f;

            w -= 40f;
            if (panel_w > 900f)
                w = Mathf.Min(w, panel_w - 860f);
            return Mathf.Max(360f, w);
        }

        private void EnsureEffectTabBar()
        {
            if (effect_tab_bar != null)
                return;
            GameObject go = new GameObject("EffectTabBar", typeof(RectTransform));
            effect_tab_bar = go.GetComponent<RectTransform>();
            effect_tab_bar.SetParent(transform, false);
            effect_tab_bar.anchorMin = new Vector2(0, 1);
            effect_tab_bar.anchorMax = new Vector2(0, 1);
            effect_tab_bar.pivot = new Vector2(0, 1);
            effect_tab_bar.anchoredPosition = new Vector2(30, -64);
            effect_tab_bar.sizeDelta = new Vector2(1000, 26);
        }

        private Font TabFont()
        {
            return LegacyUIFont();   //状态文本已 TMP 化；残留的旧版 UGUI 控件统一用内置字体
        }

        /// <summary>旧版 UGUI 控件字体（TMP 迁移后仅少量旧控件仍需 Font）：取内置字体，避免空引用</summary>
        private static Font LegacyUIFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>效果 tab（左上角）：三种状态配色与字号统一，文字统一走 TMP + 全项目字体管线
        /// （旧版用 Legacy UGUI Text，字体/清晰度与页面其它文字不一致，且操作项与效果项样式无区分）。</summary>
        private void CreateEffectTabButton(string label, int index, bool active, float x, float width, bool action_tab)
        {
            GameObject go = new GameObject("EffectTab", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(effect_tab_bar, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(width, 0);

            Image img = go.GetComponent<Image>();
            img.color = active
                ? new Color(0.20f, 0.55f, 0.85f, 1f)            //选中（当前正在编辑的效果）
                : (action_tab ? new Color(1f, 1f, 1f, 0.24f)    //操作项：+ 新效果 / 删除当前
                              : new Color(1f, 1f, 1f, 0.12f)); //普通效果

            Button btn = go.GetComponent<Button>();
            if (index >= 0)
            {
                int captured = index;
                btn.onClick.AddListener(() => SelectEffect(captured));
            }
            else if (index == -1)
            {
                btn.onClick.AddListener(OnAddEffect);
            }
            else
            {
                btn.onClick.AddListener(OnDeleteEffect);
            }

            TMP_Text t = MakeNodeTmpText(rt, "Label", label, 14, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 6, 0, 6, 0);
            t.color = active
                ? Color.white
                : (action_tab ? new Color(1f, 0.93f, 0.78f, 1f) : new Color(0.88f, 0.92f, 0.98f, 1f));
        }

        /// <summary>
        /// 关键词模式：编辑某个关键词某条规则的规则图（graph 直接引用 rule.graph，保存时写回资产）。
        /// 属性区（卡名/费用等）在关键词模式下不生效，保存仅写回关键词资产。
        /// </summary>
        public void OpenForKeyword(KeywordData keyword, KeywordRule rule)
        {
            if (keyword == null || rule == null)
                return;

            pool = null;
            card = null;
            save_path = null;
            editing_keyword = keyword;
            editing_rule = rule;
            editing_button_config = null;   //退出按钮模式

            graph = rule.graph;
            if (graph == null)
            {
                graph = new GraphData();
                rule.graph = graph;
            }
            if (string.IsNullOrEmpty(graph.name))
                graph.name = "keyword_" + keyword.id;

            node_index = 0;
            undo_stack.Clear();
            redo_stack.Clear();
            copied_node = null;
            foreach (GraphNode n in graph.nodes)
            {
                MigrateEntryTargetSlots(graph, n);   //入口目标槽：旧的无编号字段/引脚 → 编号化（含连线改写）
                MigratePins(n);
            }
            RefreshForm();
            RefreshPanelArtRow();
            RebuildCanvas();
            RefreshNodeLib();
            ResetView();
            RefreshEffectTabs();

            SetStatus("正在编辑关键词规则图: " + keyword.title + "（保存写回关键词资产）");
        }

        /// <summary>是否处于关键词编辑模式</summary>
        public bool IsKeywordMode => editing_keyword != null;

        /// <summary>增益模式：编辑某个增益定义的效果规则图（graph 直接引用 buff.graph，保存时写回 buffs.json）。
        /// 入口节点为「增益触发」事件（添加增益时/后、移除增益时/后、每回合开始/结束）。
        /// 属性区（卡名/费用等）在增益模式下不生效，节点库开放全部节点。</summary>
        public void OpenBuff(BuffData buff)
        {
            if (buff == null)
                return;

            pool = null;
            card = null;
            save_path = null;
            editing_keyword = null;
            editing_rule = null;
            editing_buff = buff;
            editing_button_config = null;   //退出按钮模式

            graph = buff.graph;
            if (graph == null)
            {
                graph = new GraphData();
                buff.graph = graph;
            }
            if (string.IsNullOrEmpty(graph.name))
                graph.name = "buff_" + buff.id;

            node_index = 0;
            undo_stack.Clear();
            redo_stack.Clear();
            copied_node = null;
            foreach (GraphNode n in graph.nodes)
            {
                MigrateEntryTargetSlots(graph, n);   //入口目标槽：旧的无编号字段/引脚 → 编号化（含连线改写）
                MigratePins(n);
            }
            RefreshForm();
            RefreshPanelArtRow();
            RebuildCanvas();
            RefreshNodeLib();
            ResetView();
            RefreshEffectTabs();

            SetStatus("正在编辑增益效果图: " + buff.GetTitle() + "（保存写回 buffs.json，触发入口=增益触发事件）");
        }

        /// <summary>是否处于增益编辑模式</summary>
        public bool IsBuffMode => editing_buff != null;

        /// <summary>是否处于按钮编辑模式（全局按钮图：一图多按钮）</summary>
        public bool IsButtonMode => editing_button_config != null;

        /// <summary>打开按钮编辑模式：编辑全局按钮配置的共享按钮图（一图多按钮）。
        /// 保存时写回 buttons.json（BattleButtonIO.SaveAll）。</summary>
        public void OpenButtons(BattleButtonConfig config)
        {
            if (config == null)
                return;

            pool = null;
            card = null;
            save_path = null;
            editing_keyword = null;
            editing_rule = null;
            editing_buff = null;
            editing_button_config = config;

            graph = config.graph;
            if (graph == null)
            {
                graph = new GraphData();
                config.graph = graph;
            }
            if (string.IsNullOrEmpty(graph.name))
                graph.name = "battle_buttons";

            node_index = 0;
            undo_stack.Clear();
            redo_stack.Clear();
            copied_node = null;
            foreach (GraphNode n in graph.nodes)
            {
                MigrateEntryTargetSlots(graph, n);   //入口目标槽：旧的无编号字段/引脚 → 编号化（含连线改写）
                MigratePins(n);
            }
            RefreshForm();
            RefreshPanelArtRow();
            RebuildCanvas();
            RefreshNodeLib();
            ResetView();
            RefreshEffectTabs();

            SetStatus("正在编辑按钮图: " + (graph.name ?? "battle_buttons") + "（一图多按钮，保存写回 buttons.json）");
        }

        /// <summary>关键词资产保存回调：由 Editor 程序集注册（SetDirty+SaveAssets），运行时为空则只改内存</summary>
        public static System.Action<UnityEngine.Object> keyword_asset_saver;

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
            if (dropdown_keyword != null)
            {
                KeywordData.Load();
                SetupDropdownOptions(dropdown_keyword, KeywordData.GetAll(), keyword_ids, k => k.title, k => k.id);
            }
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
            EnsureCardExtraFields();   //补齐：关键词行（原表单缺失）/ 种族多选 / 音效试听
            RefreshCardExtraFields();
            if (!card_extra_built)
            {
                SetDropdown(dropdown_trait, trait_ids, card.trait);
                SetDropdown(dropdown_team, team_ids, card.team);
                SetDropdown(dropdown_rarity, rarity_ids, card.rarity);
                if (dropdown_keyword != null)
                    SetDropdown(dropdown_keyword, keyword_ids,
                        card.keywords != null && card.keywords.Count > 0 ? card.keywords[0] : "");
            }

            if (dropdown_type != null && !card_extra_built)
            {
                int idx = IndexOf(TYPE_ENUMS, card.type);
                if (idx >= 0 && idx < dropdown_type.options.Count)
                    dropdown_type.value = idx;
            }
            //左上角标题「规则编辑 - 卡名」不再显示：信息无实际用途，且该文字正好压住效果 tab 栏
            //（表现为"配了 3 个效果却只看到 2 个 tab"）。卡名在卡牌列表/属性区已有显示。
            if (title_text != null && title_text.gameObject.activeSelf)
                title_text.gameObject.SetActive(false);
        }

        // ---------------- 卡牌属性区扩展：关键词多选 / 种族多选 / 音效试听 ----------------

        /// <summary>补齐属性区控件（运行时创建，无需重跑生成工具）：
        /// 1) 种族：原单选下拉 → 多选（自带种族多选 + 可自定义）
        /// 2) 关键词：原表单没有该行 → 复制种族行插入多选按钮
        /// 3) 音效 4 行：各加一个「▶」试听按钮</summary>
        private void EnsureCardExtraFields()
        {
            if (card_extra_built || dropdown_trait == null)
                return;
            RectTransform field = dropdown_trait.transform.parent as RectTransform;
            RectTransform row = field != null ? field.parent as RectTransform : null;
            RectTransform content = row != null ? row.parent as RectTransform : null;
            if (field == null || row == null || content == null)
                return;

            //1) 种族：隐藏旧单选下拉，同位置换成多选按钮（TMP）；先清同名残留（场景保存过运行时对象时防重复）
            dropdown_trait.gameObject.SetActive(false);
            Transform stale_sel = field.Find("TraitSelect");
            if (stale_sel != null)
                Destroy(stale_sel.gameObject);
            txt_trait_select = CreateCardSelectButton(field, "TraitSelect", OnClickTraitSelect);

            //1.5) 类型/阵营/稀有度：旧单选下拉 → TMP 选择按钮（统一风格，弹层选择）
            txt_type_select = ReplaceDropdownWithSelect(dropdown_type, "TypeSelect", OnClickTypeSelect);
            txt_team_select = ReplaceDropdownWithSelect(dropdown_team, "TeamSelect", OnClickTeamSelect);
            txt_rarity_select = ReplaceDropdownWithSelect(dropdown_rarity, "RaritySelect", OnClickRaritySelect);

            //2) 关键词：复制种族行（沿用标签/行高/布局），插在种族行下面；先清同名残留防重复
            Transform stale_kw = content.Find("PropRowKeyword");
            if (stale_kw != null)
                Destroy(stale_kw.gameObject);
            RectTransform kw_row = Instantiate(row.gameObject, content).GetComponent<RectTransform>();
            kw_row.name = "PropRowKeyword";
            kw_row.SetSiblingIndex(row.GetSiblingIndex() + 1);
            kw_row.gameObject.SetActive(true);
            SetLabelText(kw_row.Find("PropLabel"), "关键词");
            RectTransform kw_field = kw_row.Find("Field") as RectTransform;
            if (kw_field != null)
            {
                for (int i = kw_field.childCount - 1; i >= 0; i--)   //清掉复制来的旧控件
                    Destroy(kw_field.GetChild(i).gameObject);
                txt_keyword_select = CreateCardSelectButton(kw_field, "KeywordSelect", OnClickKeywordSelect);
            }

            //3) 音效行试听「▶」与短字段凑行已在生成工具中直接生成，此处不再运行时重排

            card_extra_built = true;
        }

        /// <summary>按标签文本找属性行（只查 content 的直接子行；占用行已被打包则查不到，天然幂等）</summary>
        private static RectTransform FindPropRow(RectTransform content, string label)
        {
            if (content == null || string.IsNullOrEmpty(label))
                return null;
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform row = content.GetChild(i) as RectTransform;
                if (row != null && RowLabelOf(row) == label)
                    return row;
            }
            return null;
        }

        /// <summary>属性行标签文本（兼容旧版 Text 与 TMP）</summary>
        private static string RowLabelOf(RectTransform row)
        {
            Transform lb = row != null ? row.Find("PropLabel") : null;
            if (lb == null)
                return "";
            TMP_Text tmp = lb.GetComponent<TMP_Text>();
            if (tmp != null)
                return tmp.text;
            Text legacy = lb.GetComponent<Text>();
            return legacy != null ? legacy.text : "";
        }

        /// <summary>刷新扩展控件显示（种族/关键词多选摘要 + 类型/阵营/稀有度；未注册 id 原样显示）</summary>
        private void RefreshCardExtraFields()
        {
            if (!card_extra_built)
                return;
            CardCustomData c = card;
            if (txt_trait_select != null)
                txt_trait_select.text = c != null ? MultiSummary(c.EnsureTraits(), TraitTitle, "（选择种族）") : "";
            if (txt_keyword_select != null)
                txt_keyword_select.text = c != null ? MultiSummary(c.keywords, KeywordTitle, "（选择关键词）") : "";
            if (txt_type_select != null)
                txt_type_select.text = c != null ? TypeNameOf(c.type) : "";
            if (txt_team_select != null)
                txt_team_select.text = c != null ? OneSummary(TeamTitle(c.team), "（选择阵营）") : "";
            if (txt_rarity_select != null)
                txt_rarity_select.text = c != null ? OneSummary(RarityTitle(c.rarity), "（选择稀有度）") : "";
        }

        /// <summary>单值摘要：空给占位提示</summary>
        private static string OneSummary(string value, string placeholder)
        {
            return string.IsNullOrEmpty(value) ? placeholder : value;
        }

        private static string TypeNameOf(string enum_name)
        {
            int idx = IndexOf(TYPE_ENUMS, enum_name);
            return idx >= 0 && idx < TYPE_NAMES.Length ? TYPE_NAMES[idx] : enum_name;
        }

        private static string TeamTitle(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";
            TeamData.Load();
            TeamData t = TeamData.Get(id);
            return t != null && !string.IsNullOrEmpty(t.title) ? t.title : id;
        }

        private static string RarityTitle(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";
            RarityData.Load();
            RarityData r = RarityData.Get(id);
            return r != null && !string.IsNullOrEmpty(r.title) ? r.title : id;
        }

        /// <summary>旧单选下拉 → TMP 选择按钮（隐藏下拉、同位置放按钮），返回按钮文本</summary>
        private TMP_Text ReplaceDropdownWithSelect(Dropdown dd, string name, Action onClick)
        {
            if (dd == null)
                return null;
            RectTransform field = dd.transform.parent as RectTransform;
            dd.gameObject.SetActive(false);
            if (field == null)
                return null;
            return CreateCardSelectButton(field, name, onClick);
        }

        /// <summary>类型选择（单选）：随从/法术/英雄/神器/装备/奥秘</summary>
        private void OnClickTypeSelect()
        {
            CardCustomData c = card;
            if (c == null)
                return;
            OpenSingleSelectPopup("类型", TypeNameOf(c.type), TYPE_NAMES, TYPE_ENUMS, v =>
            {
                c.type = v;
                CardPoolIO.UpdateCardData(c);
                RefreshCardExtraFields();
            });
        }

        /// <summary>阵营选择（单选）：列出游戏自带阵营；自定义 id 需有对应 TeamData 资产才生效</summary>
        private void OnClickTeamSelect()
        {
            CardCustomData c = card;
            if (c == null)
                return;
            TeamData.Load();
            List<string> titles = new List<string>();
            List<string> ids = new List<string>();
            foreach (TeamData t in TeamData.GetAll())
            {
                if (t == null || string.IsNullOrEmpty(t.title))
                    continue;
                titles.Add(t.title);
                ids.Add(t.id);
            }
            OpenSingleSelectPopup("阵营", TeamTitle(c.team), titles.ToArray(), ids.ToArray(), v =>
            {
                c.team = v;
                CardPoolIO.UpdateCardData(c);
                RefreshCardExtraFields();
            });
        }

        /// <summary>稀有度选择（单选）：列出游戏自带稀有度；自定义 id 需有对应 RarityData 资产才生效</summary>
        private void OnClickRaritySelect()
        {
            CardCustomData c = card;
            if (c == null)
                return;
            RarityData.Load();
            List<string> titles = new List<string>();
            List<string> ids = new List<string>();
            foreach (RarityData r in RarityData.GetAll())
            {
                if (r == null || string.IsNullOrEmpty(r.title))
                    continue;
                titles.Add(r.title);
                ids.Add(r.id);
            }
            OpenSingleSelectPopup("稀有度", RarityTitle(c.rarity), titles.ToArray(), ids.ToArray(), v =>
            {
                c.rarity = v;
                CardPoolIO.UpdateCardData(c);
                RefreshCardExtraFields();
            });
        }

        /// <summary>种族多选：列出游戏自带种族（显示标题、存 TraitData.id），也可输入自定义种族</summary>
        private void OnClickTraitSelect()
        {
            CardCustomData c = card;
            if (c == null)
                return;
            TraitData.Load();
            List<string> titles = new List<string>();
            List<string> ids = new List<string>();
            foreach (TraitData t in TraitData.GetAll())
            {
                if (t == null || string.IsNullOrEmpty(t.title))
                    continue;
                titles.Add(t.title);
                ids.Add(t.id);
            }
            OpenMultiSelectPopup("种族", c.EnsureTraits(), titles.ToArray(), ids.ToArray(), ApplyTraitSelection);
        }

        private void ApplyTraitSelection(List<string> list)
        {
            CardCustomData c = card;
            if (c == null)
                return;
            c.traits = new List<string>(list);
            c.trait = c.traits.Count > 0 ? c.traits[0] : "";   //兼容旧字段
            CardPoolIO.UpdateCardData(c);                      //卡面立即反映
            RefreshCardExtraFields();
        }

        /// <summary>关键词多选：列出游戏自带关键词（显示标题、存 KeywordData.id），也可输入自定义关键词</summary>
        private void OnClickKeywordSelect()
        {
            CardCustomData c = card;
            if (c == null)
                return;
            if (c.keywords == null)
                c.keywords = new List<string>();
            KeywordData.Load();
            List<string> titles = new List<string>();
            List<string> ids = new List<string>();
            foreach (KeywordData k in KeywordData.GetAll())
            {
                if (k == null || string.IsNullOrEmpty(k.title))
                    continue;
                titles.Add(k.title);
                ids.Add(k.id);
            }
            OpenMultiSelectPopup("关键词", c.keywords, titles.ToArray(), ids.ToArray(), ApplyKeywordSelection);
        }

        private void ApplyKeywordSelection(List<string> list)
        {
            CardCustomData c = card;
            if (c == null)
                return;
            c.keywords = new List<string>(list);
            CardPoolIO.UpdateCardData(c);
            RefreshCardExtraFields();
        }

        /// <summary>属性行：填满父级的 TMP 选择按钮（返回文本组件用于刷新显示）</summary>
        private TMP_Text CreateCardSelectButton(RectTransform parent, string name, Action onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            SetStretchRect(rt, 0, 0, 0, 0);
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.18f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            TMP_Text t = MakeNodeTmpText(rt, "Text", "", 14, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 8, 0, 8, 0);
            t.raycastTarget = false;
            return t;
        }

        /// <summary>设置按钮文字（兼容 TMP 与旧版 Text）：用于把模板里的装饰字符统一成字体必有的字形</summary>
        private static void SetButtonText(Transform btn, string text)
        {
            if (btn == null)
                return;
            TMP_Text tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = text;
                return;
            }
            Text legacy = btn.GetComponentInChildren<Text>(true);
            if (legacy != null)
                legacy.text = text;
        }

        /// <summary>属性行标签文本（兼容旧版 Text 与 TMP）</summary>
        private static void SetLabelText(Transform label, string text)
        {
            if (label == null)
                return;
            TMP_Text tmp = label.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = text;
                return;
            }
            Text legacy = label.GetComponent<Text>();
            if (legacy != null)
                legacy.text = text;
        }

        /// <summary>多选摘要文本："冲锋、风怒"；空则显示占位提示</summary>
        private static string MultiSummary(List<string> values, Func<string, string> display, string placeholder)
        {
            if (values == null || values.Count == 0)
                return placeholder;
            List<string> names = new List<string>();
            foreach (string v in values)
                names.Add(display(v));
            return string.Join("、", names.ToArray());
        }

        private static string TraitTitle(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";
            TraitData.Load();
            TraitData t = TraitData.Get(id);
            return t != null && !string.IsNullOrEmpty(t.title) ? t.title : id;
        }

        private static string KeywordTitle(string id)
        {
            return KeywordDisplay(id);   //复用节点侧「id → 关键词标题」
        }

        /// <summary>试听音效槽（0打出 1攻击 2死亡 3受伤）：优先用输入框当前文件名（未保存也能听）</summary>
        public void OnPreviewAudio(int slot)
        {
            if (card == null)
                return;
            TMP_InputField input = AudioInputOf(slot);
            string fname = input != null ? input.text : "";
            if (string.IsNullOrEmpty(fname))
                fname = AudioIdOf(card, slot);
            if (string.IsNullOrEmpty(fname))
            {
                SetStatus("尚未选择音效（先点「选择音频」）");
                return;
            }
            string path = Path.Combine(CardPoolIO.AudioFolder, fname);
            if (!File.Exists(path))
            {
                SetStatus("音频文件不存在: " + fname);
                return;
            }
            CardAudioLoader.Preview(fname);
            SetStatus("试听: " + fname);
        }

        private TMP_InputField AudioInputOf(int slot)
        {
            switch (slot)
            {
                case 0: return input_audio_spawn;
                case 1: return input_audio_attack;
                case 2: return input_audio_death;
                default: return input_audio_damage;
            }
        }

        private static string AudioIdOf(CardCustomData c, int slot)
        {
            switch (slot)
            {
                case 0: return c.spawn_audio_id;
                case 1: return c.attack_audio_id;
                case 2: return c.death_audio_id;
                default: return c.damage_audio_id;
            }
        }

        // ---------------- 全页字体统一（旧版 Text → TMP） ----------------

        /// <summary>把面板里残留的旧版 Text 统一迁移为 TMP（中文动态字体、清晰度与节点内一致）。
        /// 输入框/下拉框内部的 Text 不迁移（由控件自身驱动，迁移会破坏控件）。
        /// 运行时执行一次，无需重跑生成工具；生成工具已用 TMP 的对象自动跳过。</summary>
        private void EnsureTmpUI()
        {
            if (tmp_ui_done)
                return;
            tmp_ui_done = true;

            //1) 面板直接引用的文本：场景里仍是旧版 Text 时先接管（否则引用为空、状态提示会丢）
            if (status_text == null)
                status_text = FindOrConvertText(transform, "StatusText");
            if (node_lib_count == null)
                node_lib_count = FindOrConvertText(transform.Find("LibArea"), "AreaTitle");

            //2) Toggle 勾选标记（graphic）需要重新绑定到 TMP，否则勾选不再显隐（转换失败则保留原 Text）
            foreach (Toggle tg in GetComponentsInChildren<Toggle>(true))
            {
                if (tg != null && tg.graphic is Text gt)
                {
                    TMP_Text converted = ConvertTextToTMP(gt);
                    if (converted != null)
                        tg.graphic = converted;
                }
            }

            //2.5) 输入框：场景里残留的旧版 InputField → TMP_InputField（内部 Text/Placeholder 一并迁移）。
            //面板字段已改为 TMP 类型，旧场景里这些引用会是空，所以按「属性行标签」重新定位并回填。
            input_name = BindInput(input_name, "名称");
            input_mana = BindInput(input_mana, "费用");
            input_attack = BindInput(input_attack, "攻击");
            input_hp = BindInput(input_hp, "生命");
            input_text = BindInput(input_text, "卡牌文本");
            input_desc = BindInput(input_desc, "描述");
            input_cost = BindInput(input_cost, "购买价");
            input_audio_spawn = BindInput(input_audio_spawn, "打出音效");
            input_audio_attack = BindInput(input_audio_attack, "攻击音效");
            input_audio_death = BindInput(input_audio_death, "死亡音效");
            input_audio_damage = BindInput(input_audio_damage, "受伤音效");
            if (node_search_input == null)
            {
                Transform lib = transform.Find("LibArea");
                InputField legacy_search = lib != null ? lib.GetComponentInChildren<InputField>(true) : null;
                if (legacy_search != null)
                    node_search_input = ConvertInputToTMP(legacy_search);
            }

            //3) 其余旧版 Text 全量迁移（输入框/下拉内部跳过）
            Text[] all = GetComponentsInChildren<Text>(true);
            foreach (Text t in all)
            {
                if (t == null)
                    continue;
                if (t.GetComponentInParent<InputField>() != null)   //输入框内部文本
                    continue;
                if (t.GetComponentInParent<Dropdown>() != null)     //下拉框内部文本
                    continue;
                ConvertTextToTMP(t);
            }
        }

        /// <summary>旧版 InputField → TMP_InputField（同物体换组件，内部 Text/Placeholder 迁移为 TMP）。
        /// 调用方负责把返回值回填到面板字段（场景引用指向旧组件，换组件后需重绑）。</summary>
        private TMP_InputField ConvertInputToTMP(InputField legacy)
        {
            if (legacy == null)
                return null;
            GameObject go = legacy.gameObject;
            TMP_InputField tmp = go.GetComponent<TMP_InputField>();
            if (tmp != null)
                return tmp;
            tmp_converted.Add(go);

            TMP_Text text = ConvertTextToTMP(legacy.textComponent as Text);
            TMP_Text ph = ConvertTextToTMP(legacy.placeholder as Text);
            string init = legacy.text;
            bool multiline = legacy.lineType == InputField.LineType.MultiLineNewline;
            Graphic target = legacy.targetGraphic;

            Destroy(legacy);
            try
            {
                tmp = go.AddComponent<TMP_InputField>();
                if (tmp == null)
                {
                    Debug.LogWarning("[TMP迁移] 无法添加 TMP_InputField（物体未激活？）：" + go.name);
                    return null;
                }
                tmp.targetGraphic = target;
                if (text != null)
                {
                    tmp.textComponent = text;
                    text.raycastTarget = false;   //文本不挡射线，点击落到输入框上
                }
                if (ph != null)
                {
                    tmp.placeholder = ph;
                    ph.raycastTarget = false;
                }
                tmp.textViewport = go.transform as RectTransform;   //光标/选区定位用：缺失会导致无法输入
                tmp.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
                tmp.text = init;
                tmp.caretColor = Color.white;   //默认深色光标在深色底上不可见
                //TMP 的 Caret 只在 OnEnable 里创建且要求 textComponent 已赋值；AddComponent 会先触发一次
                //OnEnable（此时为空）→ 光标建不出来。绑定完组件后重启一次输入框补建。
                tmp.enabled = false;
                tmp.enabled = true;
                return tmp;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TMP迁移] 输入框转换失败：" + go.name + "\n" + e);
                return null;
            }
        }

        /// <summary>绑定属性行输入框：按标签找到该行的输入框（旧版则转为 TMP）；找不到时保留现值</summary>
        private TMP_InputField BindInput(TMP_InputField current, string label)
        {
            TMP_InputField found = FindRowInput(label);
            return found != null ? found : current;
        }

        /// <summary>按属性行标签找该行的输入框：已是 TMP 直接返回；旧版 InputField 转换后返回；找不到返回 null</summary>
        private TMP_InputField FindRowInput(string label)
        {
            RectTransform row = FindPropRow(PropFormContent(), label);
            if (row == null)
                return null;
            TMP_InputField tmp = row.GetComponentInChildren<TMP_InputField>(true);
            if (tmp != null)
                return tmp;
            return ConvertInputToTMP(row.GetComponentInChildren<InputField>(true));
        }

        /// <summary>属性表单容器（PropArea/PropScroll/Viewport/Content）</summary>
        private RectTransform PropFormContent()
        {
            Transform prop = transform.Find("PropArea");
            Transform c = prop != null ? prop.Find("PropScroll/Viewport/Content") : null;
            return c as RectTransform;
        }

        /// <summary>
        /// 统一字号：区块标题=FontSection、状态条=FontStatus、其余=FontBody、行标签=FontSmall（一律取 UITheme 令牌）。
        /// 按钮文字例外：保留按钮自身字号（页面级按钮生成时已用 UITheme.FontButton），不被压成正文 16。
        /// </summary>
        private static int NormalizeFontSize(GameObject go, int legacy_size)
        {
            if (go == null)
                return UITheme.FontBody;
            if (go.name == "AreaTitle")
                return UITheme.FontSection;
            if (go.name == "StatusText")
                return UITheme.FontStatus;
            if (go.GetComponentInParent<Button>() != null)
                return legacy_size;
            if (go.name == "PropLabel" || go.name == "ToggleLabel" || go.name.StartsWith("Btn") || go.name.EndsWith("Btn"))
                return UITheme.FontSmall;
            return UITheme.FontBody;
        }

        /// <summary>按名找文本：已是 TMP 直接返回；旧版 Text 则转换后返回；找不到返回 null</summary>
        private TMP_Text FindOrConvertText(Transform root, string name)
        {
            Transform t = root != null ? root.Find(name) : null;
            if (t == null)
                return null;
            TMP_Text tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
                return tmp;
            return ConvertTextToTMP(t.GetComponent<Text>());
        }

        /// <summary>旧版 Text → TMP（保留文本/字号/颜色/对齐/换行/射线），同物体换组件并统一字体</summary>
        private TMP_Text ConvertTextToTMP(Text legacy)
        {
            if (legacy == null)
                return null;
            GameObject go = legacy.gameObject;
            if (tmp_converted.Contains(go))
                return go.GetComponent<TMP_Text>();
            tmp_converted.Add(go);   //标记后不再重试（失败也跳过，避免每次打开都试一遍）

            string text = legacy.text;
            int size = NormalizeFontSize(go, legacy.fontSize);   //统一字号（不再沿用旧版杂乱字号）
            Color color = legacy.color;
            TextAlignmentOptions align = ToTmpAlignment(legacy.alignment);
            bool raycast = legacy.raycastTarget;
            bool wrap = legacy.horizontalOverflow == HorizontalWrapMode.Wrap;
            bool bestfit = legacy.resizeTextForBestFit;

            try
            {
                //先创建并配置 TMP，全部成功后才移除旧组件（失败则保留旧 Text，页面不受影响）
                TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
                if (tmp == null)
                    tmp = go.AddComponent<TextMeshProUGUI>();
                if (tmp == null)
                {
                    Debug.LogWarning("[TMP迁移] 无法添加 TMP 组件，保留旧版 Text：" + go.name);
                    return null;
                }
                ApplyNodeFont(tmp, string.IsNullOrEmpty(text) ? FontProbe : text + FontProbe);
                tmp.text = text;
                tmp.fontSize = size;
                tmp.color = color;
                tmp.alignment = align;
                tmp.raycastTarget = raycast;
                tmp.enableWordWrapping = wrap;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                if (bestfit)
                    tmp.enableAutoSizing = true;
                Destroy(legacy);
                return tmp;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TMP迁移] 转换失败，保留旧版 Text：" + go.name + "\n" + e);
                TextMeshProUGUI bad = go.GetComponent<TextMeshProUGUI>();   //半成品组件销毁，避免与旧 Text 叠影
                if (bad != null)
                    Destroy(bad);
                return null;
            }
        }

        // ---------------- 右侧栏 Tab：卡牌参数 / 节点库（各占满一整列） ----------------

        /// <summary>切换右侧栏页：true=卡牌参数，false=节点库（Tab 栏与整列布局已由生成工具建好，这里只切显隐与高亮）</summary>
        public void SelectRightTab(bool prop)
        {
            right_tab_prop = prop;
            if (prop_area_rt == null)
                prop_area_rt = transform.Find("PropArea") as RectTransform;
            if (lib_area_rt == null)
                lib_area_rt = transform.Find("LibArea") as RectTransform;
            if (prop_area_rt != null)
                prop_area_rt.gameObject.SetActive(prop);
            if (lib_area_rt != null)
                lib_area_rt.gameObject.SetActive(!prop);
            if (prop && node_field_root != null)
                node_field_root.SetActive(false);
            SetTabStyle(tab_prop_text, right_tab_prop);
            SetTabStyle(tab_lib_text, !right_tab_prop);
        }

        private static void SetTabStyle(TMP_Text t, bool active)
        {
            if (t == null)
                return;
            Image img = t.transform.parent != null ? t.transform.parent.GetComponent<Image>() : null;
            if (img != null)
                img.color = active ? new Color(0.2f, 0.55f, 0.85f, 1f) : new Color(1f, 1f, 1f, 0.12f);
            t.color = active ? Color.white : new Color(1f, 1f, 1f, 0.7f);
        }

        /// <summary>节点库分类过滤：TMP 下拉在部分场景会渲染出空白方块（点它弹不出内容），统一换成
        /// 「选择按钮 + 居中多选弹层」（与节点参数选择同款）。本次加固：
        /// ① 字段未绑定时按结构找分类下拉（节点库区域内、与搜索框同层、选项数与分类表一致）；
        /// ② **找不到下拉也必须建出按钮**（挂在搜索框正下方）→ 保证"点分类一定能弹框"；
        /// ③ 旧下拉连同模板一并隐藏；④ 记录日志便于在 Console 核对替换结果。</summary>
        private void ReplaceFilterDropdown()
        {
            if (txt_filter_select != null)
                return;   //已替换过（按钮还在）
            List<string> opts = FilterOptions();
            if (opts == null || opts.Count == 0)
                return;

            //字段绑定可能指向"卡牌参数页"的下拉（旧场景误绑）→ 此时按结构在节点库区里重新找，避免
            //把参数页的下拉隐藏掉、而节点库那个坏下拉还留在屏幕上（症状：点"全部"弹不出内容）
            TMP_Dropdown dd = node_filter_dropdown;
            Transform lib_area = transform.Find("LibArea");
            if (dd != null && lib_area != null && !dd.transform.IsChildOf(lib_area))
            {
                Debug.LogWarning("[节点库] node_filter_dropdown 不在节点库区内（疑似误绑），改为按结构查找分类下拉");
                dd = null;
            }
            if (dd == null)
                dd = FindFilterDropdown();

            //旧分类控件（可能是 TMP_Dropdown，也可能只是"写着某个分类名"的自绘行/按钮）：
            //必须用它来定位并**隐藏**，否则会出现"我的按钮 + 旧控件"两个都显示、互相叠加。
            RectTransform legacy_rt = dd != null ? dd.gameObject.GetComponent<RectTransform>() : FindLegacyFilterWidget();
            RectTransform parent = legacy_rt != null ? legacy_rt.parent as RectTransform : FilterButtonParent();
            if (parent == null)
                return;

            if (dd != null)
            {
                filter_select_value = Mathf.Clamp(dd.value, 0, opts.Count - 1);
                //旧下拉当前值（非"全部"）带入多选集合，保持行为连贯；新场景默认 0=全部 → 集合为空（不过滤）
                if (filter_select_value > 0 && filter_cats.Count == 0)
                    filter_cats.Add(opts[filter_select_value]);
            }
            if (legacy_rt != null)
            {
                legacy_rt.gameObject.SetActive(false);   //旧控件整体隐藏（TMP 下拉的模板会渲染出空白方块）
                Debug.Log("[节点库] 已隐藏旧的分类控件：" + legacy_rt.name);
            }

            Transform stale = parent.Find("FilterSelect");
            if (stale != null)
                Destroy(stale.gameObject);

            GameObject go = new GameObject("FilterSelect", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            if (legacy_rt != null)
            {
                rt.anchorMin = legacy_rt.anchorMin;   //与旧控件完全同位（视觉上原地替换，不会叠加）
                rt.anchorMax = legacy_rt.anchorMax;
                rt.offsetMin = legacy_rt.offsetMin;
                rt.offsetMax = legacy_rt.offsetMax;
            }
            else
            {
                //没有旧下拉可复制位置：放在搜索框正下方（同宽、下一行），保证一定看得见
                RectTransform src = node_search_input != null ? node_search_input.GetComponent<RectTransform>() : null;
                if (src != null)
                {
                    rt.anchorMin = src.anchorMin;
                    rt.anchorMax = src.anchorMax;
                    rt.pivot = src.pivot;
                    rt.anchoredPosition = src.anchoredPosition + new Vector2(0f, -(src.rect.height + 4f));
                    rt.sizeDelta = new Vector2(src.rect.width, Mathf.Max(24f, src.rect.height));
                }
                else
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -44f);
                    rt.sizeDelta = new Vector2(0f, 26f);
                }
            }
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnClickFilterSelect);   //点击 → 居中多选弹层（见 OpenFilterMultiSelect）
            //显示缺陷修复：文字居中，并左右留边（右侧给"下拉提示"留位）。
            //原来左对齐 + 24px 高的大空条，中间整片空白，看起来像"中间内容没显示出来"。
            TMP_Text t = MakeNodeTmpText(rt, "Text", FilterLabel(), 14, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 26, 0, 26, 0);
            t.raycastTarget = false;
            txt_filter_select = t;
            Debug.Log("[节点库] 分类筛选已换成「选择按钮 + 多选弹层」（旧控件="
                + (legacy_rt != null ? legacy_rt.name : "未找到（已按搜索框下方兜底建按钮）") + "）");
        }

        /// <summary>找节点库的分类下拉：只认节点库区内的下拉（排除卡牌参数页的类型/阵营/稀有度下拉），
        /// 并按「选项数与分类表一致 &gt; 与搜索框同层 &gt; 名字像分类」打分取最优。找不到返回 null。</summary>
        private TMP_Dropdown FindFilterDropdown()
        {
            List<string> opts = FilterOptions();
            Transform lib = transform.Find("LibArea");
            TMP_Dropdown best = null;
            int best_score = -1;
            TMP_Dropdown[] all = GetComponentsInChildren<TMP_Dropdown>(true);
            foreach (TMP_Dropdown d in all)
            {
                if (d == null)
                    continue;
                if (lib != null && !d.transform.IsChildOf(lib))
                    continue;                       //只认节点库区域内
                int score = 0;
                if (d.options != null && opts != null && d.options.Count == opts.Count)
                    score += 4;                     //选项数与分类表一致 → 基本可确认
                if (node_search_input != null && d.transform.parent == node_search_input.transform.parent)
                    score += 3;                     //与搜索框同层（分类栏就在搜索框下面）
                string n = d.name;
                if (n.IndexOf("Filter", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Category", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("分类", System.StringComparison.Ordinal) >= 0)
                    score += 2;
                if (score > best_score)
                {
                    best_score = score;
                    best = d;
                }
            }
            return best;
        }

        /// <summary>分类按钮找不到旧下拉时的父级：搜索框所在层 → 节点库区 → 面板本体</summary>
        private RectTransform FilterButtonParent()
        {
            if (node_search_input != null && node_search_input.transform.parent != null)
                return node_search_input.transform.parent as RectTransform;
            Transform lib = transform.Find("LibArea");
            if (lib != null)
                return lib as RectTransform;
            return transform as RectTransform;
        }

        /// <summary>
        /// 找节点库里"旧的分类控件"——**不限类型**：可能是 TMP_Dropdown，也可能只是写着"全部"的自绘行/按钮
        /// （实际遇到的就是这种：它不是 Dropdown，所以之前"找不到就什么都不隐藏"，导致旧控件与我的按钮叠加）。
        /// 识别方式：它在搜索框所在层或节点库区里，且自身/子级文本**恰好等于某个分类名**（全部/收藏/分类名）。
        /// 排除：我的 FilterSelect 按钮、搜索框自身、节点列表（ScrollRect）内部——避免误伤列表项。
        /// </summary>
        private RectTransform FindLegacyFilterWidget()
        {
            List<string> opts = FilterOptions();
            if (opts == null || opts.Count == 0)
                return null;
            Transform[] roots = new Transform[]
            {
                node_search_input != null ? node_search_input.transform.parent : null,   //分类栏与搜索框同层
                transform.Find("LibArea"),
            };
            foreach (Transform root in roots)
            {
                if (root == null)
                    continue;
                RectTransform hit = ScanForFilterWidget(root as RectTransform, opts);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        /// <summary>在 root 子树里找第一个"显示着分类名"的物体（深度优先 → 取到的即层级最浅的那个）</summary>
        private RectTransform ScanForFilterWidget(RectTransform root, List<string> opts)
        {
            if (root == null)
                return null;
            Transform list = node_lib_scroll != null ? node_lib_scroll.transform : null;
            Transform search = node_search_input != null ? node_search_input.transform : null;
            RectTransform[] all = root.GetComponentsInChildren<RectTransform>(true);
            foreach (RectTransform c in all)
            {
                if (c == null || c == root || !c.gameObject.activeSelf)
                    continue;
                if (c.name == "FilterSelect")
                    continue;                                   //我自己（及子级）不要动
                if (list != null && c.IsChildOf(list))
                    continue;                                   //节点列表内部（列表项不参与识别）
                if (search != null && (c == search || c.IsChildOf(search)))
                    continue;                                   //搜索框自身/其子级
                if (search != null && search.IsChildOf(c))
                    continue;                                   //是搜索框的父级容器（隐藏它会连带藏掉搜索框）
                if (WidgetShowsCategory(c, opts))
                    return c;
            }
            return null;
        }

        /// <summary>该物体是否显示着某个分类名（TMP 或旧版 UGUI 文本，精确匹配，避免误伤长句标签）</summary>
        private static bool WidgetShowsCategory(RectTransform c, List<string> opts)
        {
            foreach (TMP_Text t in c.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == null)
                    continue;
                string s = (t.text ?? "").Trim();
                if (!string.IsNullOrEmpty(s) && opts.Contains(s))
                    return true;
            }
            foreach (Text t in c.GetComponentsInChildren<Text>(true))
            {
                if (t == null)
                    continue;
                string s = (t.text ?? "").Trim();
                if (!string.IsNullOrEmpty(s) && opts.Contains(s))
                    return true;
            }
            return false;
        }

        /// <summary>隐藏节点库里的旧分类控件（幂等）。每次刷新列表都调用一次：
        /// 万一旧控件被其它逻辑重新激活，也不会再与我的按钮叠成"两个全部"。</summary>
        private void HideLegacyFilterWidget()
        {
            if (txt_filter_select == null)
                return;
            RectTransform legacy = FindLegacyFilterWidget();
            if (legacy == null)
                return;
            if (legacy.name == "FilterSelect")
                return;
            if (legacy.gameObject.activeSelf)
            {
                legacy.gameObject.SetActive(false);
                Debug.Log("[节点库] 隐藏残留的旧分类控件：" + legacy.name);
            }
        }

        /// <summary>节点分类过滤的多选集合（空=全部）。与筛选按钮文案、InFilter 过滤共用。</summary>
        private readonly HashSet<string> filter_cats = new HashSet<string>();

        /// <summary>分类选择：点击按钮 → **居中的多选选择框**（复用节点参数那套多选弹层 UI，
        /// 配色/边框/选中态与既有多选框完全一致）。「全部」=清空选择（不过滤）；其余分类可多选。</summary>
        private void OnClickFilterSelect()
        {
            Debug.Log("[节点库] 点击分类筛选（当前：" + FilterLabelText() + "）→ 打开居中多选弹层");
            try
            {
                OpenFilterMultiSelect();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[节点库] 打开分类多选弹层失败：" + e);
                SetStatus("打开分类选择框失败：" + e.Message);
            }
            //自检：弹层若没能显示出来，留下明确日志（便于区分"构建失败"与"被遮挡/未激活"）
            if (field_select_popup == null || !field_select_popup.activeSelf)
                Debug.LogError("[节点库] 分类多选弹层未显示：field_select_popup="
                    + (field_select_popup == null ? "null" : "inactive"));
        }

        /// <summary>打开分类多选弹层；提交后更新按钮文案并刷新节点列表</summary>
        private void OpenFilterMultiSelect()
        {
            List<string> opts = FilterOptions();
            if (opts == null || opts.Count == 0)
                return;
            //未选任何分类时把「全部」作为初始勾选项，保证弹层里能看到当前是"全部"
            List<string> initial = filter_cats.Count == 0 ? new List<string> { CAT_ALL } : new List<string>(filter_cats);
            OpenMultiSelectPopup("节点分类", initial, opts.ToArray(), null, list =>
            {
                filter_cats.Clear();
                if (list != null)
                {
                    foreach (string v in list)
                    {
                        if (string.IsNullOrEmpty(v) || v == CAT_ALL)
                            continue;   //「全部」不是一个真实分类：选它就等于清空（=全部）
                        filter_cats.Add(v);
                    }
                }
                if (txt_filter_select != null)
                    txt_filter_select.text = FilterLabel();
                RefreshNodeLib();
            });
        }

        /// <summary>筛选按钮文案（含右侧下拉提示 ▾）：未选=全部；单选=分类名；多选=「N 个分类 +」</summary>
        private string FilterLabel()
        {
            string s = FilterLabelText();
            return string.IsNullOrEmpty(s) ? s : s + "  ▾";
        }

        /// <summary>筛选按钮文案主体（不含 ▾）</summary>
        private string FilterLabelText()
        {
            if (filter_cats.Count == 0)
                return CAT_ALL;
            if (filter_cats.Count == 1)
            {
                foreach (string s in filter_cats)
                    return s;
            }
            return filter_cats.Count + " 个分类 +";
        }

        /// <summary>隐藏节点库标题（Tab 上已有「节点库」字样，数量提示冗余）</summary>
        private void HideLibTitle()
        {
            if (node_lib_count != null)
            {
                node_lib_count.gameObject.SetActive(false);
                return;
            }
            Transform lib = transform.Find("LibArea");
            Transform title = lib != null ? lib.Find("AreaTitle") : null;
            if (title != null)
                title.gameObject.SetActive(false);
        }

        /// <summary>音效试听按钮：生成工具已建（PlayAudioBtn_0~3），但编辑器里加的监听不会存进场景，运行时重挂</summary>
        private void RewireAudioPreviewButtons()
        {
            string[] labels = { "打出音效", "攻击音效", "死亡音效", "受伤音效" };
            RectTransform content = PropFormContent();
            if (content == null)
                return;
            for (int slot = 0; slot < labels.Length; slot++)
            {
                RectTransform row = FindPropRow(content, labels[slot]);
                if (row == null)
                    continue;
                string target = "PlayAudioBtn_" + slot;
                foreach (Button b in row.GetComponentsInChildren<Button>(true))
                {
                    if (b != null && b.gameObject.name == target)
                    {
                        int captured = slot;
                        b.onClick.AddListener(() => OnPreviewAudio(captured));
                        break;
                    }
                }
            }
        }

        // ---------------- 音效DIY（可视化音效编辑器入口） ----------------

        /// <summary>给 4 个音效行补「音效DIY」按钮：生成工具不含该按钮，运行时补建（幂等）。
        /// 关键：音效行的控件（输入框/按钮）在 PropRow 的子物体 **Field** 里（CreateFieldRow 返回 Field），
        /// 因此所有重排与新建都必须相对 Field 做，否则坐标系错位会与「选择音频」按钮叠字。
        /// 重排比例（相对 Field）：输入 0~0.46 / 选择音频 0.47~0.64 / 音效DIY 0.65~0.83 / ▶ 0.85~1。</summary>
        private void EnsureAudioDiyButtons()
        {
            string[] labels = { "打出音效", "攻击音效", "死亡音效", "受伤音效" };
            RectTransform content = PropFormContent();
            if (content == null)
                return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);   //先结算布局，才能按实际宽度决定按钮文案

            for (int slot = 0; slot < labels.Length; slot++)
            {
                RectTransform row = FindPropRow(content, labels[slot]);
                if (row == null)
                    continue;
                RectTransform field = FieldOfRow(row);
                if (field == null)
                    continue;
                if (field.Find("DiyAudioBtn") != null)
                    continue;   //已建（幂等）

                //行内重排：给 DIY 按钮腾位置，同时清零 offset，避免锚点与旧偏移互相拉扯导致重叠
                RectTransform input = FindChildRect(field, "AudioInput");
                SetRowAnchors(input, 0f, 0.46f);
                ShrinkRowLabel(input, 14f);
                RectTransform pick = FindChildRect(field, "PickAudioBtn");
                SetRowAnchors(pick, 0.47f, 0.64f);
                ShrinkRowLabel(pick, 14f);          //按钮变窄后同步缩字号，防止文字溢出/裁切
                RectTransform play = FindChildRect(field, "PlayAudioBtn_" + slot);
                SetRowAnchors(play, 0.85f, 1f);
                ShrinkRowLabel(play, 15f);

                //音效DIY 按钮
                GameObject go = new GameObject("DiyAudioBtn", typeof(RectTransform));
                go.transform.SetParent(field, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                SetRowAnchors(rt, 0.65f, 0.83f);

                Image img = go.AddComponent<Image>();
                img.color = new Color(0.95f, 0.72f, 0.35f, 0.5f);
                Button btn = go.AddComponent<Button>();
                btn.targetGraphic = img;

                GameObject txt_go = new GameObject("Text", typeof(RectTransform));
                txt_go.transform.SetParent(go.transform, false);
                RectTransform txt_rt = txt_go.GetComponent<RectTransform>();
                txt_rt.anchorMin = Vector2.zero;
                txt_rt.anchorMax = Vector2.one;
                txt_rt.offsetMin = new Vector2(2f, 2f);   //留内边距，文字不贴边
                txt_rt.offsetMax = new Vector2(-2f, -2f);
                TextMeshProUGUI txt = txt_go.AddComponent<TextMeshProUGUI>();
                ApplyNodeFont(txt);
                //窄屏（面板很窄时 DIY 按钮只有几十像素）自动用短标签，避免省略号截断或压字
                float diy_width = field.rect.width > 1f ? field.rect.width * 0.18f : 80f;
                txt.text = diy_width < 62f ? "DIY" : "音效DIY";
                txt.fontSize = 14;
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = Color.white;
                txt.raycastTarget = false;
                txt.enableWordWrapping = false;           //五个字放不下时按省略号截断，而不是换行撑高/覆盖
                txt.overflowMode = TextOverflowModes.Ellipsis;

                int captured = slot;
                btn.onClick.AddListener(() => OnAudioDiy(captured));
            }
        }

        /// <summary>音效行控件所在的容器（CreateFieldRow 的子物体 "Field"；旧布局无该容器时回退 PropRow 本身）</summary>
        private static RectTransform FieldOfRow(RectTransform row)
        {
            if (row == null)
                return null;
            Transform field = row.Find("Field");
            return field != null ? field as RectTransform : row;
        }

        /// <summary>在容器内按名字找控件（先直系子物体，再退化到深层搜索，兼容不同生成版本）</summary>
        private static RectTransform FindChildRect(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;
            Transform t = parent.Find(name);
            if (t == null)
            {
                Transform[] all = parent.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].name == name)
                    {
                        t = all[i];
                        break;
                    }
                }
            }
            return t as RectTransform;
        }

        /// <summary>行内控件按横向比例定位（并清零 offset，避免与生成工具留下的偏移叠加）</summary>
        private static void SetRowAnchors(RectTransform rt, float min_x, float max_x)
        {
            if (rt == null)
                return;
            rt.anchorMin = new Vector2(min_x, 0f);
            rt.anchorMax = new Vector2(max_x, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>行内控件文字缩字号（TMP 与旧版 Text 都处理），并改为不换行+省略号，避免文字裁切或撑破控件</summary>
        private static void ShrinkRowLabel(RectTransform rt, float size)
        {
            if (rt == null)
                return;
            TMP_Text tmp = rt.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.fontSize = size;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
            }
            Text legacy = rt.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                legacy.fontSize = Mathf.RoundToInt(size);
                legacy.horizontalOverflow = HorizontalWrapMode.Overflow;
                legacy.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        /// <summary>打开可视化音效编辑器：先载入该槽当前音效作为素材（首次为空），再弹框。</summary>
        public void OnAudioDiy(int slot)
        {
            if (card == null)
                return;
            Button btn = AudioDiyButtonOf(slot);
            if (btn == null)
            {
                SetStatus("未找到音效位按钮（请重新打开规则编辑器）");
                return;
            }

            AudioClipEditorUI editor = btn.GetComponent<AudioClipEditorUI>();
            if (editor == null)
            {
                editor = btn.gameObject.AddComponent<AudioClipEditorUI>();
                int captured = slot;
                editor.onAudioChanged += clip => OnAudioEdited(captured, clip);   //只在首次订阅，避免重复回写
            }
            editor.slot_index = slot;
            editor.slot_label = AudioSlotLabel(slot);

            string id = AudioIdOf(card, slot);
            if (string.IsNullOrEmpty(id))
            {
                editor.Open();
                return;
            }

            SetStatus("载入音效中：" + id + " …");
            CardAudioLoader.LoadClip(id, clip =>
            {
                if (editor != null)
                    editor.SetCurrentClip(clip, id);
                if (editor != null)
                    editor.Open();
            });
        }

        /// <summary>音效DIY确定后回写：加工结果编码为 WAV 落盘到 Workshop/Audio，再写入对应音效槽并刷新运行时卡数据。
        /// 命名 {cardId}_{slot}_diy.wav（与「选择音频」的 {cardId}_{slot}{ext} 区分，互不覆盖）。</summary>
        private void OnAudioEdited(int slot, AudioClip clip)
        {
            if (card == null)
                return;

            if (clip == null)
            {
                SetAudioId(slot, "");
                CardPoolIO.UpdateCardData(card);
                SetStatus("已清空" + AudioSlotLabel(slot));
                return;
            }

            try
            {
                byte[] wav = PoolPackageIO.AudioClipToWav(clip);
                if (wav == null || wav.Length == 0)
                {
                    SetStatus("音效保存失败：无法编码为 WAV");
                    return;
                }
                Directory.CreateDirectory(CardPoolIO.AudioFolder);
                string fname = (string.IsNullOrEmpty(card.id)
                        ? "audio_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                        : card.id)
                    + "_" + slot + "_diy.wav";
                File.WriteAllBytes(Path.Combine(CardPoolIO.AudioFolder, fname), wav);

                CardAudioLoader.Invalidate(fname);   //同名文件被覆盖，旧缓存必须失效
                SetAudioId(slot, fname);
                CardPoolIO.UpdateCardData(card);     //让真实对局立即用上新音效
                SetStatus(string.Format("音效已保存：{0}（{1:0.00}s）", fname, clip.length));
            }
            catch (Exception e)
            {
                Debug.LogError("[音效DIY] 保存失败: " + e.Message);
                SetStatus("音效保存失败: " + e.Message);
            }
        }

        private Button AudioDiyButtonOf(int slot)
        {
            string[] labels = { "打出音效", "攻击音效", "死亡音效", "受伤音效" };
            RectTransform content = PropFormContent();
            if (content == null || slot < 0 || slot >= labels.Length)
                return null;
            RectTransform row = FindPropRow(content, labels[slot]);
            if (row == null)
                return null;
            RectTransform rt = FindChildRect(FieldOfRow(row), "DiyAudioBtn");
            return rt != null ? rt.GetComponent<Button>() : null;
        }

        private void SetAudioId(int slot, string fname)
        {
            switch (slot)
            {
                case 0: card.spawn_audio_id = fname; SetInput(input_audio_spawn, fname); break;
                case 1: card.attack_audio_id = fname; SetInput(input_audio_attack, fname); break;
                case 2: card.death_audio_id = fname; SetInput(input_audio_death, fname); break;
                default: card.damage_audio_id = fname; SetInput(input_audio_damage, fname); break;
            }
        }

        private static string AudioSlotLabel(int slot)
        {
            switch (slot)
            {
                case 0: return "打出音效";
                case 1: return "攻击音效";
                case 2: return "死亡音效";
                default: return "受伤音效";
            }
        }

        // ---------------- 富文本编辑弹层（卡牌文本/描述，TMP 标签源码编辑） ----------------

        /// <summary>把多行文本框换成「点击弹出富文本编辑」的按钮：摘要显示去标签后的前 40 字，点击弹编辑层</summary>
        private void SetupRichTextRow(string label, TMP_InputField source, string btn_name)
        {
            if (source == null)
                return;
            RectTransform row = FindPropRow(PropFormContent(), label);
            if (row == null)
                return;
            Transform field = row.Find("Field");
            if (field == null)
                return;
            Transform stale = field.Find(btn_name);
            if (stale != null)
                Destroy(stale.gameObject);
            source.gameObject.SetActive(false);   //原输入框隐藏（值仍在，保存逻辑不变）

            GameObject go = new GameObject(btn_name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(field, false);
            SetStretchRect(rt, 0, 0, 0, 0);
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Text", RichTextSummary(source.text), 14, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 8, 6, 8, 6);
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            TMP_Text captured = t;
            btn.onClick.AddListener(() =>
            {
                OpenRichTextEditor(label, source, v => { if (captured != null) captured.text = RichTextSummary(v); });
            });
        }

        /// <summary>富文本摘要：去标签后的前 40 字（空值给占位提示）</summary>
        private static string RichTextSummary(string rich)
        {
            if (string.IsNullOrEmpty(rich))
                return "（点击编辑）";
            string plain = System.Text.RegularExpressions.Regex.Replace(rich, "<[^>]+>", "").Replace("\n", " ").Trim();
            return plain.Length > 40 ? plain.Substring(0, 40) + "…" : plain;
        }

        /// <summary>打开富文本编辑弹框：确认后回写 source 并触发 on_confirm（刷新入口摘要）。
        /// 未绑定场景弹框时自动在同 Canvas 下创建（复用 UI/RichText 下的通用组件）。</summary>
        public void OpenRichTextEditor(string title, TMP_InputField source, System.Action<string> on_confirm)
        {
            if (rich_text_popup == null)
                rich_text_popup = RichTextPopupUI.Create(transform);
            if (rich_text_popup == null)
                return;

            TMP_FontAsset node_font = NodeFont();   //复用画布已渲染成功的中文字体，避免弹框出现方块字
            if (node_font != null)
                rich_text_popup.font = node_font;
            rich_text_popup.title = string.IsNullOrEmpty(title) ? "编辑卡牌描述" : title;

            // 「确定」时必须把结果写回表单输入框：该输入框被隐藏但保存时 ReadForm 仍按它取值，
            // 只调 on_confirm（仅刷新摘要）会导致卡牌文本存不进去、重开丢样式
            rich_text_popup.Open(source != null ? source.text : "", v =>
            {
                if (source != null)
                    source.text = v;
                if (on_confirm != null)
                    on_confirm(v);
            });
        }

        // ---------------- 卡图裁切（点击预览图 → 裁切弹框） ----------------

        /// <summary>
        /// 给「卡牌图片」/「面板（全图）图片」两行预览挂上「点击 → 卡图裁切」入口。
        /// 两行的显示区域比例不同，必须分别传，否则裁出来的比例与游戏里对不上：
        /// 卡牌图片 = 卡牌正面（手牌/收藏） 250×350（HandCard.prefab 的 card_image）；
        /// 面板图片 = 战场面板 8.56×8.36（BoardCard.prefab 的 card_sprite）。
        /// slot：0 写 art_path（战场），1 写 art_full_path（卡牌正面）。
        /// </summary>
        private void SetupArtClipRows()
        {
            art_clip_card = EnsureArtClipTarget(art_preview, 1, new Vector2(250f, 350f));        //卡牌正面：竖长
            art_clip_panel = EnsureArtClipTarget(art_full_preview, 0, new Vector2(8.56f, 8.36f)); //战场面板：近方
        }

        /// <summary>slot：0 卡面图（art_path）/ 1 面板图（art_full_path）</summary>
        private ImageClipEditorUI EnsureArtClipTarget(Image target, int slot, Vector2 target_size)
        {
            if (target == null)
                return null;

            // 预览图在「还没选图」时会被 RefreshArt 置为 enabled=false（Image 组件停用即收不到射线），
            // 所以点击入口放在一个盖住它的透明命中层上，保证首次（无图）也能点开裁切弹框
            Transform hit = target.transform.Find("ClipHit");
            if (hit == null)
            {
                GameObject go = new GameObject("ClipHit", typeof(RectTransform), typeof(Image));
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.SetParent(target.transform, false);
                SetStretchRect(rt, 0, 0, 0, 0);
                Image hit_img = go.GetComponent<Image>();
                hit_img.color = new Color(1f, 1f, 1f, 0f);   //全透明仍参与射线检测（UI 射线不看 alpha）
                hit_img.raycastTarget = true;
                hit = rt;
            }

            ImageClipEditorUI editor = hit.GetComponent<ImageClipEditorUI>();
            if (editor == null)
            {
                editor = hit.gameObject.AddComponent<ImageClipEditorUI>();
                editor.onImageChanged += sp => OnArtClipped(slot, sp);
            }
            editor.display = target;             //回写/读取都指向真正的预览图
            editor.currentSprite = target.sprite;
            editor.targetSize = target_size;     //裁切比例按该行的显示区域
            return editor;
        }

        /// <summary>
        /// 裁切结果回写：编码 PNG 落盘到 Workshop/Art（卡图最终以文件形式持久化，存档/重开/打包都还在），
        /// 再写 art_path / art_full_path 并刷新预览。
        /// </summary>
        private void OnArtClipped(int slot, Sprite sp)
        {
            if (card == null || sp == null)
                return;

            try
            {
                Texture2D tex = sp.texture;
                byte[] png = tex != null ? tex.EncodeToPNG() : null;
                if (png == null)
                {
                    SetStatus("裁切结果编码失败");
                    return;
                }

                Directory.CreateDirectory(CardPoolIO.ArtFolder);
                string base_name = string.IsNullOrEmpty(card.id)
                    ? "art_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                    : card.id;
                string fname = slot == 0 ? base_name + ".png" : base_name + "_full.png";
                File.WriteAllBytes(Path.Combine(CardPoolIO.ArtFolder, fname), png);

                if (slot == 0)
                    card.art_path = fname;
                else
                    card.art_full_path = fname;

                CardPoolIO.UpdateCardData(card);   //立即同步运行时卡面，避免选图后卡牌上不更新
                RefreshArt();

                // 落盘后预览图被换成「磁盘加载的新 Sprite」，把入口的当前图同步过去，
                // 避免下次打开被判定为「外部换图」而丢弃裁切状态（这样重开能回到上次位置继续微调）
                if (slot == 0)
                    art_clip_panel.currentSprite = art_full_preview != null ? art_full_preview.sprite : null;   //面板行 = art_path
                else
                    art_clip_card.currentSprite = art_preview != null ? art_preview.sprite : null;              //卡牌行 = art_full_path

                SetStatus((slot == 0 ? "已设置面板(战场)图片: " : "已设置卡牌图片: ") + fname);
            }
            catch (Exception e)
            {
                Debug.LogError("保存裁切图片失败: " + e.Message);
                SetStatus("保存裁切图片失败: " + e.Message);
            }
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
            if (!card_extra_built)   //扩展行（种族/关键词多选）生效时，以多选控件写入的值为准
            {
                card.trait = GetDropdown(dropdown_trait, trait_ids, card.trait);
                if (dropdown_keyword != null)
                {
                    string kw = GetDropdown(dropdown_keyword, keyword_ids, null);
                    card.keywords.Clear();
                    if (!string.IsNullOrEmpty(kw))
                        card.keywords.Add(kw);
                }
            }
            else
            {
                card.EnsureTraits();   //保持旧字段 trait = traits[0]
            }

            if (!card_extra_built && dropdown_type != null && dropdown_type.value >= 0 && dropdown_type.value < TYPE_ENUMS.Length)
                card.type = TYPE_ENUMS[dropdown_type.value];
        }

        private void RefreshArt()
        {
            if (card == null)
                return;
            //「卡牌图片」行 = 卡牌正面（手牌/收藏/回合历史）：art_full_path
            if (art_preview != null)
            {
                art_preview.sprite = CardPoolIO.LoadArt(card.art_full_path);
                art_preview.enabled = art_preview.sprite != null;
            }
            //「面板（全图）图片」行 = 战场面板：art_path
            if (art_full_preview != null)
            {
                art_full_preview.sprite = CardPoolIO.LoadArt(card.art_path);
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
                //「卡牌图片」行 = 卡牌正面（手牌/收藏）→ art_full_path
                string fname = (string.IsNullOrEmpty(card.id) ? "art_" + Guid.NewGuid().ToString("N").Substring(0, 8) : card.id + "_full") + ext;
                Directory.CreateDirectory(CardPoolIO.ArtFolder);
                string dst = Path.Combine(CardPoolIO.ArtFolder, fname);
                File.Copy(src, dst, true);
                card.art_full_path = fname;
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

        /// <summary>选择面板（战场）图片，复制到 ArtFolder 并写入 card.art_path</summary>
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
                //「面板图片」行 = 战场面板 → art_path
                string fname = (string.IsNullOrEmpty(card.id) ? "art_" + Guid.NewGuid().ToString("N").Substring(0, 8) : card.id) + ext;
                Directory.CreateDirectory(CardPoolIO.ArtFolder);
                string dst = Path.Combine(CardPoolIO.ArtFolder, fname);
                File.Copy(src, dst, true);
                card.art_path = fname;
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

        /// <summary>选择音频文件（slot：0打出 1攻击 2死亡 3受伤）：先校验时长（≤10 秒），再复制进 AudioFolder</summary>
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
            string src = files[0];
            SetStatus("正在检查音频长度（上限 " + MAX_AUDIO_SECONDS + " 秒）…");
            CardAudioLoader.ValidateLength(src, MAX_AUDIO_SECONDS, (ok, len) =>
            {
                if (!ok)
                {
                    SetStatus(len > 0f
                        ? string.Format("音频过长（{0:0.0} 秒 > {1:0} 秒），已取消导入", len, MAX_AUDIO_SECONDS)
                        : "无法读取该音频文件（格式不支持或文件损坏），已取消导入");
                    return;
                }
                ImportAudio(src, slot);
            });
        }

        /// <summary>把选中的音频复制到 Workshop/Audio 并写入对应音效槽</summary>
        private void ImportAudio(string src, int slot)
        {
            if (card == null || string.IsNullOrEmpty(src))
                return;
            try
            {
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

        // ---------------- 节点画布 TMP 文本工具 ----------------

        /// <summary>当前界面使用的中文字体：统一由 UIFonts 解析（全项目单一字体管线）。
        /// 供富文本弹框等新界面复用，避免退回无中文字形的默认字体。</summary>
        public TMP_FontAsset NodeFont()
        {
            return UIFonts.ResolveFont();
        }

        /// <summary>字体探测句：唯一定义在 UIFonts（全项目统一），此处保留别名以免改动大量调用点</summary>
        private const string FontProbe = UIFonts.FontProbe;

        /// <summary>实测字体能否渲染探测文本——统一走 UIFonts（动态补字逻辑只在一处）</summary>
        private static bool FontCovers(TMP_FontAsset fa, string text)
        {
            return UIFonts.FontCovers(fa, text);
        }

        /// <summary>为 TMP 文本设置画布字体：字体解析与套用统一在 UIFonts（全项目单一管线）。
        /// probe 参数保留仅为不改动既有调用点——字体能否渲染中文由 UIFonts.ResolveFont 统一验证。</summary>
        private void ApplyNodeFont(TMP_Text tmp, string probe = null)
        {
            UIFonts.ApplyFont(tmp);
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

        /// <summary>取子物体上的 TMP 文本；若场景模板还是旧 UGUI Text，则销毁旧组件换成 TMP
        /// （字号/颜色/对齐迁移，字体换成 ApplyNodeFont）。TMP 环境异常（坏字体资产/设置缺失）时
        /// 整体回退为旧版 Text 并把真实错误打进 Console，保证节点 UI 永不因字体问题崩溃。</summary>
        private TMP_Text EnsureTmpText(Transform root, string path)
        {
            Transform t = root != null ? root.Find(path) : null;
            if (t == null)
                return null;
            Text legacy = t.GetComponent<Text>();
            string s = legacy != null ? legacy.text : "";
            TMP_Text tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                //已是 TMP 也要查字：场景模板可能绑了缺字字体（如 MSYH SDF 缺「的/（」），覆盖不了就换
                if (!FontCovers(tmp.font, string.IsNullOrEmpty(s) ? FontProbe : s + FontProbe))
                    ApplyNodeFont(tmp, s);
                return tmp;
            }
            Color c = legacy != null ? legacy.color : Color.white;
            int size = legacy != null ? legacy.fontSize : 14;
            TextAnchor anchor = legacy != null ? legacy.alignment : TextAnchor.UpperLeft;
            bool raycast = legacy != null && legacy.raycastTarget;
            try
            {
                //必须 DestroyImmediate：Destroy 是帧末销毁，同一帧内旧 Text 还占着 Graphic 位，
                //AddComponent<TextMeshProUGUI> 会因「一个 GameObject 只能有一个 Graphic」被拒绝
                if (legacy != null)
                    DestroyImmediate(legacy);
                tmp = t.gameObject.AddComponent<TextMeshProUGUI>();
                if (tmp == null)
                    throw new System.InvalidOperationException("AddComponent<TextMeshProUGUI> 返回 null（TMP 组件创建失败）");
                ApplyNodeFont(tmp, s);
                tmp.fontSize = size;
                tmp.color = c;
                tmp.alignment = ToTmpAlignment(anchor);
                tmp.raycastTarget = raycast;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                return tmp;
            }
            catch (System.Exception e)
            {
                Debug.LogError("节点 TMP 化失败（" + path + " @ " + root.name + "），该文本已回退为旧版 Text。原因：\n" + e);
                //旧 Text 已标记销毁的话重建等价 Text，界面照常工作
                if (t.GetComponent<Text>() == null)
                {
                    Text back = t.gameObject.AddComponent<Text>();
                    back.font = LegacyUIFont();
                    back.fontSize = size;
                    back.color = Opaque(c);
                    back.alignment = anchor;
                    back.raycastTarget = raycast;
                    back.text = s;
                }
                return null;
            }
        }

        /// <summary>强制文字颜色不透明（保留 RGB，只把 alpha 置 1；规格：节点文字不做半透明淡化）</summary>
        private static Color Opaque(Color c)
        {
            return new Color(c.r, c.g, c.b, 1f);
        }

        // ---------------- 节点库 ----------------

        private void RefreshNodeLib()
        {
            if (node_lib_content == null)
                return;

            //兜底：分类筛选按钮必须存在（TMP 迁移路径没跑到时也能补建）→ 保证"点分类一定能弹框"
            if (txt_filter_select == null)
                ReplaceFilterDropdown();
            HideLegacyFilterWidget();   //再压一次旧分类控件：不允许出现"两个全部"叠加

            //清除旧项（保留模板；「最近使用」栏也是列表内的一行，同样不能被清掉）
            for (int i = node_lib_content.childCount - 1; i >= 0; i--)
            {
                Transform child = node_lib_content.GetChild(i);
                if (child == null || child.gameObject == node_lib_template)
                    continue;
                if (node_recent_root != null && child == node_recent_root)
                    continue;   //「最近使用」栏：已按需求移除（这里先跳过，紧接着由 RefreshRecentBar→RemoveRecentBar 销毁）
                Destroy(child.gameObject);
            }
            RefreshRecentBar();   //已改为空实现：只负责把「最近使用」栏彻底移除

            int shown = 0;
            bool use_dropdown = node_filter_dropdown != null;
            List<NodePreset> source = AllPresets();   //节点库只出 NodeDoc(zmcs) 节点（内置节点已移除）
            for (int i = 0; i < source.Count; i++)
            {
                NodePreset p = source[i];
                if (p.hidden)
                    continue;   //过时/内部/同名重复节点不展示（旧图仍兼容）
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

            TMP_Text title = EnsureTmpText(inst.transform, "TitleText");
            if (title != null)
                title.text = preset.title;

            //分类色条 + 图标字符（规格第6.4节：找节点靠颜色+图标扫）
            Image cat = inst.transform.Find("CatBar")?.GetComponent<Image>();
            if (cat != null)
                cat.color = CategoryColor(preset.type);
            TMP_Text icon = EnsureTmpText(inst.transform, "IconText");
            if (icon != null)
            {
                icon.text = CategoryIcon(preset.type);
                icon.color = CategoryColor(preset.type);
            }

            //端口概要（▸输出 ◂输入）；未接入执行的 zmcs 节点：灰显 + 「未接入」标记，点击不生成只提示
            TMP_Text desc = EnsureTmpText(inst.transform, "DescText");
            if (desc != null)
                desc.text = PortSummary(preset);
            if (!preset.supported)
            {
                Color gray = new Color(0.55f, 0.55f, 0.58f, 1f);   //灰显但字色不透明
                if (title != null)
                    title.text = preset.title + "（未接入）";
                if (title != null) title.color = gray;
                if (desc != null) desc.color = gray;
                if (icon != null) icon.color = gray;
            }

            //收藏星标（右上角，点击切换收藏状态并持久化）
            bool is_fav = favs.Contains(preset.action);
            TMP_Text star = EnsureTmpText(inst.transform, "FavBtn/Text");
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

            //主按钮：已接入的拖入画布；未接入的点击只提示
            Button btn = inst.GetComponent<Button>();
            if (btn == null)
                btn = inst.AddComponent<Button>();
            if (preset.supported)
                btn.onClick.AddListener(() => AddNodeFromPreset(preset));
            else
            {
                string t = preset.title;
                btn.onClick.AddListener(() => SetStatus("「" + t + "」尚未接入执行层，暂不能加入规则图（已接入节点不带此标记）"));
            }
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

        /// <summary>刷新「最近使用」栏：该栏已按需求整体移除（见 RemoveRecentBar），本方法保留为空实现以兼容旧调用点。</summary>
        private void RefreshRecentBar()
        {
            RemoveRecentBar();
        }

        /// <summary>彻底移除「最近使用」栏（界面上不应再出现任何该栏元素）。
        /// 该栏原为节点库顶部的一行"最近使用小按钮"：与节点列表项样式/层级不一致，在列表容器内外都踩过坑
        /// （位置错位、溢出重叠），按需求整体删除：运行时销毁其物体并置空引用，之后不再产生任何界面元素。
        /// 最近使用数据仍会记录（PlayerPrefs），若将来要恢复该功能，把本方法改成空实现即可。</summary>
        private void RemoveRecentBar()
        {
            if (node_recent_root == null)
                return;
            node_recent_root.gameObject.SetActive(false);
            Destroy(node_recent_root.gameObject);
            node_recent_root = null;
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
        private TMP_Text hover_tooltip_text;        // 悬停细目提示文本
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

            TMP_Text txt = CreateStretchTextChild(go.transform, 15, Color.white);   //Text 放独立子对象（Graphic 唯一限制）
            if (txt != null)
            {
                txt.alignment = TextAlignmentOptions.Center;
                txt.text = "!";
            }
            return go;
        }

        /// <summary>在指定父级下创建铺满父级的 TMP 文本子对象（Unity 限制一个 GameObject 只能有一个 Graphic，
        /// 因此背景 Image 与文字 Text 必须分属父子两个对象）。TMP 环境异常时返回 null（调用方需判空）。</summary>
        private TMP_Text CreateStretchTextChild(Transform parent, int font_size, Color color)
        {
            GameObject tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(parent, false);
            RectTransform trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            try
            {
                TextMeshProUGUI txt = tgo.AddComponent<TextMeshProUGUI>();
                ApplyNodeFont(txt);
                txt.fontSize = font_size;
                txt.color = color;
                txt.raycastTarget = false;
                return txt;
            }
            catch (System.Exception e)
            {
                Debug.LogError("节点 TMP 文本创建失败，已跳过（" + parent.name + "）。原因：\n" + e);
                Destroy(tgo);
                return null;
            }
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

            //约束：每张效果图只允许 1 个入口/触发节点（多入口 → 编译出多个能力、图上入口语义互相打架）
            string limit_err;
            if (!CanAddEntryTriggerNode(preset.category, out limit_err))
            {
                SetStatus(limit_err);
                return;
            }

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

        /// <summary>是否是"带目标槽的图入口"（主动效果入口 / 起动式效果入口）：
        /// 决定「＋ 新增目标」按钮、目标槽字段 synthesis、目标去重字段是否可用。</summary>
        private static bool IsTargetSlotEntry(GraphNode node)
        {
            return node != null && (node.action == "ActivateEffect" || node.action == "ActivateAbility");
        }

        /// <summary>是否是"图的入口/触发节点"（每张效果图只允许 1 个）：
        /// ① zmcs 五类效果入口（入口：主动/起动式/光环/被动/事件）
        /// ② 图事件入口（事件：打出时/死亡时/回合开始…）
        /// ③ 增益触发入口（增益触发：添加增益后/每回合开始…）
        /// 「按钮」入口不计入：全局按钮图是"一图多按钮"共享设计（每个按钮一个入口）。
        /// 注：CAT_EVENT 同时被取值/条件节点（EFCardOwner 等）使用，故必须同时要求 type==Event。</summary>
        private static bool IsEntryOrTriggerNode(GraphNode node)
        {
            return node != null && node.type == GraphNodeType.Event && IsEntryTriggerCategory(node.category);
        }

        /// <summary>入口/触发分类判定（预设与节点通用）</summary>
        private static bool IsEntryTriggerCategory(string category)
        {
            return category == CAT_ENTRY || category == CAT_EVENT || category == CAT_BUFF_TRIGGER;
        }

        /// <summary>取图上的入口/触发节点（无 → null）</summary>
        private static GraphNode FindEntryTriggerNode(GraphData g)
        {
            if (g == null || g.nodes == null)
                return null;
            foreach (GraphNode n in g.nodes)
            {
                if (IsEntryOrTriggerNode(n))
                    return n;
            }
            return null;
        }

        /// <summary>约束校验：每张「卡牌效果图」只允许 1 个入口/触发节点。非入口分类恒可通过；
        /// 返回 false 时 error 给出可操作提示（供加节点/粘贴前拦截）。
        /// 只在卡模式生效：关键词/增益/按钮三种模式的图不是"效果图"语义
        /// （增益图可同时挂 每回合开始 + 移除后 等触发，按钮图本就是"一图多按钮"）。</summary>
        private bool CanAddEntryTriggerNode(string category, out string error)
        {
            error = null;
            if (card == null)
                return true;
            if (!IsEntryTriggerCategory(category))
                return true;
            GraphNode exist = FindEntryTriggerNode(graph);
            if (exist == null)
                return true;
            error = "本效果图已有入口/触发节点「" + (string.IsNullOrEmpty(exist.title) ? exist.action : exist.title)
                + "」：每张效果图只允许 1 个入口。先删除它，或点左上角「+ 新效果」新建一张效果图再配第 2 个入口。";
            return false;
        }

        /// <summary>打开/切换效果图时的校验（仅卡模式）：历史图若已有多个入口/触发节点 → 只在状态栏与 Console 提示，不自动删数据</summary>
        private void ValidateSingleEntryTrigger()
        {
            if (card == null)
                return;
            if (graph == null || graph.nodes == null || graph.nodes.Count == 0)
                return;
            List<GraphNode> entries = new List<GraphNode>();
            foreach (GraphNode n in graph.nodes)
            {
                if (IsEntryOrTriggerNode(n))
                    entries.Add(n);
            }
            if (entries.Count <= 1)
                return;
            string names = "";
            foreach (GraphNode n in entries)
                names += (names.Length > 0 ? "、" : "") + (string.IsNullOrEmpty(n.title) ? n.action : n.title);
            string msg = "本效果图有 " + entries.Count + " 个入口/触发节点（" + names
                + "）：每张效果图只允许 1 个（多入口会编译成多个能力并互相抢触发）。"
                + "请把多余入口移到「+ 新效果」新建的效果图（工具不自动删节点）。";
            Debug.LogWarning("[规则图] " + msg);
            SetStatus("⚠ " + msg);
        }

        /// <summary>主动效果入口的目标槽迁移：旧图用无编号字段/引脚（target_type / targetCondition），
        /// 统一改成编号形式（target_type1 / targetCondition1），并把引用旧引脚 id 的连线改到新 id。
        /// 只在"存在旧的无编号 target_type 且还没有 target_type1"时执行，幂等。</summary>
        /// <summary>运算节点（112004 整数运算 / 112005 逻辑运算）输入槽自愈（幂等，随打开图执行）：
        /// ① 每个槽都补齐「端口 + **同名字段**」（旧图的 variadic 参数口 → 单槽输入端口；缺字段 → 补默认值 0/false）；
        /// ② 同一槽出现多个名字（如历史脏数据 arg 与 arg1 并存）时统一到端口名：
        ///    冗余字段删除、冗余端口的**连线改接到保留端口后再删除** —— 避免"手填框与端口错位"和"没有端口的空行"；
        /// ③ 值为空串则规整为默认值（0 / false）。</summary>
        private static void MigrateParamSlots(GraphData graph, GraphNode node)
        {
            string base_name = ParamSlotBaseName(node);
            if (base_name == null || node == null)
                return;
            List<int> slots = ParamSlotsOf(node, base_name);
            if (slots.Count == 0)
                return;
            bool is_int = base_name == "arg";
            if (node.pins == null)
                node.pins = new List<GraphPin>();
            if (node.fields == null)
                node.fields = new List<FieldCustomData>();

            foreach (int s in slots)
            {
                //该槽的端口名（端口是连线的锚点 → 以端口名作为规范名）
                string canon = null;
                foreach (GraphPin p in node.pins)
                {
                    if (p == null || p.is_output || SlotNumberOf(p.name, base_name) != s)
                        continue;
                    p.is_array = false;          //旧 variadic 参数口 → 单槽
                    p.display_name = "值";
                    if (canon == null)
                        canon = p.name;
                }
                if (canon == null)
                {
                    foreach (FieldCustomData f in node.fields)
                    {
                        if (f != null && SlotNumberOf(f.name, base_name) == s)
                        {
                            canon = f.name;
                            break;
                        }
                    }
                }
                if (canon == null)
                    canon = s == 1 ? base_name : base_name + s;

                //字段：统一到 canon（值优先取 canon，其次取同槽其它变体），并删除该槽的冗余字段
                string val = null;
                foreach (FieldCustomData f in node.fields)
                {
                    if (f != null && f.name == canon)
                        val = f.value;
                }
                if (string.IsNullOrEmpty(val))
                {
                    foreach (FieldCustomData f in node.fields)
                    {
                        if (f != null && SlotNumberOf(f.name, base_name) == s && !string.IsNullOrEmpty(f.value))
                        {
                            val = f.value;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(val))
                    val = is_int ? "0" : "false";
                node.fields.RemoveAll(f => f != null && f.name != canon && SlotNumberOf(f.name, base_name) == s);
                FieldCustomData keep = null;
                foreach (FieldCustomData f in node.fields)
                {
                    if (f != null && f.name == canon)
                        keep = f;
                }
                if (keep == null)
                    node.fields.Add(new FieldCustomData { name = canon, value = val });
                else
                    keep.value = val;

                //端口：canon 不存在则补；同槽的其它端口先把连线改接到 canon 再删除
                GraphPin canon_pin = null;
                foreach (GraphPin p in node.pins)
                {
                    if (p != null && p.name == canon)
                        canon_pin = p;
                }
                if (canon_pin == null)
                {
                    canon_pin = new GraphPin
                    {
                        id = node.id + "_" + canon,
                        name = canon,
                        display_name = "值",
                        type = is_int ? NodeValueType.Int32 : NodeValueType.Boolean,
                        is_output = false,
                    };
                    node.pins.Add(canon_pin);
                }
                for (int i = node.pins.Count - 1; i >= 0; i--)
                {
                    GraphPin p = node.pins[i];
                    if (p == null || p.is_output || p.name == canon || SlotNumberOf(p.name, base_name) != s)
                        continue;
                    if (graph != null && graph.links != null)
                    {
                        foreach (GraphLink l in graph.links)
                        {
                            if (l != null && l.to_node == node.id && l.to_pin == p.id)
                                l.to_pin = canon_pin.id;   //连线改接到保留的端口
                        }
                    }
                    Debug.Log("[规则图] 运算节点输入槽自愈：合并同名端口 " + p.name + " → " + canon);
                    node.pins.RemoveAt(i);
                }
            }
        }

        private static void MigrateEntryTargetSlots(GraphData graph, GraphNode node)
        {
            MigrateParamSlots(graph, node);   //运算节点输入槽自愈（与目标槽迁移同一批执行）
            if (node == null || node.fields == null || !IsTargetSlotEntry(node))
                return;
            if (HasField(node, "target_type1") || !HasField(node, "target_type"))
                return;     //已是编号形式，或本就没有目标槽（新图默认无目标）

            RenameNodeField(node, "target_type", "target_type1");
            RenameNodeField(node, "target_error", "target_error1");
            RenameNodeField(node, "target_side", "target_side1");     //归属已不在 UI 显示；保留数据供编译层沿用旧行为

            if (node.pins == null)
                return;
            string old_id = node.id + "_targetCondition";
            string new_id = node.id + "_targetCondition1";
            foreach (GraphPin p in node.pins)
            {
                if (p != null && p.name == "targetCondition")
                {
                    p.name = "targetCondition1";
                    p.display_name = "目标1条件";
                    p.id = new_id;
                }
            }
            if (graph != null && graph.links != null)
            {
                foreach (GraphLink l in graph.links)
                {
                    if (l == null)
                        continue;
                    if (l.from_node == node.id && l.from_pin == old_id)
                        l.from_pin = new_id;
                    if (l.to_node == node.id && l.to_pin == old_id)
                        l.to_pin = new_id;
                }
            }
            //槽1 的「目标卡牌1」输出口（新预设不再默认提供，迁移时补上）
            if (node.pins.FindIndex(p => p != null && p.name == "targetCard1") < 0)
            {
                node.pins.Add(new GraphPin
                {
                    id = node.id + "_targetCard1",
                    name = "targetCard1",
                    display_name = "目标卡牌1",
                    type = NodeValueType.Card,
                    is_output = true,
                });
            }
        }

        /// <summary>节点字段改名（目标名已存在或源字段不存在则不动）</summary>
        private static void RenameNodeField(GraphNode node, string from, string to)
        {
            if (node == null || node.fields == null || HasField(node, to))
                return;
            foreach (FieldCustomData f in node.fields)
            {
                if (f != null && f.name == from)
                {
                    f.name = to;
                    return;
                }
            }
        }

        /// <summary>旧图数据迁移：①旧节点引脚无类型（type=None）→ 按预设整体重建（id 命名规则不变，连线保持有效）；
        /// ②节点分类规则升级后被改型的（如 202041 由取值改动作）→ 修正 node.type；
        /// ③预设后来新增的引脚（如 主动效果入口的 目标1条件）→ 增量补进节点，已有引脚与连线不动。</summary>
        private static void MigratePins(GraphNode node)
        {
            if (node == null || node.pins == null)
                return;
            NodePreset preset = FindPreset(node.type, node.action);
            if (preset == null)
            {
                //按动作跨类型找（分类规则升级：如 202041 有事件参数输出 → 由取值改判为动作）
                foreach (NodePreset p in AllPresets())
                {
                    if (p.action == node.action && p.type != node.type)
                    {
                        preset = p;
                        node.type = p.type;
                        break;
                    }
                }
            }
            if (preset == null)
                return;

            //字段去重：历史脏数据（旧版本重复追加同名字段）会让内联控件重复显示，同名只保留第一条
            if (node.fields != null)
            {
                HashSet<string> seen_fields = new HashSet<string>();
                node.fields.RemoveAll(f => f == null || string.IsNullOrEmpty(f.name) || !seen_fields.Add(f.name));
            }

            //预设后来新增的字段（如 标签列表/目标错误提示/动态目标槽字段）→ 增量补默认值，旧图立即渲染出控件
            if (preset.fields != null && preset.fields.Count > 0)
            {
                if (node.fields == null)
                    node.fields = new List<FieldCustomData>();
                foreach (FieldDef fd in preset.fields)
                {
                    if (fd != null && !string.IsNullOrEmpty(fd.name) && !HasField(node, fd.name))
                        node.fields.Add(new FieldCustomData { name = fd.name, value = fd.def ?? "" });
                }
            }

            bool need_rebuild = node.pins.Count == 0;
            foreach (GraphPin p in node.pins)
            {
                if (p.type == NodeValueType.None)
                {
                    need_rebuild = true;
                    break;
                }
            }
            if (need_rebuild)
            {
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
                return;
            }
            //增量同步：预设里新增的引脚补进来；已有引脚按预设修正类型（如 ActionNode 分支口 → 执行流），
            //引脚 id 命名规则不变，旧连线不受影响
            foreach (PinDef pd in preset.pins)
            {
                GraphPin found = null;
                foreach (GraphPin p in node.pins)
                {
                    if (p.name == pd.name)
                    {
                        found = p;
                        break;
                    }
                }
                if (found == null)
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
                else
                {
                    found.type = pd.type;
                    found.is_output = pd.is_output;
                    found.is_array = pd.is_array;
                    found.display_name = pd.display_name;
                }
            }
            //按预设顺序重排（如执行 in/out 移到最上面）：连线按引脚 id 引用，顺序变化不影响已有连线；
            //预设里没有的引脚（历史遗留）保持在末尾
            int sorted = 0;
            foreach (PinDef pd in preset.pins)
            {
                int idx = node.pins.FindIndex(x => x.name == pd.name);
                if (idx >= 0 && idx != sorted)
                {
                    GraphPin move = node.pins[idx];
                    node.pins.RemoveAt(idx);
                    node.pins.Insert(sorted, move);
                }
                sorted++;
            }
        }

        /// <summary>给节点加一圈分类色边框（替代左上角色条）：上下左右四条 3px 边条，随节点尺寸自适应</summary>
        private static void ApplyNodeRing(RectTransform node_rect, Color color)
        {
            if (node_rect == null)
                return;
            MakeRingEdge(node_rect, "RingL", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(1.5f, 0), color);
            MakeRingEdge(node_rect, "RingR", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(0, 0), new Vector2(1.5f, 0), color);
            MakeRingEdge(node_rect, "RingT", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(0, 1.5f), color);
            MakeRingEdge(node_rect, "RingB", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(0, 1.5f), color);
        }

        private static void MakeRingEdge(RectTransform parent, string name, Vector2 amin, Vector2 amax,
            Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            Transform t = parent.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (t == null)
                go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = amin;
            rt.anchorMax = amax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            go.transform.SetAsLastSibling();   //显示在节点内容之上
        }

        // ---------------- 节点帮助「?」按钮 + 使用方法弹窗 ----------------

        private GameObject node_help_popup;
        private TMP_Text node_help_title;
        private TMP_Text node_help_body;

        /// <summary>节点头部「?」按钮（右上角按钮组最左侧）：点击弹出该节点使用方法</summary>
        private void EnsureNodeHelpButton(Transform header, GraphNode node)
        {
            if (header == null || node == null)
                return;
            Transform t = header.Find("BtnHelp");
            GameObject go = t != null ? t.gameObject : new GameObject("BtnHelp", typeof(RectTransform), typeof(Image), typeof(Button));
            if (t == null)
                go.transform.SetParent(header, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-53f, -7f);
            rt.sizeDelta = new Vector2(18f, 18f);
            Image img = go.GetComponent<Image>();
            img.color = new Color(0.28f, 0.55f, 0.85f, 0.9f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.RemoveAllListeners();
            string nid = node.id;
            btn.onClick.AddListener(() => ShowNodeHelp(nid));

            TMP_Text txt = go.GetComponentInChildren<TMP_Text>(true);
            if (txt == null)
            {
                txt = MakeText("Text", rt, "?", 14, TextAnchor.MiddleCenter, TabFont());
                SetStretchRect(txt.rectTransform, 0, 0, 0, 0);
            }
            txt.text = "?";
            txt.fontSize = 14;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
        }

        // ---------------- 节点特效按钮（动作/事件节点，位于「?」左侧） ----------------

        private static readonly Color VFX_BTN_EMPTY = new Color(0.42f, 0.36f, 0.30f, 0.85f);   //未配置：灰/空态
        private static readonly Color VFX_BTN_SET = new Color(1f, 0.82f, 0.32f, 0.95f);      //已配置：金色高亮

        /// <summary>节点右上角特效按钮：仅动作/事件节点显示（函数=Value 节点隐藏），样式与「?」一致（18×18、同锚点体系），
        /// 位置在「?」左侧（? 在 -53，故取 -76）。未配置=灰空态，已配置=金色实心星。</summary>
        private void EnsureNodeVfxButton(Transform header, GraphNode node)
        {
            if (header == null || node == null)
                return;

            bool supported = node.type == GraphNodeType.Action || node.type == GraphNodeType.Event;
            Transform t = header.Find("BtnVfx");
            if (!supported)
            {
                if (t != null)
                    t.gameObject.SetActive(false);   //函数节点不显示
                return;
            }

            GameObject go = t != null ? t.gameObject
                : new GameObject("BtnVfx", typeof(RectTransform), typeof(Image), typeof(Button));
            if (t == null)
                go.transform.SetParent(header, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-76f, -7f);   //「?」(-53) 左侧紧邻
            rt.sizeDelta = new Vector2(18f, 18f);

            bool configured = node.vfx != null && node.vfx.HasFrames;
            Image img = go.GetComponent<Image>();
            img.color = configured ? VFX_BTN_SET : VFX_BTN_EMPTY;
            img.raycastTarget = true;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.RemoveAllListeners();
            string nid = node.id;
            btn.onClick.AddListener(() => OpenVfxEditor(nid));

            TMP_Text txt = go.GetComponentInChildren<TMP_Text>(true);
            if (txt == null)
            {
                txt = MakeText("Text", rt, "✦", 13, TextAnchor.MiddleCenter, TabFont());
                SetStretchRect(txt.rectTransform, 0, 0, 0, 0);
            }
            txt.text = configured ? "✦" : "·";     //已配置=实心星，未配置=淡点
            txt.fontSize = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = configured ? new Color(0.25f, 0.16f, 0.02f, 1f) : new Color(1f, 1f, 1f, 0.75f);
            txt.raycastTarget = false;

            go.transform.SetAsLastSibling();       //保证可点（不被标题文本遮挡）
        }

        /// <summary>节点特效配置变化后刷新按钮状态（免整图重建）</summary>
        private void RefreshNodeVfxButton(string node_id)
        {
            GraphNode node = graph != null ? graph.GetNode(node_id) : null;
            if (node == null)
                return;
            if (node_rows == null || !node_rows.TryGetValue(node_id, out RectTransform rect) || rect == null)
                return;
            EnsureNodeVfxButton(rect.Find("Header"), node);
        }

        /// <summary>打开该节点的特效编辑器：回显当前配置，确定后写回 node.vfx（随图 JSON 落盘）</summary>
        public void OpenVfxEditor(string node_id)
        {
            GraphNode node = graph != null ? graph.GetNode(node_id) : null;
            if (node == null)
                return;
            if (node.type != GraphNodeType.Action && node.type != GraphNodeType.Event)
            {
                SetStatus("该节点不支持特效（仅动作/事件节点可配）");
                return;
            }

            VFXEditorPopup popup = VFXEditorPopup.Create(transform);
            popup.Open(node.vfx, node.title, cfg =>
            {
                //没选帧的配置视为"未配置"，保持图数据干净、运行时零开销
                node.vfx = (cfg != null && cfg.HasFrames) ? cfg : null;
                RefreshNodeVfxButton(node.id);
                SetStatus(node.vfx != null
                    ? "已设置节点特效：" + node.vfx.Summary() + "（记得保存卡牌/卡池以落盘）"
                    : "已清除该节点特效（记得保存卡牌/卡池以落盘）");
            });
        }

        private void ShowNodeHelp(string node_id)
        {
            GraphNode node = graph != null ? graph.GetNode(node_id) : null;
            if (node == null)
                return;
            EnsureNodeHelpPopup();
            string help = NodeHelp(node);
            if (string.IsNullOrEmpty(help))
                help = "该节点暂无使用说明。";
            node_help_title.text = string.IsNullOrEmpty(node.title) ? node.action : node.title;
            node_help_body.text = help;
            node_help_popup.SetActive(true);
            node_help_popup.transform.SetAsLastSibling();
        }

        private void CloseNodeHelpPopup()
        {
            if (node_help_popup != null)
                node_help_popup.SetActive(false);
        }

        private void EnsureNodeHelpPopup()
        {
            if (node_help_popup != null)
                return;
            Font font = TabFont();

            node_help_popup = new GameObject("NodeHelpPopup", typeof(RectTransform));
            RectTransform root = node_help_popup.GetComponent<RectTransform>();
            root.SetParent(transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            Image back = node_help_popup.AddComponent<Image>();
            back.color = new Color(0, 0, 0, 0.55f);
            Button bbtn = node_help_popup.AddComponent<Button>();
            bbtn.targetGraphic = back;
            bbtn.onClick.AddListener(CloseNodeHelpPopup);

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            RectTransform prt = panel.GetComponent<RectTransform>();
            prt.SetParent(root, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(560, 380);
            Image pimg = panel.AddComponent<Image>();
            pimg.color = new Color(0.12f, 0.12f, 0.15f, 1f);
            Button pbtn = panel.AddComponent<Button>();   //吞掉点击，避免穿透到遮罩
            pbtn.targetGraphic = pimg;

            node_help_title = MakeText("Title", prt, "节点说明", 24, TextAnchor.MiddleLeft, font);
            RectTransform trt = node_help_title.rectTransform;
            trt.anchorMin = new Vector2(0, 1);
            trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0.5f, 1);
            trt.anchoredPosition = new Vector2(0, -10);
            trt.sizeDelta = new Vector2(-70, 34);

            RectTransform close = MakeButton("Close", prt, "×", font, CloseNodeHelpPopup);
            close.anchorMin = new Vector2(1, 1);
            close.anchorMax = new Vector2(1, 1);
            close.pivot = new Vector2(1, 1);
            close.anchoredPosition = new Vector2(-8, -8);
            close.sizeDelta = new Vector2(34, 34);

            node_help_body = MakeText("Body", prt, "", 20, TextAnchor.UpperLeft, font);
            RectTransform brt = node_help_body.rectTransform;
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(1, 1);
            brt.offsetMin = new Vector2(20, 20);
            brt.offsetMax = new Vector2(-20, -52);
            node_help_body.enableWordWrapping = true;
            node_help_body.overflowMode = TextOverflowModes.Overflow;
            node_help_body.lineSpacing = 1.15f;

            node_help_popup.SetActive(false);
        }

        /// <summary>节点画布底部说明：显示该节点的介绍/使用方法（预设 desc），无则留空（高度 0）。
        /// 旧图内已有节点也按 action 反查预设拿说明。</summary>
        private static string NodeHelp(GraphNode node)
        {
            if (node == null)
                return "";
            NodePreset p = FindPreset(node.type, node.action);
            if (p != null && !string.IsNullOrEmpty(p.desc))
                return p.desc;
            return "";
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
            CloseNodeHelpPopup();   //重建前关闭节点说明弹窗

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

            //Header：分类色条 + 类型标签 + 标题（画布节点文字统一 TMP、颜色不透明）
            Transform header = inst.transform.Find("Header");
            if (header != null)
            {
                //分类色改为「整节点一圈同色边框」：隐藏原左上角色条
                Image cat = header.Find("CatBar")?.GetComponent<Image>();
                if (cat != null)
                    cat.gameObject.SetActive(false);
                ApplyNodeRing(rect, CategoryColor(node.type));
                TMP_Text type = EnsureTmpText(header, "TypeText");
                if (type != null)
                {
                    //zmcs(NodeDoc) 节点头部显示其主题分类（如"卡牌"），内置节点沿用原四类名
                    string label = string.IsNullOrEmpty(node.category) ? NodeTypeLabel(node.type) : node.category;
                    type.text = CategoryIcon(node.type) + " " + label;   //规格第6.4节：颜色+图标辅助扫视
                    type.color = Opaque(CategoryColor(node.type));
                }
            }
            TMP_Text title = EnsureTmpText(inst.transform, "Header/TitleText");
            if (title != null)
            {
                title.text = node.title;
                title.color = Opaque(title.color);
            }
            //底部说明行已移除：节点介绍/使用方法改为右上角「?」按钮弹窗展示
            TMP_Text desc = EnsureTmpText(inst.transform, "DescText");
            if (desc != null)
                desc.gameObject.SetActive(false);

            //节点宽/高自适应：宽度取「标题 TMP 实测宽度(preferredWidth)+边距」与端口标签估算的最宽者
            //（TMP 实测精确到字形，中文/英文混排不再偏窄）；高度按 Header+端口行数+说明区（规格第3节），
            //输出口 x 与端口行 y 依赖该尺寸，须先于 CreatePinUI 设置
            float title_w = 0f;
            if (title != null && !string.IsNullOrEmpty(title.text))
            {
                try { title_w = title.preferredWidth + 104f; }   //104 = 左边距12 + 右侧按钮区90 + 2
                catch (System.Exception) { title_w = 0f; }   //字体/材质异常时退回估算宽度，不让排版崩掉整个画布
            }
            float node_w = Mathf.Max(EstimateNodeWidth(node), title_w);
            rect.sizeDelta = new Vector2(Mathf.Clamp(node_w, 176f, 720f), EstimateNodeHeight(node));
            rect.localScale = new Vector3(NodeScale, NodeScale, 1f);   //整体缩小（面积≈原来的 1/4）

            //收起/删除按钮（每个节点自带，规格第4节）
            string nid = node.id;
            Button btn_del = inst.transform.Find("Header/BtnDel")?.GetComponent<Button>();
            if (btn_del != null)
            {
                btn_del.onClick.AddListener(() => OnDeleteNodeId(nid));
                SetButtonText(btn_del.transform, "×");   //模板红叉字符统一为 ×（✔/✕ 等装饰字符部分字体缺字形会显示空白）
                //锚点/pivot 显式重设 + 内缩：红叉绝不超出节点外框（场景物体可能与源码不一致）
                //红叉放到原「–」缩小的位置
                RectTransform dr = btn_del.GetComponent<RectTransform>();
                dr.anchorMin = new Vector2(1, 1);
                dr.anchorMax = new Vector2(1, 1);
                dr.pivot = new Vector2(1, 1);
                dr.anchoredPosition = new Vector2(-30f, -7f);
                dr.sizeDelta = new Vector2(18f, 18f);
            }
            //取消右上角「–」缩小按钮（红叉已占用其位置）
            Button btn_min = inst.transform.Find("Header/BtnMin")?.GetComponent<Button>();
            if (btn_min != null)
                btn_min.gameObject.SetActive(false);
            //「?」帮助按钮：放在右上角按钮组最左侧，点击弹出该节点的使用方法
            EnsureNodeHelpButton(inst.transform.Find("Header"), node);
            //特效按钮：动作/事件节点才有，位于「?」左侧（函数节点自动隐藏）
            EnsureNodeVfxButton(inst.transform.Find("Header"), node);

            //引脚
            Transform pins_root = inst.transform.Find("Pins");
            if (pins_root != null)
            {
                foreach (GraphPin pin in node.pins)
                    CreatePinUI(node, pin, pins_root, rect);
            }

            //节点内联参数：把预设字段直接画在节点底部，方便在画布上直接改（对齐醉梦传说）
            CreateNodeInlineFields(inst.transform, node, rect);

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
                for (int ci = 0; ci < inst.transform.childCount; ci++)
                {
                    Transform ch = inst.transform.GetChild(ci);
                    if (ch != null && ch.name.StartsWith("InlineField_"))
                        ch.gameObject.SetActive(false);   //收起：内联参数隐藏
                }
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

        // ---------------- 节点内联参数（把预设字段直接画在节点上，对齐醉梦传说） ----------------

        /// <summary>把节点预设字段直接画在节点底部（每行 26px），方便在画布上直接改参数</summary>
        private void CreateNodeInlineFields(Transform node_root, GraphNode node, RectTransform node_rect)
        {
            List<FieldDef> fields = NodeRenderFields(node);
            if (fields.Count == 0)
                return;
            int rows = fields.Count;
            //输入口占用左列前若干行：字段紧接其后，同属左列、左对齐
            int in_c = 0;
            if (node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                    if (!p.is_output) in_c++;
            }
            float h = node_rect != null ? node_rect.sizeDelta.y : 0f;
            float node_w = node_rect != null ? node_rect.sizeDelta.x : 440f;

            //右侧输出口占位：圆点内缩12 + 输出口名标签宽度 + 间距 → 字段控件不得越过这条线（否则盖住输出口，线都连不了）
            float out_need = 0f;
            if (node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    if (!p.is_output)
                        continue;
                    string nm = string.IsNullOrEmpty(p.display_name) ? p.name : p.display_name;
                    out_need = Mathf.Max(out_need, EstimateTextWidth(nm, 12) + 18f);
                }
            }
            float right_reserve = out_need > 0f ? out_need + 20f : 10f;

            int next_row = in_c;   //无同名输入口的字段：接在输入口下方依次排
            for (int i = 0; i < rows; i++)
            {
                FieldDef fd = fields[i];
                if (fd == null || string.IsNullOrEmpty(fd.name))
                    continue;
                GraphPin pin = FindInputPin(node, fd.name);
                int row;
                float x0 = 20f;
                bool with_label = true;
                if (pin != null)
                {
                    //与输入口同名：常量控件直接画在该端口行右侧（端口名由端口标签显示）
                    if (graph != null && graph.GetIncomingLink(node.id, pin.id) != null)
                        continue;   //有连线：值由连线提供，隐藏常量控件
                    row = InputPinRow(node, pin.name);
                    x0 = PinFieldX(node, pin.name, 12f);
                    with_label = false;
                }
                else
                {
                    row = next_row;
                    next_row++;
                }
                //行中心与端口行一致：y = H-45-row*24；字段行高 20，故行底 = 行中心-10
                float y = h - 45f - row * 24f - 10f;
                CreateInlineFieldRow(node_root, node, fd, GetFieldValue(node, fd.name, fd.def ?? ""), y, node_w, right_reserve, x0, with_label);
            }

            //多目标：入口节点末尾追加「＋ 新增目标」按钮行（槽号只增不复用，中间删槽不重排其余编号）
            if (IsTargetSlotEntry(node))
            {
                float ay = h - 45f - next_row * 24f - 10f;
                CreateAddTargetSlotButton(node_root, node, ay);
            }

            //运算节点（112004 整数运算 / 112005 逻辑运算）：每个输入槽行右端挂「×」删除 + 末尾「＋ 新增输入」
            if (IsParamSlotNode(node))
            {
                string pbase = ParamSlotBaseName(node);
                List<int> pslots = ParamSlotsOf(node, pbase);
                //至少保留 1 个输入：只剩 1 个时不画「×」（删除按钮只在 >1 时出现）
                if (pslots.Count > 1)
                {
                    foreach (int s in pslots)
                    {
                        string nm = SlotParamNameOf(node, pbase, s);
                        GraphPin pin = FindInputPin(node, nm);
                        int prow = pin != null ? InputPinRow(node, pin.name) : s - 1;   //与该端口同一行
                        float row_center = h - 45f - prow * 24f;
                        CreateRemoveParamSlotButton(node_root, node, s, node_w, right_reserve, row_center);
                    }
                }
                float pay = h - 45f - next_row * 24f - 10f;
                CreateAddParamSlotButton(node_root, node, pay);
            }

            RefreshPinFieldVisibility();
        }

        // ---------------- 多目标（主动效果入口）：目标槽增删 ----------------

        /// <summary>节点实际渲染的字段序列：按 node.fields 顺序（预设字段 + 动态目标槽字段）逐项取定义；
        /// 未知/无定义的字段不渲染；同名重复字段只渲染一次（防历史脏数据重复显示）；
        /// 个别字段按条件隐藏（见 IsFieldVisible）。</summary>
        private static List<FieldDef> NodeRenderFields(GraphNode node)
        {
            List<FieldDef> list = new List<FieldDef>();
            if (node == null || node.fields == null)
                return list;
            HashSet<string> seen = new HashSet<string>();
            foreach (FieldCustomData f in node.fields)
            {
                if (f == null || string.IsNullOrEmpty(f.name) || !seen.Add(f.name))
                    continue;
                if (!IsFieldVisible(node, f.name))
                    continue;
                FieldDef fd = FindFieldDef(node, f.name);
                if (fd != null)
                    list.Add(fd);
            }
            return list;
        }

        /// <summary>字段是否渲染（条件字段统一在这）：
        /// 「目标去重」仅在存在 ≥2 个目标槽时才有意义，单目标时隐藏（运行时同样只在 ≥2 槽时生效）。</summary>
        private static bool IsFieldVisible(GraphNode node, string name)
        {
            if (name == "unique_targets" && IsTargetSlotEntry(node))
                return TargetSlots(node).Count >= 2;
            return true;
        }

        /// <summary>字段名 → FieldDef：预设优先；目标槽字段（target_type / target_error 及其编号形式）按命名约定合成。
        /// 注意：target_side 不在这里合成——入口 UI 已移除「目标归属」，归属改由「目标N条件」链里的「卡牌归属」节点判断
        /// （旧图里若残留该字段仍会被编译层读取，保证老卡行为不变）。</summary>
        private static FieldDef FindFieldDef(GraphNode node, string name)
        {
            NodePreset preset = FindPreset(node.type, node.action);
            if (preset != null && preset.fields != null)
            {
                foreach (FieldDef fd in preset.fields)
                {
                    if (fd != null && fd.name == name)
                        return fd;
                }
            }
            if (IsSlotField(name, "target_type"))
                return new FieldDef(name, "目标类型", FieldEditType.Dropdown, new string[] { "无", "角色", "英雄" }, "无");
            if (IsSlotField(name, "target_error"))
                return new FieldDef(name, "错误提示", FieldEditType.Input, null, "");
            //运算节点的编号输入槽（arg1… / value1…）：与端口同名 → 手填控件直接画在该端口行上
            //（整数运算=数字输入框，逻辑运算=勾选框；该端口有连线时自动隐藏，见 CreateNodeInlineFields）
            string param_base = ParamSlotBaseName(node);
            if (param_base != null && SlotNumberOf(name, param_base) > 0)
                return param_base == "arg" ? IntField(name, "值", "0") : BoolField(name, "值", "false");
            return null;
        }

        /// <summary>name 是否是「前缀」或「前缀+编号(≥1)」形式的槽字段</summary>
        private static bool IsSlotField(string name, string base_name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith(base_name))
                return false;
            string num = name.Substring(base_name.Length);
            if (string.IsNullOrEmpty(num))
                return true;    //无编号：合法的槽1 旧形式
            return int.TryParse(num, out int n) && n >= 1;
        }

        /// <summary>字段名 → 槽号：target_typeN（N≥1）→ N；旧的无编号 target_type → 1；不是目标槽字段返回 0</summary>
        private static int TargetTypeSlotOf(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            if (name == "target_type")
                return 1;
            if (!name.StartsWith("target_type"))
                return 0;
            string num = name.Substring("target_type".Length);
            return (!string.IsNullOrEmpty(num) && int.TryParse(num, out int n) && n >= 1) ? n : 0;
        }

        /// <summary>节点现存的目标槽号（升序；没有目标槽时返回空列表 = 默认不使用任何目标）</summary>
        private static List<int> TargetSlots(GraphNode node)
        {
            List<int> list = new List<int>();
            if (node != null && node.fields != null)
            {
                foreach (FieldCustomData f in node.fields)
                {
                    int s = TargetTypeSlotOf(f != null ? f.name : null);
                    if (s > 0 && !list.Contains(s))
                        list.Add(s);
                }
            }
            list.Sort();
            return list;
        }

        /// <summary>新增一个目标槽：槽号 = 现存最大 +1（首次为 1；只增不复用，删中间槽后其余槽号不变、连线不受影响）</summary>
        private void AddTargetSlot(GraphNode node)
        {
            if (node == null)
                return;
            List<int> slots = TargetSlots(node);
            int slot = slots.Count > 0 ? slots[slots.Count - 1] + 1 : 1;
            PushUndo();
            if (node.fields == null)
                node.fields = new List<FieldCustomData>();
            node.fields.Add(new FieldCustomData { name = "target_type" + slot, value = "角色" });
            node.fields.Add(new FieldCustomData { name = "target_error" + slot, value = "" });
            //引脚插到同类末尾（输入口排在最后一个 targetCondition* 之后；输出口排在最后一个 targetCard* 之后），
            //保持「目标N条件」与「目标卡牌N」各自成组
            GraphPin cond_pin = new GraphPin
            {
                id = node.id + "_targetCondition" + slot,
                name = "targetCondition" + slot,
                display_name = "目标" + slot + "条件",
                type = NodeValueType.Boolean,
                is_output = false,
            };
            GraphPin card_pin = new GraphPin
            {
                id = node.id + "_targetCard" + slot,
                name = "targetCard" + slot,
                display_name = "目标卡牌" + slot,
                type = NodeValueType.Card,
                is_output = true,
            };
            int cond_at = node.pins.FindLastIndex(p => p != null && !p.is_output && p.name.StartsWith("targetCondition"));
            node.pins.Insert(cond_at >= 0 ? cond_at + 1 : node.pins.Count, cond_pin);
            int card_at = node.pins.FindLastIndex(p => p != null && p.is_output && p.name.StartsWith("targetCard"));
            node.pins.Insert(card_at >= 0 ? card_at + 1 : node.pins.Count, card_pin);
            RebuildCanvas();
            SetStatus("已新增目标" + slot + "：给它连一个条件链，下游用「目标卡牌" + slot + "」引用该目标");
        }

        /// <summary>删除目标槽：字段 + 引脚 + 落在这些引脚上的连线一起移除（删空后入口就是"无目标"）</summary>
        private void RemoveTargetSlot(GraphNode node, int slot)
        {
            if (node == null || slot <= 0)
                return;
            PushUndo();
            // 槽1 兼容旧的无编号字段形式：两种命名都清掉
            string[] type_names = slot == 1
                ? new string[] { "target_type", "target_type1", "target_error", "target_error1", "target_side", "target_side1" }
                : new string[] { "target_type" + slot, "target_error" + slot, "target_side" + slot };
            string cond_name = slot == 1 ? "targetCondition1" : "targetCondition" + slot;
            string card_name = "targetCard" + slot;
            string cond_id = node.id + "_" + cond_name;
            string card_id = node.id + "_" + card_name;
            node.pins.RemoveAll(p => p != null
                && (p.name == cond_name || p.name == card_name
                    || (slot == 1 && (p.name == "targetCondition" || p.name == "targetCard1"))));
            node.fields.RemoveAll(f => f != null && System.Array.IndexOf(type_names, f.name) >= 0);
            if (graph != null && graph.links != null)
            {
                graph.links.RemoveAll(l => l != null
                    && ((l.from_node == node.id && l.from_pin == card_id)
                        || (l.to_node == node.id && l.to_pin == cond_id)));
            }
            RenumberTargetSlots(node);   //删除后自动重排：目标3/4 → 目标2/3（字段/引脚/连线一起改写，不留编号断层）
            RebuildCanvas();
            SetStatus("已删除目标" + slot + "，后续目标已自动重排编号（连线保持有效）");
        }

        // ---------------- 运算节点：编号输入槽增删（112004 整数运算 / 112005 逻辑运算） ----------------

        /// <summary>槽号 → 实际名字（槽 1 可能叫 arg 也可能叫 arg1：以节点上现存的名字为准）</summary>
        private static string SlotParamNameOf(GraphNode node, string base_name, int slot)
        {
            if (node != null && node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    if (p != null && SlotNumberOf(p.name, base_name) == slot)
                        return p.name;
                }
            }
            if (node != null && node.fields != null)
            {
                foreach (FieldCustomData f in node.fields)
                {
                    if (f != null && SlotNumberOf(f.name, base_name) == slot)
                        return f.name;
                }
            }
            return slot == 1 ? base_name : base_name + slot;
        }

        /// <summary>新增一个编号输入槽：字段 + 端口（槽号只增不复用，与入口目标槽同规则）。
        /// 新槽默认手填值 0/false；连上取值线后手填框自动隐藏。</summary>
        private void AddParamSlot(GraphNode node)
        {
            string base_name = ParamSlotBaseName(node);
            if (node == null || base_name == null)
                return;
            bool is_int = base_name == "arg";
            List<int> slots = ParamSlotsOf(node, base_name);
            int slot = slots.Count > 0 ? slots[slots.Count - 1] + 1 : 1;
            string name = slot == 1 ? base_name : base_name + slot;   //槽1 沿用无编号名（与预设/旧图一致）
            PushUndo();
            if (node.fields == null)
                node.fields = new List<FieldCustomData>();
            if (node.pins == null)
                node.pins = new List<GraphPin>();
            node.fields.Add(new FieldCustomData { name = name, value = is_int ? "0" : "false" });
            node.pins.Add(new GraphPin
            {
                id = node.id + "_" + name,
                name = name,
                display_name = "值",
                type = is_int ? NodeValueType.Int32 : NodeValueType.Boolean,
                is_output = false,
            });
            RebuildCanvas();
            SetStatus("已新增输入 " + slot + "（可连取值线，也可直接填" + (is_int ? "整数" : "布尔") + "值）");
        }

        /// <summary>删除一个编号输入槽：字段 + 端口 + 落在该端口上的连线一起移除。
        /// 规则：**至少保留 1 个输入**（只剩 1 个时拒绝删除）。
        /// 槽号保持稳定不重排（避免"字段改名成功、端口改名失败"造成的手填框与端口错位）；
        /// 运行时按现存槽号升序求值，编号有空洞不影响结果。</summary>
        private void RemoveParamSlot(GraphNode node, int slot)
        {
            string base_name = ParamSlotBaseName(node);
            if (node == null || base_name == null || slot <= 0)
                return;
            if (ParamSlotsOf(node, base_name).Count <= 1)
            {
                SetStatus("至少要保留 1 个输入值（当前只有 1 个）：如需改成单输入，直接编辑它的值即可。");
                return;
            }
            PushUndo();
            string name = SlotParamNameOf(node, base_name, slot);
            string pin_id = node.id + "_" + name;
            if (node.pins != null)
                node.pins.RemoveAll(p => p != null && p.name == name);
            if (node.fields != null)
                node.fields.RemoveAll(f => f != null && f.name == name);
            if (graph != null && graph.links != null)
                graph.links.RemoveAll(l => l != null && l.to_node == node.id && l.to_pin == pin_id);
            RebuildCanvas();
            SetStatus("已删除输入 " + slot + "（至少保留 1 个输入）");
        }

        /// <summary>「＋ 新增输入」按钮（运算节点底部，与入口「＋ 新增目标」同款）</summary>
        private void CreateAddParamSlotButton(Transform parent, GraphNode node, float y)
        {
            GameObject row = new GameObject("InlineField_AddParam", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(12f, y);
            rt.sizeDelta = new Vector2(112f, 20f);
            Image img = row.GetComponent<Image>();
            img.color = new Color(0.25f, 0.5f, 0.75f, 0.85f);
            Button btn = row.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => AddParamSlot(node));
            TMP_Text t = MakeNodeTmpText(rt, "Label", "＋ 新增输入", 12, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);
        }

        /// <summary>输入槽行上的「×」删除按钮（画在该端口行的右端，与入口目标槽的行内删除按钮同款）</summary>
        private void CreateRemoveParamSlotButton(Transform parent, GraphNode node, int slot,
            float node_w, float right_reserve, float row_center_y)
        {
            GameObject go = new GameObject("BtnRemoveParam" + slot, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(Mathf.Max(60f, node_w - 20f - right_reserve - 22f), row_center_y);
            rt.sizeDelta = new Vector2(18f, 18f);
            Image img = go.GetComponent<Image>();
            img.color = new Color(0.75f, 0.25f, 0.25f, 0.9f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Label", "×", 12, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);
            int captured = slot;
            btn.onClick.AddListener(() => RemoveParamSlot(node, captured));
        }

        /// <summary>删除目标槽后重排编号：剩余槽按从小到大连续编号（原目标2、3 → 目标1、2）。
        /// 同步改写字段名（target_typeN/target_errorN/target_sideN）、引脚名/ID/显示名，
        /// 并把引用旧引脚 ID 的连线改到新 ID——因此重排后连线不失效、也不会出现编号空洞。
        /// 重排映射恒"向下收紧"且按槽号升序处理，目标名不会与现存引脚冲突。</summary>
        private void RenumberTargetSlots(GraphNode node)
        {
            if (node == null)
                return;
            List<int> slots = TargetSlots(node);
            for (int i = 0; i < slots.Count; i++)
            {
                int old_no = slots[i];
                int new_no = i + 1;
                if (old_no == new_no)
                    continue;
                RenameNodeField(node, "target_type" + old_no, "target_type" + new_no);
                RenameNodeField(node, "target_error" + old_no, "target_error" + new_no);
                RenameNodeField(node, "target_side" + old_no, "target_side" + new_no);
                RenameNodePin(graph, node, "targetCondition" + old_no, "targetCondition" + new_no,
                    "目标" + new_no + "条件", NodeValueType.Boolean, false);
                RenameNodePin(graph, node, "targetCard" + old_no, "targetCard" + new_no,
                    "目标卡牌" + new_no, NodeValueType.Card, true);
            }
        }

        /// <summary>引脚改名（name/ID/显示名/类型同步），并把引用旧引脚 ID 的连线改到新 ID</summary>
        private static void RenameNodePin(GraphData graph, GraphNode node, string old_name, string new_name,
            string display, NodeValueType type, bool is_output)
        {
            if (node == null || node.pins == null)
                return;
            string old_id = node.id + "_" + old_name;
            string new_id = node.id + "_" + new_name;
            foreach (GraphPin p in node.pins)
            {
                if (p != null && p.name == old_name)
                {
                    p.name = new_name;
                    p.id = new_id;
                    p.display_name = display;
                    p.type = type;
                    p.is_output = is_output;
                }
            }
            if (graph != null && graph.links != null)
            {
                foreach (GraphLink l in graph.links)
                {
                    if (l == null)
                        continue;
                    if (l.from_node == node.id && l.from_pin == old_id)
                        l.from_pin = new_id;
                    if (l.to_node == node.id && l.to_pin == old_id)
                        l.to_pin = new_id;
                }
            }
        }

        /// <summary>「＋ 新增目标」按钮（主动效果入口底部）</summary>
        private void CreateAddTargetSlotButton(Transform parent, GraphNode node, float y)
        {
            GameObject row = new GameObject("InlineField_AddTarget", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(20f, y);
            rt.sizeDelta = new Vector2(110f, 20f);
            Image img = row.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.14f);
            Button btn = row.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Label", "＋ 新增目标", 12, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);
            btn.onClick.AddListener(() => AddTargetSlot(node));
        }

        /// <summary>目标槽行右端的「×」删除按钮（槽数 >1 时才建）</summary>
        private void CreateRemoveTargetSlotButton(RectTransform row, GraphNode node, int slot, float node_w, float right_reserve)
        {
            GameObject go = new GameObject("BtnRemoveSlot", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(row, false);
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(Mathf.Max(60f, node_w - 20f - right_reserve - 22f), 0f);
            rt.sizeDelta = new Vector2(18f, 18f);
            Image img = go.GetComponent<Image>();
            img.color = new Color(0.75f, 0.25f, 0.25f, 0.9f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Label", "×", 12, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);
            btn.onClick.AddListener(() => RemoveTargetSlot(node, slot));
        }

        /// <summary>新建 TMP 文本并应用节点字体（与节点内其他文字同字体/同渲染，避免样式和清晰度不一致）</summary>
        private TMP_Text MakeNodeTmpText(Transform parent, string name, string text, int size, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            ApplyNodeFont(t, string.IsNullOrEmpty(text) ? FontProbe : text + FontProbe);
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>控件里显示的文本（下拉/多选显示选中项，其余显示原值）</summary>
        private static string InlineFieldText(FieldDef fd, string value)
        {
            if (fd != null && (fd.edit == FieldEditType.Dropdown || fd.edit == FieldEditType.MultiOptions))
                return SelectDisplay(fd, value);
            if (fd != null && fd.edit == FieldEditType.KeywordSelect)
                return KeywordSelectText(value);   //存 id、显示标题
            if (fd != null && fd.edit == FieldEditType.BuffSelect)
                return BuffSelectText(value);      //存 BuffData.id、显示「标题 (id)」
            if (fd != null && fd.edit == FieldEditType.CardSelect)
                return CardSelectText(value);      //存卡牌 id、显示「标题 (id)」
            if (fd != null && fd.edit == FieldEditType.BgmSelect)
                return BgmSelectText(value);       //存 BgmEntry.id、显示「标题（来源）」
            return value;
        }

        /// <summary>关键词下拉当前显示文本（未选择时给占位提示）</summary>
        private static string KeywordSelectText(string keyword_id)
        {
            string s = KeywordDisplay(keyword_id);
            return string.IsNullOrEmpty(s) ? "（选择关键词）" : s;
        }

        /// <summary>关键词 id → 标题（下拉显示用；未注册/为空则原样返回，便于回看自定义值）</summary>
        private static string KeywordDisplay(string keyword_id)
        {
            if (string.IsNullOrEmpty(keyword_id))
                return "";
            KeywordData.Load();
            KeywordData kd = KeywordData.Get(keyword_id);
            return kd != null && !string.IsNullOrEmpty(kd.title) ? kd.title : keyword_id;
        }

        /// <summary>游戏自带关键词：下拉显示名（标题）</summary>
        private static string[] KeywordSelectOptions()
        {
            KeywordData.Load();
            List<string> opts = new List<string>();
            foreach (KeywordData k in KeywordData.GetAll())
            {
                if (k != null && !string.IsNullOrEmpty(k.title))
                    opts.Add(k.title);
            }
            return opts.ToArray();
        }

        /// <summary>游戏自带关键词：与显示名一一对应的实际值（KeywordData.id）</summary>
        private static string[] KeywordSelectValues()
        {
            KeywordData.Load();
            List<string> vals = new List<string>();
            foreach (KeywordData k in KeywordData.GetAll())
            {
                if (k != null && !string.IsNullOrEmpty(k.title))
                    vals.Add(k.id);
            }
            return vals.ToArray();
        }

        /// <summary>内联控件宽度：默认 6 个字符；内容更长时按内容加宽（节点宽度随之自适应）</summary>
        private static float InlineControlWidth(string value)
        {
            float base_w = EstimateTextWidth("测测测测测测", 12);   //6 个全角字符
            float text_w = EstimateTextWidth(value, 12) + 18f;
            return Mathf.Max(base_w, text_w);
        }

        /// <summary>内联字段「字段名」列宽：按文字实测宽度（长字段名不压到控件）</summary>
        private static float InlineLabelWidth(FieldDef fd)
        {
            return Mathf.Max(88f, EstimateTextWidth(fd != null ? fd.display_name : "", 12) + 10f);
        }

        private void CreateInlineFieldRow(Transform parent, GraphNode node, FieldDef fd, string current, float y,
            float node_w, float right_reserve, float x0 = 20f, bool with_label = true)
        {
            GameObject row = new GameObject("InlineField_" + fd.name, typeof(RectTransform));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(x0, y);    //左列：与输入口文字同一起点，左对齐（端口行内控件由调用方给 x0）
            rt.sizeDelta = new Vector2(1f, 20f);         //行容器不限宽（子控件绝对定位）

            //字段名：TMP + 节点字体（与节点内其他文字同一渲染），宽度按文字实测，避免与控件文字重叠
            //（字段画在端口行时端口名已由端口标签显示，with_label=false 跳过）
            float lw = 0f;
            if (with_label)
            {
                lw = InlineLabelWidth(fd);
                TMP_Text lb = MakeNodeTmpText(rt, "Label", fd.display_name, 12, TextAlignmentOptions.Left);
                RectTransform lrt = lb.rectTransform;
                lrt.anchorMin = new Vector2(0, 0);
                lrt.anchorMax = new Vector2(0, 1);
                lrt.pivot = new Vector2(0, 0.5f);
                lrt.anchoredPosition = new Vector2(0f, 0);
                lrt.sizeDelta = new Vector2(lw, 0);
                lb.color = new Color(1f, 1f, 1f, 0.75f);
                lb.raycastTarget = false;
                lw += 4f;
            }

            GameObject ctl_go = new GameObject("Ctl", typeof(RectTransform));
            RectTransform ctl = ctl_go.GetComponent<RectTransform>();
            ctl.SetParent(rt, false);
            ctl.anchorMin = new Vector2(0, 0);
            ctl.anchorMax = new Vector2(0, 1);
            ctl.pivot = new Vector2(0, 0.5f);
            //控件宽度：默认6字、内容长则加宽；但不得超过「节点宽 − 左侧起点 − 字段名 − 右侧输出口占位」
            //（超出部分由 TMP 省略号截断：既不压住右侧输出口，也不冲出节点）
            float avail = node_w - x0 - lw - 4f - right_reserve;
            float cw = Mathf.Min(InlineControlWidth(InlineFieldText(fd, current)), Mathf.Max(60f, avail));
            ctl.anchoredPosition = new Vector2(lw, 0);
            ctl.sizeDelta = new Vector2(cw, -2f);

            if (fd.edit == FieldEditType.Toggle)
                CreateInlineToggle(ctl, node, fd, current);
            else if (fd.edit == FieldEditType.KeywordSelect)
                CreateInlineKeywordSelect(ctl, node, fd, current);
            else if (fd.edit == FieldEditType.BuffSelect)
                CreateInlineBuffSelect(ctl, node, fd, current);     //增益池下拉（206001/206002/106002/106003 的 buff_id）
            else if (fd.edit == FieldEditType.CardSelect)
                CreateInlineCardSelect(ctl, node, fd, current);     //卡池卡牌下拉（103002/202003/202004/202005/202046 的定义口）
            else if (fd.edit == FieldEditType.BgmSelect)
                CreateInlineBgmSelect(ctl, node, fd, current);      //音乐库下拉（209101 设置战斗BGM 的 bgm 口）
            else if (fd.edit == FieldEditType.Dropdown || fd.edit == FieldEditType.MultiOptions)
                CreateInlineSelect(ctl, node, fd, current, fd.edit == FieldEditType.MultiOptions);
            else
                CreateInlineInput(ctl, node, fd, current);

            //多目标：目标类型行的右端给一个「×」删除该槽（删空即回到"无目标"）
            int slot_no = TargetTypeSlotOf(fd.name);
            if (slot_no > 0)
                CreateRemoveTargetSlotButton(rt, node, slot_no, node_w, right_reserve);
        }

        private void CreateInlineInput(RectTransform parent, GraphNode node, FieldDef fd, string current)
        {
            GameObject go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMPro.TMP_InputField));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.14f);
            TMPro.TMP_InputField inp = go.GetComponent<TMPro.TMP_InputField>();
            inp.targetGraphic = img;
            //TMP 输入框必须给 textViewport（标准结构："Text Area"(RectMask2D) → "Text"）：
            //  ① TMP 的 OnDrag → MouseDragOutsideRect 直接解引用 textViewport（为空 → NullReferenceException，
            //     在框内拖动/选字时必现）；
            //  ② 光标默认深色(50,50,50)，在深色节点底上不可见 → 显式设成白色，点击后能看到插入光标。
            GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            RectTransform art = area.GetComponent<RectTransform>();
            art.SetParent(rt, false);
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(5f, 1f);
            art.offsetMax = new Vector2(-5f, -1f);
            TMP_Text t = MakeNodeTmpText(art, "Text", current, 12, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);
            inp.textViewport = art;
            inp.textComponent = t;
            inp.caretColor = Color.white;
            inp.selectionColor = new Color(0.35f, 0.6f, 0.9f, 0.5f);
            inp.text = current;
            //TMP 的 Caret（插入光标）只在 OnEnable 里创建，且要求 m_TextComponent 已赋值；
            //而 AddComponent 会立刻触发一次 OnEnable（那时 textComponent 还是 null）→ 光标永远建不出来
            //（表现为"点了没光标"）。绑定完组件后重启一次输入框，让 TMP 建出 Caret 并完成初始化。
            inp.enabled = false;
            inp.enabled = true;
            inp.onValueChanged.AddListener((v) =>
            {
                SetFieldValue(node, fd.name, v);
                RefreshNodeSummary(node);
                RefreshPinValues();
            });
            //结束编辑（回车/失焦）后重排：内容变长则输入框与节点一起撑大（只触发一次，避免销毁时回调重入）
            bool relayout_done = false;
            inp.onEndEdit.AddListener((v) =>
            {
                if (relayout_done)
                    return;
                relayout_done = true;
                RebuildCanvas();
            });
        }

        private void CreateInlineToggle(RectTransform parent, GraphNode node, FieldDef fd, string current)
        {
            GameObject go = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Text", "√", 12, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);

            bool on = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
            img.color = on ? new Color(0.25f, 0.6f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.15f);
            t.text = on ? "√" : "";
            btn.onClick.AddListener(() =>
            {
                bool cur = string.Equals(GetFieldValue(node, fd.name, fd.def ?? ""), "true", StringComparison.OrdinalIgnoreCase);
                bool nv = !cur;
                SetFieldValue(node, fd.name, nv ? "true" : "false");
                img.color = nv ? new Color(0.25f, 0.6f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.15f);
                t.text = nv ? "√" : "";
                RefreshNodeSummary(node);
                RefreshPinValues();
                RebuildCanvas();   //切换后重排（宽度随内容）
            });
        }

        private void CreateInlineSelect(RectTransform parent, GraphNode node, FieldDef fd, string current, bool multi)
        {
            GameObject go = new GameObject("Select", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Text", SelectDisplay(fd, current), 12, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 5, 0, 5, 0);
            TMP_Text captured_t = t;
            btn.onClick.AddListener(() => OpenFieldSelectPopup(node, fd, multi,
                () => { if (captured_t != null) captured_t.text = SelectDisplay(fd, GetFieldValue(node, fd.name, fd.def ?? "")); }));
        }

        /// <summary>内联关键词下拉：显示关键词标题、存储 KeywordData.id（列出游戏自带全部关键词）。
        /// 后续关键词支持自定义设计后，这里换成自定义关键词池即可，节点语义不变。</summary>
        private void CreateInlineKeywordSelect(RectTransform parent, GraphNode node, FieldDef fd, string current)
        {
            GameObject go = new GameObject("Select", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Text", KeywordSelectText(current), 12, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 5, 0, 5, 0);
            TMP_Text captured_t = t;
            btn.onClick.AddListener(() => OpenFieldSelectPopup(node, fd, false,
                () => { if (captured_t != null) captured_t.text = KeywordSelectText(GetFieldValue(node, fd.name, fd.def ?? "")); },
                KeywordSelectOptions(), KeywordSelectValues()));
        }

        /// <summary>内联增益下拉：显示「标题 (id)」、存储 BuffData.id（列出 BuffPoolIO 增益池全部定义）。
        /// 206001 添加增益 / 206002 移除增益 / 106002 获取增益定义 / 106003 是否具有增益 的 buff_id 字段用。</summary>
        private void CreateInlineBuffSelect(RectTransform parent, GraphNode node, FieldDef fd, string current)
        {
            GameObject go = new GameObject("Select", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Text", BuffSelectText(current), 12, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 5, 0, 5, 0);
            TMP_Text captured_t = t;
            btn.onClick.AddListener(() => OpenFieldSelectPopup(node, fd, false,
                () => { if (captured_t != null) captured_t.text = BuffSelectText(GetFieldValue(node, fd.name, fd.def ?? "")); },
                BuffSelectOptions(), BuffSelectValues()));
        }

        /// <summary>内联卡牌下拉：显示「标题 (id)」、存储卡牌 id（列出当前编辑卡池的全部卡牌）。
        /// 103002 获取卡牌定义 / 202003/202004/202005/202046 创建衍生卡 的定义口用。</summary>
        private void CreateInlineCardSelect(RectTransform parent, GraphNode node, FieldDef fd, string current)
        {
            GameObject go = new GameObject("Select", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Text", CardSelectText(current), 12, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 5, 0, 5, 0);
            TMP_Text captured_t = t;
            List<string> opts = new List<string>();
            List<string> vals = new List<string>();
            if (pool != null && pool.cards != null)
            {
                foreach (CardCustomData c in pool.cards)
                {
                    if (c == null || string.IsNullOrEmpty(c.id))
                        continue;
                    vals.Add(c.id);
                    opts.Add((string.IsNullOrEmpty(c.title) ? c.id : c.title) + " (" + c.id + ")");
                }
            }
            string[] options = opts.ToArray();
            string[] values = vals.ToArray();
            btn.onClick.AddListener(() => OpenFieldSelectPopup(node, fd, false,
                () => { if (captured_t != null) captured_t.text = CardSelectText(GetFieldValue(node, fd.name, fd.def ?? "")); },
                options, values));
        }

        /// <summary>内联音乐库下拉：显示「标题（官方/导入）」、存储 BgmEntry.id（首个选项=留空停止 BGM）。
        /// 209101 设置战斗BGM 的 bgm 口用；弹层底部仍可手填标识/显示名（音乐库为空时也能填）。</summary>
        private void CreateInlineBgmSelect(RectTransform parent, GraphNode node, FieldDef fd, string current)
        {
            GameObject go = new GameObject("Select", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeNodeTmpText(rt, "Text", BgmSelectText(current), 12, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 5, 0, 5, 0);
            TMP_Text captured_t = t;
            btn.onClick.AddListener(() => OpenFieldSelectPopup(node, fd, false,
                () => { if (captured_t != null) captured_t.text = BgmSelectText(GetFieldValue(node, fd.name, fd.def ?? "")); },
                BgmSelectOptions(), BgmSelectValues()));
        }

        /// <summary>增益下拉显示文本：增益池里能查到 → 「标题 (id)」；查不到 → 保留原 id（便于发现已删定义）；未选 → 占位</summary>
        private static string BuffSelectText(string buff_id)
        {
            if (string.IsNullOrEmpty(buff_id))
                return "（选择增益）";
            BuffData b = BuffPoolIO.Get(buff_id);
            if (b == null)
                return buff_id;
            return (string.IsNullOrEmpty(b.title) ? b.id : b.title) + " (" + b.id + ")";
        }

        private static string[] BuffSelectOptions()
        {
            List<string> list = new List<string>();
            foreach (BuffData b in BuffPoolIO.GetAll())
            {
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                list.Add((string.IsNullOrEmpty(b.title) ? b.id : b.title) + " (" + b.id + ")");
            }
            return list.ToArray();
        }

        private static string[] BuffSelectValues()
        {
            List<string> list = new List<string>();
            foreach (BuffData b in BuffPoolIO.GetAll())
            {
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                list.Add(b.id);
            }
            return list.ToArray();
        }

        /// <summary>卡牌下拉显示文本：卡牌注册表里能查到 → 「标题 (id)」；查不到 → 保留原 id；未选 → 占位</summary>
        private static string CardSelectText(string card_id)
        {
            if (string.IsNullOrEmpty(card_id))
                return "（选择卡牌）";
            CardData d = CardData.Get(card_id);
            if (d == null)
                return card_id;
            return (string.IsNullOrEmpty(d.title) ? d.id : d.title) + " (" + d.id + ")";
        }

        /// <summary>BGM 下拉显示文本：音乐库里按 id 或显示名查到 → 「标题（官方/导入）」；查不到 → 保留原值；未选 → 占位（留空=停止）</summary>
        private static string BgmSelectText(string bgm_id)
        {
            if (string.IsNullOrEmpty(bgm_id))
                return "（选择 BGM；留空=停止）";
            TcgEngine.Audio.BgmEntry e = TcgEngine.Audio.BgmLibrary.Find(bgm_id);
            if (e == null)
                return bgm_id;
            return (string.IsNullOrEmpty(e.title) ? e.id : e.title) + "（" + TcgEngine.Audio.BgmLibrary.SourceLabel(e) + "）";
        }

        /// <summary>BGM 下拉选项：首个=留空（停止当前战斗BGM），其后=音乐库全部条目。与 BgmSelectValues 一一对应。</summary>
        private static string[] BgmSelectOptions()
        {
            List<string> list = new List<string> { "（留空：停止当前战斗BGM）" };
            foreach (TcgEngine.Audio.BgmEntry e in TcgEngine.Audio.BgmLibrary.GetAll())
            {
                if (e == null || string.IsNullOrEmpty(e.id))
                    continue;
                list.Add((string.IsNullOrEmpty(e.title) ? e.id : e.title) + "（" + TcgEngine.Audio.BgmLibrary.SourceLabel(e) + "）");
            }
            return list.ToArray();
        }

        /// <summary>BGM 下拉实际值（写入 node.fields 的 bgm）：首个=""，其后=BgmEntry.id</summary>
        private static string[] BgmSelectValues()
        {
            List<string> list = new List<string> { "" };
            foreach (TcgEngine.Audio.BgmEntry e in TcgEngine.Audio.BgmLibrary.GetAll())
            {
                if (e == null || string.IsNullOrEmpty(e.id))
                    continue;
                list.Add(e.id);
            }
            return list.ToArray();
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
            int field_rows = 0;
            //与输入口同名的字段直接画在该端口行右侧（不额外占行）；其余字段各占一行
            //（用节点实际字段：预设字段 + 动态目标槽字段）
            foreach (FieldDef fd in NodeRenderFields(node))
            {
                if (fd != null && !string.IsNullOrEmpty(fd.name) && !FieldHasInputPin(node, fd.name))
                    field_rows++;
            }
            if (IsTargetSlotEntry(node))
                field_rows++;   //「＋ 新增目标」按钮行
            if (IsParamSlotNode(node))
                field_rows++;   //「＋ 新增输入」按钮行（运算节点）
            //字段接在左侧输入口下方（同属左列），故左列行数 = 输入口 + 字段；取左右列较大者
            int rows = Mathf.Max(in_c + field_rows, out_c);
            return 33f + rows * 24f + 8f;
        }

        /// <summary>估算节点宽度：取标题/端口标签/内联值框中最宽一行（中文全角按字号、ASCII半角按0.55字号估算；
        /// 画布实例化时会用标题 TMP preferredWidth 实测值取更宽者，min190/max460，规格第3节）</summary>
        private static float EstimateNodeWidth(GraphNode node)
        {
            NodePreset preset = FindPreset(node.type, node.action);
            float w = EstimateTextWidth(node.title, 20) + 24;
            if (node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    string name = string.IsNullOrEmpty(p.display_name) ? p.name : p.display_name;
                    //有同名字段的输入口：值由行内控件承载，宽度按下方「端口行 + 控件」单独估算
                    if (!p.is_output && !HasPresetField(node, p.name))
                        name += " = 10";   //内联值框追加估算
                    w = Mathf.Max(w, EstimateTextWidth(name, 12) + 44);
                }
            }
            //同一行同时有输入口(名+值框约120px)与输出口名：留足总宽，避免左右文字叠在一起
            float out_name_w = 0f;
            bool has_in = false, has_out = false;
            if (node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    string nm = string.IsNullOrEmpty(p.display_name) ? p.name : p.display_name;
                    if (p.is_output)
                    {
                        has_out = true;
                        out_name_w = Mathf.Max(out_name_w, EstimateTextWidth(nm, 12) + 18f);
                    }
                    else
                    {
                        has_in = true;
                    }
                }
            }
            //左列占用：输入口(名+值框) 与 内联字段(左边距12 + 字段名 + 4 + 控件110)
            float left_need = has_in ? 152f : 0f;
            if (preset != null && preset.fields != null)
            {
                foreach (FieldDef fd in preset.fields)
                {
                    if (fd == null || string.IsNullOrEmpty(fd.name))
                        continue;
                    float cw = InlineControlWidth(InlineFieldText(fd, GetFieldValue(node, fd.name, fd.def ?? "")));
                    if (FieldHasInputPin(node, fd.name))
                    {
                        //端口行右侧的常量控件：圆点12 + 点间隙14 + 端口名 + 6 + 控件 + 10
                        left_need = Mathf.Max(left_need, PinFieldX(node, fd.name, 12f) + cw + 10f);
                        continue;
                    }
                    left_need = Mathf.Max(left_need, 20f + InlineLabelWidth(fd) + 4f + cw + 10f);
                }
            }
            //左列 + 右列输出口名 的总宽（避免左右文字叠在一起）；无输出口时只保证左列放得下
            w = Mathf.Max(w, left_need + (has_out ? out_name_w + 20f : 16f));
            return Mathf.Clamp(w, 176f, 720f);   //长内容可把节点撑宽一些，右上限后由控件省略号截断
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
            float y = node_rect.sizeDelta.y - 45f - row * 24f;   //端口行高 24（原 28），节点更紧凑
            float x = pin.is_output ? Mathf.Max(0f, node_rect.sizeDelta.x - 12f) : 12f;   //内缩，避开边框与文字
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
                //数据输入口若有同名字段：端口名照常显示，常量控件由 CreateNodeInlineFields 画在本行右侧
                if (pin.type == NodeValueType.Flow || HasPresetField(node, pin.name))
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

        /// <summary>创建端口名称标签（TMP，挂在引脚实例上：输出口贴点左侧右对齐，输入口贴点右侧左对齐）</summary>
        private void CreatePinNameLabel(Transform pin_inst, GraphPin pin, bool is_output)
        {
            GameObject go = new GameObject("PinName", typeof(RectTransform));
            go.transform.SetParent(pin_inst, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            //标签框贴合文字宽度（原来固定 130 会向左伸出很远，挤占字段控件的可用宽度）
            string nm0 = string.IsNullOrEmpty(pin.display_name) ? pin.name : pin.display_name;
            float name_w = EstimateTextWidth(nm0, 12) + 18f;
            //输入口标签贴圆点右侧、输出口贴圆点左侧（均向框内），中心偏移 = 圆点半径6 + 间隙8 + 半宽
            float off = 6f + 8f + name_w * 0.5f;
            rt.anchoredPosition = new Vector2(is_output ? -off : off, 0f);
            rt.sizeDelta = new Vector2(name_w, 18f);

            TextMeshProUGUI txt;
            try
            {
                txt = go.AddComponent<TextMeshProUGUI>();
                ApplyNodeFont(txt, string.IsNullOrEmpty(pin.display_name) ? pin.name : pin.display_name);
                txt.fontSize = 12;
                txt.alignment = is_output ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
                txt.color = Opaque(PinColor(pin));   //端口标签不透明
                txt.text = string.IsNullOrEmpty(pin.display_name) ? pin.name : pin.display_name;
                txt.raycastTarget = false;
                txt.enableWordWrapping = false;
                txt.overflowMode = TextOverflowModes.Ellipsis;   //超出框内截断，避免文字溢出节点
            }
            catch (System.Exception e)
            {
                //TMP 环境异常：回退旧版 Text，端口标签照常显示
                Debug.LogError("端口标签 TMP 化失败，已回退旧版 Text。原因：\n" + e);
                Text back = go.AddComponent<Text>();
                back.font = LegacyUIFont();
                back.fontSize = 12;
                back.alignment = is_output ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                back.color = Opaque(PinColor(pin));
                back.text = string.IsNullOrEmpty(pin.display_name) ? pin.name : pin.display_name;
                back.raycastTarget = false;
            }
        }

        /// <summary>创建输入口的内联数值框（TMP，挂在引脚实例上，位于圆点右侧）；失败返回 null（NodePin 已判空）</summary>
        private TMP_Text CreatePinValueLabel(Transform pin_inst)
        {
            GameObject go = new GameObject("ValueLabel", typeof(RectTransform));
            go.transform.SetParent(pin_inst, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(68f, 0f);   //文字起点与端口名标签/字段名对齐（圆点右侧留空）
            rt.sizeDelta = new Vector2(120f, 18f);

            try
            {
                TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
                ApplyNodeFont(txt);
                txt.fontSize = 12;
                txt.alignment = TextAlignmentOptions.Left;
                txt.color = new Color(0.357f, 0.616f, 1f, 1f);
                txt.raycastTarget = true;
                txt.enableWordWrapping = false;
                txt.overflowMode = TextOverflowModes.Ellipsis;   //值框超出宽度截断，避免溢出节点
                return txt;
            }
            catch (System.Exception e)
            {
                Debug.LogError("端口值框 TMP 化失败，已回退旧版 Text。原因：\n" + e);
                Text back = go.AddComponent<Text>();
                back.font = LegacyUIFont();
                back.fontSize = 12;
                back.alignment = TextAnchor.MiddleLeft;
                back.color = new Color(0.357f, 0.616f, 1f, 1f);
                back.raycastTarget = true;
                return null;
            }
        }

        /// <summary>刷新所有输入口的内联数值框（连线/字段编辑后调用）</summary>
        private void RefreshPinValues()
        {
            foreach (NodePin np in all_pins)
            {
                if (np != null)
                    np.RefreshValueLabel();
            }
            RefreshPinFieldVisibility();
        }

        /// <summary>端口同名字段控件显隐：输入口有连线时隐藏（值走连线），无连线时显示（可编辑常量）</summary>
        private void RefreshPinFieldVisibility()
        {
            if (graph == null)
                return;
            foreach (KeyValuePair<string, RectTransform> kv in node_rows)
            {
                RectTransform root = kv.Value;
                GraphNode node = graph.GetNode(kv.Key);
                if (root == null || node == null || node.pins == null)
                    continue;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform ch = root.GetChild(i);
                    if (ch == null || !ch.name.StartsWith("InlineField_"))
                        continue;
                    string fname = ch.name.Substring("InlineField_".Length);
                    GraphPin pin = FindInputPin(node, fname);
                    bool hide = false;
                    if (pin != null)
                        hide = graph.GetIncomingLink(node.id, pin.id) != null;
                    ch.gameObject.SetActive(!hide && !node.collapsed);
                }
            }
        }

        /// <summary>节点上同名的输入口（无则 null）；用于「端口行内常量控件 ↔ 连线取值」互斥</summary>
        private static GraphPin FindInputPin(GraphNode node, string pin_name)
        {
            if (node == null || node.pins == null || string.IsNullOrEmpty(pin_name))
                return null;
            foreach (GraphPin p in node.pins)
            {
                if (!p.is_output && p.name == pin_name)
                    return p;
            }
            return null;
        }

        /// <summary>输入口在输入序列中的行号（找不到返回 -1）</summary>
        private static int InputPinRow(GraphNode node, string pin_name)
        {
            int r = 0;
            if (node == null || node.pins == null)
                return -1;
            foreach (GraphPin p in node.pins)
            {
                if (p.is_output)
                    continue;
                if (p.name == pin_name)
                    return r;
                r++;
            }
            return -1;
        }

        /// <summary>端口行内常量控件的横向起点：圆点(12) + 点半径间隙(6+8) + 端口名标签 + 间距(6)</summary>
        private static float PinFieldX(GraphNode node, string pin_name, float dot_x)
        {
            float nm_w = 0f;
            if (node != null && node.pins != null)
            {
                foreach (GraphPin p in node.pins)
                {
                    if (!p.is_output && p.name == pin_name)
                    {
                        nm_w = EstimateTextWidth(string.IsNullOrEmpty(p.display_name) ? p.name : p.display_name, 12) + 18f;
                        break;
                    }
                }
            }
            return dot_x + 6f + 8f + nm_w + 6f;
        }

        /// <summary>节点是否有同名输入口（字段是否该画到端口行内）</summary>
        private static bool FieldHasInputPin(GraphNode node, string field_name)
        {
            return FindInputPin(node, field_name) != null;
        }

        /// <summary>节点是否有同名字段（输入口是否该画成「端口名 + 行内控件」；含动态目标槽字段）</summary>
        private static bool HasPresetField(GraphNode node, string pin_name)
        {
            if (node == null || string.IsNullOrEmpty(pin_name))
                return false;
            return FindFieldDef(node, pin_name) != null;
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
            inst.transform.SetAsLastSibling(); //连线绘制在节点上层（高度在节点上方），避免被节点遮挡看不清

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
            //zmcs 的 Object 口是万能多态槽（集合/单卡/单定义/任意元素，运行时按上下文语义解析），
            //NodeValueRef 口是"表达式回调槽"（111012 筛选条件/212005 循环条件等）——两者都放宽为任意数据类型可连；
            //数组性(is_array)不参与校验：单值口连集合口由运行时兼容（FirstCard/单定义回退）
            if (fp.type == NodeValueType.Object || tp.type == NodeValueType.Object
                || fp.type == NodeValueType.NodeValueRef || tp.type == NodeValueType.NodeValueRef)
                return true;
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
                    //三级状态：选中蓝 > 缺输入暗红（规格第6.6节）> 默认深灰底；全部不透明
                    bool issue = !string.IsNullOrEmpty(MissingInputHint(node_id));
                    if (node_id == selected_node)
                        bg.color = new Color(0.20f, 0.36f, 0.55f, 1f);
                    else if (issue)
                        bg.color = new Color(0.40f, 0.12f, 0.12f, 1f);
                    else
                        bg.color = new Color(0.14f, 0.15f, 0.20f, 1f);
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

            //参数面板已移除：参数一律在节点上直接编辑，这里只保证右下角恒显示节点库
            ShowFieldPanel(false);

            //旧图/手改 JSON 可能缺字段：补齐默认值，保证节点内联控件有值
            if (node != null)
            {
                NodePreset preset = FindPreset(node.type, node.action);
                if (preset != null && preset.fields != null)
                {
                    foreach (FieldDef fd in preset.fields)
                    {
                        if (!HasField(node, fd.name))
                            node.fields.Add(new FieldCustomData { name = fd.name, value = fd.def ?? "" });
                    }
                }
            }
        }

        /// <summary>节点参数面板已移除（参数都在节点上直接改）：右列由 Tab 决定显示内容，这里只保证参数面板不弹</summary>
        private void ShowFieldPanel(bool show)
        {
            CloseFieldSelectPopup();   //切换节点时关闭可能残留的下拉弹层
            if (node_field_root != null)
                node_field_root.SetActive(false);
            if (node_field_hint != null)
                node_field_hint.gameObject.SetActive(false);
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

        // ---------------- 统一字段下拉：单选/多选 + 自定义输入（自绘弹层，避免 UGUI Dropdown 底部裁切） ----------------

        private GameObject field_select_popup;      // 复用的弹出面板
        private RectTransform field_select_list;    // 选项行容器（ScrollRect Content）
        private TMP_Text field_select_title;
        private TMPro.TMP_InputField field_select_custom;
        private GraphNode field_select_node;
        private FieldDef field_select_fd;
        private bool field_select_multi;
        private Action field_select_refresh;
        private string[] field_select_options;   // 弹层选项显示名（null=用字段预设 options）
        private string[] field_select_values;    // 与显示名一一对应的实际值（null=显示名即实际值）
        private List<string> field_select_pending;         // 通用多选（卡牌关键词/种族等）：当前选中值
        private Action<List<string>> field_select_commit;  // 通用多选：点选即回调；非空时弹层走"卡牌字段"模式（不写 node.fields）
        private bool relayout_pending;    //字段内容变化 → 延迟重建画布，让节点宽高随内容自适应

        private static string SelectDisplay(FieldDef fd, string value)
        {
            if (string.IsNullOrEmpty(value) && fd != null && fd.options != null && fd.options.Length > 0)
                return fd.options[0];
            return value;
        }

        private void CreateFieldSelect(GraphNode node, FieldDef fd, string current, bool multi)
        {
            if (node_field_dropdown_template == null)
                return;
            GameObject inst = Instantiate(node_field_dropdown_template, node_field_area);
            inst.name = "Field_" + fd.name;
            inst.SetActive(true);

            Text label = inst.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.text = fd.display_name;

            RectTransform field = inst.transform.Find("Field") as RectTransform;
            if (field == null)
                return;
            // 去掉模板自带的 UGUI Dropdown（底部会被裁切、且不支持多选），换成自绘按钮
            Dropdown old_dd = field.GetComponent<Dropdown>();
            if (old_dd != null)
                Destroy(old_dd);
            for (int i = field.childCount - 1; i >= 0; i--)
                Destroy(field.GetChild(i).gameObject);

            Image fimg = field.GetComponent<Image>();
            if (fimg == null)
                fimg = field.gameObject.AddComponent<Image>();
            fimg.color = new Color(1f, 1f, 1f, 0.18f);
            Button btn = field.GetComponent<Button>();
            if (btn == null)
                btn = field.gameObject.AddComponent<Button>();
            btn.targetGraphic = fimg;

            TMP_Text val_txt = MakeText("Value", field, SelectDisplay(fd, current), 18, TextAnchor.MiddleLeft, TabFont());
            SetStretchRect(val_txt.rectTransform, 10, 0, 10, 0);
            val_txt.raycastTarget = false;
            TMP_Text captured = val_txt;
            btn.onClick.AddListener(() => OpenFieldSelectPopup(node, fd, multi,
                () => captured.text = SelectDisplay(fd, GetFieldValue(node, fd.name, fd.def ?? ""))));
        }

        private void OpenFieldSelectPopup(GraphNode node, FieldDef fd, bool multi, Action refresh)
        {
            OpenFieldSelectPopup(node, fd, multi, refresh, null, null);
        }

        /// <summary>打开选择弹层；options/values 非空时按「显示名 ↔ 实际值」映射（如关键词：显示标题、存 id）</summary>
        private void OpenFieldSelectPopup(GraphNode node, FieldDef fd, bool multi, Action refresh, string[] options, string[] values)
        {
            EnsureFieldSelectPopup();
            field_select_options = options;
            field_select_values = values;
            field_select_node = node;
            field_select_fd = fd;
            field_select_multi = multi;
            field_select_refresh = refresh;
            field_select_popup.SetActive(true);
            field_select_popup.transform.SetAsLastSibling();
            if (field_select_title != null)
                field_select_title.text = (multi ? "多选：" : "选择：") + fd.display_name;
            if (field_select_custom != null)
                field_select_custom.text = "";
            RebuildFieldSelectList();
        }

        /// <summary>通用多选弹层（卡牌关键词/种族等）：显示名 ↔ 值映射 + 自定义输入；点选即回调提交。
        /// 复用节点字段那套弹层 UI（多选勾选 + 自定义输入），避免重复造控件。</summary>
        private void OpenMultiSelectPopup(string title, List<string> selected, string[] options, string[] values,
            Action<List<string>> on_commit)
        {
            EnsureFieldSelectPopup();
            field_select_commit = on_commit;
            field_select_pending = selected != null ? new List<string>(selected) : new List<string>();
            field_select_options = options;
            field_select_values = values;
            field_select_node = null;
            field_select_fd = null;
            field_select_multi = true;
            field_select_refresh = null;
            field_select_popup.SetActive(true);
            field_select_popup.transform.SetAsLastSibling();
            if (field_select_title != null)
            {
                field_select_title.text = "多选：" + title;
                SetFieldSelectTitleFont(field_select_title);
            }
            if (field_select_custom != null)
                field_select_custom.text = "";
            RebuildFieldSelectList();
        }

        /// <summary>通用单选弹层（卡牌类型/阵营/稀有度等）：显示名 ↔ 值映射；选中即回调并关闭</summary>
        private void OpenSingleSelectPopup(string title, string current, string[] options, string[] values,
            Action<string> on_pick)
        {
            EnsureFieldSelectPopup();
            field_select_commit = list => { if (on_pick != null) on_pick(list != null && list.Count > 0 ? list[0] : ""); };
            field_select_pending = string.IsNullOrEmpty(current) ? new List<string>() : new List<string> { current };
            field_select_options = options;
            field_select_values = values;
            field_select_node = null;
            field_select_fd = null;
            field_select_multi = false;
            field_select_refresh = null;
            field_select_popup.SetActive(true);
            field_select_popup.transform.SetAsLastSibling();
            if (field_select_title != null)
                field_select_title.text = "选择：" + title;
            if (field_select_custom != null)
                field_select_custom.text = "";
            RebuildFieldSelectList();
        }

        /// <summary>弹层标题统一 TMP 字体（与节点内文字一致）</summary>
        private void SetFieldSelectTitleFont(TMP_Text t)
        {
            if (t != null)
                ApplyNodeFont(t, "多选关键词");
        }

        private void CloseFieldSelectPopup()
        {
            if (field_select_popup != null)
                field_select_popup.SetActive(false);
            field_select_commit = null;
            field_select_pending = null;
            if (relayout_pending)
            {
                relayout_pending = false;
                RebuildCanvas();   //内容变化后重排节点（宽高/端口/字段位置随之自适应）
            }
        }

        private void RebuildFieldSelectList()
        {
            if (field_select_list == null)
                return;
            bool commit_mode = field_select_commit != null;   //卡牌字段多选：选中集来自 pending，不读 node.fields
            if (!commit_mode && (field_select_fd == null || field_select_node == null))
                return;
            for (int i = field_select_list.childCount - 1; i >= 0; i--)
            {
                Transform c = field_select_list.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
            FieldDef fd = field_select_fd;
            string cur = commit_mode ? "" : GetFieldValue(field_select_node, fd.name, fd.def ?? "");
            string[] sel = commit_mode
                ? (field_select_pending != null ? field_select_pending.ToArray() : new string[0])
                : SplitMulti(cur);
            string[] opts = field_select_options ?? (fd != null ? fd.options : null);   //运行时选项优先（关键词/种族等）
            if (opts != null)
            {
                for (int i = 0; i < opts.Length; i++)
                {
                    string label = opts[i];
                    if (string.IsNullOrEmpty(label))
                        continue;
                    string val = SelectValueAt(i, label);
                    bool on = (field_select_multi || commit_mode) ? Array.IndexOf(sel, val) >= 0 : cur == val;
                    CreateSelectOptionRow(label, val, on);
                }
            }
            // 不在预设里的自定义值也列出来，便于回看/取消
            foreach (string raw in sel)
            {
                if (string.IsNullOrEmpty(raw) || SelectHasValue(opts, raw))
                    continue;
                CreateSelectOptionRow(raw, raw, true);
            }
        }

        /// <summary>第 i 个选项显示名对应的实际值（无映射时显示名即值）</summary>
        private string SelectValueAt(int index, string label)
        {
            if (field_select_values != null && index < field_select_values.Length)
                return field_select_values[index];
            return label;
        }

        /// <summary>选项里是否已包含该实际值（用于补列自定义值，避免重复行）</summary>
        private bool SelectHasValue(string[] opts, string value)
        {
            if (opts == null)
                return false;
            for (int i = 0; i < opts.Length; i++)
            {
                if (SelectValueAt(i, opts[i]) == value)
                    return true;
            }
            return false;
        }

        private void CreateSelectOptionRow(string label, string value, bool on)
        {
            GameObject row = new GameObject("Opt", typeof(RectTransform));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(field_select_list, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 30;
            le.minHeight = 30;
            Image bg = row.AddComponent<Image>();
            bg.color = on ? new Color(0.2f, 0.55f, 0.85f, 0.95f) : new Color(1f, 1f, 1f, 0.08f);
            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;

            TMP_Text t = MakeText("Label", rt, (field_select_multi ? (on ? "☑ " : "☐ ") : "") + label,
                18, TextAnchor.MiddleLeft, TabFont());
            SetStretchRect(t.rectTransform, 12, 0, 12, 0);
            t.raycastTarget = false;

            string captured = value;   //写回实际值（如关键词 id），显示仍用 label
            btn.onClick.AddListener(() => OnFieldSelectOption(captured));
        }

        private void OnFieldSelectOption(string opt)
        {
            if (field_select_commit != null)
            {
                //卡牌字段（单选/多选）：单选=选中即回调并关弹层；多选=切换选中并保持弹层
                if (field_select_multi)
                {
                    if (field_select_pending == null)
                        field_select_pending = new List<string>();
                    if (field_select_pending.Contains(opt))
                        field_select_pending.Remove(opt);
                    else
                        field_select_pending.Add(opt);
                    field_select_commit(new List<string>(field_select_pending));
                    RebuildFieldSelectList();
                }
                else
                {
                    field_select_commit(new List<string> { opt });
                    CloseFieldSelectPopup();
                }
                return;
            }
            FieldDef fd = field_select_fd;
            GraphNode node = field_select_node;
            if (fd == null || node == null)
                return;
            string cur = GetFieldValue(node, fd.name, fd.def ?? "");
            string val;
            if (field_select_multi)
            {
                List<string> sel = new List<string>(SplitMulti(cur));
                if (sel.Contains(opt)) sel.RemoveAll(s => s == opt);
                else sel.Add(opt);
                val = string.Join(";", sel.ToArray());
            }
            else
            {
                val = opt;
            }
            SetFieldValue(node, fd.name, val);
            relayout_pending = true;
            RefreshNodeSummary(node);
            RefreshPinValues();
            if (field_select_refresh != null)
                field_select_refresh();
            if (field_select_multi)
                RebuildFieldSelectList();
            else
                CloseFieldSelectPopup();
        }

        /// <summary>自定义输入：单击字段=直接使用该值；多选字段=追加一个自定义项</summary>
        private void OnFieldSelectUseCustom()
        {
            if (field_select_custom == null)
                return;
            string v = field_select_custom.text;
            if (string.IsNullOrEmpty(v))
                return;
            if (field_select_commit != null)
            {
                //卡牌字段多选：自定义项加入选中集合并回调（如自建种族/关键词）
                if (field_select_pending == null)
                    field_select_pending = new List<string>();
                if (!field_select_pending.Contains(v))
                    field_select_pending.Add(v);
                field_select_commit(new List<string>(field_select_pending));
                field_select_custom.text = "";
                RebuildFieldSelectList();
                return;
            }
            if (field_select_fd == null || field_select_node == null)
                return;
            FieldDef fd = field_select_fd;
            GraphNode node = field_select_node;
            if (field_select_multi)
            {
                List<string> sel = new List<string>(SplitMulti(GetFieldValue(node, fd.name, fd.def ?? "")));
                if (!sel.Contains(v)) sel.Add(v);
                SetFieldValue(node, fd.name, string.Join(";", sel.ToArray()));
                field_select_custom.text = "";
            }
            else
            {
                SetFieldValue(node, fd.name, v);
            }
            relayout_pending = true;
            RefreshNodeSummary(node);
            RefreshPinValues();
            if (field_select_refresh != null)
                field_select_refresh();
            if (field_select_multi)
                RebuildFieldSelectList();
            else
                CloseFieldSelectPopup();
        }

        private void EnsureFieldSelectPopup()
        {
            if (field_select_popup != null)
                return;
            Font font = TabFont();

            //清掉上次构建中途失败留下的半成品：它会挡住点击、而且因为字段不为 null 永远无法重建
            Transform stale_popup = transform.Find("FieldSelectPopup");
            if (stale_popup != null)
                Destroy(stale_popup.gameObject);

            //注意：本方法在**全部构建成功后**才把对象赋给 field_select_popup（见方法末尾）。
            //中途抛异常时字段保持 null → 下次点击会重新构建，不会留下"点不出来也关不掉"的半成品弹层。
            GameObject root_go = new GameObject("FieldSelectPopup", typeof(RectTransform));
            RectTransform root = root_go.GetComponent<RectTransform>();
            root.SetParent(transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            Image back = root_go.AddComponent<Image>();
            back.color = new Color(0, 0, 0, 0.55f);
            Button back_btn = root_go.AddComponent<Button>();
            back_btn.targetGraphic = back;
            back_btn.onClick.AddListener(CloseFieldSelectPopup);

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            RectTransform prt = panel.GetComponent<RectTransform>();
            prt.SetParent(root, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(440, 520);
            Image pimg = panel.AddComponent<Image>();
            pimg.color = new Color(0.12f, 0.12f, 0.15f, 1f);
            Button pbtn = panel.AddComponent<Button>();   // 吞掉点击，避免穿透到遮罩关闭
            pbtn.targetGraphic = pimg;

            field_select_title = MakeText("Title", prt, "选择", 22, TextAnchor.MiddleLeft, font);
            RectTransform trt = field_select_title.rectTransform;
            trt.anchorMin = new Vector2(0, 1);
            trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0.5f, 1);
            trt.anchoredPosition = new Vector2(0, -6);
            trt.sizeDelta = new Vector2(-60, 34);

            RectTransform close = MakeButton("Close", prt, "×", font, CloseFieldSelectPopup);
            close.anchorMin = new Vector2(1, 1);
            close.anchorMax = new Vector2(1, 1);
            close.pivot = new Vector2(1, 1);
            close.anchoredPosition = new Vector2(-6, -6);
            close.sizeDelta = new Vector2(34, 34);

            GameObject scroll_go = new GameObject("Scroll", typeof(RectTransform));
            RectTransform srt = scroll_go.GetComponent<RectTransform>();
            srt.SetParent(prt, false);
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(12, 60);
            srt.offsetMax = new Vector2(-12, -46);
            ScrollRect scroll = scroll_go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            GameObject view_go = new GameObject("Viewport", typeof(RectTransform));
            RectTransform vrt = view_go.GetComponent<RectTransform>();
            vrt.SetParent(srt, false);
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            view_go.AddComponent<RectMask2D>();
            scroll.viewport = vrt;

            GameObject content_go = new GameObject("Content", typeof(RectTransform));
            RectTransform crt = content_go.GetComponent<RectTransform>();
            crt.SetParent(vrt, false);
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0, 0);
            VerticalLayoutGroup vlg = content_go.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 3;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            ContentSizeFitter csf = content_go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            field_select_list = crt;

            GameObject input_go = new GameObject("CustomInput", typeof(RectTransform), typeof(Image), typeof(TMPro.TMP_InputField));
            RectTransform irt = input_go.GetComponent<RectTransform>();
            irt.SetParent(prt, false);
            irt.anchorMin = new Vector2(0, 0);
            irt.anchorMax = new Vector2(1, 0);
            irt.pivot = new Vector2(0.5f, 0);
            irt.anchoredPosition = new Vector2(-44, 14);
            irt.sizeDelta = new Vector2(-104, 34);
            input_go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
            TMPro.TMP_InputField inp = input_go.GetComponent<TMPro.TMP_InputField>();
            inp.targetGraphic = input_go.GetComponent<Image>();
            //同上：给 textViewport（Text Area），否则拖动/选字会 NullReferenceException；并让光标可见
            GameObject area_go = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            RectTransform art = area_go.GetComponent<RectTransform>();
            art.SetParent(irt, false);
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(10f, 1f);
            art.offsetMax = new Vector2(-10f, -1f);
            TMP_Text itxt = MakeText("Text", art, "", 18, TextAnchor.MiddleLeft, font);
            SetStretchRect(itxt.rectTransform, 0, 0, 0, 0);
            itxt.richText = false;
            itxt.raycastTarget = false;
            inp.textViewport = art;
            inp.textComponent = itxt;
            inp.caretColor = Color.white;
            inp.enabled = false;    //同上：重启一次才能让 TMP 建出插入光标
            inp.enabled = true;
            TMP_Text ph = MakeText("Placeholder", irt, "自定义值…", 18, TextAnchor.MiddleLeft, font);
            SetStretchRect(ph.rectTransform, 10, 0, 10, 0);
            ph.color = new Color(1f, 1f, 1f, 0.4f);
            ph.raycastTarget = false;
            inp.placeholder = ph;
            field_select_custom = inp;

            RectTransform use = MakeButton("Use", prt, "使用", font, OnFieldSelectUseCustom);
            use.anchorMin = new Vector2(1, 0);
            use.anchorMax = new Vector2(1, 0);
            use.pivot = new Vector2(1, 0);
            use.anchoredPosition = new Vector2(-14, 14);
            use.sizeDelta = new Vector2(76, 34);

            root_go.SetActive(false);
            field_select_popup = root_go;   //构建成功才赋值（见方法开头注释：失败时保持 null，下次重建）
        }

        /// <summary>新建文本：统一用 TMP（旧版 UGUI Text 太糊），自动应用节点字体</summary>
        private TMP_Text MakeText(string name, Transform parent, string txt, int size, TextAnchor anchor, Font font)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            ApplyNodeFont(t, string.IsNullOrEmpty(txt) ? FontProbe : txt + FontProbe);
            t.text = txt;
            t.fontSize = size;
            t.alignment = ToTmpAlignment(anchor);
            t.color = Color.white;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private RectTransform MakeButton(string name, Transform parent, string txt, Font font, Action onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.18f);
            Button btn = go.GetComponent<Button>();
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            TMP_Text t = MakeText("Text", rt, txt, 18, TextAnchor.MiddleCenter, font);
            t.raycastTarget = false;
            return rt;
        }

        /// <summary>修正 UGUI Dropdown 展开模板：按选项数撑开 Content 与模板高度（避免底部被裁切、可滚动到底）</summary>
        private static void FixDropdownTemplate(Dropdown dd)
        {
            if (dd == null || dd.template == null)
                return;
            RectTransform tpl = dd.template;
            RectTransform content = tpl.Find("Viewport/Content") as RectTransform;
            if (content == null)
                return;
            RectTransform item = content.Find("Item") as RectTransform;
            float item_h = (item != null && item.rect.height > 1f) ? item.rect.height : 28f;
            int n = dd.options != null ? dd.options.Count : 0;
            float content_h = item_h * n + 8f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, content_h);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            tpl.sizeDelta = new Vector2(tpl.sizeDelta.x, Mathf.Min(content_h + 8f, 360f));
            ScrollRect sr = tpl.GetComponent<ScrollRect>();
            if (sr != null)
                sr.verticalNormalizedPosition = 1f;
        }

        private static void SetStretchRect(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>卡牌定义选择器（CardDefine 输入口）：下拉列出当前卡池的所有卡牌（显示「标题 (id)」，存储 id）</summary>
        private void CreateFieldCardSelect(GraphNode node, FieldDef fd, string current)
        {
            if (node_field_dropdown_template == null)
                return;
            GameObject inst = Instantiate(node_field_dropdown_template, node_field_area);
            inst.name = "Field_" + fd.name;
            inst.SetActive(true);

            Text label = inst.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.text = fd.display_name;

            //选项动态生成：当前卡池全部卡牌（字段定义里不预设，因为随编辑的卡池变化）
            List<string> ids = new List<string>();
            List<string> displays = new List<string>();
            if (pool != null && pool.cards != null)
            {
                foreach (CardCustomData c in pool.cards)
                {
                    if (c == null || string.IsNullOrEmpty(c.id))
                        continue;
                    ids.Add(c.id);
                    displays.Add((string.IsNullOrEmpty(c.title) ? c.id : c.title) + " (" + c.id + ")");
                }
            }

            Dropdown dd = inst.GetComponentInChildren<Dropdown>(true);
            if (dd == null)
                return;
            dd.ClearOptions();
            dd.AddOptions(displays);
            int idx = ids.IndexOf(current);
            dd.value = idx < 0 ? 0 : idx;
            dd.RefreshShownValue();
            FixDropdownTemplate(dd);
            dd.onValueChanged.AddListener((v) =>
            {
                string val = (v >= 0 && v < ids.Count) ? ids[v] : "";
                SetFieldValue(node, fd.name, val);
                RefreshNodeSummary(node);
                RefreshPinValues();
            });
        }

        /// <summary>增益定义选择器（BuffDefine 输入口）：下拉列出增益池全部 BuffData（显示「标题 (id)」，存储 id）</summary>
        private void CreateFieldBuffSelect(GraphNode node, FieldDef fd, string current)
        {
            if (node_field_dropdown_template == null)
                return;
            GameObject inst = Instantiate(node_field_dropdown_template, node_field_area);
            inst.name = "Field_" + fd.name;
            inst.SetActive(true);

            Text label = inst.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.text = fd.display_name;

            //选项动态生成：BuffPoolIO 增益池（字段定义里不预设，随增益编辑器保存的池变化）
            List<string> ids = new List<string>();
            List<string> displays = new List<string>();
            foreach (BuffData b in BuffPoolIO.GetAll())
            {
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                ids.Add(b.id);
                displays.Add((string.IsNullOrEmpty(b.title) ? b.id : b.title) + " (" + b.id + ")");
            }

            Dropdown dd = inst.GetComponentInChildren<Dropdown>(true);
            if (dd == null)
                return;
            dd.ClearOptions();
            dd.AddOptions(displays);
            int idx = ids.IndexOf(current);
            dd.value = idx < 0 ? 0 : idx;
            dd.RefreshShownValue();
            FixDropdownTemplate(dd);
            dd.onValueChanged.AddListener((v) =>
            {
                string val = (v >= 0 && v < ids.Count) ? ids[v] : "";
                SetFieldValue(node, fd.name, val);
                RefreshNodeSummary(node);
                RefreshPinValues();
            });
        }

        /// <summary>战斗按钮选择器（button_id 字段）：下拉列出按钮池全部按钮（显示「标题 (id)」，存储 id）。
        /// 按钮配置随按钮编辑器保存的 buttons.json 变化，每次打开参数区都重新取当前列表。</summary>
        private void CreateFieldButtonSelect(GraphNode node, FieldDef fd, string current)
        {
            if (node_field_dropdown_template == null)
                return;
            GameObject inst = Instantiate(node_field_dropdown_template, node_field_area);
            inst.name = "Field_" + fd.name;
            inst.SetActive(true);

            Text label = inst.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.text = fd.display_name;

            //选项动态生成：BattleButtonIO 按钮池
            List<string> ids = new List<string>();
            List<string> displays = new List<string>();
            foreach (BattleButtonData b in BattleButtonIO.GetAll())
            {
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                ids.Add(b.id);
                displays.Add((string.IsNullOrEmpty(b.title) ? b.id : b.title) + " (" + b.id + ")");
            }

            Dropdown dd = inst.GetComponentInChildren<Dropdown>(true);
            if (dd == null)
                return;
            dd.ClearOptions();
            dd.AddOptions(displays);
            int idx = ids.IndexOf(current);
            dd.value = idx < 0 ? 0 : idx;
            dd.RefreshShownValue();
            FixDropdownTemplate(dd);
            dd.onValueChanged.AddListener((v) =>
            {
                string val = (v >= 0 && v < ids.Count) ? ids[v] : "";
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

        private static string[] SplitMulti(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new string[0];
            return value.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>字段值变化后刷新画布上节点摘要文本（显示最新参数）</summary>
        private void RefreshNodeSummary(GraphNode node)
        {
            if (node == null || node_rows == null)
                return;
            if (node_rows.TryGetValue(node.id, out RectTransform rect))
            {
                TMP_Text desc = rect.Find("DescText")?.GetComponent<TMP_Text>();
                if (desc != null)
                    desc.gameObject.SetActive(false);   //说明行已移除（改为节点「?」弹窗）
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

        /// <summary>删除指定节点及其所有连线（工具栏按钮与节点自带 × 按钮共用；Ctrl+Z 可撤销）</summary>
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
                desc.gameObject.SetActive(false);   //说明行已移除（改为节点「?」弹窗）
            //内联参数随收起/展开显隐（端口同名字段：有连线时仍保持隐藏）
            RefreshPinFieldVisibility();
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
                TMP_Text t = badge.GetComponentInChildren<TMP_Text>(true);
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

            TMP_Text txt = CreateStretchTextChild(go.transform, 14, Color.white);   //Text 放独立子对象（Graphic 唯一限制）
            if (txt != null)
            {
                txt.alignment = TextAlignmentOptions.Center;
                txt.text = "×0";
            }
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

            TMP_Text tip = EnsureHoverTooltip();
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
        private TMP_Text EnsureHoverTooltip()
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
            TMP_Text txt = CreateStretchTextChild(go.transform, 13, Color.white);
            if (txt != null)
                txt.alignment = TextAlignmentOptions.Center;

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

            //可多入的数据口（is_array，如 112004/112005 的 值 参数口）不替换旧连线：允许同口接多条取值线；
            //其余入线已占用则移除旧连线（数据 + UI 实例，避免替换后旧线残留画布造成"一入口多线"假象）
            bool multi_input = tpin != null && tpin.is_array
                && tpin.type != NodeValueType.Flow && tpin.type != NodeValueType.None;
            if (!multi_input)
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
            inst.transform.SetAsLastSibling();
            NodeLink nl = inst.GetComponent<NodeLink>();
            if (nl == null)
                nl = inst.AddComponent<NodeLink>();
            nl.Setup(inst.GetComponent<RectTransform>());
            nl.SetEndpoints(drag_from_pin, drag_from_pin);
            temp_link = nl;
        }

        private void DrawTempLink(Vector2 a, Vector2 b)
        {
            if (temp_link == null)
                return;
            temp_link.Draw(a, b);   //拖拽中同样走贝塞尔曲线
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
                //默认视图按 1/NodeScale 显示：节点内部虽缩小，但进来时的观感与原来一致
                float s = NodeScale > 0.01f ? 1f / NodeScale : 1f;
                canvas_content.localScale = new Vector3(s, s, 1f);
            }
            //放宽缩放范围（场景里的 zoom_max 可能偏小，导致"放大都放不大"）
            if (graph_canvas != null)
            {
                graph_canvas.zoom_min = 0.2f;
                graph_canvas.zoom_max = 5f;
            }
        }

        // ---------------- 保存/测试/关闭 ----------------

        private void OnSave()
        {
            //关键词模式：校验后直接写回关键词资产（无卡池文件/卡牌属性）
            if (editing_keyword != null)
            {
                List<GraphIssue> kissues = ValidateGraph();
                if (kissues.Count > 0)
                {
                    ApplyValidationMarks();
                    SetStatus("无法保存：规则图有 " + kissues.Count + " 处缺输入，请先补全（红「!」节点）");
                    return;
                }
                editing_rule.graph = graph;
                if (string.IsNullOrEmpty(graph.name))
                    graph.name = "keyword_" + editing_keyword.id;
                if (keyword_asset_saver != null)
                    keyword_asset_saver.Invoke(editing_keyword);   //Editor 程序集注册：写资产并 SaveAssets
                SetStatus("已保存关键词规则图: " + editing_keyword.title);
                return;
            }

            if (editing_buff != null)
            {
                List<GraphIssue> bissues = ValidateGraph();
                if (bissues.Count > 0)
                {
                    ApplyValidationMarks();
                    SetStatus("无法保存：规则图有 " + bissues.Count + " 处缺输入，请先补全（红「!」节点）");
                    return;
                }
                editing_buff.graph = graph;
                if (string.IsNullOrEmpty(graph.name))
                    graph.name = "buff_" + editing_buff.id;
                BuffPoolIO.SaveAll();
                SetStatus("已保存增益效果图: " + editing_buff.GetTitle());
                return;
            }

            //按钮模式：校验后直接写回全局按钮配置（一图多按钮，保存写盘 buttons.json）
            if (editing_button_config != null)
            {
                List<GraphIssue> cissues = ValidateGraph();
                if (cissues.Count > 0)
                {
                    ApplyValidationMarks();
                    SetStatus("无法保存：规则图有 " + cissues.Count + " 处缺输入，请先补全（红「!」节点）");
                    return;
                }
                editing_button_config.graph = graph;
                if (string.IsNullOrEmpty(graph.name))
                    graph.name = "battle_buttons";
                BattleButtonIO.SaveAll();
                SetStatus("已保存按钮图（写回 buttons.json）");
                return;
            }

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
            SyncLegacyGraphField();

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

        /// <summary>「模拟测试」：与卡牌编辑页同款——先把当前规则图保存进卡牌并同步运行时数据，
        /// 再跳转人机战斗（我方卡组由当前卡组成一整套，AI 用随机初始卡池，开局双方法力直接为上限）</summary>
        private void OnTest()
        {
            if (editing_keyword != null)
            {
                SetStatus("关键词不支持单独模拟测试，请把关键词挂到卡牌上后在对局验证");
                return;
            }

            if (editing_buff != null)
            {
                SetStatus("增益效果图不支持单独模拟测试，请用规则图「添加增益(206001)」挂到卡牌上后在对局验证");
                return;
            }

            if (card == null || pool == null)
            {
                SetStatus("没有可测试的卡牌");
                return;
            }

            //带规则图时先做与保存同规的校验+写回（否则进对战跑的还是上一次保存的图）
            if (graph != null && graph.nodes.Count > 0)
            {
                List<GraphIssue> issues = ValidateGraph();
                if (issues.Count > 0)
                {
                    ApplyValidationMarks();
                    SetStatus("无法测试：规则图有 " + issues.Count + " 处缺输入，请先补全（红「!」节点）");
                    return;
                }
                ReadForm();
                graph.name = string.IsNullOrEmpty(card.title) ? "NewGraph" : card.title;
                SyncLegacyGraphField();
            }

            //确保该卡运行时数据已注册且为最新（含规则图编译的能力）
            CardData data = CardData.Get(card.id);
            if (data == null)
            {
                data = CardPoolIO.BuildCardData(card);
                if (data != null)
                    CardPoolIO.RegisterCard(data);
            }
            else
            {
                CardPoolIO.UpdateCardData(card); //同步最新属性与规则图能力
            }
            if (data == null)
            {
                SetStatus("无法构建测试卡牌数据，请先保存卡池");
                return;
            }

            //我方卡组：一整套全是这张卡
            int deck_size = GameplayData.Get().deck_size;
            UserDeckData test_deck = new UserDeckData();
            test_deck.tid = "test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            test_deck.title = "测试 - " + (string.IsNullOrEmpty(card.title) ? data.title : card.title);
            test_deck.hero = GetDefaultHero();
            test_deck.cards = new UserCardData[]
            {
                new UserCardData { tid = data.id, variant = VariantData.GetDefault().id, quantity = deck_size }
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
                    TMP_Text txt = t.GetComponent<TMP_Text>();
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
                    TMP_Text txt = t.GetComponent<TMP_Text>();
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
            if (editing_button_config != null)
            {
                //按钮模式：返回卡牌编辑器（按钮编辑器内嵌其中），刷新按钮列表
                CardEditorPanel panel = CardEditorPanel.Get();
                if (panel == null)
                    panel = FindObjectOfType<CardEditorPanel>(true);
                if (panel != null)
                {
                    panel.Show();
                    panel.NotifyButtonGraphClosed();
                }
                return;
            }
            if (editing_buff != null)
            {
                //增益模式：返回增益编辑器（BuffPanel）
                BuffPanel panel = BuffPanel.Get();
                if (panel == null)
                    panel = FindObjectOfType<BuffPanel>(true);
                if (panel != null)
                {
                    panel.Show();
                    panel.NotifyGraphClosed();
                }
                return;
            }
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
            {
                //入口目标配置字段(目标类型/报错)与 标签/优先级 不进节点摘要（控件已显示，摘要里再拼一次会显得重复）
                if (f.name.StartsWith("target_") || f.name == "priority"
                    || f.name == "tags" || f.name == "tag_list")
                    continue;
                //运算节点的编号输入槽（arg1…/value1…）：控件已显示在手填框里，不进摘要
                if (IsParamSlotNode(node)
                    && (SlotNumberOf(f.name, "arg") > 0 || SlotNumberOf(f.name, "value") > 0))
                    continue;
                s += "  " + f.name + "=" + f.value;
            }
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

        private static void SetInput(TMP_InputField field, string val)
        {
            if (field != null)
                field.text = val ?? "";
        }

        private static string GetInput(TMP_InputField field, string def)
        {
            if (field == null)
                return def;
            return string.IsNullOrEmpty(field.text) ? def : field.text;
        }

        private static int GetInputInt(TMP_InputField field, int def)
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
