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
    /// <summary>【结束回合闸门 + 额外回合的回合计数】
    /// 断言：
    /// ① 非主阶段调 EndTurn() → **必须留下告警**（修复前静默返回，"点了没反应"无迹可寻）
    /// ② 对照：同一判定走统一入口 RequestEndTurn() 本来就带告警且返回 false
    /// ③ 额外回合（HeroNewTurn）结束时 turn_count 只能 +1（修复前先手拿额外回合会 +2 —— 因为
    ///    "current_player == first_player" 在额外回合里原地成立）
    /// ④ 对照：正常换人回到先手时 turn_count +1
    /// 触发：建 tools/endturn_flag.txt → 进 Play → 写 tools/endturn_result.tsv → 自动删标记。
    /// </summary>
    public class EndTurnGateProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/endturn_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/endturn_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("EndTurnGateProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<EndTurnGateProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();
        private readonly List<string> warnings = new List<string>();

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Warning || type == LogType.Error)
                warnings.Add(msg ?? "");
        }

        private bool HasWarning(string keyword)
        {
            for (int i = 0; i < warnings.Count; i++)
                if (warnings[i].Contains(keyword))
                    return true;
            return false;
        }

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
            Debug.Log("[结束回合] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private static Game NewGame(CardData heroData, string uid, out GameLogic logic)
        {
            Game g = new Game(uid, 2);
            g.state = GameState.Play;
            g.current_player = 0;
            logic = new GameLogic(g);
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);
            p0.hero = Card.Create(heroData, null, p0, uid + "_h0");
            p1.hero = Card.Create(heroData, null, p1, uid + "_h1");
            p0.hp_max = 30; p0.hp = 30;
            p1.hp_max = 30; p1.hp = 30;
            return g;
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            CardData heroData = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (c.type == CardType.Hero) { heroData = c; break; }
            }
            if (heroData == null)
            {
                sb.AppendLine("SKIP\t缺少英雄卡定义\t");
                return;
            }

            //---- ① 非主阶段 EndTurn 必须留下告警 ----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_end_a", out logic);
                g.phase = GamePhase.StartTurn;
                warnings.Clear();
                logic.EndTurn();
                bool warned = HasWarning("结束回合");
                bool phase_kept = g.phase == GamePhase.StartTurn;
                Check("① 非主阶段 EndTurn 有明确告警（不再静默）", warned && phase_kept,
                    "告警=" + warned + "（期望 True） 相位未变=" + phase_kept + "（期望 True）｜捕获告警 " + warnings.Count + " 条");
            }

            //---- ② 对照：统一入口 RequestEndTurn 本来就带告警 ----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_end_b", out logic);
                g.phase = GamePhase.EndTurn;
                warnings.Clear();
                bool ok = logic.RequestEndTurn();
                bool warned = HasWarning("结束回合");
                Check("② 对照：统一入口 RequestEndTurn 返回 false 且告警", !ok && warned,
                    "返回=" + ok + "（期望 False） 告警=" + warned + "（期望 True）");
            }

            //---- ③ 额外回合不额外计"新回合"（必须连着两次换人才显形）----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_end_c", out logic);
                g.first_player = 1;
                g.current_player = 0;
                g.turn_count = 5;
                logic.StartNextTurn();                       //① 正常换人：回到先手 p1 → 新回合 +1
                int after_normal = g.turn_count;
                Player p1 = g.GetPlayer(1);
                p1.status.Add(new CardStatus(StatusType.HeroNewTurn, 1, 0));   //② 先手拿一个额外回合
                logic.StartNextTurn();                       //   额外回合：current_player 原地不动
                Check("③ 额外回合不再多计一个回合数", g.turn_count == after_normal && after_normal == 6,
                    "正常换人后=" + after_normal + "（期望 6） 额外回合后=" + g.turn_count
                    + "（期望仍 6；修复前会变 7 ⇒ 一个真实轮次 +2） 当前玩家=" + g.current_player);
            }

            //---- ④ 对照：正常换人回到先手时 +1 ----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_end_d", out logic);
                g.first_player = 1;
                g.current_player = 0;
                g.turn_count = 5;
                logic.StartNextTurn();
                Check("④ 对照：正常换人回到先手 → turn_count +1", g.turn_count == 6 && g.current_player == 1,
                    "turn_count 5 → " + g.turn_count + "（期望 6） 当前玩家=" + g.current_player + "（期望 1）");
            }
        }
    }
}
