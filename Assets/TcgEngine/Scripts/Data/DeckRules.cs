using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 对局前勾选的可选规则 id ↔ 单个字符串。
    /// 为什么不用 List：可选规则要随 GameSettings 走网络，而 Netcode 的 SerializeValue 不支持集合字段。
    /// </summary>
    public static class DeckRuleToggles
    {
        public const char Separator = '|';

        public static string Join(List<string> rule_ids)
        {
            if (rule_ids == null || rule_ids.Count == 0)
                return "";
            return string.Join(Separator.ToString(), rule_ids);
        }

        public static List<string> Split(string joined)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(joined))
                return list;
            foreach (string s in joined.Split(Separator))
            {
                if (!string.IsNullOrEmpty(s))
                    list.Add(s);
            }
            return list;
        }
    }

    /// <summary>
    /// 结算后的**实际**构筑规则：环境（DeckFormatData）叠加所有生效修饰（DeckModifier）之后的结果。
    /// 校验器只读这个对象 —— 所以"新增一种修饰来源"不需要改校验逻辑。
    /// </summary>
    public class ResolvedDeckRules
    {
        public int deck_size;
        public int max_copies;
        public int max_legendary;      //<=0 表示不限传说
        public List<DeckZone> zones = new List<DeckZone>();
        public List<DeckConstraint> constraints = new List<DeckConstraint>();
        public List<DeckPredicate> predicates = new List<DeckPredicate>();
        public List<DeckModifier> active_modifiers = new List<DeckModifier>();

        /// <summary>按 id 取额外区定义（环境自带 + 修饰追加的都算）</summary>
        public DeckZone FindZone(string zone_id)
        {
            if (string.IsNullOrEmpty(zone_id))
                return null;
            foreach (DeckZone z in zones)
            {
                if (z != null && z.id == zone_id)
                    return z;
            }
            return null;
        }
    }

    /// <summary>
    /// 把「环境 + 生效修饰」结算成 ResolvedDeckRules。
    /// 修饰来源目前两条：① 环境里的可选规则（被勾选才生效）② 卡牌自带（卡上 deck_modifier_id 按 id 取）。
    /// 以后新增来源（例如赛季规则），只要往 active_modifiers 里加一条，ApplyModifier 不用改。
    /// </summary>
    public static class DeckRulesResolver
    {
        public const int DefaultDeckSize = 30;
        public const int DefaultMaxCopies = 2;

        public static ResolvedDeckRules Resolve(UserDeckData deck, DeckFormatData format, List<string> toggled_rules)
        {
            ResolvedDeckRules rules = new ResolvedDeckRules();

            //① 环境基础值：有环境资产就用它，否则回退 GameplayData 的全局默认（老流程行为不变）
            GameplayData gdata = GameplayData.Get();
            rules.deck_size = format != null ? format.deck_size : (gdata != null ? gdata.deck_size : DefaultDeckSize);
            rules.max_copies = format != null ? format.max_copies : (gdata != null ? gdata.deck_duplicate_max : DefaultMaxCopies);
            rules.max_legendary = format != null ? format.max_legendary : 0;

            if (format != null)
            {
                rules.zones.AddRange(format.zones);
                rules.constraints.AddRange(format.constraints);
                rules.predicates.AddRange(format.predicates);
            }

            //② 收集生效修饰
            CollectOptionalModifiers(rules, format, toggled_rules);
            CollectCardModifiers(rules, deck);

            //③ 逐个叠加
            foreach (DeckModifier mod in rules.active_modifiers)
                ApplyModifier(rules, mod);
            return rules;
        }

        /// <summary>环境里的"可选规则"：只有被勾选的才生效</summary>
        private static void CollectOptionalModifiers(ResolvedDeckRules rules, DeckFormatData format, List<string> toggled_rules)
        {
            if (format == null || format.optional_modifiers == null || toggled_rules == null)
                return;
            foreach (DeckModifier mod in format.optional_modifiers)
            {
                if (mod == null || string.IsNullOrEmpty(mod.id))
                    continue;
                if (toggled_rules.Contains(mod.id))
                    rules.active_modifiers.Add(mod);
            }
        }

        /// <summary>卡牌自带修饰：按卡上的 deck_modifier_id 取（同一种修饰只生效一次）</summary>
        private static void CollectCardModifiers(ResolvedDeckRules rules, UserDeckData deck)
        {
            if (deck == null)
                return;
            HashSet<string> applied = new HashSet<string>();
            foreach (UserCardData uc in AllDeckCards(deck))
            {
                CardData card = uc != null ? CardData.Get(uc.tid) : null;
                if (card == null || string.IsNullOrEmpty(card.deck_modifier_id))
                    continue;
                if (!applied.Add(card.deck_modifier_id))
                    continue;
                DeckModifier mod = DeckModifierData.GetModifier(card.deck_modifier_id);
                if (mod != null)
                    rules.active_modifiers.Add(mod);
            }
        }

        /// <summary>主卡 + 所有额外区的卡（去重前的原始集合）</summary>
        public static List<UserCardData> AllDeckCards(UserDeckData deck)
        {
            List<UserCardData> list = new List<UserCardData>();
            if (deck == null)
                return list;
            if (deck.cards != null)
                list.AddRange(deck.cards);
            if (deck.zones != null)
            {
                foreach (UserDeckZone z in deck.zones)
                {
                    if (z != null && z.cards != null)
                        list.AddRange(z.cards);
                }
            }
            return list;
        }

        private static void ApplyModifier(ResolvedDeckRules rules, DeckModifier mod)
        {
            if (mod == null)
                return;
            if (mod.overrides != null)
            {
                foreach (DeckOverride ov in mod.overrides)
                    ApplyOverride(rules, ov);
            }
            if (mod.constraints != null) rules.constraints.AddRange(mod.constraints);
            if (mod.zones != null) rules.zones.AddRange(mod.zones);
            if (mod.predicates != null) rules.predicates.AddRange(mod.predicates);
        }

        private static void ApplyOverride(ResolvedDeckRules rules, DeckOverride ov)
        {
            if (ov == null)
                return;
            int current = GetField(rules, ov.field);
            int next = ov.mode == DeckOverrideMode.Add ? current + ov.value : ov.value;
            SetField(rules, ov.field, next);
        }

        private static int GetField(ResolvedDeckRules rules, DeckFormatField field)
        {
            if (field == DeckFormatField.DeckSize) return rules.deck_size;
            if (field == DeckFormatField.MaxLegendary) return rules.max_legendary;
            return rules.max_copies;
        }

        private static void SetField(ResolvedDeckRules rules, DeckFormatField field, int value)
        {
            if (field == DeckFormatField.DeckSize) rules.deck_size = value;
            else if (field == DeckFormatField.MaxLegendary) rules.max_legendary = value;
            else rules.max_copies = value;
        }
    }
}
