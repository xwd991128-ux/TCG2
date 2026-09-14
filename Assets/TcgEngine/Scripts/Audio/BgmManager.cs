using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Audio
{
    /// <summary>
    /// 场景 BGM 播放层（表现层，DontDestroyOnLoad 单例）：
    /// ① 按"界面标识"查 SceneBgmConfig → 切歌（双通道交叉淡入淡出，旧乐淡出/新乐淡入，时长可配）；
    /// ② 界面未配置 → 用配置的默认兜底曲 → 再退到调用方给的 fallback（迁移期兼容旧场景硬挂的音乐）→ 都没有则不播且不报错；
    /// ③ 战斗内 BGM 覆盖：响应 BgmBattleRuntime 请求（仅在"对战场景"生效），支持"恢复默认"；
    /// ④ 播放底层完全复用 AudioTool（专用通道 bgm_a/bgm_b，不碰既有 music/ambience/fx/sfx 通道）。
    /// </summary>
    public class BgmManager : MonoBehaviour
    {
        /// <summary>BGM 双通道：交叉淡入淡出用（交替使用，永不与既有通道冲突）</summary>
        public const string CHANNEL_A = "bgm_a";
        public const string CHANNEL_B = "bgm_b";

        private static BgmManager instance;

        private string current_key;              //当前界面标识
        private string current_bgm_id;           //当前 BGM 条目 id（空=无）
        private string active_channel = CHANNEL_A;

        private bool battle_override;            //战斗内是否被节点覆盖
        private string battle_override_id;       //覆盖曲目 id（空=覆盖为静音）

        /// <summary>面板类名 → 界面标识（只有列在这里的面板会触发切歌；弹窗/选择器不在此列，天然忽略）</summary>
        private static readonly Dictionary<string, string> PANEL_KEYS = new Dictionary<string, string>
        {
            { "LoginMenu", BgmKeys.Login },
            { "HomePanel", BgmKeys.MainMenu },
            { "CollectionPanel", BgmKeys.Collection },
            { "CardPoolPanel", BgmKeys.CardPool },
            { "CardEditorPanel", BgmKeys.CardEditor },
            { "GraphEditorPanel", BgmKeys.CardEditor },
            { "EndGamePanel", BgmKeys.EndGame },
        };

        public static string CurrentKey { get { return instance != null ? instance.current_key : null; } }
        public static string CurrentBgmId { get { return instance != null ? instance.current_bgm_id : null; } }

        /// <summary>是否处于对战场景（战斗内 BGM 覆盖只在此生效；用棋盘对象存在性判定，纯表现层判断）</summary>
        public static bool IsBattleContext
        {
            get
            {
                try
                {
                    return TcgEngine.Client.GameBoard.Get() != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static BgmManager Get()
        {
            Ensure();
            return instance;
        }

        private static void Ensure()
        {
            if (instance != null)
                return;
            GameObject go = new GameObject("BgmManager");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BgmManager>();
            BgmLibrary.Ensure();
        }

        // ---------------- 对外接口 ----------------

        /// <summary>切到某界面的 BGM。fallback/fallback_volume 用于迁移期兼容（配置里没配时用场景硬挂的曲子）。</summary>
        public static void PlayFor(string scene_key, bool force = false, AudioClip fallback = null, float fallback_volume = 0.4f)
        {
            Ensure();
            instance.Apply(scene_key, force, fallback, fallback_volume);
        }

        /// <summary>停止 BGM（带淡出）</summary>
        public static void Stop(float fade = 0.6f)
        {
            Ensure();
            instance.battle_override = false;
            instance.battle_override_id = null;
            instance.current_bgm_id = null;
            instance.StopActive(fade);
        }

        /// <summary>界面显示通知（UIPanel.Show 统一调用；只有映射表里的面板会切歌）</summary>
        public static void NotifyPanelShown(string type_name)
        {
            if (string.IsNullOrEmpty(type_name))
                return;
            if (!PANEL_KEYS.TryGetValue(type_name, out string key))
                return;
            PlayFor(key);
        }

        /// <summary>战斗内覆盖 BGM（由 BgmBattleRuntime 派生调用）：仅在对战场景生效；bgm 为空 = 停止战斗 BGM</summary>
        public static void BattleSet(string bgm_id_or_title, float volume_override, float fade)
        {
            //AI 推演线程（后台线程）会执行图并派发换曲请求：表现层不能碰 Unity API，直接忽略
            if (!MainThreadUtil.IsMainThread)
                return;
            Ensure();
            if (!IsBattleContext)
            {
                Debug.LogWarning("[BGM] 忽略战斗换曲请求（当前不在对战场景）: " + bgm_id_or_title);
                return;
            }
            instance.battle_override = true;
            instance.battle_override_id = bgm_id_or_title ?? "";
            instance.ApplyForId(instance.battle_override_id, volume_override, fade, fade, "节点设置战斗BGM");
        }

        /// <summary>战斗内恢复默认 BGM（清掉覆盖后按对战界面的配置重放）</summary>
        public static void BattleRestoreDefault(float fade)
        {
            //同上：AI 推演线程上的请求一律忽略（表现层只在主线程执行）
            if (!MainThreadUtil.IsMainThread)
                return;
            Ensure();
            instance.battle_override = false;
            instance.battle_override_id = null;
            if (!IsBattleContext)
                return;
            SceneBgmConfig cfg = SceneBgmConfig.Get();
            SceneBgmConfig.Entry e = cfg.Find(BgmKeys.Battle);
            float vol = e != null ? e.volume : cfg.default_volume;
            float fin = fade > 0f ? fade : (e != null ? e.fade_in : cfg.default_fade_in);
            float fout = fade > 0f ? fade : (e != null ? e.fade_out : cfg.default_fade_out);

            string id = (e != null && e.IsPick) ? e.bgm_id : null;
            if (string.IsNullOrEmpty(id))
                id = cfg.default_bgm_id;
            if (string.IsNullOrEmpty(id))
            {
                BgmEntry de = BgmLibrary.GetDefault();   //库里第一首兜底
                if (de != null)
                    id = de.id;
            }
            instance.ApplyEntry(BgmKeys.Battle, e, null, id, vol, fin, fout, "恢复默认BGM", true);
        }

        // ---------------- 内部 ----------------

        private void Apply(string scene_key, bool force, AudioClip fallback, float fallback_volume)
        {
            if (string.IsNullOrEmpty(scene_key))
                return;

            bool entering_battle = scene_key == BgmKeys.Battle;
            if (!entering_battle && battle_override)
            {
                //离开对战：清掉战斗内覆盖（战斗里的临时 BGM 不应影响主菜单/编辑页）
                battle_override = false;
                battle_override_id = null;
            }

            //同一界面且正在播：不重启（除非 force）
            bool playing = AudioTool.Get().IsMusicPlaying(active_channel);
            if (!force && !battle_override && scene_key == current_key && playing && !string.IsNullOrEmpty(current_bgm_id))
                return;

            SceneBgmConfig cfg = SceneBgmConfig.Get();
            SceneBgmConfig.Entry e = cfg.Find(scene_key);
            current_key = scene_key;

            float vol = e != null ? e.volume : cfg.default_volume;
            float fin = e != null ? e.fade_in : cfg.default_fade_in;
            float fout = e != null ? e.fade_out : cfg.default_fade_out;

            //策略①：该界面不播 BGM
            if (e != null && e.IsMute)
            {
                current_bgm_id = null;
                StopActive(fout);
                return;
            }

            //结算：默认停 BGM（交给胜负音乐）——只有显式"指定曲目"时才播
            if (scene_key == BgmKeys.EndGame && cfg.end_game_stop_bgm && (e == null || !e.IsPick))
            {
                current_bgm_id = null;
                StopActive(fout);
                return;
            }

            //策略②③：指定曲目 / 默认兜底；默认兜底再退到"音乐库第一首"，保证有素材就有声
            string id = (e != null && e.IsPick) ? e.bgm_id : null;
            if (string.IsNullOrEmpty(id))
                id = cfg.default_bgm_id;
            if (string.IsNullOrEmpty(id))
            {
                BgmEntry de = BgmLibrary.GetDefault();
                if (de != null)
                    id = de.id;
            }

            ApplyEntry(scene_key, e, fallback, id, vol, fin, fout, "界面:" + scene_key, force, fallback_volume);
        }

        /// <summary>按已解析好的曲目 id 播放；id 为空时退到调用方 fallback，仍为空则保持当前音乐。
        /// fallback_volume &lt; 0 表示沿用配置音量（volume）。</summary>
        private void ApplyEntry(string scene_key, SceneBgmConfig.Entry e, AudioClip fallback, string resolved_id,
            float volume, float fade_in, float fade_out, string reason, bool force, float fallback_volume = -1f)
        {
            current_key = scene_key;

            string id = resolved_id;

            if (!string.IsNullOrEmpty(id))
            {
                if (!force && id == current_bgm_id && AudioTool.Get().IsMusicPlaying(active_channel))
                    return;                                  //同曲不重启
                BgmEntry entry = BgmLibrary.Find(id);
                if (entry != null)
                {
                    ApplyForId(entry.id, volume, fade_in, fade_out, reason);
                    return;
                }
                Debug.LogWarning("[BGM] " + reason + " 配置的 BGM 不存在于音乐库: " + id + "（回退 fallback/兜底）");
            }

            //兜底：调用方给的 fallback（迁移期：主菜单/对战场景硬挂的曲子）
            if (fallback != null)
            {
                float fvol = fallback_volume > 0f ? fallback_volume : volume;
                PlayCrossfade(fallback, fvol, Mathf.Max(fade_in, 0f), Mathf.Max(fade_out, 0f));
                current_bgm_id = "fallback:" + fallback.name;
                return;
            }

            //三档都空：按需求"未配置的界面保持当前音乐"——不切歌、不静音、不报错
            //（结算界面"停 BGM"已在 Apply 里单独处理，不受这里影响）
            Debug.Log("[BGM] " + reason + " 未配置曲目（也无默认兜底/fallback）→ 保持当前 BGM："
                + (string.IsNullOrEmpty(current_bgm_id) ? "无" : current_bgm_id));
            current_key = scene_key;
        }

        /// <summary>按音乐库 id 播放（volume_override &lt; 0 表示用库里的音量）</summary>
        private void ApplyForId(string bgm_id, float volume_override, float fade_in, float fade_out, string reason)
        {
            if (string.IsNullOrEmpty(bgm_id))
            {
                current_bgm_id = null;
                StopActive(fade_out);
                return;
            }
            BgmEntry entry = BgmLibrary.Find(bgm_id);
            if (entry == null)
            {
                Debug.LogWarning("[BGM] " + reason + " 找不到 BGM: " + bgm_id);
                current_bgm_id = null;
                StopActive(fade_out);
                return;
            }
            float vol = volume_override >= 0f ? volume_override : BgmLibrary.VolumeOf(entry, 0.5f);
            BgmLibrary.LoadClip(entry, (clip, err) =>
            {
                if (clip == null)
                {
                    Debug.LogWarning("[BGM] " + reason + " 加载失败: " + err);
                    return;
                }
                PlayCrossfade(clip, vol, fade_in, fade_out);
                current_bgm_id = entry.id;
                Debug.Log("[BGM] 播放 " + entry.title + "（" + BgmLibrary.SourceLabel(entry) + "）音量 " +
                    Mathf.RoundToInt(vol * 100f) + "% 淡入 " + fade_in + "s / 淡出 " + fade_out + "s ← " + reason);
            });
        }

        /// <summary>双通道交叉淡入淡出：旧通道淡出、新通道淡入（真正的重叠过渡，不是先停后播）</summary>
        private void PlayCrossfade(AudioClip clip, float volume, float fade_in, float fade_out)
        {
            if (clip == null)
                return;

            AudioTool tool = AudioTool.Get();
            string next = active_channel == CHANNEL_A ? CHANNEL_B : CHANNEL_A;

            //同一首已在播：只校正音量，不重启
            if (tool.GetMusicClip(active_channel) == clip && tool.IsMusicPlaying(active_channel))
            {
                tool.PlayMusicFade(active_channel, clip, volume, 0.01f, true);
                return;
            }

            if (tool.IsMusicPlaying(active_channel))
                tool.StopMusicFade(active_channel, Mathf.Max(fade_out, 0.01f));
            else
                tool.StopMusic(active_channel);

            tool.PlayMusicFade(next, clip, Mathf.Max(volume, 0.001f), Mathf.Max(fade_in, 0.01f), true);
            active_channel = next;
        }

        private void StopActive(float fade)
        {
            AudioTool tool = AudioTool.Get();
            if (tool.IsMusicPlaying(active_channel))
                tool.StopMusicFade(active_channel, Mathf.Max(fade, 0.01f));
            //另一条通道也一并淡出（防御：中断中的过渡残留）
            string other = active_channel == CHANNEL_A ? CHANNEL_B : CHANNEL_A;
            if (tool.IsMusicPlaying(other))
                tool.StopMusicFade(other, Mathf.Max(fade, 0.01f));
        }
    }
}
