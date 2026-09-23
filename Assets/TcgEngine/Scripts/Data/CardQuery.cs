using System;
using System.Collections.Generic;
using System.Text;
using TcgEngine.Workshop;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>一条已解析的筛选条件。field 为规范字段名，op 为 : = != &lt; &lt;= &gt; &gt;=</summary>
    public class CardQueryTerm
    {
        public string field;      // mana / atk / hp / trait / kw / type / team / rarity / pool / pack / prop / id / text / free
        public string op;
        public string value;
        public string raw;        // 原始片段（用于 chips 显示与"点 × 移除"）
        public bool negate;       // 取反（- 前缀 或 != ）
    }

    /// <summary>字段速查条目（高级筛选帮助面板用：点一下插进输入框）</summary>
    public class CardQueryFieldHelp
    {
        public string group;      // 分组标题（"字段" / "常用取值"）
        public string title;      // 显示名（如 "费用"）
        public string example;    // 插入到搜索框的示例（如 "mana<=3"）
        public string desc;       // 说明
    }

    /// <summary>
    /// 卡牌筛选"迷你语法"：把搜索框里的一行文本解析成若干条件并逐张卡匹配。
    ///
    /// 为什么用它：筛选维度由**自定义规则/自定义卡池**决定，不可能预先在 UI 上摆好固定控件；
    /// 用一段可读的表达式（trait:龙 mana&lt;=3 kw:战吼 p:元素=火）就能筛任意字段，
    /// 且**新字段零改动**即可支持（见 <see cref="CardPoolIO.GetCustomData"/> 的自定义参数）。
    ///
    /// 语法（空格分隔，全部为"与"关系；`-` 前缀或 `!=` 取反；值可用双引号包住以含空格）：
    ///   trait:龙      种族/特性（TraitData 的 id 或标题，包含匹配）
    ///   kw:战吼       关键词（KeywordData 的 id 或标题）
    ///   type:法术     卡牌类型（中文名或枚举名：英雄/随从/法术/神器/奥秘/装备）
    ///   team:火       阵营/颜色（TeamData id 或标题）      rarity:传说   稀有度
    ///   mana&lt;=3      费用（= != &lt; &lt;= &gt; &gt;=，也支持区间 mana:2-4）
    ///   atk&gt;=5       攻击力        hp&lt;=2   生命值
    ///   p:元素=火     卡牌自定义参数（"p:参数名[=值]"；数组参数按包含匹配）
    ///   pool:卡池名   卡池（内置卡包 id/标题 或本地卡池文件名）      pack:卡包    内置卡包
    ///   id:xxx        卡牌 id        text:火焰   卡面文字/描述/标题模糊
    ///   其余裸词       等价于 text:（模糊搜索 id/标题/卡面文字），保持旧搜索框行为不变
    /// </summary>
    public class CardQuery
    {
        /// <summary>输入框提示用的一行示例</summary>
        public const string SyntaxHint = "trait:龙  mana<=3  kw:战吼  p:元素=火";

        public readonly List<CardQueryTerm> terms = new List<CardQueryTerm>();

        public bool IsEmpty { get { return terms.Count == 0; } }

        // ---------------- 解析 ----------------

        public static CardQuery Parse(string text)
        {
            CardQuery q = new CardQuery();
            if (string.IsNullOrEmpty(text))
                return q;

            List<string> tokens = Tokenize(text);
            for (int i = 0; i < tokens.Count; i++)
            {
                CardQueryTerm t = ParseToken(tokens[i]);
                if (t != null)
                    q.terms.Add(t);
            }
            return q;
        }

        /// <summary>按空白切分，但双引号内的空白不切（支持 text:"以火焰 之名"）</summary>
        private static List<string> Tokenize(string text)
        {
            List<string> list = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool in_quote = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    in_quote = !in_quote;
                    continue;                       //引号本身不进 token
                }
                if (!in_quote && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0)
                list.Add(sb.ToString());
            return list;
        }

        private static CardQueryTerm ParseToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            CardQueryTerm t = new CardQueryTerm();
            t.raw = token;

            //取反前缀：-trait:龙
            string body = token;
            if (body.Length > 1 && body[0] == '-')
            {
                t.negate = true;
                body = body.Substring(1);
            }

            //找最早的运算符（顺序敏感：先两字符再单字符）
            int op_at = -1;
            string op = "";
            string[] ops = { "<=", ">=", "!=", "=", "<", ">", ":" };
            for (int i = 0; i < ops.Length; i++)
            {
                int idx = body.IndexOf(ops[i], StringComparison.Ordinal);
                if (idx > 0 && (op_at < 0 || idx < op_at))
                {
                    op_at = idx;
                    op = ops[i];
                }
            }

            if (op_at < 0)
            {
                //裸词：模糊搜索（与旧搜索框行为一致）
                t.field = "free";
                t.op = ":";
                t.value = body;
                return t;
            }

            string name = NormalizeField(body.Substring(0, op_at));
            string value = body.Substring(op_at + op.Length);

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
                return null;

            if (op == "!=")
                t.negate = !t.negate;

            t.field = name;
            t.op = (op == "!=") ? "=" : op;      //比较符统一交给 field 处理（!= 已折算成 negate）
            t.value = value;
            return t;
        }

        /// <summary>字段名/中文别名 → 规范字段名</summary>
        private static string NormalizeField(string s)
        {
            string f = (s ?? "").Trim().ToLowerInvariant();
            switch (f)
            {
                case "trait": case "traits": case "种族": case "特性": return "trait";
                case "kw": case "keyword": case "keywords": case "关键词": return "kw";
                case "type": case "类型": case "种类": return "type";
                case "team": case "color": case "colour": case "阵营": case "颜色": return "team";
                case "rarity": case "稀有度": return "rarity";
                case "mana": case "cost": case "费用": case "法力": case "法力值": return "mana";
                case "atk": case "attack": case "攻击": case "攻击力": return "atk";
                case "hp": case "health": case "生命": case "生命值": return "hp";
                case "p": case "prop": case "参数": case "自定义参数": return "prop";
                case "pool": case "卡池": return "pool";
                case "pack": case "卡包": return "pack";
                case "id": return "id";
                case "text": case "desc": case "描述": case "文字": return "text";
                default: return "";
            }
        }

        /// <summary>把（可能被删掉几条的）条件列表拼回语法串，供"点 × 移除条件"后写回输入框</summary>
        public static string ToSyntax(List<CardQueryTerm> list)
        {
            if (list == null || list.Count == 0)
                return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || string.IsNullOrEmpty(list[i].raw))
                    continue;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(list[i].raw);
            }
            return sb.ToString();
        }

        // ---------------- 匹配 ----------------

        public bool Match(CardData card)
        {
            if (card == null)
                return false;
            for (int i = 0; i < terms.Count; i++)
            {
                bool ok = MatchTerm(terms[i], card);
                if (terms[i].negate)
                    ok = !ok;
                if (!ok)
                    return false;
            }
            return true;
        }

        private static bool MatchTerm(CardQueryTerm t, CardData card)
        {
            switch (t.field)
            {
                case "free": return Fuzzy(card, t.value);
                case "id": return Cmp(card.id, t.op, t.value, false);
                case "text": return Fuzzy(card, t.value);
                case "trait": return MatchList(card.traits, t);
                case "kw": return MatchList(card.keywords, t);
                case "type": return MatchType(card, t);
                case "team": return MatchTeam(card, t);
                case "rarity": return MatchRarity(card, t);
                case "mana": return MatchInt(card.mana, t);
                case "atk": return MatchInt(card.attack, t);
                case "hp": return MatchInt(card.hp, t);
                case "pool": return MatchPool(card, t);
                case "pack": return MatchPack(card, t);
                case "prop": return MatchCustomProp(card, t);
                default: return true;
            }
        }

        /// <summary>模糊搜索：id / 标题 / 卡面文字 / 描述（与旧搜索框完全一致的行为）</summary>
        private static bool Fuzzy(CardData card, string value)
        {
            string v = (value ?? "").ToLowerInvariant();
            if (v.Length == 0)
                return true;
            if (!string.IsNullOrEmpty(card.id) && card.id.ToLowerInvariant().Contains(v)) return true;
            if (!string.IsNullOrEmpty(card.title) && card.title.ToLowerInvariant().Contains(v)) return true;
            string body = card.GetText() ?? "";
            if (body.ToLowerInvariant().Contains(v)) return true;
            if (!string.IsNullOrEmpty(card.desc) && card.desc.ToLowerInvariant().Contains(v)) return true;
            return false;
        }

        private static bool MatchList(UnityEngine.Object[] list, CardQueryTerm t)
        {
            if (list == null)
                return false;
            for (int i = 0; i < list.Length; i++)
            {
                string id = null, title = null;
                TraitData tr = list[i] as TraitData;
                if (tr != null) { id = tr.id; title = tr.title; }
                KeywordData kw = list[i] as KeywordData;
                if (kw != null) { id = kw.id; title = kw.title; }
                if (id == null && title == null && list[i] != null)
                    id = list[i].name;
                if (Contains(id, t.value) || Contains(title, t.value))
                    return true;
            }
            return false;
        }

        private static bool MatchType(CardData card, CardQueryTerm t)
        {
            CardType want = ParseType(t.value);
            if (want != CardType.None)
                return card.type == want;
            //无法识别成枚举 → 按类型显示名做包含匹配（例如 "随从" 也算命中 Character）
            return Contains(TypeName(card.type), t.value);
        }

        private static bool MatchTeam(CardData card, CardQueryTerm t)
        {
            if (card.team == null)
                return false;
            return Contains(card.team.id, t.value) || Contains(card.team.title, t.value);
        }

        private static bool MatchRarity(CardData card, CardQueryTerm t)
        {
            if (card.rarity == null)
                return false;
            return Contains(card.rarity.id, t.value) || Contains(card.rarity.title, t.value);
        }

        private static bool MatchPack(CardData card, CardQueryTerm t)
        {
            if (card.packs == null)
                return false;
            for (int i = 0; i < card.packs.Length; i++)
            {
                PackData p = card.packs[i];
                if (p == null)
                    continue;
                if (Contains(p.id, t.value) || Contains(p.title, t.value))
                    return true;
            }
            return false;
        }

        private static bool MatchPool(CardData card, CardQueryTerm t)
        {
            string v = t.value ?? "";
            if (v.StartsWith("pack:") || v.StartsWith("file:"))
                return CardPoolIO.IsCardInPool(card, v);
            //裸名字：先按内置卡包（PackData id/标题），再按本地卡池文件名
            if (MatchPack(card, t))
                return true;
            return CardPoolIO.IsCardInPool(card, "file:" + v);
        }

        /// <summary>数值：支持 = != &lt; &lt;= &gt; &gt;= 与区间 "2-4"</summary>
        private static bool MatchInt(int actual, CardQueryTerm t)
        {
            int lo, hi;
            if (ParseRange(t.value, out lo, out hi))
                return actual >= lo && actual <= hi;

            int want;
            if (!int.TryParse((t.value ?? "").Trim(), out want))
                return false;
            switch (t.op)
            {
                case "=": return actual == want;
                case "<": return actual < want;
                case "<=": return actual <= want;
                case ">": return actual > want;
                case ">=": return actual >= want;
            }
            return false;
        }

        /// <summary>自定义参数：p:元素=火 / p:元素:火 / p:强度&gt;=5 / p:元素（存在即命中；数组按包含）</summary>
        private static bool MatchCustomProp(CardData card, CardQueryTerm t)
        {
            string body = t.value ?? "";
            string name = body;
            string cmp = "";
            string val = null;

            //参数名后可能带比较符（注意顺序：先两字符，避免把 ">=" 里的 "=" 当成"名/值分隔"）
            string[] ops = { ">=", "<=", "!=", "=", "<", ">", ":" };
            for (int i = 0; i < ops.Length; i++)
            {
                int at = body.IndexOf(ops[i], StringComparison.Ordinal);
                if (at > 0)
                {
                    name = body.Substring(0, at);
                    cmp = ops[i];
                    val = body.Substring(at + ops[i].Length);
                    break;
                }
            }
            name = name.Trim();

            CardCustomData data = CardPoolIO.GetCustomData(card.id);
            if (data == null)
                return false;
            BuffCustomProp prop = data.FindCustomProp(name);
            if (prop == null)
                return false;
            if (string.IsNullOrEmpty(val))
                return true;                       //只看"有没有这个参数"

            //数组参数：按原始值文本包含匹配
            if (prop.is_array)
                return Contains(prop.init_value, val);

            //数值参数：两边都能解析成数字 → 按数值比较（含区间 2-4），否则按字符串比
            int want;
            int actual;
            if (int.TryParse(val.Trim(), out want) && int.TryParse((prop.init_value ?? "").Trim(), out actual))
            {
                switch (cmp)
                {
                    case "": case "=": case ":": return actual == want;
                    case "<": return actual < want;
                    case "<=": return actual <= want;
                    case ">": return actual > want;
                    case ">=": return actual >= want;
                }
                return false;
            }
            if (cmp == "" || cmp == ":" || cmp == "=")
                return Cmp(prop.init_value, "=", val, true) || Contains(prop.init_value, val);
            return Cmp(prop.init_value, cmp, val, true);
        }

        // ---------------- 小工具 ----------------

        private static bool ParseRange(string s, out int lo, out int hi)
        {
            lo = 0; hi = 0;
            if (string.IsNullOrEmpty(s))
                return false;
            int dash = s.IndexOf('-', 1);          //从 1 开始：避免把负数当区间
            if (dash <= 0)
                return false;
            return int.TryParse(s.Substring(0, dash).Trim(), out lo)
                && int.TryParse(s.Substring(dash + 1).Trim(), out hi);
        }

        private static bool Cmp(string actual, string op, string want, bool fuzzy_on_colon)
        {
            string a = actual ?? "";
            string w = want ?? "";
            if (op == "=")
                return string.Equals(a, w, StringComparison.OrdinalIgnoreCase);
            return fuzzy_on_colon ? Contains(a, w) : a.ToLowerInvariant().Contains(w.ToLowerInvariant());
        }

        private static bool Contains(string actual, string want)
        {
            if (string.IsNullOrEmpty(want))
                return true;
            if (string.IsNullOrEmpty(actual))
                return false;
            return actual.ToLowerInvariant().Contains(want.ToLowerInvariant());
        }

        public static CardType ParseType(string s)
        {
            string v = (s ?? "").Trim().ToLowerInvariant();
            switch (v)
            {
                case "hero": case "英雄": return CardType.Hero;
                case "character": case "minion": case "随从": case "角色": return CardType.Character;
                case "spell": case "法术": return CardType.Spell;
                case "artifact": case "神器": return CardType.Artifact;
                case "secret": case "奥秘": return CardType.Secret;
                case "equipment": case "equip": case "装备": return CardType.Equipment;
            }
            return CardType.None;
        }

        public static string TypeName(CardType t)
        {
            switch (t)
            {
                case CardType.Hero: return "英雄";
                case CardType.Character: return "随从";
                case CardType.Spell: return "法术";
                case CardType.Artifact: return "神器";
                case CardType.Secret: return "奥秘";
                case CardType.Equipment: return "装备";
            }
            return "";
        }

        // ---------------- 字段速查（高级筛选帮助面板） ----------------

        /// <summary>字段说明（固定部分）</summary>
        public static List<CardQueryFieldHelp> FieldHelp()
        {
            List<CardQueryFieldHelp> list = new List<CardQueryFieldHelp>();
            list.Add(new CardQueryFieldHelp { group = "字段", title = "种族/特性", example = "trait:龙", desc = "按种族或特性筛选（可自定义，id 或名称都认）" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "关键词", example = "kw:战吼", desc = "按机制关键词筛选（战吼/亡语/风怒…）" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "卡牌类型", example = "type:法术", desc = "英雄 / 随从 / 法术 / 神器 / 奥秘 / 装备" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "费用", example = "mana<=3", desc = "支持 = != < <= > >=，也支持区间 mana:2-4" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "攻击力", example = "atk>=5", desc = "数值比较，同费用写法" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "生命值", example = "hp<=2", desc = "数值比较，同费用写法" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "自定义参数", example = "p:元素=火", desc = "按卡牌自定义参数筛选；只写 p:参数名 表示「有该参数」" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "阵营/颜色", example = "team:火", desc = "按阵营或颜色筛选" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "稀有度", example = "rarity:传说", desc = "按稀有度筛选" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "卡池", example = "pool:我的卡池", desc = "按卡池筛选（内置卡包或本地卡池文件名）" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "卡牌 id", example = "id:dragon", desc = "按 id 精确/包含匹配" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "文字", example = "text:火焰", desc = "在标题/卡面文字/描述里模糊搜索（裸词即此行为）" });
            list.Add(new CardQueryFieldHelp { group = "字段", title = "取反", example = "-trait:龙", desc = "在字段前加减号（或写 !=）表示排除" });
            return list;
        }

        /// <summary>常用取值（从当前数据里动态生成：种族 / 关键词 / 自定义参数名）——点一下就插进输入框</summary>
        public static List<CardQueryFieldHelp> ValueHelp(int max_each = 40)
        {
            List<CardQueryFieldHelp> list = new List<CardQueryFieldHelp>();

            List<TraitData> traits = TraitAssetIO.All();
            for (int i = 0; i < traits.Count && i < max_each; i++)
            {
                if (traits[i] == null || string.IsNullOrEmpty(traits[i].id))
                    continue;
                list.Add(new CardQueryFieldHelp { group = "种族", title = traits[i].GetTitle(), example = "trait:" + traits[i].id, desc = "种族" });
            }

            List<KeywordData> kws = KeywordData.GetAll();
            for (int i = 0; i < kws.Count && i < max_each; i++)
            {
                if (kws[i] == null || string.IsNullOrEmpty(kws[i].id))
                    continue;
                //注意：KeywordData 没有 GetTitle()（只有 title 字段），这里直接读字段，避免编译不过
                string ktitle = string.IsNullOrEmpty(kws[i].title) ? kws[i].id : kws[i].title;
                list.Add(new CardQueryFieldHelp { group = "关键词", title = ktitle, example = "kw:" + kws[i].id, desc = "关键词" });
            }

            List<string> props = CardPoolIO.GetAllCustomPropNames();
            for (int i = 0; i < props.Count && i < max_each; i++)
            {
                if (string.IsNullOrEmpty(props[i]))
                    continue;
                list.Add(new CardQueryFieldHelp { group = "自定义参数", title = props[i], example = "p:" + props[i], desc = "卡牌自定义参数" });
            }
            return list;
        }
    }
}
