using System;
using System.Collections.Generic;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 卡池包：一个可导入/导出的卡牌集合（JSON 序列化格式）
    /// 卡牌数据（ScriptableObject）无法直接序列化（含 Sprite/GameObject 等资源引用），
    /// 因此通过本 DTO 层转为纯数据，用于本地文件分享与创意工坊传输。
    /// </summary>
    [Serializable]
    public class CardPoolData
    {
        public string name = "MyCardPool";
        public string description = "";
        public string author = "";
        public string version = "1.0";
        public string timestamp = "";
        public List<CardCustomData> cards = new List<CardCustomData>();
        public GraphData graph;   // 卡池规则图（节点编辑器数据，可为空）
    }

    /// <summary>
    /// 卡牌数据（CardData 的 DTO 形式），引用类型统一用 id/枚举名表达
    /// </summary>
    [Serializable]
    public class CardCustomData
    {
        public string id;
        public string title;
        public string type;          // CardType 枚举名
        public string team;          // TeamData.id
        public string rarity;        // RarityData.id
        public string trait;         // 兼容旧数据：单个种族 id（无 traits 时作为 traits[0]；有 traits 时同步 traits[0]）
        public List<string> traits = new List<string>();   // 种族/特质（TraitData.id，可多选 + 可自定义）
        public int mana;
        public int attack;
        public int hp;
        public string text;
        public string desc;
        public bool deckbuilding;
        public int cost;
        public List<AbilityCustomData> abilities = new List<AbilityCustomData>();
        public List<string> keywords = new List<string>();   // 关键词（KeywordData.id，可多个）

        // ---- 卡牌节点编辑器（P2+）附加配置 ----
        public GraphData graph;              // 兼容旧数据：单张效果图（无 effects 时作为 effects[0]；有 effects 时同步 effects[0]）
        public List<CardEffectData> effects = new List<CardEffectData>();  // 多效果图：每张图一个入口（战吼/亡语/光环/事件…）
        public string art_path;              // 卡牌图片文件名（Workshop/Art/ 目录下）
        public string art_full_path;         // 面板（全图）图片文件名（Workshop/Art/，法术/奥秘无）
        public string spawn_audio_id;        // 音效/音乐配置（字符串 id，运行时加载）
        public string attack_audio_id;
        public string death_audio_id;
        public string damage_audio_id;

        /// <summary>取效果图列表（首次调用会把旧单图 graph 迁移为 effects[0]；保证至少 1 张）。</summary>
        public List<CardEffectData> EnsureEffects()
        {
            if (effects == null)
                effects = new List<CardEffectData>();
            if (effects.Count == 0)
                effects.Add(new CardEffectData { name = "效果1", graph = graph });
            // 双写兼容：graph 始终指向第一张效果图，旧编译/旧工具无需改动
            graph = effects[0] != null ? effects[0].graph : graph;
            return effects;
        }

        /// <summary>取种族列表（首次调用把旧单种族字段 trait 迁移为 traits[0]；旧工具读 trait 仍然正确）。</summary>
        public List<string> EnsureTraits()
        {
            if (traits == null)
                traits = new List<string>();
            if (traits.Count == 0 && !string.IsNullOrEmpty(trait))
                traits.Add(trait);
            trait = traits.Count > 0 ? traits[0] : "";   //双写兼容
            return traits;
        }

        // ==================== 卡牌自定义属性（与「增益」里那套完全同一规格） ====================
        // 声明格式对齐「醉梦传说」的「自定义效果属性 = 名称:类型[:数组]」，并额外带**初始值**。
        // 复用 BuffData.cs 里的 BuffCustomProp / BuffPropType（同一套类型命名与结构），
        // 差别只在于"归属"：这里描述的是**这张卡牌**的自定义参数。

        /// <summary>卡牌自定义属性声明（名称 / 类型 / 是否为数组 / 初始值）</summary>
        public List<BuffCustomProp> custom_prop_defs = new List<BuffCustomProp>();

        /// <summary>自定义属性列表（幂等自愈：为 null 时补空表）</summary>
        public List<BuffCustomProp> EnsureCustomPropDefs()
        {
            if (custom_prop_defs == null)
                custom_prop_defs = new List<BuffCustomProp>();
            return custom_prop_defs;
        }

        /// <summary>按名找自定义属性（找不到→null）</summary>
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

        /// <summary>新增（同名视为编辑：覆盖类型 / 是否数组 / 初始值）</summary>
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
            return exist;
        }

        /// <summary>删除自定义属性</summary>
        public void RemoveCustomProp(string name)
        {
            if (custom_prop_defs != null)
                custom_prop_defs.RemoveAll(c => c == null || c.name == name);
        }
    }

    /// <summary>
    /// 单张效果图：一张卡可有多个效果（战吼/亡语/光环/事件…），每个效果一张独立图、各自一个入口，
    /// 编译为互相独立的能力，天然避免"一条图里多个入口谁生效"的歧义。
    /// </summary>
    [Serializable]
    public class CardEffectData
    {
        public string name = "效果1";   // 展示名（编辑器 tab 标题）
        public GraphData graph;         // 该效果的规则图（入口节点 + 动作链）

        /// <summary>★迁移期新增（内置卡迁移 D 批）：「数据型目标过滤器」。
        /// 为什么不做成节点：过滤器（随机取1/最低攻/首个…）要**看整个目标集合**才能算，
        /// 而图是"目标相对"的（引擎解析出目标集合后逐目标 Run 一次图，图内不得自枚举集合，
        /// 否则动作会被重复施加）。引擎本来就在解析目标集合时应用它：
        /// AbilityData.GetCardTargets/GetPlayerTargets/GetSlotTargets 里逐个 FilterTargets()。
        /// 因此按"数据进 DTO"原则原样保留（同 status_ids 的思路），编译侧还原到 ab.filters_target
        /// → 与旧能力逐行一致。空/缺省 = 不过滤（旧图行为完全不变）。</summary>
        public List<ComponentCustomData> filters_target;

        /// <summary>★迁移期新增（内置卡迁移 D 批）：「数据型条件」。
        /// 条件本来由引擎判定（发动前 `AreTriggerConditionsMet`、解析目标集合 `AreTargetConditionsMet`），
        /// 图是"目标相对"的（引擎先解析目标集合，再逐目标 Run 一次图）→ 条件既可以进图做守卫，
        /// 也可以原样交给引擎。**图里表达不了的条件**（ConditionCount / ConditionSlotRange /
        /// 类型含阵营·种族 / ConditionSelectedValue…）走这里，避免整条能力作废：
        ///   - 行为与旧能力**逐行一致**（同一批 ConditionData 交回同一套引擎判定）；
        ///   - CardSelector 的**候选列表**（GetCardTargets）+ 选择校验（IsCardSelectionValid）也才筛得对
        ///     —— 这部分图守卫替代不了（守卫只能事后挡，候选列表会全列出来）。
        /// 空/缺省 = 条件只由图守卫表达（旧图行为完全不变）。</summary>
        public List<ComponentCustomData> conditions_target;
        public List<ComponentCustomData> conditions_trigger;

        /// <summary>★迁移期新增（内置卡迁移 B 批）：「能力自带的状态」（旧系统"状态 + 效果混合"能力）。
        /// 旧引擎在 `AbilityData.DoEffects(logic, caster, target)` 里把 `status` 施加到**每个已解析目标**上
        /// （在效果结算之后），所以走 DTO 直通即可逐行等价（与 filters/conditions 同一思路）；
        /// 图里不新增"添加状态"节点。取值 = `StatusType` 枚举名（与 AbilityCustomData.status_ids 同口径）。</summary>
        public List<string> status_ids;

        /// <summary>★迁移期新增（内置卡迁移 C 批 = 批6 连锁）：连锁能力 id 列表（旧 `AbilityData.chain_abilities`）。
        /// 旧引擎在 `AfterAbilityResolved` 里 `foreach chain_abilities → TriggerCardAbility(chain, caster)`
        /// （ChoiceSelector 目标除外——那时 chain_abilities 是选择菜单的选项，由 SelectChoice 消费）。
        /// 这是**引擎侧**行为、与图无关 → 原样带走即可逐行等价（同 filters/conditions/status 的思路）；
        /// 被引用的连锁能力资产走"池内 id 引用"（同 EffectAddAbility，Phase 5 打包时随池打）。
        /// 空/缺省 = 无连锁（旧图行为完全不变）。</summary>
        public List<string> chain_ability_ids;
    }

    /// <summary>
    /// 能力数据（AbilityData 的 DTO 形式）
    /// </summary>
    [Serializable]
    public class AbilityCustomData
    {
        public string id;
        public string trigger;           // AbilityTrigger 枚举名
        public string target;            // AbilityTarget 枚举名
        public int value;
        public int duration;
        public int mana_cost;
        public bool exhaust;
        public string title;
        public string desc;
        public List<string> status_ids = new List<string>();            // StatusData.effect 枚举名
        public List<string> chain_ability_ids = new List<string>();     // AbilityData.id 引用
        public List<ComponentCustomData> effects = new List<ComponentCustomData>();
        public List<ComponentCustomData> conditions_trigger = new List<ComponentCustomData>();
        public List<ComponentCustomData> conditions_target = new List<ComponentCustomData>();
        public List<ComponentCustomData> filters_target = new List<ComponentCustomData>();
    }

    /// <summary>
    /// 效果/条件/过滤器的通用 DTO，通过反射按字段名还原参数
    /// </summary>
    [Serializable]
    public class ComponentCustomData
    {
        public string type;                          // 类名，如 "EffectDamage"
        public List<FieldCustomData> fields = new List<FieldCustomData>();
    }

    /// <summary>
    /// 单个字段的值（字符串化；引用类型用 id）
    /// </summary>
    [Serializable]
    public class FieldCustomData
    {
        public string name;
        public string value;
    }
}
