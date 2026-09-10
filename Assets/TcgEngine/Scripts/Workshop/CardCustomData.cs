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
