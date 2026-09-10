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
        private bool card_extra_built = false;  // 扩展行是否已构建（每次打开面板构建一次）
        private const float MAX_AUDIO_SECONDS = 10f;   //导入音效时长上限（秒）

        // ---- 全页字体统一 + 右侧栏 Tab（运行时构建）----
        private bool tmp_ui_done = false;                        // 旧版 Text → TMP 是否已跑过
        private bool ui_setup_pending = false;                   // 等面板激活后执行 TMP 迁移/Tab 构建
        private readonly HashSet<GameObject> tmp_converted = new HashSet<GameObject>();
        private RectTransform prop_area_rt;                      // 卡牌参数区（占满右列）
        private RectTransform lib_area_rt;                       // 节点库区（占满右列）
        private TMP_Text tab_prop_text;                          // 「卡牌参数」Tab 文本
        private TMP_Text tab_lib_text;                           // 「节点库」Tab 文本
        private bool right_tabs_built = false;
        private bool right_tab_prop = false;                     // 当前是否在「卡牌参数」页
        public TMP_InputField input_mana;    // 费用（TMP）
        public TMP_InputField input_attack;  // 攻击（TMP）
        public TMP_InputField input_hp;      // 生命（TMP）
        public TMP_InputField input_text;    // 卡牌文本（TMP）
        public TMP_InputField input_desc;    // 描述（TMP）
        public Toggle toggle_deckbuilding;   // 可组卡
        public TMP_InputField input_cost;    // 购买价（TMP）
        public Image art_preview;            // 卡面图片预览
        public Button btn_pick_art;          // 选择卡面图片
        public RectTransform art_full_row;   // 面板图片行（法术/奥秘隐藏）
        public Image art_full_preview;       // 面板图片预览
        public Button btn_pick_full_art;     // 选择面板图片
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
        private enum FieldEditType { Input, Dropdown, Toggle, CardSelect, BuffSelect, ButtonSelect, MultiOptions, KeywordSelect }

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
            act.fields.Add(new FieldDef("target_type", "目标类型", FieldEditType.Dropdown, new string[] { "无", "角色", "英雄" }, "无"));
            act.fields.Add(new FieldDef("target_side", "目标归属", FieldEditType.Dropdown, new string[] { "任意", "敌方", "友方" }, "任意"));
            act.pins.Add(new PinDef("targetCondition", "目标1条件", NodeValueType.Boolean, false));
            act.pins.Add(new PinDef("out", "动作", NodeValueType.Flow, true));
            //对齐醉梦传说：入口不再平铺自身/目标/玩家数据口，事件数据走「当前事件→获取变量」
            presets.Add(act);

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
            //动作（执行层已实现）
            "202001",   //造成伤害（单目标）
            "202041",   //造成伤害或法伤（多目标/法伤开关，卡池主流）
            "202016",   //消灭
            "202013",   //治疗目标卡牌（执行器仍兼容 202039/202047 变体，但库内只放这一个避免同名重复）
            //第二批图事件后续补充的已接入节点（第 6 批）
            "112001",   //布尔常量
            "112003",   //整数常量
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
                //增益家族 v1（206002/106003/106004/206003/106005/106006）：Buff 引用口不生成（v1 无 Buff 值通道，
                //以 buff_id 下拉字段引用增益定义）；106004/206003 没有 card 口，下方补一个；
                //106005/106006 的 Buff 口跳过（取定义/取集合留待 Buff 值通道落地后实现）
                if ((p.action == "206002" || p.action == "106004" || p.action == "206003" || p.action == "106003"
                        || p.action == "106005" || p.action == "106006")
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
                //ActionNode 型"输入"实为分支/循环的动作出口（zmcs 控制流节点规范）→ 转成执行流输出口
                if (ip.type == NodeValueType.ActionNode)
                {
                    p.pins.Add(new PinDef(ip.name, ip.display_name, NodeValueType.Flow, true));
                    continue;
                }
                p.pins.Add(new PinDef(ip.name, ip.display_name, ip.type, false, ip.is_array));
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
                    p.fields.Add(new FieldDef(ip.name, ip.display_name, FieldEditType.Input, null, ""));
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
                    //105001/105005 卡牌所在牌堆判断：要能选全部区域（含战场/装备/奥秘，运行时只判真区域）
                    //101017 获取牌堆只能抓 牌库/手牌/墓地（TCG2 可抓取的实体堆）
                    if (p.action == "105001" || p.action == "105005")
                        p.fields.Add(EnumField(ip.name, ip.display_name,
                            new string[] { "手牌", "牌库", "墓地", "战场", "装备", "奥秘" }, "战场"));
                    else
                        p.fields.Add(EnumField(ip.name, ip.display_name, new string[] { "牌库", "手牌", "墓地" }, "牌库"));
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
            //202044 复制卡牌：zmcs 的 Pile 引用口 v1 换成目标牌堆下拉
            if (p.action == "202044")
                p.fields.Add(EnumField("targetPile", "目标牌堆", new string[] { "手牌", "战场", "牌库" }, "手牌"));
            //103002 获取卡牌定义：DefineReference 引用口的替代表达——卡牌选择下拉（选当前卡池的卡牌）
            if (p.action == "103002")
                p.fields.Add(new FieldDef("cardRef", "卡牌定义", FieldEditType.CardSelect, null, ""));
            //事件家族（108002 获取变量 / 208001 设置变量）变量名用下拉；108003 转换事件类型用事件类型下拉
            if (p.action == "108002" || p.action == "208001")
                p.fields.Add(EnumField("varName", "变量名",
                    new string[] { "卡牌", "玩家", "来源", "数值", "目标", "标签" }, "卡牌"));
            if (p.action == "108003")
                p.fields.Add(EnumField("eventReference", "事件类型",
                    new string[] { "打出", "伤害", "治疗", "死亡", "弃牌", "装备", "抽卡", "回合", "游戏" }, "死亡"));
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

            //面板激活后的第一帧：执行整页 TMP 迁移 + 右侧栏 Tab 构建（未激活时 AddComponent 会失败）
            if (ui_setup_pending && gameObject.activeInHierarchy)
            {
                ui_setup_pending = false;
                EnsureTmpUI();
                EnsureRightTabs();
                if (card != null)
                {
                    RefreshForm();             //把当前卡的值填进新换上的 TMP 控件
                    RefreshCardExtraFields();
                }
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
                MigratePins(n);
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
                MigratePins(n);
            RebuildCanvas();
            ResetView();
            RefreshEffectTabs();
            SetStatus("当前效果图：" + e.name);
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

        /// <summary>构建/刷新效果图 tab 栏（运行时创建；非卡模式隐藏）</summary>
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

            for (int i = effect_tab_bar.childCount - 1; i >= 0; i--)
            {
                Transform c = effect_tab_bar.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            Font font = TabFont();
            float x = 0f;
            for (int i = 0; i < effs.Count; i++)
            {
                CardEffectData e = effs[i];
                e.name = SuggestedEffectName(e.graph, i);
                CreateEffectTabButton(e.name, i, i == effect_index, font, x);
                x += 132f;
            }
            CreateEffectTabButton("+ 新效果", -1, false, font, x);
            CreateEffectTabButton("删除当前", -2, false, font, x + 132f);
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

        private void CreateEffectTabButton(string label, int index, bool active, Font font, float x)
        {
            GameObject go = new GameObject("EffectTab", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(effect_tab_bar, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(124, 0);

            Image img = go.GetComponent<Image>();
            img.color = active ? new Color(0.2f, 0.55f, 0.85f, 1f) : new Color(1f, 1f, 1f, 0.15f);

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

            GameObject txt_go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            RectTransform trt = txt_go.GetComponent<RectTransform>();
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4, 0);
            trt.offsetMax = new Vector2(-4, 0);
            Text t = txt_go.GetComponent<Text>();
            t.text = label;
            t.font = font;
            t.fontSize = 16;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
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
                MigratePins(n);
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
                MigratePins(n);
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
                MigratePins(n);
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
            if (title_text != null)
                title_text.text = "规则编辑 - " + (string.IsNullOrEmpty(card.title) ? "未命名" : card.title);
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

            //1) 种族：隐藏旧单选下拉，同位置换成多选按钮（TMP）
            dropdown_trait.gameObject.SetActive(false);
            txt_trait_select = CreateCardSelectButton(field, "TraitSelect", OnClickTraitSelect);

            //1.5) 类型/阵营/稀有度：旧单选下拉 → TMP 选择按钮（统一风格，弹层选择）
            txt_type_select = ReplaceDropdownWithSelect(dropdown_type, "TypeSelect", OnClickTypeSelect);
            txt_team_select = ReplaceDropdownWithSelect(dropdown_team, "TeamSelect", OnClickTeamSelect);
            txt_rarity_select = ReplaceDropdownWithSelect(dropdown_rarity, "RaritySelect", OnClickRaritySelect);

            //2) 关键词：复制种族行（沿用标签/行高/布局），插在种族行下面
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

            //3) 音效行：各加「▶ 试听」
            AddAudioPreviewButton(input_audio_spawn, 0);
            AddAudioPreviewButton(input_audio_attack, 1);
            AddAudioPreviewButton(input_audio_death, 2);
            AddAudioPreviewButton(input_audio_damage, 3);

            //4) 短字段两两一行（长字段保持整行）
            PackShortPropRows(content);

            card_extra_built = true;
        }

        /// <summary>短字段两两一行：把相邻短字段行并排（阵营|稀有度、费用|攻击、可组卡|购买价）。
        /// 运行时重排属性区，无需重跑生成工具；名称/卡牌文本/描述/种族/关键词/图片/音效保持整行。</summary>
        private void PackShortPropRows(RectTransform content)
        {
            if (content == null)
                return;
            string[][] pairs = new string[][]
            {
                new string[] { "类型", "阵营" },
                new string[] { "稀有度", "费用" },
                new string[] { "攻击", "生命" },
            };
            foreach (string[] pair in pairs)
            {
                RectTransform a = FindPropRow(content, pair[0]);
                RectTransform b = FindPropRow(content, pair[1]);
                if (a == null || b == null || a == b)
                    continue;
                int idx = a.GetSiblingIndex();
                RectTransform pack = CreatePackRow(content);
                MakeFlexible(a);
                MakeFlexible(b);
                a.SetParent(pack, false);
                b.SetParent(pack, false);
                pack.SetSiblingIndex(Mathf.Clamp(idx, 0, content.childCount - 1));
            }
        }

        /// <summary>新建横向「打包行」（两个字段并排，行高与普通属性行一致）</summary>
        private static RectTransform CreatePackRow(RectTransform content)
        {
            GameObject go = new GameObject("PropPackRow", typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(content, false);
            HorizontalLayoutGroup h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.padding = new RectOffset(0, 0, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.minHeight = 40f;
            return rt;
        }

        /// <summary>打包行内的子行：等宽平分 + 固定行高</summary>
        private static void MakeFlexible(RectTransform row)
        {
            LayoutElement le = row.GetComponent<LayoutElement>();
            if (le == null)
                le = row.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredWidth = 0f;
            le.minWidth = 0f;
            le.preferredHeight = 40f;
            le.minHeight = 40f;
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

        /// <summary>音效行加「▶」试听按钮（压缩原输入框/选择按钮宽度腾位置）</summary>
        private void AddAudioPreviewButton(TMP_InputField input, int slot)
        {
            if (input == null)
                return;
            RectTransform field = input.transform.parent as RectTransform;   //AudioRow
            if (field == null)
                return;
            RectTransform input_rt = input.GetComponent<RectTransform>();
            if (input_rt != null)
            {
                input_rt.anchorMin = new Vector2(0, 0);
                input_rt.anchorMax = new Vector2(0.52f, 1);
                input_rt.offsetMin = Vector2.zero;
                input_rt.offsetMax = Vector2.zero;
            }
            RectTransform prt = field.Find("PickAudioBtn") as RectTransform;
            if (prt != null)
            {
                prt.anchorMin = new Vector2(0.55f, 0);
                prt.anchorMax = new Vector2(0.82f, 1);
                prt.offsetMin = Vector2.zero;
                prt.offsetMax = Vector2.zero;
            }

            GameObject go = new GameObject("PlayAudioBtn_" + slot, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(field, false);
            rt.anchorMin = new Vector2(0.85f, 0);
            rt.anchorMax = new Vector2(1f, 1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(0.4f, 0.85f, 0.6f, 0.45f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            int captured = slot;
            btn.onClick.AddListener(() => OnPreviewAudio(captured));
            TMP_Text t = MakeNodeTmpText(rt, "Text", "▶", 14, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);
            t.raycastTarget = false;
        }

        /// <summary>试听音效槽（0打出 1攻击 2死亡 3受伤）：优先用输入框当前文件名（未保存也能听）</summary>
        private void OnPreviewAudio(int slot)
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

        /// <summary>统一字号：区块标题 20、状态条 18、行标签/按钮 15，其余 16</summary>
        private static int NormalizeFontSize(string go_name, int legacy_size)
        {
            if (go_name == "AreaTitle")
                return 20;
            if (go_name == "StatusText")
                return 18;
            if (go_name == "PropLabel" || go_name == "ToggleLabel" || go_name.StartsWith("Btn") || go_name.EndsWith("Btn"))
                return 15;
            return 16;
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
            int size = NormalizeFontSize(go.name, legacy.fontSize);   //统一字号（不再沿用旧版杂乱字号）
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

        /// <summary>把「卡牌参数」与「节点库」从上下各半改为整列互斥 + Tab 切换（显示内容翻倍）。
        /// 运行时构建，无需重跑生成工具。</summary>
        private void EnsureRightTabs()
        {
            if (right_tabs_built)
                return;
            prop_area_rt = transform.Find("PropArea") as RectTransform;
            lib_area_rt = transform.Find("LibArea") as RectTransform;
            if (prop_area_rt == null || lib_area_rt == null)
                return;
            right_tabs_built = true;

            SetRightColumn(prop_area_rt);
            SetRightColumn(lib_area_rt);
            if (node_field_root != null)
                SetRightColumn(node_field_root.transform as RectTransform);

            RectTransform bar = new GameObject("RightTabBar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(transform, false);
            bar.anchorMin = new Vector2(0.77f, 0.855f);
            bar.anchorMax = new Vector2(0.99f, 0.895f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            HorizontalLayoutGroup h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            tab_prop_text = MakeRightTab(bar, "卡牌参数", () => SelectRightTab(true));
            tab_lib_text = MakeRightTab(bar, "节点库", () => SelectRightTab(false));
            bar.SetAsLastSibling();   //保证 Tab 在属性/库面板之上可点

            SelectRightTab(false);    //默认节点库（与旧行为一致）
        }

        /// <summary>右侧栏面板统一矩形：占满整列（内容区在 Tab 栏下方）</summary>
        private static void SetRightColumn(RectTransform rt)
        {
            if (rt == null)
                return;
            rt.anchorMin = new Vector2(0.77f, 0.04f);
            rt.anchorMax = new Vector2(0.99f, 0.85f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private TMP_Text MakeRightTab(RectTransform bar, string label, Action onClick)
        {
            GameObject go = new GameObject("Tab_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(bar, false);
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.12f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            TMP_Text t = MakeNodeTmpText(rt, "Text", label, 18, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 4, 0, 4, 0);
            t.raycastTarget = false;
            return t;
        }

        /// <summary>切换右侧栏页：true=卡牌参数，false=节点库</summary>
        public void SelectRightTab(bool prop)
        {
            right_tab_prop = prop;
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

        /// <summary>节点画布 TMP 字体候选（按优先级）：
        /// ① 场景绑定的 title_text 字体；② 项目 STSONG CJK 字体资产；③ 场景里已实际渲染在用的字体（必然可用）；
        /// ④ TMP 内置 LiberationSans；⑤ 其余。坏资产由 ApplyNodeFont 的 try/catch 自动淘汰。</summary>
        private List<TMP_FontAsset> TmpFontCandidates()
        {
            List<TMP_FontAsset> list = new List<TMP_FontAsset>();
            if (title_text != null && title_text.font != null)
                list.Add(title_text.font);

            TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            List<TMP_FontAsset> others = new List<TMP_FontAsset>();
            foreach (TMP_FontAsset fa in all)
            {
                if (fa == null || list.Contains(fa))
                    continue;
                if (fa.name.Contains("STSONG"))
                    list.Add(fa);
                else
                    others.Add(fa);
            }
            //场景中已在使用中的字体资产（有 TMP 文本引用且非空）优先
            foreach (TextMeshProUGUI t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (t == null || t.font == null || list.Contains(t.font) || others.Contains(t.font))
                    continue;
                others.Insert(0, t.font);
            }
            others.Sort((a, b) =>
                (a.name.Contains("LiberationSans") ? 0 : 1) - (b.name.Contains("LiberationSans") ? 0 : 1));
            foreach (TMP_FontAsset fa in others)
            {
                if (!list.Contains(fa))
                    list.Add(fa);
            }
            return list;
        }

        private TMP_FontAsset m_node_tmp_font;        //成功赋值过的字体缓存
        private readonly HashSet<string> m_bad_fonts = new HashSet<string>();   //赋值失败/缺字过的字体资产名
        private static TMP_FontAsset m_os_font_asset; //运行时用系统字体现做的动态字体资产（全项目共享）

        /// <summary>探测句：涵盖节点标题/说明里最常用的汉字（含此前显示为方块的：开始/生效/获取/所有/随机/筛选等）。
        /// 候选字体必须能渲染其中全部字符才算合格，从根上避免"半个词是方块"的缺字体面。</summary>
        private const string FontProbe = "规则编辑器回合开始结束生效获取友方敌方所有随机筛选目标卡牌属性攻击生命法力值抽牌治疗召唤伤害创建衍生并置入战场简单玩家触发条件类型判断打击消灭";

        /// <summary>实测字体能否渲染探测文本：动态字体会当场补字，补不齐（图盘满/资产损坏）即判不合格</summary>
        private static bool FontCovers(TMP_FontAsset fa, string text)
        {
            if (fa == null || string.IsNullOrEmpty(text))
                return true;
            try
            {
                uint[] missing;
                fa.HasCharacters(text, out missing, false, true);
                return missing == null || missing.Length == 0;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>用项目里导入的中文字体现做动态 TMP 字体资产（图盘按需增长、不会缺字）。
        /// 编辑器下必须用导入的字体文件——OS 动态字体没有资产路径，TMP 动态补字会全部失败（整体变方块）；
        /// 打包环境退回 OS 字体。</summary>
        private TMP_FontAsset CreateOsFontAsset()
        {
            if (m_os_font_asset != null)
                return m_os_font_asset;
            try
            {
                Font f = null;
#if UNITY_EDITOR
                string[] asset_candidates = {
                    "Assets/TcgEngine/Fonts/SimHei.ttf",
                    "Assets/TcgEngine/Fonts/MSYH.TTC",
                    "Assets/TcgEngine/Fonts/STSONG.TTF",
                    "Assets/TcgEngine/Fonts/SIMSUN.TTC",
                };
                foreach (string p in asset_candidates)
                {
                    f = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(p);
                    if (f != null)
                        break;
                }
#endif
                if (f == null)
                    f = Font.CreateDynamicFontFromOSFont(new string[]
                        { "SimHei", "Microsoft YaHei", "微软雅黑", "SimSun", "宋体", "STSong", "华文宋体" }, 24);
                if (f == null)
                    return null;
                //不能用单参重载：其默认 90pt 采样 + 1024 图盘，一页只装得下几十个字，很快塞满出方块。
                //48pt 采样 + 2048 图盘 + 多页支持：一页可容纳两千余字，超出自动加页
                TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(f, 48, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (fa == null)
                    return null;
                fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                m_os_font_asset = fa;
                Debug.Log("节点 TMP：已用「" + f.name + "」现做动态字体资产（48pt/2048/多页）");
                return fa;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("动态 TMP 字体创建失败：" + e.Message);
                return null;
            }
        }

        /// <summary>为 TMP 文本设置画布字体：优先用系统字体（黑体/雅黑）现做的动态字体资产——
        /// 项目里的 SimHei_TMP/STSONG 资产缺字严重（图盘满、多页关闭），不作首选；
        /// 系统字体不可用时再逐个尝试项目字体资产，实测覆盖探测文本才算合格</summary>
        private void ApplyNodeFont(TMP_Text tmp, string probe = null)
        {
            if (tmp == null)
                return;
            string probe_text = string.IsNullOrEmpty(probe) ? FontProbe : (probe + FontProbe);
            TMP_FontAsset original_font = tmp.font;   //全失败时恢复用（防 setter 半途抛异常留下 font/材质不一致的残缺态）
            if (m_node_tmp_font != null)
            {
                try { tmp.font = m_node_tmp_font; return; }
                catch (System.Exception) { m_bad_fonts.Add(m_node_tmp_font.name); m_node_tmp_font = null; }
            }

            //第一优先：系统字体现做动态字体（图盘按需增长、不会缺字）
            TMP_FontAsset os = CreateOsFontAsset();
            if (os != null && !m_bad_fonts.Contains(os.name))
            {
                try
                {
                    tmp.font = os;
                    if (!FontCovers(os, probe_text))
                        throw new System.InvalidOperationException("系统动态字体缺字");
                    m_node_tmp_font = os;
                    Debug.Log("节点 TMP 字体选定：系统动态字体（" + os.name + "）");
                    return;
                }
                catch (System.Exception e)
                {
                    m_bad_fonts.Add(os.name);
                    Debug.LogWarning("系统动态字体不可用，转用项目字体资产：" + e.Message);
                }
            }

            foreach (TMP_FontAsset fa in TmpFontCandidates())
            {
                if (fa == null || m_bad_fonts.Contains(fa.name))
                    continue;
                try
                {
                    tmp.font = fa;
                }
                catch (System.Exception e)
                {
                    m_bad_fonts.Add(fa.name);
                    Debug.LogWarning("节点 TMP 字体「" + fa.name + "」赋值失败，已跳过：" + e.Message);
                    continue;
                }
                if (!FontCovers(fa, probe_text))
                {
                    m_bad_fonts.Add(fa.name);
                    Debug.LogWarning("节点 TMP 字体「" + fa.name + "」缺字（无法渲染探测句全部汉字），已跳过");
                    continue;
                }
                m_node_tmp_font = fa;
                Debug.Log("节点 TMP 字体选定：" + fa.name);
                return;
            }
            //所有候选都失败：若 setter 半途抛异常导致 sharedMaterial 为 null，后续排版（preferredWidth 等）
            //会在 TMP 内部空引用，恢复原字体保持 font/材质一致，界面退回旧字体显示
            try
            {
                if (tmp.fontSharedMaterial == null && original_font != null)
                    tmp.font = original_font;
            }
            catch (System.Exception) { }
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

            //清除旧项（保留模板）
            for (int i = node_lib_content.childCount - 1; i >= 0; i--)
            {
                Transform child = node_lib_content.GetChild(i);
                if (child != null && child.gameObject != node_lib_template)
                    Destroy(child.gameObject);
            }

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
            TMP_Text txt = go.AddComponent<TextMeshProUGUI>();
            ApplyNodeFont(txt);
            txt.fontSize = 13;
            txt.color = new Color(0.9f, 1f, 1f, 1f);
            txt.alignment = TextAlignmentOptions.Center;
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

            RectTransform close = MakeButton("Close", prt, "✕", font, CloseNodeHelpPopup);
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
            NodePreset preset = FindPreset(node.type, node.action);
            if (preset == null || preset.fields == null || preset.fields.Count == 0)
                return;
            int rows = preset.fields.Count;
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
                FieldDef fd = preset.fields[i];
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
            RefreshPinFieldVisibility();
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
            else if (fd.edit == FieldEditType.Dropdown || fd.edit == FieldEditType.MultiOptions)
                CreateInlineSelect(ctl, node, fd, current, fd.edit == FieldEditType.MultiOptions);
            else
                CreateInlineInput(ctl, node, fd, current);
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
            TMP_Text t = MakeNodeTmpText(rt, "Text", current, 12, TextAlignmentOptions.Left);
            SetStretchRect(t.rectTransform, 5, 0, 5, 0);
            inp.textComponent = t;
            inp.text = current;
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
            TMP_Text t = MakeNodeTmpText(rt, "Text", "✔", 12, TextAlignmentOptions.Center);
            SetStretchRect(t.rectTransform, 0, 0, 0, 0);

            bool on = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
            img.color = on ? new Color(0.25f, 0.6f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.15f);
            t.text = on ? "✔" : "";
            btn.onClick.AddListener(() =>
            {
                bool cur = string.Equals(GetFieldValue(node, fd.name, fd.def ?? ""), "true", StringComparison.OrdinalIgnoreCase);
                bool nv = !cur;
                SetFieldValue(node, fd.name, nv ? "true" : "false");
                img.color = nv ? new Color(0.25f, 0.6f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.15f);
                t.text = nv ? "✔" : "";
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
            NodePreset preset = FindPreset(node.type, node.action);
            if (preset != null && preset.fields != null)
            {
                //与输入口同名的字段直接画在该端口行右侧（不额外占行）；其余字段各占一行
                foreach (FieldDef fd in preset.fields)
                {
                    if (fd != null && !string.IsNullOrEmpty(fd.name) && !FieldHasInputPin(node, fd.name))
                        field_rows++;
                }
            }
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

        /// <summary>预设里是否有同名字段（输入口是否该画成「端口名 + 行内控件」）</summary>
        private static bool HasPresetField(GraphNode node, string pin_name)
        {
            if (node == null || string.IsNullOrEmpty(pin_name))
                return false;
            NodePreset preset = FindPreset(node.type, node.action);
            if (preset == null || preset.fields == null)
                return false;
            foreach (FieldDef fd in preset.fields)
            {
                if (fd != null && fd.name == pin_name)
                    return true;
            }
            return false;
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

            field_select_popup = new GameObject("FieldSelectPopup", typeof(RectTransform));
            RectTransform root = field_select_popup.GetComponent<RectTransform>();
            root.SetParent(transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            Image back = field_select_popup.AddComponent<Image>();
            back.color = new Color(0, 0, 0, 0.55f);
            Button back_btn = field_select_popup.AddComponent<Button>();
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

            RectTransform close = MakeButton("Close", prt, "✕", font, CloseFieldSelectPopup);
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
            TMP_Text itxt = MakeText("Text", irt, "", 18, TextAnchor.MiddleLeft, font);
            SetStretchRect(itxt.rectTransform, 10, 0, 10, 0);
            itxt.richText = false;
            itxt.raycastTarget = false;
            inp.textComponent = itxt;
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

            field_select_popup.SetActive(false);
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

            //设置对战参数并跳转人机战斗
            GameClient.player_settings.deck = test_deck;
            GameClient.ai_settings.deck = new UserDeckData(ai_data);
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
                //入口目标配置字段(归属/范围/优先级/报错)不进节点摘要，避免刷屏英文键名
                if (f.name == "target_side" || f.name == "target_scope"
                    || f.name == "target_error" || f.name == "priority")
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
