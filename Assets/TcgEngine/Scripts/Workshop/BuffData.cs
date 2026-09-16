using System;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.VFX;      // VFXConfig（增益「特效」字段；VFX 家族统一放在 TcgEngine.VFX）

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 增益属性项：key 为属性名（"攻击加成"/"生命加成"/"持续回合"/自定义名），value 为数值。
    /// 攻击/生命加成在施加时自动映射原生 StatusType.AddAttack/AddHP 参与真实战斗，
    /// 其余自定义属性存入 Buff 实例供规则图读取（106004）/写入（206003）。
    /// </summary>
    [Serializable]
    public class BuffProp
    {
        public string key;
        public int value;

        public BuffProp() { }
        public BuffProp(string key, int value) { this.key = key; this.value = value; }
    }

    /// <summary>
    /// 增益「属性修改」规则的一条（拥有该增益时对携带卡的属性做什么修改）。
    /// 目标属性分两类（下拉候选由 BuffModTarget 提供，运行时按同名口径判定）：
    ///   · 数值型：花费 / 攻击 / 生命 / 护甲 → 增加 / 减少 / 设置为 / 引用属性
    ///   · 枚举型：关键词 / 种族             → 增加（附加） / 移除
    /// 以及玩家新增的**自定义参数**（target 记为自定义名，按数值型处理）。
    ///
    /// 数值来源二选一：
    ///   · 固定值：value（可正可负）
    ///   · 引用属性：mode = 引用属性 时，取目标卡「value_source」属性的当前值
    ///     （value_source 可与 target 相同 → 表示"按自身当前值再改"，也可指向别的属性）
    /// </summary>
    [Serializable]
    public class BuffPropMod
    {
        public string target = BuffModTarget.Attack;   // 目标属性（数值型/枚举型/自定义参数名）
        public string mode = BuffModMode.Add;          // 变化方式
        public int value = 0;                          // 固定数值（引用属性时忽略）
        public string value_source = "";               // 引用属性：被引用的属性名
        public string enum_id = "";                    // 枚举型：KeywordData.id / TraitData.id
        public string enum_title = "";                 // 枚举型：显示名（编辑器写，便于回显）
        public bool is_custom = false;                 // true = 玩家新增的自定义参数（target 即参数名）

        public BuffPropMod() { }
        public BuffPropMod(string target, string mode, int value)
        {
            this.target = target;
            this.mode = mode;
            this.value = value;
        }

        /// <summary>把「引用属性」的数值来源解析成实际值（fixed_value 为固定值兜底）。
        /// 减少/减少属性 都按减少处理（两种写法并存）。</summary>
        public int ResolveValue(int fixed_value, int referenced_value)
        {
            if (mode == BuffModMode.Reference)
                return referenced_value;
            if (BuffModMode.IsSub(mode))
                return -fixed_value;
            return fixed_value;
        }
    }

    /// <summary>属性修改的目标属性（数值型/枚举型口径，编辑器下拉与运行时共用一套名字）</summary>
    public static class BuffModTarget
    {
        public const string Cost = "花费";
        public const string Attack = "攻击";
        public const string HP = "生命";
        public const string Armor = "护甲";
        public const string Keyword = "关键词";
        public const string Trait = "种族";

        /// <summary>数值型属性（支持 增加/减少/设置为/引用属性）</summary>
        public static readonly string[] Numeric = { Cost, Attack, HP, Armor };

        /// <summary>枚举型属性（支持 增加/移除）</summary>
        public static readonly string[] Enum = { Keyword, Trait };

        /// <summary>内置目标属性（自定义参数追加在后面）</summary>
        public static readonly string[] Builtin = { Cost, Attack, HP, Armor, Keyword, Trait };

        public static bool IsNumeric(string target)
        {
            return target == Cost || target == Attack || target == HP || target == Armor;
        }

        public static bool IsEnum(string target)
        {
            return target == Keyword || target == Trait;
        }
    }

    /// <summary>属性修改的变化方式（此处的"属性"= **增益属性**，见 BuffModTarget）</summary>
    public static class BuffModMode
    {
        public const string Add = "增加";
        public const string Sub = "减少";
        public const string Set = "设置为";
        public const string Remove = "移除";

        /// <summary>历史值（仅旧数据可能存到）：面板已不再提供"引用属性"，打开时归一为"设置为"</summary>
        public const string Reference = "引用属性";

        // 写法并存：旧数据/旧界面写的是「增加/减少/设置为」，新界面写的是「增加属性/减少属性/设置为属性」。
        // 两种都要**同时可用**（下拉里都列出来，选什么就存什么），运行时按下面的 IsXxx 判定，行为完全一致。

        /// <summary>数值型可用方式（下拉候选 = 存盘值，含两种写法）</summary>
        public static readonly string[] NumericOptions = { Add, Sub, Set, "增加属性", "减少属性", "设置为属性" };

        /// <summary>兼容旧字段名：数值型候选取值（同 NumericOptions）</summary>
        public static readonly string[] Numeric = NumericOptions;

        /// <summary>枚举型可用方式（下拉候选，含两种写法）</summary>
        public static readonly string[] EnumOptions = { Add, Remove, "增加属性", "移除属性" };

        /// <summary>兼容旧字段名：枚举型候选（同 EnumOptions）</summary>
        public static readonly string[] Enum = EnumOptions;

        // ---------------- 行为判定（两种写法都认，运行时/归一只用这里，避免各处 == 比较漏写法） ----------------

        /// <summary>是否"增加"（增加 / 增加属性）</summary>
        public static bool IsAdd(string m)
        {
            return m == Add || m == "增加属性";
        }

        /// <summary>是否"减少"（减少 / 减少属性）</summary>
        public static bool IsSub(string m)
        {
            return m == Sub || m == "减少属性";
        }

        /// <summary>是否"设置为"（设置为 / 设置为属性）</summary>
        public static bool IsSet(string m)
        {
            return m == Set || m == "设置为属性";
        }

        /// <summary>是否"移除"（移除 / 移除属性）</summary>
        public static bool IsRemove(string m)
        {
            return m == Remove || m == "移除属性";
        }
    }

    /// <summary>
    /// 自定义属性的类型（与「醉梦传说」规则编辑器里入口字段「自定义效果属性 = 每行 名称:类型[:数组]」同一套口径，
    /// 也就是参考图「新建自定义属性」弹框里那个类型下拉的选项）。增益的自定义参数沿用同一套命名，便于跨页面对照。
    /// </summary>
    public static class BuffPropType
    {
        public const string Int = "整数";
        public const string Bool = "真值";
        public const string Text = "文本";
        public const string Keyword = "关键词";
        public const string CardTag = "卡牌标签";
        public const string CardRef = "卡牌定义引用";
        public const string PoolRef = "卡池引用";
        public const string Deck = "牌堆";
        public const string KeywordSet = "关键词集合";
        public const string CardTagSet = "卡牌标签集合";
        public const string BuffRef = "增益定义引用";

        /// <summary>类型下拉候选（顺序与参考图「新建自定义属性」一致）</summary>
        public static readonly string[] All =
        {
            Int, Bool, Text, Keyword, CardTag, CardRef, PoolRef, Deck, KeywordSet, CardTagSet, BuffRef
        };

        /// <summary>是否数值型（「增益属性修改」可直接加减；真值按 0/1 处理）</summary>
        public static bool IsNumeric(string t)
        {
            return t == Int || t == Bool;
        }
    }

    /// <summary>
    /// 增益的「自定义属性」声明（= 参考图「新建自定义属性」：名称 + 类型 + 是否为数组 + **初始值**）。
    /// 与旧字段 BuffData.custom_props（只有名字）并存：EnsureCustomPropDefs 会把旧名字迁移成本类实例（类型=整数、初始值=0）。
    /// </summary>
    [Serializable]
    public class BuffCustomProp
    {
        public string name;                        // 属性名（会出现在「增益属性」下拉候选里）
        public string type = BuffPropType.Int;     // 类型（BuffPropType 命名）
        public bool is_array;                      // 是否为数组（数组时初始值用逗号分隔）
        public string init_value = "0";            // 初始值（文本存；整数/真值按数值解析，其余按文本）

        public BuffCustomProp() { }

        public BuffCustomProp(string name, string type, bool is_array, string init_value)
        {
            this.name = name;
            this.type = string.IsNullOrEmpty(type) ? BuffPropType.Int : type;
            this.is_array = is_array;
            this.init_value = init_value ?? "";
        }

        /// <summary>初始值的数值形式（整数→原值；真值→真/1/是 记 1，其余 0；文本/引用取不出数值则 0）</summary>
        public int InitInt()
        {
            string s = init_value == null ? "" : init_value.Trim();
            if (type == BuffPropType.Bool)
                return (s == "1" || s == "是" || s == "真" || s == "true" || s == "True") ? 1 : 0;
            int v;
            return int.TryParse(s, out v) ? v : 0;
        }

        /// <summary>按「自定义效果属性」的行格式输出：名称:类型[:数组]（便于与节点入口互相印证 / 打日志）</summary>
        public string Line()
        {
            return name + ":" + type + (is_array ? ":数组" : "");
        }
    }

    /// <summary>
    /// 增益定义（BuffDefine 的纯数据 DTO，JSON 存储于 Workshop/buffs.json）
    /// </summary>
    [Serializable]
    public class BuffData
    {
        public string id;
        public string title;
        public string category = "增益";      // 增益/减益/光环/印记
        public string desc;                    // 说明，支持 <value> 占位符
        public int duration;                   // 默认持续回合（0=永久）
        public List<BuffProp> props = new List<BuffProp>();              // 旧字段：key-value 属性（保留兼容）
        public List<BuffPropMod> mods = new List<BuffPropMod>();         // 属性修改规则列表（新：目标属性+变化方式+数值/引用）
        public List<string> custom_props = new List<string>();           // 旧字段：自定义参数名列表（只有名字，兼容保留/自动迁移）
        public List<BuffCustomProp> custom_prop_defs = new List<BuffCustomProp>();   // 自定义属性完整声明：名称/类型/是否数组/初始值

        /// <summary>旧 props → mods 的迁移是否已完成（**一次性**，随 buffs.json 落盘）。
        /// 旧实现是无条件迁移：用户把属性修改删空后，下一次刷新又会被旧 props 迁回来，
        /// 表现就是"点了删除，行又冒出来了（删不掉）"。迁移过一次后必须记住，避免复活。</summary>
        public bool mods_migrated = false;
        public VFXConfig vfx;                                            // 应用特效（点「特效」用 VFXEditorPopup 选择；null=未配置）
        public GraphData graph;                // 兼容字段：= graphs[0]（旧工具/旧编译仍读它；入口为「增益触发」事件节点）
        public List<CardEffectData> graphs = new List<CardEffectData>();   // 多张增益图（每张可各配触发入口）

        public string GetTitle() { return string.IsNullOrEmpty(title) ? id : title; }

        /// <summary>多张增益图（幂等）：首次调用把旧单图 graph 迁移成 graphs[0]，并把 graph 双写指向第一张。</summary>
        public List<CardEffectData> EnsureGraphs()
        {
            if (graphs == null)
                graphs = new List<CardEffectData>();
            if (graphs.Count == 0)
            {
                if (graph == null)
                    graph = new GraphData();
                graphs.Add(new CardEffectData { name = "增益图1", graph = graph });
            }
            if (graphs[0] != null && graphs[0].graph != null)
                graph = graphs[0].graph;
            return graphs;
        }

        /// <summary>目标属性下拉的完整候选：内置 6 项 + 本增益的自定义属性（去重、保持顺序）</summary>
        public List<string> TargetOptions()
        {
            List<string> list = new List<string>(BuffModTarget.Builtin);
            List<BuffCustomProp> defs = EnsureCustomPropDefs();   //幂等自愈：旧名字会在这里补成完整声明
            foreach (BuffCustomProp c in defs)
            {
                if (c != null && !string.IsNullOrEmpty(c.name) && !list.Contains(c.name))
                    list.Add(c.name);
            }
            return list;
        }

        // ==================== 自定义属性（名称 + 类型 + 是否为数组 + 初始值） ====================

        /// <summary>自定义属性声明（幂等自愈）：把旧 custom_props（只有名字）迁移成完整声明（类型=整数、初始值=0）</summary>
        public List<BuffCustomProp> EnsureCustomPropDefs()
        {
            if (custom_prop_defs == null)
                custom_prop_defs = new List<BuffCustomProp>();
            if (custom_props == null)
                custom_props = new List<string>();
            for (int i = 0; i < custom_props.Count; i++)
            {
                string n = custom_props[i];
                if (string.IsNullOrEmpty(n) || FindCustomProp(n) != null)
                    continue;
                custom_prop_defs.Add(new BuffCustomProp(n, BuffPropType.Int, false, "0"));
            }
            return custom_prop_defs;
        }

        /// <summary>按名找自定义属性声明（找不到→null）</summary>
        public BuffCustomProp FindCustomProp(string name)
        {
            if (custom_prop_defs == null || string.IsNullOrEmpty(name))
                return null;
            foreach (BuffCustomProp c in custom_prop_defs)
            {
                if (c != null && c.name == name)
                    return c;
            }
            return null;
        }

        /// <summary>取某自定义属性的初始值（找不到→0）</summary>
        public int CustomPropInit(string name)
        {
            BuffCustomProp c = FindCustomProp(name);
            return c != null ? c.InitInt() : 0;
        }

        /// <summary>新增自定义属性；同名时视为"编辑"，覆盖类型/是否数组/初始值</summary>
        public BuffCustomProp AddCustomProp(string name, string type, bool is_array, string init_value)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            EnsureCustomPropDefs();
            BuffCustomProp exist = FindCustomProp(name);
            if (exist == null)
            {
                exist = new BuffCustomProp(name, type, is_array, init_value);
                custom_prop_defs.Add(exist);
            }
            else
            {
                exist.type = string.IsNullOrEmpty(type) ? BuffPropType.Int : type;
                exist.is_array = is_array;
                exist.init_value = init_value ?? "";
            }
            if (!custom_props.Contains(name))
                custom_props.Add(name);
            return exist;
        }

        /// <summary>删除自定义属性（同时清掉旧名字列表里的同名项）</summary>
        public void RemoveCustomProp(string name)
        {
            if (custom_prop_defs != null)
                custom_prop_defs.RemoveAll(c => c == null || c.name == name);
            if (custom_props != null)
                custom_props.Remove(name);
        }

        /// <summary>属性修改列表（幂等自愈）：空列表时把旧字段 props 迁移成 mods，
        /// 保证老 buffs.json 打开就是"增加 X 点"的等价规则，不丢数据。</summary>
        public List<BuffPropMod> EnsureMods()
        {
            if (mods == null)
                mods = new List<BuffPropMod>();
            if (custom_props == null)
                custom_props = new List<string>();
            //★ 只迁移一次：迁移过就不再从旧 props 复活（否则"删空属性修改"会被反复覆盖回来）
            if (!mods_migrated && mods.Count == 0 && props != null && props.Count > 0)
            {
                foreach (BuffProp p in props)
                {
                    if (p == null || string.IsNullOrEmpty(p.key))
                        continue;
                    BuffPropMod m = new BuffPropMod();
                    m.value = p.value;
                    m.mode = BuffModMode.Add;
                    if (p.key == BuffRuntime.ATK_KEY)
                        m.target = BuffModTarget.Attack;
                    else if (p.key == BuffRuntime.HP_KEY)
                        m.target = BuffModTarget.HP;
                    else
                    {
                        m.target = p.key;          //旧的自定义属性名原样保留
                        m.is_custom = true;
                        if (!custom_props.Contains(p.key))
                            custom_props.Add(p.key);
                    }
                    mods.Add(m);
                }
                Debug.Log("[增益] 旧属性已迁移为属性修改规则：" + GetTitle() + "（" + mods.Count + " 条）");
                mods_migrated = true;
            }
            for (int i = 0; i < mods.Count; i++)
                NormalizeMod(mods[i]);      //旧写法 / 历史"引用属性"归一（幂等）
            return mods;
        }

        /// <summary>单条属性修改规则归一（幂等）：
        /// 空方式→增加；历史"引用属性"→"设置为"（该方式已从面板下线，值仍取 m.value）。</summary>
        public static void NormalizeMod(BuffPropMod m)
        {
            if (m == null)
                return;
            if (string.IsNullOrEmpty(m.mode))
                m.mode = BuffModMode.Add;
            m.mode = m.mode.Trim();
            if (m.mode == BuffModMode.Reference)
                m.mode = BuffModMode.Set;

            //目标属性别名归一：旧 props 里常见「生命值 / 攻击力 / 攻击加成 / 法力费用 / 护甲值 / 关键字 / 特性」，
            //不归一的话运行时 StatusOf() 认不出 → 规则看起来存在但改不动真实属性。
            if (!string.IsNullOrEmpty(m.target))
            {
                m.target = m.target.Trim();
                switch (m.target)
                {
                    case "生命值":
                    case "生命加成":
                        m.target = BuffModTarget.HP;
                        break;
                    case "攻击力":
                    case "攻击加成":
                        m.target = BuffModTarget.Attack;
                        break;
                    case "法力费用":
                    case "费用":
                    case "耗":
                    case "法力值":
                        m.target = BuffModTarget.Cost;
                        break;
                    case "护甲值":
                    case "护盾":
                        m.target = BuffModTarget.Armor;
                        break;
                    case "关键字":
                        m.target = BuffModTarget.Keyword;
                        break;
                    case "特性":
                        m.target = BuffModTarget.Trait;
                        break;
                }
                m.is_custom = !(BuffModTarget.IsNumeric(m.target) || BuffModTarget.IsEnum(m.target));
            }
        }

        /// <summary>把 mods 同步回旧字段 props（攻击/生命加成 + 自定义数值），
        /// 让仍在读 props 的老代码（规则图 106004 等）行为不变。</summary>
        public void SyncLegacyProps()
        {
            EnsureMods();
            mods_migrated = true;      //走到"以 mods 为准"回写，迁移阶段即告结束
            if (props == null)
                props = new List<BuffProp>();
            props.Clear();
            foreach (BuffPropMod m in mods)
            {
                if (m == null || m.mode == BuffModMode.Reference || m.mode == BuffModMode.Remove)
                    continue;                       //引用/移除无法用固定数值表达
                string key = m.target == BuffModTarget.Attack ? BuffRuntime.ATK_KEY
                    : m.target == BuffModTarget.HP ? BuffRuntime.HP_KEY
                    : m.target;
                int v = m.value;
                if (m.mode == BuffModMode.Sub)
                    v = -v;
                props.Add(new BuffProp(key, v));
            }
        }
        public string GetDesc()
        {
            if (string.IsNullOrEmpty(desc))
                return "";
            string d = desc;
            foreach (BuffProp p in props)
                d = d.Replace("<" + p.key + ">", p.value.ToString());
            return d;
        }
    }

    /// <summary>
    /// 增益池 JSON 容器（对应 Workshop/buffs.json）
    /// </summary>
    [Serializable]
    public class SerializableBuffPool
    {
        public string version = "1.0";
        public string timestamp = "";
        public List<BuffData> buffs = new List<BuffData>();
    }

    /// <summary>
    /// 对局中卡牌身上的增益实例（随 Card 一起序列化）。
    /// props 保存实例属性（含从定义复制的全部属性，攻击/生命加成同时映射为原生 AddAttack/AddHP 状态）。
    /// </summary>
    [Serializable]
    public class CardBuff
    {
        public string buff_id;                    // 引用 BuffData.id
        public List<BuffProp> props = new List<BuffProp>();
        public int duration = 0;                  // 剩余回合（0=永久）
        public bool permanent = true;

        [NonSerialized]
        private BuffData data = null;

        public CardBuff() { }

        public CardBuff(string buff_id, List<BuffProp> props, int duration)
        {
            this.buff_id = buff_id;
            this.permanent = (duration <= 0);
            this.duration = duration;
            if (props != null)
                this.props.AddRange(props);
        }

        public BuffData BuffData
        {
            get
            {
                if (data == null || data.id != buff_id)
                    data = BuffPoolIO.Get(buff_id);
                return data;
            }
        }

        public int GetProp(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;
            foreach (BuffProp p in props)
            {
                if (p.key == key)
                    return p.value;
            }
            return 0;
        }

        public void SetProp(string key, int value)
        {
            foreach (BuffProp p in props)
            {
                if (p.key == key)
                {
                    p.value = value;
                    return;
                }
            }
            props.Add(new BuffProp(key, value));
        }
    }
}
