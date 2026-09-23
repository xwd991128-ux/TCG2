using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Client;
using TcgEngine.UI;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

/// <summary>【临时探针】Phase 3 差分验证：**旧能力实现 vs 转换出的规则图**，同夹具同目标对跑。
///
/// 为什么这样比：本引擎整局不可复现（洗牌走 System.Random、AI 在后台线程），所以判据必须是
/// "受控夹具 + 每例重置 + 固定随机种子 + 只比较前后状态差分"。
///
/// 用例来源（不重新转换）：
///   tools/base_pool_v1.json  —— 转换器产出的卡池（每卡 effects[] 为规则图）
///   tools/converter_report.tsv —— 逐能力 ok/todo；同一张卡里 **ok 行按序** 对应 effects[] 按序
///
/// 判定：对同一目标集合，旧路径（DoEffects/DoOngoingEffects）与新路径（NodeDocRunner.Run）
///       产生的前后状态差分**必须逐字符相同**；不同即列为 DIFF 并打印两边差分。
///
/// 用法：拷到 Assets/TcgEngine/Scripts/__TempProbe/ 并编译 → 建 tools/diff_flag.txt → 进 Play
///       产出 tools/diff_runtime_result.tsv（跑完自动删标记）
public class AbilityDiffProbe : MonoBehaviour
{
    public static string FlagPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/diff_flag.txt")); } }
    private static string PoolPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/base_pool_v1.json")); } }
    private static string ReportPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/converter_report.tsv")); } }
    private static string OutPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/diff_runtime_result.tsv")); } }

    private GameLogic logic;
    private Game data;
    private Player p0, p1;
    private float _t;
    private int phase;
    private readonly StringBuilder outBuf = new StringBuilder();
    private readonly List<string> cap = new List<string>();
    private int same, diff, err, skip, warn;

    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }
    private void OnLog(string msg, string stack, LogType type)
    {
        if (!string.IsNullOrEmpty(msg) && cap.Count < 40)
            cap.Add(msg.Replace("\n", " ").Replace("\r", " "));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!File.Exists(FlagPath))
            return;
        GameObject go = new GameObject("__AbilityDiffProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<AbilityDiffProbe>();
        Debug.Log("[差分] 启动");
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (phase == 0)
        {
            if (_t < 6f) return;
            phase = 1;
            StartMatch();
            return;
        }
        if (phase == 1)
        {
            if (_t < 12f) return;
            TryHook();
            return;
        }
        if (phase == 2)
        {
            phase = 3;
            StartCoroutine(RunAll());
        }
    }

    // ---------------- 开局（与夹具一致：静默对局） ----------------
    private void StartMatch()
    {
        try
        {
            GameplayData g = GameplayData.Get();
            DeckData pd = g.test_deck != null ? g.test_deck : (g.free_decks != null && g.free_decks.Length > 0 ? g.free_decks[0] : null);
            DeckData ad = g.test_deck_ai != null ? g.test_deck_ai : (g.ai_decks != null && g.ai_decks.Length > 0 ? g.ai_decks[0] : null);
            if (pd == null || ad == null) { Fail("无可用卡组"); return; }
            g.ai_vs_ai = false;
            GameClient.game_settings.game_type = GameType.Solo;
            GameClient.game_settings.game_mode = GameMode.Casual;
            GameClient.game_settings.game_uid = "diff_phase3";
            GameClient.game_settings.scene = (g.arena_list != null && g.arena_list.Length > 0) ? g.arena_list[0] : "Game";
            GameClient.player_settings.deck = new UserDeckData(pd);
            GameClient.ai_settings.deck = new UserDeckData(ad);
            GameClient.ai_settings.ai_level = g.ai_level;
            MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual);
        }
        catch (Exception e) { Fail("开局异常: " + e); }
    }

    private static object Dive(object o, int depth, ref int budget)
    {
        if (o == null || depth > 2 || budget-- <= 0) return null;
        Type t = o.GetType();
        if (t.GetMethod("GetGameData") != null && t.Name.IndexOf("Logic", StringComparison.OrdinalIgnoreCase) >= 0) return o;
        if (t.IsPrimitive || o is string) return null;
        if (t.Namespace != null && (t.Namespace.StartsWith("System") || t.Namespace.StartsWith("Unity"))) return null;
        foreach (System.Reflection.FieldInfo f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
        {
            object v;
            try { v = f.GetValue(o); } catch { continue; }
            if (v == null || v is string) continue;
            object r = Dive(v, depth + 1, ref budget);
            if (r != null) return r;
        }
        return null;
    }

    private void TryHook()
    {
        try
        {
            int budget = 8000;
            object lo = null;
            foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null) continue;
                lo = Dive(mb, 0, ref budget);
                if (lo != null) break;
                if (budget <= 0) break;
            }
            logic = lo as GameLogic;
            if (logic == null) { Debug.Log("[差分] 未找到 GameLogic…"); return; }
            data = logic.GetGameData();
            if (data == null || data.players == null || data.players.Length < 2) return;
            p0 = data.GetPlayer(0); p1 = data.GetPlayer(1);
            if (p0 == null || p1 == null) return;
            p0.is_ai = false; p1.is_ai = false;
            data.first_player = 0; data.current_player = 0;
            //★ 断开客户端刷新/通知链：GameServer.RefreshAll → 客户端 FX 回调（BoardCardFX.OnPlayed →
            //  BoardCard.GetEquipCard）在批处理中抛 NRE 且与对局状态无关（实测 6 例异常全是它）。
            //  这些委托是普通 UnityAction 字段（GameLogic.cs L24-51），置空即断，核心模拟不受影响。
            GameLog.Verbose = true;   //★ 打开轨迹日志：否则 GameLog.Log（分支/动作结算轨迹）全部静默，cap 采不到（实测踩到）
            logic.onRefresh = null;
            logic.onCardPlayed = null;
            logic.onCardSummoned = null;
            logic.onCardMoved = null;
            logic.onCardTransformed = null;
            logic.onCardDiscarded = null;
            logic.onCardDrawn = null;
            logic.onRollValue = null;
            logic.onCardDamaged = null;
            logic.onCardHealed = null;
            Debug.Log("[差分] 已挂载并静默对局，开始跑用例");
            phase = 2;
        }
        catch (Exception e) { Debug.Log("[差分] 挂载异常: " + e.Message); }
    }

    private void Fail(string why)
    {
        Debug.LogError("[差分] 失败：" + why);
        try { File.WriteAllText(OutPath, "# 失败：" + why, new UTF8Encoding(false)); } catch { }
        try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
        phase = 3; enabled = false;
    }

    // ---------------- 主循环 ----------------
    private IEnumerator RunAll()
    {
        CardPoolData pool = null;
        try { pool = JsonUtility.FromJson<CardPoolData>(File.ReadAllText(PoolPath, Encoding.UTF8)); }
        catch (Exception e) { Fail("读卡池失败: " + e.Message); yield break; }
        if (pool == null || pool.cards == null) { Fail("卡池为空"); yield break; }

        // 报告：每卡 ok 的能力 id（按序）→ 与 effects[] 按序一一对应
        Dictionary<string, List<string>> okByCard = new Dictionary<string, List<string>>();
        try
        {
            foreach (string ln in File.ReadAllLines(ReportPath, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(ln) || ln.StartsWith("#")) continue;
                string[] f = ln.Split('\t');
                if (f.Length < 6 || f[4] != "ok") continue;
                if (!okByCard.ContainsKey(f[0])) okByCard[f[0]] = new List<string>();
                okByCard[f[0]].Add(f[1]);
            }
        }
        catch (Exception e) { Fail("读报告失败: " + e.Message); yield break; }

        outBuf.AppendLine("# Phase 3 差分结果（旧能力 vs 规则图）  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        outBuf.AppendLine("# 列：结果 | 卡 | 能力 | 入口 | 目标模式 | 旧差分 | 新差分 | 说明");

        int cases = 0;
        foreach (CardCustomData cd in pool.cards)
        {
            if (cd == null || cd.effects == null || cd.effects.Count == 0) continue;
            List<string> okIds = okByCard.ContainsKey(cd.id) ? okByCard[cd.id] : null;
            if (okIds == null) continue;

            for (int i = 0; i < cd.effects.Count && i < okIds.Count; i++)
            {
                CardEffectData eff = cd.effects[i];
                if (eff == null || eff.graph == null) continue;
                cases++;
                CaseOutcome oc = RunOne(cd.id, okIds[i], eff.graph, eff.status_ids);
                if (oc.Result == "same") same++;
                else if (oc.Result == "DIFF") diff++;
                else if (oc.Result == "warn") warn++;
                else if (oc.Result == "err") err++;
                else skip++;
                outBuf.AppendLine(oc.Result + "\t" + cd.id + "\t" + okIds[i] + "\t" + oc.Entry + "\t" + oc.Target
                    + "\t" + Trunc(oc.OldDelta, 300) + "\t" + Trunc(oc.NewDelta, 300) + "\t" + oc.Note);
                yield return null;
            }
        }

        outBuf.AppendLine("# 汇总：一致 " + same + "｜DIFF " + diff + "｜★warn " + warn + "｜异常 " + err
            + "｜跳过 " + skip + "｜共 " + cases);
        try { File.WriteAllText(OutPath, outBuf.ToString(), new UTF8Encoding(false)); }
        catch (Exception e) { Debug.LogError("[差分] 写结果失败: " + e.Message); }
        Debug.Log("[差分] 完成：same=" + same + " DIFF=" + diff + " err=" + err + " skip=" + skip + " → " + OutPath);
        try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
        enabled = false;
    }

    private class CaseOutcome
    {
        public string Result = "err";
        public string Entry = "";
        public string Target = "";
        public string OldDelta = "";
        public string NewDelta = "";
        public string Note = "";
    }

    private static string Trunc(string s, int n) { return s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…"); }

    /// <summary>两个差分串的**第一处不同** + 附近窗口（差分被截断时靠这个定位，不用再猜/加长截断）</summary>
    private static string FirstDiff(string a, string b)
    {
        if (a == null) a = "";
        if (b == null) b = "";
        int n = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && a[i] == b[i]) i++;
        int s = Math.Max(0, i - 80);
        string wa = i >= a.Length ? "(旧串结束)" : Trunc(a.Substring(s, Math.Min(220, a.Length - s)), 220);
        string wb = i >= b.Length ? "(新串结束)" : Trunc(b.Substring(s, Math.Min(220, b.Length - s)), 220);
        return "首处不同@" + i + " 旧[" + wa + "] 新[" + wb + "]";
    }

    private CaseOutcome RunOne(string cardId, string abilityId, GraphData graph, System.Collections.Generic.List<string> statusIds)
    {
        CaseOutcome oc = new CaseOutcome();
        try
        {
            AbilityData ab = AbilityData.Get(abilityId);
            CardData cdata = CardData.Get(cardId);
            if (ab == null) { oc.Result = "skip"; oc.Note = "旧能力不在运行期字典"; return oc; }
            if (cdata == null) { oc.Result = "skip"; oc.Note = "卡定义未加载"; return oc; }

            string entryAction = "";
            foreach (GraphNode n in graph.nodes)
                if (n != null && n.type == GraphNodeType.Event) { entryAction = n.action; break; }
            oc.Entry = entryAction;

            // 目标模式取图的入口字段（转换器写入），与旧 target 对齐
            string tmode = "";
            foreach (GraphNode n in graph.nodes)
                if (n != null && n.type == GraphNodeType.Event) { tmode = GraphRuntime.GetFieldString(n, "target_mode", ""); break; }
            oc.Target = tmode + "/" + ab.target;
            if (string.IsNullOrEmpty(tmode) || tmode != TargetModeOf(ab.target))
            { oc.Result = "skip"; oc.Note = "目标模式不一致（图=" + tmode + " 旧=" + TargetModeOf(ab.target) + "）"; return oc; }

            if (ab.target == AbilityTarget.ChoiceSelector || ab.target == AbilityTarget.ValueSelector
                || ab.target == AbilityTarget.EquippedCard)
            { oc.Result = "skip"; oc.Note = "目标模式本夹具不支持"; return oc; }

            bool ongoing = ab.trigger == AbilityTrigger.Ongoing;

            // ---------------- 旧路径 ----------------
            SeedRng(20250919);
            ResetFixture();
            Card casterA = MakeCaster(cdata);
            if (casterA == null) { oc.Result = "skip"; oc.Note = "无法构造施法卡"; return oc; }
            List<object> targetsA = ResolveTargets(ab);      //★ 必须在建好施法卡之后（Self 目标依赖 casterRef）
            SimulateDeath(ab, targetsA);                     //★ 亡语语义：目标卡刚死、其槽位已空出
            PrepareForEffect(ab, casterA, targetsA);         //★ 前置状态（横置/可清状态），否则解除/清空类效果无差分
            Dictionary<string, int> b1 = Snapshot();
            cap.Clear();
            ApplyLegacy(ab, casterA, targetsA, ongoing);
            oc.OldDelta = Diff(b1, Snapshot());
            string logA = string.Join(" / ", cap.ToArray());

            // ---------------- 新路径（同夹具、同目标集合） ----------------
            SeedRng(20250919);
            ResetFixture();
            Card casterB = MakeCaster(cdata);
            if (casterB == null) { oc.Result = "skip"; oc.Note = "新路径无法构造施法卡"; return oc; }
            List<object> targetsB = ResolveTargets(ab);
            SimulateDeath(ab, targetsB);
            PrepareForEffect(ab, casterB, targetsB);         //★ 与旧路径**同等**准备前置状态，保证可比
            Dictionary<string, int> b2 = Snapshot();
            int ran = 0;
            cap.Clear();
            foreach (object t in targetsB)
            {
                Card tc = t as Card;
                Player tp = t as Player;
                Slot ts = (t is Slot) ? (Slot)t : Slot.None;   //★D 批：槽位目标必须带进图（否则召唤类落到别的格子）
                CardData td = t as CardData;                   //★零头批：卡牌定义目标（AllCardData，逐定义结算）
                if (tc == null && tp == null && ts == Slot.None && td == null) continue;
                ran += NodeDocRunner.Run(logic, graph, casterB, tc, tp, entryAction, target_slot: ts, target_define: td);
                //★B 批：新路径 = **编译后的能力**行为 —— 引擎在 DoEffects 里先跑效果、再把能力自带 status
                //  施加到该目标（用该能力的 value/duration）。探针不经过编译后的 AbilityData，所以要照样施加，
                //  否则"状态+效果混合"用例会假 DIFF（旧路径施加了、新路径没施加）。
                ApplyDataStatuses(tc, tp, statusIds, ab.value, ab.duration);
            }
            oc.NewDelta = Diff(b2, Snapshot());
            string logB = string.Join(" / ", cap.ToArray());
            //★ 动作数必须**无条件**带进说明：只靠日志判断不出"图没执行"还是"执行了但没效果"
            string ranNote = "新路径动作数=" + ran;
            if (ran == 0) ranNote += "（图未被执行？）";
            string logPart = ranNote + "｜旧日志:[" + Trunc(logA, 200) + "] 新日志:[" + Trunc(logB, 200) + "]";

            //★ 假绿陷阱：图里出现"未支持的 NodeDoc 动作"时两边可能都没干活 → 差分相同但毫无意义
            //  （实测：PlayCardFree 的动作 case 因同文件并行编辑丢失，日志里在报"未支持的 NodeDoc 动作"，
            //   而结果仍是 same → 必须单独判成 warn，不能混进"全绿"）
            if (logB.Contains("未支持的 NodeDoc 动作"))
            {
                oc.Result = "warn";
                oc.Note = "★新路径有未支持的 NodeDoc 动作（日志见下）→ 本用例判据不成立｜" + logPart;
                return oc;
            }

            oc.Result = (oc.OldDelta == oc.NewDelta) ? "same" : "DIFF";
            if (oc.Result == "DIFF")
                oc.Note = "状态差分不一致｜" + FirstDiff(oc.OldDelta, oc.NewDelta) + "｜" + logPart;
            else if (!string.IsNullOrEmpty(ranNote) && ran == 0)
                oc.Note = ranNote + "（一致但图未执行 → 判据不可信，必须查）";
            return oc;
        }
        catch (Exception e)
        {
            oc.Result = "err";
            Exception x = e.InnerException != null ? e.InnerException : e;
            string stack = x.StackTrace ?? "";
            string[] frames = stack.Split('\n');
            string head = "";
            for (int i = 0; i < frames.Length && i < 4; i++)
                head += " <" + frames[i].Trim() + ">";
            oc.Note = "异常: " + x.Message + head;
            return oc;
        }
    }

    /// <summary>把"能力自带的状态"施加到目标（模拟编译后 AbilityData.status 的施加时机与数值）。
    /// 旧引擎：AbilityData.DoEffects 先跑效果，再 `target.AddStatus(stat, ability.value, ability.duration)`。</summary>
    private void ApplyDataStatuses(Card target_card, Player target_player,
        System.Collections.Generic.List<string> ids, int value, int duration)
    {
        if (ids == null || ids.Count == 0)
            return;
        foreach (string sid in ids)
        {
            if (!System.Enum.TryParse<StatusType>(sid, true, out StatusType st) || st == StatusType.None)
                continue;
            StatusData sd = StatusData.Get(st);
            if (sd == null)
                continue;
            if (target_card != null)
                target_card.AddStatus(sd, value, duration);
            else if (target_player != null)
                target_player.AddStatus(sd, value, duration);
        }
    }

    private static string TargetModeOf(AbilityTarget t)
    {
        switch (t)
        {
            case AbilityTarget.None: return "无";
            case AbilityTarget.Self: return "自身";
            case AbilityTarget.PlayerSelf: return "施法者玩家";
            case AbilityTarget.PlayerOpponent: return "对手玩家";
            case AbilityTarget.AllPlayers: return "全体玩家";
            case AbilityTarget.AllCardsBoard: return "所有角色";
            case AbilityTarget.AllCardsHand: return "双方手牌";
            case AbilityTarget.AllCardsAllPiles: return "所有牌堆";
            case AbilityTarget.AllCardData: return "卡牌定义";
            case AbilityTarget.SelectTarget: return "选择目标";
            case AbilityTarget.PlayTarget: return "打出目标";
            case AbilityTarget.AbilityTriggerer: return "触发者";   //★D 批：陷阱/反击类（触发者=敌方首个角色）
            case AbilityTarget.CardSelector: return "卡牌选择";      //★D 批：选择器（候选=引擎 GetCardTargets，夹具取首个）
            case AbilityTarget.AllSlots: return "所有槽位";          //★D 批：逐槽（与引擎同规：条件+过滤器筛槽）
            case AbilityTarget.ChoiceSelector: return "选择器";      //★C 批：选择菜单（夹具不支持，按 skip 记）
            default: return "未支持:" + t;
        }
    }

    /// <summary>旧 target 模式 → 目标集合（旧/新两条路径共用同一集合，保证可比）</summary>
    private List<object> ResolveTargets(AbilityData ab)
    {
        List<object> list = new List<object>();
        switch (ab.target)
        {
            case AbilityTarget.None:
                break;
            case AbilityTarget.Self:
                list.Add(casterRef);
                break;
            case AbilityTarget.PlayerSelf:
                list.Add(p0);
                break;
            case AbilityTarget.PlayerOpponent:
                list.Add(p1);
                break;
            case AbilityTarget.AllPlayers:
                list.Add(p0); list.Add(p1);
                break;
            case AbilityTarget.AllCardsBoard:
                foreach (Player pl in new Player[] { p0, p1 })
                    foreach (Card c in pl.cards_board) if (c != null) list.Add(c);
                break;
            case AbilityTarget.AllCardsHand:
                foreach (Player pl in new Player[] { p0, p1 })
                    foreach (Card c in pl.cards_hand) if (c != null) list.Add(c);
                break;
            case AbilityTarget.AllCardData:
            {
                //★零头批：卡牌定义目标 = **定义集合**（不是场上的卡）。与引擎同规：
                //  CardData.GetAll() 逐条过 conditions_target（+filters_target）。
                //  此前这里错误地当成"所有区域的卡"→ EffectCreate 这类"按定义创建"的效果拿到的是卡对象、
                //  走的还是 Card 重载（创建的是那张卡的副本），与真实引擎行为不同 = 假夹具。
                List<CardData> defs = null;
                try { defs = ab.GetCardDataTargets(data, casterRef); } catch { defs = null; }
                if (defs != null)
                    foreach (CardData df in defs)
                        if (df != null) list.Add(df);
                break;
            }
            case AbilityTarget.AllCardsAllPiles:
                foreach (Player pl in new Player[] { p0, p1 })
                {
                    foreach (Card c in pl.cards_board) if (c != null) list.Add(c);
                    foreach (Card c in pl.cards_hand) if (c != null) list.Add(c);
                    foreach (Card c in pl.cards_equip) if (c != null) list.Add(c);
                    foreach (Card c in pl.cards_discard) if (c != null) list.Add(c);
                    foreach (Card c in pl.cards_temp) if (c != null) list.Add(c);
                    foreach (Card c in pl.cards_secret) if (c != null) list.Add(c);
                    foreach (Card c in pl.cards_deck) if (c != null) list.Add(c);
                }
                break;
            case AbilityTarget.SelectTarget:
            case AbilityTarget.PlayTarget:
            {
                List<Card> legal = null;
                try { legal = ab.GetCardTargets(data, casterRef); } catch { legal = null; }
                Card picked = (legal != null && legal.Count > 0) ? legal[0] : FirstBoard(1);
                if (picked != null) list.Add(picked);
                break;
            }
            case AbilityTarget.AbilityTriggerer:
            {
                //★D 批：夹具里没有"触发者"这个事件上下文 → 近似用**敌方首个角色**当触发者
                //  （与 AbilityFixtureProbe 同一口径）。两条路径拿到的是同一张卡，比对仍然有意义。
                Card trig = FirstBoard(1);
                if (trig != null) list.Add(trig);
                break;
            }
            case AbilityTarget.CardSelector:
            {
                //★D 批：候选列表就用引擎自己的（CardSelector 分支 = 全区域卡过 conditions_target + filters_target）；
                //  夹具取**首个候选**（确定性），两条路径拿到同一张 → 比对仍有意义。
                List<Card> cand = null;
                try { cand = ab.GetCardTargets(data, casterRef); } catch { cand = null; }
                if (cand != null && cand.Count > 0)
                    list.Add(cand[0]);
                break;
            }
            case AbilityTarget.AllSlots:
            {
                //★D 批：与引擎同规（AbilityData.GetSlotTargets）——全部槽过 conditions_target，再逐条套 filters_target
                List<Slot> ok = new List<Slot>();
                foreach (Slot s in Slot.GetAll())
                {
                    try { if (ab.AreTargetConditionsMet(data, casterRef, s)) ok.Add(s); } catch { }
                }
                if (ab.filters_target != null && ok.Count > 0)
                {
                    foreach (FilterData f in ab.filters_target)
                    {
                        if (f != null)
                            ok = f.FilterTargets(data, ab, casterRef, ok, new List<Slot>());
                    }
                }
                foreach (Slot s in ok)
                    list.Add(s);
                break;
            }
            default:
                break;
        }
        return list;
    }

    private Card casterRef;

    private Card FirstBoard(int pid)
    {
        Player pl = data.GetPlayer(pid);
        if (pl == null) return null;
        foreach (Card c in pl.cards_board) if (c != null) return c;
        return null;
    }

    /// <summary>亡语上下文：旧 EffectAddToDeck(Card) 会往"目标卡的槽位"召唤，前提是目标刚死、槽位已空。
    /// 夹具里目标还活着 → 旧路径必然失败（而新路径会自动找空位），造成**夹具语义差异**而非转换 bug。
    /// 这里在快照前把目标卡移出战场，让两条路径面对同样的初始状态。</summary>
    private void SimulateDeath(AbilityData ab, List<object> targets)
    {
        if (ab.trigger != AbilityTrigger.OnDeath || targets == null)
            return;
        foreach (object o in targets)
        {
            Card tc = o as Card;
            if (tc == null)
                continue;
            try
            {
                Player owner = logic.GameData.GetPlayer(tc.player_id);
                if (owner != null && owner.cards_board.Contains(tc))
                {
                    //★ RemoveCardFromAllGroups 会连带清掉 card.slot，而旧 EffectAddToDeck(Card) 靠 **target.slot**
                    //  定位落点 → 必须先存槽位再移除，移除后**还原槽位**，否则旧路径必然静默失败（实测踩到）。
                    Slot keep = tc.slot;
                    owner.RemoveCardFromAllGroups(tc);
                    tc.slot = keep;
                }
            }
            catch { }
        }
    }

    private void ApplyLegacy(AbilityData ab, Card caster, List<object> targets, bool ongoing)
    {
        if (ab.target == AbilityTarget.None)
        {
            ab.DoEffects(logic, caster);
            return;
        }
        foreach (object o in targets)
        {
            Card tc = o as Card;
            Player tp = o as Player;
            if (tc != null)
            {
                //★ 旧路径也必须走条件判定：引擎是按 AreTargetConditionsMet 逐目标过滤的，
                //  直接调 DoEffects 会绕过 → 新路径（图里有分支/筛选）过滤了、旧路径没过滤 → 假 DIFF。
                try
                {
                    if (!ab.AreTargetConditionsMet(data, caster, tc))
                        continue;
                }
                catch { }
                if (ongoing) ab.DoOngoingEffects(logic, caster, tc); else ab.DoEffects(logic, caster, tc);
            }
            else if (tp != null)
            {
                try
                {
                    if (!ab.AreTargetConditionsMet(data, caster, tp))
                        continue;
                }
                catch { }
                if (ongoing) ab.DoOngoingEffects(logic, caster, tp); else ab.DoEffects(logic, caster, tp);
            }
            else if (o is CardData)
            {
                //★零头批：卡牌定义目标（AllCardData）——引擎在 ResolveCardAbilityCardData 里逐定义
                //  `DoEffects(logic, caster, CardData)`；定义集合已由 GetCardDataTargets 按条件筛过，不再重复判条件。
                if (!ongoing) ab.DoEffects(logic, caster, (CardData)o);
            }
            else if (o is Slot)
            {
                //★D 批：逐槽结算（AllSlots）——旧路径也必须走槽位条件判定（与引擎同规），否则假 DIFF
                Slot ts = (Slot)o;
                try
                {
                    if (!ab.AreTargetConditionsMet(data, caster, ts))
                        continue;
                }
                catch { }
                //注：AbilityData 没有 DoOngoingEffects(...,Slot) 重载；且 Ongoing 能力整体走数据直通（不产图），
                //    本探针的 AllSlots 用例全部是非 Ongoing 图能力 → 这里只跑非 Ongoing 分支。
                if (!ongoing) ab.DoEffects(logic, caster, ts);
            }
        }
    }

    // ---------------- 夹具（与基线探针一致） ----------------
    private static readonly string[] DUMMY_CANDIDATES = { "imp", "woodland", "fire_element", "cave", "coin", "recruit" };

    private void ResetFixture()
    {
        foreach (Player pl in new Player[] { p0, p1 })
        {
            if (pl == null) continue;
            List<Card> all = new List<Card>(pl.cards_all.Values);
            foreach (Card c in all) { try { pl.RemoveCardFromAllGroups(c); } catch { } }
            pl.cards_deck.Clear(); pl.cards_hand.Clear(); pl.cards_board.Clear();
            pl.cards_equip.Clear(); pl.cards_discard.Clear(); pl.cards_secret.Clear(); pl.cards_temp.Clear();
            pl.hp = 30; pl.hp_max = 30;
            pl.mana = 10; pl.mana_max = 10; pl.mana_max_total = 10;
            pl.kill_count = 0; pl.skip_turns = 0;
        }
        int placed = 0;
        for (int i = 0; i < DUMMY_CANDIDATES.Length && placed < 3; i++)
        {
            CardData cd = CardData.Get(DUMMY_CANDIDATES[i]);
            if (cd == null || !cd.IsBoardCard()) continue;
            Player owner = placed < 1 ? p0 : p1;
            int slotX = placed == 0 ? 1 : (placed == 1 ? 2 : 3);
            Card c = Card.Create(cd, VariantData.GetDefault(), owner);
            try { logic.PlaceCardOnBoard(c, new Slot(slotX, Slot.y_min, owner.player_id)); } catch { }
            placed++;
        }
        CardData filler = CardData.Get("woodland") ?? PickBoardCard();
        foreach (Player pl in new Player[] { p0, p1 })
        {
            for (int i = 0; i < 5 && filler != null; i++)
                try { logic.AddCardDeck(pl, filler, VariantData.GetDefault()); } catch { }
            for (int i = 0; i < 3 && filler != null; i++)
                try { pl.AddCard(pl.cards_hand, Card.Create(filler, VariantData.GetDefault(), pl)); } catch { }
        }
    }

    private static CardData PickBoardCard()
    {
        foreach (CardData cd in CardData.GetAllDeckbuilding())
            if (cd != null && cd.IsBoardCard()) return cd;
        return null;
    }

    private Card MakeCaster(CardData cd)
    {
        Card c;
        if (cd.type == CardType.Hero)
        {
            c = Card.Create(cd, VariantData.GetDefault(), p0);
            p0.hero = c;
        }
        else if (cd.IsBoardCard())
        {
            c = Card.Create(cd, VariantData.GetDefault(), p0);
            bool ok = false;
            try { ok = logic.PlaceCardOnBoard(c, new Slot(4, Slot.y_min, 0)); } catch { ok = false; }
            if (!ok) { try { p0.AddCard(p0.cards_hand, c); } catch { } }
        }
        else
        {
            c = Card.Create(cd, VariantData.GetDefault(), p0);
            try { p0.AddCard(p0.cards_hand, c); } catch { }
        }
        casterRef = c;
        return c;
    }

    private void SeedRng(int seed)
    {
        try { UnityEngine.Random.InitState(seed); } catch { }
        try
        {
            System.Reflection.FieldInfo f = typeof(GameLogic).GetField("random", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (f != null && f.FieldType == typeof(System.Random)) f.SetValue(logic, new System.Random(seed));
        }
        catch { }
        try
        {
            foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("TcgEngine.Workshop.GraphRuntime", false);
                if (t == null) continue;
                System.Reflection.FieldInfo f = t.GetField("rng", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (f != null && f.FieldType == typeof(System.Random)) f.SetValue(null, new System.Random(seed));
                break;
            }
        }
        catch { }
    }

    /// <summary>给"需要前置状态才看得出效果"的效果准备夹具（两条路径**同等**准备，再各自快照）：
    ///   - 解除横置（EffectExhaust exhausted=false）：先把施法卡置成横置，否则 false→false 没有差分；
    ///   - 清状态（EffectClearStatus）：先给目标挂一个"会被清掉"的状态；且要避开本能力自带的 status，
    ///     否则清完又加回来 → 又是无差分；
    /// 目的：避免"两边都没干活"的空洞一致被当成验证通过（本项目最危险的一类假绿）。</summary>
    private void PrepareForEffect(AbilityData ab, Card caster, List<object> targets)
    {
        try
        {
            if (ab == null || ab.effects == null)
                return;
            foreach (EffectData eff in ab.effects)
            {
                if (eff is EffectExhaust && caster != null)
                    caster.exhausted = true;

                EffectClearStatus cs = eff as EffectClearStatus;
                if (cs != null)
                {
                    StatusType pt = cs.status != null ? cs.status.effect : StatusType.Stealth;
                    if (AbilityHasStatus(ab, pt))
                        pt = StatusType.AddAttack;
                    foreach (object o in targets)
                    {
                        Card tc = o as Card;
                        if (tc != null)
                            tc.AddStatus(pt, 1, 0);
                    }
                }
            }
        }
        catch { }
    }

    private static bool AbilityHasStatus(AbilityData ab, StatusType st)
    {
        if (ab == null || ab.status == null)
            return false;
        foreach (StatusData s in ab.status)
            if (s != null && s.effect == st)
                return true;
        return false;
    }

    // ---------------- 状态快照与差分（与基线探针同口径） ----------------
    private Dictionary<string, int> Snapshot()
    {
        Dictionary<string, int> d = new Dictionary<string, int>();
        foreach (Player pl in new Player[] { p0, p1 })
        {
            if (pl == null) continue;
            string k = "P" + pl.player_id;
            d[k + ".hp"] = pl.hp;
            d[k + ".mana"] = pl.mana;
            d[k + ".mana_max"] = pl.mana_max;
            d[k + ".mana_max_total"] = pl.mana_max_total;
            d[k + ".kills"] = pl.kill_count;
            d[k + ".skip"] = pl.skip_turns;
            d[k + ".n_deck"] = pl.cards_deck.Count;
            d[k + ".n_hand"] = pl.cards_hand.Count;
            d[k + ".n_board"] = pl.cards_board.Count;
            d[k + ".n_equip"] = pl.cards_equip.Count;
            d[k + ".n_discard"] = pl.cards_discard.Count;
            d[k + ".n_secret"] = pl.cards_secret.Count;
            d[k + ".n_temp"] = pl.cards_temp.Count;
            Agg(d, k, "board", pl.cards_board);
            Agg(d, k, "hand", pl.cards_hand);
            Agg(d, k, "deck", pl.cards_deck);
            Agg(d, k, "discard", pl.cards_discard);
            Agg(d, k, "equip", pl.cards_equip);
            Agg(d, k, "secret", pl.cards_secret);
            Agg(d, k, "temp", pl.cards_temp);
        }
        return d;
    }

    private void Agg(Dictionary<string, int> d, string pkey, string zone, List<Card> list)
    {
        if (list == null) return;
        foreach (Card c in list)
        {
            if (c == null) continue;
            string pre = pkey + "." + zone + "." + (c.card_id ?? "?");
            Bump(d, pre + ".cnt", 1);
            Bump(d, pre + ".hp", c.hp);
            Bump(d, pre + ".atk", c.attack);
            Bump(d, pre + ".mana", c.mana);
            Bump(d, pre + ".dmg", c.damage);
            Bump(d, pre + ".exh", c.exhausted ? 1 : 0);
            Bump(d, pre + ".onatk", c.attack_ongoing);
            Bump(d, pre + ".onhp", c.hp_ongoing);
            Bump(d, pre + ".onmana", c.mana_ongoing);
            int sig = 0;
            try { sig = c.StatusSignature(); } catch { }
            if (sig != 0) Bump(d, pre + ".sig_nz", 1);
            //★状态要**逐条**入快照：原来只记"签名非零"的计数 → 状态 A 变状态 B、或两个都非零之间的变化都看不见，
            //  于是"沉默/清状态/隐身"这类用例两边都是"(无状态变化)"的**假绿**（玩家/卡牌状态迁移必须能被差分到）。
            if (c.status != null)
            {
                foreach (CardStatus st in c.status)
                {
                    if (st == null) continue;
                    Bump(d, pre + ".st_" + st.type, st.value + 1);          //+1：值为 0 的状态也要体现"存在"
                    Bump(d, pre + ".st_" + st.type + "_dur", st.duration + 1);
                }
            }
            //★能力数量：添加技能(202050)/复位类效果要能看出来
            int ab_n = 0;
            try { List<AbilityData> abs = c.GetAbilities(); ab_n = abs != null ? abs.Count : 0; } catch { }
            if (ab_n != 0) Bump(d, pre + ".ab_n", ab_n);
        }
    }

    private static void Bump(Dictionary<string, int> d, string k, int v)
    {
        int cur;
        d[k] = d.TryGetValue(k, out cur) ? cur + v : v;
    }

    private static string Diff(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        SortedSet<string> keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string k in a.Keys) keys.Add(k);
        foreach (string k in b.Keys) keys.Add(k);
        List<string> parts = new List<string>();
        foreach (string k in keys)
        {
            int av, bv;
            bool ha = a.TryGetValue(k, out av), hb = b.TryGetValue(k, out bv);
            if (!ha) av = 0;
            if (!hb) bv = 0;
            if (av != bv) parts.Add(k + ":" + av + "→" + bv);
            if (parts.Count >= 60) { parts.Add("…(截断)"); break; }
        }
        return parts.Count == 0 ? "(无状态变化)" : string.Join("; ", parts.ToArray());
    }
}
