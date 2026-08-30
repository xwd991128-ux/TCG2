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
        public int mana;
        public int attack;
        public int hp;
        public string text;
        public string desc;
        public bool deckbuilding;
        public int cost;
        public List<AbilityCustomData> abilities = new List<AbilityCustomData>();
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
