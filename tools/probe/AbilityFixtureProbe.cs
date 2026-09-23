using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Client;
using TcgEngine.UI;
using TcgEngine.Gameplay;

/// <summary>【临时探针】逐能力隔离夹具 —— Phase 0-B 运行层基线（主判据）。
///
/// 为什么不用"整局回放比对"：本引擎洗牌走 `GameLogic.random`（System.Random 实例）、
/// 规则图随机走 `GraphRuntime` 自持 System.Random、AI 还在后台线程 —— 整局不可复现（已实测两次不一致）。
/// 因此改为**受控夹具逐能力执行**：每例先把夹具重置到固定状态、把随机源重置到固定种子，
/// 只记录"执行前后的状态差分"，差分与卡序无关、与 uid 无关 → 同版本两次运行必一致。
///
/// 用法：
///   ① 拷到 Assets/TcgEngine/Scripts/__TempProbe/ 并编译
///   ② 写标记文件 {persistentDataPath}/Workshop/fixture.json（内容 {"seed":12345}）
///   ③ 进 Play：探针自己开一局（**不开 ai_vs_ai**，对局静默等输入 → 状态不会被 AI 动）
///      逐条跑 tools/card_baseline.tsv 里的能力，写真值到 tools/card_runtime_baseline.tsv
///   ④ 迁移后再跑一次，用 tools/diff_runtime_baseline.ps1 逐行比对
///
/// 记录口径：每个用例 = 一张卡的某个能力，在固定夹具（caster=被测卡、双方固定随从/手牌/牌库）
///          上按该能力的"目标模式"执行旧实现（DoEffects / DoOngoingEffects），
///          记录 `键:前→后` 形式的状态差分（排序后拼接，保证可比）。
public class AbilityFixtureProbe : MonoBehaviour
{
    [Serializable]
    public class Cfg
    {
        public int seed = 20250919;
        public bool onlyActionable = false;   //true=只跑本夹具支持的目标模式（默认也记录不支持项，便于看覆盖率）
        public int maxCases = 0;              //0=全部
    }

    public static string FlagPath { get { return Path.Combine(Application.persistentDataPath, "Workshop", "fixture.json"); } }
    private static string CasesPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/card_baseline.tsv")); } }
    private static string OutPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/card_runtime_baseline.tsv")); } }

    private Cfg cfg = new Cfg();
    private GameLogic logic;
    private Game data;
    private Player p0, p1;

    private float _t;
    private int phase;             // 0=等待开场 1=等待就绪 2=跑用例 3=完成
    private bool started;
    private readonly StringBuilder outBuf = new StringBuilder();
    private readonly List<string> cap = new List<string>();     // 本用例期间的日志
    private int pass, skip, err;
    private readonly List<string> caseLines = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        try
        {
            if (!File.Exists(FlagPath)) return;
            Cfg c = JsonUtility.FromJson<Cfg>(File.ReadAllText(FlagPath)) ?? new Cfg();
            GameObject go = new GameObject("__AbilityFixtureProbe");
            DontDestroyOnLoad(go);
            AbilityFixtureProbe p = go.AddComponent<AbilityFixtureProbe>();
            p.cfg = c;
            Debug.Log("[夹具] 启动：seed=" + c.seed + " cases=" + CasesPath);
        }
        catch (Exception e) { Debug.LogError("[夹具] 启动失败: " + e.Message); }
    }

    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }

    private void OnLog(string msg, string stack, LogType type)
    {
        if (string.IsNullOrEmpty(msg)) return;
        if (cap.Count < 40) cap.Add(msg.Replace("\n", " ").Replace("\r", " "));
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

    // ---------------- 开局（照抄 AutoBattleRunner，但不开 ai_vs_ai → 静默等输入） ----------------
    private void StartMatch()
    {
        try
        {
            GameplayData g = GameplayData.Get();
            DeckData pd = g.test_deck != null ? g.test_deck
                : (g.free_decks != null && g.free_decks.Length > 0 ? g.free_decks[0] : null);
            DeckData ad = g.test_deck_ai != null ? g.test_deck_ai
                : (g.ai_decks != null && g.ai_decks.Length > 0 ? g.ai_decks[0] : null);
            if (pd == null || ad == null) { Fail("无可用卡组"); return; }

            g.ai_vs_ai = false;      //★ 关键：玩家0交给"人"，对局不会自己推进 → 夹具安静
            GameClient.game_settings.game_type = GameType.Solo;
            GameClient.game_settings.game_mode = GameMode.Casual;
            GameClient.game_settings.game_uid = "fixture_phase0";
            GameClient.game_settings.scene = (g.arena_list != null && g.arena_list.Length > 0) ? g.arena_list[0] : "Game";
            GameClient.player_settings.deck = new UserDeckData(pd);
            GameClient.ai_settings.deck = new UserDeckData(ad);
            GameClient.ai_settings.ai_level = g.ai_level;
            Debug.Log("[夹具] 开局：player=" + pd.id + " ai=" + ad.id + " ai_vs_ai=false");
            MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual);
            started = true;
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
        foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
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
            if (logic == null) { Debug.Log("[夹具] 还没找到 GameLogic…"); return; }
            data = logic.GetGameData();
            if (data == null || data.players == null || data.players.Length < 2) { Debug.Log("[夹具] 对局尚未就绪…"); return; }
            p0 = data.GetPlayer(0); p1 = data.GetPlayer(1);
            if (p0 == null || p1 == null) return;

            //★ 让对局彻底静下来：双方都不交给 AI，并把先手/当前玩家钉在 0
            p0.is_ai = false; p1.is_ai = false;
            data.first_player = 0;
            data.current_player = 0;
            Debug.Log("[夹具] 已挂载并静默对局：p0=" + (p0.username ?? "?") + " p1=" + (p1.username ?? "?")
                + " 回合=" + data.turn_count + " 手牌=" + p0.cards_hand.Count + "/" + p1.cards_hand.Count);
            phase = 2;
        }
        catch (Exception e) { Debug.Log("[夹具] 挂载异常: " + e.Message); }
    }

    private void Fail(string why)
    {
        Debug.LogError("[夹具] 失败：" + why);
        try { File.WriteAllText(OutPath, "# 夹具失败：" + why, new UTF8Encoding(false)); } catch { }
        try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
        phase = 3; enabled = false;
    }

    // ---------------- 主循环：逐用例 ----------------
    private IEnumerator RunAll()
    {
        string[] lines;
        try { lines = File.ReadAllLines(CasesPath, Encoding.UTF8); }
        catch (Exception e) { Fail("读用例失败: " + e.Message); yield break; }

        outBuf.AppendLine("# 能力运行层基线（逐能力隔离夹具）  seed=" + cfg.seed + "  at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        outBuf.AppendLine("# 列：结果 | 卡 | 能力 | 触发 | 目标 | 状态差分 | 备注");

        int ran = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            string ln = lines[i];
            if (string.IsNullOrEmpty(ln)) continue;
            string[] f = ln.Split('\t');
            if (f.Length < 19) continue;
            string cardId = f[0], cardType = f[1], abilId = f[7], trigger = f[8], target = f[9];
            if (string.IsNullOrEmpty(abilId) || abilId == "(无能力)") continue;

            string result, delta, note;
            cap.Clear();
            RunOne(cardId, abilId, trigger, target, out result, out delta, out note);
            if (result == "ok") pass++; else if (result == "skip") skip++; else err++;

            string noteAll = note;
            if (cap.Count > 0) noteAll += (noteAll.Length > 0 ? "｜" : "") + "log=" + Trunc(string.Join(" / ", cap.ToArray()), 220);
            caseLines.Add(result + "\t" + cardId + "\t" + abilId + "\t" + trigger + "\t" + target + "\t" + delta + "\t" + noteAll);

            ran++;
            if (ran % 20 == 0) Debug.Log("[夹具] 进度 " + ran + " 例（ok=" + pass + " skip=" + skip + " err=" + err + "）");
            if (cfg.maxCases > 0 && ran >= cfg.maxCases) break;
            yield return null;
        }

        foreach (string c in caseLines) outBuf.AppendLine(c);
        outBuf.AppendLine("# 汇总：执行 " + pass + "｜跳过 " + skip + "｜异常 " + err + "｜共 " + (pass + skip + err));

        try { File.WriteAllText(OutPath, outBuf.ToString(), new UTF8Encoding(false)); }
        catch (Exception e) { Debug.LogError("[夹具] 写结果失败: " + e.Message); }
        Debug.Log("[夹具] 完成：ok=" + pass + " skip=" + skip + " err=" + err + " → " + OutPath);
        try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
        enabled = false;
    }

    private static string Trunc(string s, int n) { return s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…"); }

    // ---------------- 单个用例 ----------------
    private void RunOne(string cardId, string abilId, string trigger, string target, out string result, out string delta, out string note)
    {
        result = "err"; delta = ""; note = "";
        try
        {
            AbilityData ab = AbilityData.Get(abilId);
            if (ab == null) { result = "skip"; note = "能力不在运行期字典（id 未加载）"; return; }
            CardData cd = CardData.Get(cardId);
            if (cd == null) { result = "skip"; note = "卡定义未加载"; return; }

            SeedRng(cfg.seed);
            ResetFixture();
            Card caster = MakeCaster(cd);
            if (caster == null) { result = "skip"; note = "无法构造施法卡"; return; }

            if (!CanAutoResolve(target)) { result = "skip"; note = "目标模式需交互/需前置状态：" + target; return; }

            bool ongoing = trigger == "Ongoing";
            Dictionary<string, int> before = Snapshot();
            Apply(ab, caster, ongoing, out note);
            Dictionary<string, int> after = Snapshot();
            delta = Diff(before, after);
            if (delta.Length == 0) delta = "(无状态变化)";
            result = "ok";
        }
        catch (Exception e)
        {
            result = "err";
            note = "异常: " + (e.InnerException != null ? e.InnerException.Message : e.Message);
        }
    }

    /// <summary>本夹具能自动确定目标的目标模式（其余如实记为 skip，不假通过）</summary>
    private static bool CanAutoResolve(string target)
    {
        switch (target)
        {
            case "None": case "Self": case "PlayerSelf": case "PlayerOpponent": case "AllPlayers":
            case "AllCardsBoard": case "AllCardsHand": case "AllCardsAllPiles": case "AllSlots":
            case "AllCardData": case "SelectTarget": case "PlayTarget": case "CardSelector": case "AbilityTriggerer":
                return true;
            default:
                return false;   // ChoiceSelector / ValueSelector / EquippedCard / Last* → 需交互或前置状态
        }
    }

    /// <summary>按目标模式执行旧实现（效果语义直测：目标由夹具决定）</summary>
    private void Apply(AbilityData ab, Card caster, bool ongoing, out string note)
    {
        note = "";
        switch (ab.target)
        {
            case AbilityTarget.None:
                ab.DoEffects(logic, caster);
                return;
            case AbilityTarget.Self:
                if (ongoing) ab.DoOngoingEffects(logic, caster, caster); else ab.DoEffects(logic, caster, caster);
                return;
            case AbilityTarget.PlayerSelf:
                if (ongoing) ab.DoOngoingEffects(logic, caster, p0); else ab.DoEffects(logic, caster, p0);
                return;
            case AbilityTarget.PlayerOpponent:
                if (ongoing) ab.DoOngoingEffects(logic, caster, p1); else ab.DoEffects(logic, caster, p1);
                return;
            case AbilityTarget.AllPlayers:
                foreach (Player pl in new Player[] { p0, p1 })
                { if (ongoing) ab.DoOngoingEffects(logic, caster, pl); else ab.DoEffects(logic, caster, pl); }
                return;
            case AbilityTarget.AllCardsBoard:
                //与引擎一致：AllCardsBoard 只迭代"场上卡"，不含玩家（玩家走 PlayerSelf/AllPlayers）
                foreach (Card c in AllBoard()) { if (ongoing) ab.DoOngoingEffects(logic, caster, c); else ab.DoEffects(logic, caster, c); }
                return;
            case AbilityTarget.AllCardsHand:
            case AbilityTarget.AllCardsAllPiles:
                foreach (Card c in AllPiles(ab.target == AbilityTarget.AllCardsHand)) { ab.DoEffects(logic, caster, c); }
                return;
            case AbilityTarget.AllSlots:
                for (int pid = 0; pid <= 1; pid++)
                    for (int x = 1; x <= 5; x++)
                    { Slot s = new Slot(x, Slot.y_min, pid); if (ab.AreTargetConditionsMet(data, caster, s)) ab.DoEffects(logic, caster, s); }
                return;
            case AbilityTarget.AllCardData:
                note = "近似：用施法卡自身定义充当 AllCardData";
                ab.DoEffects(logic, caster, caster.CardData);
                return;
            case AbilityTarget.AbilityTriggerer:
                note = "近似：用敌方首个角色充当触发者";
                { Card t = FirstBoard(1); if (t != null) ab.DoEffects(logic, caster, t); }
                return;
            default:
                // SelectTarget / PlayTarget / CardSelector：自动挑一个合法目标（保持确定性）
                {
                    List<Card> targets = null;
                    try { targets = ab.GetCardTargets(data, caster); } catch { targets = null; }
                    Card picked = (targets != null && targets.Count > 0) ? targets[0] : FirstBoard(1);
                    if (picked != null) { note = "取合法目标首选 " + picked.card_id; if (ongoing) ab.DoOngoingEffects(logic, caster, picked); else ab.DoEffects(logic, caster, picked); }
                    else note = "无可选目标（未执行）";
                }
                return;
        }
    }

    private List<Card> AllBoard()
    {
        List<Card> r = new List<Card>();
        foreach (Player pl in new Player[] { p0, p1 }) foreach (Card c in pl.cards_board) if (c != null) r.Add(c);
        r.Sort((a, b) => string.CompareOrdinal(a.card_id, b.card_id));
        return r;
    }

    private Card FirstBoard(int pid)
    {
        Player pl = data.GetPlayer(pid);
        if (pl == null) return null;
        foreach (Card c in pl.cards_board) if (c != null) return c;
        return null;
    }

    private List<Card> AllPiles(bool handOnly)
    {
        List<Card> r = new List<Card>();
        foreach (Player pl in new Player[] { p0, p1 })
        {
            Add(r, pl.cards_hand);
            if (!handOnly)
            {
                Add(r, pl.cards_board); Add(r, pl.cards_equip); Add(r, pl.cards_discard);
                Add(r, pl.cards_secret); Add(r, pl.cards_temp); Add(r, pl.cards_deck);
            }
        }
        r.Sort((a, b) => string.CompareOrdinal(a.card_id, b.card_id));
        return r;
    }

    private static void Add(List<Card> r, List<Card> src)
    {
        if (src == null) return;
        foreach (Card c in src) if (c != null && !r.Contains(c)) r.Add(c);
    }

    // ---------------- 夹具：重置 + 构造 ----------------
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

        // 固定随从：p1 两个、p0 一个（供"敌方/友方/全体"类目标有确定落点）
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

        // 固定牌库/手牌（数量确定，内容用同一张牌，保证差分可比）
        CardData filler = CardData.Get("woodland");
        if (filler == null) filler = PickBoardCard();
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
        CardData filler = CardData.Get("woodland") ?? PickBoardCard();
        if (cd.type == CardType.Hero)
        {
            Card hero = Card.Create(cd, VariantData.GetDefault(), p0);
            p0.hero = hero;
            return hero;
        }
        if (cd.IsBoardCard())
        {
            Card c = Card.Create(cd, VariantData.GetDefault(), p0);
            bool ok = false;
            try { ok = logic.PlaceCardOnBoard(c, new Slot(4, Slot.y_min, 0)); } catch { ok = false; }
            if (!ok) { try { p0.AddCard(p0.cards_hand, c); } catch { } }
            return c;
        }
        // 法术/装备等：按引擎语义"从手牌施放"，施法卡本身就在手牌
        Card s = Card.Create(cd, VariantData.GetDefault(), p0);
        try { p0.AddCard(p0.cards_hand, s); } catch { }
        return s;
    }

    // ---------------- 随机源固定（反射，避免改产品代码） ----------------
    private void SeedRng(int seed)
    {
        try { UnityEngine.Random.InitState(seed); } catch { }
        try
        {
            FieldInfo f = typeof(GameLogic).GetField("random", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null && f.FieldType == typeof(System.Random)) f.SetValue(logic, new System.Random(seed));
        }
        catch { }
        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("TcgEngine.Workshop.GraphRuntime", false);
                if (t == null) t = asm.GetType("GraphRuntime", false);
                if (t == null) continue;
                FieldInfo f = t.GetField("rng", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null && f.FieldType == typeof(System.Random)) f.SetValue(null, new System.Random(seed));
                break;
            }
        }
        catch { }
    }

    // ---------------- 状态快照与差分 ----------------
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
            int sig = SafeSig(c);
            if (sig != 0) Bump(d, pre + ".sig_nz", 1);
        }
    }

    private static void Bump(Dictionary<string, int> d, string k, int v)
    {
        int cur;
        d[k] = d.TryGetValue(k, out cur) ? cur + v : v;
    }

    private static int SafeSig(Card c) { try { return c.StatusSignature(); } catch { return 0; } }

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
        return parts.Count == 0 ? "" : string.Join("; ", parts.ToArray());
    }
}
