using System;
using System.IO;
using UnityEngine;
using TcgEngine.Workshop;

namespace TcgEngine.Audio
{
    /// <summary>每个界面的 BGM 播放策略</summary>
    public static class BgmModes
    {
        public const string Default = "default";   //用默认兜底曲（全局默认；没设就用音乐库第一首）
        public const string Pick = "pick";         //指定曲目（bgm_id）
        public const string Mute = "mute";         //该界面不播 BGM（淡出停止）

        public static string DisplayName(string mode)
        {
            switch (mode)
            {
                case Pick: return "指定曲目";
                case Mute: return "不播 BGM";
                default: return "默认兜底";
            }
        }

        public static readonly string[] All = { Default, Pick, Mute };
    }

    /// <summary>
    /// 场景 BGM 配置（ScriptableObject）：一行 = 界面标识 + 策略（指定/默认兜底/不播放）+ 曲目 + 音量 + 淡入淡出。
    ///
    /// 两级来源：
    /// ① 出厂默认：`Resources/SceneBgmConfig.asset`（编辑器菜单「TcgEngine/创建场景BGM配置」生成，可在 Inspector 编辑）；
    /// ② 运行时覆盖：`persistentDataPath/Workshop/scene_bgm.json` —— 由游戏内「界面BGM配置」面板保存，
    ///    存在时**优先于资产**（运行时可写、立即生效、随存档持久化）；
    ///    「恢复出厂配置」删除该 JSON 即可回到资产配置。
    /// </summary>
    [CreateAssetMenu(fileName = "SceneBgmConfig", menuName = "TcgEngine/创建场景BGM配置", order = 10)]
    public class SceneBgmConfig : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("界面标识：见 BgmKeys")]
            public string scene_key = BgmKeys.MainMenu;

            [Tooltip("策略：default=默认兜底 / pick=指定曲目 / mute=不播 BGM")]
            public string mode = BgmModes.Default;

            [Tooltip("指定曲目（mode=pick 时生效）：音乐库条目的 id 或显示名")]
            public string bgm_id;

            [Range(0f, 1f)]
            public float volume = 0.5f;

            public float fade_in = 0.8f;
            public float fade_out = 0.6f;

            public bool IsMute { get { return mode == BgmModes.Mute; } }
            public bool IsPick { get { return mode == BgmModes.Pick; } }
            public bool UseDefault { get { return mode == BgmModes.Default || string.IsNullOrEmpty(mode); } }
        }

        [Serializable]
        public class Data
        {
            public Entry[] entries = new Entry[0];
            public string default_bgm_id;
            public float default_volume = 0.4f;
            public float default_fade_in = 0.8f;
            public float default_fade_out = 0.6f;
            public bool end_game_stop_bgm = true;
        }

        [Header("界面 → BGM 映射")]
        public Entry[] entries = new Entry[0];

        [Header("默认兜底（策略=默认兜底、或指定曲目缺失时使用）")]
        [Tooltip("留空 = 自动取音乐库第一首（音乐库为空则不播，保持当前音乐）")]
        public string default_bgm_id;
        [Range(0f, 1f)]
        public float default_volume = 0.4f;
        public float default_fade_in = 0.8f;
        public float default_fade_out = 0.6f;

        [Header("结算")]
        [Tooltip("进入结算界面默认停止 BGM（让胜负音乐/音效接管）")]
        public bool end_game_stop_bgm = true;

        public const string ResourcePath = "SceneBgmConfig";
        public const string OverrideFileName = "scene_bgm.json";

        private static SceneBgmConfig cached;

        public static string OverridePath
        {
            get { return Path.Combine(CardPoolIO.SaveFolder, OverrideFileName); }
        }

        public static bool HasOverride
        {
            get { return File.Exists(OverridePath); }
        }

        public static SceneBgmConfig Get()
        {
            if (cached == null)
            {
                cached = Resources.Load<SceneBgmConfig>(ResourcePath);
                if (cached == null)
                {
                    cached = CreateInstance<SceneBgmConfig>();
                    cached.EnsureDefaultEntries();
                }
                cached.LoadOverrideIfAny();
            }
            return cached;
        }

        public static void ClearCache()
        {
            cached = null;
        }

        // ---------------- 覆盖（运行时配置） ----------------

        /// <summary>读取 Workshop/scene_bgm.json 覆盖当前配置（存在才生效）</summary>
        public void LoadOverrideIfAny()
        {
            if (!HasOverride)
                return;
            try
            {
                Data d = JsonUtility.FromJson<Data>(File.ReadAllText(OverridePath));
                if (d != null)
                {
                    ApplyData(d);
                    Debug.Log("[BGM] 已加载运行时界面BGM配置覆盖：" + OverridePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BGM] 读取界面BGM配置覆盖失败（忽略）: " + e.Message);
            }
        }

        public Data Capture()
        {
            Data d = new Data
            {
                entries = entries,
                default_bgm_id = default_bgm_id,
                default_volume = default_volume,
                default_fade_in = default_fade_in,
                default_fade_out = default_fade_out,
                end_game_stop_bgm = end_game_stop_bgm,
            };
            return d;
        }

        public void ApplyData(Data d)
        {
            if (d == null)
                return;
            if (d.entries != null && d.entries.Length > 0)
                entries = d.entries;
            default_bgm_id = d.default_bgm_id;
            default_volume = d.default_volume;
            default_fade_in = d.default_fade_in;
            default_fade_out = d.default_fade_out;
            end_game_stop_bgm = d.end_game_stop_bgm;
            EnsureDefaultEntries();
        }

        /// <summary>把当前配置写入 Workshop/scene_bgm.json（运行时可写、立即生效）</summary>
        public bool SaveOverride(out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(CardPoolIO.SaveFolder);
                File.WriteAllText(OverridePath, JsonUtility.ToJson(Capture(), true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>删除运行时覆盖，回到出厂资产配置</summary>
        public static void ClearOverride(out string error)
        {
            error = null;
            try
            {
                if (File.Exists(OverridePath))
                    File.Delete(OverridePath);
                ClearCache();
            }
            catch (Exception e)
            {
                error = e.Message;
            }
        }

        // ---------------- 查询/编辑 ----------------

        public Entry Find(string scene_key)
        {
            if (entries == null || string.IsNullOrEmpty(scene_key))
                return null;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e != null && e.scene_key == scene_key)
                    return e;
            }
            return null;
        }

        /// <summary>取（不存在则创建）某界面的配置行</summary>
        public Entry GetOrAdd(string scene_key)
        {
            Entry e = Find(scene_key);
            return e != null ? e : AddEntry(scene_key);
        }

        public void EnsureDefaultEntries()
        {
            if (entries == null)
                entries = new Entry[0];
            for (int i = 0; i < BgmKeys.All.Length; i++)
            {
                if (Find(BgmKeys.All[i]) == null)
                    AddEntry(BgmKeys.All[i]);
            }
        }

        public Entry AddEntry(string scene_key)
        {
            Entry e = new Entry { scene_key = scene_key };
            Entry[] bigger = new Entry[(entries != null ? entries.Length : 0) + 1];
            if (entries != null)
                Array.Copy(entries, bigger, entries.Length);
            bigger[bigger.Length - 1] = e;
            entries = bigger;
            return e;
        }
    }
}
