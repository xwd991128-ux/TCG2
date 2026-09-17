using System.IO;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 全局诊断日志开关（C1）。
    ///
    /// 背景：规则图执行（NodeDocRunner）与回合/触发流程（GameLogic）里有**逐节点、逐事件**的执行轨迹日志，
    /// 一场对战下来会刷出成百上千条 + 对应量的字符串分配。它们对排查很有用，但不该在正常运行时一直开着。
    ///
    /// 默认**关闭**，需要时用下面任一方式打开：
    ///   ① 代码：`GameLog.Verbose = true;`
    ///   ② 标记文件：`{persistentDataPath}/Workshop/verbose_log.txt` 存在 → 本次运行开启（删掉即恢复关闭）
    ///   ③ 运行时热键：**F8** 切换（仅 Editor / Development 版；切换时会打一条状态日志）
    ///
    /// ★重要约定：**只走轨迹日志**。警告/错误一律保持原来的 `Debug.LogWarning / Debug.LogError`（永远可见），
    /// 例如「卡 Connecting」「缺输入」「按钮没配」这类必须能看见的问题日志**没有**被这个开关接管。
    /// </summary>
    public static class GameLog
    {
        /// <summary>高频轨迹日志开关（默认关）</summary>
        public static bool Verbose;

        /// <summary>是否已初始化过（便于其它系统判断）</summary>
        public static bool Initialized { get; private set; }

        /// <summary>标记文件：存在即开启（排查完删掉即可，不用改代码）</summary>
        public static string FlagFile
        {
            get { return Path.Combine(Application.persistentDataPath, "Workshop", "verbose_log.txt"); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            Initialized = true;
            Verbose = false;
            try
            {
                if (File.Exists(FlagFile))
                    Verbose = true;
            }
            catch { /* 读不到就当关闭 */ }

            if (Verbose)
                Debug.Log("[GameLog] 高频轨迹日志已开启（来源：标记文件 " + FlagFile + "）→ 排查完请删除该文件，或按 F8 关闭");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GameObject go = new GameObject("__GameLogToggle");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameLogToggle>();
#endif
        }

        /// <summary>轨迹日志（默认静默）</summary>
        public static void Log(string msg)
        {
            if (Verbose)
                Debug.Log(msg);
        }

        /// <summary>轨迹类警告（默认静默；真正的异常/配置错误请用 Debug.LogWarning，不要走这里）</summary>
        public static void LogWarning(string msg)
        {
            if (Verbose)
                Debug.LogWarning(msg);
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>运行时 F8 切换 `GameLog.Verbose`（仅 Editor / Development 版创建）</summary>
    public class GameLogToggle : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                GameLog.Verbose = !GameLog.Verbose;
                Debug.Log("[GameLog] 高频轨迹日志 " + (GameLog.Verbose ? "已开启（按 F8 关闭）" : "已关闭"));
            }
        }
    }
#endif
}
