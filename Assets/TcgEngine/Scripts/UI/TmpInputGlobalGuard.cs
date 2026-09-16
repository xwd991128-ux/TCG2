using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 全局兜底驱动：周期性把「**非聚焦且文本为空**」的 TMP 输入框补上占位，
    /// 覆盖所有面板（含各弹层 / 其它模块自建的输入框），不依赖它们是否接入 TmpInputUtil。
    ///
    /// 背景：TMP 3.0.7 的 TMP_InputField.GenerateHightlight 在「文本为空 + 失焦后
    /// m_SelectionStillActive 仍为真」时会访问 characterInfo[-1] 抛 IndexOutOfRangeException。
    /// 修法要点是"输入框永不保持空文本"，但逐个改造所有自建输入框成本高、易漏，
    /// 因此这里再加一层全局网：任何漏网的空输入框也会在 0.4s 内被补上占位。
    ///
    /// 为什么只处理非聚焦：聚焦中的输入框走 GenerateCaret 分支（不会越界），
    /// 且此时补字符会打断用户正在进行的输入 —— 崩溃路径恰好是"失焦之后"。
    /// </summary>
    public class TmpInputGlobalGuard : MonoBehaviour
    {
        private const float Interval = 0.4f;
        private float timer;
        private readonly List<TMP_InputField> buf = new List<TMP_InputField>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameObject go = new GameObject("[TmpInputGlobalGuard]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<TmpInputGlobalGuard>();
        }

        private void Update()
        {
            timer += Time.unscaledDeltaTime;
            if (timer < Interval)
                return;
            timer = 0f;
            buf.Clear();
            buf.AddRange(FindObjectsOfType<TMP_InputField>(true));
            for (int i = 0; i < buf.Count; i++)
            {
                TMP_InputField inp = buf[i];
                if (inp == null || inp.isFocused)
                    continue;
                TmpInputUtil.EnsureNotEmpty(inp);
            }
        }
    }
}
