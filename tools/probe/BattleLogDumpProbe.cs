using System;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine.Probe
{
    /// <summary>【对战记录导出探针·追加版】把**每一局**的 battle_log 追加导出（不删标记、不覆盖），
    /// 用于跨多局扫描"被消灭/召唤/增益"这类低频事件有没有真的进记录。
    /// 触发：tools/bldump_flag.txt 存在即工作；每局结束追加一段。输出 tools/battlelog_rows.tsv
    /// 用法：建 tools/bldump_flag.txt →（mcp.ps1 autobattle N）→ 读 tools/battlelog_rows.tsv → 用完删标记。</summary>
    public class BattleLogDumpProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/battlelog_rows.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/bldump_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("BattleLogDumpProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<BattleLogDumpProbe>();
        }

        private float timer;
        private bool wrote_header;
        private int last_count = -1;
        private int game_index;

        private void Update()
        {
            if (!File.Exists(FlagPath))
                return;
            timer += Time.unscaledDeltaTime;
            if (timer < 2f)
                return;
            timer = 0f;

            Game g = BattleLog.Current;
            if (g == null || g.battle_log == null)
                return;

            if (g.state == GameState.GameEnded && g.battle_log.Count != last_count)
            {
                last_count = g.battle_log.Count;
                game_index++;
                Append(g);
            }
        }

        private void Append(Game g)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                if (!wrote_header)
                {
                    sb.AppendLine("# 对战记录导出（逐局追加）" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    wrote_header = true;
                }
                sb.AppendLine("===== 第 " + game_index + " 局 | 条数=" + g.battle_log.Count + " | 回合=" + g.turn_count
                    + " | " + DateTime.Now.ToString("HH:mm:ss") + " =====");
                for (int i = 0; i < g.battle_log.Count; i++)
                {
                    BattleLogEntry e = g.battle_log[i];
                    if (e == null)
                        continue;
                    if (e.kind == (byte)BattleLogKind.Draw || e.kind == (byte)BattleLogKind.TurnStart)
                        continue;   //抽牌/回合分隔略过（保持文件小）
                    string fmt;
                    try { fmt = BattleLog.Format(e, g, 0); }
                    catch (Exception ex) { fmt = "（Format 异常：" + ex.Message + "）"; }
                    sb.AppendLine("#" + e.seq + "\t[T" + e.turn + "]\t" + fmt + "\t(kind=" + e.kind + ")");
                }
                int[] cnt = new int[64];
                foreach (BattleLogEntry e in g.battle_log)
                    if (e != null && e.kind < 64) cnt[e.kind]++;
                sb.Append("类型: ");
                for (int k = 1; k < 32; k++)
                    if (cnt[k] > 0)
                        sb.Append((BattleLogKind)k + "=" + cnt[k] + " ");
                sb.AppendLine();
                File.AppendAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
                Debug.Log("[对战记录导出] 第 " + game_index + " 局已追加 → " + OutPath);
            }
            catch (Exception e) { Debug.LogWarning("[对战记录导出] 失败：" + e.Message); }
        }
    }
}
