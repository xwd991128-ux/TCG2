using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using TcgEngine;
using TcgEngine.Client;
using TcgEngine.UI;
using TcgEngine.Workshop;

namespace TcgEngine.DevTools
{
    /// <summary>
    /// 自动对战回归工具（AI vs AI）：进 Play 后**全自动**打完 N 局，收集"异常 / 卡面重建 / 回合数 /
    /// 规则图是否执行 / 卡在 Connecting"等指标，写一份报告到磁盘，并打一条汇总日志。
    ///
    /// ★触发方式（二选一，都不需要在游戏里点任何 UI）：
    ///   ① 外部触发（供 AI/脚本用）：存在标记文件就开跑 ——
    ///      `{persistentDataPath}/Workshop/autobattle.json`
    ///      内容可选（JsonUtility）：{ "battles": 1, "maxSecondsPerBattle": 180, "stuckSeconds": 40 }
    ///      跑完会**自动删除标记文件**，避免下次 Play 又跑一遍。
    ///   ② 编辑器菜单：TcgEngine →「自动对战回归（AI vs AI）」——它会写标记文件并进入 Play。
    ///
    /// ★报告位置：`{persistentDataPath}/Workshop/autobattle_report.txt`（key=value，便于脚本解析）
    ///
    /// 设计约束：没标记文件时**零开销**（连 GameObject 都不建）。
    /// </summary>
    public class AutoBattleRunner : MonoBehaviour
    {
        // ---------------- 配置 ----------------
        [Serializable]
        public class Config
        {
            public int battles = 1;                 //跑几局
            public int maxSecondsPerBattle = 180;   //单局上限（超时算 TIMEOUT）
            public int stuckSeconds = 40;           //开局卡在 Connecting 多久算 STUCK
        }

        public static string FlagFile { get { return Path.Combine(CardPoolIO.SaveFolder, "autobattle.json"); } }
        public static string ReportFile { get { return Path.Combine(CardPoolIO.SaveFolder, "autobattle_report.txt"); } }

        private static AutoBattleRunner _instance;
        private static Config _cfg;

        private readonly StringBuilder _report = new StringBuilder();
        private readonly List<string> _error_samples = new List<string>();

        private int _battle_index;
        private float _battle_time;
        private bool _in_battle;
        private bool _finished;
        private int _errors, _exceptions, _asserts;
        private int _nodoc_lines;
        private int _trace_lines;      //★ 被 GameLog 接管的高频轨迹日志条数（ResolveEffectTarget/[NodeDoc] 等）
        private int _card_calls, _card_rebuilds;
        private bool _logged_start_ok;

        // ---------------- 启动入口 ----------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null)
                return;                                   //已经在跑了（场景切换会再次进这里）

            if (!File.Exists(FlagFile))
                return;                                   //没有标记文件 → 零开销，什么都不做

            _cfg = new Config();
            try
            {
                string json = File.ReadAllText(FlagFile, Encoding.UTF8);
                if (!string.IsNullOrEmpty(json) && json.Trim().Length > 2)
                    _cfg = JsonUtility.FromJson<Config>(json) ?? new Config();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AutoBattle] 标记文件解析失败，用默认配置: " + e.Message);
            }

            GameObject go = new GameObject("__AutoBattleRunner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AutoBattleRunner>();
            _instance.Begin();
        }

        private void Begin()
        {
            Application.logMessageReceived += OnLog;
            SceneManager.sceneLoaded += OnSceneLoaded;

            _report.AppendLine("# AutoBattleRunner 报告 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _report.AppendLine("config.battles=" + _cfg.battles);
            _report.AppendLine("config.maxSecondsPerBattle=" + _cfg.maxSecondsPerBattle);
            _report.AppendLine("config.stuckSeconds=" + _cfg.stuckSeconds);
            _report.AppendLine("flag_file=" + FlagFile);

            try { File.Delete(FlagFile); } catch { }     //先删标记，避免下次 Play 重复触发

            Debug.Log("[AutoBattle] 启动：计划 " + _cfg.battles + " 局 AI vs AI（单局上限 " + _cfg.maxSecondsPerBattle
                + "s，卡 Connecting 判定 " + _cfg.stuckSeconds + "s）；报告将写入 " + ReportFile);

            StartNextBattle();
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // ---------------- 每局流程 ----------------
        private void StartNextBattle()
        {
            _battle_index++;
            _battle_time = 0f;
            _in_battle = false;
            _logged_start_ok = false;
            _errors = 0; _exceptions = 0; _asserts = 0;
            _nodoc_lines = 0;
            _trace_lines = 0;
            _error_samples.Clear();
            _card_calls = CardUI.stat_calls;
            _card_rebuilds = CardUI.stat_rebuilds;

            try
            {
                GameplayData g = GameplayData.Get();
                DeckData pd = g.test_deck != null ? g.test_deck
                    : (g.free_decks != null && g.free_decks.Length > 0 ? g.free_decks[0] : null);
                DeckData ad = g.test_deck_ai != null ? g.test_deck_ai
                    : (g.ai_decks != null && g.ai_decks.Length > 0 ? g.ai_decks[0] : null);
                if (pd == null || ad == null)
                {
                    Finish("ABORT_NO_DECK", "test_deck/test_deck_ai/free_decks/ai_decks 都为空");
                    return;
                }

                g.ai_vs_ai = true;   //★ 让"玩家 0"也交给 AI（GameServer.StartGame 会据此给双方建 AIPlayer）→ 全程无需输入
                GameClient.game_settings.game_type = GameType.Solo;
                GameClient.game_settings.game_mode = GameMode.Casual;
                GameClient.game_settings.game_uid = "autobattle_" + _battle_index + "_" + UnityEngine.Random.Range(1000, 9999);
                GameClient.game_settings.scene = (g.arena_list != null && g.arena_list.Length > 0) ? g.arena_list[0] : "Game";
                GameClient.player_settings.deck = new UserDeckData(pd);
                GameClient.ai_settings.deck = new UserDeckData(ad);
                GameClient.ai_settings.ai_level = g.ai_level;

                Debug.Log("[AutoBattle] 第 " + _battle_index + "/" + _cfg.battles + " 局：player=" + pd.id
                    + " ai=" + ad.id + " scene=" + GameClient.game_settings.scene + " ai_vs_ai=true");
                MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual);
                _in_battle = true;
            }
            catch (Exception e)
            {
                Finish("ABORT_START_EXCEPTION", e.ToString());
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            //开局/回菜单都可能走这里；只记一笔，决策留给 Update
            _report.AppendLine("scene_loaded=" + scene.name + " (battle=" + _battle_index + ")");
        }

        private void Update()
        {
            if (!_in_battle || _finished)
                return;

            _battle_time += Time.unscaledDeltaTime;

            GameClient client = GameClient.Get();
            bool ready = client != null && client.IsReady();

            if (!ready)
            {
                if (_battle_time > _cfg.stuckSeconds)
                {
                    //★ 这正是"客户端永远卡在 Connecting"那类回归：把现场信息写进报告
                    string diag = DiagnoseConnecting(client);
                    Finish("STUCK_CONNECTING", diag);
                }
                return;
            }

            if (!_logged_start_ok)
            {
                _logged_start_ok = true;
                Debug.Log("[AutoBattle] 第 " + _battle_index + " 局已开局（" + Mathf.RoundToInt(_battle_time) + "s）：开始收集指标");
            }

            Game data = client.GetGameData();
            if (data != null && data.HasEnded())
            {
                AppendBattleResult("ENDED");
                NextOrFinish();
                return;
            }

            if (_battle_time > _cfg.maxSecondsPerBattle)
            {
                AppendBattleResult("TIMEOUT");
                NextOrFinish();
            }
        }

        private void NextOrFinish()
        {
            _in_battle = false;
            if (_battle_index < _cfg.battles)
            {
                //回菜单再开下一局（避免在已结束的对局场景里直接 StartGame）
                try { SceneManager.LoadScene("Menu"); } catch (Exception e) { Finish("ABORT_SCENE", e.Message); return; }
                Invoke(nameof(StartNextBattle), 2f);
            }
            else
            {
                Finish("OK", null);
            }
        }

        // ---------------- 指标 ----------------
        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                if (type == LogType.Exception) _exceptions++;
                else if (type == LogType.Assert) _asserts++;
                else _errors++;
                if (_error_samples.Count < 5)
                    _error_samples.Add("[" + type + "] " + message);
            }
            if (message != null && message.StartsWith("[NodeDoc]"))
                _nodoc_lines++;
            //★ 这些是「被 GameLog 接管」的执行轨迹：开关关掉时应当一条都没有（A/B 验证用）
            if (message != null
                && (message.StartsWith("ResolveEffectTarget")
                    || message.StartsWith("ResolveCardAbilityPlayTarget")
                    || message.StartsWith("[起动触发]")
                    || message.StartsWith("[多目标]")))
                _trace_lines++;
        }

        private string DiagnoseConnecting(GameClient client)
        {
            StringBuilder sb = new StringBuilder();
            Game data = client != null ? client.GetGameData() : null;
            sb.Append("data=").Append(data == null ? "null" : "ok");
            if (data != null)
            {
                sb.Append(";state=").Append(data.state);
                sb.Append(";players=").Append(data.players != null ? data.players.Length : 0);
                if (data.players != null)
                {
                    foreach (Player p in data.players)
                    {
                        if (p == null) continue;
                        sb.Append(";p").Append(p.player_id)
                          .Append("(ai=").Append(p.is_ai)
                          .Append(",ready=").Append(p.ready)
                          .Append(",conn=").Append(p.IsConnected())
                          .Append(",deck=").Append(p.cards_deck != null ? p.cards_deck.Count : 0).Append(")");
                    }
                }
            }
            return sb.ToString();
        }

        private void AppendBattleResult(string outcome)
        {
            GameClient client = GameClient.Get();
            Game data = client != null ? client.GetGameData() : null;
            int turns = data != null ? data.turn_count : -1;
            string scene = SceneManager.GetActiveScene().name;
            int calls = CardUI.stat_calls - _card_calls;
            int rebuilds = CardUI.stat_rebuilds - _card_rebuilds;
            int skipped = calls - rebuilds;
            int skip_rate = calls > 0 ? (100 * skipped / calls) : 0;

            //按钮栏（「增加/删除按钮」节点效果）
            Player me = client != null ? client.GetPlayer() : null;
            int bar = (me != null && me.battle_buttons != null) ? me.battle_buttons.Count : 0;

            _report.AppendLine("battle." + _battle_index + ".outcome=" + outcome);
            _report.AppendLine("battle." + _battle_index + ".seconds=" + Mathf.RoundToInt(_battle_time));
            _report.AppendLine("battle." + _battle_index + ".scene=" + scene);
            _report.AppendLine("battle." + _battle_index + ".turns=" + turns);
            _report.AppendLine("battle." + _battle_index + ".cardui_calls=" + calls);
            _report.AppendLine("battle." + _battle_index + ".cardui_rebuilds=" + rebuilds);
            _report.AppendLine("battle." + _battle_index + ".cardui_skip_rate=" + skip_rate + "%");
            _report.AppendLine("battle." + _battle_index + ".nodoc_logs=" + _nodoc_lines);
            _report.AppendLine("battle." + _battle_index + ".trace_logs=" + _trace_lines
                + "   # 被 GameLog 接管的高频轨迹日志条数（开关关闭时应为 0）");
            _report.AppendLine("battle." + _battle_index + ".gamelog_verbose=" + GameLog.Verbose);
            _report.AppendLine("battle." + _battle_index + ".battle_buttons=" + bar);
            _report.AppendLine("battle." + _battle_index + ".errors=" + _errors
                + " exceptions=" + _exceptions + " asserts=" + _asserts);
            foreach (string s in _error_samples)
                _report.AppendLine("battle." + _battle_index + ".error_sample=" + s);

            Debug.Log("[AutoBattle] 第 " + _battle_index + " 局结束：outcome=" + outcome
                + " 用时=" + Mathf.RoundToInt(_battle_time) + "s 回合=" + turns
                + " 卡面调用=" + calls + "/重建=" + rebuilds + "（跳过率 " + skip_rate + "%）"
                + " 规则图日志=" + _nodoc_lines + " 按钮栏=" + bar
                + " 错误=" + _errors + " 异常=" + _exceptions);
        }

        private void Finish(string result, string detail)
        {
            if (_finished)
                return;
            _finished = true;
            _in_battle = false;

            _report.AppendLine("result=" + result);
            if (!string.IsNullOrEmpty(detail))
                _report.AppendLine("detail=" + detail);
            _report.AppendLine("total.errors=" + _errors + " exceptions=" + _exceptions + " asserts=" + _asserts);
            _report.AppendLine("done=1");

            try
            {
                Directory.CreateDirectory(CardPoolIO.SaveFolder);
                File.WriteAllText(ReportFile, _report.ToString(), Encoding.UTF8);
                Debug.Log("[AutoBattle] 报告已写入 " + ReportFile + "｜result=" + result);
            }
            catch (Exception e)
            {
                Debug.LogError("[AutoBattle] 报告写入失败: " + e.Message + "\n" + _report);
            }
            Debug.Log("[AutoBattle] SUMMARY result=" + result + " battles=" + _battle_index
                + "/" + _cfg.battles + " errors=" + _errors + " exceptions=" + _exceptions
                + (string.IsNullOrEmpty(detail) ? "" : (" detail=" + detail)));
        }
    }

#if UNITY_EDITOR
    /// <summary>编辑器入口：写标记文件 + 进入 Play（跑完自动删标记、写报告）</summary>
    public static class AutoBattleRunnerMenu
    {
        [UnityEditor.MenuItem("TcgEngine/自动对战回归（AI vs AI）")]
        public static void RunOne()
        {
            WriteFlag(1, 180, 40);
            UnityEditor.EditorApplication.isPlaying = true;
        }

        [UnityEditor.MenuItem("TcgEngine/自动对战回归 ×3 局")]
        public static void RunThree()
        {
            WriteFlag(3, 180, 40);
            UnityEditor.EditorApplication.isPlaying = true;
        }

        [UnityEditor.MenuItem("TcgEngine/打开自动对战报告")]
        public static void OpenReport()
        {
            string path = AutoBattleRunner.ReportFile;
            if (!File.Exists(path))
            {
                UnityEditor.EditorUtility.DisplayDialog("自动对战", "还没有报告文件：\n" + path, "确定");
                return;
            }
            UnityEditor.EditorUtility.RevealInFinder(path);
        }

        private static void WriteFlag(int battles, int maxSeconds, int stuckSeconds)
        {
            try
            {
                Directory.CreateDirectory(CardPoolIO.SaveFolder);
                AutoBattleRunner.Config cfg = new AutoBattleRunner.Config();
                cfg.battles = battles;
                cfg.maxSecondsPerBattle = maxSeconds;
                cfg.stuckSeconds = stuckSeconds;
                File.WriteAllText(AutoBattleRunner.FlagFile, JsonUtility.ToJson(cfg, true), Encoding.UTF8);
                Debug.Log("[AutoBattle] 已写标记文件 " + AutoBattleRunner.FlagFile + "（battles=" + battles + "）");
            }
            catch (Exception e)
            {
                UnityEditor.EditorUtility.DisplayDialog("自动对战", "写标记文件失败：" + e.Message, "确定");
            }
        }
    }
#endif
}
