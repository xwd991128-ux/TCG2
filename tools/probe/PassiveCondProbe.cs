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
    /// 【被动效果入口「生效条件」验证】用真实导入路径（写池 json → ImportFromFile）建一张卡：
    ///   被动效果入口（亡语/战场）+ 生效条件 ← 布尔常量（先 false 后改 true）
    ///   动作线(out) → 造成伤害 3（目标接「这张卡牌」）
    ///   生效线(enable) → 简单抽牌×1 ；失效线(disable) → 简单抽牌×2
    ///   （抽牌节点无目标依赖，用 p0 手牌数即可稳定观察三条线是否执行）
    /// ① 条件=false：进场扫描不执行生效线（手牌 0、damage 0）；动作线直接执行也被挡
    /// ② 条件=true ：进场扫描执行生效线（手牌 1）；动作线执行（damage 3）
    /// ③ 条件再改回 false：补执行失效线（手牌 3 = 1+2）——失效线**不该**被条件挡
    /// 触发：建 tools/passivecond_flag.txt → 进 Play → 写 tools/passivecond_result.txt → 自动删标记。
    /// </summary>
    public class PassiveCondProbe : MonoBehaviour
    {
        private const string CardId = "probe_passive_cond";

        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/passivecond_result.txt"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/passivecond_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("PassiveCondProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PassiveCondProbe>();
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
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[被动生效条件] 验证完成 → " + OutPath);
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

        private void Run()
        {
            CardData cd = CardData.Get(CardId);
            if (cd == null)
            {
                GraphData g = new GraphData { name = "被动条件探针", nodes = new List<GraphNode>(), links = new List<GraphLink>() };

                GraphNode ev = Node(g, "ev", GraphNodeType.Event, "PassiveEffect", "被动效果入口",
                    "tag_list", "亡语", "live_area", "战场", "priority", "0");
                Pin(ev, "cond", NodeValueType.Boolean, false);
                Pin(ev, "out", NodeValueType.Flow, true);
                Pin(ev, "enable", NodeValueType.Flow, true);
                Pin(ev, "disable", NodeValueType.Flow, true);
                Pin(ev, "player", NodeValueType.Player, true);
                Pin(ev, "card", NodeValueType.Card, true);

                GraphNode c1 = Node(g, "c1", GraphNodeType.Condition, "112001", "布尔常量", "value", "false");
                Pin(c1, "return", NodeValueType.Boolean, true);

                //动作线：造成伤害 3，目标 = 这张卡牌
                GraphNode d3 = Node(g, "d3", GraphNodeType.Action, "202001", "造成伤害", "value", "3");
                Pin(d3, "in", NodeValueType.Flow, false);
                Pin(d3, "out", NodeValueType.Flow, true);
                Pin(d3, "value", NodeValueType.Int32, false);
                Pin(d3, "card", NodeValueType.Card, false);
                GraphNode t1 = Node(g, "t1", GraphNodeType.Value, "102001", "这张卡牌");
                Pin(t1, "return", NodeValueType.Card, true);
                Link(g, t1, "return", d3, "card");

                //生效线：造成伤害 2 → 目标=「获取玩家英雄」（不依赖能力选中目标，落到 p0 血量上便于观察）
                GraphNode e1 = Node(g, "e1", GraphNodeType.Action, "202001", "造成伤害", "value", "2");
                Pin(e1, "in", NodeValueType.Flow, false);
                Pin(e1, "out", NodeValueType.Flow, true);
                Pin(e1, "value", NodeValueType.Int32, false);
                Pin(e1, "card", NodeValueType.Card, false);
                GraphNode h1 = Node(g, "h1", GraphNodeType.Value, "101004", "获取玩家英雄");
                Pin(h1, "player", NodeValueType.Player, false);
                Pin(h1, "return", NodeValueType.Card, true);
                Link(g, h1, "return", e1, "card");

                //失效线：造成伤害 4 → 同上（与生效线数值不同，便于区分）
                GraphNode x1 = Node(g, "x1", GraphNodeType.Action, "202001", "造成伤害", "value", "4");
                Pin(x1, "in", NodeValueType.Flow, false);
                Pin(x1, "out", NodeValueType.Flow, true);
                Pin(x1, "value", NodeValueType.Int32, false);
                Pin(x1, "card", NodeValueType.Card, false);
                Link(g, h1, "return", x1, "card");

                Link(g, c1, "return", ev, "cond");
                Link(g, ev, "out", d3, "in");
                Link(g, ev, "enable", e1, "in");
                Link(g, ev, "disable", x1, "in");

                CardCustomData dto = new CardCustomData();
                dto.id = CardId;
                dto.title = "被动条件探针";
                dto.type = "Character";
                dto.mana = 1;
                dto.attack = 1;
                dto.hp = 30;
                dto.effects = new List<CardEffectData> { new CardEffectData { name = "效果1", graph = g } };

                CardPoolData pool = new CardPoolData();
                pool.name = "probe_passive_cond_pool";
                pool.cards = new List<CardCustomData> { dto };
                string tmp = Path.Combine(Application.persistentDataPath, "Workshop/probe_passive_cond_pool.json");
                File.WriteAllText(tmp, JsonUtility.ToJson(pool), new UTF8Encoding(false));
                //★第二个参数是 grantOwnership：true 会把探针测试卡写进**玩家存档的"已拥有"**（污染构筑界面）；
                //  探针只关心编译/被动生效失效，不需要归属 → false。用完即删，别把测试池留在用户 Workshop 目录。
                CardPoolIO.ImportFromFile(tmp, false);
                try { File.Delete(tmp); } catch { }
                cd = CardData.Get(CardId);
            }

            sb.AppendLine("① 卡定义 = " + (cd == null ? "❌ 未注册" : (cd.id + " hp=" + cd.hp + " 能力数=" + (cd.abilities != null ? cd.abilities.Length : -1))));
            if (cd == null || cd.abilities == null)
                return;
            AbilityData actionAb = null;
            foreach (AbilityData a in cd.abilities)
            {
                if (a == null) continue;
                EffectRunGraph rg = (a.effects != null && a.effects.Length > 0) ? a.effects[0] as EffectRunGraph : null;
                sb.AppendLine("   能力 id=" + a.id + " trigger=" + a.trigger
                    + " entry_pin=" + (rg != null ? rg.entry_pin : "?") + " group=" + a.passive_group);
                if (a.trigger == AbilityTrigger.OnDeath)
                    actionAb = a;    //亡语 = 动作线
            }

            Game game = new Game("probe_passive_cond", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0];
            p0.hp_max = 30; p0.hp = 30; p0.mana_max = 20; p0.mana = 20;
            game.players[1].hp_max = 30; game.players[1].hp = 30;

            // 卡上战场 + 给 p0 一个英雄（生效/失效线的伤害落到英雄→玩家血量，便于观察）
            CardData heroDef = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Hero) { heroDef = c; break; }
            if (heroDef != null)
                p0.hero = Card.Create(heroDef, null, p0, "pc_hero");
            Card card = Card.Create(cd, null, p0, "pc_card");
            card.slot = new Slot(1, 1, p0.player_id);
            p0.cards_board.Add(card);
            GraphData graph = FindGraph(cd, actionAb);

            int hp0 = p0.hp;

            // ① 条件=false：进场扫描不应执行生效线
            logic.SyncPassiveEffects();
            sb.AppendLine((p0.hp == hp0 && card.damage == 0 ? "PASS" : "FAIL")
                + "\t① 条件=false 进场扫描 → 生效线不执行\tp0.hp=" + p0.hp + " damage=" + card.damage
                + " 已生效分组=" + (card.passive_groups != null ? card.passive_groups.Count : -1));

            // ② 条件=false：动作线直接执行也应被挡
            if (actionAb != null)
            {
                actionAb.DoEffects(logic, card, card);
                sb.AppendLine((card.damage == 0 && p0.hp == hp0 ? "PASS" : "FAIL")
                    + "\t② 条件=false 直接执行动作线 → 被挡\tdamage=" + card.damage + " p0.hp=" + p0.hp);
            }
            else sb.AppendLine("FAIL\t② 找不到亡语动作线能力");

            // ③ 条件改 true → 进场扫描应执行生效线（英雄受 2 点 → 玩家掉 2）
            SetCond(graph, "true");
            logic.SyncPassiveEffects();
            sb.AppendLine(((hp0 - p0.hp) == 2 ? "PASS" : "FAIL")
                + "\t③ 条件=true 进场扫描 → 生效线执行(伤害2)\tp0.hp=" + hp0 + "→" + p0.hp
                + " 已生效分组=" + (card.passive_groups != null ? card.passive_groups.Count : -1));

            // ④ 条件=true：动作线（卡自身受 3 点）
            if (actionAb != null)
            {
                actionAb.DoEffects(logic, card, card);
                sb.AppendLine((card.damage == 3 ? "PASS" : "FAIL")
                    + "\t④ 条件=true 执行动作线(+3伤害)\tdamage=" + card.damage);
            }

            // ⑤ 条件改回 false → 补执行失效线（英雄再受 4 点）——失效线不该被条件挡
            SetCond(graph, "false");
            int hp5 = p0.hp;
            logic.SyncPassiveEffects();
            sb.AppendLine(((hp5 - p0.hp) == 4 ? "PASS" : "FAIL")
                + "\t⑤ 条件=false → 补执行失效线(伤害4)\tp0.hp=" + hp5 + "→" + p0.hp
                + " 已生效分组=" + (card.passive_groups != null ? card.passive_groups.Count : -1));

            // [诊断] 逐路径对比：生效/失效线的动作到底在哪条路径上会执行
            AbilityData enAb = null, disAb = null;
            foreach (AbilityData a in card.GetAbilities())
            {
                if (a == null) continue;
                if (a.trigger == AbilityTrigger.OnPassiveEnable) enAb = a;
                if (a.trigger == AbilityTrigger.OnPassiveDisable) disAb = a;
            }
            if (disAb != null)
            {
                int hpe = p0.hp;
                int n3 = NodeDocRunner.Run(logic, graph, card, card, null, "PassiveEffect", ability: disAb, entry_pin: "disable");
                sb.AppendLine("   [诊断] (d) Run(disable, target=card)：动作数=" + n3 + " hp " + hpe + "→" + p0.hp
                    + " 触发条件=" + disAb.AreTriggerConditionsMet(game, card, card));
                int hpf = p0.hp;
                disAb.DoEffects(logic, card);
                sb.AppendLine("   [诊断] (e) DoEffects(disable 1参)：hp " + hpf + "→" + p0.hp);
            }
            if (enAb != null)
            {
                int hpa = p0.hp;
                enAb.DoEffects(logic, card);       //(a) 与 EnablePassive 完全同路
                sb.AppendLine("   [诊断] (a) DoEffects(1参)：hp " + hpa + "→" + p0.hp);

                int hpb = p0.hp;
                NodeDocRunner.Run(logic, graph, card, null, null, "PassiveEffect", ability: enAb,
                    target_slots: logic.GetMultiTargetResults(), entry_pin: "enable");
                sb.AppendLine("   [诊断] (a2) Run(target=null, 带slots)：hp " + hpb + "→" + p0.hp);

                int hpc = p0.hp;
                int n1 = NodeDocRunner.Run(logic, graph, card, null, null, "PassiveEffect", ability: enAb, entry_pin: "enable");
                sb.AppendLine("   [诊断] (b) Run(target=null, 无slots)：动作数=" + n1 + " hp " + hpc + "→" + p0.hp);

                int hpd = p0.hp;
                int n2 = NodeDocRunner.Run(logic, graph, card, card, null, "PassiveEffect", ability: enAb, entry_pin: "enable");
                sb.AppendLine("   [诊断] (c) Run(target=card, 无slots)：动作数=" + n2 + " hp " + hpd + "→" + p0.hp);
            }

        }

        private static GraphData FindGraph(CardData cd, AbilityData ab)
        {
            if (ab != null && ab.effects != null)
                foreach (EffectData e in ab.effects)
                {
                    EffectRunGraph rg = e as EffectRunGraph;
                    if (rg != null && rg.graph != null)
                        return rg.graph;
                }
            return null;
        }

        /// <summary>把入口「生效条件」的布尔常量改成指定值（运行时直接改图，模拟条件真假变化）</summary>
        private static void SetCond(GraphData graph, string value)
        {
            if (graph == null || graph.nodes == null)
                return;
            foreach (GraphNode node in graph.nodes)
            {
                if (node == null || node.action != "112001" || node.fields == null)
                    continue;
                foreach (FieldCustomData f in node.fields)
                    if (f != null && f.name == "value")
                        f.value = value;
            }
        }
    }
}
