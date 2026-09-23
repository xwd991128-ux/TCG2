using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 启动耗时探针（诊断用，不影响对局）：回答"进 Play 为什么要等这么久"。
    /// 触发：工程根存在 tools/startup_timing_flag.txt → 进 Play 时自动跑一次，写 tools/startup_timing.tsv 并删标记。
    ///   （也可不建标记：本探针只写日志，标记仅用于避免每次 Play 都刷，见 Boot。）
    /// 打点（都用 Time.realtimeSinceStartup = 进 Play 会话计时）：
    ///   ① BeforeSceneLoad：托管侧最早时机（≈域重载结束、脚本开始跑）
    ///   ② AfterSceneLoad ：场景 Awake（含 DataLoader.Awake 的全部数据加载）之后
    ///   ③ +1s / +3s     ：首帧与稳定后（能看出"数据加载 vs 首帧 UI"各占多少）
    /// 另记 DateTime.Now，便于和"外部发起 Play 的时刻"对齐（差值≈域重载+编辑器侧排队）。
    /// </summary>
    public static class StartupTimingProbe
    {
        private static readonly StringBuilder sb = new StringBuilder();
        private static float t_before_scene;
        private static bool booted;

        private static string Root
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
        }
        private static string FlagPath { get { return Path.Combine(Root, "tools/startup_timing_flag.txt"); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/startup_timing.tsv"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeScene()
        {
            t_before_scene = Time.realtimeSinceStartup;
            if (booted)
                return;
            booted = true;
            Note("BeforeSceneLoad");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterScene()
        {
            Note("AfterSceneLoad");
            var go = new GameObject("__StartupTimingProbe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Ticker>();
        }

        private class Ticker : MonoBehaviour
        {
            private float t0;
            private int marks;

            private void Awake()
            {
                t0 = Time.realtimeSinceStartup;
            }

            private void Update()
            {
                float el = Time.realtimeSinceStartup - t0;
                if (marks == 0 && el >= 1f) { marks = 1; Note("+1s"); }
                else if (marks == 1 && el >= 3f) { marks = 2; Note("+3s"); Write(); }
            }
        }

        private static void Note(string label)
        {
            sb.AppendLine(label + "\t" + Time.realtimeSinceStartup.ToString("F3")
                + "\t" + DateTime.Now.ToString("HH:mm:ss.fff")
                + "\tsince_play=" + Time.realtimeSinceStartup.ToString("F3")
                + "\tsince_beforeScene=" + (Time.realtimeSinceStartup - t_before_scene).ToString("F3"));
        }

        private static void Write()
        {
            try
            {
                var head = "# 启动耗时打点（列：阶段｜realtimeSinceStartup｜墙钟｜自进Play秒｜自BeforeScene秒）\n"
                    + "# realtimeSinceStartup 在进 Play 时从 0 起算 → 它就是「进 Play 到现在」的秒数\n";
                File.WriteAllText(OutPath, head + sb.ToString(), new UTF8Encoding(false));
                Debug.Log("[启动耗时] 打点已写 " + OutPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[启动耗时] 写盘失败: " + e.Message);
            }
            try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
        }
    }
}
