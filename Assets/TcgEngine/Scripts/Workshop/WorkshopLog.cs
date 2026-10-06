using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 工作台（卡池导入 / 规则图编译 / 卡牌编辑器）的「详细诊断日志」总开关。
    ///
    /// 为什么要有它：这些日志是按「每张卡 / 每个能力 / 每次开面板 / 每行属性」打印的，
    /// 跑一阵 Console 就上万条 —— Unity 编辑器会因此整体变卡（拖节点尤其明显），
    /// 但它们只在排查问题时有用，日常使用纯属噪音。故**默认静默**。
    ///
    /// 注意：警告与错误（Debug.LogWarning / Debug.LogError）**不走这里**，永远照常打印。
    /// 打开方式：菜单 TcgEngine / 规则图诊断日志(详细)（PlayerPrefs 持久化）。
    /// </summary>
    public static class WorkshopLog
    {
        private const string PrefKey = "Workshop.VerboseLog";
        private static int cached = -1;

        /// <summary>详细诊断日志是否打开（默认关；读一次 PlayerPrefs 后缓存）</summary>
        public static bool Verbose
        {
            get
            {
                if (cached < 0)
                    cached = PlayerPrefs.GetInt(PrefKey, 0);
                return cached != 0;
            }
        }

        /// <summary>打一条「详细诊断」日志：仅在开关打开时输出（高频日志一律走这里）</summary>
        public static void Info(string message)
        {
            if (Verbose)
                Debug.Log(message);
        }

        /// <summary>切换详细日志（持久化到 PlayerPrefs）</summary>
        public static void SetVerbose(bool on)
        {
            cached = on ? 1 : 0;
            PlayerPrefs.SetInt(PrefKey, cached);
            PlayerPrefs.Save();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("TcgEngine/规则图诊断日志(详细)")]
        private static void ToggleVerbose()
        {
            SetVerbose(!Verbose);
            Debug.Log("[诊断日志] 详细诊断日志 = " + (Verbose ? "开（Console 会变多）" : "关（默认）"));
        }
#endif
    }
}
