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
            try
            {
                CardData built = CardPoolIO.BuildCardData(d);
                CardPoolData pool = CardPoolIO.BuildPool(new List<CardData> { built }, "probe_skill_pool");
                CardPoolIO.ImportToGame(pool);
                cd = CardData.Get(d.id) != null ? CardData.Get(d.id) : built;
            }
            catch (Exception e) { sb.AppendLine("① 导入抛异常: " + e.Message); }
            sb.AppendLine((cd != null && cd.type == CardType.Skill ? "PASS" : "FAIL")
                + "\t① type=\"Skill\" 编译为 CardType.Skill\t实际=" + (cd != null ? cd.type.ToString() : "null")
                + " GetTypeId=" + (cd != null ? cd.GetTypeId() : "?"));
            if (cd == null)
                return;

            //---- 打出技能卡：效果结算 + 进墓地（法术同款生命周期）----
            Card play = Card.Create(cd, null, p0, "st_play");
            if (play.abilities == null) play.abilities = new List<string>();
            if (play.abilities_ongoing == null) play.abilities_ongoing = new List<string>();
            if (cd.abilities != null)
                foreach (AbilityData a in cd.abilities)
                {
                    if (a == null || string.IsNullOrEmpty(a.id)) continue;
                    if (a.trigger == AbilityTrigger.Ongoing) play.abilities_ongoing.Add(a.id);
                    else play.abilities.Add(a.id);
                }
            p0.cards_hand.Add(play);
            p0.mana_max = 10; p0.mana = 10;
            int before = p1.hp;
            bool threw = false;
            try { logic.PlayCard(play, new Slot(1, 1, p0.player_id), true); }
            catch (Exception e) { threw = true; sb.AppendLine("② PlayCard 抛异常: " + e.Message); }
            int lost = before - p1.hp;
            bool inDiscard = p0.cards_discard.Contains(play);
            bool onBoard = p0.cards_board.Contains(play);
            sb.AppendLine((!threw && lost == 2 ? "PASS" : "FAIL") + "\t② 打出技能卡 → 效果结算（敌方角色分配伤害 2）\t掉血=" + lost
                + "（定义能力数=" + (cd.abilities != null ? cd.abilities.Length : -1) + "）");
            sb.AppendLine((!threw && inDiscard && !onBoard ? "PASS" : "FAIL")
                + "\t③ 打出后进墓地、不进战场（法术同款）\t进墓地=" + inDiscard + " 进战场=" + onBoard);
        }
    }
}
