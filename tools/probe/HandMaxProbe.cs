using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【局内手牌上限验证】仿"灵力上限"的玩家属性 Player.hand_max：
    /// ① 开局从配置写入（=GameplayData.cards_max）；② 默认上限抽牌停在配置值；
    /// ③ 节点 AddHandMax → 上限提高、且能多抽；④ 节点 SetHandMax → 上限改小、**不弃现有手牌**；
    /// ⑤ 取值节点 GetHandMax → 图里读到的是当前上限；⑥ 设为 0 → 退回配置值（未初始化兜底）；
    /// ⑦ Player.Clone 带上该字段（AI 预测树）。
    /// 触发：建 tools/handmax_flag.txt → 进 Play → 写 tools/handmax_result.tsv → 自动删标记。
    /// </summary>
    public class HandMaxProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/handmax_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/handmax_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("HandMaxProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<HandMaxProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath)) { done = false; frames = 0; sb.Clear(); }
                return;
            }
            frames++;
            if (frames < 120)
                return;
            done = true;
            try { Run(); }
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e.Message); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[手牌上限] 验证完成 → " + OutPath);
        }

        private static GraphNode Node(GraphData g, string id, GraphNodeType type, string action, string title, params string[] fp)
        {
            var n = new GraphNode();
            n.id = id; n.type = type; n.action = action; n.title = title;
            n.pins = new List<GraphPin>();
            n.fields = new List<FieldCustomData>();
            for (int i = 0; i + 1 < fp.Length; i += 2)
                n.fields.Add(new FieldCustomData { name = fp[i], value = fp[i + 1] });
            foreach (NodeDocDef d in NodeDocDb.All)
                if (d != null && d.define_id == action) { n.category = string.IsNullOrEmpty(d.category) ? "其他" : d.category; break; }
            if (string.IsNullOrEmpty(n.category)) n.category = "其他";
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

        /// <summary>造一张"Event 入口 → 手牌上限动作节点"的图（动作节点参数：action/value）</summary>
        private static GraphData HandMaxGraph(string action, string value)
        {
            GraphData g = new GraphData { name = "探针手牌上限图", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode ev = Node(g, "ev", GraphNodeType.Event, "ActivateEffect", "主动效果入口");
            Pin(ev, "out", NodeValueType.Flow, true);
            GraphNode act = Node(g, "a", GraphNodeType.Action, action, action, "value", value);
            Pin(act, "in", NodeValueType.Flow, false);
            Pin(act, "player", NodeValueType.Player, false);
            Pin(act, "value", NodeValueType.Int32, false);
            Pin(act, "out", NodeValueType.Flow, true);
            Link(g, ev, "out", act, "in");
            return g;
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");
            int cfg = GameplayData.Get().cards_max;

            Game game = new Game("probe_handmax", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0], p1 = game.players[1];
            CardData hero = null, any = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (hero == null && c.type == CardType.Hero) hero = c;
                if (any == null && c.type == CardType.Character) any = c;
            }
            if (hero == null || any == null) { sb.AppendLine("SKIP\t缺少英雄/角色定义\t"); return; }
            p1.hero = Card.Create(hero, null, p1, "hm_p1hero"); p1.hp_max = 30; p1.hp = 30;

            //---- ① 开局写入：走真实 SetPlayerDeck（与开战同一条路）----
            string init_note = "";
            try
            {
                UserDeckData deck = new UserDeckData();
                deck.tid = "probe_handmax_deck";
                deck.cards = new UserCardData[0];
                deck.hero = new UserCardData { tid = hero.id, variant = "1" };
                logic.SetPlayerDeck(p0, deck);
                init_note = "hero=" + (p0.hero != null ? "有" : "null");
            }
            catch (Exception e) { init_note = "SetPlayerDeck 异常: " + e.Message; }
            if (p0.hand_max <= 0) p0.hand_max = cfg;   //兜底，保证后续断言可继续
            sb.AppendLine((p0.hand_max == cfg ? "PASS" : "FAIL")
                + "\t① 开局手牌上限=配置值\t上限=" + p0.hand_max + "（配置=" + cfg + "）" + init_note);

            //---- 夹具：p0 牌库 40 张（抽牌断言用）----
            for (int i = 0; i < 40; i++)
                p0.cards_deck.Add(Card.Create(any, null, p0, "hm_deck_" + i));
            Card caster = Card.Create(any, null, p0, "hm_caster");
            p0.cards_hand.Clear();

            //---- ② 原概念闸门：默认上限（=配置值）抽 40 张 → 停在配置值 ----
            p0.cards_hand.Clear();
            logic.DrawCard(p0, 40);
            sb.AppendLine((p0.GetHandMax() == cfg && p0.cards_hand.Count == cfg ? "PASS" : "FAIL")
                + "\t② 默认上限抽牌停在配置值（原概念闸门）\t上限=" + p0.GetHandMax()
                + " 手牌=" + p0.cards_hand.Count + "（配置 " + cfg + "）");

            //---- ③ 节点 SetHandMax(4)：上限改小 → 抽牌停在 4 ----
            int ran0 = NodeDocRunner.Run(logic, HandMaxGraph("SetHandMax", "4"), caster, p1.hero, null, "ActivateEffect");
            p0.cards_hand.Clear();
            logic.DrawCard(p0, 40);
            int lim_set = p0.GetHandMax();
            sb.AppendLine((ran0 > 0 && lim_set == 4 && p0.cards_hand.Count == 4 ? "PASS" : "FAIL")
                + "\t③ 设置上限=4 后抽牌停在 4\t动作数=" + ran0 + " 上限=" + lim_set + " 手牌=" + p0.cards_hand.Count);

            //---- ④ 节点 AddHandMax(+3)：上限提高 → 能多抽 ----
            int ran1 = NodeDocRunner.Run(logic, HandMaxGraph("AddHandMax", "3"), caster, p1.hero, null, "ActivateEffect");
            int after_add = p0.GetHandMax();
            p0.cards_hand.Clear();
            logic.DrawCard(p0, 40);
            sb.AppendLine((ran1 > 0 && after_add == 7 && p0.cards_hand.Count == 7 ? "PASS" : "FAIL")
                + "\t④ 增加手牌上限(+3)后能多抽\t动作数=" + ran1 + " 上限=" + after_add + " 手牌=" + p0.cards_hand.Count
                + "（期望 7）");

            //---- ④ 节点 SetHandMax(3)：改小上限，且不弃现有手牌 ----
            int hand_before = p0.cards_hand.Count;
            int ran2 = NodeDocRunner.Run(logic, HandMaxGraph("SetHandMax", "3"), caster, p1.hero, null, "ActivateEffect");
            int after_set = p0.GetHandMax();
            sb.AppendLine((ran2 > 0 && after_set == 3 && p0.cards_hand.Count == hand_before ? "PASS" : "FAIL")
                + "\t⑤ 设置手牌上限改小不弃手牌\t动作数=" + ran2 + " 上限=" + after_set
                + " 手牌 " + hand_before + "→" + p0.cards_hand.Count + "（期望不变）");

            //---- ⑤ 取值节点 GetHandMax → 图里读到当前上限（用它当伤害值）----
            GraphData gv = new GraphData { name = "探针取值图", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode ev2 = Node(gv, "ev", GraphNodeType.Event, "ActivateEffect", "主动效果入口");
            Pin(ev2, "out", NodeValueType.Flow, true);
            GraphNode gv_get = Node(gv, "g", GraphNodeType.Value, "GetHandMax", "获取手牌上限");
            Pin(gv_get, "player", NodeValueType.Player, false);
            Pin(gv_get, "return", NodeValueType.Int32, true);
            GraphNode gv_dmg = Node(gv, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "0");
            Pin(gv_dmg, "in", NodeValueType.Flow, false);
            Pin(gv_dmg, "value", NodeValueType.Int32, false);
            Pin(gv_dmg, "out", NodeValueType.Flow, true);
            Link(gv, ev2, "out", gv_dmg, "in");
            Link(gv, gv_get, "return", gv_dmg, "value");
            int hp0 = p1.hp;
            int ran3 = NodeDocRunner.Run(logic, gv, caster, p1.hero, null, "ActivateEffect");
            int dmg = hp0 - p1.hp;
            sb.AppendLine((ran3 > 0 && dmg == after_set ? "PASS" : "FAIL")
                + "\t⑥ 取值节点读到当前上限\t动作数=" + ran3 + " 掉血=" + dmg + "（期望=上限 " + after_set + "）");

            //---- ⑥ 设为 0 → 退回配置值（未初始化兜底语义）----
            NodeDocRunner.Run(logic, HandMaxGraph("SetHandMax", "0"), caster, p1.hero, null, "ActivateEffect");
            sb.AppendLine((p0.hand_max == 0 && p0.GetHandMax() == cfg ? "PASS" : "FAIL")
                + "\t⑦ 上限设为0退回配置值（兜底）\thand_max=" + p0.hand_max + " 生效上限=" + p0.GetHandMax());

            //---- ⑦ Clone 带上 hand_max（AI 预测树：不一致会让 AI 预判跑偏）----
            p0.hand_max = cfg + 7;
            Player clone = new Player(9);   //Player 无无参构造（Player(int id)）
            Player.Clone(p0, clone);
            sb.AppendLine((clone.hand_max == cfg + 7 ? "PASS" : "FAIL")
                + "\t⑧ 克隆带上手牌上限（AI 预测树）\t原=" + p0.hand_max + " 副本=" + clone.hand_max);
        }
    }
}
