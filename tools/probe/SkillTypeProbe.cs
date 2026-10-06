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
    /// 【技能卡牌类型验证】新增的 CardType.Skill：
    /// ① BuildCardData 能把 type="Skill" 编译成 CardType.Skill；
    /// ② 打出技能卡 = 法术同款生命周期（结算效果 → 进墓地，不进战场）；
    /// ③ 类型判断（中文名"技能"）能识别。
    /// 触发：建 tools/skilltype_flag.txt → 进 Play → 写 tools/skilltype_result.txt → 自动删标记。
    /// </summary>
    public class SkillTypeProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/skilltype_result.txt"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/skilltype_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("SkillTypeProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<SkillTypeProbe>();
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
            Debug.Log("[技能类型] 验证完成 → " + OutPath);
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

        /// <summary>[诊断] 输出"卡定义第 1 个能力的效果明细"：效果数量 / 类型 / EffectRunGraph 的图与触发名</summary>
        private static string GraphInfo(CardData c)
        {
            if (c == null)
                return "卡定义=null";
            if (c.abilities == null || c.abilities.Length == 0)
                return "能力=0";
            AbilityData a0 = c.abilities[0];
            if (a0 == null)
                return "能力[0]=null";
            EffectData[] ef = a0.effects;
            if (ef == null)
                return "能力=" + a0.id + " effects=null";
            StringBuilder s = new StringBuilder("能力=" + a0.id + " trigger=" + a0.trigger + " effects=" + ef.Length);
            for (int i = 0; i < ef.Length; i++)
            {
                if (ef[i] == null) { s.Append(" [null]"); continue; }
                EffectRunGraph rg = ef[i] as EffectRunGraph;
                s.Append(" [" + ef[i].GetType().Name);
                if (rg != null)
                    s.Append(" graph=" + (rg.graph == null ? "★null" : "有(" + (rg.graph.nodes != null ? rg.graph.nodes.Count.ToString() : "?") + "节点)")
                        + " trigger=" + (rg.trigger_action == null ? "null" : rg.trigger_action));
                s.Append("]");
            }
            return s.ToString();
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
            Game game = new Game("probe_skill", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0], p1 = game.players[1];
            CardData hero = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Hero) { hero = c; break; }
            if (hero == null) { sb.AppendLine("SKIP 缺少英雄定义"); return; }
            p0.hero = Card.Create(hero, null, p0, "st_p0hero"); p0.hp_max = 30; p0.hp = 30;
            p1.hero = Card.Create(hero, null, p1, "st_p1hero"); p1.hp_max = 30; p1.hp = 30;

            //---- 组一张 type="Skill" 的卡：主动效果入口 → 造成伤害 2 ----
            GraphData g = new GraphData { name = "技能效果", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode ev = Node(g, "ev", GraphNodeType.Event, "ActivateEffect", "主动效果入口");
            Pin(ev, "out", NodeValueType.Flow, true);
            Pin(ev, "card", NodeValueType.Card, true);
            GraphNode chars = Node(g, "t", GraphNodeType.Value, "102017", "获取敌方角色");
            Pin(chars, "return", NodeValueType.Array, true);
            GraphNode dmg = Node(g, "d", GraphNodeType.Action, "202012", "分配固定法术伤害", "damage", "2");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "targets", NodeValueType.Array, false);
            Link(g, ev, "out", dmg, "in");
            Link(g, chars, "return", dmg, "targets");

            CardCustomData d = new CardCustomData();
            d.id = "probe_skill_card";
            d.title = "探针技能卡";
            d.type = "Skill";
            d.mana = 1;
            d.deckbuilding = true;
            d.effects = new List<CardEffectData> { new CardEffectData { name = "效果1", graph = g } };

            //★走**真实导入路径**（BuildPool → ImportToGame → RegisterCard）：
            //  只调 BuildCardData 不会把能力注册进运行期注册表 → AbilityData.Get(id) 解析不到 → 打出不触发（探针夹具坑）
            CardData cd = null;
            //★built 必须声明在 try 外：下面的诊断行要用它。
            //  （之前写在 try 内 → CS0103 → **整包编译失败 → Unity 拒绝进 Play**，而不是什么"插件卡住"。）
            CardData built = null;
            try
            {
                built = CardPoolIO.BuildCardData(d);
                CardPoolData pool = CardPoolIO.BuildPool(new List<CardData> { built }, "probe_skill_pool");
                CardPoolIO.ImportToGame(pool);
                cd = CardData.Get(d.id) != null ? CardData.Get(d.id) : built;
            }
            catch (Exception e) { sb.AppendLine("① 导入抛异常: " + e.Message); }
            sb.AppendLine((cd != null && cd.type == CardType.Skill ? "PASS" : "FAIL")
                + "\t① type=\"Skill\" 编译为 CardType.Skill\t实际=" + (cd != null ? cd.type.ToString() : "null")
                + " GetTypeId=" + (cd != null ? cd.GetTypeId() : "?") + " [v2]");
            // [诊断] 关键：对比"刚编译出的实例"与"导入后取回的实例"——效果/图是否被导入流程丢掉
            sb.AppendLine("   [诊断] 编译实例: " + GraphInfo(built));
            sb.AppendLine("   [诊断] 导入实例: " + GraphInfo(cd));
            // [诊断] 列出这张卡定义上的**全部**能力（定位"同一效果被编译两次"）
            if (cd.abilities != null)
            {
                for (int i = 0; i < cd.abilities.Length; i++)
                {
                    AbilityData ab_i = cd.abilities[i];
                    sb.AppendLine("   [诊断] 定义能力[" + i + "]=" + (ab_i == null ? "null" : ab_i.id)
                        + " trigger=" + (ab_i == null ? "?" : ab_i.trigger.ToString())
                        + " effects=" + (ab_i == null || ab_i.effects == null ? -1 : ab_i.effects.Length));
                }
            }
            if (cd == null)
                return;

            //---- 打出技能卡：效果结算 + 进墓地（法术同款生命周期）----
            Card play = Card.Create(cd, null, p0, "st_play");
            //★不要再手动 Add：Card.Create → SetAbilities(定义) 已经把定义上的能力填进 play.abilities，
            //  再手动加一遍就是**同一个能力被列两次** → 打出时结算两次（实测：2 点伤害变成 4 点，白查一轮）。
            if (play.abilities == null) play.abilities = new List<string>();
            if (play.abilities_ongoing == null) play.abilities_ongoing = new List<string>();
            p0.cards_hand.Add(play);
            p0.mana_max = 10; p0.mana = 10;
            int before = p1.hp;
            bool threw = false;
            try { logic.PlayCard(play, new Slot(1, 1, p0.player_id), true); }
            catch (Exception e) { threw = true; sb.AppendLine("② PlayCard 抛异常: " + e.Message); }
            //★能力是**入队异步结算**的：TriggerCardAbility → resolve_queue.AddAbility，PlayCard 末尾只
            //  ResolveAll(0.3f) 设了延迟；必须推进队列（GameLogic.Update）才会真正结算。
            //  此前探针在 PlayCard 同帧就读血量 → 必然"掉血=0"（探针自身时序 bug，引擎无问题）。
            for (int i = 0; i < 6; i++)
                logic.Update(0.25f);
            int lost = before - p1.hp;
            bool inDiscard = p0.cards_discard.Contains(play);
            bool onBoard = p0.cards_board.Contains(play);
            sb.AppendLine((!threw && lost == 2 ? "PASS" : "FAIL") + "\t② 打出技能卡 → 效果结算（敌方角色分配伤害 2）\t掉血=" + lost
                + "（定义能力数=" + (cd.abilities != null ? cd.abilities.Length : -1) + "）");

            // [诊断] 隔离定位：① 触发→队列 路径（上面已推过队列）② 直接调能力 DoEffects（跳过触发层）
            //          ③ 能力自身的目标模式 / 结算队列状态
            int hpd = p1.hp;
            try { cd.abilities[0].DoEffects(logic, play); }
            catch (Exception e) { sb.AppendLine("   [诊断] DoEffects 抛异常: " + e.Message); }
            sb.AppendLine("   [诊断] 直接 DoEffects：p1.hp " + hpd + "→" + p1.hp
                + "｜能力 trigger=" + cd.abilities[0].trigger + " target=" + cd.abilities[0].target
                + " id=" + cd.abilities[0].id
                + "｜手牌=" + p0.cards_hand.Contains(play) + " 墓地=" + p0.cards_discard.Contains(play)
                + "｜IsResolving=" + logic.IsResolving()
                + "｜p1.hero=" + (p1.hero != null ? p1.hero.uid : "null")
                + " p1.场上卡数=" + p1.cards_board.Count);

            // [诊断] 直接跑图（不走能力/EffectRunGraph）：返回值=实际执行的动件数
            //   0 → 入口没匹配上（图/入口层问题）；>0 → 入口匹配了但伤害没落地（节点/输入层问题）
            int ran = -1;
            try { ran = NodeDocRunner.Run(logic, g, play, p1.hero, p1, "ActivateEffect"); }
            catch (Exception e) { sb.AppendLine("   [诊断] 直接 Run 抛异常: " + e.Message); }
            sb.AppendLine("   [诊断] 直接 Run(trigger=ActivateEffect)：执行动作数=" + ran + " → p1.hp=" + p1.hp);

            // [诊断] 复刻 EffectRunGraph.DoEffect(logic, ability, caster) 的调用参数，逐项对比
            //   —— 直接 Run 能出伤害、走效果却不行，差别只可能在 图实例 / trigger_action / target_slots
            string eff2 = "(无 EffectRunGraph 效果)";
            if (cd.abilities[0].effects != null && cd.abilities[0].effects.Length > 0
                && cd.abilities[0].effects[0] is EffectRunGraph)
            {
                EffectRunGraph rg0 = (EffectRunGraph)cd.abilities[0].effects[0];
                var slots0 = logic.GetMultiTargetResults();
                eff2 = "图同一=" + object.ReferenceEquals(rg0.graph, g)
                    + " trigger_action=" + (rg0.trigger_action == null ? "null" : rg0.trigger_action)
                    + " entry_pin=" + (string.IsNullOrEmpty(rg0.entry_pin) ? "空" : rg0.entry_pin)
                    + " slots=" + (slots0 == null ? "null" : slots0.Count.ToString());
                int ran2 = NodeDocRunner.Run(logic, rg0.graph, play, null, null, rg0.trigger_action,
                    ability: cd.abilities[0], target_slots: slots0, entry_pin: rg0.entry_pin);
                eff2 += " 复刻执行动作数=" + ran2 + " hp=" + p1.hp;
                // 体检能力里那份图：入口引脚/连线是否在导入（JSON 往返）后仍然完整
                GraphData gg = rg0.graph;
                if (gg != null)
                {
                    GraphNode evn = gg.GetNode("ev");
                    GraphNode dnn = gg.GetNode("d");
                    eff2 += "｜体检: 节点=" + (gg.nodes != null ? gg.nodes.Count : -1)
                        + " 连线=" + (gg.links != null ? gg.links.Count : -1)
                        + " 入口节点=" + (evn != null) + " 入口pins=" + (evn != null && evn.pins != null ? evn.pins.Count : -1)
                        + " 入口category=" + (evn != null ? (evn.category == null ? "null" : evn.category) : "-")
                        + " 伤害节点=" + (dnn != null) + " 伤害pins=" + (dnn != null && dnn.pins != null ? dnn.pins.Count : -1);
                    if (gg.links != null && gg.links.Count > 0)
                    {
                        GraphLink l0 = gg.links[0];
                        eff2 += " 连线0=" + l0.from_node + ":" + l0.from_pin + "→" + l0.to_node + ":" + l0.to_pin
                            + " fromPin存在=" + (gg.GetPin(l0.from_node, l0.from_pin) != null);
                    }
                }
            }
            sb.AppendLine("   [诊断] 复刻效果调用：" + eff2);
            sb.AppendLine((!threw && inDiscard && !onBoard ? "PASS" : "FAIL")
                + "\t③ 打出后进墓地、不进战场（法术同款）\t进墓地=" + inDiscard + " 进战场=" + onBoard);
        }
    }
}
