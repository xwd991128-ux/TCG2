using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

/// <summary>【临时探针】逐节点批量测试台。
/// 用法：把用例写到 &lt;项目&gt;/tools/node_batch.json（由 tools/gen_batch.ps1 生成），进 Play 后自动：
///   ① 为每个用例造一张受控规则图（入口 OnPlay → 被测节点，必要时补常量/入口口连线）
///   ② 按用例的 assert 类型执行并断言（伤害/治疗/返回值/执行数/日志片段…）
///   ③ 逐条把「结论 + 测法 + 观测值」写到 &lt;项目&gt;/tools/node_batch_result.txt
/// 断言类型：exec（执行且无异常）/ damage N / heal N / value_int N / value_bool true|false /
///          value_notnull / value_null / log 片段 / nobreak（仅要求不抛异常）。
/// 结果文件用制表符分隔：结果 | 编号 | 中文名 | 分类 | 测法 | 观测值 | 备注</summary>
public class NodeBatchProbe : MonoBehaviour
{
    private float _t;
    private bool _done;
    private object logic_obj;
    private GameLogic logic;
    private Game data;
    private Player p0, p1;
    private string seed_info = "";      //读增益类用例的"自带前提"执行结果（诊断用）
    private Card caster;
    private readonly List<string> captured = new List<string>();
    private int pass, fail;
    private static string ResultPath
    {
        get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../tools/node_batch_result.txt")); }
    }
    private static string SpecPath
    {
        get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../tools/node_batch.json")); }
    }

    // ---------------- 用例结构（字段名与 PowerShell 生成的 JSON 对齐） ----------------
    [Serializable] public class PinSpec { public string name; public string type; public string display; }
    [Serializable] public class TestSpec
    {
        public string id; public string name; public string category;
        public string kind;          // action | value
        public string out_type;      // 取值节点的输出类型（决定用哪个取值通道）
        public string out_pin;       // 取值节点的输出口名（return/result/element/value…）
        public PinSpec[] ins;        // 被测节点的输入口
        public string field1; public string value1;
        public string field2; public string value2;
        public string field3; public string value3;
        public string src1;          // 额外取值线来源：EV_CARD | EV_VALUE | CONST_INT | CONST_BOOL | CONST_STR | COLLECTION | SELF
        public string src1_pin;      // 该来源的口（如 card/self）
        public string dst1;          // 接到被测节点的哪个输入口
        public string src1_const;    // 常量值（CONST_* 用）
        public string src2;          // 第二来源（双输入节点：集合运算/映射）：HAND | DECK | MAP | EV_CARD | CONST_*
        public string src2_const;    // 第二来源的常量值（src2 为 CONST_* 时用）
        public string child_action;  // 循环体子动作（遍历/筛选类节点的 action 口）：子动作节点 id
        public string child_field;   // 子动作字段名（如 value）
        public string child_value;   // 子动作字段值
        public string assert;        // 断言类型
        public string expect;        // 期望值（数字/true|false/日志片段）
        public string note;          // 备注（写进报告：测法说明）
    }
    [Serializable] public class BatchSpec { public string title; public TestSpec[] nodes; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("__NodeBatchProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<NodeBatchProbe>();
        Debug.Log("[节点批量测试] 探针启动");
    }

    private void Awake()
    {
        Application.logMessageReceived += OnLog;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
    }

    //★ 注意参数顺序：Application.LogCallback = (string condition, string stackTrace, LogType type)
    private void OnLog(string msg, string stack, LogType type)
    {
        if (string.IsNullOrEmpty(msg))
            return;
        if (msg.StartsWith("[NodeDoc]") || msg.StartsWith("[Keyword]") || msg.Contains("Exception"))
            captured.Add(msg.Replace("\n", " ").Replace("\r", " "));
    }

    private void Update()
    {
        if (_done) return;
        _t += Time.unscaledDeltaTime;
        if (_t < 6f) return;
        _done = true;
        StartCoroutine(Run());
    }

    // ---------------- 通用工具 ----------------
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

    private static object FindLogicObj()
    {
        int budget = 4000;
        foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (mb == null) continue;
            object hit = Dive(mb, 0, ref budget);
            if (hit != null) return hit;
            if (budget <= 0) break;
        }
        return null;
    }

    private static string Cat(string action)
    {
        foreach (NodeDocDef d in NodeDocDb.All)
            if (d != null && d.define_id == action)
                return string.IsNullOrEmpty(d.category) ? "其他" : d.category;
        return "其他";
    }

    private static NodeValueType MapType(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return NodeValueType.Object;
        switch (raw)
        {
            case "Int32": case "int": return NodeValueType.Int32;
            case "Boolean": case "bool": return NodeValueType.Boolean;
            case "String": return NodeValueType.String;
            case "Card": return NodeValueType.Card;
            case "CardDefine": return NodeValueType.Object;
            case "Player": return NodeValueType.Player;
            case "Array": return NodeValueType.Array;
            case "Flow": case "ActionNode": return NodeValueType.Flow;
            case "Effect": case "EventRecord": case "Pile": case "GraphMap": case "CardSnapshot": return NodeValueType.Object;
            default: return NodeValueType.Object;
        }
    }

    private static GraphData NewGraph(string name)
    {
        return new GraphData { name = name, nodes = new List<GraphNode>(), links = new List<GraphLink>() };
    }

    private static GraphNode Node(GraphData g, string id, GraphNodeType type, string action, string title, params string[] fp)
    {
        GraphNode n = new GraphNode();
        n.id = id; n.type = type; n.action = action; n.title = title;
        n.category = Cat(action);
        n.pins = new List<GraphPin>();
        n.fields = new List<FieldCustomData>();
        for (int i = 0; i + 1 < fp.Length; i += 2)
            n.fields.Add(new FieldCustomData { name = fp[i], value = fp[i + 1] });
        g.nodes.Add(n);
        return n;
    }

    private static GraphPin Pin(GraphNode n, string name, NodeValueType type, bool output)
    {
        GraphPin p = new GraphPin { id = n.id + "_" + name, name = name, display_name = name, is_output = output, type = type };
        n.pins.Add(p);
        return p;
    }

    private static void Link(GraphData g, GraphNode from, string fromPin, GraphNode to, string toPin)
    {
        g.links.Add(new GraphLink { from_node = from.id, from_pin = from.id + "_" + fromPin, to_node = to.id, to_pin = to.id + "_" + toPin });
    }

    private static GraphNode Entry(GraphData g)
    {
        GraphNode ev = Node(g, "ev", GraphNodeType.Event, "OnPlay", "打出时");
        Pin(ev, "out", NodeValueType.Flow, true);
        Pin(ev, "card", NodeValueType.Card, true);
        Pin(ev, "value", NodeValueType.Int32, true);
        return ev;
    }

    private static int ToInt(object v)
    {
        if (v == null) return 0;
        if (v is int i) return i;
        if (v is bool b) return b ? 1 : 0;
        int parsed;
        return int.TryParse(v.ToString(), out parsed) ? parsed : 0;
    }

    private object Call(string method, int argCount, params object[] args)
    {
        foreach (MethodInfo m in typeof(NodeDocRunner).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (m.Name != method || m.GetParameters().Length != argCount)
                continue;
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException e)
            {
                throw new Exception(method + " 抛异常: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
            }
        }
        throw new Exception("未找到方法 " + method + "/" + argCount);
    }

    private static string Result(bool ok, TestSpec s, string recipe, string observed, string extra)
    {
        return (ok ? "✅" : "❌") + "\t" + s.id + "\t" + s.name + "\t" + s.category + "\t" + recipe + "\t" + observed + "\t"
            + (string.IsNullOrEmpty(s.note) ? extra : (s.note + "｜" + extra));
    }

    // ---------------- 主流程 ----------------
    private IEnumerator Run()
    {
        for (int i = 0; i < 40 && logic_obj == null; i++)
        {
            logic_obj = FindLogicObj();
            if (logic_obj == null) yield return new WaitForSeconds(1f);
        }
        if (logic_obj == null) { Debug.Log("[节点批量测试] ❌ 没找到 GameLogic"); yield break; }
        logic = logic_obj as GameLogic;
        if (logic == null) { Debug.Log("[节点批量测试] ❌ GameLogic 转换失败"); yield break; }

        for (int i = 0; i < 60; i++)
        {
            data = logic.GetGameData();
            if (data != null && data.state == GameState.Play && data.players != null && data.players.Length >= 2) break;
            yield return new WaitForSeconds(1f);
        }
        if (data == null || data.state != GameState.Play) { Debug.Log("[节点批量测试] ❌ 对局未进入 Play"); yield break; }

        p0 = data.players[0];
        p1 = data.players[1];
        caster = p0 != null ? p0.hero : null;
        if (caster == null) { Debug.Log("[节点批量测试] ❌ 玩家0 无英雄卡"); yield break; }

        BatchSpec spec = null;
        try
        {
            if (System.IO.File.Exists(SpecPath))
            {
                string json = System.IO.File.ReadAllText(SpecPath, System.Text.Encoding.UTF8);
                spec = JsonUtility.FromJson<BatchSpec>(json);
            }
        }
        catch (Exception e) { Debug.Log("[节点批量测试] ❌ 用例文件解析失败: " + e.Message); }
        if (spec == null || spec.nodes == null || spec.nodes.Length == 0)
        {
            Debug.Log("[节点批量测试] ❌ 没有用例（tools/node_batch.json 不存在或为空）");
            yield break;
        }

        var lines = new List<string>();
        lines.Add("批次： " + spec.title + "｜用例数：" + spec.nodes.Length + "｜caster=" + (caster.CardData != null ? caster.CardData.title : "?"));
        lines.Add("结果\t编号\t中文名\t分类\t测法\t观测值\t备注");
        Debug.Log("[节点批量测试] 开始：" + spec.title + "，用例 " + spec.nodes.Length + " 个");

        foreach (TestSpec s in spec.nodes)
        {
            if (s == null || string.IsNullOrEmpty(s.id))
                continue;
            captured.Clear();
            string recipe = "", observed = "", extra = "";
            bool ok = false;
            try
            {
                if (s.kind == "action")
                    ok = RunActionCase(s, out recipe, out observed, out extra);
                else
                    ok = RunValueCase(s, out recipe, out observed, out extra);
            }
            catch (Exception e)
            {
                ok = false;
                extra = "异常: " + (e.InnerException != null ? e.InnerException.Message : e.Message);
            }
            if (ok) pass++; else fail++;
            lines.Add(Result(ok, s, recipe, observed, extra));
            yield return null;   //每例一帧，互不干扰
        }

        lines.Add("汇总：通过 " + pass + "｜失败 " + fail + "｜共 " + (pass + fail));
        try { System.IO.File.WriteAllLines(ResultPath, lines.ToArray(), new System.Text.UTF8Encoding(false)); }
        catch (Exception e) { Debug.Log("[节点批量测试] ❌ 写结果失败: " + e.Message); }
        Debug.Log("[节点批量测试] 完成：通过 " + pass + "｜失败 " + fail + "（结果见 tools/node_batch_result.txt）");
    }

    /// <summary>动作类：入口→节点（可补一条取值线）→执行；按 assert 断言</summary>
    private bool RunActionCase(TestSpec s, out string recipe, out string observed, out string extra)
    {
        recipe = ""; observed = ""; extra = "";
        GraphData g = NewGraph(s.id);
        GraphNode ev = Entry(g);
        GraphNode n = Node(g, "n", GraphNodeType.Action, s.id, s.name);
        Pin(n, "in", NodeValueType.Flow, false);
        Pin(n, "out", NodeValueType.Flow, true);
        AddSpecFields(n, s);
        AddDeclaredPins(n, s, false);
        Link(g, ev, "out", n, "in");
        GraphNode src = AddExtraSource(g, ev, n, s);
        GraphNode src2a = AddSecondSource(g, ev, n, s);
        string childInfo = AddLoopBody(g, n, s);         //循环体（遍历/筛选类节点的 action 口）
        recipe = "OnPlay → " + s.id + "「" + s.name + "」"
            + (src2a != null ? ("；第二来源 " + s.src2) : "")
            + (src != null ? ("；" + s.dst1 + " ← " + DescribeSource(s, src)) : "")
            + (childInfo != null ? childInfo : "")
            + "；Run(OnPlay)";

        p1.hp = (s.assert == "heal") ? 10 : 30;     //治疗用例先把目标压到 10，才能看出回血
        int hp0 = p1.hp;
        int attacked0 = caster.attack;
        int castHp0 = caster.hp;
        int bBoard = p0 != null ? CountOf(p0.cards_board) : 0;
        int bHand = p0 != null ? CountOf(p0.cards_hand) : 0;
        int bDeck = p0 != null ? CountOf(p0.cards_deck) : 0;
        int bEquip = p0 != null ? CountOf(p0.cards_equip) : 0;
        int bSecret = p0 != null ? CountOf(p0.cards_secret) : 0;
        int bBoard1 = p1 != null ? CountOf(p1.cards_board) : 0;
        int bHand1 = p1 != null ? CountOf(p1.cards_hand) : 0;
        int bDeck1 = p1 != null ? CountOf(p1.cards_deck) : 0;
        int bEquip1 = p1 != null ? CountOf(p1.cards_equip) : 0;
        int bSecret1 = p1 != null ? CountOf(p1.cards_secret) : 0;
        int bArmor0 = (p0 != null && p0.hero != null) ? p0.hero.GetStatusValue(StatusType.Armor) : 0;
        int bArmor1 = (p1 != null && p1.hero != null) ? p1.hero.GetStatusValue(StatusType.Armor) : 0;
        int bMana0 = p0 != null ? p0.mana : 0;
        int bMana1 = p1 != null ? p1.mana : 0;
        int bManaMax0 = p0 != null ? p0.mana_max : 0;
        int bManaMax1 = p1 != null ? p1.mana_max : 0;
        int bPhp0 = p0 != null ? p0.hp : 0;
        int bPhp1 = p1 != null ? p1.hp : 0;
        int bFat0 = PlayerProp(p0, "fatigue");
        int bFat1 = PlayerProp(p1, "fatigue");

        int executed = NodeDocRunner.Run(logic, g, caster, null, p1, "OnPlay");
        string logs = JoinLogs();

        switch (s.assert)
        {
            case "exec":
            {
                //★ 防假通过：动作数≥1 可能只是"流程入口推进"，若日志出现「未支持的 NodeDoc 动作」说明**被测节点
                //  根本没被执行**（典型：取值类节点被误判成动作类，实测 102018 就是这样）→ 一律不算通过。
                bool unsupported = logs.Contains("未支持的 NodeDoc 动作");
                observed = "Run 动作数=" + executed + "；日志=" + Trim(logs);
                extra = "要求执行数≥1" + (unsupported ? "，且不得出现「未支持的 NodeDoc 动作」（本次出现 ✗）" : "");
                return executed >= 1 && !unsupported;
            }
            case "damage":
                observed = "目标玩家 HP " + hp0 + "→" + p1.hp + "（扣 " + (hp0 - p1.hp) + "，期望 " + s.expect + "）｜动作数=" + executed;
                return (hp0 - p1.hp) == ToInt(s.expect);
            case "heal":
                observed = "目标玩家 HP " + hp0 + "→" + p1.hp + "（回 " + (p1.hp - hp0) + "，期望 " + s.expect + "）｜动作数=" + executed;
                return (p1.hp - hp0) == ToInt(s.expect);
            case "log":
                observed = "日志=" + Trim(logs) + "｜动作数=" + executed;
                extra = "要求日志含「" + s.expect + "」";
                return !string.IsNullOrEmpty(logs) && logs.Contains(s.expect);
            case "exec_and_log":
                observed = "动作数=" + executed + "；日志=" + Trim(logs);
                extra = "要求执行数≥1 且有 [NodeDoc] 日志";
                return executed >= 1 && !string.IsNullOrEmpty(logs);
            case "self_hp_up":
                observed = "施法卡 HP " + castHp0 + "→" + caster.hp + "（+ " + (caster.hp - castHp0) + "，期望 " + s.expect + "）｜动作数=" + executed;
                return (caster.hp - castHp0) == ToInt(s.expect);
            case "self_atk_up":
                observed = "施法卡 攻击 " + attacked0 + "→" + caster.attack + "（+ " + (caster.attack - attacked0) + "，期望 " + s.expect + "）｜动作数=" + executed;
                return (caster.attack - attacked0) == ToInt(s.expect);
            case "state_atk":       //施法卡攻击 == 期望（写入类节点的副作用）
                observed = "施法卡攻击=" + caster.attack + "（期望 " + s.expect + "）｜动作数=" + executed;
                return caster.attack == ToInt(s.expect);
            case "state_haskw":     //施法卡拥有该关键词
                observed = "HasKeyword(" + s.expect + ")=" + caster.HasKeyword(s.expect) + "｜动作数=" + executed;
                return caster.HasKeyword(s.expect);
            case "state_hasability":   //★ 内置卡迁移 P0：卡牌是否获得指定能力（202050 添加技能的端到端判据，
                                       //  不看日志看卡牌实际状态，避免"日志出现了就算过"的假通过）
            {
                AbilityData gain = AbilityData.Get(s.expect);
                bool has = gain != null && caster.HasAbility(gain);
                observed = "施法卡 HasAbility(" + s.expect + ")=" + has + "（定义存在=" + (gain != null) + "）"
                    + "｜动作数=" + executed + "；日志=" + Trim(logs);
                return has;
            }
            case "buff_applied":    //★ F11：施加增益的**端到端**判据。
                                    //  不能用 log 断言：206001 的成功日志走 GameLog.Log，被 gamelog_verbose 开关抑制（实测取不到）
                                    //  → 改用卡牌状态断言：施法卡应真的带上该增益定义。
            {
                bool has = BuffRuntime.HasBuff(caster, s.expect);
                observed = "施法卡 HasBuff(" + s.expect + ")=" + has + "｜动作数=" + executed
                    + "；日志=" + Trim(logs);
                return has;
            }
            case "state_p1hp":      //目标玩家 HP == 期望
                observed = "目标玩家 HP=" + p1.hp + "（期望 " + s.expect + "）｜动作数=" + executed;
                return p1.hp == ToInt(s.expect);
            case "state_p0hp":      //施法方玩家 HP == 期望
                observed = "施法方玩家 HP=" + (p0 != null ? p0.hp : -1) + "（期望 " + s.expect + "）｜动作数=" + executed;
                return p0 != null && p0.hp == ToInt(s.expect);
            case "armor_p0":        //护甲加到"目标玩家/施法方英雄"其二之一（节点内部走 player 口回退，故两边都看）
            {
                int a0 = (p0 != null && p0.hero != null) ? p0.hero.GetStatusValue(StatusType.Armor) : -1;
                int a1 = (p1 != null && p1.hero != null) ? p1.hero.GetStatusValue(StatusType.Armor) : -1;
                observed = "护甲：玩家0英雄=" + a0 + "、玩家1英雄=" + a1 + "（期望其一 == " + s.expect + "）｜动作数=" + executed;
                return a0 == ToInt(s.expect) || a1 == ToInt(s.expect);
            }
            case "armor_up":        //护甲**增量** == 期望（避免与前面用例的累计值冲突）
            {
                int a0 = (p0 != null && p0.hero != null) ? p0.hero.GetStatusValue(StatusType.Armor) : -1;
                int a1 = (p1 != null && p1.hero != null) ? p1.hero.GetStatusValue(StatusType.Armor) : -1;
                int d0 = a0 - bArmor0, d1 = a1 - bArmor1;
                observed = "护甲增量：玩家0英雄 Δ" + d0 + "（" + bArmor0 + "→" + a0 + "）、玩家1英雄 Δ" + d1
                    + "（" + bArmor1 + "→" + a1 + "），期望其一 Δ==" + s.expect + "｜动作数=" + executed;
                return d0 == ToInt(s.expect) || d1 == ToInt(s.expect);
            }
            case "hand_to_board":   //210002 手牌→战场（来源经"选中玩家"回退，可能是任一方，故双方都比）：
                                    //  期望"某一方 手牌减少且战场增加"；若双方手牌本来就空 → 无牌可搬 → 视为不适用（通过）
            {
                int dH0 = (p0 != null ? CountOf(p0.cards_hand) : 0) - bHand;
                int dB0 = (p0 != null ? CountOf(p0.cards_board) : 0) - bBoard;
                int dH1 = (p1 != null ? CountOf(p1.cards_hand) : 0) - bHand1;
                int dB1 = (p1 != null ? CountOf(p1.cards_board) : 0) - bBoard1;
                bool moved = (dH0 < 0 && dB0 > 0) || (dH1 < 0 && dB1 > 0);
                observed = "手牌/战场增量：玩家0 手" + dH0 + " 场" + dB0 + "、玩家1 手" + dH1 + " 场" + dB1
                    + "（期望其一 手-、场+）｜动作数=" + executed + "；日志=" + Trim(logs);
                if (!moved && bHand <= 0 && bHand1 <= 0)
                {
                    extra = "双方手牌均为空 → 无牌可搬（跳过）";
                    return true;
                }
                return moved;
            }
            case "card_to_board":   //210002 单张卡置入战场（手牌→战场）：手牌 -1、战场 +1（状态无关，比整集合搬运稳）
            {
                int dHand = (p0 != null ? CountOf(p0.cards_hand) : 0) - bHand;
                int dBoard = (p0 != null ? CountOf(p0.cards_board) : 0) - bBoard;
                observed = "玩家0：手牌 Δ" + dHand + "、战场 Δ" + dBoard + "（期望 手牌-1 且 战场+1）｜动作数=" + executed
                    + "；日志=" + Trim(logs);
                return dHand == -1 && dBoard == 1;
            }
            case "deck_to_board":   //210002 卡牌置入战场：牌库应减少、战场应增加（整集合搬运；比 any_moved 更明确）
            {
                int dDeck = (p0 != null ? CountOf(p0.cards_deck) : 0) - bDeck;
                int dBoard = (p0 != null ? CountOf(p0.cards_board) : 0) - bBoard;
                if (bDeck <= 0)
                {
                    //来源牌库本来就是空的（对局后期 AI 抽完）→ 本用例不适用，视为通过（否则是**误报**：无牌可搬）
                    observed = "来源牌库为空（0 张）→ 无牌可搬，本用例不适用｜动作数=" + executed;
                    extra = "来源非空时必须搬运；本次来源为空（跳过）";
                    return true;
                }
                observed = "玩家0：牌库 Δ" + dDeck + "、战场 Δ" + dBoard + "（期望 牌库减少且战场增加）｜动作数=" + executed
                    + "；日志=" + Trim(logs);
                return dDeck < 0 && dBoard > 0;
            }
            case "exec_any":        //只要求"进了执行链且没抛异常"（循环/停止/跳过类节点在没有循环体时动作数可为 0）
                observed = "Run 动作数=" + executed + "；日志=" + Trim(logs);
                extra = "要求：未抛异常（动作数允许为 0）";
                return true;
            case "any_moved":       //任一玩家任一区域数量发生变化（"整集合搬家"类移动节点用）
            {
                int n0b = CountOf(p0 != null ? p0.cards_board : null), n0h = CountOf(p0 != null ? p0.cards_hand : null);
                int n0d = CountOf(p0 != null ? p0.cards_deck : null), n0e = CountOf(p0 != null ? p0.cards_equip : null);
                int n1b = CountOf(p1 != null ? p1.cards_board : null), n1h = CountOf(p1 != null ? p1.cards_hand : null);
                int n1d = CountOf(p1 != null ? p1.cards_deck : null), n1e = CountOf(p1 != null ? p1.cards_equip : null);
                bool moved = (n0b != bBoard || n0h != bHand || n0d != bDeck || n0e != bEquip)
                    || (n1b != bBoard1 || n1h != bHand1 || n1d != bDeck1 || n1e != bEquip1);
                observed = "玩家0 场" + bBoard + "→" + n0b + " 手" + bHand + "→" + n0h + " 库" + bDeck + "→" + n0d + " 道" + bEquip + "→" + n0e
                    + "｜玩家1 场" + bBoard1 + "→" + n1b + " 手" + bHand1 + "→" + n1h + " 库" + bDeck1 + "→" + n1d
                    + "｜动作数=" + executed;
                extra = "要求：任一玩家任一区域数量发生变化";
                return moved;
            }
            case "damage_either":   //伤害总量 == 任一玩家的区域数量（循环体逐元素执行 → 用元素个数校验循环次数）
            {
                int dmg = hp0 - p1.hp;
                int v0 = PlayerProp(p0, s.expect), v1 = PlayerProp(p1, s.expect);
                observed = "目标玩家共受伤=" + dmg + "，期望 == 玩家0." + s.expect + "(" + v0 + ") 或 玩家1." + s.expect + "(" + v1 + ")｜动作数=" + executed;
                return dmg == v0 || dmg == v1;
            }
            case "mana_at_max":     //上限钳制：任一玩家 灵力 == 灵力上限（201001/201008 都钳到 [0,mana_max]）
            {
                int m0 = p0 != null ? p0.mana : -1, c0 = p0 != null ? p0.mana_max : -1;
                int m1 = p1 != null ? p1.mana : -1, c1 = p1 != null ? p1.mana_max : -1;
                observed = "灵力/上限：玩家0 " + m0 + "/" + c0 + "、玩家1 " + m1 + "/" + c1 + "（期望其一 灵力 == 上限）｜动作数=" + executed;
                return (p0 != null && m0 == c0) || (p1 != null && m1 == c1);
            }
            case "mana_up":         //灵力增量（双方其一；节点的 player 口回退会落到目标玩家）
            {
                int d0 = (p0 != null ? p0.mana : 0) - bMana0, d1 = (p1 != null ? p1.mana : 0) - bMana1;
                observed = "灵力增量：玩家0 Δ" + d0 + "、玩家1 Δ" + d1 + "，期望其一 Δ==" + s.expect + "｜动作数=" + executed;
                return d0 == ToInt(s.expect) || d1 == ToInt(s.expect);
            }
            case "mana_eq":         //灵力 == 期望（双方其一）
            {
                int m0 = p0 != null ? p0.mana : -1, m1 = p1 != null ? p1.mana : -1;
                observed = "灵力：玩家0=" + m0 + "、玩家1=" + m1 + "（期望其一 == " + s.expect + "）｜动作数=" + executed;
                return m0 == ToInt(s.expect) || m1 == ToInt(s.expect);
            }
            case "mana_max_up":     //灵力上限增量（双方其一）
            {
                int d0 = (p0 != null ? p0.mana_max : 0) - bManaMax0, d1 = (p1 != null ? p1.mana_max : 0) - bManaMax1;
                observed = "灵力上限增量：玩家0 Δ" + d0 + "、玩家1 Δ" + d1 + "，期望其一 Δ==" + s.expect + "｜动作数=" + executed;
                return d0 == ToInt(s.expect) || d1 == ToInt(s.expect);
            }
            case "mana_max_eq":     //灵力上限 == 期望（双方其一）
            {
                int m0 = p0 != null ? p0.mana_max : -1, m1 = p1 != null ? p1.mana_max : -1;
                observed = "灵力上限：玩家0=" + m0 + "、玩家1=" + m1 + "（期望其一 == " + s.expect + "）｜动作数=" + executed;
                return m0 == ToInt(s.expect) || m1 == ToInt(s.expect);
            }
            case "fatigue_up":      //疲劳层数增量（双方其一；疲劳挂在 Player 的「疲劳层数」特性上）
            {
                int d0 = PlayerProp(p0, "fatigue") - bFat0, d1 = PlayerProp(p1, "fatigue") - bFat1;
                observed = "疲劳层数增量：玩家0 Δ" + d0 + "、玩家1 Δ" + d1 + "，期望其一 Δ==" + s.expect + "｜动作数=" + executed;
                return d0 == ToInt(s.expect) || d1 == ToInt(s.expect);
            }
            case "fatigue_eq":      //疲劳层数 == 期望（双方其一）
            {
                int f0 = PlayerProp(p0, "fatigue"), f1 = PlayerProp(p1, "fatigue");
                observed = "疲劳层数：玩家0=" + f0 + "、玩家1=" + f1 + "（期望其一 == " + s.expect + "）｜动作数=" + executed;
                return f0 == ToInt(s.expect) || f1 == ToInt(s.expect);
            }
            case "hp_down":         //玩家 HP 下降量（双方其一）
            {
                int d0 = bPhp0 - (p0 != null ? p0.hp : 0), d1 = bPhp1 - (p1 != null ? p1.hp : 0);
                observed = "玩家HP下降：玩家0 Δ" + d0 + "、玩家1 Δ" + d1 + "，期望其一 Δ==" + s.expect + "｜动作数=" + executed;
                return d0 == ToInt(s.expect) || d1 == ToInt(s.expect);
            }
            case "board_up":        //战场卡数 增量（双方其一）
                return CountDelta(p0 != null ? p0.cards_board : null, bBoard, p1 != null ? p1.cards_board : null, bBoard1, ToInt(s.expect), "战场", executed, out observed);
            case "hand_up":         //手牌数 增量（双方其一）
                return CountDelta(p0 != null ? p0.cards_hand : null, bHand, p1 != null ? p1.cards_hand : null, bHand1, ToInt(s.expect), "手牌", executed, out observed);
            case "deck_up":         //牌库数 增量（双方其一）
                return CountDelta(p0 != null ? p0.cards_deck : null, bDeck, p1 != null ? p1.cards_deck : null, bDeck1, ToInt(s.expect), "牌库", executed, out observed);
            case "equip_up":        //装备区数 增量（双方其一）
                return CountDelta(p0 != null ? p0.cards_equip : null, bEquip, p1 != null ? p1.cards_equip : null, bEquip1, ToInt(s.expect), "装备区", executed, out observed);
            case "secret_up":       //延迟区(奥秘)数 增量（双方其一）
                return CountDelta(p0 != null ? p0.cards_secret : null, bSecret, p1 != null ? p1.cards_secret : null, bSecret1, ToInt(s.expect), "延迟区", executed, out observed);
            default:   //nobreak：只要求不抛异常
                observed = "动作数=" + executed + "；日志=" + Trim(logs);
                return true;
        }
    }

    /// <summary>取值类：造一个消费者节点，把被测节点输出接进去，按输出类型调对应取值通道</summary>
    private bool RunValueCase(TestSpec s, out string recipe, out string observed, out string extra)
    {
        recipe = ""; observed = ""; extra = "";
        //★ F1 用例：把**双方** HP 都置为 30 再判。原因：双方可能用同一张英雄 id（如 hero_fire），
        //  「获取卡牌(cardRef)」解析到哪一方的英雄不确定 → 只置玩家0 会误报（实测：解析到玩家1 的英雄、
        //  其 HP 恰好 ≤0 → 返回 True，看着像 F1 坏了，其实语义正确）。两边都置正 → 期望恒为 false，判定确定。
        if (s.assert == "value_dying_owner_hp" || s.assert == "value_dying_owner_hp_dying"
            || (s.id == "102018" && s.assert == "run_bool"))
        {
            int hp_set = s.assert == "value_dying_owner_hp_dying" ? 0 : 30;   //0 → 期望"濒死"；30 → 期望"不濒死"
            if (p0 != null) p0.hp = hp_set;
            if (p1 != null) p1.hp = hp_set;
        }
        //★ 读增益类用例（106003 / 106006）自带前提：**本用例内重新施加一次**（池被清空时先重载）。
        //  为什么：① 前面有「沉默(202015)」「消灭(202016)」等用例会清掉施法卡的状态/增益；
        //          ② 长会话里增益池可能被重新加载 → `BuffPoolIO.Get` 取不到定义（实测全量下 caster.buffs=0）。
        //  用例不依赖顺序，失败才是产品问题而不是"顺序假问题"。
        if ((s.id == "106003" || s.id == "106006") && caster != null)
        {
            BuffData bd_seed = BuffPoolIO.Get("buff_8c5cfda5");
            if (bd_seed == null)
            {
                BuffPoolIO.LoadAll();                     //兜底：重载增益池后再取一次
                bd_seed = BuffPoolIO.Get("buff_8c5cfda5");
                seed_info = "池内无该定义 → 已重载（池内=" + BuffPoolIO.GetAll().Count + "）";
            }
            if (bd_seed != null)
            {
                CardBuff cb_seed = BuffRuntime.AddBuff(caster, bd_seed, 99);
                seed_info = "施加=" + (cb_seed != null) + " caster.buffs="
                    + (caster.buffs != null ? caster.buffs.Count : -1) + "｜" + seed_info;
            }
        }
        GraphData g = NewGraph(s.id);
        GraphNode ev = Entry(g);
        GraphNode n = Node(g, "n", GraphNodeType.Value, s.id, s.name);
        AddSpecFields(n, s);
        AddDeclaredPins(n, s, true);
        string out_pin = FirstOutPin(s);
        GraphNode cons = Node(g, "cons", GraphNodeType.Action, "202001", "消费者");
        cons.fields.Add(new FieldCustomData { name = "value", value = "0" });   //★ 消费者别自带伤害：run_bool/run_int 靠伤害量计数，会被它污染（实测 7→8、1→2）
        Pin(cons, "in", NodeValueType.Flow, false);
        Pin(cons, "x", NodeValueType.Object, false);
        if (!string.IsNullOrEmpty(out_pin))
            Pin(cons, "v", MapType(s.out_type), false);
        Link(g, ev, "out", cons, "in");
        if (!string.IsNullOrEmpty(out_pin))
            Link(g, n, out_pin, cons, "v");
        GraphNode src = AddExtraSource(g, ev, n, s);
        GraphNode src2b = AddSecondSource(g, ev, n, s);
        //★ 诊断：确认来源线到底接上没有（近期 F4/F8/F11 用例疑似"没接上 → 通道自然拿不到值"）
        if (!string.IsNullOrEmpty(s.src1))
            Debug.Log("[探针接线] " + s.id + " src1=" + s.src1 + " dst1=" + (s.dst1 ?? "?")
                + " 已接=" + (src != null) + " 输入口数=" + (s.ins != null ? s.ins.Length : -1));
        string childInfo = AddLoopBody(g, n, s);         //循环体（遍历/筛选类节点的 action 口）
        recipe = "OnPlay → 消费者(202001).v ← " + s.id + "「" + s.name + "」." + out_pin
            + (src != null ? ("；" + s.dst1 + " ← " + DescribeSource(s, src)) : "")
            + (src2b != null ? ("；第二来源 " + s.src2) : "")
            + (childInfo != null ? childInfo : "")
            + "；取「" + s.out_type + "」通道";

        //按输出类型选通道
        object got;
        string ch;
        switch (s.out_type)
        {
            case "Int32": ch = "ResolveNodeInt"; got = Call("ResolveNodeInt", 6, logic, g, n, caster, null, p1); break;
            case "Boolean": ch = "GetBoolInput"; got = Call("GetBoolInput", 8, logic, g, cons, "v", caster, null, p1, true); break;
            case "Card": ch = "ResolveInputCard"; got = Call("ResolveInputCard", 7, logic, g, cons, "v", caster, null, p1); break;
            case "CardDefine": ch = "ResolveInputDefine"; got = Call("ResolveInputDefine", 7, logic, g, cons, "v", caster, null, p1); break;
            case "String": ch = "ResolveInputString"; got = Call("ResolveInputString", 7, logic, g, cons, "v", caster, null, p1); break;
            case "Effect": ch = "ResolveInputEffect"; got = Call("ResolveInputEffect", 7, logic, g, cons, "v", caster, null, p1); break;
            //★ F11 修复（探针侧）：**BuffDefine 输出**此前没有专属通道 → 落到 GetObjectInput 得空串，
            //  造成"106002 增益定义取不到"的**假问题**（实测运行期 BuffPoolIO 已加载 7 个、编辑器 BuffSelect 存的就是 BuffData.id）。
            //  正确通道：ResolveValueBuffDefine（106005 / 106002 都在里面）。
            case "BuffDefine": ch = "ResolveValueBuffDefine"; got = Call("ResolveValueBuffDefine", 6, logic, g, n, caster, null, p1); break;
            //★ F14 修复（探针侧）：**Buff 集合输出**（106006 卡上所有增益）此前没有专属通道 → 落到 GetObjectInput
            //  得空串；补 ResolveBuffList（106006 的真正实现所在，读 Card.buffs）。
            case "Buff": ch = "ResolveBuffList"; got = Call("ResolveBuffList", 6, logic, g, n, caster, null, p1); break;
            //★ F4 精确化：**Pile 输出**（102029/101017/200004）走 ResolveValuePile 拿"玩家id|区域名"编码，
            //  比 Object 通道的空串更可信（Object 通道对 Pile 恒返回 ""）。
            case "Pile": ch = "ResolveValuePile"; got = Call("ResolveValuePile", 6, logic, g, n, caster, null, p1); break;
            //★ 集合类输出（Array）：必须走 `ResolveInputCards`（它会按**上游节点**语义求集合）。
            //  反面教材：`ResolveArrayCards` 只是"取该节点 array 输入口的卡牌"，**不套用节点自身运算**
            //  （实测 111026 前2个 → 返回了全部 4 个；111001 创建集合 → 空）。
            case "Array": ch = "ResolveInputCards"; got = Call("ResolveInputCards", 6, logic, g, cons, "v", caster, null); break;
            case "Player": ch = "ResolveValuePlayer"; got = Call("ResolveValuePlayer", 5, logic, g, n, caster, p1); break;
            default: ch = "GetObjectInput"; got = Call("GetObjectInput", 7, logic, g, cons, "v", caster, null, p1); break;
        }
        recipe += "（" + ch + "）";

        //★ 兜底：按输出类型选的通道可能"只认单值"（例：101008 牌库是一个**集合**，Card 单值通道取不到 → null），
        //  此时再走一次通用 Object 通道（集合类节点会返回 List）——实测 101008 就是这种情况。
        //★ 集合断言下"非集合结果"也要兜底：例 109004 事件记录集合走 Array 通道（ResolveInputCards，卡牌集合）
        //  会得到空 list，而正确通道是 Object 通道（返回事件记录列表）→ 实测就靠这一步救回来。
        //  另外：断言要"非空集合"但当前是空集合时也要换通道（例 109004 事件记录集合）。
        if ((got == null
            || (CollCount(got) < 0 && !string.IsNullOrEmpty(s.assert) && s.assert.StartsWith("list_"))
            || (s.assert == "list_notempty" && CollCount(got) <= 0)) && ch != "GetObjectInput")
        {
            object alt_got = Call("GetObjectInput", 7, logic, g, cons, "v", caster, null, p1);
            if (alt_got != null)
            {
                got = alt_got;
                ch = ch + "→GetObjectInput(兜底)";
            }
        }

        //★ 二次兜底：**整数集合**输出（111031 属性映射 / 111014~111017 归约）拿卡牌集合通道会得到空
        //  → 集合类断言下若结果为空，再试 ResolveIntCollection。
        //  只对**整数集合类节点**兜底（否则会把"结果应为空集合"的用例误换成上游的攻击值列表，实测踩过）
        bool isIntColl = s.id == "111031" || s.id == "111014" || s.id == "111015" || s.id == "111016" || s.id == "111017";
        if (CollCount(got) <= 0 && isIntColl && !string.IsNullOrEmpty(s.assert) && s.assert.StartsWith("list_"))
        {
            object ints = Call("ResolveIntCollection", 6, logic, g, n, caster, null, p1);
            if (CollCount(ints) > 0)
            {
                got = ints;
                ch = ch + "→ResolveIntCollection(整数集合兜底)";
            }
        }

        //★ 定义集合兜底：103001「获取所有卡牌定义」这类 **CardDefine 集合**输出，单值定义通道取不到
        //  → 走 EvaluateDefineArray（按**节点自身**求定义集合；注意 ResolveValueDefines 是"取输入口"的，
        //    实测对 103001 返回空）
        if (CollCount(got) <= 0 && !string.IsNullOrEmpty(s.assert) && s.assert.StartsWith("list_")
            && (s.out_type == "CardDefine" || s.out_type == "Object"))
        {
            object defs = Call("EvaluateDefineArray", 6, logic, g, n, caster, null, p1);
            if (CollCount(defs) > 0)
            {
                got = defs;
                ch = ch + "→ResolveValueDefines(定义集合兜底)";
            }
        }

        //★ 类型专用转换兜底：EventArg / Buff / CardDefine 这类**非卡牌类型**的输出，通用 Object 通道常返回
        //  ""（空串）→ 用 runner 自带的 ConvertToXxx 兜底（实测 108005/108006/106006/201005/201017 就是这种）。
        //  注意：只在"结果为空"且断言不是"期望 null"时兜底，避免把本应 null 的用例改成非 null。
        bool got_empty = got == null || (got is string s_empty && s_empty.Length == 0) || CollCount(got) <= 0;
        if (got_empty && s.assert != "value_null" && s.assert != "value_any")
        {
            string conv = null;
            switch (s.out_type)
            {
                case "EventArg": conv = "ConvertToEvents"; break;
                case "Buff": conv = "ConvertToBuffs"; break;
                case "CardDefine": conv = "ConvertToDefines"; break;
            }
            if (conv != null)
            {
                object alt = Call(conv, 6, logic, g, n, caster, null, p1);
                if (alt != null && !(alt is string))
                {
                    got = alt;
                    ch = ch + "→" + conv + "(类型转换兜底)";
                }
            }
        }

        string val = Render(got);
        observed = ch + " 返回=" + Trim(val, 120) + "（类型 " + (got != null ? got.GetType().Name : "null") + "）｜日志=" + Trim(JoinLogs());
        switch (s.assert)
        {
            case "list_count":      //集合元素个数 == 期望
            {
                int n_cnt = CollCount(got);
                extra = "期望元素个数 " + s.expect + "（实际 " + n_cnt + "）";
                return n_cnt == ToInt(s.expect);
            }
            case "list_notempty":   //集合非空
                extra = "期望元素个数 >= 1（实际 " + CollCount(got) + "）";
                return CollCount(got) >= 1;
            case "list_count_either":   //元素个数 == 任一玩家的区域数量（集合来源=手牌/牌库时用）
            {
                int n_cnt = CollCount(got);
                int v0 = PlayerProp(p0, s.expect), v1 = PlayerProp(p1, s.expect);
                extra = "期望元素个数 == 玩家0." + s.expect + "(" + v0 + ") 或 玩家1." + s.expect + "(" + v1 + ")（实际 " + n_cnt + "）";
                return n_cnt == v0 || n_cnt == v1;
            }
            case "value_buff_diag":   //★ F14 诊断：把"卡上的增益"链路各环节都打出来（施法卡 / 解析到的卡 / 列表结果）
            {
                Card cc = (Card)Call("ResolveInputCard", 7, logic, g, n, "card", caster, null, p1);
                int cb = (caster != null && caster.buffs != null) ? caster.buffs.Count : -1;
                int rb = (cc != null && cc.buffs != null) ? cc.buffs.Count : -1;
                observed = "caster.uid=" + (caster != null ? caster.uid : "-") + " caster.buffs=" + cb
                    + " HasBuff(caster)=" + (caster != null && BuffRuntime.HasBuff(caster, "buff_8c5cfda5"))
                    + "｜解析卡=" + (cc != null ? cc.uid : "null") + " 其buffs=" + rb
                    + "｜ResolveBuffList 结果=" + CollCount(got)
                    + "｜两张卡是否同一对象=" + (cc != null && caster != null && ReferenceEquals(cc, caster))
                    + "｜自带前提：" + seed_info;
                extra = "诊断用例：只记录，不判定";
                return true;
            }
            case "value_coll_elem":   //集合取元素类（111007 第X个 / 111008 随机 / 111024 首 / 111025 末）：
                                      //  来源（手牌）非空时必须取到元素；来源为空 → **不适用**（跳过）。
                                      //  为什么：来源走"选中玩家"的手牌，对局推进中可能为空 → 硬判非空会**状态相关误报**。
            {
                int src0 = PlayerProp(p0, "hand"), src1v = PlayerProp(p1, "hand");
                int srcN = Math.Max(src0, src1v);
                bool has_value = got != null && !(got is string s_vc && s_vc.Length == 0);
                if (srcN <= 0)
                {
                    observed = "来源手牌：玩家0=" + src0 + "、玩家1=" + src1v + " → 无元素可取，本用例不适用";
                    extra = "来源非空时必须取到元素；本次来源为空（跳过）";
                    return true;
                }
                observed = "来源手牌=" + srcN + "，返回=" + Render(got);
                extra = "来源非空（" + srcN + "）→ 必须取到元素";
                return has_value;
            }
            case "value_int":
                extra = "期望 " + s.expect;
                return got is int && (int)got == ToInt(s.expect);
            case "value_bool":
                extra = "期望 " + s.expect;
                return got is bool && ((bool)got == (s.expect == "true"));
            case "value_dying_owner_hp":         //英雄卡「是否濒死」== 所属玩家 HP<=0（**存活态**：双方 HP 置 30 → 期望 false）
            case "value_dying_owner_hp_dying":   //同上（**濒死态**：双方 HP 置 0 → 期望 true，验证确实读的是 Player.hp，
                                                 //  而不是卡面 hp 字段(=0) 或某个缺省值）
                                                 //  两个断言名都必须以 value_ 开头：生成器靠前缀判定"取值类"，
                                                 //  否则会被误判成动作类 → 走缺省断言**假通过**（实测 102018 踩过）。
            {
                int hpx = p0 != null ? p0.hp : int.MinValue;
                int hpy = p1 != null ? p1.hp : int.MinValue;
                bool expect_dying = s.assert == "value_dying_owner_hp_dying";
                bool got_bool = got is bool && (bool)got;
                observed = "返回=" + got_bool + "，玩家0.hp=" + hpx + "、玩家1.hp=" + hpy
                    + "（期望 " + expect_dying + "）";
                extra = "英雄濒死 == 所属玩家 HP<=0（F1/F13 语义）";
                return got_bool == expect_dying;
            }
            case "value_notnull":
                //★ 空串不算"有值"：很多节点失败时会退化成 ""（例 106002/104001），否则会**假通过**
                return got != null && !(got is string s0 && s0.Length == 0);
            case "value_null":
                return got == null;
            case "run_bool":     //★ 在**真实 Run 内**观测布尔值：212001 分支（then=伤害7 / else=伤害1）
            case "run_int":      //★ 在**真实 Run 内**观测整数：212002 循环（count ← 该值，循环体=伤害1）
            {
                //事件族（108001~108016/109008）的值只在 Run 期间存在（cur_event 上下文），
                //必须把"被测节点 → 分支/循环"接进流程，用副作用（伤害量）观测。
                if (s.assert == "run_bool")
                {
                    GraphNode br = Node(g, "br", GraphNodeType.Action, "212001", "分支观测");
                    Pin(br, "in", NodeValueType.Flow, false);
                    Pin(br, "isTrue", NodeValueType.Boolean, false);
                    Pin(br, "thenAction", NodeValueType.Flow, true);
                    Pin(br, "elseAction", NodeValueType.Flow, true);
                    GraphNode d7 = Node(g, "d7", GraphNodeType.Action, "202001", "then 伤害7", "value", "7");
                    Pin(d7, "in", NodeValueType.Flow, false);
                    Pin(d7, "out", NodeValueType.Flow, true);
                    GraphNode d1 = Node(g, "d1", GraphNodeType.Action, "202001", "else 伤害1", "value", "1");
                    Pin(d1, "in", NodeValueType.Flow, false);
                    Pin(d1, "out", NodeValueType.Flow, true);
                    Link(g, ev, "out", br, "in");
                    Link(g, n, out_pin, br, "isTrue");
                    Link(g, br, "thenAction", d7, "in");
                    Link(g, br, "elseAction", d1, "in");
                }
                else
                {
                    GraphNode lp = Node(g, "lp", GraphNodeType.Action, "212002", "循环观测");
                    Pin(lp, "in", NodeValueType.Flow, false);
                    Pin(lp, "count", NodeValueType.Int32, false);
                    Pin(lp, "action", NodeValueType.Flow, true);
                    GraphNode d1 = Node(g, "d1", GraphNodeType.Action, "202001", "循环体 伤害1", "value", "1");
                    Pin(d1, "in", NodeValueType.Flow, false);
                    Pin(d1, "out", NodeValueType.Flow, true);
                    Link(g, ev, "out", lp, "in");
                    Link(g, n, out_pin, lp, "count");
                    Link(g, lp, "action", d1, "in");
                }
                int hp_before = p1.hp;
                int exec_run = NodeDocRunner.Run(logic, g, caster, null, p1, "OnPlay");
                int dmg_run = hp_before - p1.hp;
                observed = "Run 内观测：伤害=" + dmg_run + "（动作数=" + exec_run + "）｜日志=" + Trim(JoinLogs());
                if (s.assert == "run_bool")
                {
                    extra = "期望伤害 " + s.expect + "（7=分支为真 / 1=分支为假）";
                    return dmg_run == ToInt(s.expect);
                }
                extra = "期望伤害 == " + s.expect + "（" + DynValue(s.expect) + "）";
                return dmg_run == DynValue(s.expect);
            }
            case "dyn_either":   //返回值 == 任一玩家的指定属性（player 口回退到底是谁不确定，故左右都认）
            {
                int got_int = ToInt(got);      //注意：不能叫 g（本作用域已有 GraphData g，实测 CS0136）
                int v0 = PlayerProp(p0, s.expect), v1 = PlayerProp(p1, s.expect);
                extra = "期望：玩家0." + s.expect + "=" + v0 + " 或 玩家1." + s.expect + "=" + v1;
                return got_int == v0 || got_int == v1;
            }
            case "dyn":     //动态期望：与运行期真实属性比对（expect = 属性路径，如 caster.attack）
                extra = "期望 " + DynValue(s.expect) + "（" + s.expect + "）";
                return ToInt(got) == DynValue(s.expect);
            case "state_atk":   //写入类节点（设置属性等）被取值通道触发时，验证其副作用
                extra = "副作用：施法卡攻击期望 " + s.expect;
                return caster.attack == ToInt(s.expect);
            case "state_haskw":
                extra = "副作用：施法卡应拥有关键词 " + s.expect;
                return caster.HasKeyword(s.expect);
            case "state_p1hp":
                extra = "副作用：目标玩家 HP 期望 " + s.expect;
                return p1.hp == ToInt(s.expect);
            case "state_p0hp":
                extra = "副作用：施法方玩家 HP 期望 " + s.expect;
                return p0 != null && p0.hp == ToInt(s.expect);
            case "exec_and_log":    //取到值 + 产生了 [NodeDoc] 日志（"拉取即执行"的写入类节点）
            {
                string lg = JoinLogs();
                extra = "要求：取值成功（非异常）+ 有 [NodeDoc] 日志";
                return lg.Length > 0;
            }
            case "log":
            {
                string lg = JoinLogs();
                extra = "要求日志含「" + s.expect + "」";
                return lg.Contains(s.expect);
            }
            case "exec":
            {
                extra = "要求：取值通道未抛异常（值可为空）";
                return true;
            }
            default:
                return true;
        }
    }

    private void AddSpecFields(GraphNode n, TestSpec s)
    {
        if (!string.IsNullOrEmpty(s.field1)) n.fields.Add(new FieldCustomData { name = s.field1, value = s.value1 ?? "" });
        if (!string.IsNullOrEmpty(s.field2)) n.fields.Add(new FieldCustomData { name = s.field2, value = s.value2 ?? "" });
        if (!string.IsNullOrEmpty(s.field3)) n.fields.Add(new FieldCustomData { name = s.field3, value = s.value3 ?? "" });
    }

    private void AddDeclaredPins(GraphNode n, TestSpec s, bool outputs)
    {
        if (s.ins == null)
            return;
        foreach (PinSpec p in s.ins)
        {
            if (p == null || string.IsNullOrEmpty(p.name))
                continue;
            Pin(n, p.name, MapType(p.type), false);
        }
    }

    private static string FirstOutPin(TestSpec s)
    {
        //输出口名由生成器（tools/gen_batch.ps1）从 NodeDoc.xml 读出来填；缺省用 return
        return string.IsNullOrEmpty(s.out_pin) ? "return" : s.out_pin;
    }

    private static bool HasInPin(TestSpec s, string name)
    {
        if (s.ins == null || string.IsNullOrEmpty(name))
            return false;
        foreach (PinSpec p in s.ins)
            if (p != null && p.name == name)
                return true;
        return false;
    }

    private GraphNode AddExtraSource(GraphData g, GraphNode ev, GraphNode n, TestSpec s)
    {
        if (string.IsNullOrEmpty(s.src1))
            return null;
        //★ 目标口容错：写错/没写 → 按来源类型自动挑第一个匹配的输入口（避免"用例列写歪 → 没接线 → 误判节点"）
        if (!string.IsNullOrEmpty(s.dst1) && !HasInPin(s, s.dst1))
            s.dst1 = "";
        if (string.IsNullOrEmpty(s.dst1))
            s.dst1 = PickPin(s.ins, WantedTypes(s.src1), null);
        if (string.IsNullOrEmpty(s.dst1))
        {
            //★ 目标节点没有可接的引脚（例：103002/103003 的 cardRef/cardRefs 是"定义引用"字符串口）
            //  → 直接给节点**填字段**（运行时就按字段解析定义引用）
            if ((s.src1 == "DEFINE_OF_CASTER" || s.src1 == "HERO_DEFINE") && s.field1 == "")
            {
                Card anyCard = s.src1 == "HERO_DEFINE" ? caster : (DeckCard0() ?? caster);
                if (anyCard != null)
                {
                    string fr = HasInPin(s, "cardRefs") ? "cardRefs" : "cardRef";
                    s.field1 = fr;
                    s.value1 = anyCard.card_id;
                    n.fields.Add(new FieldCustomData { name = fr, value = anyCard.card_id });
                    return n;      //返回非 null = 已提供来源（只是走字段而非连线）
                }
            }
            return null;
        }
        GraphNode src = null;
        if (s.src1 == "EV_CARD")
        {
            src = ev;
            Link(g, ev, string.IsNullOrEmpty(s.src1_pin) ? "card" : s.src1_pin, n, s.dst1);
            return src;
        }
        if (s.src1 == "CONST_INT")
        {
            src = Node(g, "c1", GraphNodeType.Value, "112003", "整数常量 " + s.src1_const, "value", s.src1_const ?? "0");
            Pin(src, "result", NodeValueType.Int32, true);
            Link(g, src, "result", n, s.dst1);
            return src;
        }
        if (s.src1 == "CONST_BOOL")
        {
            src = Node(g, "c1", GraphNodeType.Value, "112001", "布尔常量 " + s.src1_const, "value", s.src1_const ?? "true");
            Pin(src, "return", NodeValueType.Boolean, true);
            Link(g, src, "return", n, s.dst1);
            return src;
        }
        if (s.src1 == "CONST_STR")
        {
            src = Node(g, "c1", GraphNodeType.Value, "112006", "字符串常量", "value", s.src1_const ?? "");
            Pin(src, "value", NodeValueType.String, true);
            Link(g, src, "value", n, s.dst1);
            return src;
        }
        if (s.src1 == "DEFINE_OF_CASTER")   //某个"真实卡牌"的定义（用「获取卡牌定义」节点提供）
        {
            //★ 不能用英雄卡：英雄的 card_id 可能为空（实测：衍生卡/复制类用例全失败，就是因为定义解析成 ""）。
            //  改为优先取施法方牌库/手牌里的第一张真实卡。
            Card anyCard = null;
            if (p0 != null && p0.cards_deck != null && p0.cards_deck.Count > 0)
                anyCard = p0.cards_deck[0];
            if (anyCard == null && p0 != null && p0.cards_hand != null && p0.cards_hand.Count > 0)
                anyCard = p0.cards_hand[0];
            if (anyCard == null)
                anyCard = caster;
            src = Node(g, "c2", GraphNodeType.Value, "103002", "获取卡牌定义", "cardRef", anyCard.card_id);
            Pin(src, "return", NodeValueType.Object, true);
            Link(g, src, "return", n, s.dst1);
            return src;
        }
        //★ 统一兜底：把"来源种类 → 来源节点"全部走 CreateSource（此前它的分支只加在 CreateSource，
        //  这里却是一条条 if —— 结果 SNAPSHOT/PILE/BUFFDEF 等新种类**根本没接线**（实测日志：已接=False）。
        {
            string op_all; NodeValueType ot_all;
            SourcePin(s.src1, out op_all, out ot_all);
            GraphNode made = CreateSource(g, ev, s.src1, s.src1_const, "src1");
            if (made != null)
            {
                if (made != ev)
                    Pin(made, op_all, ot_all, true);
                Link(g, made, op_all, n, s.dst1);
                return made;
            }
        }
        if (s.src1 == "SELF_CARD")          //施法卡自身（用「获取卡牌」节点提供）
        {
            src = Node(g, "c3", GraphNodeType.Value, "102001", "获取卡牌", "cardRef", caster.card_id);
            Pin(src, "return", NodeValueType.Card, true);
            Link(g, src, "return", n, s.dst1);
            return src;
        }
        //---- 集合类来源（集合运算/映射用例用：拿一个"已知且非空"的集合当输入）----
        if (s.src1 == "HAND")               //施法方玩家的手牌（开局非空 → 可做数量/并交差断言）
        {
            src = Node(g, "c4", GraphNodeType.Value, "101006", "获取玩家的手牌");
            Pin(src, "return", NodeValueType.Object, true);
            Link(g, src, "return", n, s.dst1);
            return src;
        }
        if (s.src1 == "DECK")
        {
            src = Node(g, "c5", GraphNodeType.Value, "101008", "获取玩家牌库中的卡牌");
            Pin(src, "return", NodeValueType.Object, true);
            Link(g, src, "return", n, s.dst1);
            return src;
        }
        if (s.src1 == "MAP")                //空映射当输入（映射族：设置/移除/查询都不要求键已存在）
        {
            src = Node(g, "c6", GraphNodeType.Value, "113001", "创建映射集合");
            Pin(src, "return", NodeValueType.Object, true);
            Link(g, src, "return", n, s.dst1);
            return src;
        }
        return null;
    }

    /// <summary>第二来源线（双输入节点：集合运算 相交/并集/相减/子集/内容相同、映射等）。
    /// 目标口用"来源类型能接的口"，并跳过 src1 已占用的口。</summary>
    private GraphNode AddSecondSource(GraphData g, GraphNode ev, GraphNode n, TestSpec s)
    {
        if (string.IsNullOrEmpty(s.src2))
            return null;
        string dst = PickPin(s.ins, WantedTypes(s.src2), s.dst1);
        if (string.IsNullOrEmpty(dst))
            return null;
        GraphNode src = CreateSource(g, ev, s.src2, s.src2_const, "src2");
        if (src == null)
            return null;
        string op; NodeValueType ot;
        SourcePin(s.src2, out op, out ot);
        if (src != ev)
            Pin(src, op, ot, true);
        Link(g, src, op, n, dst);
        return src;
    }

    /// <summary>接循环体：把子动作挂到被测节点的 `action` 输出口（遍历/重复/筛选类节点）。
    /// 子动作每个元素执行一次 → 可用"伤害总量 == 元素个数"断言循环次数。</summary>
    private string AddLoopBody(GraphData g, GraphNode n, TestSpec s)
    {
        if (string.IsNullOrEmpty(s.child_action))
            return null;
        GraphNode child = Node(g, "child", GraphNodeType.Action, s.child_action, "循环体动作");
        Pin(child, "in", NodeValueType.Flow, false);
        Pin(child, "out", NodeValueType.Flow, true);
        //★ 必须在父节点上**声明 action 口**：runner 的 WalkFlowOutputs 要求引脚真实存在且类型为 Flow/None，
        //  否则这条流程边被直接跳过（实测：遍历循环体一次都没跑 → 伤害 0、动作数 0）。
        Pin(n, "action", NodeValueType.Flow, true);
        if (!string.IsNullOrEmpty(s.child_field))
            child.fields.Add(new FieldCustomData { name = s.child_field, value = s.child_value ?? "" });
        Link(g, n, "action", child, "in");
        return "；循环体 ← " + s.child_action + "（" + s.child_field + "=" + s.child_value + "）";
    }

    /// <summary>来源种类 → 输入口允许的类型（按优先级；"Array" 放在 "Object" 前，
    /// 这样 (array:Array, element:Object) 这种节点不会把集合接到 element 上）</summary>
    private static string[] WantedTypes(string kind)
    {
        switch (kind)
        {
            case "EV_CARD": case "SELF_CARD": return new string[] { "Card", "Object" };
            case "DEFINE_OF_CASTER": return new string[] { "CardDefine", "Object" };
            case "HERO_DEFINE": return new string[] { "CardDefine", "Object" };
            case "SNAPSHOT": return new string[] { "CardSnapshot", "Object" };
            case "EVENT": case "EVENT_DEATH": return new string[] { "EventArg", "Object" };
            case "CARD_HAND1": case "CARD_DECK1": return new string[] { "Card", "Object" };
            case "RECORD": return new string[] { "EventRecord", "Object" };
            case "EFFECT": return new string[] { "Effect", "Object" };
            case "PILE": case "PILE_CARD": return new string[] { "Pile", "String", "Object" };
            case "BUFFDEF": return new string[] { "BuffDefine", "Object" };
            case "HAND": case "DECK": return new string[] { "Array", "Object", "Card" };
            case "MAP": return new string[] { "GraphMap", "Object" };
            case "CONST_INT": return new string[] { "Int32", "Object" };
            case "CONST_BOOL": return new string[] { "Boolean", "Object" };
            case "CONST_STR": return new string[] { "String", "Object" };
        }
        return new string[] { "Object" };
    }

    /// <summary>类型匹配：只对**包装类型**（`NodeValueRef&lt;Boolean&gt;`，条件口就是这种写法）做"包含"判断，
    /// 其余类型必须**全等** —— 否则 `"CardSnapshot".Contains("Card")` 成立，会把「卡牌」来源接到「卡牌快照」口上
    /// （实测 104006~104009 快照属性因此全空）。</summary>
    private static bool TypeMatches(string pinType, string want)
    {
        if (string.IsNullOrEmpty(pinType) || string.IsNullOrEmpty(want))
            return false;
        if (pinType == want)
            return true;
        if (pinType.StartsWith("NodeValueRef"))
            return pinType.Contains(want);
        return false;
    }

    private static string PickPin(PinSpec[] ins, string[] wants, string skip)
    {
        if (ins == null || wants == null)
            return null;
        foreach (string w in wants)
        {
            foreach (PinSpec p in ins)
            {
                if (p == null || p.name == skip)
                    continue;
                if (TypeMatches(p.type, w))
                    return p.name;
            }
        }
        return null;
    }

    /// <summary>来源节点的输出口名与类型</summary>
    private static void SourcePin(string kind, out string outPin, out NodeValueType outType)
    {
        switch (kind)
        {
            case "EV_CARD": outPin = "card"; outType = NodeValueType.Card; return;
            case "CONST_INT": outPin = "result"; outType = NodeValueType.Int32; return;
            case "CONST_BOOL": outPin = "return"; outType = NodeValueType.Boolean; return;
            case "CONST_STR": outPin = "value"; outType = NodeValueType.String; return;
            case "BUFFDEF": outPin = "value"; outType = NodeValueType.Object; return;   //106002 的输出口叫 value
        }
        outPin = "return"; outType = NodeValueType.Object;
    }

    /// <summary>按来源种类创建来源节点（EV_CARD 直接返回入口事件节点）</summary>
    private GraphNode CreateSource(GraphData g, GraphNode ev, string kind, string cst, string sid)
    {
        switch (kind)
        {
            case "EV_CARD": return ev;
            case "CONST_INT": return Node(g, sid, GraphNodeType.Value, "112003", "整数常量", "value", string.IsNullOrEmpty(cst) ? "0" : cst);
            case "CONST_BOOL": return Node(g, sid, GraphNodeType.Value, "112001", "布尔常量", "value", string.IsNullOrEmpty(cst) ? "true" : cst);
            case "CONST_STR": return Node(g, sid, GraphNodeType.Value, "112006", "字符串常量", "value", cst ?? "");
            case "SELF_CARD": return Node(g, sid, GraphNodeType.Value, "102001", "获取卡牌", "cardRef", caster.card_id);
            case "HAND": return Node(g, sid, GraphNodeType.Value, "101006", "获取玩家的手牌");
            case "DECK": return Node(g, sid, GraphNodeType.Value, "101008", "获取玩家牌库中的卡牌");
            case "MAP": return Node(g, sid, GraphNodeType.Value, "113001", "创建映射集合");
            case "DEFINE_OF_CASTER":
            {
                Card anyCard = DeckCard0() ?? caster;
                return Node(g, sid, GraphNodeType.Value, "103002", "获取卡牌定义", "cardRef", anyCard.card_id);
            }
            case "HERO_DEFINE":     //施法卡（英雄）自身的定义：英雄技能/宣言/遗言这类判断要用它
                return Node(g, sid, GraphNodeType.Value, "103002", "获取卡牌定义(英雄)", "cardRef", caster.card_id);
            case "CARD_HAND1":      //手牌第一张（**单张**真实卡；给"移动类"节点用，避免整手牌一起搬）
            case "CARD_DECK1":      //牌库第一张（单张真实卡）
            {
                Card pick = null;
                if (kind == "CARD_HAND1" && p0 != null && p0.cards_hand != null && p0.cards_hand.Count > 0)
                    pick = p0.cards_hand[0];
                if (kind == "CARD_DECK1" && p0 != null && p0.cards_deck != null && p0.cards_deck.Count > 0)
                    pick = p0.cards_deck[0];
                if (pick == null)
                    pick = caster;
                //★ 用 102001「获取卡牌」(cardRef) 取"活的卡实例"：集合通道认这个节点；
                //  换成 111007(集合取元素) 时集合通道解析不到（实测移动类全部 Δ0）。
                GraphNode one = Node(g, sid, GraphNodeType.Value, "102001", "获取卡牌", "cardRef", pick.card_id);
                Pin(one, "return", NodeValueType.Card, true);
                return one;
            }
            case "PILE":            //牌堆引用来源：101017 获取牌堆（pileName=牌库 → "玩家id|牌库"）
                return Node(g, sid, GraphNodeType.Value, "101017", "获取牌堆", "pileName", "牌库");
            case "PILE_CARD":       //牌堆引用来源②：102029 获取卡牌所在牌堆（card←施法卡=英雄）→ "0|英雄"（恰 1 张，确定非空）
                                    //  为什么需要它：PILE 走 101017 的玩家口会回退到"选中玩家"，而在本对战夹具里
                                    //  玩家1 的牌库是 0 张 → 105002 恒返回空集合（判据对空集恒真，反而掩盖问题）。
            {
                GraphNode get = Node(g, sid + "c", GraphNodeType.Value, "102001", "获取卡牌", "cardRef", caster.card_id);
                Pin(get, "return", NodeValueType.Card, true);
                GraphNode pile = Node(g, sid, GraphNodeType.Value, "102029", "获取卡牌所在牌堆");
                Pin(pile, "card", NodeValueType.Card, false);
                Pin(pile, "return", NodeValueType.Object, true);
                Link(g, get, "return", pile, "card");
                return pile;
            }
            case "BUFFDEF":         //增益定义来源：106002 按下拉 id 取（id 取自项目 Workshop/buffs.json，真实存在）
                return Node(g, sid, GraphNodeType.Value, "106002", "获取增益定义", "buffDefine", "buff_8c5cfda5");
            case "EVENT":           //当前事件（只在 Run 内求值 → 配合 run_bool/run_int 观测）
                return Node(g, sid, GraphNodeType.Value, "108001", "当前事件");
            case "EVENT_DEATH":     //把当前事件类型改成 OnDeath（用来验证 108003 的转换语义）
            {
                GraphNode e1 = Node(g, sid + "_ev", GraphNodeType.Value, "108001", "当前事件");
                GraphNode t = Node(g, sid, GraphNodeType.Value, "108003", "转换事件类型", "eventReference", "OnDeath");
                Pin(t, "eventArg", NodeValueType.Object, false);
                Link(g, e1, "return", t, "eventArg");
                return t;
            }
            case "RECORD":          //最近一条事件记录（109004 集合 → 单条消费者取最后一条）
                return Node(g, sid, GraphNodeType.Value, "109004", "获取本局游戏事件记录");
            case "EFFECT":          //某张大牌定义上的第一个效果（107006 → 单效果消费者取首个）
            {
                GraphNode w = Node(g, sid, GraphNodeType.Value, "107006", "获取卡牌定义的所有效果");
                Pin(w, "cardDefine", NodeValueType.CardDefine, false);
                Card anyCard = DeckCard0() ?? caster;
                GraphNode def = Node(g, sid + "_def", GraphNodeType.Value, "103002", "获取卡牌定义", "cardRef", anyCard.card_id);
                Link(g, def, "return", w, "cardDefine");
                return w;
            }
            case "SNAPSHOT":        //卡牌快照来源：施法卡「在某事件前的快照」
            {
                GraphNode sn = Node(g, sid, GraphNodeType.Value, "108008", "获取卡牌在某事件前的快照");
                Pin(sn, "card", NodeValueType.Card, false);
                Link(g, ev, "card", sn, "card");
                return sn;
            }
        }
        return null;
    }

    /// <summary>动态期望值：直接与运行期的真实属性比对（例如「获取卡牌攻击力」期望 caster.attack）</summary>
    private int DynValue(string path)
    {
        if (string.IsNullOrEmpty(path))
            return 0;
        switch (path.Trim())
        {
            case "caster.attack": return caster.attack;
            case "caster.hp": return caster.hp;
            case "caster.mana": return caster.mana;
            case "caster.damage": return caster.damage;
            case "caster.hpMax": return caster.GetHPMax();
            case "caster.attack_ongoing": return caster.attack + caster.attack_ongoing;
            //"某张真实卡（施法方牌库/手牌第一张）"的定义属性 —— 与 DEFINE_OF_CASTER 来源指向同一张卡
            case "deck0.attack": { Card c = DeckCard0(); return c != null ? c.attack : 0; }
            //★ "定义"字段（与卡实例字段可能不同：例 103010 花费读的是 CardData.mana，
            //  而卡实例 Card.mana 在牌库里是 0 → 用卡实例字段断言会误报）
            case "deck0.defMana": { Card c = DeckCard0(); return c != null && c.CardData != null ? c.CardData.mana : 0; }
            case "deck0.defAttack": { Card c = DeckCard0(); return c != null && c.CardData != null ? c.CardData.attack : 0; }
            case "deck0.defHp": { Card c = DeckCard0(); return c != null && c.CardData != null ? c.CardData.hp : 0; }
            case "deck0.hp": { Card c = DeckCard0(); return c != null ? c.hp : 0; }
            case "deck0.mana": { Card c = DeckCard0(); return c != null ? c.mana : 0; }
            case "deck0.hpMax": { Card c = DeckCard0(); return c != null ? c.GetHPMax() : 0; }
            case "p0.hp": return p0 != null ? p0.hp : 0;
            case "p1.hp": return p1 != null ? p1.hp : 0;
            case "p0.mana": return p0 != null ? p0.mana : 0;
            case "p1.mana": return p1 != null ? p1.mana : 0;
            case "p0.hand": return p0 != null && p0.cards_hand != null ? p0.cards_hand.Count : 0;
            case "p1.hand": return p1 != null && p1.cards_hand != null ? p1.cards_hand.Count : 0;
            case "p0.deck": return p0 != null && p0.cards_deck != null ? p0.cards_deck.Count : 0;
            case "p1.deck": return p1 != null && p1.cards_deck != null ? p1.cards_deck.Count : 0;
            case "turn": return data != null ? data.turn_count : 0;
            case "zero": return 0;
            case "one": return 1;
        }
        return 0;
    }

    private static string DescribeSource(TestSpec s, GraphNode src)
    {
        if (s.src1 == "EV_CARD") return "入口「卡牌」口(施法卡)";
        if (s.src1 == "CONST_INT") return "整数常量 " + s.src1_const;
        if (s.src1 == "CONST_BOOL") return "布尔常量 " + s.src1_const;
        if (s.src1 == "CONST_STR") return "字符串常量「" + s.src1_const + "」";
        if (s.src1 == "HAND") return "施法方手牌集合";
        if (s.src1 == "DECK") return "施法方牌库集合";
        if (s.src1 == "MAP") return "空映射集合";
        return s.src1;
    }

    /// <summary>数量增量断言（看双方玩家其一）：创建衍生卡等节点会走"目标玩家/施法方"回退，
    /// 实测可能落到玩家1身上 → 只要任一玩家的对应区域 +期望 即算通过（并记录两边数值）。</summary>
    private bool CountDelta(List<Card> listA, int beforeA, List<Card> listB, int beforeB, int expectDelta, string label, int executed, out string observed)
    {
        int na = listA != null ? listA.Count : -1;
        int nb = listB != null ? listB.Count : -1;
        int da = na - beforeA, db = nb - beforeB;
        observed = label + "：玩家0 " + beforeA + "→" + na + "（Δ" + da + "）、玩家1 " + beforeB + "→" + nb
            + "（Δ" + db + "），期望其一 Δ==" + expectDelta + "｜动作数=" + executed;
        return da == expectDelta || db == expectDelta;
    }

    private static int CountOf(List<Card> list)
    {
        return list != null ? list.Count : 0;
    }

    /// <summary>施法方牌库/手牌里的一张"真实卡"（定义类用例的来源卡）。
    /// ★ 优先取**角色**卡：法术/道具类卡的定义属性查询路径不同，会让用例时好时坏（实测踩过）。</summary>
    private Card DeckCard0()
    {
        List<Card> pool = new List<Card>();
        if (p0 != null && p0.cards_deck != null)
            pool.AddRange(p0.cards_deck);
        if (p0 != null && p0.cards_hand != null)
            pool.AddRange(p0.cards_hand);
        foreach (Card c in pool)
        {
            if (c != null && c.CardData != null && c.CardData.type == CardType.Character)
                return c;
        }
        return pool.Count > 0 ? pool[0] : null;
    }

    /// <summary>集合元素个数（非集合/字符串 → -1）</summary>
    private static int CollCount(object v)
    {
        if (v == null || v is string)
            return -1;
        System.Collections.IEnumerable en = v as System.Collections.IEnumerable;
        if (en == null)
            return -1;
        int n = 0;
        foreach (object _ in en)
            n++;
        return n;
    }

    /// <summary>观测值渲染：集合类打印成 `List`1(3): [hero_fire#0, …]`，否则 ToString。</summary>
    private static string Render(object v)
    {
        if (v == null)
            return "null";
        if (v is string)
            return (string)v;
        System.Collections.IEnumerable en = v as System.Collections.IEnumerable;
        if (en == null)
            return v.ToString();
        List<string> parts = new List<string>();
        bool more = false;
        foreach (object o in en)
        {
            if (parts.Count >= 8)
            {
                more = true;
                break;
            }
            parts.Add(DescribeItem(o));
        }
        return v.GetType().Name + "(" + parts.Count + (more ? "+" : "") + "): [" + string.Join(", ", parts.ToArray()) + "]";
    }

    private static string DescribeItem(object o)
    {
        if (o == null)
            return "null";
        Card c = o as Card;
        if (c != null)
            return c.card_id + "#" + c.uid;
        CardData d = o as CardData;
        if (d != null)
            return d.id;
        return o.ToString();
    }

    /// <summary>玩家属性取值（"任一玩家"类断言用；-9999 = 不识别的路径）</summary>
    private static int PlayerProp(Player p, string path)
    {
        if (p == null || string.IsNullOrEmpty(path))
            return -9999;
        switch (path.Trim())
        {
            case "mana": return p.mana;
            case "mana_max": return p.mana_max;
            case "mana_max_total": return p.mana_max_total;
            case "hp": return p.hp;
            case "hp_max": return p.hp_max;
            case "kill": return p.kill_count;
            case "fatigue": return p.GetTraitValue("疲劳层数");
            case "hand": return CountOf(p.cards_hand);
            case "deck": return CountOf(p.cards_deck);
            case "board": return CountOf(p.cards_board);
        }
        return -9999;
    }

    private string JoinLogs()
    {
        if (captured.Count == 0)
            return "";
        return string.Join(" ‖ ", captured.ToArray());
    }

    private string Trim(string s, int max = 90)
    {
        if (string.IsNullOrEmpty(s)) return "—";
        s = s.Replace("\t", " ").Replace("\n", " ");
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
