using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TcgEngine.Workshop;

namespace TcgEngine.Audio
{
    /// <summary>界面标识（场景 BGM 配置表的 key）：新增界面时在这里加常量即可</summary>
    public static class BgmKeys
    {
        public const string Login = "login";              //登录页
        public const string MainMenu = "main_menu";       //主菜单
        public const string Collection = "collection";    //构筑/组卡
        public const string CardPool = "card_pool";       //卡池管理
        public const string CardEditor = "card_editor";   //卡牌编辑（含规则图编辑器）
        public const string Battle = "battle";            //对战
        public const string EndGame = "end_game";         //结算

        public static readonly string[] All =
        {
            Login, MainMenu, Collection, CardPool, CardEditor, Battle, EndGame
        };

        public static string DisplayName(string key)
        {
            switch (key)
            {
                case Login: return "登录页";
                case MainMenu: return "主菜单";
                case Collection: return "构筑/组卡";
                case CardPool: return "卡池管理";
                case CardEditor: return "卡牌编辑";
                case Battle: return "对战";
                case EndGame: return "结算";
                default: return key;
            }
        }
    }

    /// <summary>一条 BGM 素材：official=项目内置（Resources/BGM，只读）；导入的=玩家放的本地文件（存在 Workshop/Audio）</summary>
    [Serializable]
    public class BgmEntry
    {
        public string id;          //唯一标识：官方="official:名字"；导入=文件名
        public string title;       //显示名（可重命名）
        public string file;        //导入素材的文件名（官方为空）
        public bool official;      //来源：true=项目内置，false=玩家导入
        public float volume = 0.5f;

        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(id); }
        }
    }

    [Serializable]
    public class BgmLibraryData
    {
        public List<BgmEntry> entries = new List<BgmEntry>();
        public string default_bgm;          //默认兜底 BGM 的 id（未配置的界面用它）
    }

    /// <summary>
    /// BGM 素材库（数据层）：
    /// ① 官方内置：Resources/BGM 下的 AudioClip（只读，来源标记"官方"）；
    /// ② 玩家导入：复制到 persistentDataPath/Workshop/Audio 并在 bgm_library.json 登记（来源标记"导入"）；
    /// ③ 统一加载接口 LoadClip(entry)：官方走 Resources，导入走 CardAudioLoader（缓存/异步解码）——调用方无需区分来源。
    /// </summary>
    public static class BgmLibrary
    {
        /// <summary>官方 BGM 的 Resources 子目录（把 AudioClip 放进 Assets/任意/Resources/BGM/ 即自动出现在库里）</summary>
        public const string OfficialFolder = "BGM";

        private static readonly string[] SUPPORTED_EXT = { ".wav", ".ogg", ".mp3", ".aif", ".aiff" };

        private static BgmLibraryData data;

        public static string ConfigPath
        {
            get { return Path.Combine(CardPoolIO.SaveFolder, "bgm_library.json"); }
        }

        public static BgmLibraryData Data
        {
            get
            {
                Ensure();
                return data;
            }
        }

        public static void Ensure()
        {
            if (data != null)
                return;
            Reload();
        }

        /// <summary>重新扫描官方 BGM 并读取导入清单（保留导入条目的标题与音量）</summary>
        public static void Reload()
        {
            data = LoadJson();
            if (data == null)
                data = new BgmLibraryData();
            if (data.entries == null)
                data.entries = new List<BgmEntry>();

            //官方条目：每次重新扫描（Resources 内容变化后自动跟上）
            //① 优先 Resources/BGM/ 目录；② 再补扫整个 Resources 里已有的 AudioClip（项目自带音乐/音效也能直接选用，
            //   否则"音乐库为空 → 各界面都配不了曲"，用户会看不到任何 BGM）
            List<BgmEntry> official = new List<BgmEntry>();
            HashSet<string> seen_names = new HashSet<string>();
            AppendOfficial(official, seen_names, Resources.LoadAll<AudioClip>(OfficialFolder));
            AppendOfficial(official, seen_names, Resources.LoadAll<AudioClip>(""));
            official.Sort((a, b) => string.CompareOrdinal(a.title, b.title));

            //导入条目：清单里有、但文件已被外部删除的，保留条目并标记（加载时会失败提示）
            List<BgmEntry> imported = new List<BgmEntry>();
            for (int i = 0; i < data.entries.Count; i++)
            {
                BgmEntry e = data.entries[i];
                if (e != null && !e.official && !string.IsNullOrEmpty(e.file))
                    imported.Add(e);
            }

            data.entries = new List<BgmEntry>();
            data.entries.AddRange(official);
            data.entries.AddRange(imported);

            //默认兜底失效时清空
            if (!string.IsNullOrEmpty(data.default_bgm) && FindIn(data.entries, data.default_bgm) == null)
                data.default_bgm = "";
        }

        public static List<BgmEntry> GetAll()
        {
            Ensure();
            return data.entries;
        }

        public static BgmEntry Find(string id_or_title)
        {
            if (string.IsNullOrEmpty(id_or_title))
                return null;
            Ensure();
            BgmEntry by_id = FindIn(data.entries, id_or_title);
            if (by_id != null)
                return by_id;
            for (int i = 0; i < data.entries.Count; i++)   //允许按显示名引用（节点里填中文标题更直观）
            {
                BgmEntry e = data.entries[i];
                if (e != null && string.Equals(e.title, id_or_title, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        public static BgmEntry GetDefault()
        {
            Ensure();
            BgmEntry e = Find(data.default_bgm);
            if (e != null)
                return e;
            return data.entries.Count > 0 ? data.entries[0] : null;
        }

        public static void SetDefault(BgmEntry e)
        {
            Ensure();
            data.default_bgm = e != null ? e.id : "";
            Save();
        }

        /// <summary>导入本地音频文件（复制进 Workshop/Audio 并登记）；失败返回 null 并给出原因</summary>
        public static BgmEntry Import(string src_path, string title, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(src_path) || !File.Exists(src_path))
            {
                error = "文件不存在";
                return null;
            }
            if (!IsSupportedFile(src_path))
            {
                error = "不支持的音频格式（支持 wav/ogg/mp3/aif/aiff）";
                return null;
            }
            try
            {
                Ensure();
                Directory.CreateDirectory(CardPoolIO.AudioFolder);
                string ext = Path.GetExtension(src_path);
                if (string.IsNullOrEmpty(ext))
                    ext = ".wav";
                string base_name = string.IsNullOrEmpty(title) ? Path.GetFileNameWithoutExtension(src_path) : title;
                string file = "bgm_" + Sanitize(base_name) + "_" + DateTime.Now.ToString("HHmmssfff") + ext.ToLowerInvariant();
                File.Copy(src_path, Path.Combine(CardPoolIO.AudioFolder, file), true);

                BgmEntry e = new BgmEntry
                {
                    id = file,
                    title = base_name,
                    file = file,
                    official = false,
                    volume = 0.5f,
                };
                data.entries.Add(e);
                Save();
                return e;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static bool Rename(BgmEntry e, string new_title)
        {
            if (e == null || string.IsNullOrEmpty(new_title))
                return false;
            e.title = new_title.Trim();
            Save();
            return true;
        }

        /// <summary>删除：官方条目不可删；导入条目连同文件一起删除</summary>
        public static bool Delete(BgmEntry e, out string error)
        {
            error = null;
            if (e == null)
                return false;
            Ensure();
            if (e.official)
            {
                error = "内置 BGM 不可删除（如需隐藏请从 Resources/BGM 移除资源）";
                return false;
            }
            try
            {
                if (!string.IsNullOrEmpty(e.file))
                {
                    string path = Path.Combine(CardPoolIO.AudioFolder, e.file);
                    if (File.Exists(path))
                        File.Delete(path);
                }
                data.entries.Remove(e);
                if (data.default_bgm == e.id)
                    data.default_bgm = "";
                Save();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>统一加载接口：官方=Resources 同步取；导入=CardAudioLoader 缓存/异步解码。回调可能在同帧同步触发。</summary>
        public static void LoadClip(BgmEntry e, Action<AudioClip, string> onDone)
        {
            if (e == null)
            {
                if (onDone != null)
                    onDone(null, "BGM 条目为空");
                return;
            }
            if (e.official)
            {
                string name = e.id.StartsWith("official:") ? e.id.Substring("official:".Length) : e.title;
                AudioClip clip = Resources.Load<AudioClip>(OfficialFolder + "/" + name);
                if (onDone != null)
                    onDone(clip, clip != null ? null : ("找不到内置 BGM：" + name));
                return;
            }

            CardAudioLoader.LoadClip(e.file, clip =>
            {
                if (onDone != null)
                    onDone(clip, clip != null ? null : ("找不到或无法解码音频：" + e.file));
            });
        }

        public static string SourceLabel(BgmEntry e)
        {
            if (e == null)
                return "";
            return e.official ? "官方" : "导入";
        }

        public static float VolumeOf(BgmEntry e, float fallback)
        {
            if (e == null)
                return fallback;
            return e.volume > 0.001f ? e.volume : fallback;
        }

        public static bool IsSupportedFile(string path)
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

        // ---------------- 内部 ----------------

        /// <summary>把一批 Resources 音频并入官方条目（按名字去重，保留用户在配置里改过的标题/音量）</summary>
        private static void AppendOfficial(List<BgmEntry> list, HashSet<string> seen, AudioClip[] clips)
        {
            if (clips == null)
                return;
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip c = clips[i];
                if (c == null || string.IsNullOrEmpty(c.name) || seen.Contains(c.name))
                    continue;
                seen.Add(c.name);
                string id = "official:" + c.name;
                BgmEntry old = FindIn(data.entries, id);
                list.Add(new BgmEntry
                {
                    id = id,
                    title = old != null ? old.title : c.name,
                    file = "",
                    official = true,
                    volume = old != null ? old.volume : 0.5f,
                });
            }
        }

        private static BgmEntry FindIn(List<BgmEntry> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].id == id)
                    return list[i];
            }
            return null;
        }

        private static BgmLibraryData LoadJson()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return null;
                string json = File.ReadAllText(ConfigPath);
                return JsonUtility.FromJson<BgmLibraryData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BGM] 读取音乐库失败: " + e.Message);
                return null;
            }
        }

        public static void Save()
        {
            try
            {
                Ensure();
                Directory.CreateDirectory(CardPoolIO.SaveFolder);
                File.WriteAllText(ConfigPath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BGM] 保存音乐库失败: " + e.Message);
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "bgm";
            char[] invalid = Path.GetInvalidFileNameChars();
            string s = name.Trim();
            for (int i = 0; i < invalid.Length; i++)
                s = s.Replace(invalid[i], '_');
            s = s.Replace(' ', '_');
            return s.Length > 24 ? s.Substring(0, 24) : s;
        }
    }
}
