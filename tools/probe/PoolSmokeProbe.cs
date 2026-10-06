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
    /// 【全卡池端到端冒烟】把**卡池里每张卡的每一张规则图**都真跑一遍（引擎自己的执行入口 NodeDocRunner.Run），
    /// 记录：抛异常的图 / 找不到入口节点的图 / 执行过程中打出的告警（取值为空、目标数=0 …）。
    /// 用途：批量发现"图看着对、一跑就废"的卡 —— 这是逐节点用例覆盖不到的那一层。
    /// 触发：建 tools/pool_smoke_flag.txt → 进 Play → 写 tools/pool_smoke_result.txt → 自动删标记。
    /// </summary>
    public class PoolSmokeProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/pool_smoke_result.txt"); } }
        //★用自己的标记名：以前与 PoolCompileProbe 共用 pool_smoke_flag.txt —— 两个探针同帧都 File.Exists
        //  然后各自 File.Delete，先跑的那个删掉标记后另一个就不再触发（实测 PoolSmokeProbe 一次都没跑过）。
        private static string FlagPath { get { return Path.Combine(Root, "tools/poolsmoke_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("PoolSmokeProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PoolSmokeProbe>();
        }

        private int frames;
        private bool done;
        private int cardCount, graphCount, runCount, errCount, noEntry, handRun, boardRun;
        private readonly List<string> problems = new List<string>();

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath)) { done = false; frames = 0; Reset(); Debug.Log("[卡池冒烟] 重跑"); }
                return;
            }
            frames++;
            if (frames < 120)
                return;
            done = true;
            var sb = new StringBuilder();
            try { Sweep(sb); }
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e.Message); problems.Add("整体异常: " + e); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[卡池冒烟] 完成：卡 " + cardCount + "｜图 " + graphCount + "｜执行 " + runCount
                + "｜异常 " + errCount + "｜无入口 " + noEntry + " → " + OutPath);
        }

        private void Reset()
        {
            cardCount = graphCount = runCount = errCount = noEntry = handRun = boardRun = 0;
            problems.Clear();
        }

        private void Sweep(StringBuilder sb)
        {
            List<CardData> cards = CardPoolIO.GetCustomCards();
            sb.AppendLine("卡数=" + (cards != null ? cards.Count : 0));

            Game game = new Game("probe_smoke", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0], p1 = game.players[1];
            CardData hero = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Hero) { hero = c; break; }
            if (hero == null) { sb.AppendLine("没有英雄定义，无法建夹具"); return; }
            p0.hero = Card.Create(hero, null, p0, "smoke_p0hero");
            p0.hp_max = 30; p0.hp = 30;
            p1.hero = Card.Create(hero, null, p1, "smoke_p1hero");
            p1.hp_max = 30; p1.hp = 30;

            if (cards != null)
                foreach (CardData cd in cards)
                    SweepCardData(logic, p0, p1, cd, CardPoolIO.GetCustomData(cd != null ? cd.id : null), sb);

            //★内置卡池（base_pool_v1.json 等）不在 GetCustomCards() 里 → 直接读磁盘 JSON 再跑一遍（覆盖迁移后的 155 张）
            string wdir = Path.Combine(Application.persistentDataPath, "Workshop");
            if (Directory.Exists(wdir))
            {
                foreach (string f in Directory.GetFiles(wdir, "*.json"))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.IndexOf("pool", StringComparison.OrdinalIgnoreCase) < 0
                        && fn.IndexOf("Pack", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;    //只扫卡池类文件（buffs/custom_nodes 不是卡池）
                    CardPoolData pool = null;
                    try { pool = JsonUtility.FromJson<CardPoolData>(File.ReadAllText(f, Encoding.UTF8)); }
                    catch (Exception e) { problems.Add("JSON 解析失败 " + fn + "：" + e.Message); continue; }
                    if (pool == null || pool.cards == null)
                        continue;
                    foreach (CardCustomData d in pool.cards)
                    {
                        if (d == null || string.IsNullOrEmpty(d.id))
                            continue;
                        SweepCardData(logic, p0, p1, CardData.Get(d.id), d, sb);
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("== 汇总 ==");
            sb.AppendLine("卡数=" + cardCount + " 图数=" + graphCount + " 执行次数=" + runCount
                + "（战场 " + boardRun + " / 手牌 " + handRun + "）异常=" + errCount + " 无入口=" + noEntry);
            sb.AppendLine("问题条数=" + problems.Count);
            sb.AppendLine();
            sb.AppendLine("== 明细（前 60 条）==");
            for (int i = 0; i < problems.Count && i < 60; i++)
                sb.AppendLine(problems[i]);
        }

        private void SweepCardData(GameLogic logic, Player p0, Player p1, CardData cd, CardCustomData d, StringBuilder sb)
        {
            if (cd == null || string.IsNullOrEmpty(cd.id))
                return;
            if (d == null) { problems.Add(cd.id + " 无 CardCustomData（跑不了图）"); return; }
            cardCount++;

            List<GraphData> graphs = new List<GraphData>();
            foreach (CardEffectData e in d.EnsureEffects())
                if (e != null && e.graph != null) graphs.Add(e.graph);
            //（AbilityCustomData 没有 graph 字段 → 效果图只从 effects 取）
            if (graphs.Count == 0 && d.graph != null) graphs.Add(d.graph);

            foreach (GraphData g in graphs)
            {
                graphCount++;
                string entryAction = null;
                if (g.nodes != null)
                    foreach (GraphNode n in g.nodes)
                        if (n != null && n.type == GraphNodeType.Event) { entryAction = n.action; break; }
                //★区分"空占位图"与"真坏图"：卡池 DTO 里每张卡都可能挂一张空的 "NewGraph"
                //  （效果其实走"数据直通能力"），以前不看节点数一律报错 → 154 条噪音，
                //  把真正"有节点却没有入口"的坏图（这种才是打出去没反应）淹掉了。
                bool has_node = g.nodes != null && g.nodes.Count > 0;
                if (string.IsNullOrEmpty(entryAction))
                {
                    noEntry++;
                    if (has_node)
                        problems.Add(cd.id + "/" + (cd.title ?? "?") + " 图「" + (g.name ?? "?") + "」有节点但没有入口(Event)节点");
                    continue;
                }
                RunOne(sb, logic, p0, p1, cd, g, entryAction, true);
                RunOne(sb, logic, p0, p1, cd, g, entryAction, false);
            }
        }

        private void RunOne(StringBuilder sb, GameLogic logic, Player p0, Player p1, CardData cd,
            GraphData g, string entryAction, bool onBoard)
        {
            runCount++;
            if (onBoard) boardRun++; else handRun++;
            Card host = null;
            try
            {
                host = Card.Create(cd, null, p0, "smoke_" + cd.id + (onBoard ? "_b" : "_h"));
                //★能力必须灌进卡实例：Card.GetAbilities() 读实例的 abilities / abilities_ongoing（Create 不填）
                if (host.abilities == null) host.abilities = new List<string>();
                if (host.abilities_ongoing == null) host.abilities_ongoing = new List<string>();
                if (cd.abilities != null)
                {
                    foreach (AbilityData a in cd.abilities)
                    {
                        if (a == null || string.IsNullOrEmpty(a.id)) continue;
                        if (a.trigger == AbilityTrigger.Ongoing) host.abilities_ongoing.Add(a.id);
                        else host.abilities.Add(a.id);
                    }
                }
                if (onBoard) p0.cards_board.Add(host);
                else p0.cards_hand.Add(host);

                NodeDocRunner.Run(logic, g, host, p1.hero, null, entryAction);
            }
            catch (Exception e)
            {
                errCount++;
                Exception inner = e.InnerException != null ? e.InnerException : e;
                problems.Add("异常｜" + cd.id + "/" + (cd.title ?? "?") + "｜入口=" + entryAction
                    + "｜" + (onBoard ? "战场" : "手牌") + "｜" + inner.Message
                    + "｜" + (inner.StackTrace != null ? inner.StackTrace.Split('\n')[0].Trim() : ""));
            }
            finally
            {
                if (host != null)
                {
                    if (onBoard) p0.cards_board.Remove(host);
                    else p0.cards_hand.Remove(host);
                }
            }
        }
    }
}
