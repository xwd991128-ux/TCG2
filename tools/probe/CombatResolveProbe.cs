using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【战斗结算验证：践踏溢出 / 吸血上限 / 伤害重定向后的新目标防御】
    /// ① 践踏(Trample)溢出伤害必须走 DamagePlayer 统一入口 → 英雄**免疫/护甲**能挡住它
    ///    （旧写法 `tplayer.hp -= extra` 裸写：绕过护甲/免疫、不广播图事件、不写对战记录）
    /// ② 吸血(LifeSteal)是治疗 → 不得超过 hp_max（卡伤害路径 + 打脸路径两条）
    /// ③ 图上「208005 更改受伤卡牌」把伤害重定向到**新目标**后，新目标的 圣盾/护甲 必须重新生效
    ///    （旧写法只换 target 就继续结算：新目标的圣盾/免疫/护甲被整体跳过）
    /// ④ 对照：主目标自己的 圣盾/护甲 行为不变（证明本次改动没破坏原路径）
    /// 触发：建 tools/combat_flag.txt → 进 Play → 写 tools/combat_result.tsv → 自动删标记。
    /// </summary>
    public class CombatResolveProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/combat_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/combat_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("CombatResolveProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<CombatResolveProbe>();
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
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体\t" + e.Message + "\t" + e.StackTrace); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[战斗结算] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private static Card NewCard(CardData cd, Player p, string uid)
        {
            Card c = Card.Create(cd, null, p, uid);
            p.cards_board.Add(c);
            return c;
        }

        // ---------------- 全局入口夹具：伤害时 → 208005(更改受伤卡牌) ← 事件.source ----------------

        private static List<CardPoolIO.GlobalEntry> saved_entries;
        private static CardPoolIO.GlobalEntry added_entry;

        private static GraphNode Node(GraphData g, string id, GraphNodeType type, string action, string title)
        {
            var n = new GraphNode();
            n.id = id; n.type = type; n.action = action; n.title = title;
            n.pins = new List<GraphPin>();
            n.fields = new List<FieldCustomData>();
            n.category = type == GraphNodeType.Event ? "事件" : "伤害";
            g.nodes.Add(n);
            return n;
        }

        private static void Pin(GraphNode n, string name, NodeValueType type, bool output)
        {
            n.pins.Add(new GraphPin
            {
                id = n.id + "_" + name, name = name, display_name = name,
                is_output = output, type = type
            });
        }

        private static void Link(GraphData g, GraphNode from, string fromPin, GraphNode to, string toPin)
        {
            g.links.Add(new GraphLink
            {
                from_node = from.id, from_pin = from.id + "_" + fromPin,
                to_node = to.id, to_pin = to.id + "_" + toPin
            });
        }

        /// <summary>「伤害时」图：把"受伤卡牌"改成**伤害来源**（=玩家口径的"反弹伤害给攻击者"）。
        /// 这样一条既真实存在、又不需要任何卡池夹具的重定向图。</summary>
        private static GraphData RedirectGraph()
        {
            GraphData g = new GraphData
            {
                name = "探针伤害重定向图",
                nodes = new List<GraphNode>(),
                links = new List<GraphLink>()
            };
            GraphNode ev = Node(g, "ev", GraphNodeType.Event, "OnBeforeDamage", "伤害时");
            Pin(ev, "out", NodeValueType.Flow, true);
            Pin(ev, "source", NodeValueType.Card, true);      //事件来源卡（=伤害来源）
            GraphNode act = Node(g, "a", GraphNodeType.Action, "208005", "更改受伤卡牌");
            Pin(act, "in", NodeValueType.Flow, false);
            Pin(act, "target", NodeValueType.Card, false);
            Pin(act, "out", NodeValueType.Flow, true);
            Link(g, ev, "out", act, "in");
            Link(g, ev, "source", act, "target");             //source → 208005.target
            return g;
        }

        private static void RegisterRedirectGlobal()
        {
            List<CardPoolIO.GlobalEntry> list;
            if (!CardPoolIO.GlobalEntriesByTrigger.TryGetValue(AbilityTrigger.OnBeforeDamage, out list) || list == null)
            {
                list = new List<CardPoolIO.GlobalEntry>();
                CardPoolIO.GlobalEntriesByTrigger[AbilityTrigger.OnBeforeDamage] = list;
                saved_entries = null;      //原本没有这个键 → 结束后整个移除
            }
            else
            {
                saved_entries = new List<CardPoolIO.GlobalEntry>(list);
            }
            added_entry = new CardPoolIO.GlobalEntry { graph = RedirectGraph(), action = "OnBeforeDamage" };
            list.Add(added_entry);
        }

        private static void UnregisterRedirectGlobal()
        {
            List<CardPoolIO.GlobalEntry> list;
            if (!CardPoolIO.GlobalEntriesByTrigger.TryGetValue(AbilityTrigger.OnBeforeDamage, out list) || list == null)
                return;
            if (saved_entries == null)
                CardPoolIO.GlobalEntriesByTrigger.Remove(AbilityTrigger.OnBeforeDamage);
            else
            {
                list.Clear();
                list.AddRange(saved_entries);
            }
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            List<CardData> chars = new List<CardData>();
            CardData heroData = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (heroData == null && c.type == CardType.Hero) heroData = c;
                if (c.type == CardType.Character && !string.IsNullOrEmpty(c.title)) chars.Add(c);
            }
            if (heroData == null || chars.Count < 3)
            {
                sb.AppendLine("SKIP\t缺少英雄/至少 3 张角色卡定义\t");
                return;
            }

            Game game = new Game("probe_combat", 2);
            game.state = GameState.Play;
            game.current_player = 0;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0], p1 = game.players[1];
            p0.hero = Card.Create(heroData, null, p0, "cb_h0");
            p1.hero = Card.Create(heroData, null, p1, "cb_h1");
            p0.hp_max = 30; p0.hp = 30;
            p1.hp_max = 30; p1.hp = 30;

            try
            {
                //---- ① 践踏溢出：英雄 免疫 / 护甲 必须能挡住溢出伤害 ----
                Card atk = NewCard(chars[0], p0, "cb_tr_atk");
                Card def = NewCard(chars[1], p1, "cb_tr_def");
                atk.AddStatus(StatusType.Trample, 1, 0);
                def.hp = 2; def.damage = 0;

                p1.hero.AddStatus(StatusType.Immunity, 1, 0);
                int hp_before = p1.hp;
                logic.DamageCard(atk, def, 5);
                int hp_imm = p1.hp;
                bool imm_ok = hp_imm == hp_before;
                p1.hero.RemoveStatus(StatusType.Immunity);

                def.damage = 0;                       //重置（5 伤害已把 2 血随从打穿 → 不清就会把 extra 算成 8）
                p1.hero.AddStatus(StatusType.Armor, 5, 0);
                int hp2 = p1.hp;
                logic.DamageCard(atk, def, 5);
                int hp_armor = p1.hp;
                bool armor_ok = hp_armor == hp2;
                Check("① 践踏溢出走统一入口（英雄免疫/护甲能挡）", imm_ok && armor_ok,
                    "英雄免疫时 30→" + hp_imm + "（期望不变） 英雄护甲5时 30→" + hp_armor + "（期望不变，溢出=3）");

                //---- ② 吸血不超过 hp_max（卡伤害路径 + 打脸路径）----
                Card ls_atk = NewCard(chars[2], p0, "cb_ls_atk");
                Card ls_def = NewCard(chars[1], p1, "cb_ls_def");
                ls_atk.AddStatus(StatusType.LifeSteal, 1, 0);
                ls_def.hp = 10; ls_def.damage = 0;
                p0.hp = 30;
                logic.DamageCard(ls_atk, ls_def, 3);
                int hp_after_card = p0.hp;
                p0.hp = 30;
                logic.DamagePlayer(ls_atk, p1, 3);
                int hp_after_face = p0.hp;
                Check("② 吸血不超过 hp_max（卡伤害/打脸两条路）", hp_after_card <= p0.hp_max && hp_after_face <= p0.hp_max,
                    "满血30 卡伤害吸血后=" + hp_after_card + " 打脸吸血后=" + hp_after_face + "（期望都不超过 " + p0.hp_max + "）");

                //---- ③ 伤害重定向到新目标后，新目标自己的 圣盾/护甲 必须重新生效 ----
                RegisterRedirectGlobal();
                Card src = NewCard(chars[0], p0, "cb_rd_src");
                Card victim = NewCard(chars[1], p1, "cb_rd_victim");
                src.AddStatus(StatusType.Shell, 1, 0);      //重定向后的新目标 = 来源卡（带圣盾）
                victim.hp = 20; victim.damage = 0;
                int src_hp0 = src.GetHP();
                logic.DamageCard(src, victim, 4);
                int src_hp_shell = src.GetHP();
                bool shell_ok = src_hp_shell == src_hp0 && !src.HasStatus(StatusType.Shell) && victim.GetHP() == 20;

                Card victim2 = NewCard(chars[1], p1, "cb_rd_victim2");   //护甲用例换一套卡，避免被上一次改造污染
                Card src2 = NewCard(chars[2], p0, "cb_rd_src2");
                src2.hp = 20; src2.damage = 0;
                src2.AddStatus(StatusType.Armor, 3, 0);      //新目标护甲 3 → 4 点伤害应只吃 1
                victim2.hp = 20; victim2.damage = 0;
                int src2_hp0 = src2.GetHP();
                logic.DamageCard(src2, victim2, 4);
                int src_taken = src2_hp0 - src2.GetHP();
                bool rd_armor_ok = src_taken == 1;
                Check("③ 重定向后新目标防御重新生效", shell_ok && rd_armor_ok,
                    "圣盾：新目标HP " + src_hp0 + "→" + src_hp_shell + " 圣盾consumed=" + !src.HasStatus(StatusType.Shell)
                    + " 原目标未受伤=" + (victim.GetHP() == 20)
                    + "；护甲3：新目标实吃=" + src_taken + "（期望 1，原目标未受伤=" + (victim2.GetHP() == 20) + "）");
                UnregisterRedirectGlobal();     //★必须先撤掉：否则后面的对照用例的伤害也会被它重定向

                //---- ④ 对照：主目标自己的 圣盾/护甲 行为不变（用全新卡，避免上一次战斗的改造残留）----
                Card atk3 = NewCard(chars[0], p0, "cb_con_atk");
                Card def3 = NewCard(chars[1], p1, "cb_con_def");
                def3.hp = 20; def3.damage = 0;
                def3.AddStatus(StatusType.Shell, 1, 0);
                logic.DamageCard(atk3, def3, 4);
                bool con_shell = def3.GetHP() == 20 && !def3.HasStatus(StatusType.Shell);

                Card def4 = NewCard(chars[2], p1, "cb_con_def2");    //护甲用例独立卡
                def4.hp = 20; def4.damage = 0;
                def4.AddStatus(StatusType.Armor, 2, 0);
                logic.DamageCard(atk3, def4, 4);
                int con_hp = def4.GetHP();
                Check("④ 对照：主目标圣盾/护甲行为不变", con_shell && con_hp == 18,
                    "圣盾吃掉首击=" + con_shell + "（HP 仍 20） 护甲2 吃 4 点 → HP=" + con_hp + "（期望 18）");
            }
            finally
            {
                UnregisterRedirectGlobal();
            }
        }
    }
}
