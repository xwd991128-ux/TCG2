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
        private static HashSet<string> loading = new HashSet<string>();

        private static void Ensure()
        {
            if (instance != null)
                return;
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
            if (loading.Contains(fname))
                return;   //加载中，完成后由缓存回调统一赋值
            loading.Add(fname);
            instance.StartCoroutine(instance.Load(fname, apply));
        }

        private IEnumerator Load(string fname, Action<AudioClip> apply)
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
                loading.Remove(fname);
                apply(clip);
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
