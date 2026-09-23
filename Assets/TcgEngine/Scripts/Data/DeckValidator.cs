using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>一条构筑错误：中文提示 + 出错对象（供后续"返回调整"时定位到具体卡/额外区）</summary>
    public class DeckError
    {
        public string message;
        public string card_tid;   //出错的卡（空 = 整副卡组级别的问题）
        public string zone_id;    //出错的额外区（空 = 非额外区问题）

        public DeckError(string message, string card_tid = null, string zone_id = null)
        {
            this.message = message;
            this.card_tid = card_tid;
            this.zone_id = zone_id;
        }

        public override string ToString()
        {
            return message;
        }
    }

    /// <summary>
    /// 卡组校验器：输入「玩家卡组 + 构筑环境 + 勾选的可选规则」→ 输出**中文**错误列表（空 = 合法）。
    ///
    /// 只读 ResolvedDeckRules，所以新增一种修饰来源、或加一套乱斗环境，都不需要改判定流程。
    ///
    /// 判定口径（重要，避免歧义）：
    ///   · CardCount / DistinctCount → **整组聚合**比较（最多/最少/恰好几张）
    ///   · ManaCost / Attack / Hp / Rarity(rank) → **逐张**判定（"每张卡都要满足"，如"费用全偶数"）
    ///   · op=Even/Odd → 逐张判费用奇偶；op=Require → 卡组里必须存在某类卡（attr_id 给修饰 id / 类型 / 种族…）
    ///   · 传说的口径：`max_legendary` = **同一种**传说卡最多几张（与项目既有 CollectionPanel 对 mythic 的规则一致）；
    ///     "传说"= 稀有度 rank 最高的一档，不硬编码稀有度 id。
    ///   · 暂不参与判定的组合（如 attr=关键字 + op=Max）会被跳过，不会误报。
    /// </summary>
    public static class DeckValidator
    {
        public static List<DeckError> Validate(UserDeckData deck, DeckFormatData format, List<string> toggled_rules)
        {
            List<DeckError> errors = new List<DeckError>();
            if (deck == null)
            {
                errors.Add(new DeckError("没有选择卡组。"));
                return errors;
            }

            ResolvedDeckRules rules = DeckRulesResolver.Resolve(deck, format, toggled_rules);
            int legendary_rank = HighestRarityRank();

            CheckMainSize(errors, deck, rules);
            CheckCopies(errors, deck, rules, legendary_rank);
            CheckZones(errors, deck, rules);
            CheckConstraints(errors, deck, rules);
            return errors;
        }

        /// <summary>
        /// 按「对局设置」里的构筑规则校验 —— 客户端对局前拦截与服务端权威校验**共用这一个入口**，
        /// 保证两端判据完全一致（设置随 GameSettings 走网络）。
        /// </summary>
        public static List<DeckError> Validate(UserDeckData deck, GameSettings settings)
        {
            DeckFormatData format = settings != null ? DeckFormatData.Get(settings.deck_format_id) : null;
            if (format == null)
                format = DeckFormatData.GetStandard();   //没指定环境 → 用标准环境（再没有就回退 GameplayData 默认）
            List<string> toggles = settings != null ? DeckRuleToggles.Split(settings.deck_optional_rules) : new List<string>();
            return Validate(deck, format, toggles);
        }

        /// <summary>便捷判定：没有任何错误即合法</summary>
        public static bool IsValid(UserDeckData deck, DeckFormatData format, List<string> toggled_rules)
        {
            return Validate(deck, format, toggled_rules).Count == 0;
        }

        /// <summary>把错误拼成一段中文（供弹框/日志直接显示）</summary>
        public static string JoinMessages(List<DeckError> errors, string separator = "\n")
        {
            if (errors == null || errors.Count == 0)
                return "";
            List<string> lines = new List<string>();
            foreach (DeckError e in errors)
                lines.Add("· " + e.message);
            return string.Join(separator, lines);
        }

        //---------------- 主卡数量 ----------------

        private static void CheckMainSize(List<DeckError> errors, UserDeckData deck, ResolvedDeckRules rules)
        {
            int count = CountCards(deck.cards);
            if (count == rules.deck_size)
                return;
            errors.Add(count > rules.deck_size
                ? new DeckError("主卡超出：" + count + " / " + rules.deck_size + " 张。")
                : new DeckError("主卡不足：" + count + " / " + rules.deck_size + " 张。"));
        }

        //---------------- 同名单卡上限（含传说单卡上限）----------------

        private static void CheckCopies(List<DeckError> errors, UserDeckData deck, ResolvedDeckRules rules, int legendary_rank)
        {
            foreach (KeyValuePair<string, int> kv in CountByTid(deck.cards))
            {
                CardData card = CardData.Get(kv.Key);
                int cap = CapFor(card, rules, legendary_rank);
                if (cap <= 0 || kv.Value <= cap)
                    continue;
                bool legendary = IsLegendary(card, legendary_rank) && rules.max_legendary > 0 && rules.max_legendary < rules.max_copies;
                errors.Add(new DeckError(
                    "「" + NameOf(card, kv.Key) + "」放了 " + kv.Value + " 张，超过" + (legendary ? "传说单卡上限 " : "同名单卡上限 ") + cap + " 张。",
                    kv.Key));
            }
        }

        /// <summary>单卡实际上限：同名上限与传说上限取小</summary>
        private static int CapFor(CardData card, ResolvedDeckRules rules, int legendary_rank)
        {
            int cap = rules.max_copies;
            if (rules.max_legendary > 0 && IsLegendary(card, legendary_rank))
                cap = Mathf.Min(cap, rules.max_legendary);
            return cap;
        }

        private static bool IsLegendary(CardData card, int legendary_rank)
        {
            return legendary_rank > 0 && card != null && card.rarity != null && card.rarity.rank >= legendary_rank;
        }

        /// <summary>最高稀有度的 rank（= 传说档）；查不到返回 0</summary>
        private static int HighestRarityRank()
        {
            int highest = 0;
            foreach (RarityData r in RarityData.GetAll())
            {
                if (r != null && r.rank > highest)
                    highest = r.rank;
            }
            return highest;
        }

        //---------------- 额外区 ----------------

        private static void CheckZones(List<DeckError> errors, UserDeckData deck, ResolvedDeckRules rules)
        {
            foreach (DeckZone zone in rules.zones)
            {
                if (zone == null || string.IsNullOrEmpty(zone.id))
                    continue;
                int count = CountCards(ZoneCards(deck, zone.id));
                if (count > zone.max_count)
                    errors.Add(new DeckError("「" + ZoneTitle(zone) + "」最多 " + zone.max_count + " 张，当前 " + count + " 张。", null, zone.id));
                else if (count < zone.min_count)
                    errors.Add(new DeckError("「" + ZoneTitle(zone) + "」至少 " + zone.min_count + " 张，当前 " + count + " 张。", null, zone.id));

                CheckZonePerCard(errors, zone, ZoneCards(deck, zone.id));
            }
        }

        private static void CheckZonePerCard(List<DeckError> errors, DeckZone zone, List<UserCardData> cards)
        {
            if (zone.per_card_max <= 0)
                return;
            foreach (KeyValuePair<string, int> kv in CountByTid(cards))
            {
                if (kv.Value <= zone.per_card_max)
                    continue;
                errors.Add(new DeckError(
                    "「" + ZoneTitle(zone) + "」里「" + NameOf(CardData.Get(kv.Key), kv.Key) + "」放了 " + kv.Value + " 张，超过每卡上限 " + zone.per_card_max + " 张。",
                    kv.Key, zone.id));
            }
        }

        private static List<UserCardData> ZoneCards(UserDeckData deck, string zone_id)
        {
            if (deck.zones == null)
                return new List<UserCardData>();
            foreach (UserDeckZone z in deck.zones)
            {
                if (z != null && z.zone_id == zone_id && z.cards != null)
                    return new List<UserCardData>(z.cards);
            }
            return new List<UserCardData>();
        }

        //---------------- 约束 ----------------

        private static void CheckConstraints(List<DeckError> errors, UserDeckData deck, ResolvedDeckRules rules)
        {
            foreach (DeckConstraint c in rules.constraints)
            {
                DeckError err = Evaluate(deck, c);
                if (err != null)
                    errors.Add(err);
            }
        }

        private static DeckError Evaluate(UserDeckData deck, DeckConstraint c)
        {
            if (c == null)
                return null;
            if (c.op == DeckOp.Even) return CheckParity(deck, c, 0);
            if (c.op == DeckOp.Odd) return CheckParity(deck, c, 1);
            if (c.op == DeckOp.Require) return CheckRequire(deck, c);
            return CheckCompare(deck, c);        //Max / Min / Equal
        }

        /// <summary>奇偶：逐张判费用（parity: 0=全偶、1=全奇）</summary>
        private static DeckError CheckParity(UserDeckData deck, DeckConstraint c, int parity)
        {
            foreach (UserCardData uc in ScopeCards(deck, c))
            {
                CardData card = CardData.Get(uc.tid);
                if (card == null)
                    continue;
                if (Mathf.Abs(card.mana) % 2 == parity)
                    continue;
                return new DeckError(Message(c, "「" + NameOf(card, uc.tid) + "」的费用是 " + card.mana
                    + "，" + (parity == 0 ? "不是偶数" : "不是奇数") + "。"), uc.tid);
            }
            return null;
        }

        /// <summary>Require：卡组里必须存在某类卡（修饰 / 类型 / 种族 / 阵营 / 关键词）</summary>
        private static DeckError CheckRequire(UserDeckData deck, DeckConstraint c)
        {
            string title = RequireTitle(c);
            foreach (UserCardData uc in ScopeCards(deck, c))
            {
                if (MatchesRequire(CardData.Get(uc.tid), c))
                    return null;
            }
            return new DeckError(Message(c, "卡组里必须至少有一张" + (string.IsNullOrEmpty(title) ? "符合条件" : "「" + title + "」") + "的卡。"));
        }

        private static bool MatchesRequire(CardData card, DeckConstraint c)
        {
            if (card == null)
                return false;
            if (c.attr == DeckAttr.Modifier) return card.deck_modifier_id == c.attr_id;
            if (c.attr == DeckAttr.CardType) return card.type.ToString() == c.attr_id;
            if (c.attr == DeckAttr.Rarity) return card.rarity != null && card.rarity.id == c.attr_id;
            if (c.attr == DeckAttr.Team) return card.team != null && card.team.id == c.attr_id;
            if (c.attr == DeckAttr.Trait) return HasTrait(card, c.attr_id);
            return false;
        }

        private static bool HasTrait(CardData card, string trait_id)
        {
            if (card.traits == null)
                return false;
            foreach (TraitData t in card.traits)
            {
                if (t != null && t.id == trait_id)
                    return true;
            }
            return false;
        }

        /// <summary>比较类：聚合属性整组比，数值属性逐张比</summary>
        private static DeckError CheckCompare(UserDeckData deck, DeckConstraint c)
        {
            List<UserCardData> cards = ScopeCards(deck, c);
            if (c.attr == DeckAttr.CardCount || c.attr == DeckAttr.DistinctCount)
            {
                int total = c.attr == DeckAttr.CardCount ? CountCards(cards) : CountByTid(cards).Count;
                if (Compare(total, c.op, c.value))
                    return null;
                return new DeckError(Message(c, "当前 " + total + "，要求" + OpText(c.op) + " " + c.value + "。"));
            }

            foreach (UserCardData uc in cards)
            {
                CardData card = CardData.Get(uc.tid);
                if (card == null || !TryAttrValue(card, c.attr, out int actual))
                    continue;
                if (Compare(actual, c.op, c.value))
                    continue;
                return new DeckError(Message(c, "「" + NameOf(card, uc.tid) + "」的" + AttrText(c.attr) + "是 " + actual
                    + "，要求" + OpText(c.op) + " " + c.value + "。"), uc.tid);
            }
            return null;
        }

        private static bool TryAttrValue(CardData card, DeckAttr attr, out int value)
        {
            if (attr == DeckAttr.ManaCost) { value = card.mana; return true; }
            if (attr == DeckAttr.Attack) { value = card.attack; return true; }
            if (attr == DeckAttr.Hp) { value = card.hp; return true; }
            if (attr == DeckAttr.Rarity && card.rarity != null) { value = card.rarity.rank; return true; }
            value = 0;
            return false;
        }

        private static bool Compare(int actual, DeckOp op, int expected)
        {
            if (op == DeckOp.Max) return actual <= expected;
            if (op == DeckOp.Min) return actual >= expected;
            return actual == expected;
        }

        /// <summary>约束的作用卡集（scope 决定范围）</summary>
        private static List<UserCardData> ScopeCards(UserDeckData deck, DeckConstraint c)
        {
            List<UserCardData> list = new List<UserCardData>();
            if (deck == null)
                return list;
            if (c.scope != DeckScope.ExtraZone && deck.cards != null)
                list.AddRange(deck.cards);
            if (c.scope == DeckScope.WholeDeck || c.scope == DeckScope.ExtraZone)
            {
                foreach (UserDeckZone z in ZoneList(deck))
                {
                    if (c.scope == DeckScope.ExtraZone && !string.IsNullOrEmpty(c.zone_id) && z.zone_id != c.zone_id)
                        continue;
                    if (z.cards != null)
                        list.AddRange(z.cards);
                }
            }
            return list;
        }

        private static List<UserDeckZone> ZoneList(UserDeckData deck)
        {
            return deck != null && deck.zones != null ? new List<UserDeckZone>(deck.zones) : new List<UserDeckZone>();
        }

        //---------------- 公共小工具 ----------------

        private static Dictionary<string, int> CountByTid(UserCardData[] cards)
        {
            return CountByTid(cards != null ? new List<UserCardData>(cards) : new List<UserCardData>());
        }

        private static Dictionary<string, int> CountByTid(List<UserCardData> cards)
        {
            Dictionary<string, int> map = new Dictionary<string, int>();
            foreach (UserCardData uc in cards)
            {
                if (uc == null || string.IsNullOrEmpty(uc.tid))
                    continue;
                int n = uc.quantity > 0 ? uc.quantity : 0;
                map.TryGetValue(uc.tid, out int old);
                map[uc.tid] = old + n;
            }
            return map;
        }

        private static int CountCards(UserCardData[] cards)
        {
            return cards != null ? CountCards(new List<UserCardData>(cards)) : 0;
        }

        private static int CountCards(List<UserCardData> cards)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> kv in CountByTid(cards))
                total += kv.Value;
            return total;
        }

        private static string NameOf(CardData card, string tid)
        {
            return card != null && !string.IsNullOrEmpty(card.title) ? card.title : tid;
        }

        private static string ZoneTitle(DeckZone zone)
        {
            return string.IsNullOrEmpty(zone.title) ? zone.id : zone.title;
        }

        private static string RequireTitle(DeckConstraint c)
        {
            if (c.attr != DeckAttr.Modifier)
                return c.attr_id;
            DeckModifier m = DeckModifierData.GetModifier(c.attr_id);
            return m != null && !string.IsNullOrEmpty(m.title) ? m.title : c.attr_id;
        }

        private static string Message(DeckConstraint c, string fallback)
        {
            return string.IsNullOrEmpty(c.message) ? fallback : c.message;
        }

        private static string OpText(DeckOp op)
        {
            if (op == DeckOp.Max) return "最多";
            if (op == DeckOp.Min) return "最少";
            return "恰好";
        }

        private static string AttrText(DeckAttr attr)
        {
            if (attr == DeckAttr.ManaCost) return "费用";
            if (attr == DeckAttr.Attack) return "攻击";
            if (attr == DeckAttr.Hp) return "生命";
            return "数值";
        }
    }
}
