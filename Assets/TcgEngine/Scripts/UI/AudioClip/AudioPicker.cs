using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>一个可选的音频素材（本地文件 / Workshop 图库文件 / Resources 音频资源）</summary>
    public class AudioPickItem
    {
        public string display;      //界面显示名
        public string fname;        //Workshop 文件名（图库项）
        public string path;         //绝对路径（本地文件/图库文件）
        public AudioClip preloaded; //已在内存里的资源（Resources 音频）
        public bool is_resource;

        public bool IsValid
        {
            get { return preloaded != null || !string.IsNullOrEmpty(path); }
        }
    }

    /// <summary>
    /// 音效素材选材（与弹框 UI 解耦）：
    /// ① 本地文件：系统文件对话框（仅 Windows，走 FileBrowserBridge）；
    /// ② 项目图库：Workshop/Audio 目录下已导入的音频文件；
    /// ③ 项目 Resources：Resources 下的 AudioClip 资源。
    /// 解码统一走 CardAudioLoader（异步 UnityWebRequest，运行时不能同步解码 mp3/ogg）。
    /// </summary>
    public static class AudioPicker
    {
        /// <summary>支持的音频扩展名（与 CardAudioLoader.GetAudioType 对齐）</summary>
        public static readonly string[] SUPPORTED_EXT = { ".wav", ".ogg", ".mp3", ".aif", ".aiff" };

        public static bool IsSupportedAudio(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return false;
            ext = ext.ToLowerInvariant();
            for (int i = 0; i < SUPPORTED_EXT.Length; i++)
            {
                if (SUPPORTED_EXT[i] == ext)
                    return true;
            }
            return false;
        }

        /// <summary>打开本地音频文件对话框；取消 / 平台不支持返回 null</summary>
        public static string OpenLocalFile()
        {
            string path = FileBrowserBridge.OpenAudioFile("选择音频素材");
            if (string.IsNullOrEmpty(path))
                return null;
            if (!IsSupportedAudio(path))
            {
                Debug.LogWarning("[音效DIY] 不支持的音频格式: " + path);
                return null;
            }
            return path;
        }

        /// <summary>Workshop/Audio 目录下已有音频（按文件名排序），即"项目已导入图库"</summary>
        public static List<AudioPickItem> GetWorkshopLibrary()
        {
            List<AudioPickItem> items = new List<AudioPickItem>();
            try
            {
                string dir = CardPoolIO.AudioFolder;
                if (!Directory.Exists(dir))
                    return items;
                string[] files = Directory.GetFiles(dir);
                List<string> kept = new List<string>();
                for (int i = 0; i < files.Length; i++)
                {
                    if (IsSupportedAudio(files[i]))
                        kept.Add(files[i]);
                }
                kept.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < kept.Count; i++)
                {
                    items.Add(new AudioPickItem
                    {
                        display = Path.GetFileName(kept[i]),
                        fname = Path.GetFileName(kept[i]),
                        path = kept[i],
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[音效DIY] 读取音频图库失败: " + e.Message);
            }
            return items;
        }

        /// <summary>Resources 下的 AudioClip 资源（真·项目图库）</summary>
        public static List<AudioPickItem> GetResourceLibrary()
        {
            List<AudioPickItem> items = new List<AudioPickItem>();
            try
            {
                AudioClip[] clips = Resources.LoadAll<AudioClip>("");
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] == null)
                        continue;
                    items.Add(new AudioPickItem
                    {
                        display = clips[i].name,
                        preloaded = clips[i],
                        is_resource = true,
                    });
                }
                items.Sort((a, b) => string.CompareOrdinal(a.display, b.display));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[音效DIY] 读取 Resources 音频失败: " + e.Message);
            }
            return items;
        }

        /// <summary>载入素材：Resources 资源直接返回；Workshop 文件走缓存；其它绝对路径单次解码</summary>
        public static void Load(AudioPickItem item, Action<AudioClip, string> onDone)
        {
            if (item == null)
            {
                if (onDone != null)
                    onDone(null, "素材为空");
                return;
            }
            if (item.preloaded != null)
            {
                if (onDone != null)
                    onDone(item.preloaded, null);
                return;
            }
            if (string.IsNullOrEmpty(item.path))
            {
                if (onDone != null)
                    onDone(null, "素材路径为空");
                return;
            }
            if (!string.IsNullOrEmpty(item.fname))
                LoadWorkshop(item.fname, onDone);       //图库文件复用缓存
            else
                LoadPath(item.path, onDone);
        }

        /// <summary>按 Workshop 文件名加载（复用 CardAudioLoader 缓存）</summary>
        public static void LoadWorkshop(string fname, Action<AudioClip, string> onDone)
        {
            CardAudioLoader.LoadClip(fname, clip =>
            {
                if (onDone != null)
                    onDone(clip, clip != null ? null : ("找不到或无法解码音频：" + fname));
            });
        }

        /// <summary>按绝对路径加载（不进缓存）</summary>
        public static void LoadPath(string full_path, Action<AudioClip, string> onDone)
        {
            CardAudioLoader.LoadExternal(full_path, onDone);
        }

        /// <summary>给"音效位当前音效"生成展示名（去掉卡 id 前缀更易读）</summary>
        public static string DisplayName(string fname)
        {
            if (string.IsNullOrEmpty(fname))
                return "（空）";
            return fname;
        }
    }
}
