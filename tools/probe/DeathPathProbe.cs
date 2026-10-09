using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;   //★ GraphEventContext 在这个命名空间

namespace TcgEngine.Probe
{
    /// <summary>【死亡/离场结算：战斗击杀必须与"消灭 / 致死 / 毒"走同一条统一入口】
    /// 断言：
    /// ① 战斗击杀一张角色 → 图事件必须是「死亡时 / 死亡后」（修复前发的是「弃牌时 / 弃牌后」）
    /// ② 战斗击杀 → attacker.kill_count +1（修复前为 0；AI 用 kill_count 估值，所以它不认战斗击杀）
    /// ③ 一次击杀恰好发一次死亡事件链（不许重复、不许漏）
    /// ④ 击杀后死者确实离场（不在战场、已进墓地）
    /// ⑤ 对照：致死(Deathtouch)击杀 —— 它本来就正确，防止被改坏
    /// ⑥ 对照：无来源致死（2 参伤害=毒/燃烧口径）—— 仍发死亡事件、不计击杀数
    /// 触发：建 tools/deathpath_flag.txt → 进 Play → 写 tools/deathpath_result.tsv → 自动删标记。
    /// </summary>
    public class DeathPathProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/deathpath_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/deathpath_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("DeathPathProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<DeathPathProbe>();
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
            Debug.Log("[死亡链] 验证完成 → " + OutPath);
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

        /// <summary>统计事件日志区间 [from, end) 内各类动作的次数</summary>
        private static void CountEvents(GameLogic logic, int from, out int death, out int discard,
            out int before_death, out int after_death, out string names)
        {
            death = 0; discard = 0; before_death = 0; after_death = 0;
            List<GraphEventContext> log = logic.GetEventLog();
            List<string> seen = new List<string>();
            for (int i = from; i < log.Count; i++)
            {
                string a = log[i].action;
                seen.Add(a);
                if (a == "OnBeforeDeath") { death++; before_death++; }
                else if (a == "OnAfterDeath") { death++; after_death++; }
                else if (a == "OnBeforeDiscard" || a == "OnAfterDiscard") discard++;
            }
            names = string.Join(",", seen.ToArray());
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
                if (c.type == CardType.Character) chars.Add(c);
            }
            if (heroData == null || chars.Count < 3)
            {
                sb.AppendLine("SKIP\t缺少英雄 / 至少 3 张角色卡定义\t");
                return;
            }

            Game g = new Game("probe_death", 2);
            g.state = GameState.Play;
            g.current_player = 0;
            GameLogic logic = new GameLogic(g);
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);
            p0.hero = Card.Create(heroData, null, p0, "dp_h0");
            p1.hero = Card.Create(heroData, null, p1, "dp_h1");

            //---- ①②③④ 战斗击杀（4 参伤害 = 战斗/效果伤害路径）----
            Card atk = NewCard(chars[0], p0, "dp_atk");
            Card def = NewCard(chars[1], p1, "dp_def");
            def.hp = 3; def.damage = 0;
            int log0 = logic.GetEventLog().Count;
            int kills0 = p0.kill_count;
            logic.DamageCard(atk, def, 5);

            int death1, discard1, bd1, ad1;
            string names1;
            CountEvents(logic, log0, out death1, out discard1, out bd1, out ad1, out names1);
            Check("① 战斗击杀发的是「死亡时/后」", death1 == 2 && discard1 == 0,
                "死亡事件=" + death1 + "（期望 2） 弃牌事件=" + discard1 + "（期望 0）｜本次事件=" + names1);
            Check("② 战斗击杀计入 kill_count", p0.kill_count == kills0 + 1,
                "kill_count " + kills0 + " → " + p0.kill_count + "（期望 +1）");
            Check("③ 一次击杀 = 一次死亡事件链（不重复）", bd1 == 1 && ad1 == 1,
                "OnBeforeDeath=" + bd1 + "（期望 1） OnAfterDeath=" + ad1 + "（期望 1）");
            Check("④ 死者已离场并进墓地", !g.IsOnBoard(def) && g.IsInDiscard(def),
                "在战场=" + g.IsOnBoard(def) + "（期望 False） 在墓地=" + g.IsInDiscard(def) + "（期望 True）");

            //---- ⑤ 对照：致死（Deathtouch）----
            Card atk2 = NewCard(chars[0], p0, "dp_atk2");
            atk2.AddStatus(StatusType.Deathtouch, 1, 0);
            Card def2 = NewCard(chars[2], p1, "dp_def2");
            def2.hp = 20; def2.damage = 0;
            int log2 = logic.GetEventLog().Count;
            int kills2 = p0.kill_count;
            logic.DamageCard(atk2, def2, 1);
            int death2, discard2, bd2, ad2;
            string names2;
            CountEvents(logic, log2, out death2, out discard2, out bd2, out ad2, out names2);
            Check("⑤ 对照：致死击杀仍正确（发死亡事件 + 计击杀）",
                death2 == 2 && discard2 == 0 && p0.kill_count == kills2 + 1 && !g.IsOnBoard(def2) && g.IsInDiscard(def2),
                "死亡事件=" + death2 + " 弃牌事件=" + discard2 + " kill_count=" + p0.kill_count
                + "（期望 " + (kills2 + 1) + "）｜事件=" + names2);

            //---- ⑥ 对照：无来源致死（2 参伤害 = 毒/燃烧口径）----
            Card def3 = NewCard(chars[2], p1, "dp_def3");
            def3.hp = 1; def3.damage = 0;
            int log3 = logic.GetEventLog().Count;
            int kills3 = p0.kill_count;
            logic.DamageCard(def3, 1);
            int death3, discard3, bd3, ad3;
            string names3;
            CountEvents(logic, log3, out death3, out discard3, out bd3, out ad3, out names3);
            Check("⑥ 对照：无来源致死（毒/燃烧口径）发死亡事件、不计击杀",
                death3 == 2 && discard3 == 0 && p0.kill_count == kills3 && !g.IsOnBoard(def3) && g.IsInDiscard(def3),
                "死亡事件=" + death3 + " 弃牌事件=" + discard3 + " kill_count 不变=" + (p0.kill_count == kills3)
                + "｜事件=" + names3);

            //---- ⑦ 0 血（已判死、但本帧还没走到结算）的卡不再继续吃伤害 ----
            Card atk4 = NewCard(chars[0], p0, "dp_atk4");
            Card def4 = NewCard(chars[1], p1, "dp_def4");
            def4.hp = 0; def4.damage = 0;                 //模拟"HP 被别的来源降到 0，但还没轮到结算"
            int log4 = logic.GetEventLog().Count;
            logic.DamageCard(atk4, def4, 3);
            int dmg4 = def4.damage;
            int death4, discard4, bd4, ad4;
            string names4;
            CountEvents(logic, log4, out death4, out discard4, out bd4, out ad4, out names4);
            Check("⑦ 0 血卡不再继续吃伤害（不浪费伤害、不重复发死亡事件）", dmg4 == 0 && death4 == 0,
                "伤害累加=" + dmg4 + "（期望 0） 死亡事件=" + death4 + "（期望 0）｜事件=" + names4);

            //---- ⑧ DiscardCard 幂等：带 Disappear 的卡不进墓地 ⇒ IsInDiscard 闸门失效，必须另有挡 ----
            Card dis = NewCard(chars[2], p1, "dp_dis");
            dis.AddStatus(StatusType.Disappear, 1, 0);
            logic.DiscardCard(dis);                        //第一次：正常离场
            int log5 = logic.GetEventLog().Count;
            logic.DiscardCard(dis);                        //第二次：应被幂等挡住（否则重复清理 + 重复发事件）
            int death5, discard5, bd5, ad5;
            string names5;
            CountEvents(logic, log5, out death5, out discard5, out bd5, out ad5, out names5);
            Check("⑧ Disappear 卡重复 DiscardCard 被幂等挡住", death5 + discard5 == 0,
                "第二次产生的事件数=" + (death5 + discard5) + "（期望 0） 在墓地=" + g.IsInDiscard(dis)
                + "（Disappear 不进墓地，期望 False）｜事件=" + names5);
        }
    }
}
