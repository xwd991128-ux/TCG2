using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【语义断言探针】批量测试台的自动层 289/312 条只是"不崩/返回非空"（冒烟级），证明不了节点语义。
    /// 这个探针给**关键节点**上硬断言：把待测节点接进「202001 造成伤害」的 value 口，
    /// 用**伤害数字**当观测量（目标玩家血量是精确可测的），于是"取值对不对"变成可失败断言。
    /// 覆盖：
    ///   102006 获取攻击 / 102005 获取费用 / 102007 最大生命 / 102008 当前生命（★英雄卡=玩家血）
    ///   102013 获取己方手牌 / 112004 整数运算(-, /, *) / 202001 造成伤害（对照）
    ///   202012 分配伤害（总量必须 == damage；含"目标被打死后剩余点数不丢"）
    /// 触发：建 tools/semantic_flag.txt → 进 Play → 写 tools/semantic_result.tsv → 自动删标记。
    /// </summary>
    public class SemanticProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/semantic_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/semantic_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("SemanticProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<SemanticProbe>();
        }

        private int frames, pass, fail;
        private bool done;

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath))
                {
                    done = false; frames = 0; pass = 0; fail = 0;
                    Debug.Log("[语义断言] 检测到标志 → 重跑");
                }
                return;
            }
            frames++;
            if (frames < 120)
                return;
            done = true;
            var sb = new StringBuilder();
            sb.AppendLine("结果\t用例\t观测\t期望");
            try { Run(sb); }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION\t整体\t" + e.Message + "\t");
                Debug.LogError("[语义断言] 异常: " + e);
            }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[语义断言] 完成：通过 " + pass + "｜失败 " + fail + " → " + OutPath);
        }

        private void Check(StringBuilder sb, string name, bool ok, string observed, string expect)
        {
            if (ok) pass++; else fail++;
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + observed + "\t" + expect);
        }

        // ---------------- 建图辅助（与批量测试台同一套 API） ----------------
        private static GraphData NewGraph(string name)
        {
            return new GraphData { name = name, nodes = new List<GraphNode>(), links = new List<GraphLink>() };
        }

        private static GraphNode Node(GraphData g, string id, GraphNodeType type, string action, string title, params string[] fp)
        {
            var n = new GraphNode();
            n.id = id; n.type = type; n.action = action; n.title = title;
            //★category 必须按 NodeDoc 填：引擎按 category 分发（空 category 的节点会被整段跳过 → 伤害/取值全 0，实测）
            foreach (NodeDocDef d in NodeDocDb.All)
            {
                if (d != null && d.define_id == action)
                {
                    n.category = string.IsNullOrEmpty(d.category) ? "其他" : d.category;
                    break;
                }
            }
            if (string.IsNullOrEmpty(n.category))
                n.category = "其他";
            n.pins = new List<GraphPin>();
            n.fields = new List<FieldCustomData>();
            for (int i = 0; i + 1 < fp.Length; i += 2)
                n.fields.Add(new FieldCustomData { name = fp[i], value = fp[i + 1] });
            g.nodes.Add(n);
            return n;
        }

        private static void Pin(GraphNode n, string name, NodeValueType type, bool output)
        {
            n.pins.Add(new GraphPin { id = n.id + "_" + name, name = name, display_name = name, is_output = output, type = type });
        }

        private static void Link(GraphData g, GraphNode from, string fromPin, GraphNode to, string toPin)
        {
            g.links.Add(new GraphLink
            {
                from_node = from.id, from_pin = from.id + "_" + fromPin,
                to_node = to.id, to_pin = to.id + "_" + toPin
            });
        }

        /// <summary>入口（主动效果入口）：动作 / 玩家 / 卡牌 三个输出口</summary>
        private static GraphNode Entry(GraphData g)
        {
            GraphNode ev = Node(g, "ev", GraphNodeType.Event, "ActivateEffect", "主动效果入口");
            Pin(ev, "out", NodeValueType.Flow, true);
            Pin(ev, "player", NodeValueType.Player, true);
            Pin(ev, "card", NodeValueType.Card, true);
            return ev;
        }

        private static CardData FirstCard(CardType type)
        {
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == type && !string.IsNullOrEmpty(c.id))
                    return c;
            return null;
        }

        private static CardData Minion()
        {
            return FirstCard(CardType.Character);
        }

        /// <summary>找一张费用 ≥ min 的角色卡（费用有 0 下限，"降 2"要用足够贵的卡才验得出来）</summary>
        private static CardData FirstMinionWithMana(int min)
        {
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Character && c.mana >= min && !string.IsNullOrEmpty(c.id))
                    return c;
            return null;
        }

        // ---------------- 用例 ----------------
        private void Run(StringBuilder sb)
        {
            CardData heroData = FirstCard(CardType.Hero);
            CardData minionData = Minion();
            if (heroData == null || minionData == null)
            {
                sb.AppendLine("SKIP\t夹具\t卡池缺少英雄/随从定义\t");
                return;
            }

            CaseInt(sb, "102001 对照（常量 3）", null, null, 3, null, null, 3);
            CaseAttack(sb, minionData);
            CaseMana(sb, minionData);
            CaseHeroHp(sb, heroData, true);
            CaseHeroHp(sb, heroData, false);
            CaseIntOp(sb, "-", 7, 2, 5);
            CaseIntOp(sb, "/", 7, 2, 3);
            CaseIntOp(sb, "*", 7, 2, 14);
            CaseAllCharactersToAssign(sb, minionData);
            CaseAssignDamage(sb, minionData, 8, false);
            CaseAssignDamage(sb, minionData, 8, true);   // 随从都只有 1 血 → 大量死亡 → 检验"剩余点数不丢"

            //---- 第二批：条件/分支族、属性写入族、灵力族、技能族 ----
            CaseCompare(sb, minionData, ">", 3, 5, false);      //3>5 假 → else 分支
            CaseCompare(sb, minionData, "小于", 3, 5, true);     //中文"小于" → 真 → then 分支
            CaseCompare(sb, minionData, "等于", 5, 5, true);     //中文"等于"
            CaseCompare(sb, minionData, "≠", 5, 5, false);       //符号"≠"
            CaseCompare(sb, minionData, "≥", 5, 5, true);        //符号"≥"
            CaseCompare(sb, minionData, "≤", 5, 3, false);       //符号"≤"
            CaseSetCardProp(sb, minionData);
            CaseSetCardPropHp(sb, minionData);
            CaseSetMana(sb, 10, 5, 5);       //上限 10：设为 5 → 5
            CaseSetMana(sb, 3, 5, 3);        //上限 3：设为 5 → 钳到 3
            CasePlayerProp(sb, minionData);
            CaseAddAbility(sb, minionData);

            //---- 第三批：增益族（含"费用降低"链路的增益侧）----
            CaseAddBuff(sb, minionData, false);
            CaseAddBuff(sb, FirstMinionWithMana(3) ?? minionData, true);   //费用≥3 的卡：才能验"确实降 2"（费用有 0 下限）

            //---- 第四批：移动 / 治疗 / 消灭 / 护甲 / 复制 / 变形 / 关键词 ----
            CaseDraw(sb, minionData);
            CaseDiscard(sb, minionData);
            CaseHeal(sb, minionData);
            CaseDestroy(sb, minionData);
            CaseArmorAddLose(sb, minionData);
            CaseCopy(sb, minionData);
            CaseTransform(sb, minionData);
            CaseKeywordCheck(sb, minionData, true);
            CaseKeywordCheck(sb, minionData, false);

            //---- 第五批：老写法兼容（卡池里真实存在的接线风格）----
            CaseIntOpSlots(sb, "-", 7, 2, 5);      //操作数走 arg / arg2 编号槽（"熔岩人"就是这种写法）
            CaseIntOpSlots(sb, "+", 7, 2, 9);
            CaseAssignDamagePin(sb, minionData, 8);  //分配伤害的 damage 走引脚（不是同名字段）

            //---- 第六批：**真实卡池里的卡**端到端（"熔岩人"：手牌光环 → 已损失生命 → 花费下降）----
            CaseRealCardLava(sb);

            //---- 第七批：事件入口族（死亡时 / 攻击时）+ 伤害族（普通吃护甲 / 法伤无视护甲）----
            CaseEntryTrigger(sb, "OnDeath", minionData);
            CaseEntryTrigger(sb, "OnAttack", minionData);
            CaseDamageOrSpell(sb, minionData, false);
            CaseDamageOrSpell(sb, minionData, true);
            CasePlayerDirectDamage(sb, minionData);   //直接以"玩家"为目标（target_card=null）也吃护甲
        }

        /// <summary>通用：入口 → 202001 造成伤害(value ← 待测节点)；用 p1 掉血量当观测量。</summary>
        private Game MakeGame(out GameLogic logic, out Player p0, out Player p1)
        {
            Game g = new Game("probe_semantic", 2);
            logic = new GameLogic(g);
            p0 = g.players[0];
            p1 = g.players[1];
            //★合成对局必须进入 Play 状态：伤害/增益等入口在非 Play 状态下会直接返回（实测全 0 就是这个原因）
            g.state = GameState.Play;
            p0.hero = Card.Create(FirstCard(CardType.Hero), null, p0);
            p0.hp_max = 30;
            p0.hp = 29;
            p1.hero = Card.Create(FirstCard(CardType.Hero), null, p1);
            p1.hp_max = 30;
            p1.hp = 30;
            return g;
        }

        private int RunDamage(GraphData graph, Card host, GameLogic logic, Player p1)
        {
            int before = p1.hp;
            //★必须传目标卡：伤害类动作在没有目标上下文时直接不造成伤害（实测伤害恒 0 就是这个原因）
            NodeDocRunner.Run(logic, graph, host, p1 != null ? p1.hero : null, null, "ActivateEffect");
            return before - p1.hp;
        }

        /// <summary>常量对照 + 整数运算 + 常规整数取值都走这里：value←节点（或直接常量）</summary>
        private void CaseInt(StringBuilder sb, string name, string action, string[] consts, int fieldVal,
            string op, string extra, int expect)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            GraphData graph = NewGraph(name);
            GraphNode ev = Entry(graph);
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", fieldVal.ToString());
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");

            if (action == null)
            {
                // 对照：直接把字段值当伤害
                if (fieldVal != expect)
                    Debug.LogWarning("[语义断言] 对照片段写错");
            }
            else
            {
                GraphNode vn = string.IsNullOrEmpty(op)
                    ? Node(graph, "v", GraphNodeType.Value, action, name)
                    : Node(graph, "v", GraphNodeType.Value, action, name, "operator", op);
                Pin(vn, "A", NodeValueType.Int32, false);
                Pin(vn, "B", NodeValueType.Int32, false);
                Pin(vn, "return", NodeValueType.Int32, true);
                // 常量 A/B
                GraphNode ca = Node(graph, "ca", GraphNodeType.Value, "112003", "常量A", "value", consts[0]);
                Pin(ca, "return", NodeValueType.Int32, true);
                GraphNode cb = Node(graph, "cb", GraphNodeType.Value, "112003", "常量B", "value", consts[1]);
                Pin(cb, "return", NodeValueType.Int32, true);
                Link(graph, ca, "return", vn, "A");
                Link(graph, cb, "return", vn, "B");
                Link(graph, vn, "return", dmg, "value");
            }

            Card host = Card.Create(Minion(), null, p0);
            int got = RunDamage(graph, host, logic, p1);
            Check(sb, name, got == expect, "伤害=" + got, "期望 " + expect);
        }

        /// <summary>102006 获取卡牌攻击 → 伤害应等于该卡攻击</summary>
        private void CaseAttack(StringBuilder sb, CardData data)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            GraphData graph = NewGraph("102006");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, "102006", "获取卡牌攻击");
            Pin(vn, "card", NodeValueType.Card, false);
            Pin(vn, "return", NodeValueType.Int32, true);
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "0");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, ev, "card", vn, "card");
            Link(graph, vn, "return", dmg, "value");

            Card host = Card.Create(data, null, p0);
            int expect = host.GetAttack();
            int got = RunDamage(graph, host, logic, p1);
            Check(sb, "102006 获取卡牌攻击", got == expect, "伤害=" + got + "（卡攻击=" + expect + "）", "期望 " + expect);
        }

        /// <summary>102005 获取卡牌费用 → 伤害应等于该卡费用</summary>
        private void CaseMana(StringBuilder sb, CardData data)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            GraphData graph = NewGraph("102005");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, "102005", "获取卡牌费用");
            Pin(vn, "card", NodeValueType.Card, false);
            Pin(vn, "return", NodeValueType.Int32, true);
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "0");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, ev, "card", vn, "card");
            Link(graph, vn, "return", dmg, "value");

            Card host = Card.Create(data, null, p0);
            int expect = host.GetMana();
            int got = RunDamage(graph, host, logic, p1);
            Check(sb, "102005 获取卡牌费用", got == expect, "伤害=" + got + "（卡费用=" + expect + "）", "期望 " + expect);
        }

        /// <summary>102007/102008：宿主是**英雄卡** → 必须等于所属玩家 hp_max / hp（不能读卡自身字段）</summary>
        private void CaseHeroHp(StringBuilder sb, CardData heroData, bool max)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            p0.hero = Card.Create(heroData, null, p0);
            p0.hp_max = 30;
            p0.hp = 29;
            string id = max ? "102007" : "102008";
            string name = max ? "102007 最大生命(英雄卡→玩家hp_max)" : "102008 当前生命(英雄卡→玩家hp)";

            GraphData graph = NewGraph(name);
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, id, name);
            Pin(vn, "card", NodeValueType.Card, false);
            Pin(vn, "return", NodeValueType.Int32, true);
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "0");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, ev, "card", vn, "card");
            Link(graph, vn, "return", dmg, "value");

            int expect = max ? p0.hp_max : p0.hp;
            int got = RunDamage(graph, p0.hero, logic, p1);
            Check(sb, name, got == expect,
                "伤害=" + got + "（玩家hp=" + p0.hp + "/" + p0.hp_max + "，英雄卡 hp=" + p0.hero.GetHP() + "/" + p0.hero.GetHPMax() + "）",
                "期望 " + expect);
        }

        /// <summary>112004 整数运算：operator 是字段，操作数是 **arg（参数数组）**，输出口是 **result**（不是 return）。
        /// 两个常量都接到同一个 arg 参数口（params 口按入线集合取）。</summary>
        private void CaseIntOp(StringBuilder sb, string op, int a, int b, int expect)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            string name = "112004 整数运算 " + a + op + b;
            GraphData graph = NewGraph(name);
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, "112004", name, "operator", op);
            Pin(vn, "arg", NodeValueType.Int32, false);
            Pin(vn, "result", NodeValueType.Int32, true);
            GraphNode ca = Node(graph, "ca", GraphNodeType.Value, "112003", "常量A", "value", a.ToString());
            Pin(ca, "return", NodeValueType.Int32, true);
            GraphNode cb = Node(graph, "cb", GraphNodeType.Value, "112003", "常量B", "value", b.ToString());
            Pin(cb, "return", NodeValueType.Int32, true);
            Link(graph, ca, "return", vn, "arg");
            Link(graph, cb, "return", vn, "arg");     //params 口：两条入线都要给

            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "0");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, vn, "result", dmg, "value");

            Card host = Card.Create(Minion(), null, p0);
            int got = RunDamage(graph, host, logic, p1);
            Check(sb, name, got == expect, "伤害=" + got, "期望 " + expect);
        }

        /// <summary>102013 获取所有角色（数组）→ 接进 202012 的 targets：总量必须仍是 8（顺带验证数组口非空可用）</summary>
        private void CaseAllCharactersToAssign(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            p1.cards_board.Add(Card.Create(minion, null, p1));

            GraphData graph = NewGraph("102013→202012");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, "102013", "获取所有角色");
            Pin(vn, "return", NodeValueType.Array, true);
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202012", "分配固定法术伤害", "damage", "8");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "targets", NodeValueType.Array, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, vn, "return", dmg, "targets");

            Card host = p0.hero != null ? p0.hero : Card.Create(minion, null, p0);
            Card enemy0 = p1.cards_board[0];
            int before = p1.hp + enemy0.GetHP() + p0.hp;      //★用增量：夹具本身就不是满血（p0=29/30），绝对值会多算 1
            NodeDocRunner.Run(logic, graph, host, null, null, "ActivateEffect");
            int lost = before - (p1.hp + enemy0.GetHP() + p0.hp);
            Check(sb, "102013 获取所有角色 → 202012.targets 总量=8", lost == 8,
                "总掉血=" + lost + "（战前合计=" + before + "）", "期望 8");
        }

        /// <summary>202012 分配伤害：总量必须 == damage（含大量死亡时）</summary>
        private void CaseAssignDamage(StringBuilder sb, CardData minion, int total, bool fragile)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            List<Card> enemies = new List<Card>();
            for (int i = 0; i < 3; i++)
            {
                Card m = Card.Create(minion, null, p1);
                m.hp = fragile ? 1 : (3 + i * 2);
                m.damage = 0;
                p1.cards_board.Add(m);
                enemies.Add(m);
            }

            GraphData graph = NewGraph("202012");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "t", GraphNodeType.Value, "102017", "获取敌方角色");
            Pin(vn, "return", NodeValueType.Array, true);
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202012", "分配固定法术伤害",
                "damage", total.ToString());
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "targets", NodeValueType.Array, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, vn, "return", dmg, "targets");

            Card host = p0.hero != null ? p0.hero : Card.Create(minion, null, p0);
            NodeDocRunner.Run(logic, graph, host, null, null, "ActivateEffect");

            int minionDmg = 0, dead = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                minionDmg += enemies[i].damage;
                if (enemies[i].GetHP() <= 0) dead++;
            }
            int face = p1.hp_max - p1.hp;
            int got = minionDmg + face;
            Check(sb, "202012 分配伤害总量=" + total + (fragile ? "（随从1血·大量死亡）" : "（随从3/5/7血）"),
                got == total, "随从受伤=" + minionDmg + "（死 " + dead + "） 打脸=" + face + " 总=" + got,
                "期望 " + total);
        }

        // ============ 第二批：条件/分支、属性写入、灵力、技能 ============

        /// <summary>112002 比较（A/B 是引脚，operator 是字段）+ 212001 分支动作：
        /// 比较为真 → thenAction 造成 3 点；为假 → elseAction 造成 7 点。于是"比较结果对不对"变成可失败断言，
        /// 同时验证中文运算符（大于/小于/等于）与符号（&gt; / ≠）都被正确归一。</summary>
        private void CaseCompare(StringBuilder sb, CardData minion, string op, int a, int b, bool expectTrue)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            string name = "112002 比较 " + a + op + b;

            GraphData graph = NewGraph(name);
            GraphNode ev = Entry(graph);
            GraphNode cmp = Node(graph, "cmp", GraphNodeType.Value, "112002", name, "operator", op);
            Pin(cmp, "A", NodeValueType.Int32, false);
            Pin(cmp, "B", NodeValueType.Int32, false);
            Pin(cmp, "result", NodeValueType.Boolean, true);
            GraphNode ca = Node(graph, "ca", GraphNodeType.Value, "112003", "常量A", "value", a.ToString());
            Pin(ca, "return", NodeValueType.Int32, true);
            GraphNode cb = Node(graph, "cb", GraphNodeType.Value, "112003", "常量B", "value", b.ToString());
            Pin(cb, "return", NodeValueType.Int32, true);
            Link(graph, ca, "return", cmp, "A");
            Link(graph, cb, "return", cmp, "B");

            GraphNode br = Node(graph, "br", GraphNodeType.Action, "212001", "分支动作");
            Pin(br, "in", NodeValueType.Flow, false);
            Pin(br, "out", NodeValueType.Flow, true);
            Pin(br, "isTrue", NodeValueType.Boolean, false);
            Pin(br, "thenAction", NodeValueType.Flow, false);
            Pin(br, "elseAction", NodeValueType.Flow, false);
            Link(graph, ev, "out", br, "in");
            Link(graph, cmp, "result", br, "isTrue");

            GraphNode t = Node(graph, "t", GraphNodeType.Action, "202001", "造成伤害", "value", "3");
            Pin(t, "in", NodeValueType.Flow, false);
            Pin(t, "out", NodeValueType.Flow, true);
            Pin(t, "value", NodeValueType.Int32, false);
            GraphNode f = Node(graph, "f", GraphNodeType.Action, "202001", "造成伤害", "value", "7");
            Pin(f, "in", NodeValueType.Flow, false);
            Pin(f, "out", NodeValueType.Flow, true);
            Pin(f, "value", NodeValueType.Int32, false);
            Link(graph, br, "thenAction", t, "in");
            Link(graph, br, "elseAction", f, "in");

            Card host = Card.Create(minion, null, p0);
            int got = RunDamage(graph, host, logic, p1);
            int expect = expectTrue ? 3 : 7;
            Check(sb, name, got == expect, "伤害=" + got + "（真→3 假→7）",
                "期望 " + expect + "（比较应=" + expectTrue + "）");
        }

        /// <summary>202037 设置卡牌属性（card ← 入口卡牌 / propName=攻击 / value=7）→ 卡攻击必须变成 7</summary>
        private void CaseSetCardProp(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            GraphData graph = NewGraph("202037");
            GraphNode ev = Entry(graph);
            GraphNode set = Node(graph, "s", GraphNodeType.Action, "202037", "设置卡牌属性", "propName", "攻击");
            Pin(set, "in", NodeValueType.Flow, false);
            Pin(set, "out", NodeValueType.Flow, true);
            Pin(set, "card", NodeValueType.Card, false);
            Pin(set, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", set, "in");
            Link(graph, ev, "card", set, "card");
            GraphNode c7 = Node(graph, "c7", GraphNodeType.Value, "112003", "常量7", "value", "7");
            Pin(c7, "return", NodeValueType.Int32, true);
            Link(graph, c7, "return", set, "value");

            Card host = Card.Create(minion, null, p0);
            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            Check(sb, "202037 设置卡牌属性(攻击=7)", host.GetAttack() == 7,
                "卡攻击=" + host.GetAttack() + "（原 " + minion.attack + "）", "期望 7");
        }

        /// <summary>202037 设置卡牌属性（propName=生命 / value=9）→ 卡当前生命必须变成 9</summary>
        private void CaseSetCardPropHp(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            GraphData graph = NewGraph("202037生命");
            GraphNode ev = Entry(graph);
            GraphNode set = Node(graph, "s", GraphNodeType.Action, "202037", "设置卡牌属性", "propName", "生命");
            Pin(set, "in", NodeValueType.Flow, false);
            Pin(set, "out", NodeValueType.Flow, true);
            Pin(set, "card", NodeValueType.Card, false);
            Pin(set, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", set, "in");
            Link(graph, ev, "card", set, "card");
            GraphNode c9 = Node(graph, "c9", GraphNodeType.Value, "112003", "常量9", "value", "9");
            Pin(c9, "return", NodeValueType.Int32, true);
            Link(graph, c9, "return", set, "value");

            Card host = Card.Create(minion, null, p0);
            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            Check(sb, "202037 设置卡牌属性(生命=9)", host.GetHP() == 9,
                "卡当前生命=" + host.GetHP() + "（原 " + minion.hp + "）", "期望 9");
        }

        /// <summary>201001 设置当前灵力值：count 走**常量线**（字段只是无连线兜底）。
        /// 注意上限钳制：灵力被钳到 [0, mana_max]（实测 mana_max 默认 0 → 设 5 结果 0，不是 bug）。</summary>
        private void CaseSetMana(StringBuilder sb, int manaMax, int count, int expect)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            p0.mana_max = manaMax;
            GraphData graph = NewGraph("201001");
            GraphNode ev = Entry(graph);
            GraphNode set = Node(graph, "s", GraphNodeType.Action, "201001", "设置当前灵力值");
            Pin(set, "in", NodeValueType.Flow, false);
            Pin(set, "out", NodeValueType.Flow, true);
            Pin(set, "player", NodeValueType.Player, false);
            Pin(set, "count", NodeValueType.Int32, false);
            Link(graph, ev, "out", set, "in");
            Link(graph, ev, "player", set, "player");
            //数量接**常量线**（不是同名字段：有端口时以线为准，字段只是无连线兜底）
            GraphNode c5 = Node(graph, "c5", GraphNodeType.Value, "112003", "常量" + count, "value", count.ToString());
            Pin(c5, "return", NodeValueType.Int32, true);
            Link(graph, c5, "return", set, "count");

            Card host = p0.hero;
            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            Check(sb, "201001 设置灵力=" + count + "（上限" + manaMax + "）", p0.mana == expect,
                "玩家0灵力=" + p0.mana + "（上限=" + manaMax + "）", "期望 " + expect);
        }

        /// <summary>101002 获取玩家属性（player ← 入口玩家 / propName=生命）→ 值必须等于该玩家当前生命</summary>
        private void CasePlayerProp(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            GraphData graph = NewGraph("101002");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, "101002", "获取玩家属性", "propName", "生命");
            Pin(vn, "player", NodeValueType.Player, false);
            Pin(vn, "return", NodeValueType.Int32, true);
            Link(graph, ev, "player", vn, "player");
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "0");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, vn, "return", dmg, "value");

            Card host = Card.Create(minion, null, p0);
            int expect = p0.hp;
            int got = RunDamage(graph, host, logic, p1);
            Check(sb, "101002 获取玩家属性(生命)", got == expect, "伤害=" + got + "（玩家0生命=" + expect + "）", "期望 " + expect);
        }

        /// <summary>202050 添加技能（cards ← 入口卡牌 / abilityId = 卡池里第一个技能）→ 施法卡必须"之前没有、之后有"该能力</summary>
        private void CaseAddAbility(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            AbilityData ab = null;
            foreach (AbilityData a in AbilityData.GetAll())
            {
                if (a != null && !string.IsNullOrEmpty(a.id)) { ab = a; break; }
            }
            if (ab == null) { sb.AppendLine("SKIP\t202050\t卡池无技能定义\t"); return; }

            GraphData graph = NewGraph("202050");
            GraphNode ev = Entry(graph);
            GraphNode add = Node(graph, "s", GraphNodeType.Action, "202050", "添加技能", "abilityId", ab.id);
            Pin(add, "in", NodeValueType.Flow, false);
            Pin(add, "out", NodeValueType.Flow, true);
            Pin(add, "cards", NodeValueType.Card, false);
            Link(graph, ev, "out", add, "in");
            Link(graph, ev, "card", add, "cards");

            Card host = Card.Create(minion, null, p0);
            bool before = host.HasAbility(ab);
            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            bool after = host.HasAbility(ab);
            Check(sb, "202050 添加技能 " + ab.id, !before && after,
                "施法卡 HasAbility: " + before + " → " + after, "期望 False → True");
        }

        // ============ 第三批：增益族 ============

        /// <summary>注册固定 id 的测试增益（攻击加成+1，可选 花费-2）。
        /// 必须走反射写私有字典 buff_dict：BuffPoolIO.New() 是按**随机 id** 登记的，
        /// 不换键的话 Get(固定id) 取不到（批量测试台同款配方）。</summary>
        private static BuffData EnsureTestBuff(bool withCost)
        {
            string id = withCost ? "probe_buff_cost" : "probe_buff_atk";
            BuffData bd = BuffPoolIO.Get(id);
            if (bd != null && bd.props != null && bd.props.Count > 0)
                return bd;
            bd = BuffPoolIO.New();
            bd.title = "语义探针增益";
            bd.id = id;
            //★权威通道是 mods（SyncLegacyProps 是 mods→props 的单向回写；只写 props 运行期取不到值 → 攻击/费用都不变）
            bd.mods = new List<BuffPropMod> { new BuffPropMod(BuffModTarget.Attack, BuffModMode.Add, 1) };
            if (withCost)
                bd.mods.Add(new BuffPropMod(BuffModTarget.Cost, BuffModMode.Add, -2));
            bd.SyncLegacyProps();
            try
            {
                FieldInfo fi = typeof(BuffPoolIO).GetField("buff_dict", BindingFlags.Static | BindingFlags.NonPublic);
                object dict = fi != null ? fi.GetValue(null) : null;
                MethodInfo setter = dict != null ? dict.GetType().GetMethod("set_Item") : null;
                if (setter != null)
                    setter.Invoke(dict, new object[] { id, bd });
            }
            catch (Exception e) { Debug.LogWarning("[语义断言] 注册测试增益失败：" + e.Message); }
            return bd;
        }

        /// <summary>206001 添加增益（buffDefine=测试增益 / cards ← 入口卡牌）：
        /// 断言 ①卡真的带上了该增益 ②攻击 +1 ③花费 -2（这就是"费用降低"链路的**增益侧**，
        /// 会失败的话说明 -花费 的属性修改根本没进原生状态）。</summary>
        private void CaseAddBuff(StringBuilder sb, CardData minion, bool withCost)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            BuffData bd = EnsureTestBuff(withCost);
            Card host = Card.Create(minion, null, p0);
            p0.cards_board.Add(host);                       //★上战场：ongoing 收敛点是按区域枚举卡牌的
            int baseAtk = host.GetAttack();
            int baseMana = host.GetMana();

            GraphData graph = NewGraph("206001");
            GraphNode ev = Entry(graph);
            GraphNode add = Node(graph, "s", GraphNodeType.Action, "206001", "添加增益", "buffDefine", bd.id);
            Pin(add, "in", NodeValueType.Flow, false);
            Pin(add, "out", NodeValueType.Flow, true);
            Pin(add, "cards", NodeValueType.Card, false);
            Link(graph, ev, "out", add, "in");
            Link(graph, ev, "card", add, "cards");

            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            //★收敛点：把增益的原生状态（AddManaCost 等）合进当前值；只调 Reapply 不够（实测攻击/费用都不变）
            logic.UpdateOngoing();
            BuffRuntime.Reapply(host);

            bool has = BuffRuntime.HasBuff(host, bd.id);
            Check(sb, "206001 添加增益（buffDefine" + (withCost ? "+花费-2" : "") + "）", has,
                "HasBuff(" + bd.id + ")=" + has, "期望 True");
            int manaExpect = baseMana + (withCost ? -2 : 0);
            Check(sb, "206001 费用" + (withCost ? "-2" : "不变"), host.GetMana() == manaExpect,
                "费用 " + baseMana + " → " + host.GetMana(), "期望 " + manaExpect);
            Check(sb, "206001 攻击+1", host.GetAttack() == baseAtk + 1,
                "攻击 " + baseAtk + " → " + host.GetAttack(), "期望 " + (baseAtk + 1));
        }

        // ============ 第四批：移动 / 治疗 / 消灭 / 护甲 / 复制 / 变形 / 关键词 ============

        private static int Cnt(List<Card> l) { return l != null ? l.Count : 0; }
        private static int AllCards(Player p)
        {
            return Cnt(p.cards_hand) + Cnt(p.cards_board) + Cnt(p.cards_deck)
                 + Cnt(p.cards_equip) + Cnt(p.cards_secret);
        }

        /// <summary>210001 简单抽牌（player ← 入口玩家）→ 手牌 +1 且 牌库 -1</summary>
        private void CaseDraw(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            for (int i = 0; i < 3; i++)
                p0.cards_deck.Add(Card.Create(minion, null, p0));
            int h0 = Cnt(p0.cards_hand), d0 = Cnt(p0.cards_deck);

            GraphData graph = NewGraph("210001");
            GraphNode ev = Entry(graph);
            GraphNode n = Node(graph, "s", GraphNodeType.Action, "210001", "简单抽牌");
            Pin(n, "in", NodeValueType.Flow, false);
            Pin(n, "out", NodeValueType.Flow, true);
            Pin(n, "player", NodeValueType.Player, false);
            Link(graph, ev, "out", n, "in");
            Link(graph, ev, "player", n, "player");

            NodeDocRunner.Run(logic, graph, p0.hero, p1.hero, null, "ActivateEffect");
            Check(sb, "210001 简单抽牌", Cnt(p0.cards_hand) == h0 + 1 && Cnt(p0.cards_deck) == d0 - 1,
                "手牌 " + h0 + "→" + Cnt(p0.cards_hand) + "，牌库 " + d0 + "→" + Cnt(p0.cards_deck),
                "手牌+1 且 牌库-1");
        }

        /// <summary>202002 丢弃卡牌（hands ← 入口卡牌；宿主放在手牌里）→ 手牌 -1</summary>
        private void CaseDiscard(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            Card host = Card.Create(minion, null, p0);
            p0.cards_hand.Add(host);
            int h0 = Cnt(p0.cards_hand);

            GraphData graph = NewGraph("202002");
            GraphNode ev = Entry(graph);
            GraphNode n = Node(graph, "s", GraphNodeType.Action, "202002", "丢弃卡牌");
            Pin(n, "in", NodeValueType.Flow, false);
            Pin(n, "out", NodeValueType.Flow, true);
            Pin(n, "hands", NodeValueType.Card, false);
            Link(graph, ev, "out", n, "in");
            Link(graph, ev, "card", n, "hands");

            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            Check(sb, "202002 丢弃卡牌", Cnt(p0.cards_hand) == h0 - 1,
                "手牌 " + h0 + "→" + Cnt(p0.cards_hand), "期望 " + (h0 - 1));
        }

        /// <summary>202013 治疗目标卡牌（targets ← 入口卡牌 / value=3；宿主先扣 5 点）→ 伤害标记应剩 2</summary>
        private void CaseHeal(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            Card host = Card.Create(minion, null, p0);
            host.damage = 5;
            p0.cards_board.Add(host);

            GraphData graph = NewGraph("202013");
            GraphNode ev = Entry(graph);
            GraphNode n = Node(graph, "s", GraphNodeType.Action, "202013", "治疗目标卡牌");
            Pin(n, "in", NodeValueType.Flow, false);
            Pin(n, "out", NodeValueType.Flow, true);
            Pin(n, "targets", NodeValueType.Card, false);
            Pin(n, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", n, "in");
            Link(graph, ev, "card", n, "targets");
            GraphNode c3 = Node(graph, "c3", GraphNodeType.Value, "112003", "常量3", "value", "3");
            Pin(c3, "return", NodeValueType.Int32, true);
            Link(graph, c3, "return", n, "value");

            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            Check(sb, "202013 治疗(5 伤害 + 治 3)", host.damage == 2,
                "伤害标记 5 → " + host.damage + "（当前血 " + host.GetHP() + "）", "期望 2");
        }

        /// <summary>202016 消灭（cards ← 入口卡牌）→ 目标应死亡</summary>
        private void CaseDestroy(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            //★必须给显式 uid：区域查找（IsOnBoard/IsInHand…）都是按 uid 查的，
            //  uid 为空时 GetBoardCard 查不到 → KillCard/DiscardCard 直接早退（表现为"消灭什么都不干"）
            Card host = Card.Create(minion, null, p1, "probe_destroy_target");
            p1.cards_board.Add(host);   //放对面，避免"消灭自己的卡"被自家光环类逻辑干扰

            GraphData graph = NewGraph("202016");
            GraphNode ev = Entry(graph);
            GraphNode n = Node(graph, "s", GraphNodeType.Action, "202016", "消灭");
            Pin(n, "in", NodeValueType.Flow, false);
            Pin(n, "out", NodeValueType.Flow, true);
            Pin(n, "cards", NodeValueType.Card, false);
            Link(graph, ev, "out", n, "in");
            Link(graph, ev, "card", n, "cards");

            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            logic.UpdateOngoing();   //★消灭先打"濒死"标记，死亡在收敛点处理（不跑这步看不到结果）
            bool dead = host.GetHP() <= 0 || !p1.cards_board.Contains(host);
            Check(sb, "202016 消灭", dead,
                "目标当前生命=" + host.GetHP() + "（damage=" + host.damage + "，仍在战场="
                + p1.cards_board.Contains(host) + "）", "期望 死亡/离场");
        }

        /// <summary>202019 增加固定数量护甲(+3) → 202020 失去护甲(1)：净 +2</summary>
        private void CaseArmorAddLose(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            int a0 = p0.hero.GetStatusValue(StatusType.Armor);

            GraphData graph = NewGraph("202019→202020");
            GraphNode ev = Entry(graph);
            GraphNode add = Node(graph, "a", GraphNodeType.Action, "202019", "增加固定数量护甲");
            Pin(add, "in", NodeValueType.Flow, false);
            Pin(add, "out", NodeValueType.Flow, true);
            Pin(add, "player", NodeValueType.Player, false);
            Pin(add, "count", NodeValueType.Int32, false);
            GraphNode lose = Node(graph, "l", GraphNodeType.Action, "202020", "失去护甲");
            Pin(lose, "in", NodeValueType.Flow, false);
            Pin(lose, "out", NodeValueType.Flow, true);
            Pin(lose, "player", NodeValueType.Player, false);
            Pin(lose, "count", NodeValueType.Int32, false);
            Link(graph, ev, "out", add, "in");
            Link(graph, add, "out", lose, "in");
            Link(graph, ev, "player", add, "player");
            Link(graph, ev, "player", lose, "player");
            GraphNode c3 = Node(graph, "c3", GraphNodeType.Value, "112003", "常量3", "value", "3");
            Pin(c3, "return", NodeValueType.Int32, true);
            Link(graph, c3, "return", add, "count");
            GraphNode c1 = Node(graph, "c1", GraphNodeType.Value, "112003", "常量1", "value", "1");
            Pin(c1, "return", NodeValueType.Int32, true);
            Link(graph, c1, "return", lose, "count");

            NodeDocRunner.Run(logic, graph, p0.hero, p1.hero, null, "ActivateEffect");
            int a1 = p0.hero.GetStatusValue(StatusType.Armor);
            Check(sb, "202019 +3 → 202020 -1 护甲", a1 == a0 + 2,
                "护甲 " + a0 + " → " + a1, "期望 " + (a0 + 2));
        }

        /// <summary>202044 复制卡牌（card ← 入口卡牌）→ 该玩家总牌数（手牌+战场+牌库+装备+奥秘）+1</summary>
        private void CaseCopy(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            Card host = Card.Create(minion, null, p0);
            p0.cards_hand.Add(host);
            int n0 = AllCards(p0);

            GraphData graph = NewGraph("202044");
            GraphNode ev = Entry(graph);
            GraphNode n = Node(graph, "s", GraphNodeType.Action, "202044", "复制卡牌");
            Pin(n, "in", NodeValueType.Flow, false);
            Pin(n, "out", NodeValueType.Flow, true);
            Pin(n, "card", NodeValueType.Card, false);
            Link(graph, ev, "out", n, "in");
            Link(graph, ev, "card", n, "card");

            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            Check(sb, "202044 复制卡牌", AllCards(p0) == n0 + 1,
                "总牌数 " + n0 + " → " + AllCards(p0), "期望 " + (n0 + 1));
        }

        /// <summary>202029 变形为卡牌定义（card ← 入口卡牌 / define=字段指向另一张卡）→ 该卡的卡牌定义应被换掉</summary>
        private void CaseTransform(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            CardData other = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Character && c.id != minion.id && !string.IsNullOrEmpty(c.id))
                { other = c; break; }
            if (other == null) { sb.AppendLine("SKIP\t202029\t没有第二张角色定义\t"); return; }

            Card host = Card.Create(minion, null, p0);
            p0.cards_board.Add(host);
            string before = host.CardData != null ? host.CardData.id : "?";

            GraphData graph = NewGraph("202029");
            GraphNode ev = Entry(graph);
            GraphNode n = Node(graph, "s", GraphNodeType.Action, "202029", "变形为卡牌定义", "define", other.id);
            Pin(n, "in", NodeValueType.Flow, false);
            Pin(n, "out", NodeValueType.Flow, true);
            Pin(n, "card", NodeValueType.Card, false);
            Link(graph, ev, "out", n, "in");
            Link(graph, ev, "card", n, "card");

            NodeDocRunner.Run(logic, graph, host, p1.hero, null, "ActivateEffect");
            string after = host.CardData != null ? host.CardData.id : "?";
            Check(sb, "202029 变形为卡牌定义", after == other.id,
                "定义 " + before + " → " + after, "期望 " + other.id);
        }

        /// <summary>真实卡端到端：卡池里标题含「熔岩」的卡（手牌光环「费用降低」→ 英雄已损失生命 → 花费下降）。
        /// 观测链：英雄掉 5 血前后，手牌里这张卡的 **费用** 应下降 5（设计意图：每损失 1 点生命费用 -1）。
        /// 这条会一次性覆盖：手牌能否当光环源、动作线是否重算、变量是否写对、增益是否落到费用上。</summary>
        private void CaseRealCardLava(StringBuilder sb)
        {
            CardData lava = null;
            string cand = "";
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null || string.IsNullOrEmpty(c.title) || !c.title.Contains("熔岩"))
                    continue;
                int cc = c.abilities != null ? c.abilities.Length : 0;
                cand += (cand.Length > 0 ? " ｜ " : "") + c.id + "(定义能力" + cc + ")";
                if (lava == null)
                    lava = c;
                if (cc > 0)
                    lava = c;      //优先取"有能力的"那份（可能存在同名旧数据）
            }
            if (lava == null) { sb.AppendLine("SKIP\t真实卡-熔岩人\t卡池里没有标题含「熔岩」的卡\t"); return; }
            sb.AppendLine("INFO\t真实卡-熔岩人 同名候选\t" + cand + "\t（诊断：是否存在重复/未编译的定义）");

            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            p0.hp_max = 30; p0.hp = 30;
            p1.hp_max = 30; p1.hp = 30;
            Card lavaCard = Card.Create(lava, null, p0, "probe_lava");
            //★必须把定义上的能力灌进**卡实例**：Card.GetAbilities() 读的是实例自己的
            //  abilities / abilities_ongoing（Card.Create 不会填），否则卡天生"没能力" →
            //  光环/被动全部不生效（实测踩到，与产品逻辑无关）。
            if (lavaCard.abilities == null) lavaCard.abilities = new List<string>();
            if (lavaCard.abilities_ongoing == null) lavaCard.abilities_ongoing = new List<string>();
            if (lava.abilities != null)
            {
                foreach (AbilityData a in lava.abilities)
                {
                    if (a == null || string.IsNullOrEmpty(a.id))
                        continue;
                    if (a.trigger == AbilityTrigger.Ongoing)
                        lavaCard.abilities_ongoing.Add(a.id);
                    else
                        lavaCard.abilities.Add(a.id);
                }
            }
            p0.cards_hand.Add(lavaCard);

            logic.UpdateOngoing();
            logic.UpdateOngoing();
            int mana0 = lavaCard.GetMana();
            string buffs0 = BuffList(lavaCard);
            sb.AppendLine("INFO\t真实卡-熔岩人 能力\t" + AbInfo(lavaCard) + "\t（诊断：光环能力是否在卡上、字段是否齐全）");

            //英雄掉 5 血（模拟"受到伤害"）
            logic.DamagePlayer(p1.hero, p0, 5);
            logic.UpdateOngoing();
            logic.UpdateOngoing();
            int mana1 = lavaCard.GetMana();
            string buffs1 = BuffList(lavaCard);

            sb.AppendLine("INFO\t真实卡-熔岩人\t费用 " + mana0 + " → " + mana1 + "（英雄 " + p0.hp + "/" + p0.hp_max
                + "，已损失 " + (p0.hp_max - p0.hp) + "）｜增益 前[" + buffs0 + "] 后[" + buffs1 + "]\t设计意图：费用 -5");
            Check(sb, "真实卡-熔岩人：掉 5 血 → 费用 -5", mana1 == mana0 - 5,
                "费用 " + mana0 + " → " + mana1 + "（已损失生命 " + (p0.hp_max - p0.hp) + "）", "期望 " + (mana0 - 5));
        }

        /// <summary>事件入口族：入口动作名 = OnDeath / OnAttack（卡池里就是这么存的英文名）。
        /// 验证「入口能不能被识别」+ 动作能不能跑（入口名对不上 = 整条效果静默不触发）。</summary>
        private void CaseEntryTrigger(StringBuilder sb, string entry, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            GraphData graph = NewGraph(entry);
            GraphNode ev = Node(graph, "ev", GraphNodeType.Event, entry, entry);
            Pin(ev, "out", NodeValueType.Flow, true);
            Pin(ev, "card", NodeValueType.Card, true);
            GraphNode d = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "3");
            Pin(d, "in", NodeValueType.Flow, false);
            Pin(d, "out", NodeValueType.Flow, true);
            Pin(d, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", d, "in");

            Card host = Card.Create(minion, null, p0, "probe_entry_" + entry);
            int before = p1.hp;
            NodeDocRunner.Run(logic, graph, host, p1.hero, null, entry);
            int got = before - p1.hp;
            Check(sb, "入口「" + entry + "」→ 造成伤害 3", got == 3, "目标玩家掉血=" + got, "期望 3");
        }

        /// <summary>伤害族：202041（damage 走**字段**、targets ← 敌方角色）。
        /// 普通伤害应被护甲吸收（4 点打在 5 护甲上 → 掉 0 血、护甲 -4）；法伤应**无视护甲**（掉 4 血、护甲不变）。</summary>
        private void CaseDamageOrSpell(StringBuilder sb, CardData minion, bool spell)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            //★走 ongoing 通道（引擎自己给护甲用的是 AddOngoingStatus；AddStatus 只写值、HasStatus 不成立
            //  → 伤害路径的 `HasStatus(Armor)` 判定为假 → 护甲被完全无视，实测踩到）
            p1.hero.AddOngoingStatus(StatusType.Armor, 5);
            int armor0 = p1.hero.GetStatusValue(StatusType.Armor);
            int hp0 = p1.hp;

            GraphData graph = NewGraph("202041");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "t", GraphNodeType.Value, "102017", "获取敌方角色");
            Pin(vn, "return", NodeValueType.Array, true);
            GraphNode d = Node(graph, "d", GraphNodeType.Action, "202041", "造成伤害或法伤",
                "damage", "4", "damagetype", spell ? "true" : "false");
            Pin(d, "in", NodeValueType.Flow, false);
            Pin(d, "out", NodeValueType.Flow, true);
            Pin(d, "targets", NodeValueType.Array, false);
            Link(graph, ev, "out", d, "in");
            Link(graph, vn, "return", d, "targets");

            sb.AppendLine("INFO\t护甲诊断\tHasStatus(Armor)=" + p1.hero.HasStatus(StatusType.Armor)
                + " GetStatusValue(Armor)=" + p1.hero.GetStatusValue(StatusType.Armor)
                + " 英雄hp=" + p1.hero.GetHP() + "\t（判断护甲为何不吸收）");

            Card host = p0.hero;
            NodeDocRunner.Run(logic, graph, host, null, null, "ActivateEffect");
            int lost = hp0 - p1.hp;
            int armor1 = p1.hero.GetStatusValue(StatusType.Armor);
            if (spell)
                Check(sb, "202041 法伤无视护甲（4 点 vs 护甲 5）", lost == 4 && armor1 == armor0,
                    "掉血=" + lost + " 护甲 " + armor0 + "→" + armor1, "期望 掉血 4、护甲不变");
            else
                //护甲是**减伤**（value -= 护甲），本身不消耗 → 期望 掉血 0、护甲不变
                Check(sb, "202041 普通伤害被护甲吸收（4 点 vs 护甲 5）", lost == 0 && armor1 == armor0,
                    "掉血=" + lost + " 护甲 " + armor0 + "→" + armor1, "期望 掉血 0、护甲不变");
        }

        /// <summary>直接以「玩家」为目标的伤害（target_card=null、target_player=p1）：也必须吃护甲
        /// （这条走 DamagePlayer，与"以英雄卡为目标"是两条路；只修一条会漏）。</summary>
        private void CasePlayerDirectDamage(StringBuilder sb, CardData minion)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            p1.hero.AddOngoingStatus(StatusType.Armor, 5);
            int hp0 = p1.hp;

            GraphData graph = NewGraph("玩家直伤");
            GraphNode ev = Entry(graph);
            GraphNode d = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "4");
            Pin(d, "in", NodeValueType.Flow, false);
            Pin(d, "out", NodeValueType.Flow, true);
            Pin(d, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", d, "in");

            Card host = p0.hero;
            NodeDocRunner.Run(logic, graph, host, null, p1, "ActivateEffect");   //★目标=玩家（不是卡）
            int lost = hp0 - p1.hp;
            Check(sb, "玩家直伤 4 点 vs 英雄护甲 5", lost == 0,
                "目标玩家掉血=" + lost + "（护甲 5）", "期望 0（护甲全吸收）");
        }

        /// <summary>诊断用：把卡上的能力（含光环字段）打出来 —— 判断"光环为什么没生效"时一眼可见</summary>
        private static string AbInfo(Card c)
        {
            if (c == null)
                return "卡为空";
            List<AbilityData> abs = c.GetAbilities();
            if (abs == null || abs.Count == 0)
                return "无能力（GetAbilities 为空）";
            string s = "";
            for (int i = 0; i < abs.Count; i++)
            {
                AbilityData a = abs[i];
                if (a == null)
                    continue;
                s += (s.Length > 0 ? " ｜ " : "") + a.id + " trigger=" + a.trigger
                   + " group=" + a.aura_group + " buff=" + a.aura_buff
                   + " zone=" + a.aura_zone + " tzone=" + a.aura_target_zone
                   + " repeat=" + a.aura_repeat
                   + " 条件数=" + (a.conditions_target != null ? a.conditions_target.Length : 0);
            }
            return s.Length > 0 ? s : "无能力";
        }

        private static string BuffList(Card c)
        {
            if (c == null || c.buffs == null || c.buffs.Count == 0)
                return "无";
            string s = "";
            for (int i = 0; i < c.buffs.Count; i++)
            {
                CardBuff b = c.buffs[i];
                string id = (b != null && b.BuffData != null) ? b.BuffData.id : "?";
                s += (i > 0 ? "," : "") + id;
            }
            return s;
        }

        /// <summary>112004 整数运算：操作数走**编号槽 arg / arg2**（老版面板与卡池里的真实数据就是这么存的，
        /// 引擎必须认；否则第二个操作数被丢掉 → 结果静默错）。</summary>
        private void CaseIntOpSlots(StringBuilder sb, string op, int a, int b, int expect)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            string name = "112004 编号槽 " + a + op + b;
            GraphData graph = NewGraph(name);
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, "112004", name, "operator", op);
            Pin(vn, "arg", NodeValueType.Int32, false);
            Pin(vn, "arg2", NodeValueType.Int32, false);      //★老写法的第二个操作数槽
            Pin(vn, "result", NodeValueType.Int32, true);
            GraphNode ca = Node(graph, "ca", GraphNodeType.Value, "112003", "常量A", "value", a.ToString());
            Pin(ca, "return", NodeValueType.Int32, true);
            GraphNode cb = Node(graph, "cb", GraphNodeType.Value, "112003", "常量B", "value", b.ToString());
            Pin(cb, "return", NodeValueType.Int32, true);
            Link(graph, ca, "return", vn, "arg");
            Link(graph, cb, "return", vn, "arg2");

            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "0");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, vn, "result", dmg, "value");

            Card host = Card.Create(Minion(), null, p0);
            int got = RunDamage(graph, host, logic, p1);
            Check(sb, name, got == expect, "伤害=" + got, "期望 " + expect);
        }

        /// <summary>202012 分配伤害：damage 走**引脚**（连线常量），targets ← 102013 所有角色 ——
        /// 字段 vs 引脚两条路都要能取到值（只认字段的话，连线写法会被静默忽略）。</summary>
        private void CaseAssignDamagePin(StringBuilder sb, CardData minion, int total)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            Card enemy = Card.Create(minion, null, p1, "probe_assign_pin");
            p1.cards_board.Add(enemy);

            GraphData graph = NewGraph("202012引脚");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "t", GraphNodeType.Value, "102013", "获取所有角色");
            Pin(vn, "return", NodeValueType.Array, true);
            GraphNode dmg = Node(graph, "d", GraphNodeType.Action, "202012", "分配固定法术伤害");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "targets", NodeValueType.Array, false);
            Pin(dmg, "damage", NodeValueType.Int32, false);
            Link(graph, ev, "out", dmg, "in");
            Link(graph, vn, "return", dmg, "targets");
            GraphNode c8 = Node(graph, "c8", GraphNodeType.Value, "112003", "常量" + total, "value", total.ToString());
            Pin(c8, "return", NodeValueType.Int32, true);
            Link(graph, c8, "return", dmg, "damage");

            Card host = p0.hero != null ? p0.hero : Card.Create(minion, null, p0);
            int before = p1.hp + enemy.GetHP() + p0.hp;
            NodeDocRunner.Run(logic, graph, host, null, null, "ActivateEffect");
            int lost = before - (p1.hp + enemy.GetHP() + p0.hp);
            Check(sb, "202012 damage 走引脚=" + total, lost == total,
                "总掉血=" + lost, "期望 " + total);
        }

        /// <summary>102003 卡牌关键词判断（card ← 入口卡牌 / select=关键词）+ 212001 分支：
        /// 有该关键词 → 走真分支(3)；没有 → 走假分支(7)。present 参数控制给不给这张卡加关键词。</summary>
        private void CaseKeywordCheck(StringBuilder sb, CardData minion, bool present)
        {
            GameLogic logic; Player p0, p1;
            Game g = MakeGame(out logic, out p0, out p1);
            KeywordData kw = null;
            foreach (KeywordData k in KeywordData.GetAll())
                if (k != null && !string.IsNullOrEmpty(k.id)) { kw = k; break; }
            if (kw == null) { sb.AppendLine("SKIP\t102003\t无关键词定义\t"); return; }

            Card host = Card.Create(minion, null, p0);
            if (present)
                host.buff_added_keywords.Add(kw.id);   //增益获得的关键词表（ApplyModRule 写的就是这张表）

            GraphData graph = NewGraph("102003");
            GraphNode ev = Entry(graph);
            GraphNode vn = Node(graph, "v", GraphNodeType.Value, "102003", "卡牌关键词判断", "select", kw.id);
            Pin(vn, "card", NodeValueType.Card, false);
            Pin(vn, "return", NodeValueType.Boolean, true);
            Link(graph, ev, "card", vn, "card");
            GraphNode br = Node(graph, "br", GraphNodeType.Action, "212001", "分支动作");
            Pin(br, "in", NodeValueType.Flow, false);
            Pin(br, "out", NodeValueType.Flow, true);
            Pin(br, "isTrue", NodeValueType.Boolean, false);
            Pin(br, "thenAction", NodeValueType.Flow, false);
            Pin(br, "elseAction", NodeValueType.Flow, false);
            Link(graph, ev, "out", br, "in");
            Link(graph, vn, "return", br, "isTrue");
            GraphNode t = Node(graph, "t", GraphNodeType.Action, "202001", "造成伤害", "value", "3");
            Pin(t, "in", NodeValueType.Flow, false);
            Pin(t, "out", NodeValueType.Flow, true);
            Pin(t, "value", NodeValueType.Int32, false);
            GraphNode f = Node(graph, "f", GraphNodeType.Action, "202001", "造成伤害", "value", "7");
            Pin(f, "in", NodeValueType.Flow, false);
            Pin(f, "out", NodeValueType.Flow, true);
            Pin(f, "value", NodeValueType.Int32, false);
            Link(graph, br, "thenAction", t, "in");
            Link(graph, br, "elseAction", f, "in");

            int got = RunDamage(graph, host, logic, p1);
            int expect = present ? 3 : 7;
            Check(sb, "102003 关键词判断[" + kw.id + "]（" + (present ? "有" : "没有") + "关键词）", got == expect,
                "伤害=" + got + "（真→3 假→7）", "期望 " + expect);
        }
    }
}
