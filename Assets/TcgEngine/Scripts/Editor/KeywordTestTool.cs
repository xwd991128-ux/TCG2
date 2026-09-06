using UnityEditor;
using UnityEngine;
using TcgEngine;
using TcgEngine.Workshop;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 自定义关键词系统验证工具：
    /// 生成一个测试关键词（打出时对目标造成 1 点伤害），挂到任意卡的 keywords 上即可对局验证。
    /// </summary>
    public static class KeywordTestTool
    {
        [MenuItem("TcgEngine/关键词验证/生成测试关键词（打出时对目标造成1点伤害）")]
        public static void CreateTestKeyword()
        {
            const string folder = "Assets/TcgEngine/Resources/Keywords";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/TcgEngine/Resources", "Keywords");

            //最简图：OnPlay 事件 → 202001 造成伤害(1)，目标口不连线=沿用打出时选中的目标
            GraphData graph = GraphBuilder.BuildSimpleGraph("OnPlay", "202001", 1);

            KeywordData keyword = AssetDatabase.LoadAssetAtPath<KeywordData>(folder + "/keyword_test.asset");
            if (keyword == null)
            {
                keyword = ScriptableObject.CreateInstance<KeywordData>();
                AssetDatabase.CreateAsset(keyword, folder + "/keyword_test.asset");
            }
            keyword.id = "keyword_test";
            keyword.title = "测试关键词";
            keyword.desc = "验证用：打出时对目标造成 1 点伤害";
            keyword.status_type = StatusType.None;
            keyword.rules.Clear();
            if (graph != null)
                keyword.rules.Add(new KeywordRule { trigger_action = "OnPlay", graph = graph });

            EditorUtility.SetDirty(keyword);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = keyword;
            Debug.Log("[关键词验证] 已生成 Assets/TcgEngine/Resources/Keywords/keyword_test.asset，把它加到某张卡的 Keywords 列表后进对局：打出该卡→目标掉1点血=关键词链路生效", keyword);
        }

        /// <summary>原生机制关键词（StatusType 包装）一览：id/标题/说明/状态类型</summary>
        private static readonly (string id, string title, string desc, StatusType type)[] NativeKeywords =
        {
            ("charge",      "冲锋",   "入场当回合即可攻击。",                       StatusType.Haste),
            ("windfury",    "风怒",   "每回合可以攻击两次。",                       StatusType.Fury),
            ("taunt",       "嘲讽",   "敌方角色必须先攻击该随从。",                 StatusType.Protection),
            ("divine_shield","圣盾",  "抵消第一次受到的伤害。",                     StatusType.Shell),
            ("stealth",     "潜行",   "行动前无法被敌方选中攻击。",                 StatusType.Stealth),
            ("flying",      "飞行",   "无视嘲讽。",                                 StatusType.Flying),
            ("deathtouch",  "致死",   "攻击即消灭受击角色。",                       StatusType.Deathtouch),
            ("lifesteal",   "吸血",   "造成伤害的同时为你的英雄回复等量生命。",     StatusType.LifeSteal),
            ("first_strike","先攻",   "攻击时不会受到反击伤害。",                   StatusType.FirstStrike),
            ("trample",     "践踏",   "超出目标生命值的伤害将溢出给敌方英雄。",     StatusType.Trample),
            ("spell_immunity","法术免疫", "无法被法术选中或受到法术伤害。",         StatusType.SpellImmunity),
            ("armor",       "护甲",   "受到的伤害降低。",                           StatusType.Armor),
            ("immunity",    "免疫",   "受到的伤害变为 0。",                         StatusType.Immunity),
            ("regenerate",  "再生",   "回合结束时回复所有生命。",                   StatusType.Regenerate),
        };

        [MenuItem("TcgEngine/关键词验证/生成常用关键词（原生机制）")]
        public static void CreateNativeKeywords()
        {
            const string folder = "Assets/TcgEngine/Resources/Keywords";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/TcgEngine/Resources", "Keywords");

            int created = 0;
            foreach (var k in NativeKeywords)
            {
                string path = $"{folder}/{k.id}.asset";
                KeywordData keyword = AssetDatabase.LoadAssetAtPath<KeywordData>(path);
                if (keyword == null)
                {
                    keyword = ScriptableObject.CreateInstance<KeywordData>();
                    AssetDatabase.CreateAsset(keyword, path);
                    created++;
                }
                keyword.id = k.id;
                keyword.title = k.title;
                keyword.desc = k.desc;
                keyword.status_type = k.type;
                keyword.rules.Clear();
                EditorUtility.SetDirty(keyword);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[关键词验证] 原生关键词就绪：共 {NativeKeywords.Length} 个（新建 {created} 个），编辑卡牌时下拉即可选择", AssetDatabase.LoadAssetAtPath<KeywordData>(folder + "/charge.asset"));
        }

        private static readonly (string id, string title)[] CommonTraits =
        {
            ("murloc", "鱼人"), ("beast", "野兽"), ("element", "元素"), ("mech", "机械"),
            ("dragon", "龙"),   ("demon", "恶魔"), ("totem", "图腾"),   ("pirate", "海盗"),
            ("undead", "亡灵"), ("naga", "纳迦"),
        };

        [MenuItem("TcgEngine/关键词验证/生成常用种族")]
        public static void CreateCommonTraits()
        {
            const string folder = "Assets/TcgEngine/Resources/Traits";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/TcgEngine/Resources", "Traits");

            int created = 0;
            foreach (var t in CommonTraits)
            {
                string path = $"{folder}/{t.id}.asset";
                TraitData trait = AssetDatabase.LoadAssetAtPath<TraitData>(path);
                if (trait == null)
                {
                    trait = ScriptableObject.CreateInstance<TraitData>();
                    AssetDatabase.CreateAsset(trait, path);
                    created++;
                }
                trait.id = t.id;
                trait.title = t.title;
                EditorUtility.SetDirty(trait);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[关键词验证] 常用种族就绪：共 {CommonTraits.Length} 个（新建 {created} 个）", AssetDatabase.LoadAssetAtPath<TraitData>(folder + "/murloc.asset"));
        }
    }
}
