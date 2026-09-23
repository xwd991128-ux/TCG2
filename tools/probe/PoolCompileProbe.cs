using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Workshop;

/// <summary>【临时探针】卡池编译冒烟：**"图 → AbilityData"这一段的验证**（差分探针覆盖不到）。
///
/// 为什么需要它：AbilityDiffProbe 是直接把 base_pool_v1.json 里的 graph 丢给 NodeDocRunner.Run 跑的，
/// **完全不经过 CardPoolIO.CompileGraphAbilities**。因此这些改动差分全绿也证明不了：
///   target_mode 是否编译成对的 ab.target / once_per_turn 是否挂上 ConditionOnce /
///   filters_target 是否随数据还原 / 动作白名单是否漏（"未支持动作"警告不算失败）。
///
/// 用法：建 tools/pool_smoke_flag.txt → 进 Play 一次 → 写 tools/pool_compile_smoke.tsv → 自动删标记。
/// 查什么（读结果文件）：
///   1) 每张有图效果的卡：编译出几个能力、每个能力的 trigger/target/条件/过滤器/效果类名；
///   2) 汇总：效果图数 vs 编译能力数、过滤器 DTO 数 vs 编译后过滤器数（必须相等）；
///   3) MISMATCH：某卡"ok 行数（converter_report）≠ 效果图数" → 说明有效果被静默丢弃。
/// </summary>
public static class PoolCompileProbe
{
    private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    private static string FlagPath { get { return Path.Combine(Root, "tools/pool_smoke_flag.txt"); } }
    private static string PoolPath { get { return Path.Combine(Root, "tools/base_pool_v1.json"); } }
    private static string ReportPath { get { return Path.Combine(Root, "tools/converter_report.tsv"); } }
    private static string OutPath { get { return Path.Combine(Root, "tools/pool_compile_smoke.tsv"); } }

    private static bool _running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_running || !File.Exists(FlagPath))
            return;
        _running = true;
        try { Run(); }
        catch (Exception e) { Debug.LogError("[编译冒烟] 失败: " + e); }
        finally
        {
            try { File.Delete(FlagPath); } catch { }
            _running = false;
        }
    }

    private static void Run()
    {
        CardPoolData pool;
        try { pool = JsonUtility.FromJson<CardPoolData>(File.ReadAllText(PoolPath, Encoding.UTF8)); }
        catch (Exception e) { Debug.LogError("[编译冒烟] 读卡池失败: " + e.Message); return; }
        if (pool == null || pool.cards == null) { Debug.LogError("[编译冒烟] 卡池为空"); return; }

        //编译入口统一走 CardPoolIO.BuildCardData（公开 API = 真实导入同一函数）：
        //  内部就是"数据直通能力（abilities[]） + CompileGraphAbilities（图）"，两条通道都覆盖。
        //  （原来只反射 CompileGraphAbilities → 图卡上的数据直通能力不落行、无法对账。）

        Dictionary<string, int> okByCard = ReadOkCounts();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# 卡池编译冒烟（图 → AbilityData）  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("# 列：card | effIdx | ability | trigger | target | condTrigger | condTarget | filters | status | effects | chains");
        int cardsWithGraph = 0, effTotal = 0, abTotal = 0, filterDto = 0, filterAb = 0, chainDto = 0, chainAb = 0;
        int dataAbTotal = 0;   //数据直通卡编译出的能力（不在 abTotal 里：abTotal 只算图通道）
        List<string> mismatches = new List<string>();
        List<string> targetMismatch = new List<string>();

        foreach (CardCustomData cd in pool.cards)
        {
            if (cd == null)
                continue;
            List<CardEffectData> effs = cd.EnsureEffects();
            bool hasGraph = false;
            foreach (CardEffectData e in effs)
                if (e != null && e.graph != null && e.graph.nodes != null && e.graph.nodes.Count > 0)
                    hasGraph = true;
            if (!hasGraph)
            {
                //★无图卡（数据直通：Ongoing/装备/纯状态）→ 图相关断言不适用，但**编译出的能力必须落行**，
                //  否则"数据直通卡的条件/过滤器/连锁"在冒烟里完全不可见 → 对账脚本也无从比对（假绿盲区）。
                CardData only = null;
                try { only = CardPoolIO.BuildCardData(cd); } catch { only = null; }
                if (only != null && only.abilities != null)
                    foreach (AbilityData a in only.abilities)
                    {
                        if (a == null)
                            continue;
                        dataAbTotal++;
                        sb.AppendLine(cd.id + "\t-\t" + a.id + "\t" + a.trigger + "\t" + a.target + "\t"
                            + CondDetail(a.conditions_trigger) + "\t" + CondDetail(a.conditions_target) + "\t"
                            + FilterNames(a.filters_target) + "\t" + StatusNames(a.status) + "\t"
                            + EffectNames(a.effects) + "\t" + ChainNames(a.chain_abilities));
                    }
                continue;
            }
            cardsWithGraph++;

            //效果图数 vs 报告 ok 行数（每个 ok 行 = 一张图）
            int okN = okByCard.ContainsKey(cd.id) ? okByCard[cd.id] : 0;
            int graphN = 0;
            foreach (CardEffectData e in effs)
                if (e != null && e.graph != null && e.graph.nodes != null && e.graph.nodes.Count > 0)
                    graphN++;
            if (graphN != okN)
                mismatches.Add(cd.id + "（图 " + graphN + " vs ok行 " + okN + "）");

            //入口 target_mode 的期望值集合（逐图一条；用于与编译结果做**多重集**比对，
            //避免"一张卡多张图"时把图1的模式拿去比对图2的能力，造成假报警）
            List<string> expectTargets = new List<string>();
            foreach (CardEffectData e in effs)
            {
                if (e == null || e.graph == null || e.graph.nodes == null || e.graph.nodes.Count == 0)
                    continue;
                string em = TargetModeOf(e.graph);
                string ex = string.IsNullOrEmpty(em) ? null : ExpectedTarget(em);
                if (ex != null)
                    expectTargets.Add(ex);
            }

            //★改用 CardPoolIO.BuildCardData（=真实导入同一函数：**数据直通能力 + CompileGraphAbilities**）。
            //  只反射 CompileGraphAbilities 的话，同一张卡上的**数据直通能力**（如 vampire 的 vampire_lord）
            //  不落行 → 对账脚本按 id 配不上、条件也无从比对（假报警/假绿）。图相关断言仍只看 `_node` 能力。
            List<AbilityData> abs = null;
            CardData builtFull = null;
            try { builtFull = CardPoolIO.BuildCardData(cd); }
            catch (Exception e) { sb.AppendLine(cd.id + "\t-\t<编译异常>\t" + Flatten(e) + "\t\t\t\t\t\t"); continue; }
            if (builtFull != null && builtFull.abilities != null)
                abs = new List<AbilityData>(builtFull.abilities);
            if (abs == null)
            {
                sb.AppendLine(cd.id + "\t-\t<编译返回 null>\t\t\t\t\t\t\t");
                continue;
            }
            abTotal += abs.Count;

            for (int i = 0; i < effs.Count; i++)
            {
                CardEffectData e = effs[i];
                if (e == null || e.graph == null || e.graph.nodes == null || e.graph.nodes.Count == 0)
                    continue;
                effTotal++;
                int fcount = (e.filters_target != null) ? e.filters_target.Count : 0;
                filterDto += fcount;
                int ccount = (e.chain_ability_ids != null) ? e.chain_ability_ids.Count : 0;
                chainDto += ccount;
                sb.AppendLine(cd.id + "\t" + i + "\t<效果图>\t" + EntryOf(e.graph) + "\t"
                    + TargetModeOf(e.graph) + "\t\t\t" + fcount + "\t\t" + NodesOf(e.graph) + "\t"
                    + (ccount > 0 ? string.Join("+", e.chain_ability_ids.ToArray()) : ""));
            }
            List<string> actualTargets = new List<string>();
            foreach (AbilityData a in abs)
            {
                if (a == null)
                    continue;
                filterAb += (a.filters_target != null) ? a.filters_target.Length : 0;
                chainAb += (a.chain_abilities != null) ? a.chain_abilities.Length : 0;
                sb.AppendLine(cd.id + "\t-\t" + a.id + "\t" + a.trigger + "\t" + a.target + "\t"
                    + CondDetail(a.conditions_trigger) + "\t" + CondDetail(a.conditions_target) + "\t"
                    + FilterNames(a.filters_target) + "\t" + StatusNames(a.status) + "\t" + EffectNames(a.effects) + "\t"
                    + ChainNames(a.chain_abilities));
                //只核对 NodeDoc 图编译出来的能力（内置直通动作走 GetGraphTarget，不读 target_mode）
                if (a.id != null && a.id.Contains("_node"))
                    actualTargets.Add(a.target.ToString());
            }
            expectTargets.Sort();
            actualTargets.Sort();
            if (string.Join(",", expectTargets.ToArray()) != string.Join(",", actualTargets.ToArray()))
                targetMismatch.Add(cd.id + "（入口期望 " + string.Join("/", expectTargets.ToArray())
                    + " vs 编译 " + string.Join("/", actualTargets.ToArray()) + "）");
        }

        //★全池触发条件对账（**两条通道一起**）：
        //  上面主循环只走"有图的卡"（CompileGraphAbilities），而**数据直通卡**（Ongoing/装备/纯状态的
        //  abilities[].conditions_trigger）根本不经过它 → 这半边的条件以前**没人验过**（假绿盲区）。
        //  DTO 侧 = effects[].conditions_trigger + abilities[].conditions_trigger（类名）
        //  编译侧 = CardPoolIO.BuildCardData(data).abilities[].conditions_trigger（与真实导入同一函数：
        //  数据直通能力 + 图编译能力都在里面 → 两条通道都覆盖）
        //  断言：DTO 的**类名多重集必须被编译侧包含**（编译侧可能多出图推导的条件，如 once_per_turn→ConditionOnce，
        //  所以只查"没丢"，不查相等）。
        List<string> condLost = new List<string>();
        int condDtoTotal = 0, condAbTotal = 0, condCards = 0;
        foreach (CardCustomData cd in pool.cards)
        {
            if (cd == null)
                continue;
            List<string> dto = new List<string>();
            foreach (CardEffectData e in cd.EnsureEffects())
                if (e != null)
                    CollectCondNames(e.conditions_trigger, dto);
            if (cd.abilities != null)
                foreach (AbilityCustomData a in cd.abilities)
                    if (a != null)
                        CollectCondNames(a.conditions_trigger, dto);

            CardData built = null;
            try { built = CardPoolIO.BuildCardData(cd); } catch { built = null; }
            List<string> compiled = new List<string>();
            if (built != null && built.abilities != null)
                foreach (AbilityData a in built.abilities)
                    if (a != null && a.conditions_trigger != null)
                        foreach (ConditionData c in a.conditions_trigger)
                            if (c != null)
                                compiled.Add(c.GetType().Name);

            condDtoTotal += dto.Count;
            condAbTotal += compiled.Count;
            if (dto.Count > 0)
            {
                condCards++;
                foreach (string name in dto)
                {
                    int at = compiled.IndexOf(name);
                    if (at < 0)
                    {
                        condLost.Add(cd.id + " 丢了 " + name + "（DTO=" + string.Join("+", dto.ToArray())
                            + " 编译后=" + string.Join("+", compiled.ToArray()) + "）");
                        break;
                    }
                    compiled.RemoveAt(at);   //多重集：消费掉一个
                }
            }
        }

        sb.AppendLine("# 汇总：含图卡=" + cardsWithGraph + "｜效果图=" + effTotal + "｜编译能力(图)=" + abTotal
            + "｜编译能力(数据直通)=" + dataAbTotal
            + "｜过滤器 DTO=" + filterDto + " vs 编译后=" + filterAb
            + (filterDto == filterAb ? "（一致 ✓）" : "（★不一致）")
            + "｜连锁 DTO=" + chainDto + " vs 编译后=" + chainAb
            + (chainDto == chainAb ? "（一致 ✓）" : "（★不一致）"));
        sb.AppendLine("# 触发条件对账（全池·两条通道）：DTO 条件条数=" + condDtoTotal + " vs 编译后=" + condAbTotal
            + "｜带触发条件的卡=" + condCards
            + (condLost.Count == 0 ? "（DTO 条件全部还原 ✓）" : "（★丢失 " + condLost.Count + " 处）"));
        foreach (string s in condLost)
            sb.AppendLine("#   条件丢失: " + s);
        sb.AppendLine("# 效果图数 ≠ ok 行数 的卡：" + (mismatches.Count == 0 ? "无 ✓" : string.Join("、", mismatches.ToArray())));
        sb.AppendLine("# 入口 target_mode 与编译 ab.target 不一致：" + (targetMismatch.Count == 0 ? "无 ✓" : string.Join("、", targetMismatch.ToArray())));
        File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[编译冒烟] 完成：含图卡=" + cardsWithGraph + " 效果图=" + effTotal + " 编译能力=" + abTotal
            + " 过滤器 " + filterDto + "/" + filterAb + " → " + OutPath);
    }

    /// <summary>入口 target_mode 文本 → 期望的 ab.target 枚举名（与 CardPoolIO.ParseTargetMode 一一对应）</summary>
    private static string ExpectedTarget(string mode)
    {
        switch (mode)
        {
            case "无": return "None";
            case "自身": return "Self";
            case "施法者玩家": return "PlayerSelf";
            case "对手玩家": return "PlayerOpponent";
            case "全体玩家": return "AllPlayers";
            case "所有角色": return "AllCardsBoard";
            case "双方手牌": return "AllCardsHand";
            case "所有牌堆": return "AllCardsAllPiles";
            case "卡牌定义": return "AllCardData";
            case "选择目标": return "SelectTarget";
            case "打出目标": return "PlayTarget";
            case "触发者": return "AbilityTriggerer";
            case "卡牌选择": return "CardSelector";
            case "所有槽位": return "AllSlots";
            case "选择器": return "ChoiceSelector";
            default: return null;
        }
    }

    private static string EntryOf(GraphData g)
    {
        foreach (GraphNode n in g.nodes)
            if (n != null && n.type == GraphNodeType.Event)
                return n.action + (GraphRuntime.GetFieldString(n, "once_per_turn", "") == "true" ? "[once]" : "");
        return "";
    }

    private static string TargetModeOf(GraphData g)
    {
        foreach (GraphNode n in g.nodes)
            if (n != null && n.type == GraphNodeType.Event)
                return GraphRuntime.GetFieldString(n, "target_mode", "");
        return "";
    }

    private static string NodesOf(GraphData g)
    {
        StringBuilder b = new StringBuilder();
        foreach (GraphNode n in g.nodes)
            if (n != null)
                b.Append(n.action).Append(' ');
        return b.ToString().Trim();
    }

    /// <summary>条件的「类名 + 字段」明细：走 CardPoolIO.SerializeComponents（与 card_baseline.tsv 同一口径）
    /// → 可直接与迁移前基线对账，验证"数据型条件"是否**逐字段**还原（不只是类名对）。</summary>
    private static string CondDetail(ConditionData[] c)
    {
        if (c == null || c.Length == 0)
            return "";
        List<ComponentCustomData> ser = CardPoolIO.SerializeComponents(c);
        if (ser == null || ser.Count == 0)
            return "";
        StringBuilder b = new StringBuilder();
        foreach (ComponentCustomData d in ser)
        {
            if (d == null)
                continue;
            if (b.Length > 0)
                b.Append(" + ");
            b.Append(d.type);
            if (d.fields != null && d.fields.Count > 0)
            {
                b.Append('{');
                for (int i = 0; i < d.fields.Count; i++)
                {
                    if (i > 0)
                        b.Append(';');
                    b.Append(d.fields[i].name).Append('=').Append(d.fields[i].value);
                }
                b.Append('}');
            }
        }
        return b.ToString();
    }

    private static string StatusNames(StatusData[] s)
    {
        if (s == null || s.Length == 0)
            return "";
        StringBuilder b = new StringBuilder();
        foreach (StatusData x in s)
        {
            if (x == null)
                continue;
            if (b.Length > 0)
                b.Append('+');
            b.Append(x.effect);
        }
        return b.ToString();
    }

    /// <summary>DTO 条件列表 → 类名列表（ComponentCustomData.type 就是类名）</summary>
    private static void CollectCondNames(List<ComponentCustomData> src, List<string> dst)
    {
        if (src == null || dst == null)
            return;
        foreach (ComponentCustomData c in src)
            if (c != null && !string.IsNullOrEmpty(c.type))
                dst.Add(c.type);
    }

    /// <summary>连锁能力 id 列表（C 批验证点：链条要真的还原到编译出的能力上，否则又是"绿而没验"）</summary>
    private static string ChainNames(AbilityData[] chains)
    {
        if (chains == null || chains.Length == 0)
            return "";
        StringBuilder b = new StringBuilder();
        foreach (AbilityData c in chains)
        {
            if (c == null)
                continue;
            if (b.Length > 0)
                b.Append('+');
            b.Append(c.id);
        }
        return b.ToString();
    }

    private static string EffectNames(EffectData[] e)
    {
        if (e == null || e.Length == 0)
            return "";
        StringBuilder b = new StringBuilder();
        foreach (EffectData x in e)
        {
            if (x == null)
                continue;
            if (b.Length > 0)
                b.Append('+');
            b.Append(x.GetType().Name);
        }
        return b.ToString();
    }

    /// <summary>过滤器类名 + 关键整型字段（stat / amount / value），便于核对"最低攻(10)"这种参数</summary>
    private static string FilterNames(FilterData[] f)
    {
        if (f == null || f.Length == 0)
            return "";
        StringBuilder b = new StringBuilder();
        foreach (FilterData x in f)
        {
            if (x == null)
                continue;
            if (b.Length > 0)
                b.Append('+');
            b.Append(x.GetType().Name);
            foreach (FieldInfo fi in x.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                //stat 是枚举（EffectStatType），amount/value 是 int —— 都按整数打印，便于核对参数
                if (fi.FieldType != typeof(int) && !fi.FieldType.IsEnum)
                    continue;
                string fn = fi.Name.ToLowerInvariant();
                if (fn != "stat" && fn != "amount" && fn != "value")
                    continue;
                b.Append('{').Append(fi.Name).Append('=').Append(Convert.ToInt32(fi.GetValue(x))).Append('}');
            }
        }
        return b.ToString();
    }

    private static string Flatten(Exception e)
    {
        Exception x = e.InnerException != null ? e.InnerException : e;
        return x.GetType().Name + ": " + (x.Message ?? "").Replace('\t', ' ').Replace('\n', ' ');
    }

    /// <summary>converter_report.tsv 里每卡的 ok 行数（= 该卡应有几张效果图）</summary>
    private static Dictionary<string, int> ReadOkCounts()
    {
        Dictionary<string, int> map = new Dictionary<string, int>();
        try
        {
            foreach (string ln in File.ReadAllLines(ReportPath, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(ln) || ln.StartsWith("#"))
                    continue;
                string[] f = ln.Split('\t');
                if (f.Length < 5 || f[4] != "ok")
                    continue;
                map[f[0]] = map.ContainsKey(f[0]) ? map[f[0]] + 1 : 1;
            }
        }
        catch (Exception e) { Debug.LogWarning("[编译冒烟] 读报告失败: " + e.Message); }
        return map;
    }
}
