using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TcgEngine.UI;   //AudioPicker：本地文件 / Workshop 图库 / Resources 三来源统一解码

namespace TcgEngine.VFX
{
    /// <summary>
    /// 特效音效播放（**表现层，仅主线程**）：
    /// · 素材与解码完全复用项目既有链路（`AudioPicker` → `CardAudioLoader`）：本地绝对路径直接解码、
    ///   Workshop 音频文件名走图库缓存、其它名字当 Resources 里的 AudioClip；
    /// · 运行时无法同步解码 mp3/ogg，解码是异步的 → **首次**播放可能比动画晚几十毫秒，成功后进缓存即点即响；
    /// · 用内部音频源池播放，音效不随特效对象销毁（避免动画结束把音效掐断）；
    /// · 不在主线程 / 无素材时静默返回，绝不外溢异常（AI 推演线程会执行同一张图）。
    /// </summary>
    public static class VFXAudio
    {
        private const int POOL_SIZE = 4;

        private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        private static readonly List<AudioSource> pool = new List<AudioSource>();
        private static int pool_index;
        private static Transform root;

        /// <summary>播放一次音效（volume 0~1；loop=循环）。路径为空直接返回。</summary>
        public static void Play(string path, float volume = 1f, bool loop = false)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!MainThreadUtil.IsMainThread)
                return;   //AI 推演阶段不出声（并与跨线程 Unity API 访问隔离）

            float vol = Mathf.Clamp01(volume <= 0f ? 1f : volume);
            if (cache.TryGetValue(path, out AudioClip cached))
            {
                PlayClip(cached, vol, loop);
                return;
            }

            LoadClip(path, clip =>
            {
                if (clip == null)
                    return;
                cache[path] = clip;
                PlayClip(clip, vol, loop);
            });
        }

        /// <summary>停止所有正在播放的特效音（切换场景/结束战斗时可调）</summary>
        public static void StopAll()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                    pool[i].Stop();
            }
        }

        public static void ClearCache()
        {
            cache.Clear();
        }

        /// <summary>音效是否可直接播放（编辑器显示用）</summary>
        public static bool IsReady(string path)
        {
            return !string.IsNullOrEmpty(path) && cache.ContainsKey(path);
        }

        private static void LoadClip(string path, Action<AudioClip> onDone)
        {
            bool is_file = false;
            try { is_file = File.Exists(path); }
            catch (Exception) { }

            if (is_file)
            {
                AudioPicker.LoadPath(path, (clip, err) => onDone(clip));
                return;
            }

            //非绝对路径：先按 Workshop 图库文件名找，再退回 Resources 资源
            AudioPicker.LoadWorkshop(path, (clip, err) =>
            {
                if (clip != null)
                {
                    onDone(clip);
                    return;
                }
                AudioClip res = null;
                try { res = Resources.Load<AudioClip>(path); }
                catch (Exception) { }
                onDone(res);
            });
        }

        private static void PlayClip(AudioClip clip, float volume, bool loop)
        {
            if (clip == null)
                return;
            AudioSource src = GetSource();
            if (src == null)
                return;
            src.Stop();
            src.loop = loop;
            src.volume = volume;
            src.clip = clip;
            src.Play();
        }

        private static AudioSource GetSource()
        {
            if (pool.Count < POOL_SIZE)
            {
                if (root == null)
                {
                    GameObject r = new GameObject("VFXAudio");
                    UnityEngine.Object.DontDestroyOnLoad(r);
                    root = r.transform;
                }
                GameObject go = new GameObject("Sfx");
                go.transform.SetParent(root, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;      //2D：棋盘音效不需要空间衰减
                pool.Add(s);
                return s;
            }
            pool_index = (pool_index + 1) % pool.Count;
            return pool[pool_index];
        }
    }
}
