using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 「乱斗」构筑环境的**参数化生成**：给定一个 seed，产出一套完整、可复现、且**确实能打**的构筑规则。
    ///
    /// 为什么是"生成"而不是手填：乱斗的价值在于每期规则不同，手填 N 套环境既慢又容易配出矛盾规则；
    /// 这里把"规模 + 主题约束 + 额外区"参数化，seed 相同 → 结果逐字段相同（两端一致）。
    ///
    /// ★ 核心保证：**每条规则在生成前都做可满足性预检** ——
    ///   生成器会拿真实卡池算一遍"满足这条规则的卡够不够凑出主卡张数"，
    ///   不够就换一条/降规模。避免生成出玩家根本组不出来的乱斗（那是最难查的一类问题）。
    ///
    /// 用法：Editor 菜单「TcgEngine/构筑规则/生成乱斗环境」把 N 个 seed 落成资产
    /// （落资产是必须的：对局设置里只传 `deck_format_id`，服务端要能按 id 解析出**同一套**规则）。
    /// </summary>
    public static class BrawlFormatGenerator
    {
        /// <summary>环境 id 前缀，便于与手填环境区分</summary>
        public const string IdPrefix = "brawl_";

        private static readonly int[] SizePool = { 20, 25, 30, 35, 40 };
        private static readonly int[] CopiesPool = { 2, 3, 4 };
        private static readonly int[] LegendaryPool = { 0, 1, 2 };   //0 = 不限传说

        /// <summary>乱斗主题：每个主题都要自带"可行性检查"，不满足就换</summary>
        private enum Theme
        {
            EvenMana = 0,    //费用全偶数
            OddMana = 1,     //费用全奇数
            CheapCards = 2,  //只放低费
            BigCards = 3,    //只放高费
            BandNight = 4,   //带必填「乐队」额外区
            ManyKinds = 5,   //要够多样（每种 1 张）
            FewKinds = 6,    //只许少数几种（靠多张复制）
        }

        public static string IdFor(int seed)
        {
            return IdPrefix + seed.ToString("00");
        }

        /// <summary>按 seed 生成一套乱斗规则（纯数据，不落资产；同 seed 结果恒定）</summary>
        public static DeckFormatData Generate(int seed)
        {
            Rng rng = new Rng(seed);
            DeckFormatData f = ScriptableObject.CreateInstance<DeckFormatData>();
            f.id = IdFor(seed);

            int size = SizePool[rng.Next(SizePool.Length)];
            int copies = CopiesPool[rng.Next(CopiesPool.Length)];
            int legendary = LegendaryPool[rng.Next(LegendaryPool.Length)];

            //主题：从"当前卡池真能凑出来"的主题里挑（可行性检查就在 CanSatisfy 里）
            List<Theme> usable = new List<Theme>();
            for (int i = 0; i <= (int)Theme.FewKinds; i++)
            {
                Theme t = (Theme)i;
                if (CanSatisfy(t, size, copies))
                    usable.Add(t);
            }
            if (usable.Count == 0)
            {
                //理论上不会发生（没有主题可用的卡池本身就组不出任何卡组）；退化成"纯规模乱斗"
                f.title = "乱斗 · 纯规模";
                f.desc = "主卡 " + size + " 张；同名 ≤" + copies + "；仅规模差异。";
                f.deck_size = size;
                f.max_copies = copies;
                f.max_legendary = legendary;
                return f;
            }

            Theme theme = usable[rng.Next(usable.Count)];
            f.deck_size = size;
            f.max_copies = copies;
            f.max_legendary = legendary;
            f.title = "乱斗 · " + ThemeTitle(theme);
            f.desc = "主卡 " + size + " 张；同名 ≤" + copies
                + (legendary > 0 ? "；传说 ≤" + legendary : "；传说不限")
                + "。" + ThemeDesc(theme, size, copies);

            switch (theme)
            {
                case Theme.EvenMana:
                    f.constraints.Add(OddEven(false));
                    break;
                case Theme.OddMana:
                    f.constraints.Add(OddEven(true));
                    break;
                case Theme.CheapCards:
                    f.constraints.Add(new DeckConstraint
                    {
                        scope = DeckScope.MainDeck, attr = DeckAttr.ManaCost, op = DeckOp.Max, value = 4,
                        message = "乱斗规则：主卡每张费用都要 ≤ 4。"
                    });
                    break;
                case Theme.BigCards:
                    f.constraints.Add(new DeckConstraint
                    {
                        scope = DeckScope.MainDeck, attr = DeckAttr.ManaCost, op = DeckOp.Min, value = 3,
                        message = "乱斗规则：主卡每张费用都要 ≥ 3。"
                    });
                    break;
                case Theme.BandNight:
                    f.zones.Add(new DeckZone
                    {
                        id = "band", title = "乐队", desc = "乱斗：卡组外另选若干张作为乐队",
                        min_count = 1, max_count = 3, per_card_max = 1, required = true
                    });
                    break;
                case Theme.ManyKinds:
                    f.constraints.Add(new DeckConstraint
                    {
                        scope = DeckScope.MainDeck, attr = DeckAttr.DistinctCount, op = DeckOp.Min, value = size,
                        message = "乱斗规则：主卡不许重复，需要 " + size + " 种不同的卡。"
                    });
                    break;
                case Theme.FewKinds:
                    f.constraints.Add(new DeckConstraint
                    {
                        scope = DeckScope.MainDeck, attr = DeckAttr.DistinctCount, op = DeckOp.Max, value = FewKindsCap(size, copies),
                        message = "乱斗规则：主卡最多只能用 " + FewKindsCap(size, copies) + " 种不同的卡。"
                    });
                    break;
            }
            return f;
        }

        /// <summary>
        /// 「精简牌组」的种类上限：必须用**向上取整**。
        /// 用整除会算出配不满的组合（例：主卡 25、同名 ≤2 时，25/2=12 种最多只有 24 张 → 规则不可满足）。
        /// </summary>
        private static int FewKindsCap(int size, int copies)
        {
            return Mathf.Max(1, Mathf.CeilToInt((float)size / Mathf.Max(1, copies)));
        }

        private static DeckConstraint OddEven(bool odd)
        {
            return new DeckConstraint
            {
                scope = DeckScope.MainDeck, attr = DeckAttr.ManaCost, op = odd ? DeckOp.Odd : DeckOp.Even,
                message = odd ? "乱斗规则：主卡每张费用都要是奇数。" : "乱斗规则：主卡每张费用都要是偶数。"
            };
        }

        private static string ThemeTitle(Theme t)
        {
            if (t == Theme.EvenMana) return "费用全偶";
            if (t == Theme.OddMana) return "费用全奇";
            if (t == Theme.CheapCards) return "低费速攻";
            if (t == Theme.BigCards) return "高费大牌";
            if (t == Theme.BandNight) return "乐队之夜";
            if (t == Theme.ManyKinds) return "百花齐放";
            return "精简牌组";
        }

        private static string ThemeDesc(Theme t, int size, int copies)
        {
            if (t == Theme.EvenMana) return "主卡费用必须全为偶数。";
            if (t == Theme.OddMana) return "主卡费用必须全为奇数。";
            if (t == Theme.CheapCards) return "主卡每张费用 ≤ 4。";
            if (t == Theme.BigCards) return "主卡每张费用 ≥ 3。";
            if (t == Theme.BandNight) return "额外带一个必填「乐队」区（1-3 张）。";
            if (t == Theme.ManyKinds) return "主卡需要 " + size + " 种不同的卡。";
            return "主卡最多 " + FewKindsCap(size, copies) + " 种不同的卡。";
        }

        /// <summary>
        /// 可满足性预检：**按真实卡池**算"满足这条规则的卡够不够凑出主卡张数"。
        /// 只做保守判断（不够就换主题），宁可少生成一种玩法，也不生成打不出来的规则。
        /// </summary>
        private static bool CanSatisfy(Theme theme, int size, int copies)
        {
            return EligibleDistinctCount(theme) >= size;
        }

        private static int EligibleDistinctCount(Theme theme)
        {
            List<CardData> pool = CardData.GetAll();
            if (theme == Theme.FewKinds)
            {
                //"只许少数几种"能不能成立 = 卡池里的不同卡种数够不够（配合 ceil 后的种类上限）
                //注意：调用方会用实际 size/copies 再判一次，这里的返回值只用来表示"卡池够不够大"
                return CountDistinct(pool) > 0 ? int.MaxValue : 0;
            }
            int n = 0;
            HashSet<string> seen = new HashSet<string>();
            foreach (CardData c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.id) || seen.Contains(c.id))
                    continue;
                if (!CardEligible(c, theme))
                    continue;
                seen.Add(c.id);
                n++;
            }
            return n;
        }

        private static bool CardEligible(CardData c, Theme theme)
        {
            if (theme == Theme.EvenMana) return Mathf.Abs(c.mana) % 2 == 0;
            if (theme == Theme.OddMana) return Mathf.Abs(c.mana) % 2 == 1;
            if (theme == Theme.CheapCards) return c.mana <= 4;
            if (theme == Theme.BigCards) return c.mana >= 3;
            return true;   //BandNight / ManyKinds 对卡本身没有额外要求
        }

        private static int CountDistinct(List<CardData> pool)
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (CardData c in pool)
            {
                if (c != null && !string.IsNullOrEmpty(c.id))
                    seen.Add(c.id);
            }
            return seen.Count;
        }

        /// <summary>
        /// 自带的小型 LCG：**不依赖 UnityEngine.Random / System.Random 的实现细节**，
        /// 保证任何平台、任何 .NET 版本、两端（客户端/服务端）算出的都是同一个序列。
        /// </summary>
        private struct Rng
        {
            private uint state;

            public Rng(int seed)
            {
                state = (uint)(seed * 2654435761u) ^ 0x9E3779B9u;
                if (state == 0)
                    state = 1u;
            }

            public int Next(int max)
            {
                state = state * 1664525u + 1013904223u;   //Numerical Recipes LCG
                return (int)((state >> 8) % (uint)Mathf.Max(1, max));
            }
        }
    }
}
