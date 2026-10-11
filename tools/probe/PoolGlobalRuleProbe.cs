using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【验证：卡池里的"攻击限制"是否对全场生效（与这张卡是否在牌组/场上无关）】
    /// 要回答的问题：某张池卡配了「攻击时 → 攻击限制」，这张卡**没进牌组、没上场**，
    ///   为什么全场（含 AI）都打不了英雄？
    /// 本探针不依赖任何对局：
    ///   A 打印运行时全局攻击规则表（GlobalAttackGraphs）的条数；
    ///   B 打印每张全局规则图的归属卡（用 CardData.attack_graph 反查）+ 入口的 scope/zones/reject_text/limit 接线；
    ///   C 拿**内置卡**（wolf_stalker/bear/tree_angry，不在任何卡池里）当攻击者，
    ///     用**同一个 API**（NodeDocRunner.IsEntryPortMet）分别问"打英雄卡"和"打随从卡"是否放行。
    /// 触发：建 tools/poolrule_flag.txt → 进 Play → 写 tools/poolrule_result.tsv → 自动删标记。
    /// </summary>
    public class PoolGlobalRuleProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/poolrule_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/poolrule_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("PoolGlobalRuleProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PoolGlobalRuleProbe>();
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
            if (frames < 150)
                return;
            done = true;
            try { Run(); }
            catch (Exception e) { sb.AppendLine("EXCEPTION\t" + e.Message + "\t" + e.StackTrace); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[卡池全局规则] 验证完成 → " + OutPath);
        }

        private void Run()
        {
            sb.AppendLine("== A 运行时全局攻击规则表 ==");
            sb.AppendLine("GlobalAttackGraphs.Count\t" + CardPoolIO.GlobalAttackGraphs.Count);
            sb.AppendLine("GlobalDefendGraphs.Count\t" + CardPoolIO.GlobalDefendGraphs.Count);
            List<CardData> all = CardData.GetAll();
            sb.AppendLine("已注册卡总数\t" + (all != null ? all.Count : -1));

            sb.AppendLine("== B 每张全局攻击规则图 → 归属卡 & 入口字段 ==");
            for (int gi = 0; gi < CardPoolIO.GlobalAttackGraphs.Count; gi++)
            {
                GraphData g = CardPoolIO.GlobalAttackGraphs[gi];
                if (g == null) { sb.AppendLine("B[" + gi + "]\tnull"); continue; }
                string owner = "(反查不到)";
                string otitle = "";
                if (all != null)
                {
                    foreach (CardData c in all)
                    {
                        if (c != null && c.attack_graph == g) { owner = c.id; otitle = c.title; break; }
                    }
                }
                sb.AppendLine("B[" + gi + "]\t图名=" + g.name + "\t归属卡=" + owner + "\t" + otitle);
                if (g.nodes == null)
                    continue;
                foreach (GraphNode n in g.nodes)
                {
                    if (n == null || n.type != GraphNodeType.Event)
                        continue;
                    if (n.action != "OnAttack" && n.action != "OnBeforeDefend")
                        continue;
                    GraphPin lp = g.GetPinByName(n.id, "limit");
                    bool wired = lp != null && g.GetIncomingLink(n.id, lp.id) != null;
                    sb.AppendLine("B[" + gi + "].入口\t" + n.action
                        + "\tscope字段=" + GraphRuntime.GetFieldString(n, "scope", "(缺字段)")
                        + "\tzones=" + GraphRuntime.GetFieldString(n, "zones", "(缺)")
                        + "\treject_text=" + GraphRuntime.GetFieldString(n, "reject_text", "(缺)")
                        + "\tlimit接线=" + wired);
                }
            }

            sb.AppendLine("== C 用内置卡（不在任何卡池/不在这局）试打 ==");
            CardData heroCD = null;
            if (all != null)
            {
                foreach (CardData c in all)
                {
                    if (c != null && c.type == CardType.Hero && c.deckbuilding) { heroCD = c; break; }
                }
            }
            CardData minionCD = CardData.Get("bear");
            sb.AppendLine("C.夹具\thero卡=" + (heroCD != null ? heroCD.id : "(找不到英雄卡)")
                + "\t随从卡=" + (minionCD != null ? minionCD.id : "(找不到bear)"));

            string[] attackers = { "wolf_stalker", "bear", "tree_angry" };
            foreach (string aid in attackers)
            {
                CardData acd = CardData.Get(aid);
                if (acd == null) { sb.AppendLine("C\t" + aid + "\t(卡不存在)"); continue; }
                Card atk;
                try { atk = Card.Create(acd, null, new Player(0), "probe_atk_" + aid); }
                catch (Exception e) { sb.AppendLine("C\t" + aid + "\t建卡失败：" + e.Message); continue; }

                string vsHero = AskAll(atk, MakeCard(heroCD, 1, "probe_hero"), null);
                string vsMinion = AskAll(atk, MakeCard(minionCD, 1, "probe_minion"), null);
                sb.AppendLine("C\t攻击者=" + aid + "\t打英雄(允许?)=" + vsHero + "\t打随从(允许?)=" + vsMinion);
            }
        }

        private static Card MakeCard(CardData cd, int pid, string uid)
        {
            if (cd == null)
                return null;
            try { return Card.Create(cd, null, new Player(pid), uid); }
            catch { return null; }
        }

        /// <summary>按引擎自己的写法问一遍：所有全局攻击规则图 + 攻击者自己的 attack_graph。
        /// 全真才放行（与 Game.CanAttackTargetByRules 同口径）。</summary>
        private static string AskAll(Card attacker, Card target, Player target_player)
        {
            try
            {
                foreach (GraphData g in CardPoolIO.GlobalAttackGraphs)
                {
                    if (g == null)
                        continue;
                    if (!NodeDocRunner.IsEntryPortMet(null, g, "OnAttack", "limit", attacker, target, target_player))
                        return "否(被拒)";
                }
                if (attacker != null && attacker.CardData != null && attacker.CardData.attack_graph != null)
                {
                    if (!NodeDocRunner.IsEntryPortMet(null, attacker.CardData.attack_graph, "OnAttack", "limit",
                            attacker, target, target_player))
                        return "否(被拒-本卡图)";
                }
                return "是(放行)";
            }
            catch (Exception e)
            {
                return "EX：" + e.GetType().Name + " " + e.Message;
            }
        }
    }
}
