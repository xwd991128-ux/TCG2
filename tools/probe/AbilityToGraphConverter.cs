using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Workshop;

/// <summary>【迁移工具】内置卡能力 → 规则图 转换器（Phase 2）。
///
/// 用法（编辑器内，无需进 Play）：
///   ① 拷到 Assets/TcgEngine/Scripts/__TempProbe/ 并编译
///   ② 建标记文件 &lt;项目&gt;/tools/convert_flag.txt
///   ③ 等一次域重载（或手动 recompile）→ 自动转换并写：
///        tools/base_pool_v1.json      基础卡池（图取代能力；可用卡池导入验证）
///        tools/converter_report.tsv   逐能力结果：ok / todo + 原因
///   ④ 删除标记文件后不再自动跑。
///
/// v1 覆盖范围（与分批计划一致：批 1 白板 + 批 2 数值直伤）：
///   - 入口：OnPlay / Activate / OnDeath / StartOfTurn / EndOfTurn / OnDraw / OnBeforeAttack
///          + D 批：OnPlayOther / OnAfterAttack / OnKill / OnDeathOther
///   - 目标：无/自身/施法者玩家/对手玩家/全体玩家/所有角色/双方手牌/所有牌堆/卡牌定义/选择目标/打出目标
///          + D 批：触发者（AbilityTriggerer）
///   - 效果：造成伤害 / 治疗 / 消灭 / 抽牌 / 召唤 / 移动牌堆 / 加属性（基础值复合）
///   - 条件：归属/类型/所在牌堆/自身/目标类型；+ D 批：ConditionSlotEmpty（卡目标的常量语义）、
///          ConditionOwnerAI（→ 101030）；ConditionOnce 走入口 once_per_turn 字段（数据型触发条件）
///   其余一律**显式记 TODO + 原因**（绝不静默丢弃）。
public static class AbilityToGraphConverter
{
    private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    private static string FlagPath { get { return Path.Combine(Root, "tools/convert_flag.txt"); } }
    private static string PoolPath { get { return Path.Combine(Root, "tools/base_pool_v1.json"); } }
    private static string ReportPath { get { return Path.Combine(Root, "tools/converter_report.tsv"); } }
    /// <summary>批6 勘察产物：连锁 / 被引用能力明细（chain_* 资产不是卡能力，baseline TSV 里没有）</summary>
    private static string ChainReportPath { get { return Path.Combine(Root, "tools/chain_report.tsv"); } }

    private static bool _running;

    /// <summary>触发方式：与其它探针一致——进 Play 时若标记文件存在就跑一次（不需要开对局）。
    /// （原用 [InitializeOnLoad] + EditorApplication 轮询，实测域重载/轮询都没触发，故改为 Play 探针。）</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_running || !File.Exists(FlagPath))
            return;
        _running = true;
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Debug.LogError("[转换器] 失败: " + e);
        }
        finally
        {
            try { File.Delete(FlagPath); } catch { }
            _running = false;
        }
    }

    // ================= 主流程 =================
    private static void Run()
    {
        List<CardData> cards = LoadBuiltinCards();
        CardPoolData pool = new CardPoolData();
        pool.name = "基础卡池 v1（自动转换）";
        pool.description = "由 AbilityToGraphConverter 从内置卡生成：规则图取代旧能力。";
        pool.author = "converter";
        pool.version = "1.0";
        pool.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        pool.cards = new List<CardCustomData>();

        StringBuilder rep = new StringBuilder();
        rep.AppendLine("card\tability\ttrigger\ttarget\tresult\teffects\treason");
        //★批6 勘察用：连锁能力（chain_* 资产）不是卡能力、baseline TSV 里没有它们 → 运行时 dump 真实结构
        StringBuilder chainRep = new StringBuilder();
        chainRep.AppendLine("# 连锁 / 被引用能力明细（card | ability | trigger | target | 引用关系）");
        Dictionary<string, int> reasonCount = new Dictionary<string, int>();
        int ok = 0, todo = 0, dataRows = 0, cardsWithGraph = 0;

        foreach (CardData card in cards)
        {
            CardCustomData data = CardPoolIO.CardToData(card);   //全字段（含图/语音路径留空，Phase 5 补）
            data.abilities.Clear();                              //★ 图取代旧能力
            data.effects = new List<CardEffectData>();

            if (card.abilities != null)
            {
                //★健壮性：**能力引用丢失**（资产引用断了 / 被运行时池导入覆盖）时必须**记 TODO**，
                //  不能静默跳过 —— 否则"155 卡全覆盖"里会凭空少一张而报告看不出来（实测 lava_beast）。
                for (int ai = 0; ai < card.abilities.Length; ai++)
                {
                    if (card.abilities[ai] != null)
                        continue;
                    string rsn = "能力引用丢失（abilities[" + ai + "] 为空）";
                    todo++;
                    Bump(reasonCount, rsn);
                    rep.AppendLine(card.id + "\t(引用" + ai + ")\t?\t?\ttodo\t\t" + rsn);
                    Debug.LogWarning("[转换器] " + card.id + " 的能力引用为空（abilities[" + ai + "]）→ 已记 TODO");
                }
                foreach (AbilityData ab in card.abilities)
                {
                    if (ab == null)
                        continue;
                    DumpChains(chainRep, card, ab);   //★批6 勘察（只写有链/被引用能力的能力）
                    string reason;
                    //★ 批5 先走"数据直通"（状态/光环/装备）：规则图表达不了的原生语义，保真优先
                    AbilityCustomData pdto;
                    if (TryPassthrough(ab, out pdto, out reason))
                    {
                        data.abilities.Add(pdto);
                        dataRows++;
                        rep.AppendLine(card.id + "\t" + ab.id + "\t" + ab.trigger + "\t" + ab.target + "\tdata\t\t" + reason);
                        continue;
                    }
                    if (!string.IsNullOrEmpty(reason))
                    {
                        todo++;
                        Bump(reasonCount, reason);
                        rep.AppendLine(card.id + "\t" + ab.id + "\t" + ab.trigger + "\t" + ab.target + "\ttodo\t\t" + reason);
                        continue;
                    }
                    CardEffectData eff = TryConvert(card, ab, out reason);
                    if (eff != null)
                    {
                        data.effects.Add(eff);
                        ok++;
                        rep.AppendLine(card.id + "\t" + ab.id + "\t" + ab.trigger + "\t" + ab.target + "\tok\t"
                            + eff.graph.nodes.Count + " 节点\t" + reason);   //reason 非空=走了"数据直通"类通道，便于审计
                    }
                    else
                    {
                        todo++;
                        Bump(reasonCount, reason);
                        rep.AppendLine(card.id + "\t" + ab.id + "\t" + ab.trigger + "\t" + ab.target + "\ttodo\t\t" + reason);
                    }
                }
            }
            if (data.effects.Count > 0)
                cardsWithGraph++;
            pool.cards.Add(data);

            //—— 诊断（临时）：资产实例 vs 静态表里的同名卡，看是不是被"运行时池导入"顶掉了 ——
            if (card.id == "lava_beast")
            {
                CardData got = CardData.Get(card.id);
                Debug.Log("[转换器][诊断] lava_beast 资产实例 abilities="
                    + (card.abilities == null ? "null" : card.abilities.Length.ToString())
                    + "；CardData.Get 同一实例=" + (ReferenceEquals(got, card))
                    + "，其 abilities=" + (got == null ? "(null卡)" : (got.abilities == null ? "null" : got.abilities.Length.ToString()))
                    + "，type=" + (got != null ? got.type.ToString() : "-"));
                if (got != null && !ReferenceEquals(got, card) && got.abilities != null)
                    for (int gi = 0; gi < got.abilities.Length; gi++)
                        Debug.Log("[转换器][诊断]   Get.abilities[" + gi + "]=" + (got.abilities[gi] == null ? "null" : got.abilities[gi].id));
            }
        }

        File.WriteAllText(PoolPath, JsonUtility.ToJson(pool, true), new UTF8Encoding(false));
        rep.AppendLine("# 汇总：卡=" + cards.Count + "（含图的卡 " + cardsWithGraph + "）｜能力 ok=" + ok + "｜data直通=" + dataRows + "｜todo=" + todo);
        rep.AppendLine("# TODO 原因分布：");
        foreach (KeyValuePair<string, int> kv in reasonCount)
            rep.AppendLine("#   " + kv.Key + " = " + kv.Value);
        File.WriteAllText(ReportPath, rep.ToString(), new UTF8Encoding(false));
        File.WriteAllText(ChainReportPath, chainRep.ToString(), new UTF8Encoding(false));   //★批6 勘察产物

        Debug.Log("[转换器] 完成：卡=" + cards.Count + "（含图 " + cardsWithGraph + "）能力 ok=" + ok + " todo=" + todo
            + " → " + PoolPath);
    }

    // ================= 批6 勘察：连锁 / 被引用能力 dump =================
    private static void DumpChains(StringBuilder sb, CardData card, AbilityData ab)
    {
        List<string> refs = new List<string>();
        if (ab.chain_abilities != null)
        {
            for (int i = 0; i < ab.chain_abilities.Length; i++)
                refs.Add("chain" + (i + 1) + "=" + DescribeAbility(ab.chain_abilities[i], 0));
        }
        if (ab.effects != null)
        {
            foreach (EffectData e in ab.effects)
            {
                if (e == null)
                    continue;
                EffectRepeat rp = e as EffectRepeat;
                if (rp != null)
                {
                    AbilityData ra = GetAbilityField(e, "ability");
                    refs.Add("repeatType=" + GetIntField(e, "type") + " repeatAbility=" + DescribeAbility(ra, 0));
                }
            }
        }
        if (refs.Count == 0)
            return;
        sb.AppendLine(card.id + "\t" + ab.id + "\t" + ab.trigger + "\t" + ab.target + "\t" + string.Join(" || ", refs.ToArray()));
    }

    private static string DescribeAbility(AbilityData a, int depth)
    {
        if (a == null)
            return "(空)";
        StringBuilder b = new StringBuilder();
        b.Append(a.id).Append(" trig=").Append(a.trigger).Append(" tgt=").Append(a.target)
            .Append(" val=").Append(a.value).Append(" dur=").Append(a.duration);
        if (a.multi_target)
            b.Append(" multi");
        if (a.mana_cost != 0)
            b.Append(" mana_cost=").Append(a.mana_cost);
        if (a.conditions_trigger != null && a.conditions_trigger.Length > 0)
            b.Append(" condT=[").Append(JoinTypes(a.conditions_trigger)).Append(']');
        if (a.conditions_target != null && a.conditions_target.Length > 0)
            b.Append(" condG=[").Append(JoinTypes(a.conditions_target)).Append(']');
        if (a.filters_target != null && a.filters_target.Length > 0)
            b.Append(" filt=[").Append(JoinTypes(a.filters_target)).Append(']');
        if (a.status != null && a.status.Length > 0)
            b.Append(" st=[").Append(JoinTypes(a.status)).Append(']');
        b.Append(" eff=[");
        if (a.effects != null)
        {
            for (int i = 0; i < a.effects.Length; i++)
            {
                if (i > 0)
                    b.Append('+');
                b.Append(a.effects[i] != null ? a.effects[i].GetType().Name : "null");
                EffectAddStat asx = a.effects[i] as EffectAddStat;
                if (asx != null)
                    b.Append("(type=").Append((int)asx.type).Append(')');
                EffectDamage dm = a.effects[i] as EffectDamage;
                if (dm != null && dm.bonus_keyword != null)
                    b.Append("(kw=").Append(dm.bonus_keyword.id).Append(')');
                EffectSendPile sp = a.effects[i] as EffectSendPile;
                if (sp != null)
                    b.Append("(pile=").Append((int)sp.pile).Append(')');
            }
        }
        b.Append(']');
        //引用能力自身的链（只展开一层，避免环）
        if (depth < 1 && a.chain_abilities != null && a.chain_abilities.Length > 0)
        {
            b.Append(" chain=[");
            for (int i = 0; i < a.chain_abilities.Length; i++)
            {
                if (i > 0)
                    b.Append(", ");
                b.Append(DescribeAbility(a.chain_abilities[i], depth + 1));
            }
            b.Append(']');
        }
        return b.ToString();
    }

    private static string JoinTypes(ScriptableObject[] arr)
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] == null)
                continue;
            if (b.Length > 0)
                b.Append(',');
            b.Append(arr[i].GetType().Name);
            ConditionData cd = arr[i] as ConditionData;
            if (cd != null)
            {
                FieldInfoStack(b, cd, "oper");
                FieldInfoStack(b, cd, "type");
                FieldInfoStack(b, cd, "value");
            }
            FilterData fd = arr[i] as FilterData;
            if (fd != null)
                FieldInfoStack(b, fd, "amount");
        }
        return b.ToString();
    }

    /// <summary>把某字段以(name=value)追加（取不到就跳过），仅用于勘察 dump</summary>
    private static void FieldInfoStack(StringBuilder b, object obj, string field)
    {
        try
        {
            System.Reflection.FieldInfo fi = obj.GetType().GetField(field);
            if (fi == null)
                return;
            object v = fi.GetValue(obj);
            if (v == null)
                return;
            b.Append(field).Append('=').Append(Convert.ToInt32(v)).Append(';');
        }
        catch { }
    }

    private static void Bump(Dictionary<string, int> d, string k)
    {
        if (string.IsNullOrEmpty(k)) k = "(未分类)";
        d[k] = d.ContainsKey(k) ? d[k] + 1 : 1;
    }

    // ================= 批5：数据直通（状态 / 光环 / 装备） =================
    // 依据 Phase 1 定案："规则（入口/条件/目标/动作链）进规则图，数据（状态/数值/资源）留 DTO 字段"。
    // 返回值约定：true=已直通（dto 可用）；false 且 reason 为空=不属于直通范围（调用方继续走图转换）；
    //             false 且 reason 非空=属于范围但不可直通（调用方记 todo）。
    private static bool TryPassthrough(AbilityData ab, out AbilityCustomData dto, out string reason)
    {
        dto = null;
        reason = "";
        bool ongoing = ab.trigger == AbilityTrigger.Ongoing;
        bool hasStatus = ab.status != null && ab.status.Length > 0;
        int effCount = (ab.effects != null) ? ab.effects.Length : 0;

        //★C 批（批6 连锁）定案：链条走**数据直通**（CardEffectData/AbilityCustomData 的 chain_ability_ids，
        //  编译侧还原成 ab.chain_abilities）——引擎在 AfterAbilityResolved 里逐个 TriggerCardAbility，
        //  属引擎侧行为，与"图/直通"两个通道无关。故这里不再拦连锁。
        if (!ongoing && !hasStatus)
            return false;                                        //非批5范围 → 继续图转换
        if (!ongoing && hasStatus && effCount > 0)
        {
            //★B 批：状态+效果混合不再记 TODO —— 状态随效果数据带走（CardEffectData.status_ids），
            //  编译侧还原成 ab.status（引擎在 DoEffects 里按目标施加，旧行为逐行一致）。
            //  这里必须返回 **reason 为空** 的 false，否则调用方会直接记 TODO、根本不进图转换。
            return false;
        }

        dto = new AbilityCustomData();
        dto.id = ab.id;
        dto.trigger = ab.trigger.ToString();
        dto.target = ab.target.ToString();
        dto.value = ab.value;
        dto.duration = ab.duration;
        dto.mana_cost = ab.mana_cost;
        dto.exhaust = ab.exhaust;
        dto.title = ab.title;
        dto.desc = ab.desc;
        if (ab.status != null)
        {
            foreach (StatusData s in ab.status)
                if (s != null)
                    dto.status_ids.Add(s.effect.ToString());
        }

        //组件全保真：类名 + 反射字段（ReflectionUtil：StatusData→枚举名、数据资产→id、资源引用跳过）
        dto.effects = CardPoolIO.SerializeComponents(ab.effects);
        dto.conditions_trigger = CardPoolIO.SerializeComponents(ab.conditions_trigger);
        dto.conditions_target = CardPoolIO.SerializeComponents(ab.conditions_target);
        dto.filters_target = CardPoolIO.SerializeComponents(ab.filters_target);

        //---- 不动点校验：还原 → 再序列化，字段逐一相同才算保真（防"引用字段悄悄变默认值"）----
        List<ComponentCustomData>[] roundtrip =
        {
            CardPoolIO.DeserializeComponents<EffectData>(dto.effects) != null
                ? CardPoolIO.SerializeComponents(CardPoolIO.DeserializeComponents<EffectData>(dto.effects)) : null,
            CardPoolIO.SerializeComponents(CardPoolIO.DeserializeComponents<ConditionData>(dto.conditions_trigger)),
            CardPoolIO.SerializeComponents(CardPoolIO.DeserializeComponents<ConditionData>(dto.conditions_target)),
            CardPoolIO.SerializeComponents(CardPoolIO.DeserializeComponents<FilterData>(dto.filters_target))
        };
        List<ComponentCustomData>[] original =
        {
            dto.effects, dto.conditions_trigger, dto.conditions_target, dto.filters_target
        };
        for (int i = 0; i < original.Length; i++)
        {
            if (!SameComponents(original[i], roundtrip[i]))
            {
                dto = null;
                reason = "直通往返校验失败（第 " + i + " 组组件序列化不保真）";
                return false;
            }
        }

        reason = ongoing ? "旧能力直通（Ongoing 光环/装备/持续：Ongoing 管线不走图，原生语义保真）"
                         : "状态直通（纯数据，无规则可图）";
        return true;
    }

    /// <summary>两组组件 DTO 是否字段级相同（不动点校验用）</summary>
    private static bool SameComponents(List<ComponentCustomData> a, List<ComponentCustomData> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].type != b[i].type || a[i].fields.Count != b[i].fields.Count)
                return false;
            for (int j = 0; j < a[i].fields.Count; j++)
            {
                if (a[i].fields[j].name != b[i].fields[j].name
                    || a[i].fields[j].value != b[i].fields[j].value)
                    return false;
            }
        }
        return true;
    }

    private static List<CardData> LoadBuiltinCards()
    {
        //Play 模式下没有 AssetDatabase → 用 Resources.LoadAll 取内置卡（内含自定义池的卡不在 Resources 里 ✓）
        List<CardData> list = new List<CardData>();
        CardData[] all = Resources.LoadAll<CardData>("");
        foreach (CardData c in all)
        {
            if (c == null || string.IsNullOrEmpty(c.id))
                continue;
            string assetPath = c.name;      //仅用于日志
            list.Add(c);
            if (list.Count <= 3)
                Debug.Log("[转换器] 载入卡: " + c.id + "（资产名 " + assetPath + "，能力 " + (c.abilities != null ? c.abilities.Length : 0) + "）");
        }
        list.Sort((a, b) => string.CompareOrdinal(a != null ? a.id : "", b != null ? b.id : ""));
        return list;
    }

    // ================= 单个能力转换 =================
    /// <summary>先按"条件进图守卫"转一次；失败**且该能力带条件**时，再按"条件走数据直通"转一次
    /// （图只表达动作，条件原样交回引擎判定 —— 见 CardEffectData.conditions_target 的说明）。
    /// 目的：图里表达不了的条件（ConditionCount/SlotRange/阵营·种族/SelectedValue…）不再让整条能力作废；
    /// 已迁移成功的能力第一次就返回，形状完全不变。</summary>
    private static CardEffectData TryConvert(CardData card, AbilityData ab, out string reason)
    {
        //★卡牌选择器（CardSelector）/ 逐槽结算（AllSlots）：目标集合**完全**由引擎按 conditions_target +
        //  filters_target 解析（选择器候选列表 / 槽位），图守卫既替代不了、对"槽位"语境也没有取值通道
        //  → 这类能力直接走"条件数据直通"（不进图守卫）；槽位由 EffectRunGraph→Run(target_slot) 带进图内。
        if (ab.target == AbilityTarget.CardSelector || ab.target == AbilityTarget.AllSlots)
            return TryConvertOnce(card, ab, true, out reason);
        CardEffectData first = TryConvertOnce(card, ab, false, out reason);
        if (first != null)
            return first;
        bool hasCond = (ab.conditions_trigger != null && ab.conditions_trigger.Length > 0)
            || (ab.conditions_target != null && ab.conditions_target.Length > 0);
        if (!hasCond)
            return null;
        string r2;
        CardEffectData second = TryConvertOnce(card, ab, true, out r2);
        if (second != null)
        {
            reason = "条件走数据直通（图里表达不了：" + (string.IsNullOrEmpty(reason) ? "见转换器" : reason) + "）";
            return second;
        }
        reason = r2;    //第二次的原因更贴近真实缺口（不含"条件表达不了"）
        return null;
    }

    /// <summary>skipGuards=true：条件不建图守卫，随效果数据原样带走（编译侧还原成 ab.conditions_*）。</summary>
    private static CardEffectData TryConvertOnce(CardData card, AbilityData ab, bool skipGuards, out string reason)
    {
        reason = "";

        // ---- 分批闸门 ----
        //★ D 批：目标过滤**不再拦**——过滤器作用于"整个目标集合"，图是"目标相对"的（引擎逐目标 Run），
        //  两者天然互补；引擎本来就在解析目标集合时应用 filters_target（AbilityData.GetCardTargets 等）。
        //  定案：过滤器走**数据直通**（CardEffectData.filters_target，编译侧还原到 ab.filters_target），
        //  见 TryConvert 末尾写回。故这里不再记 TODO。
        //★C 批：连锁能力**不再拦**——链条随效果数据带走（CardEffectData.chain_ability_ids），
        //  编译侧还原成 ab.chain_abilities；引擎 AfterAbilityResolved 会照旧逐个 TriggerCardAbility。
        //条件：v2 支持"单条"条件（多条件的组合逻辑待补 → 记 TODO，不猜组合语义）
        int nTrigCond = (ab.conditions_trigger != null) ? ab.conditions_trigger.Length : 0;
        int nTargCond = (ab.conditions_target != null) ? ab.conditions_target.Length : 0;
        //条件：v2 支持多条（旧系统语义=全部满足 → 用 112005 逻辑运算「且」串起来）
        //★B 批：能力自带 status（"状态+效果混合"）不再拦 —— 随效果数据带走（CardEffectData.status_ids），
        //  编译侧还原成 ab.status，由引擎在 DoEffects 里对每个已解析目标施加（与旧行为逐行一致）
        if (ab.duration != 0) { reason = "含时长（状态类，批5）"; return null; }
        if (ab.multi_target || (ab.target_slots != null && ab.target_slots.Length > 0)) { reason = "多目标槽（批6）"; return null; }

        string entryAction, entryField, entryValue;
        if (!TryEntryAction(ab, out entryAction, out entryField, out entryValue, out reason))
            return null;

        //★入口节点标题：**一律用中文入口名**（编辑器节点库的词汇）。
        //  以前直接拿旧能力的 title 当标题 → 旧数据里它常为空（节点显示成裸 action 名 OnPlay/PassiveEffect），
        //  或只是半句话（"回合结束时，"）→ 用户看图会以为连错了（实测 cave/bone_warrior 被当场抓到）。
        string entryAbilityTitle;
        string entryDisplayTitle = EntryDisplayTitle(entryAction, ab.title, out entryAbilityTitle);

        string targetMode;
        if (!TryTargetMode(ab, out targetMode, out reason))
            return null;

        //★C 批：**只带连锁的"壳能力"**是合法形状 —— 典型是"选择菜单"入口（bear：
        //  target=ChoiceSelector + chain_abilities=[选项A,选项B]，入口自身没有效果，菜单由引擎 SelectChoice 消费）。
        //  只要带链条就允许建图（图里只有入口节点，动作由链条承担）；无效果又无链条才记 TODO。
        bool shell_with_chains = (ab.effects == null || ab.effects.Length == 0)
            && ab.chain_abilities != null && ab.chain_abilities.Length > 0;
        if ((ab.effects == null || ab.effects.Length == 0) && !shell_with_chains)
        { reason = "无效果（纯状态/壳能力）"; return null; }

        // ---- 建图 ----
        GraphData g = new GraphData();
        g.name = card.id + "：" + (string.IsNullOrEmpty(ab.title) ? ab.id : ab.title);
        g.nodes = new List<GraphNode>();
        g.links = new List<GraphLink>();

        GraphNode ev = NewNode(g, "ev", GraphNodeType.Event, entryAction, entryDisplayTitle);
        AddPin(ev, "out", NodeValueType.Flow, true);
        AddPin(ev, "card", NodeValueType.Card, true);
        AddPin(ev, "value", NodeValueType.Int32, true);
        AddPin(ev, "target", NodeValueType.Object, true);   //★ 目标上下文的条件链用它拿"当前目标"（GetObjectInput 事件分支：target→target_card）
        SetField(ev, "target_mode", targetMode);      //★ 编译侧新增：显式目标模式（可逆）
        SetField(ev, "ability_title", entryAbilityTitle);   //编译侧用它当能力名（所有入口都读，见 CardPoolIO.ApplyEntryOverrides）
        //入口节点的"入口专属字段"：被动效果入口要写 标签列表=亡语、事件效果入口要写 监听事件
        //（编译侧 ResolveEventTrigger 靠它们判触发时机；不写会被判 None → 整条能力静默跳过）
        if (!string.IsNullOrEmpty(entryField))
            SetField(ev, entryField, entryValue);
        if (entryAction == "ActivateAbility")
        {
            SetField(ev, "mana_cost", ab.mana_cost.ToString());
            SetField(ev, "exhaust", ab.exhaust ? "true" : "false");
            SetField(ev, "ability_desc", ab.desc ?? "");
        }

        GraphNode prev = ev;
        string firstPin = "out";
        GraphNode filterNode = null;

        // ①【已废弃：筛选自定位形状】多目标 + 目标条件曾用 筛选(111012) 自定位集合——
        //   但引擎编译链（CardPoolIO.CompileGraphAbilities L848-853）把 ab.target=入口 target_mode 原样带给能力，
        //   真实对局走"引擎逐目标解析 → EffectRunGraph.DoEffect(每目标一次)"，自定位图会被重复施加
        //   （实测 spell_growth +2 变 +6：3 目标 × 每次 Run 又遍历全集合）。
        //   ★ 定案：目标条件一律走 ③ 的「分支守卫」形状（card 口不接 → fallback target_card，每次 Run 守自己的目标），
        //     目标集合由引擎 + conditions_target 解析；filterNode 恒 null → 复合加属性走 target-relative 简单形状。

        // ② 触发条件 → 分支动作(212001)。★只保留**真正在触发期求值**的条件（重写了
        //    IsTriggerConditionMet(Game,AbilityData,Card)）；ConditionOwner/ConditionCardType 等
        //    只重写了目标判定 → 旧引擎触发期走基类恒真（空转）→ 照搬进图会拿 caster 当判定对象恒假，
        //    把分支挡死（实测 trap_damage_all2 踩到）。空转条件跳过 = 与旧引擎行为一致。
        bool once_per_turn = false;      //入口「每回合限一次」标记（见下 ConditionOnce 分支）
        List<ConditionData> liveTrigConds = new List<ConditionData>();
        if (!skipGuards && ab.conditions_trigger != null)   //skipGuards：条件整组交给引擎，不建守卫（也不设 once_per_turn）
        {
            foreach (ConditionData c in ab.conditions_trigger)
            {
                if (c == null || !IsLiveTriggerCondition(c))
                    continue;
                //★ ConditionOnce（每回合限一次）：**发动时**由引擎判定（读 ability_played，每回合由
                //  ClearTurnData 清空），图里没有等价取值节点；且效果期它已被 Add 进去 → 图里判必恒假。
                //  定案：走"数据型触发条件"——入口写 once_per_turn 字段，编译侧（CardPoolIO）给编译出的
                //  能力挂回 ConditionOnce → CanCastAbility/TriggerCardAbility 的 AreTriggerConditionsMet
                //  在发动前判定，与旧行为逐行一致。故此处**不进图**。
                if (c.GetType().Name == "ConditionOnce")
                {
                    once_per_turn = true;
                    continue;
                }
                liveTrigConds.Add(c);
            }
        }
        //条件已随数据带走时（见下方 conditions_* 直通），ConditionOnce 就在携带的数组里 →
        //不再写入口标记，否则编译侧会挂两个 ConditionOnce（行为一样但结构脏）
        if (once_per_turn && (ab.conditions_trigger == null || ab.conditions_trigger.Length == 0))
            SetField(ev, "once_per_turn", "true");
        //★B 批：能力自带 status 时，旧引擎用的是**该能力的 value**（`target.AddStatus(stat, ability.value, ...)`）
        //  → 把 value 写进入口字段，编译侧还原成 ab.value，否则状态数值会静默变 0（时长≠0 的能力仍被上面的闸门挡住）
        if (ab.status != null && ab.status.Length > 0)
            SetField(ev, "status_ability_value", ab.value.ToString());
        if (liveTrigConds.Count > 0)
        {
            //触发期条件判定的对象是**施法卡**（不是本次目标）→ slotTarget=false
            GraphNode cond = EmitConditionChain(g, liveTrigConds.ToArray(), null, true, ev, false, out reason);
            if (cond == null)
                return null;
            GraphNode br = ChainFrom(g, prev, firstPin, "br", "212001", "分支动作");
            AddPin(br, "isTrue", NodeValueType.Boolean, false);
            AddPin(br, "thenAction", NodeValueType.Flow, true);
            Link(g, cond, "result", br, "isTrue");
            prev = br;
            firstPin = "thenAction";
        }
        if (!skipGuards && nTargCond > 0 && filterNode == null)
        {
            GraphNode cond = EmitConditionChain(g, ab.conditions_target, null, false, ev,
                ab.target == AbilityTarget.AllSlots, out reason);
            if (cond == null)
                return null;
            GraphNode br = ChainFrom(g, prev, firstPin, "br2", "212001", "分支动作");
            AddPin(br, "isTrue", NodeValueType.Boolean, false);
            AddPin(br, "thenAction", NodeValueType.Flow, true);
            Link(g, cond, "result", br, "isTrue");
            prev = br;
            firstPin = "thenAction";
        }

        int idx = 0;
        foreach (EffectData eff in ab.effects)
        {
            if (eff == null)
                continue;
            idx++;
            GraphNode act = EmitEffect(g, prev, firstPin, filterNode, eff, ab, idx, ref reason);
            if (act == null)
                return null;          //reason 已填（整条能力作废，不产出半截图）
            prev = act;
            firstPin = "out";         //后续动作固定走 out
        }

        CardEffectData outEff = new CardEffectData();
        outEff.name = string.IsNullOrEmpty(ab.title) ? ("效果" + (g.nodes.Count > 0 ? "1" : "?")) : ab.title;
        outEff.graph = g;
        //★ D 批：目标过滤器随效果数据带出（编译侧还原到 ab.filters_target，引擎解析目标集合时应用）。
        //  空/缺省不写 → 旧图 JSON 形状不变。
        if (ab.filters_target != null && ab.filters_target.Length > 0)
            outEff.filters_target = CardPoolIO.SerializeComponents(ab.filters_target);
        //★条件数据直通（**一律带走**，与图守卫并存）：把**原始**条件数组交回引擎 ——
        //  引擎在发动前（AreTriggerConditionsMet）与解析目标集合/选择器候选（AreTargetConditionsMet）时判定。
        //  为什么一律带走：这些条件同时决定「目标合法性（能否被选中/高亮）」与「CardSelector 候选列表」，
        //  图守卫只能事后挡住、替代不了；原样交回又 = 与旧能力逐行一致（含"触发期空转"的那些：
        //  旧引擎也带着它们求值=恒真）。图守卫（能表达时）保留为图内可读表达，两者不冲突。
        if (ab.conditions_trigger != null && ab.conditions_trigger.Length > 0)
            outEff.conditions_trigger = CardPoolIO.SerializeComponents(ab.conditions_trigger);
        if (ab.conditions_target != null && ab.conditions_target.Length > 0)
            outEff.conditions_target = CardPoolIO.SerializeComponents(ab.conditions_target);
        //★C 批：连锁能力随数据带走（编译侧还原成 ab.chain_abilities → 引擎 AfterAbilityResolved 逐个触发）
        if (ab.chain_abilities != null && ab.chain_abilities.Length > 0)
        {
            outEff.chain_ability_ids = new List<string>();
            foreach (AbilityData ch in ab.chain_abilities)
                if (ch != null)
                    outEff.chain_ability_ids.Add(ch.id);
        }
        //★能力自带状态（"状态+效果混合"）随数据带走：编译侧还原成 ab.status（引擎按目标施加，同旧行为）
        if (ab.status != null && ab.status.Length > 0)
        {
            outEff.status_ids = new List<string>();
            foreach (StatusData s in ab.status)
                if (s != null)
                    outEff.status_ids.Add(s.effect.ToString());
        }
        return outEff;
    }

    /// <summary>旧触发时机 → **编辑器节点库里的入口节点**（分类「入口」的 zmcs 入口）。
    /// ★铁律：入口 action **必须**用编辑器认识的名字，不能写 AbilityTrigger 的原始名：
    ///   节点库里只有 主动效果入口(ActivateEffect)/起动式效果入口(ActivateAbility)/光环效果入口(AuraEffect)/
    ///   被动效果入口(PassiveEffect + 标签列表=亡语)/事件效果入口(EventEffect + 监听事件)；
    ///   写 "OnPlay"/"OnDeath"/"StartOfTurn" 这类原始名，编辑器显示为**裸 id**（用户看不懂），
    ///   而且拿不到入口专属 UI（目标槽、触发条件、标签）→ 用户会问"这不是有主动效果吗，怎么做了个 onplay"。
    /// 编译侧对应：MapGraphTrigger(ActivateEffect→OnPlay、ActivateAbility→Activate)、
    ///   ResolveEventTrigger(PassiveEffect+亡语→OnDeath、EventEffect+监听事件→MapEventName)、
    ///   MapEventName(回合开始→StartOfTurn / 回合结束→EndOfTurn / 攻击时→OnBeforeAttack /
    ///   抽到时→OnDraw / 打出牌→OnPlayOther / 死亡时→OnDeath)。
    /// 没有等价入口名的（OnAfterAttack / OnKill / OnDeathOther）只能写原始名：编译侧认识，编辑器显示为裸 id。</summary>
    private static bool TryEntryAction(AbilityData ab, out string action, out string extraField, out string extraValue, out string reason)
    {
        reason = "";
        extraField = "";
        extraValue = "";
        switch (ab.trigger)
        {
            case AbilityTrigger.OnPlay: action = "ActivateEffect"; return true;         //主动效果入口（战吼/法术：打出时触发）
            case AbilityTrigger.Activate: action = "ActivateAbility"; return true;      //起动式效果入口（点击发动）
            case AbilityTrigger.OnDeath:                                                //被动效果入口（亡语）
                action = "PassiveEffect"; extraField = "tag_list"; extraValue = "亡语"; return true;
            case AbilityTrigger.StartOfTurn:                                            //事件效果入口：回合开始
                action = "EventEffect"; extraField = "event_name"; extraValue = "回合开始"; return true;
            case AbilityTrigger.EndOfTurn:                                              //事件效果入口：回合结束
                action = "EventEffect"; extraField = "event_name"; extraValue = "回合结束"; return true;
            case AbilityTrigger.OnDraw:                                                 //事件效果入口：抽到时
                action = "EventEffect"; extraField = "event_name"; extraValue = "抽到时"; return true;
            case AbilityTrigger.OnBeforeAttack:                                         //事件效果入口：攻击时
                action = "EventEffect"; extraField = "event_name"; extraValue = "攻击时"; return true;
            case AbilityTrigger.OnPlayOther:                                            //事件效果入口：打出牌（别的卡被打出时）
                action = "EventEffect"; extraField = "event_name"; extraValue = "打出牌"; return true;
            //—— 以下无 zmcs 入口名：编译侧 MapGraphTrigger 按同名映射（编辑器里会显示成裸 id）——
            case AbilityTrigger.OnAfterAttack: action = "OnAfterAttack"; return true; //攻击结算后且自己还活着
            case AbilityTrigger.OnKill: action = "OnKill"; return true;               //攻击中击杀别的卡时
            case AbilityTrigger.OnDeathOther: action = "OnDeathOther"; return true;   //别的卡死亡时
            default: action = ""; reason = "触发时机未支持（P1）：" + ab.trigger; return false;
        }
    }

    /// <summary>入口节点显示标题：**中文入口名**（编辑器节点库词汇）+（可选）旧能力标题后缀。
    /// 为什么不能直接用旧 title：旧数据里它常为空（节点显示成裸 action 名 OnPlay/PassiveEffect），
    /// 或只是半句话（"回合结束时，"）→ 看图的人会以为连错了。
    /// ability_title 输出给编译侧当**能力名**（`graph_xxx：规则图执行` 那种名字很难看）。</summary>
    private static string EntryDisplayTitle(string action, string abTitle, out string abilityTitle)
    {
        string name;
        switch (action)
        {
            case "ActivateEffect": name = "主动效果入口"; break;
            case "ActivateAbility": name = "起动式效果入口"; break;
            case "PassiveEffect": name = "被动效果入口"; break;
            case "EventEffect": name = "事件效果入口"; break;
            case "OnAfterAttack": name = "攻击后入口"; break;
            case "OnKill": name = "击杀入口"; break;
            case "OnDeathOther": name = "他人死亡入口"; break;
            default: name = action; break;
        }
        abilityTitle = IsGoodAbilityTitle(abTitle) ? abTitle.Trim() : name;
        return abilityTitle == name ? name : (name + "：" + abilityTitle);
    }

    /// <summary>旧能力标题是否像"正常技能名"：非空、≤12 字、不以标点结尾（旧数据里有 "回合结束时，" 这种半句）</summary>
    private static bool IsGoodAbilityTitle(string t)
    {
        if (string.IsNullOrEmpty(t))
            return false;
        t = t.Trim();
        if (t.Length == 0 || t.Length > 12)
            return false;
        if (t.Contains("\n") || t.Contains("\r"))
            return false;
        char last = t[t.Length - 1];
        if (last == '，' || last == ',' || last == '。' || last == '.' || last == '：' || last == ':' || last == '、')
            return false;
        return true;
    }

    private static bool TryTargetMode(AbilityData ab, out string mode, out string reason)
    {
        reason = "";
        switch (ab.target)
        {
            case AbilityTarget.None: mode = "无"; return true;
            case AbilityTarget.Self: mode = "自身"; return true;
            case AbilityTarget.PlayerSelf: mode = "施法者玩家"; return true;
            case AbilityTarget.PlayerOpponent: mode = "对手玩家"; return true;
            case AbilityTarget.AllPlayers: mode = "全体玩家"; return true;
            case AbilityTarget.AllCardsBoard: mode = "所有角色"; return true;
            case AbilityTarget.AllCardsHand: mode = "双方手牌"; return true;
            case AbilityTarget.AllCardsAllPiles: mode = "所有牌堆"; return true;
            case AbilityTarget.AllCardData: mode = "卡牌定义"; return true;
            case AbilityTarget.SelectTarget: mode = "选择目标"; return true;
            case AbilityTarget.PlayTarget: mode = "打出目标"; return true;
            //★ D 批：触发者（陷阱/反击类）——编译侧 ParseTargetMode("触发者") 已有，
            //  运行期由引擎把 ability_triggerer 解析为本次目标（GameLogic.ResolveCardAbility 里
            //  game_data.ability_triggerer = triggerer.uid → GetCardTargets 的 AbilityTriggerer 分支）。
            case AbilityTarget.AbilityTriggerer: mode = "触发者"; return true;
            //★ D 批：卡牌选择器（从筛过的候选里选一张；候选=条件+过滤器筛过的全区卡，见 AbilityData.GetCardTargets）
            case AbilityTarget.CardSelector: mode = "卡牌选择"; return true;
            //★C 批：选择菜单（ChoiceSelector）——**chain_abilities 就是菜单选项**，由引擎 SelectChoice 消费；
            //  入口自身通常没有效果（如 bear 的"二选一：嘲讽 / 狂暴"）。编译侧 ParseTargetMode("选择器") 已有。
            case AbilityTarget.ChoiceSelector: mode = "选择器"; return true;
            //★ D 批：逐槽结算（槽位集合由引擎按 conditions_target + filters_target 筛；槽位随 Run(target_slot) 进图）
            case AbilityTarget.AllSlots: mode = "所有槽位"; return true;
            default: mode = ""; reason = "目标模式未支持：" + ab.target; return false;
        }
    }

    // ================= 效果 → 节点 =================
    private static GraphNode EmitEffect(GraphData g, GraphNode prev, string firstPin, GraphNode filterNode, EffectData eff, AbilityData ab, int idx, ref string reason)
    {
        string cls = eff.GetType().Name;
        string a = "n" + idx;
        GraphNode act;

        switch (cls)
        {
            case "EffectDamage":
            {
                EffectDamage d = (EffectDamage)eff;
                if (d.bonus_keyword != null)
                {
                    //★ 202041 造成伤害或法伤：旧语义=整体按法伤结算（GetDamage=base+法伤加成 →
                    //  DamageCard(..., true)，法伤部分含加成），202041 damagetype=true 同口径
                    act = ChainFrom(g, prev, firstPin, a, "202041", "造成伤害或法伤",
                        "damage", ab.value.ToString(), "damagetype", "true");
                    break;
                }
                act = ChainFrom(g, prev, firstPin, a, "202001", "造成伤害", "value", ab.value.ToString());
                break;
            }
            case "EffectSetStat":
            {
                //设基础值：攻击/法力 → 202037（语义完全一致）；生命 → 项目内动作（旧 SetStat 会清 damage 计数）
                EffectSetStat ss = (EffectSetStat)eff;
                if (ab.target == AbilityTarget.PlayerSelf || ab.target == AbilityTarget.PlayerOpponent
                    || ab.target == AbilityTarget.AllPlayers)
                { reason = "SetStat 目标为玩家（无玩家属性设置节点）"; return null; }
                if (ss.type == EffectStatType.HP)
                {
                    //★B 批：hp=value; damage=0（逐行等价旧 EffectSetStat(HP)）
                    act = ChainFrom(g, prev, firstPin, a, "SetCardHpClearDamage", "设置生命值(清伤害)",
                        "value", ab.value.ToString());
                    break;
                }
                if (ss.type != EffectStatType.Attack && ss.type != EffectStatType.Mana)
                { reason = "SetStat 类型未支持：" + ss.type; return null; }
                act = ChainFrom(g, prev, firstPin, a, "202037", "设置卡牌属性",
                    "propName", ss.type == EffectStatType.Attack ? "攻击" : "法力",
                    "value", ab.value.ToString());
                break;
            }
            case "EffectHeal":
                //★节点库审计：202047 与 202013 同名「治疗目标卡牌」，但 202047 在库里是**隐藏**的
                //  （过时/被同名顶掉，玩家选不到）→ 必须用库里可选的那个 id（202013）。
                act = ChainFrom(g, prev, firstPin, a, "202013", "治疗目标卡牌", "value", ab.value.ToString());
                break;
            case "EffectDestroy":
                act = ChainFrom(g, prev, firstPin, a, "202016", "消灭");
                break;
            //★ 注意：召唤效果的**类名是 EffectAddToDeck**（文件叫 EffectSummon.cs，类名不同）。
            //★ 目的地取决于旧目标类型（EffectAddToDeck 的 4 个重载各不相同）：
            //    目标=玩家   → SummonCardHand（进**手牌**）
            //    目标=卡/槽  → SummonCard(player, summon, target.slot)（进**战场**）
            //    目标=卡牌定义 → 用"目标定义"而非 summon 字段进手牌（语义不同，单独 TODO）
            case "EffectAddToDeck":
            case "EffectSummon":
            {
                CardData sum = GetCardDataField(eff, "summon");
                if (sum == null) { reason = "召唤卡定义为空"; return null; }
                if (ab.target == AbilityTarget.AllCardData) { reason = "召唤目标为卡牌定义（用目标定义召唤），待单独映射"; return null; }
                bool toHand = (ab.target == AbilityTarget.PlayerSelf
                    || ab.target == AbilityTarget.PlayerOpponent
                    || ab.target == AbilityTarget.AllPlayers);
                act = toHand
                    ? ChainFrom(g, prev, firstPin, a, "202004", "创建衍生卡并置入手牌", "cardDefine", sum.id)
                    : ChainFrom(g, prev, firstPin, a, "202003", "创建衍生卡并置入战场", "cardDefine", sum.id);
                break;
            }
            case "EffectTransform":
            {
                //★B 批：变形为指定卡定义 → 现成节点 202029（卡牌口无连线=本次目标；定义走 define 字段）
                CardData tto = GetCardDataField(eff, "transform_to");
                if (tto == null) { reason = "变形目标定义为空（transform_to）"; return null; }
                act = ChainFrom(g, prev, firstPin, a, "202029", "变形为卡牌定义", "define", tto.id);
                break;
            }
            case "EffectAddAbility":
            {
                //★B 批：添加技能 → 202050（cards + abilityId）。定案=**池内 id 引用**：
                //  只写 ability id；被引用的能力资产此刻仍在 Resources/Abilities（Phase 5 打包时随池一起打）。
                AbilityData gain = GetAbilityField(eff, "gain_ability");
                if (gain == null) { reason = "添加技能的能力引用为空（gain_ability）"; return null; }
                act = ChainFrom(g, prev, firstPin, a, "202050", "添加技能", "abilityId", gain.id);
                break;
            }
            case "EffectExhaust":
                //★B 批：横置 / 解除横置（play_haste=解除）→ 项目内动作 SetCardExhausted（=target.exhausted 赋值）
                act = ChainFrom(g, prev, firstPin, a, "SetCardExhausted", "设置横置",
                    "exhausted", GetBoolField(eff, "exhausted") ? "true" : "false");
                break;
            case "EffectMana":
            {
                //★B 批：加当前灵力（**不钳制**）→ 项目内动作 AddManaNoClamp。
                //  只覆盖 increase_value 单开的分支（本引擎 EffectMana 三个 flag 的其它组合语义不同 → 显式 TODO，不硬套）
                bool inc_value = GetBoolField(eff, "increase_value");
                bool inc_max = GetBoolField(eff, "increase_max");
                bool inc_max_total = GetBoolField(eff, "increase_max_total");
                if (!inc_value || inc_max || inc_max_total)
                {
                    reason = "EffectMana 的组合未覆盖（increase_value=" + inc_value
                        + ",increase_max=" + inc_max + ",increase_max_total=" + inc_max_total + "）";
                    return null;
                }
                act = ChainFrom(g, prev, firstPin, a, "AddManaNoClamp", "增加当前灵力(不钳制)", "count", ab.value.ToString());
                break;
            }
            case "EffectClearStatus":
            {
                //★B 批：移除状态（status 为空 = 清空全部）→ 项目内动作 ClearCardStatus
                StatusData csd = GetStatusField(eff, "status");
                act = ChainFrom(g, prev, firstPin, a, "ClearCardStatus", "移除状态",
                    "status", csd != null ? csd.effect.ToString() : "");
                break;
            }
            case "EffectResetStat":
                //★B 批：复位到卡牌定义的基础状态 → 项目内动作 ResetCardToBase
                //  （≠ 202024 重置卡牌：那个还会清状态/增益/常驻并 Refresh，比旧 EffectResetStat 更强）
                act = ChainFrom(g, prev, firstPin, a, "ResetCardToBase", "复位卡牌");
                break;
            case "EffectPlay":
                //★ D 批：把目标卡免费打出（旧 EffectPlay：移入施法者方手牌 → 随机空槽 → PlayCard(skip_cost)）
                //  用**项目内动作** PlayCardFree 逐行等价（NodeDoc 的 210002 是 PlaceCardOnBoard，落点/触发/last_played 都不同）
                act = ChainFrom(g, prev, firstPin, a, "PlayCardFree", "免费打出卡牌");
                break;
            case "EffectCreate":
            {
                //★C 批 零头：旧 EffectCreate（从**目标卡牌定义**创建一张卡进 create_pile）→ 项目内动作
                //  CreateCardFromDefineRaw。定义来源不写字段=运行期取"本次定义"（目标模式=卡牌定义时由
                //  EffectRunGraph.DoEffect(..., CardData) → Run(target_define:) 带入）。
                //  区域名与动作的 dropdow 一致；旧实现只处理 牌库/手牌/墓地/暂存区 4 个（其它=无操作）。
                int cpile = GetIntField(eff, "create_pile");
                string cpileName;
                if (cpile == 30) cpileName = "牌库";
                else if (cpile == 40) cpileName = "墓地";
                else if (cpile == 20) cpileName = "手牌";
                else if (cpile == 90) cpileName = "暂存区";
                else { reason = "EffectCreate 的区域在旧实现里=无操作（Board 等），无法等价迁移：pile=" + cpile; return null; }
                act = ChainFrom(g, prev, firstPin, a, "CreateCardFromDefineRaw", "从定义创建衍生卡",
                    "pile", cpileName,
                    "opponent", GetBoolField(eff, "create_opponent") ? "true" : "false");
                break;
            }
            case "EffectRoll":
                //★C 批：掷骰 → 项目内动作 RollValue（旧 EffectRoll：logic.RollRandomValue(dice)，结果进 rolled_value）
                act = ChainFrom(g, prev, firstPin, a, "RollValue", "掷骰",
                    "dice", GetIntField(eff, "dice").ToString());
                break;
            case "EffectRepeat":
            {
                //★C 批：重复触发另一能力 → 项目内动作 RepeatAbilityById（旧 EffectRepeat 逐行：
                //  次数 = selected_value（SelectedValue）/ 主能力 value（FixedValue，这里直接把 value 写成 count 字段）；
                //  逐次 TriggerAbilityDelayed(ability, caster, triggerer)）
                AbilityData rip = GetAbilityField(eff, "ability");
                if (rip == null) { reason = "重复执行的能力引用为空（ability）"; return null; }
                int rtype = GetIntField(eff, "type");   //EffectRepeatType: 0=FixedValue 1=SelectedValue
                act = ChainFrom(g, prev, firstPin, a, "RepeatAbilityById", "重复触发技能",
                    "abilityId", rip.id,
                    "mode", rtype == 1 ? "选中值" : "固定值",
                    "count", ab.value.ToString());
                break;
            }
            case "EffectDraw":
                //只支持"给施法者方抽牌"的目标模式（其它模式需要显式给 player 口接线）
                if (ab.target != AbilityTarget.PlayerSelf && ab.target != AbilityTarget.None && ab.target != AbilityTarget.Self)
                { reason = "抽牌的目标模式需显式接线：" + ab.target; return null; }
                {
                    //★ 210001 简单抽牌**每次固定抽 1 张（无数量口）** → 旧 ability.value 张 = 把抽牌节点串 N 次
                    int n = Mathf.Clamp(ab.value, 1, 10);
                    GraphNode cur = prev;
                    string pin = firstPin;
                    for (int k = 0; k < n; k++)
                    {
                        cur = ChainFrom(g, cur, pin, a + "_" + k, "210001", "简单抽牌");
                        pin = "out";
                    }
                    return cur;     //抽牌按玩家生效，无需接目标集合
                }
            case "EffectSendPile":
            {
                //★B 批：改用项目内动作 SendToPileRaw（严格等价旧 EffectSendPile：移入 + Clear()，不触发 OnDraw）。
                //  旧实现是 210003/210004 等 NodeDoc 节点，但那些节点会触发抽牌/死亡链且不清卡 → 差分逮到不等价。
                //  旧效果只处理 4 个区域（牌库/手牌/墓地/暂存区），其它区域在旧实现里**什么都不做** → 这里也一样。
                int pile = GetIntField(eff, "pile");
                string pileName = PileRawName(pile);
                if (pileName == null) { reason = "旧 EffectSendPile 不支持的区域（旧实现=无操作，无法等价迁移）：pile=" + pile; return null; }
                //标题必须用**库里的节点名**（"移入区域(清空状态)"）——节点库审计要求呈现与库一致；
                //具体区域看节点的「区域」字段（PileNodeName 只用于日志/可读性，不能当标题）
                act = ChainFrom(g, prev, firstPin, a, "SendToPileRaw", "移入区域(清空状态)", "pile", pileName);
                break;
            }
            case "EffectAddStat":
            {
                int statType = GetIntField(eff, "type");
                string prop, getterProp;
                if (statType == 10) { prop = "攻击"; getterProp = "基础攻击"; }
                else if (statType == 20) { prop = "生命"; getterProp = "基础生命"; }
                else if (statType == 30) { prop = "法力费用"; getterProp = "基础费用"; }
                else { reason = "加属性子型未支持（最大灵力值硬顶）：type=" + statType; return null; }
                //★C 批：加 AbilityTriggerer（触发者）——本次目标卡就是触发者，取"基础值→加→写回"的复合结构照旧生效
                if (ab.target != AbilityTarget.Self && ab.target != AbilityTarget.SelectTarget
                    && ab.target != AbilityTarget.PlayTarget && ab.target != AbilityTarget.AllCardsBoard
                    && ab.target != AbilityTarget.AbilityTriggerer)
                { reason = "加属性的目标模式未覆盖（玩家加血等需双写 hp/hp_max）：" + ab.target; return null; }

                //复合：取基础值 → 常量 → 加法 → 写回。
                //★ 只有"写入动作(202037)"在执行链上；取值/运算节点是**取值节点**，挂在链外、用引脚连线求值
                //  （放进执行链会被 WalkNode 当动作执行 → "未支持的 NodeDoc 动作" 噪音）。
                //★ 多目标（筛选）场景必须走「遍历」：把复合结构放进循环体、逐元素取该元素自己的基础值。
                //  直接把集合接到 setter 上会让"取值器"固定读 target_card → 取到错卡（实测 +2 变成了 +5）。
                if (filterNode != null)
                {
                    GraphNode loop = ChainFrom(g, prev, firstPin, a + "loop", "211001", "遍历");
                    AddPin(loop, "array", NodeValueType.Object, false);
                    AddPin(loop, "action", NodeValueType.Flow, true);   //★ 必须 Flow：WalkFlowOutputs 只沿 Flow/None 出边走，ActionNode(13) 会被静默跳过（实测循环体一次不跑、动作数=0）
                    AddPin(loop, "element", NodeValueType.Object, true);
                    Link(g, filterNode, "return", loop, "array");

                    GraphNode lget = ChainNode(g, a + "g", "102004", "获取属性", "propName", getterProp);
                    AddPin(lget, "card", NodeValueType.Card, false);
                    Link(g, loop, "element", lget, "card");
                    GraphNode lconst = ChainNode(g, a + "c", "112003", "整数常量", "value", ab.value.ToString());
                    GraphNode ladd = Chain2(g, lget, lconst, a + "a", "112004", "整数运算", "operator", "+");
                    GraphNode lset = ChainNode(g, a + "s", "202037", "设置卡牌属性", "propName", prop);
                    AddPin(lset, "card", NodeValueType.Card, false);
                    AddPin(lset, "value", NodeValueType.Int32, false);
                    Link(g, loop, "element", lset, "card");
                    Link(g, ladd, "result", lset, "value");
                    Link(g, loop, "action", lset, "in");
                    return loop;
                }

                GraphNode get = ChainNode(g, a + "g", "102004", "获取属性", "propName", getterProp);
                GraphNode cst = ChainNode(g, a + "c", "112003", "整数常量", "value", ab.value.ToString());
                GraphNode add = Chain2(g, get, cst, a + "a", "112004", "整数运算", "operator", "+");
                GraphNode set = ChainNode(g, a + "s", "202037", "设置卡牌属性", "propName", prop);
                AddPin(set, "value", NodeValueType.Int32, false);
                Link(g, add, "result", set, "value");
                Link(g, prev, firstPin, set, "in");
                WireTargets(g, set, filterNode);
                return set;
            }
            default:
                reason = "效果未支持：" + cls;
                return null;
        }

        //★ 多目标筛选场景：必须把筛选结果接到动作的目标集合口，
        //  否则动作仍走"本次施法解析出的目标"，条件就白筛了（静默错）。
        WireTargets(g, act, filterNode);
        return act;
    }

    /// <summary>把筛选结果接到动作的目标集合口（按动作的目标口名逐一映射）</summary>
    private static void WireTargets(GraphData g, GraphNode act, GraphNode src)
    {
        if (src == null || act == null)
            return;
        string pin = null;
        switch (act.action)
        {
            case "202016": pin = "cards"; break;          //消灭
            case "202041": pin = "targets"; break;        //造成伤害或法伤
            case "202013": pin = "targets"; break;        //治疗（库内可选的「治疗目标卡牌」）
            case "202001": pin = "card"; break;           //造成伤害
            case "202037": pin = "card"; break;           //设置卡牌属性
            default: return;                               //其余动作（召唤/抽牌/移牌堆）不接受目标集合
        }
        AddPin(act, pin, NodeValueType.Object, false);
        Link(g, src, "return", act, pin);
    }

    /// <summary>多目标 → 集合提供节点（筛选的数组来源）</summary>
    private static GraphNode EmitTargetCollection(GraphData g, AbilityData ab, out string reason)
    {
        reason = "";
        switch (ab.target)
        {
            case AbilityTarget.AllCardsBoard:
            {
                //★ 必须用 102013「获取所有角色」：集合通道 ResolveCollectionNode 只支持
                //  102013 全体角色 / 102014 友方角色 / 102015 友方随从 / 102016 敌方随从 / 102017 敌方角色。
                //  ❌102012「获取所有仆从」**不在集合通道支持列表里**（实测返回空）—— 曾误改成它，已回退。
                GraphNode n = ValueNode(g, "col", "102013", "获取所有角色");
                AddPin(n, "return", NodeValueType.Object, true);
                return n;
            }
            case AbilityTarget.AllPlayers:
            {
                GraphNode n = ChainNode(g, "col", "101020", "获取所有玩家");
                AddPin(n, "return", NodeValueType.Object, true);
                return n;
            }
            default:
                reason = "多目标模式缺少集合提供节点：" + ab.target;
                return null;
        }
    }

    /// <summary>CardType 枚举值 → 中文类型名（与 NodeDocRunner.DefineTypeMatches 的口径一致）</summary>
    private static string ChineseCardTypeName(int enumValue)
    {
        switch ((CardType)enumValue)
        {
            case CardType.Character: return "随从";
            case CardType.Spell: return "法术";
            case CardType.Hero: return "英雄";
            case CardType.Artifact: return "神器";
            case CardType.Equipment: return "装备";
            case CardType.Secret: return "奥秘";
            default: return "";
        }
    }

    /// <summary>多条条件 → 单个布尔节点：1 条直接返回；多条用 112005「且」组合
    /// （旧系统语义：conditions 数组**全部满足**才算通过）。
    /// 任一条不支持 → 整条能力记 TODO（不产出半截图，避免条件被静默丢弃）。</summary>
    private static GraphNode EmitConditionChain(GraphData g, ConditionData[] conds, GraphNode elementSrc, bool forTrigger, GraphNode entry, bool slotTarget, out string reason)
    {
        reason = "";
        if (conds == null || conds.Length == 0)
            return null;
        List<GraphNode> nodes = new List<GraphNode>();
        for (int i = 0; i < conds.Length; i++)
        {
            if (conds[i] == null)
                continue;
            GraphNode c = EmitCondition(g, conds[i], elementSrc, forTrigger, "cond" + (i + 1), entry, slotTarget, out reason);
            if (c == null)
                return null;
            nodes.Add(c);
        }
        if (nodes.Count == 1)
            return nodes[0];

        //★ 112005 逻辑运算：运算符="且"，编号输入槽 **value1/value2…**（base="value"）
        GraphNode logic = ValueNode(g, "and", "112005", "逻辑运算", "operator", "且");
        AddPin(logic, "result", NodeValueType.Boolean, true);
        for (int i = 0; i < nodes.Count; i++)
        {
            string pin = "value" + (i + 1);
            AddPin(logic, pin, NodeValueType.Boolean, false);
            Link(g, nodes[i], "result", logic, pin);
        }
        return logic;
    }

    /// <summary>旧条件 → 布尔取值节点。
    /// elementSrc：筛选场景下用筛选的 element 作为"当前元素"；为 null 时条件节点的卡口不接线（= 本次目标）。
    /// forTrigger：触发条件作用于施法卡（固定取"这张卡牌"）。
    /// slotTarget：目标条件里目标模式是"所有槽位"（槽位目标）——少数条件只对槽位有意义（如 ConditionSlotEmpty）。</summary>
    private static GraphNode EmitCondition(GraphData g, ConditionData cond, GraphNode elementSrc, bool forTrigger, string idBase, GraphNode entry, bool slotTarget, out string reason)
    {
        reason = "";
        string cls = cond.GetType().Name;
        bool negate = GetIntField(cond, "oper") == 1;    //ConditionOperatorBool: IsTrue=0 / IsFalse=1

        GraphNode self = null;
        GraphNode casterCard = forTrigger ? ValueNode(g, "cc", "102001", "这张卡牌") : null;

        switch (cls)
        {
            case "ConditionSlotEmpty":
            {
                //★ 旧语义（ConditionSlotEmpty.cs）逐行：只有 **Slot 目标** 才真去看"这个槽位里有没有卡"
                //  （CompareBool(data.GetSlotCard(slot) == null, oper)）；Card/Player 目标走的是
                //  `CompareBool(false, oper)` = **与目标无关的常量**（oper=IsTrue→恒假、IsFalse→恒真）。
                //  本批 14 个带此条件的能力目标全是卡（PlayTarget/SelectTarget）→ 产出布尔常量即逐行等价
                //  （旧引擎也确实按这个常量过滤目标：恒假=永远选不中，恒真=不设限）。
                if (slotTarget)
                { reason = "ConditionSlotEmpty 的槽位目标（由引擎逐槽判定，走数据直通）"; return null; }
                bool slot_empty_const = GetIntField(cond, "oper") == 1;   //ConditionOperatorBool.IsFalse=1 → !false = true
                GraphNode n = ValueNode(g, idBase, "112001", "布尔常量", "value", slot_empty_const ? "true" : "false");
                AddPin(n, "result", NodeValueType.Boolean, true);
                return n;
            }
            case "ConditionOwnerAI":
            {
                //★ 现成节点 101030「AI限定·目标与施法者同归属」：真人玩家恒真；AI 时判"目标与施法者同归属"
                //  （与旧 ConditionOwnerAI.cs 逐行一致：无目标=假、oper 决定要相同还是要不同）。
                //  ★两个卡口必须**显式接线**：运行期未接线的口会 fallback 成"本次目标"，
                //   而 caster 口要的是**施法卡本身**（不接会让 AI 分支把目标当施法者）。
                GraphNode n = ValueNode(g, idBase, "101030", "AI限定·目标与施法者同归属",
                    "operator", GetIntField(cond, "oper") == 1 ? "不同" : "相同");
                if (entry != null)
                {
                    AddPin(n, "caster", NodeValueType.Card, false);
                    Link(g, entry, "card", n, "caster");         //入口「卡牌」口 = 施法卡自身
                    AddPin(n, "target", NodeValueType.Card, false);
                    if (elementSrc != null) Link(g, elementSrc, "element", n, "target");
                    else Link(g, entry, "target", n, "target");  //入口「目标」口 = 本次目标卡
                }
                AddPin(n, "result", NodeValueType.Boolean, true);
                return n;
            }
            case "ConditionOwner":
            {
                //★ 现成节点 EFCardOwner：卡牌归属（己方/敌方），1:1 对应 ConditionOwner
                GraphNode n = ValueNode(g, idBase, "EFCardOwner", "卡牌归属判断",
                    "side", negate ? "敌方" : "己方");
                //★ 只在真接线时声明 card 口：声明了却不接 → 运行期解析为 null（条件恒假）；
                //  不声明 → 走"当前目标"回退（与旧语义一致）。实测踩到过。
                if (elementSrc != null) { AddPin(n, "card", NodeValueType.Card, false); Link(g, elementSrc, "element", n, "card"); }
                else if (forTrigger && casterCard != null) { AddPin(n, "card", NodeValueType.Card, false); Link(g, casterCard, "return", n, "card"); }
                AddPin(n, "result", NodeValueType.Boolean, true);
                return n;
            }
            case "ConditionCardType":
            {
                object team = GetObjectField(cond, "has_team");
                object trait = GetObjectField(cond, "has_trait");
                if (team != null || trait != null) { reason = "类型条件含阵营/种族（无对应节点）"; return null; }
                int t = GetIntField(cond, "has_type");
                //★ 102032 的 type 字段要**中文类型名**（DefineTypeMatches 只认 随从/法术/英雄/神器/装备/奥秘）。
                //  之前写英文枚举名（"Character"）→ 永不匹配 → 条件恒假（实测踩到）。
                string typeName = ChineseCardTypeName(t);
                if (string.IsNullOrEmpty(typeName)) { reason = "卡牌类型未支持：" + t; return null; }
                GraphNode n = ValueNode(g, idBase, "102032", "卡牌类型判断", "type", typeName);
                //同上：只在真接线时声明 card 口
                if (elementSrc != null) { AddPin(n, "card", NodeValueType.Card, false); Link(g, elementSrc, "element", n, "card"); }
                else if (forTrigger && casterCard != null) { AddPin(n, "card", NodeValueType.Card, false); Link(g, casterCard, "return", n, "card"); }
                AddPin(n, "result", NodeValueType.Boolean, true);
                if (!negate) return n;
                return Negate(g, n);
            }
            case "ConditionSelf":
            {
                //目标 == 施法卡（卡牌按 uid 判等，CompareValues 已支持）
                GraphNode cst = ValueNode(g, idBase + "_self", "102001", "这张卡牌");
                AddPin(cst, "return", NodeValueType.Card, true);
                GraphNode cmp = ValueNode(g, idBase, "112002", "比较", "operator", "==");
                AddPin(cmp, "A", NodeValueType.Object, false);
                AddPin(cmp, "B", NodeValueType.Object, false);
                AddPin(cmp, "result", NodeValueType.Boolean, true);
                if (elementSrc != null) Link(g, elementSrc, "element", cmp, "A");
                else if (entry != null) Link(g, entry, "target", cmp, "A");   //★ 目标上下文：A ← 入口.target（=当前目标卡）；不接则 A=null → 恒不等 → 取反恒真/条件失效
                Link(g, cst, "return", cmp, "B");
                return negate ? Negate(g, cmp) : cmp;
            }
            case "ConditionCardPile":
            {
                //★ 105005 卡牌所在牌堆判断：卡口（不接=当前目标）+ pileName（中文内部名，运行期 PileNormalize 同口径）
                PileType pt = ((ConditionCardPile)cond).type;
                string pileName;
                switch (pt)
                {
                    case PileType.Hand: pileName = "手牌"; break;
                    case PileType.Board: pileName = "战场"; break;
                    case PileType.Deck: pileName = "牌库"; break;
                    case PileType.Discard: pileName = "墓地"; break;
                    case PileType.Equipped: pileName = "装备"; break;
                    case PileType.Secret: pileName = "奥秘"; break;
                    case PileType.Temp: pileName = "暂存区"; break;
                    default: reason = "牌堆类型未支持：" + pt; return null;
                }
                GraphNode n = ValueNode(g, idBase, "105005", "卡牌所在牌堆判断", "pileName", pileName);
                if (elementSrc != null) { AddPin(n, "card", NodeValueType.Card, false); Link(g, elementSrc, "element", n, "card"); }
                else if (forTrigger && casterCard != null) { AddPin(n, "card", NodeValueType.Card, false); Link(g, casterCard, "return", n, "card"); }
                AddPin(n, "result", NodeValueType.Boolean, true);
                return negate ? Negate(g, n) : n;
            }
            case "ConditionTarget":
            {
                //目标类型=卡 且未取反：逐目标上下文里目标恒为卡（本引擎英雄也是卡）→ 恒真，用布尔常量表达；
                //其余（Player/Slot/取反）无对应节点 → TODO
                int tt = GetIntField(cond, "type");     //ConditionTargetType: None=0/Card=10/Player=20/Slot=30
                if (tt == 10 && !negate)
                {
                    GraphNode n = ValueNode(g, idBase, "112001", "布尔常量", "value", "true");
                    AddPin(n, "result", NodeValueType.Boolean, true);
                    return n;
                }
                reason = "目标类型条件未支持（type=" + tt + (negate ? "，取反" : "") + "）";
                return null;
            }
            default:
                reason = "条件未支持：" + cls;
                return null;
        }
    }

    /// <summary>该条件是否在**触发期**真被旧引擎求值：重写了 IsTriggerConditionMet(Game,AbilityData,Card)
    /// （即声明类型 ≠ 基类）才算"活条件"；只重写了目标判定的条件在触发期是空转（基类恒真）。</summary>
    private static bool IsLiveTriggerCondition(ConditionData c)
    {
        if (c == null)
            return false;
        System.Reflection.MethodInfo m = c.GetType().GetMethod("IsTriggerConditionMet",
            new System.Type[] { typeof(Game), typeof(AbilityData), typeof(Card) });
        if (m != null && m.DeclaringType != typeof(ConditionData))
            return true;
        //引擎还有带触发者的重载（AreTriggerConditionsMet(data, caster, triggerer) 路径）
        System.Reflection.MethodInfo m4 = c.GetType().GetMethod("IsTriggerConditionMet",
            new System.Type[] { typeof(Game), typeof(AbilityData), typeof(Card), typeof(Card) });
        if (m4 != null && m4.DeclaringType != typeof(ConditionData))
            return true;
        return false;
    }

    /// <summary>取反：112005 逻辑运算（operator=非，NodeDocRunner 读 value1 槽）。
    /// ★不能用 112009：那是"对象是否为 null"（v==null），喂布尔 false 会被判"存在"→返回 false，
    /// 取反静默失效 → 分支不走（实测 eel/play_set_attack1 踩到）。</summary>
    private static GraphNode Negate(GraphData g, GraphNode src)
    {
        GraphNode n = ValueNode(g, src.id + "_not", "112005", "逻辑运算", "operator", "非");
        AddPin(n, "value1", NodeValueType.Boolean, false);
        AddPin(n, "result", NodeValueType.Boolean, true);
        Link(g, src, "result", n, "value1");
        return n;
    }

    /// <summary>旧 EffectSendPile 真正处理的 4 个区域（其它 pile 在旧实现里是"无操作"）→ 内部区域名
    /// （与 NodeDocRunner 的 SendToPileRaw.pile 字段同口径）</summary>
    private static string PileRawName(int pile)
    {
        switch (pile)
        {
            case 20: return "手牌";
            case 30: return "牌库";
            case 40: return "墓地";
            case 90: return "暂存区";
            default: return null;
        }
    }

    private static string PileNodeId(int pile)
    {
        switch (pile)
        {
            case 10: return "210002";   //Board 战场
            case 20: return "210003";   //Hand 手牌
            case 30: return "210004";   //Deck 牌库
            case 40: return "210005";   //Discard 墓地
            case 50: return "210006";   //Secret 延迟区（本引擎=奥秘/延迟）
            case 60: return "210007";   //Equipped 道具栏
            case 90: return "210008";   //Temp 暂存区
            default: return null;
        }
    }

    private static string PileNodeName(int pile)
    {
        switch (pile)
        {
            case 10: return "卡牌置入战场";
            case 20: return "卡牌移回手牌";
            case 30: return "卡牌洗入牌库";
            case 40: return "卡牌置入墓地";
            case 50: return "卡牌置入延迟区";
            case 60: return "卡牌装备到道具栏";
            case 90: return "卡牌移动到暂存区";
            default: return "移动牌堆";
        }
    }

    // ================= 建图工具 =================
    /// <summary>建动作节点并接进主链（title 必须显式给，否则会与 fieldPairs 错位）</summary>
    private static GraphNode Chain(GraphData g, GraphNode prev, string id, string action, string title, params string[] fieldPairs)
    {
        GraphNode n = ChainNode(g, id, action, title, fieldPairs);
        Link(g, prev, "out", n, "in");
        return n;
    }

    private static GraphNode Chain(GraphData g, GraphNode prev, string id, string action)
    {
        return Chain(g, prev, id, action, Title(action));
    }

    /// <summary>接进主链，但**指定从哪个输出口出发**（分支动作的后续要走 thenAction 而非 out）</summary>
    private static GraphNode ChainFrom(GraphData g, GraphNode prev, string fromPin, string id, string action, string title, params string[] fieldPairs)
    {
        GraphNode n = ChainNode(g, id, action, title, fieldPairs);
        Link(g, prev, fromPin, n, "in");
        return n;
    }

    private static object GetObjectField(object obj, string field)
    {
        try
        {
            System.Reflection.FieldInfo f = obj.GetType().GetField(field);
            return f == null ? null : f.GetValue(obj);
        }
        catch { return null; }
    }

    /// <summary>建**取值节点**（不接执行链）：条件/集合/取值类节点必须是 Value 类型。
    /// ★ 用 ChainNode(Action) 建条件节点时，运行期的条件/取值线解析不到它 → 条件恒假（实测踩到）。</summary>
    private static GraphNode ValueNode(GraphData g, string id, string action, string title, params string[] fieldPairs)
    {
        GraphNode n = NewNode(g, id, GraphNodeType.Value, action, title);
        for (int i = 0; i + 1 < fieldPairs.Length; i += 2)
        {
            if (string.IsNullOrEmpty(fieldPairs[i]))
                continue;
            SetField(n, fieldPairs[i], fieldPairs[i + 1]);
        }
        return n;
    }

    /// <summary>建节点但不连线（供复合子图用）</summary>
    private static GraphNode ChainNode(GraphData g, string id, string action, string title, params string[] fieldPairs)
    {
        GraphNode n = NewNode(g, id, IsLibraryValueAction(action) ? GraphNodeType.Value : GraphNodeType.Action, action, title);
        AddPin(n, "in", NodeValueType.Flow, false);
        AddPin(n, "out", NodeValueType.Flow, true);
        for (int i = 0; i + 1 < fieldPairs.Length; i += 2)
        {
            if (string.IsNullOrEmpty(fieldPairs[i]))
                continue;
            SetField(n, fieldPairs[i], fieldPairs[i + 1]);
        }
        return n;
    }

    /// <summary>节点库里是**取值(Value)类型**的 action：用它们建节点时必须标 Value，否则编辑器会按"动作节点"
    /// 呈现（节点库审计抓到 102004/112003/112004 三个 30 处，池里类型=2(Action)、库=3(Value)）——呈现不准。</summary>
    private static bool IsLibraryValueAction(string action)
    {
        switch (action)
        {
            case "102004":   //获取属性
            case "112003":   //整数常量
            case "112004":   //整数运算
                return true;
            default:
                return false;
        }
    }

    /// <summary>建"整数运算"节点并把两个值来源接进两个 Int32 输入槽
    /// （★ 必须两槽都接线：有连线时运行期会忽略 arg 字段，只接一槽会把另一个数丢掉）</summary>
    private static GraphNode Chain2(GraphData g, GraphNode srcA, GraphNode srcB, string id, string action, string title, string field, string fieldValue)
    {
        GraphNode n = NewNode(g, id, IsLibraryValueAction(action) ? GraphNodeType.Value : GraphNodeType.Action, action, title);
        AddPin(n, "in", NodeValueType.Flow, false);
        AddPin(n, "out", NodeValueType.Flow, true);
        SetField(n, field, fieldValue);
        //★ 端口名必须是 arg1/arg2：ResolveIntParamSlots 只认 base_name=="arg" 的编号槽
        //  （之前写成 values1/values2 → 被忽略，退回 arg 字段=0 → 加属性算出 0，实测踩到）
        AddPin(n, "arg1", NodeValueType.Int32, false);
        AddPin(n, "arg2", NodeValueType.Int32, false);
        AddPin(n, "result", NodeValueType.Int32, true);
        Link(g, srcA, "return", n, "arg1");
        Link(g, srcB, "result", n, "arg2");
        return n;
    }

    private static GraphNode NewNode(GraphData g, string id, GraphNodeType type, string action, string title)
    {
        GraphNode n = new GraphNode();
        n.id = id;
        n.type = type;
        n.action = action;
        n.title = title;
        n.category = "其他";     //非空 → 走 NodeDoc 解释器（EffectRunGraph）
        n.pos = new Vector2Data { x = 0, y = 0 };
        n.pins = new List<GraphPin>();
        n.fields = new List<FieldCustomData>();
        g.nodes.Add(n);
        return n;
    }

    private static void AddPin(GraphNode n, string name, NodeValueType t, bool output)
    {
        GraphPin p = new GraphPin();
        p.id = n.id + "_" + name;
        p.name = name;
        p.display_name = name;
        p.is_output = output;
        p.type = t;
        n.pins.Add(p);
    }

    private static void Link(GraphData g, GraphNode from, string fromPin, GraphNode to, string toPin)
    {
        GraphLink l = new GraphLink();
        l.from_node = from.id;
        l.from_pin = from.id + "_" + fromPin;
        l.to_node = to.id;
        l.to_pin = to.id + "_" + toPin;
        g.links.Add(l);
    }

    private static void SetField(GraphNode n, string name, string value)
    {
        if (n.fields == null)
            n.fields = new List<FieldCustomData>();
        FieldCustomData f = new FieldCustomData();
        f.name = name;
        f.value = value;
        n.fields.Add(f);
    }

    private static string Title(string action)
    {
        return NodeDocTitle(action);
    }

    private static string NodeDocTitle(string action)
    {
        try
        {
            foreach (NodeDocDef d in NodeDocDb.All)
                if (d != null && d.define_id == action)
                    return d.editor_name;
        }
        catch { }
        return action;
    }

    // ---------------- 反射读旧效果字段（避免为取值而引入编译期耦合） ----------------
    private static int GetIntField(object obj, string field)
    {
        try
        {
            System.Reflection.FieldInfo f = obj.GetType().GetField(field);
            if (f == null) return 0;
            object v = f.GetValue(obj);
            return v == null ? 0 : Convert.ToInt32(v);
        }
        catch { return 0; }
    }

    private static CardData GetCardDataField(object obj, string field)
    {
        try
        {
            System.Reflection.FieldInfo f = obj.GetType().GetField(field);
            return f == null ? null : (CardData)f.GetValue(obj);
        }
        catch { return null; }
    }

    private static bool GetBoolField(object obj, string field)
    {
        try
        {
            System.Reflection.FieldInfo f = obj.GetType().GetField(field);
            if (f == null) return false;
            object v = f.GetValue(obj);
            return v != null && Convert.ToBoolean(v);
        }
        catch { return false; }
    }

    private static AbilityData GetAbilityField(object obj, string field)
    {
        try
        {
            System.Reflection.FieldInfo f = obj.GetType().GetField(field);
            return f == null ? null : (AbilityData)f.GetValue(obj);
        }
        catch { return null; }
    }

    private static StatusData GetStatusField(object obj, string field)
    {
        try
        {
            System.Reflection.FieldInfo f = obj.GetType().GetField(field);
            return f == null ? null : (StatusData)f.GetValue(obj);
        }
        catch { return null; }
    }
}
