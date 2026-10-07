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
    /// 【「使用卡牌后」入口验证】新加的 OnAfterPlay 入口：① 入口能被识别并执行；
    /// ② 真实打出卡牌时 `GameLogic.PlayCard` 末尾会广播 OnAfterPlay（不抛异常、不打断打出）。
    /// 触发：建 tools/afterplay_flag.txt → 进 Play → 写 tools/afterplay_result.txt → 自动删标记。
    /// </summary>
    public class AfterPlayProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/afterplay_result.txt"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/afterplay_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("AfterPlayProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<AfterPlayProbe>();
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
            Debug.Log("[使用卡牌后] 验证完成 → " + OutPath);
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
            Game game = new Game("probe_afterplay", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0], p1 = game.players[1];
            CardData hero = null, minion = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (hero == null && c.type == CardType.Hero) hero = c;
                if (minion == null && c.type == CardType.Character) minion = c;
            }
            if (hero == null || minion == null) { sb.AppendLine("SKIP 缺少英雄/角色定义"); return; }
            p0.hero = Card.Create(hero, null, p0, "ap_p0hero"); p0.hp_max = 30; p0.hp = 30;
            p1.hero = Card.Create(hero, null, p1, "ap_p1hero"); p1.hp_max = 30; p1.hp = 30;

            // ---- ① 入口能识别并执行：OnAfterPlay → 造成伤害 3 ----
            GraphData g = new GraphData { name = "OnAfterPlay验证", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode ev = Node(g, "ev", GraphNodeType.Event, "OnAfterPlay", "使用卡牌后");
            Pin(ev, "out", NodeValueType.Flow, true);
            Pin(ev, "card", NodeValueType.Card, true);
            Pin(ev, "player", NodeValueType.Player, true);
            GraphNode dmg = Node(g, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "3");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(g, ev, "out", dmg, "in");

            Card host = Card.Create(minion, null, p0, "ap_host");
            int before = p1.hp;
            bool ok = true;
            try { NodeDocRunner.Run(logic, g, host, p1.hero, null, "OnAfterPlay"); }
            catch (Exception e) { ok = false; sb.AppendLine("① 入口执行抛异常: " + e.Message); }
            int got = before - p1.hp;
            sb.AppendLine((ok && got == 3 ? "PASS" : "FAIL") + "\t① 入口 OnAfterPlay 可执行 → 伤害 3\t掉血=" + got);

            // ---- ② 真实打出卡牌的路径不被打断（PlayCard 末尾广播 OnAfterPlay）----
            Card play = Card.Create(minion, null, p0, "ap_play");
            if (play.abilities == null) play.abilities = new List<string>();
            if (play.abilities_ongoing == null) play.abilities_ongoing = new List<string>();
            p0.cards_hand.Add(play);
            p0.mana_max = 20; p0.mana = 20;
            int handBefore = p0.cards_hand.Count;
            bool played = false;
            Slot slot = new Slot(1, 1, p0.player_id);   //★槽位坐标从 1 起（x_min=1/y_min=1），用 0 会 IsValid=false
            sb.AppendLine("INFO\t② 前置检查\tCanPlayCard=" + game.CanPlayCard(play, slot, true)
                + " slot.IsValid=" + slot.IsValid() + " 卡类型=" + play.CardData.type
                + " IsBoardCard=" + play.CardData.IsBoardCard() + " 在手上=" + p0.HasCard(p0.cards_hand, play));
            try
            {
                logic.PlayCard(play, slot, true);   //skip_cost=true：跳过费用校验，专注验证"广播不打断打出"
                played = p0.cards_board.Contains(play);
            }
            catch (Exception e)
            {
                sb.AppendLine("② PlayCard 抛异常: " + e.Message);
            }
            sb.AppendLine((played ? "PASS" : "FAIL") + "\t② 打出卡牌流程正常（末尾广播 OnAfterPlay）\t手牌 " + handBefore + "→" + p0.cards_hand.Count
                + "，已进战场=" + played);

            //---- ③ 真实打出路径端到端：**完全复刻用户卡**——OnAfterPlay → 206001 添加增益(攻击+3, 目标=这张卡) ----
            //注册测试增益（mods：攻击/增加属性/3）——与语义探针同款反射配方
            BuffData bd3 = null;
            try
            {
                bd3 = BuffPoolIO.Get("probe_buff_atk3");
                if (bd3 == null)
                {
                    bd3 = BuffPoolIO.New();
                    bd3.title = "探针增益+3攻";
                    bd3.id = "probe_buff_atk3";
                    bd3.mods = new List<BuffPropMod> { new BuffPropMod(BuffModTarget.Attack, BuffModMode.Add, 3) };
                    bd3.SyncLegacyProps();
                    System.Reflection.FieldInfo fi3 = typeof(BuffPoolIO).GetField("buff_dict", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    object dict3 = fi3 != null ? fi3.GetValue(null) : null;
                    System.Reflection.MethodInfo set3 = dict3 != null ? dict3.GetType().GetMethod("set_Item") : null;
                    if (set3 != null) set3.Invoke(dict3, new object[] { bd3.id, bd3 });
                }
            }
            catch (Exception e_b) { sb.AppendLine("③ 注册测试增益失败: " + e_b.Message); }

            GraphData g3 = new GraphData { name = "打出后效果", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode ev3 = Node(g3, "ev", GraphNodeType.Event, "OnAfterPlay", "使用卡牌后");
            Pin(ev3, "out", NodeValueType.Flow, true);
            Pin(ev3, "card", NodeValueType.Card, true);
            Pin(ev3, "player", NodeValueType.Player, true);
            GraphNode buff = Node(g3, "b", GraphNodeType.Action, "206001", "添加增益", "buffDefine", bd3 != null ? bd3.id : "");
            Pin(buff, "in", NodeValueType.Flow, false);
            Pin(buff, "out", NodeValueType.Flow, true);
            Pin(buff, "cards", NodeValueType.Card, false);
            Link(g3, ev3, "out", buff, "in");
            Link(g3, ev3, "card", buff, "cards");   //目标=这张卡（与用户图同款）

            CardCustomData d3c = new CardCustomData();
            d3c.id = "probe_onafterplay_minion";
            d3c.title = "探针打出后";
            d3c.type = "Character";
            d3c.mana = 1;
            d3c.attack = 1; d3c.hp = 3;
            d3c.effects = new List<CardEffectData> { new CardEffectData { name = "效果1", graph = g3 } };
            try
            {
                //★走**真实文件导入路径**（写临时池 json → ImportFromFile）：
                //  先 BuildCardData 再 ImportToGame 会**双重编译**，且 CardData→DTO 往返会丢图（探针伪影 ✗）
                CardPoolData pd3 = new CardPoolData();
                pd3.name = "probe_onafterplay_pool";
                pd3.cards = new List<CardCustomData> { d3c };
                string tmp3 = Path.Combine(Application.persistentDataPath, "Workshop/probe_onafterplay_pool.json");
                File.WriteAllText(tmp3, JsonUtility.ToJson(pd3), new UTF8Encoding(false));
                //★第二个参数是 grantOwnership（不是"激活"）：传 true 会把探针测试卡写进**玩家存档的"已拥有"**，
                //  之后测试卡会出现在构筑界面里（污染用户存档）；探针只关心编译/结算，不需要归属 → 传 false。
                CardPoolIO.ImportFromFile(tmp3, false);
                //★用完即删：否则测试池 json 会留在用户的 Workshop 目录里（会在工作台卡池列表里多出一个 "probe_..."）。
                try { File.Delete(tmp3); } catch { }
                CardData cd3 = CardData.Get(d3c.id);
                if (cd3 != null && cd3.abilities != null)
                    foreach (AbilityData a in cd3.abilities)
                        sb.AppendLine("INFO\t③ 编译产物能力\tid=" + (a != null ? a.id : "null")
                            + " trigger=" + (a != null ? a.trigger.ToString() : "?")
                            + " effects=" + (a != null && a.effects != null ? a.effects.Length : -1));
                Card c3 = Card.Create(cd3, null, p0, "ap_oap_minion");
                //★SetCard 已从定义拷贝能力列表 → 只有为空才补（重复灌会造成同 id 两条 → 图执行两遍）
                if (c3.abilities == null) c3.abilities = new List<string>();
                if (c3.abilities_ongoing == null) c3.abilities_ongoing = new List<string>();
                if (c3.abilities.Count == 0 && c3.abilities_ongoing.Count == 0 && cd3.abilities != null)
                    foreach (AbilityData a in cd3.abilities)
                    {
                        if (a == null || string.IsNullOrEmpty(a.id)) continue;
                        if (a.trigger == AbilityTrigger.Ongoing) c3.abilities_ongoing.Add(a.id);
                        else c3.abilities.Add(a.id);
                    }
                p0.cards_hand.Add(c3);
                p0.mana = 20; p0.mana_max = 20;
                int b3hp = p1.hp;
                int handB3 = p0.cards_hand.Count;
                logic.PlayCard(c3, new Slot(2, 1, p0.player_id), true);
                bool onBoard3 = p0.cards_board.Contains(c3);
                int atk_after = c3.GetAttack();
                bool hasB = BuffRuntime.HasBuff(c3, "probe_buff_atk3");
                sb.AppendLine((atk_after == 4 ? "PASS" : "FAIL")
                    + "\t③ 真实打出 → OnAfterPlay 唤起图 → 添加增益(攻击+3)\t攻击=" + atk_after + "（基础1 期望4）"
                    + " HasBuff=" + hasB
                    + " AddAttack状态=" + c3.GetStatusValue(StatusType.AddAttack)
                    + " 进战场=" + onBoard3 + " 卡实例能力数=" + (c3.abilities != null ? c3.abilities.Count : -1));
            }
            catch (Exception e3) { sb.AppendLine("③ 异常: " + e3.Message); }
        }
    }
}
