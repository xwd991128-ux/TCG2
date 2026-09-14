using UnityEngine;

namespace TcgEngine.Audio
{
    /// <summary>
    /// 战斗内 BGM 变更请求通道（逻辑层 → 表现层的单向桥）。
    ///
    /// 为什么要有这一层：NodeDoc 动作在 GameLogic 结算链上执行（可能跑在服务器/无头端、也可能被 AI 预测克隆执行），
    /// 直接调 AudioTool 会污染对局逻辑、且在无音频端毫无意义。因此节点只"派发请求"，
    /// 由客户端表现层 BgmManager 在"对战场景"内响应；不在对战场景（或无 BgmManager）时请求被安全忽略。
    /// —— 换 BGM 不改任何对局状态（状态隔离）。
    /// </summary>
    public static class BgmBattleRuntime
    {
        /// <summary>请求把当前战斗 BGM 换成指定曲子；bgm_id_or_title 为空 = 停止战斗 BGM</summary>
        public static void RequestSetBgm(string bgm_id_or_title, int volume_percent, int fade_ms)
        {
            //表现层隔离：这段也会在 AI 推演线程上执行，异常与跨线程 Unity 调用都不能外溢
            try
            {
                float fade = Mathf.Max(fade_ms, 0) / 1000f;
                float volume = volume_percent < 0 ? -1f : Mathf.Clamp01(volume_percent / 100f);
                BgmManager.BattleSet(bgm_id_or_title, volume, fade);
                Debug.Log("[BGM] 战斗内请求换曲: " + (string.IsNullOrEmpty(bgm_id_or_title) ? "(静音)" : bgm_id_or_title)
                    + " 音量=" + (volume < 0f ? "默认" : Mathf.RoundToInt(volume * 100f) + "%") + " 淡入淡出=" + fade_ms + "ms");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BGM] 换曲请求失败（已忽略，不影响对局逻辑）: " + e.Message);
            }
        }

        /// <summary>请求恢复该战斗默认配置的 BGM</summary>
        public static void RequestRestoreDefault(int fade_ms)
        {
            try
            {
                float fade = Mathf.Max(fade_ms, 0) / 1000f;
                BgmManager.BattleRestoreDefault(fade);
                Debug.Log("[BGM] 战斗内请求恢复默认 BGM（淡入淡出 " + fade_ms + "ms）");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BGM] 恢复默认 BGM 请求失败（已忽略）: " + e.Message);
            }
        }
    }
}
