using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 一条筛选预设 = 一套完整的"筛选方案"：
    ///   query（高级筛选语法） + 勾选项（类型/费用/阵营/稀有度） + 卡池 + 金卡 + 排序。
    ///
    /// 向后兼容：老预设文件里只有 name/query 两个字段，JsonUtility 反序列化时其余字段取默认值
    /// （空列表/false/0）= "不限制"，所以**不需要迁移**，老文件继续可用。
    /// 引用类型一律存 **id/枚举名**（不存 SO 引用）：JsonUtility 存不了引用，且卡池/种族被删后也不会留脏引用。
    /// </summary>
    [Serializable]
    public class CardFilterPreset
    {
        public string name = "";         // 预设名（也是唯一键；由 AutoName 自动生成）
        public string query = "";        // 高级筛选语法原文（CardQuery 可解析）
        public string saved_at = "";     // 保存时间（仅展示）

        // ---- 完整方案（全部可选；为空 = 该维度不限制）----
        public string pool = "";                     // 卡池 key（pack:xxx / file:xxx / ""=全部卡池）
        public List<string> types = new List<string>();      // CardType 枚举名（"Spell"…）
        public List<string> teams = new List<string>();      // TeamData.id
        public List<string> rarities = new List<string>();   // RarityData.id
        public List<int> costs = new List<int>();            // 费用（7 表示 7+）
        public bool foil = false;                    // 仅金卡
        public int sort_by = 0;                      // 0名称 1法力 2颜色 3稀有度
        public bool sort_desc = false;               // 倒序

        /// <summary>是否有"勾选类"内容（用于判断要不要在名字里体现）</summary>
        public bool HasListState()
        {
            return (types != null && types.Count > 0)
                || (costs != null && costs.Count > 0)
                || (teams != null && teams.Count > 0)
                || (rarities != null && rarities.Count > 0)
                || !string.IsNullOrEmpty(pool) || foil;
        }

        /// <summary>人读摘要（预设列表里显示"这条筛什么"）</summary>
        public string Summary()
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(query))
                parts.Add(query);
            if (types != null && types.Count > 0)
                parts.Add("类型:" + string.Join(",", types.ToArray()));
            if (costs != null && costs.Count > 0)
                parts.Add("费用:" + string.Join(",", costs.ConvertAll(c => c.ToString()).ToArray()));
            if (teams != null && teams.Count > 0)
                parts.Add("阵营:" + string.Join(",", teams.ToArray()));
            if (rarities != null && rarities.Count > 0)
                parts.Add("稀有度:" + string.Join(",", rarities.ToArray()));
            if (!string.IsNullOrEmpty(pool))
                parts.Add("卡池:" + pool);
            if (foil)
                parts.Add("金卡");
            if (sort_by != 0 || sort_desc)
                parts.Add("排序:" + sort_by + (sort_desc ? "↓" : "↑"));
            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : "（无限制）";
        }

        /// <summary>
        /// 自动命名（免打字）：优先用语法原文；没有语法就用勾选摘要；都没有 = "全部卡牌"。
        /// 名字即唯一键，同名保存视为覆盖（可预期）。
        /// </summary>
        public static string AutoName(CardFilterPreset p)
        {
            if (p == null)
                return "";
            if (!string.IsNullOrEmpty(p.query))
                return p.query.Trim();

            List<string> parts = new List<string>();
            if (p.types != null && p.types.Count > 0)
                parts.Add("类型:" + string.Join(",", p.types.ToArray()));
            if (p.costs != null && p.costs.Count > 0)
                parts.Add("费用:" + string.Join(",", p.costs.ConvertAll(c => c.ToString()).ToArray()));
            if (p.teams != null && p.teams.Count > 0)
                parts.Add("阵营:" + string.Join(",", p.teams.ToArray()));
            if (p.rarities != null && p.rarities.Count > 0)
                parts.Add("稀有度:" + string.Join(",", p.rarities.ToArray()));
            if (!string.IsNullOrEmpty(p.pool))
                parts.Add("卡池:" + p.pool);
            if (p.foil)
                parts.Add("金卡");
            if (parts.Count == 0)
                return "全部卡牌";
            return string.Join(" ", parts.ToArray());
        }
    }

    /// <summary>预设文件（JsonUtility 需要一层容器）</summary>
    [Serializable]
    public class CardFilterPresetFile
    {
        public string timestamp = "";
        public List<CardFilterPreset> presets = new List<CardFilterPreset>();
        public List<string> recent = new List<string>();     // 最近使用（预设名，新的在前，最多 MaxRecent 条）
    }

    /// <summary>
    /// 构筑筛选预设的保存/读取/分享。
    /// 与卡池同一套做法：`Application.persistentDataPath/Workshop/filter_presets.json`
    /// （卡池加载会跳过没有 "cards" 字段的 json，因此两者互不干扰）。
    /// 分享：导出到 `Workshop/presets/&lt;名字&gt;.json`，别人把文件丢进同一目录即可导入。
    /// </summary>
    public static class CardFilterPresetIO
    {
        public const int MaxRecent = 8;

        private static CardFilterPresetFile cache;

        public static string SaveFolder
        {
            get { return Path.Combine(Application.persistentDataPath, "Workshop"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(SaveFolder, "filter_presets.json"); }
        }

        /// <summary>分享目录（导出/导入单个预设用）</summary>
        public static string ShareFolder
        {
            get { return Path.Combine(SaveFolder, "presets"); }
        }

        // ---------------- 读 ----------------

        public static List<CardFilterPreset> GetAll()
        {
            EnsureLoaded();
            return cache.presets;
        }

        public static CardFilterPreset Get(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            EnsureLoaded();
            for (int i = 0; i < cache.presets.Count; i++)
            {
                if (cache.presets[i] != null && cache.presets[i].name == name)
                    return cache.presets[i];
            }
            return null;
        }

        public static List<string> GetRecent()
        {
            EnsureLoaded();
            return cache.recent;
        }

        private static void EnsureLoaded()
        {
            if (cache != null)
                return;
            cache = new CardFilterPresetFile();
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    CardFilterPresetFile file = JsonUtility.FromJson<CardFilterPresetFile>(json);
                    if (file != null)
                        cache = file;
                }
                if (cache.presets == null)
                    cache.presets = new List<CardFilterPreset>();
                if (cache.recent == null)
                    cache.recent = new List<string>();
            }
            catch (Exception e)
            {
                Debug.LogError("[筛选预设] 读取失败: " + FilePath + " " + e.Message);
                cache = new CardFilterPresetFile();
            }
        }

        // ---------------- 写 ----------------

        /// <summary>保存整套筛选方案（同名覆盖）。返回是否保存成功。</summary>
        public static bool Save(CardFilterPreset preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.name))
                return false;
            EnsureLoaded();
            CardFilterPreset p = Get(preset.name);
            if (p == null)
            {
                p = new CardFilterPreset();
                p.name = preset.name;
                cache.presets.Add(p);
            }
            //逐字段拷贝（避免调用方后续改动同一个对象影响已存内容）
            p.query = preset.query ?? "";
            p.pool = preset.pool ?? "";
            p.foil = preset.foil;
            p.sort_by = preset.sort_by;
            p.sort_desc = preset.sort_desc;
            p.types = preset.types != null ? new List<string>(preset.types) : new List<string>();
            p.teams = preset.teams != null ? new List<string>(preset.teams) : new List<string>();
            p.rarities = preset.rarities != null ? new List<string>(preset.rarities) : new List<string>();
            p.costs = preset.costs != null ? new List<int>(preset.costs) : new List<int>();
            p.saved_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            MarkUsedInternal(p.name);
            WriteFile();
            Debug.Log("[筛选预设] 已保存「" + p.name + "」：" + p.Summary());
            return true;
        }

        /// <summary>兼容入口：只按"语法"保存（等价于一条纯语法预设）</summary>
        public static bool Save(string name, string query)
        {
            CardFilterPreset p = new CardFilterPreset();
            p.name = name;
            p.query = query ?? "";
            return Save(p);
        }

        public static bool Remove(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            EnsureLoaded();
            int removed = cache.presets.RemoveAll(x => x == null || x.name == name);
            cache.recent.RemoveAll(x => x == name);
            if (removed > 0)
            {
                WriteFile();
                Debug.Log("[筛选预设] 已删除「" + name + "」");
            }
            return removed > 0;
        }

        /// <summary>记录"最近使用"（预设名，新的在前）</summary>
        public static void MarkUsed(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            EnsureLoaded();
            MarkUsedInternal(name);
            WriteFile();
        }

        private static void MarkUsedInternal(string name)
        {
            cache.recent.Remove(name);
            cache.recent.Insert(0, name);
            while (cache.recent.Count > MaxRecent)
                cache.recent.RemoveAt(cache.recent.Count - 1);
        }

        private static void WriteFile()
        {
            try
            {
                if (!Directory.Exists(SaveFolder))
                    Directory.CreateDirectory(SaveFolder);
                cache.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(FilePath, JsonUtility.ToJson(cache, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[筛选预设] 保存失败: " + FilePath + " " + e.Message);
            }
        }

        // ---------------- 分享（导出 / 导入单个预设） ----------------

        /// <summary>导出单个预设到分享目录，返回导出文件路径（失败返回 null）</summary>
        public static string ExportPreset(string name)
        {
            CardFilterPreset p = Get(name);
            if (p == null)
                return null;
            try
            {
                if (!Directory.Exists(ShareFolder))
                    Directory.CreateDirectory(ShareFolder);
                string safe = SanitizeFileName(name);
                string path = Path.Combine(ShareFolder, safe + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(p, true));
                Debug.Log("[筛选预设] 已导出 → " + path);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError("[筛选预设] 导出失败: " + e.Message);
                return null;
            }
        }

        /// <summary>把分享目录里的预设合并进来，返回导入条数</summary>
        public static int ImportShared()
        {
            int count = 0;
            try
            {
                if (!Directory.Exists(ShareFolder))
                    return 0;
                string[] files = Directory.GetFiles(ShareFolder, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    string json = File.ReadAllText(files[i]);
                    CardFilterPreset p = JsonUtility.FromJson<CardFilterPreset>(json);
                    if (p == null || string.IsNullOrEmpty(p.name))
                        continue;
                    Save(p);                    //整套方案导入（同名覆盖：以导入的为准）
                    count++;
                }
                if (count > 0)
                    Debug.Log("[筛选预设] 已导入 " + count + " 条（来自 " + ShareFolder + "）");
            }
            catch (Exception e)
            {
                Debug.LogError("[筛选预设] 导入失败: " + e.Message);
            }
            return count;
        }

        private static string SanitizeFileName(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            string s = name;
            for (int i = 0; i < bad.Length; i++)
                s = s.Replace(bad[i], '_');
            return s;
        }
    }
}
