using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 主线程判定工具。
    ///
    /// 用途：NodeDoc 图会被 **AI 推演线程**（后台线程，`AILogic.Execute → ThreadStart`）执行以便预测走法，
    /// 而任何表现层行为（特效实例化、AudioSource、transform 访问…）都只能在主线程做——
    /// 后台线程碰 Unity API 会抛 `get_transform can only be called from the main thread`，
    /// 并被 `AILogic` 捕获为"推演线程异常"，导致 AI 本次计算被终止。
    ///
    /// 因此所有"仅表现"的入口（VFXRuntime / BgmManager 等）在触碰任何 Unity API 之前，
    /// 都先用 `IsMainThread` 判定：不是主线程就直接返回（AI 预测阶段不需要出特效/换音乐）。
    /// </summary>
    public static class MainThreadUtil
    {
        private static int main_thread_id = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureMainThread()
        {
            main_thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>当前是否在主线程（未捕获到主线程 id 时保守返回 true，避免误伤正常流程）</summary>
        public static bool IsMainThread
        {
            get
            {
                if (main_thread_id < 0)
                    return true;
                return System.Threading.Thread.CurrentThread.ManagedThreadId == main_thread_id;
            }
        }
    }
}
