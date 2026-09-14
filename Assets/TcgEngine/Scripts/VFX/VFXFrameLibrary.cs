using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcgEngine.VFX
{
    /// <summary>
    /// 序列帧素材库：统一解析"特效帧"来源。
    ///
    /// 两个来源（都可选，按名字去重）：
    /// ① **Resources**：`Resources/VFX/` 及整个 Resources 下的 Sprite（符合"素材走 Resources"的既定约定，打包后可用）；
    /// ② **工程 Sprite 目录**：`Assets/TcgEngine/Sprites/{FX, Cards, UI, Icons}`（编辑器与带 Assets 目录的桌面端可直接用；
    ///    项目现有特效图都在 `Sprites/FX/`，这样不需要先把图搬进 Resources 就能立即试做特效）。
    ///
    /// 帧在配置里只存"来源 + 名字"（JSON 友好），运行时用 Find/ResolveFrames 还原成 Sprite。
    /// </summary>
    public static class VFXFrameLibrary
    {
        /// <summary>Resources 下的序列帧根目录（把帧放这里即可被打包）</summary>
        public const string ResourcesRoot = "VFX";

        /// <summary>工程 Sprites 子目录（Assets/TcgEngine/Sprites/&lt;名&gt;）</summary>
        public static readonly string[] ProjectFolders = { "FX", "UI", "Icons", "Cards" };

        public class FrameItem
        {
            public string name;
            public string source;      //"Resources" 或工程目录名（FX/Cards/...）
            public Sprite sprite;
        }

        private static List<FrameItem> all_items;
        private static readonly Dictionary<string, Sprite> project_cache = new Dictionary<string, Sprite>();

        /// <summary>全部候选帧（懒加载 + 缓存）</summary>
        public static List<FrameItem> GetAll()
        {
            if (all_items != null)
                return all_items;

            all_items = new List<FrameItem>();
            HashSet<string> seen = new HashSet<string>();

            //① Resources（VFX 子目录优先，再补整个 Resources）
            AppendResources(all_items, seen, ResourcesRoot);
            AppendResources(all_items, seen, "");

            //② 工程 Sprite 目录（编辑器/桌面端）
            for (int i = 0; i < ProjectFolders.Length; i++)
                AppendProjectFolder(all_items, seen, ProjectFolders[i]);

            all_items.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return all_items;
        }

        public static void ClearCache()
        {
            all_items = null;
            project_cache.Clear();
            //序列图缓存：连同运行时创建的贴图一起释放（编辑器显式刷新素材时调；进行中的特效 Sprite 会随之失效）
            foreach (SheetTex st in sheet_textures.Values)
            {
                if (st != null && st.tex != null)
                    UnityEngine.Object.Destroy(st.tex);
            }
            sheet_textures.Clear();
            sheet_variants.Clear();
        }

        /// <summary>按 来源+名字 取 Sprite（找不到返回 null）</summary>
        public static Sprite Find(string source, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            //Resources 来源
            if (string.IsNullOrEmpty(source) || source == "Resources")
            {
                Sprite s = Resources.Load<Sprite>(ResourcesRoot + "/" + name);
                if (s == null)
                    s = Resources.Load<Sprite>(name);
                if (s != null)
                    return s;
            }

            //工程目录来源
            string folder = string.IsNullOrEmpty(source) ? "FX" : source;
            Sprite p = LoadProjectSprite(folder, name);
            if (p != null)
                return p;

            //来源不匹配时兜底：在所有来源里找同名
            List<FrameItem> items = GetAll();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].name == name)
                    return items[i].sprite;
            }
            return null;
        }

        /// <summary>按配置解析出帧序列：
        /// ① 配置了本地序列图（sheet_path）→ 整图切片；② 否则按 frame_names 逐张取（找不到的跳过）。</summary>
        public static List<Sprite> ResolveFrames(VFXConfig cfg)
        {
            List<Sprite> list = new List<Sprite>();
            if (cfg == null)
                return list;
            if (cfg.HasSheet)
                return ResolveSheet(cfg);       //本地序列图优先（整张 PNG 切片）
            if (cfg.frame_names == null)
                return list;
            for (int i = 0; i < cfg.frame_names.Count; i++)
            {
                Sprite s = Find(cfg.frame_source, cfg.frame_names[i]);
                if (s != null)
                    list.Add(s);
            }
            return list;
        }

        // ================= 本地序列图（整张 spritesheet 切片） =================

        /// <summary>序列图切片信息（编辑器显示尺寸/帧数/错误用；中文提示可直接展示）</summary>
        public class SheetInfo
        {
            public bool ok;
            public string message = "";      //提示或错误原因（成功时可能带"不整除"这类提示）
            public string file = "";         //解析到的绝对路径
            public int width, height;        //整图尺寸
            public int frame_w, frame_h;     //单帧尺寸
            public int total_cells;          //列 × 行
            public int used_count;           //实际切出的帧数
        }

        private class SheetTex
        {
            public string key;
            public Texture2D tex;
            public int width, height;
            public string error = "";
        }

        private class SheetVariant
        {
            public List<Sprite> frames = new List<Sprite>();
            public SheetInfo info = new SheetInfo();
        }

        /// <summary>贴图缓存（键 = 路径 + 文件修改时间：改图后自动重载）</summary>
        private static readonly Dictionary<string, SheetTex> sheet_textures = new Dictionary<string, SheetTex>();

        /// <summary>切片结果缓存（键 = 贴图 + 列/行/播放行/起始帧/帧数/缩放；编辑器里反复调参数不必重复切片）</summary>
        private static readonly Dictionary<string, SheetVariant> sheet_variants = new Dictionary<string, SheetVariant>();

        /// <summary>已警告过的失败原因（同一条只提示一次，避免每帧/每次触发刷屏）</summary>
        private static readonly HashSet<string> sheet_warned = new HashSet<string>();

        /// <summary>单帧边长候选（w、h 的公因数中落在 [16,1024] 的，降序）：
        /// 只用候选值就能保证"任何列×行都整除整图"，避免切出半格错位帧。</summary>
        public static List<int> SheetCellCandidates(int w, int h)
        {
            List<int> list = new List<int>();
            if (w <= 0 || h <= 0)
                return list;
            int g = Gcd(w, h);
            for (int d = Mathf.Min(g, 1024); d >= 16; d--)
            {
                if (g % d == 0)
                    list.Add(d);
            }
            return list;
        }

        /// <summary>自动识别的单帧边长：优先 RPG Maker 动画标准 192，否则取最接近 192 的公因数</summary>
        public static int AutoSheetCell(int w, int h)
        {
            List<int> cands = SheetCellCandidates(w, h);
            if (cands.Count == 0)
                return 0;
            int best = cands[0];
            for (int i = 0; i < cands.Count; i++)
            {
                if (Mathf.Abs(cands[i] - 192) < Mathf.Abs(best - 192))
                    best = cands[i];
            }
            return best;
        }

        /// <summary>按图片尺寸求切片网格：cell&lt;=0 或不能整除时自动识别，保证 cols/rows 整除图片</summary>
        public static void AutoGrid(int w, int h, int cell, out int cols, out int rows, out int used_cell)
        {
            cols = 1;
            rows = 1;
            used_cell = 0;
            if (w <= 0 || h <= 0)
                return;
            int c = cell;
            if (c <= 0 || w % c != 0 || h % c != 0)
                c = AutoSheetCell(w, h);      //手动值不能整分整图 → 退回自动识别
            if (c <= 0)
                return;
            cols = Mathf.Max(1, w / c);
            rows = Mathf.Max(1, h / c);
            used_cell = c;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return Mathf.Abs(a);
        }

        /// <summary>把配置里的序列图路径解析成绝对文件路径：
        /// ① 已是存在的路径（绝对或相对当前目录）直接用；② "Assets/..." 相对工程目录；
        /// ③ 其余按"工程 Sprites/{FX|UI|Icons|Cards}/&lt;名字&gt;[.png|.jpg] "探测。</summary>
        public static string ResolveSheetFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            try
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }
            catch (Exception) { }

            string p = path.Replace("\\", "/").Trim();
            if (p.StartsWith("Assets/"))
            {
                try
                {
                    string full = Path.Combine(Application.dataPath, p.Substring("Assets/".Length));
                    if (File.Exists(full))
                        return Path.GetFullPath(full);
                }
                catch (Exception) { }
            }

            string[] exts = { "", ".png", ".jpg", ".jpeg" };
            for (int f = 0; f < ProjectFolders.Length; f++)
            {
                for (int e = 0; e < exts.Length; e++)
                {
                    try
                    {
                        string cand = Path.Combine(Path.Combine(Application.dataPath, "TcgEngine/Sprites"),
                            Path.Combine(ProjectFolders[f], p + exts[e]));
                        if (File.Exists(cand))
                            return Path.GetFullPath(cand);
                    }
                    catch (Exception) { }
                }
            }
            return null;
        }

        /// <summary>解析本地序列图为帧序列（带缓存）。失败返回空列表，并在 Console 打一条原因。</summary>
        public static List<Sprite> ResolveSheet(VFXConfig cfg)
        {
            SheetVariant v = GetSheetVariant(cfg);
            if (v.info != null && !v.info.ok && !string.IsNullOrEmpty(v.info.message))
            {
                //同一条失败原因只提示一次（战斗中每次触发都刷屏会淹没真正有用的日志）
                string key = (cfg != null ? cfg.sheet_path : "") + "|" + v.info.message;
                if (sheet_warned.Add(key))
                    Debug.LogWarning("[VFX] 本地序列图切片失败：" + v.info.message
                        + "（" + (cfg != null ? cfg.sheet_path : "") + "）");
            }
            return v.frames;
        }

        /// <summary>只取切片信息（编辑器显示"图片尺寸 / 单帧尺寸 / 帧数 / 错误原因"）</summary>
        public static SheetInfo ProbeSheet(VFXConfig cfg)
        {
            return GetSheetVariant(cfg).info;
        }

        private static SheetTex LoadSheetTexture(string file)
        {
            long mtime = 0;
            try { mtime = File.GetLastWriteTimeUtc(file).Ticks; }
            catch (Exception) { }
            string key = file + "|" + mtime;
            if (sheet_textures.TryGetValue(key, out SheetTex hit))
                return hit;

            SheetTex st = new SheetTex { key = key };
            //复用项目已验证的本地图片加载器（体积/边长护栏 + 中文错误原因 + 可读 RGBA32）
            Texture2D tex;
            string err;
            if (TcgEngine.UI.ImagePicker.TryLoadTextureFromFile(file, out tex, out err))
            {
                st.tex = tex;
                st.width = tex.width;
                st.height = tex.height;
            }
            else
            {
                st.error = string.IsNullOrEmpty(err) ? "图片读取失败" : err;
            }
            sheet_textures[key] = st;
            return st;
        }

        private static SheetVariant GetSheetVariant(VFXConfig cfg)
        {
            if (cfg == null || !cfg.HasSheet)
                return new SheetVariant { info = new SheetInfo { ok = false, message = "未设置序列图" } };

            string file = ResolveSheetFile(cfg.sheet_path);
            if (file == null)
                return new SheetVariant
                {
                    info = new SheetInfo { ok = false, message = "找不到图片文件（可用绝对路径，或 Assets/... 与工程 Sprites/FX 内的文件名）" }
                };

            SheetTex st = LoadSheetTexture(file);
            if (st.tex == null)
                return new SheetVariant { info = new SheetInfo { ok = false, file = file, message = st.error } };

            int cols = Mathf.Max(1, cfg.sheet_cols);
            int rows = Mathf.Max(1, cfg.sheet_rows);
            int play_row = cfg.sheet_row;
            int start = Mathf.Max(0, cfg.sheet_start);
            int want = Mathf.Max(0, cfg.sheet_count);
            float scale = cfg.sheet_scale;
            string grid_note = "";

            //★ 统一分割：网格必须"整除整图"，否则按图片尺寸自校正。
            //  根因：RPG Maker 动画单帧恒为 192×192，故 960×960=5×5、960×768=5×4、960×576=5×3、
            //  960×384=5×2、960×192=5×1、960×1152=5×6、768×192=4×1、576×192=3×1 —— 一律按 5 行切
            //  就会把非 5 行高的图切成错位帧（这正是"只有 5×5 序列图正常"的原因）。
            if (st.width % cols != 0 || st.height % rows != 0)
            {
                int auto_cols, auto_rows, auto_cell;
                AutoGrid(st.width, st.height, cfg.sheet_cell, out auto_cols, out auto_rows, out auto_cell);
                if (auto_cols > 0 && auto_rows > 0)
                {
                    grid_note = "已按图片尺寸自动修正切片：" + auto_cols + "列×" + auto_rows + "行（单帧 "
                        + (st.width / auto_cols) + "×" + (st.height / auto_rows) + "）";
                    cols = auto_cols;
                    rows = auto_rows;
                }
            }
            string vkey = st.key + "|" + cols + "|" + rows + "|" + play_row + "|" + start + "|" + want + "|" + scale.ToString("0.###");
            if (sheet_variants.TryGetValue(vkey, out SheetVariant cached))
                return cached;

            //编辑器里反复调参会不断产生新变体：到量就整体丢弃（贴图仍被 sheet_textures 缓存，重建不读盘）
            if (sheet_variants.Count > 128)
                sheet_variants.Clear();

            SheetVariant v = new SheetVariant();
            SheetInfo info = new SheetInfo { file = file, width = st.width, height = st.height, message = grid_note };
            v.info = info;

            int fw = st.width / cols;
            int fh = st.height / rows;
            info.frame_w = fw;
            info.frame_h = fh;
            info.total_cells = cols * rows;
            if (fw <= 0 || fh <= 0)
            {
                info.message = "列/行数过大：单帧尺寸为 0（图片 " + st.width + "×" + st.height + "）";
                sheet_variants[vkey] = v;
                return v;
            }

            //选帧：先定"播放行"，再套起始帧/帧数。行优先（RPG Maker 动画：从左到右、从上到下）
            List<int> cells = new List<int>();
            if (play_row >= 0)
            {
                int r = Mathf.Clamp(play_row, 0, rows - 1);
                for (int c = 0; c < cols; c++)
                    cells.Add(r * cols + c);
            }
            else
            {
                for (int i = 0; i < cols * rows; i++)
                    cells.Add(i);
            }
            int st_index = Mathf.Clamp(start, 0, Mathf.Max(0, cells.Count - 1));
            int count = want > 0 ? want : (cells.Count - st_index);
            count = Mathf.Clamp(count, 0, cells.Count - st_index);

            //单帧像素偏大时用 PPU 反向补偿（scale=0.5 → 视觉尺寸减半），无需改动播放器/运行时
            float ppu = 100f / Mathf.Max(0.01f, scale <= 0.01f ? 1f : scale);
            string base_name = Path.GetFileNameWithoutExtension(file);
            for (int i = 0; i < count; i++)
            {
                int cell = cells[st_index + i];
                int c = cell % cols;
                int r = cell / cols;
                //贴图原点在左下、序列图行序自上而下 → y 翻转
                Rect rect = new Rect(c * fw, st.height - (r + 1) * fh, fw, fh);
                Sprite sp = Sprite.Create(st.tex, rect, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
                sp.name = base_name + "#" + cell;
                v.frames.Add(sp);
            }
            info.used_count = v.frames.Count;
            info.ok = v.frames.Count > 0;
            if (!info.ok && string.IsNullOrEmpty(info.message))
                info.message = "切出的帧数为 0（检查 播放行/起始帧/帧数 设置）";
            sheet_variants[vkey] = v;
            return v;
        }

        // ---------------- 内部 ----------------

        private static void AppendResources(List<FrameItem> list, HashSet<string> seen, string path)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite s = sprites[i];
                if (s == null || string.IsNullOrEmpty(s.name))
                    continue;
                string key = "Resources:" + s.name;
                if (!seen.Add(key))
                    continue;
                list.Add(new FrameItem { name = s.name, source = "Resources", sprite = s });
            }
        }

        private static void AppendProjectFolder(List<FrameItem> list, HashSet<string> seen, string folder)
        {
            string dir = Path.Combine(Path.Combine(Application.dataPath, "TcgEngine/Sprites"), folder);
            if (!Directory.Exists(dir))
                return;
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                string key = folder + ":" + name;
                if (!seen.Add(key))
                    continue;
                Sprite sprite = LoadProjectSprite(folder, name);
                if (sprite == null)
                    continue;
                list.Add(new FrameItem { name = name, source = folder, sprite = sprite });
            }
        }

        /// <summary>从工程目录读图并建 Sprite（缓存；同一张图只读一次）</summary>
        private static Sprite LoadProjectSprite(string folder, string name)
        {
            string key = folder + ":" + name;
            if (project_cache.TryGetValue(key, out Sprite cached))
                return cached;

            string dir = Path.Combine(Path.Combine(Application.dataPath, "TcgEngine/Sprites"), folder);
            string[] candidates = { ".png", ".jpg", ".jpeg" };
            for (int i = 0; i < candidates.Length; i++)
            {
                string file = Path.Combine(dir, name + candidates[i]);
                if (!File.Exists(file))
                    continue;
                try
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(bytes))
                    {
                        UnityEngine.Object.Destroy(tex);
                        continue;
                    }
                    tex.wrapMode = TextureWrapMode.Clamp;
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = name;
                    project_cache[key] = sprite;
                    return sprite;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[VFX] 读取工程帧失败 " + file + " : " + e.Message);
                }
            }
            project_cache[key] = null;
            return null;
        }
    }
}
