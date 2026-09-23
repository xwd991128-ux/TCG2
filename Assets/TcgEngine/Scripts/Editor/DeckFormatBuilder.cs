using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 一键生成「构筑规则」的默认资产：标准环境 + 5 个样例修饰。
    ///
    /// 为什么要有它：DeckFormatData / DeckModifierData 是手填字段较多的 ScriptableObject
    /// （规模 + 约束 + 额外区 + 条件），手工在 Inspector 里逐条填又慢又容易填错；
    /// 而且验收用例（自由构筑 / 上限+1 / ETC 乐队 / 绑卡 / 奇偶奖励）需要对应的样例数据才能演示。
    ///
    /// **幂等**：已存在的资产不会被覆盖（只在新建时填值），可重复点；已改过的样例不会被重置。
    /// 生成位置：Assets/TcgEngine/Resources/DeckFormats、Assets/TcgEngine/Resources/DeckModifiers
    /// </summary>
    public static class DeckFormatBuilder
    {
        private const string ResourcesRoot = "Assets/TcgEngine/Resources";
        private const string FormatFolder = ResourcesRoot + "/DeckFormats";
        private const string ModifierFolder = ResourcesRoot + "/DeckModifiers";

        private const int StandardDeckSize = 30;      //标准环境：主卡 30 张
        private const int StandardMaxCopies = 2;      //同名 ≤2
        private const int StandardMaxLegendary = 1;   //传说 ≤1
        private const int FreeBuildCopies = 99;       //自由构筑：实际上等于解除上限
        private const int BandMaxCount = 3;           //乐队区上限
        private const int BrawlCount = 12;            //一键生成的乱斗套数（id: brawl_01…brawl_12）

        [MenuItem("TcgEngine/构筑规则/生成默认资产（标准环境 + 样例修饰）", false, 100)]
        public static void BuildDefaults()
        {
            EnsureFolder(FormatFolder);
            EnsureFolder(ModifierFolder);

            int created = 0;
            created += CreateStandardFormat() ? 1 : 0;
            created += CreateSampleModifiers();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DeckFormatData.Load();
            DeckModifierData.Load();
            Debug.Log("[构筑规则] 默认资产处理完毕（新建 " + created + " 个；已存在的不覆盖）。标准环境 id="
                + DeckFormatData.StandardId + "，位置：" + FormatFolder);
        }

        /// <summary>标准环境：主卡 30、同名 ≤2、传说 ≤1</summary>
        private static bool CreateStandardFormat()
        {
            DeckFormatData f;
            if (!CreateIfMissing(FormatFolder, "standard", out f))
                return false;

            f.id = DeckFormatData.StandardId;
            f.title = "标准";
            f.desc = "主卡 " + StandardDeckSize + " 张；同名卡最多 " + StandardMaxCopies + " 张；传说卡最多 " + StandardMaxLegendary + " 张。";
            f.deck_size = StandardDeckSize;
            f.max_copies = StandardMaxCopies;
            f.max_legendary = StandardMaxLegendary;
            EditorUtility.SetDirty(f);
            return true;
        }

        /// <summary>样例修饰：对应验收用例（自由构筑 / 上限+1 / ETC 乐队 / 绑卡 / 奇偶奖励）</summary>
        private static int CreateSampleModifiers()
        {
            int n = 0;
            n += SaveModifier("free_build", "自由构筑", "带这张卡：解除同名卡上限。", mod =>
            {
                mod.overrides.Add(new DeckOverride { field = DeckFormatField.MaxCopies, mode = DeckOverrideMode.Set, value = FreeBuildCopies });
            });

            n += SaveModifier("copies_plus1", "上限+1", "带这张卡：同名卡上限 +1。", mod =>
            {
                mod.overrides.Add(new DeckOverride { field = DeckFormatField.MaxCopies, mode = DeckOverrideMode.Add, value = 1 });
            });

            n += SaveModifier("etc_band", "ETC 乐队", "带这张卡：强制生成必填的「乐队」额外区。", mod =>
            {
                mod.zones.Add(new DeckZone
                {
                    id = "band", title = "乐队", desc = "卡组外另选 3 张作为乐队",
                    min_count = 1, max_count = BandMaxCount, per_card_max = 1, required = true
                });
            });

            n += SaveModifier("bound_pair", "绑卡", "带这张卡：卡组里必须同时有带「ETC 乐队」的卡。", mod =>
            {
                mod.constraints.Add(new DeckConstraint
                {
                    scope = DeckScope.WholeDeck, attr = DeckAttr.Modifier, op = DeckOp.Require, attr_id = "etc_band",
                    message = "带「绑卡」的卡时，卡组里必须同时带上「ETC 乐队」的卡。"
                });
            });

            n += SaveModifier("parity_even", "费用全偶数", "带这张卡：主卡费用必须全为偶数；满足则开局获得奖励。", mod =>
            {
                mod.constraints.Add(new DeckConstraint
                {
                    scope = DeckScope.MainDeck, attr = DeckAttr.ManaCost, op = DeckOp.Even,
                    message = "带「费用全偶数」的卡时，主卡的费用必须全为偶数。"
                });
                mod.predicates.Add(new DeckPredicate
                {
                    id = "parity_even_reward",
                    title = "费用全偶数奖励",
                    desc = "开局时主卡费用全为偶数 → 结算奖励效果（奖励图待配）",
                    conditions = { new DeckConstraint { scope = DeckScope.MainDeck, attr = DeckAttr.ManaCost, op = DeckOp.Even } }
                });
            });
            return n;
        }

        private static int SaveModifier(string id, string title, string desc, Action<DeckModifier> fill)
        {
            DeckModifierData d;
            if (!CreateIfMissing(ModifierFolder, id, out d))
                return 0;

            d.id = id;
            d.modifier = new DeckModifier { id = id, title = title, desc = desc };
            fill(d.modifier);
            EditorUtility.SetDirty(d);
            return 1;
        }

        /// <summary>
        /// 一键生成 N 套**乱斗环境**（id: brawl_01…brawl_NN）。
        /// 幂等：已存在的资产不覆盖（玩家调过的乱斗不会被重置）；同 seed 生成结果恒定，可复现。
        /// 落成资产是必须的：对局设置里只传 deck_format_id，服务端要能按 id 解析出**同一套**规则。
        /// </summary>
        [MenuItem("TcgEngine/构筑规则/生成乱斗环境（12 个，可复现）", false, 101)]
        public static void BuildBrawls()
        {
            EnsureFolder(FormatFolder);

            int created = 0;
            int skipped = 0;
            System.Collections.Generic.List<UnityEngine.Object> temps = new System.Collections.Generic.List<UnityEngine.Object>();
            for (int seed = 1; seed <= BrawlCount; seed++)
            {
                string id = BrawlFormatGenerator.IdFor(seed);
                DeckFormatData asset;
                if (!CreateIfMissing(FormatFolder, id, out asset))
                {
                    skipped++;
                    continue;   //已有就不动它（幂等）
                }

                DeckFormatData gen = BrawlFormatGenerator.Generate(seed);
                temps.Add(gen);
                asset.id = gen.id;
                asset.title = gen.title;
                asset.desc = gen.desc;
                asset.deck_size = gen.deck_size;
                asset.max_copies = gen.max_copies;
                asset.max_legendary = gen.max_legendary;
                asset.zones = gen.zones;                       //List 是普通对象，拷引用即可（临时对象随后销毁）
                asset.constraints = gen.constraints;
                asset.optional_modifiers = gen.optional_modifiers;
                asset.predicates = gen.predicates;
                EditorUtility.SetDirty(asset);
                created++;
                Debug.Log("[构筑规则] 乱斗 seed=" + seed + " → " + asset.title + "｜" + asset.desc + "（id=" + asset.id + "）");
            }

            foreach (UnityEngine.Object t in temps)
                UnityEngine.Object.DestroyImmediate(t);   //临时 ScriptableObject 用完即销毁，避免留下未持久化对象

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DeckFormatData.Load();
            Debug.Log("[构筑规则] 乱斗环境处理完毕（新建 " + created + " 个，已存在跳过 " + skipped + " 个）。位置：" + FormatFolder);
        }

        /// <summary>按路径取资产；不存在则新建。返回 true 表示本次是**新建**（调用方据此决定是否填默认值，保证幂等）</summary>
        private static bool CreateIfMissing<T>(string folder, string fileName, out T result) where T : ScriptableObject
        {
            string path = folder + "/" + fileName + ".asset";
            result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (result != null)
                return false;

            result = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(result, path);
            return true;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
