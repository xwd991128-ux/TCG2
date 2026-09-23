using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 自定义卡音频加载器：把 Workshop/Audio 目录下的音频文件（mp3/wav/ogg）异步解码为
    /// AudioClip 并写回 CardData（spawn_audio/attack_audio/death_audio/damage_audio），
    /// 使规则编辑器里给卡选的音效能在真实对局中播放。结果按文件名缓存，重复加载不重复解码。
    /// 说明：Unity 运行时不能同步解码 mp3/ogg，故必须异步（file:// 请求）。
    /// </summary>
    public class CardAudioLoader : MonoBehaviour
    {
        private static CardAudioLoader instance;
        private static Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        //正在加载的文件 → 等待回调列表：同一文件被多张卡引用时，全部订阅者都要收到结果
        //（原来是 HashSet<string> + 单个 apply，第二张卡的回调会被静默丢弃 → 它的音效永远是空）
        private static readonly Dictionary<string, List<Action<AudioClip>>> loading = new Dictionary<string, List<Action<AudioClip>>>();

        private static void Ensure()
        {
            if (instance != null)
                return;
            //★ 本项目关闭了域重载（EditorSettings: EnterPlayModeOptions=DisableDomainReload），
            //  静态状态会跨 Play 存活：上一个实例被销毁时它的协程一并终止，
            //  loading 里的条目再也不会被回调 → 同一文件在后续 Play 会永久卡在"加载中"（音效静默为空）；
            //  cache 里的 AudioClip 是运行时创建的，跨 Play 后已失效，也必须重新解码。
            //  所以新建实例（= 上一个实例已不在）时把两张表都清掉。
            loading.Clear();
            cache.Clear();
            GameObject go = new GameObject("CardAudioLoader");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CardAudioLoader>();
        }

        /// <summary>为一张运行时自定义卡补载 4 个音频槽（有 id、尚未赋值且未在加载中才发请求）</summary>
        public static void LoadCardAudio(CardCustomData data, CardData card)
        {
            if (data == null || card == null)
                return;
            LoadSlot(data.spawn_audio_id, clip => { if (card != null) card.spawn_audio = clip; });
            LoadSlot(data.attack_audio_id, clip => { if (card != null) card.attack_audio = clip; });
            LoadSlot(data.death_audio_id, clip => { if (card != null) card.death_audio = clip; });
            LoadSlot(data.damage_audio_id, clip => { if (card != null) card.damage_audio = clip; });
        }

        /// <summary>编辑器试听：把 Workshop/Audio 下的音频解码后播放一次（找不到文件/解码失败则静默）。
        /// 返回 false 表示文件名无效（调用方据此提示）。</summary>
        public static void Preview(string fname)
        {
            if (string.IsNullOrEmpty(fname))
                return;
            LoadSlot(fname, clip => { if (clip != null) PlayPreview(clip); });
        }

        /// <summary>当前是否已缓存该音频（试听前判断，避免重复请求）</summary>
        public static bool IsCached(string fname)
        {
            return !string.IsNullOrEmpty(fname) && cache.ContainsKey(fname);
        }

        /// <summary>按文件名取音频（走同一份缓存）：传给"音效DIY"等编辑流程复用，避免重复解码。
        /// 文件名无效/文件缺失/解码失败 → 回调 null（不抛异常）。</summary>
        public static void LoadClip(string fname, Action<AudioClip> onDone)
        {
            if (string.IsNullOrEmpty(fname))
            {
                if (onDone != null)
                    onDone(null);
                return;
            }
            LoadSlot(fname, onDone);
        }

        /// <summary>文件被覆盖重写后让缓存失效（音效DIY保存后必须调用，否则试听与对局仍是旧音频）</summary>
        public static void Invalidate(string fname)
        {
            if (!string.IsNullOrEmpty(fname))
                cache.Remove(fname);
        }

        /// <summary>从任意绝对路径解码音频（不进播放缓存；用于导入尚未落盘的素材）。
        /// 回调 (clip, error)：成功时 error 为空；失败时 clip 为 null 且 error 为中文原因。</summary>
        public static void LoadExternal(string full_path, Action<AudioClip, string> onDone)
        {
            if (string.IsNullOrEmpty(full_path) || !File.Exists(full_path))
            {
                if (onDone != null)
                    onDone(null, "文件不存在或路径无效");
                return;
            }
            Ensure();
            instance.StartCoroutine(instance.LoadExternalCo(full_path, onDone));
        }

        private IEnumerator LoadExternalCo(string full_path, Action<AudioClip, string> onDone)
        {
            string url = new Uri(full_path).AbsoluteUri;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, GetAudioType(full_path)))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (onDone != null)
                        onDone(null, "读取失败：" + req.error);
                    yield break;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null)
                {
                    if (onDone != null)
                        onDone(null, "解码失败（格式不支持或文件损坏）");
                    yield break;
                }
                clip.name = Path.GetFileName(full_path);
                if (onDone != null)
                    onDone(clip, null);
            }
        }

        /// <summary>校验任意音频文件的时长（编辑器导入用，读绝对路径）：≤ max_seconds 回调 ok=true。
        /// 走 file:// 请求读时长，不进播放缓存。</summary>
        public static void ValidateLength(string full_path, float max_seconds, Action<bool, float> onResult)
        {
            if (string.IsNullOrEmpty(full_path) || !File.Exists(full_path))
            {
                if (onResult != null)
                    onResult(false, 0f);
                return;
            }
            Ensure();
            instance.StartCoroutine(instance.Validate(full_path, max_seconds, onResult));
        }

        private IEnumerator Validate(string full_path, float max_seconds, Action<bool, float> onResult)
        {
            string url = new Uri(full_path).AbsoluteUri;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, GetAudioType(full_path)))
            {
                yield return req.SendWebRequest();
                float len = 0f;
                bool ok = false;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null)
                    {
                        len = clip.length;
                        ok = len <= max_seconds;
                        Destroy(clip);
                    }
                }
                if (onResult != null)
                    onResult(ok, len);
            }
        }

        private static void PlayPreview(AudioClip clip)
        {
            Ensure();
            AudioSource src = instance.GetComponent<AudioSource>();
            if (src == null)
                src = instance.gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.Stop();
            src.clip = clip;
            src.Play();
        }

        private static void LoadSlot(string fname, Action<AudioClip> apply)
        {
            if (string.IsNullOrEmpty(fname))
                return;
            Ensure();
            if (cache.TryGetValue(fname, out AudioClip cached))
            {
                apply(cached);
                return;
            }
            if (loading.TryGetValue(fname, out List<Action<AudioClip>> waiters))
            {
                waiters.Add(apply);   //同一文件正在加载：挂到等待列表，加载完成后统一回调
                return;
            }
            loading[fname] = new List<Action<AudioClip>> { apply };
            instance.StartCoroutine(instance.Load(fname));
        }

        private IEnumerator Load(string fname)
        {
            string path = Path.Combine(CardPoolIO.AudioFolder, fname);
            string url = new Uri(path).AbsoluteUri;
            AudioType type = GetAudioType(fname);

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return req.SendWebRequest();

                AudioClip clip = null;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null)
                        clip.name = fname;
                    cache[fname] = clip;
                }
                else
                {
                    Debug.LogWarning("[音频] 加载失败: " + fname + " " + req.error);
                }

                //先把等待表摘掉再回调，避免回调内部又请求同一文件时被当成「已在加载」
                List<Action<AudioClip>> waiters = loading.TryGetValue(fname, out List<Action<AudioClip>> w) ? w : null;
                loading.Remove(fname);
                if (waiters != null)
                {
                    foreach (Action<AudioClip> cb in waiters)
                        cb?.Invoke(clip);
                }
            }
        }

        /// <summary>按文件扩展名选择解码类型（未知时交给 Unity 自动探测）</summary>
        private static AudioType GetAudioType(string fname)
        {
            string ext = Path.GetExtension(fname ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".aif":
                case ".aiff": return AudioType.AIFF;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
