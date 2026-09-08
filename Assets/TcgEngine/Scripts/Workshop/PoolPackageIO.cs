using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 卡池资源打包（.tcgpool = zip）：
    /// 导出时把卡池 JSON 连同卡牌引用的图片（Workshop/Art）与音频（Workshop/Audio）
    /// 一起打进一个压缩包，分享/迁移时图片与声音不丢失。
    /// 包内结构：
    ///   pool.json       卡池数据（原 CardPoolData JSON）
    ///   art/xxx.png     卡牌图片（art_path / art_full_path 引用的文件）
    ///   audio/xxx.wav   卡牌音频（spawn/attack/death/damage 槽引用的文件）
    /// 导入时解包：资源落地到本地 ArtFolder/AudioFolder（同名冲突自动改名并同步 json 引用），
    /// pool.json 落地到 SaveFolder 后复用 CardPoolIO.ImportFromFile 注册，重启自动加载等机制不变。
    /// 兼容：.json 旧格式仍由 CardPoolIO 原流程导入；本类只负责 .tcgpool。
    /// </summary>
    public static class PoolPackageIO
    {
        /// <summary>卡池包扩展名</summary>
        public const string PoolExtension = ".tcgpool";

        // ---------------- 导出 ----------------

        /// <summary>把运行时卡牌列表导出为 .tcgpool 压缩包（内置/自定义卡均可，资源取本地文件或运行时对象字节）</summary>
        public static void ExportToPackage(List<CardData> cards, string poolName, string directory)
        {
            if (cards == null || cards.Count == 0)
                return;
            CardPoolData pool = CardPoolIO.BuildPool(cards, poolName);
            ExportPoolPackage(pool, poolName, directory);
        }

        /// <summary>把已序列化的卡池数据导出为 .tcgpool 压缩包。
        /// 资源按 DTO 中的文件名从本地 Workshop 目录收集；收集不到的（如内置卡）回退从运行时对象编码。</summary>
        public static void ExportPoolPackage(CardPoolData pool, string poolName, string directory)
        {
            if (pool == null || pool.cards == null || pool.cards.Count == 0)
                return;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, poolName + PoolExtension);

            //先收集所有资源字节（同时可能回填 DTO 中缺失的资源文件名，故需先收集再序列化 json）
            Dictionary<string, byte[]> assets = CollectAssets(pool);
            string json = JsonUtility.ToJson(pool, true);

            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "pool.json", Encoding.UTF8.GetBytes(json));
                foreach (KeyValuePair<string, byte[]> kv in assets)
                {
                    string folder = IsAudioFile(kv.Key) ? "audio/" : "art/";
                    WriteEntry(zip, folder + kv.Key, kv.Value);
                }
            }
            Debug.Log("已导出卡池包: " + path + "（" + pool.cards.Count + " 张卡，" + assets.Count + " 个资源文件）");
        }

        /// <summary>按 DTO 引用的文件名收集资源字节；本地文件缺失时从运行时 Sprite/AudioClip 编码并回填文件名。</summary>
        private static Dictionary<string, byte[]> CollectAssets(CardPoolData pool)
        {
            Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>();
            if (pool == null || pool.cards == null)
                return assets;
            foreach (CardCustomData dto in pool.cards)
            {
                if (dto == null)
                    continue;
                CardData card = CardData.Get(dto.id);   //自定义卡导入后已注册；内置卡也可查到
                EnsureImage(dto.art_path, card != null ? card.art_board : null, dto.id, "_board", assets, v => dto.art_path = v);
                EnsureImage(dto.art_full_path, card != null ? card.art_full : null, dto.id, "_full", assets, v => dto.art_full_path = v);
                EnsureAudio(dto.spawn_audio_id, card != null ? card.spawn_audio : null, dto.id, "_spawn", assets, v => dto.spawn_audio_id = v);
                EnsureAudio(dto.attack_audio_id, card != null ? card.attack_audio : null, dto.id, "_attack", assets, v => dto.attack_audio_id = v);
                EnsureAudio(dto.death_audio_id, card != null ? card.death_audio : null, dto.id, "_death", assets, v => dto.death_audio_id = v);
                EnsureAudio(dto.damage_audio_id, card != null ? card.damage_audio : null, dto.id, "_damage", assets, v => dto.damage_audio_id = v);
            }
            return assets;
        }

        /// <summary>收集单张图片：优先读本地文件；缺失且卡片有 Sprite 时从纹理编码 PNG 并回填文件名。</summary>
        private static void EnsureImage(string fname, Sprite sprite, string cardId, string suffix,
            Dictionary<string, byte[]> assets, Action<string> setDto)
        {
            if (string.IsNullOrEmpty(fname))
            {
                if (sprite == null)
                    return;
                fname = SanitizeName(cardId) + suffix + ".png";
                setDto(fname);
            }
            if (assets.ContainsKey(fname))
                return;
            string path = Path.Combine(CardPoolIO.ArtFolder, fname);
            if (File.Exists(path))
            {
                assets[fname] = File.ReadAllBytes(path);
                return;
            }
            if (sprite != null && sprite.texture != null)
            {
                assets[fname] = sprite.texture.EncodeToPNG();
                return;
            }
            Debug.LogWarning("导出卡池包：卡 " + cardId + " 的图片 " + fname + " 本地不存在且无法从运行时纹理导出");
        }

        /// <summary>收集单个音频：优先读本地文件；缺失且卡片有 AudioClip 时编码为 WAV 并回填文件名。</summary>
        private static void EnsureAudio(string fname, AudioClip clip, string cardId, string suffix,
            Dictionary<string, byte[]> assets, Action<string> setDto)
        {
            if (string.IsNullOrEmpty(fname))
            {
                if (clip == null)
                    return;
                fname = SanitizeName(cardId) + suffix + ".wav";
                setDto(fname);
            }
            if (assets.ContainsKey(fname))
                return;
            string path = Path.Combine(CardPoolIO.AudioFolder, fname);
            if (File.Exists(path))
            {
                assets[fname] = File.ReadAllBytes(path);
                return;
            }
            if (clip != null)
            {
                byte[] wav = AudioClipToWav(clip);
                if (wav != null)
                {
                    assets[fname] = wav;
                    return;
                }
            }
            Debug.LogWarning("导出卡池包：卡 " + cardId + " 的音频 " + fname + " 本地不存在且无法编码");
        }

        // ---------------- 导入 ----------------

        /// <summary>导入 .tcgpool 卡池包：解包并把 art/audio 资源落地到本地目录，
        /// 同名冲突自动改名并同步 json 引用，pool.json 落地 SaveFolder 后注册到游戏。</summary>
        public static void ImportPoolPackage(string path)
        {
            if (!File.Exists(path))
                return;

            string json = null;
            Dictionary<string, string> renames = new Dictionary<string, string>();

            using (FileStream fs = File.OpenRead(path))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/"))
                        continue;   //目录项跳过
                    string file = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrEmpty(file))
                        continue;

                    if (file == "pool.json")
                    {
                        using (StreamReader sr = new StreamReader(entry.Open(), Encoding.UTF8))
                            json = sr.ReadToEnd();
                    }
                    else if (entry.FullName.StartsWith("art/"))
                    {
                        PlaceAsset(entry, CardPoolIO.ArtFolder, renames);
                    }
                    else if (entry.FullName.StartsWith("audio/"))
                    {
                        PlaceAsset(entry, CardPoolIO.AudioFolder, renames);
                    }
                }
            }

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("卡池包缺少 pool.json: " + path);
                return;
            }

            //同名冲突改名后，同步 json 中引用的旧文件名
            foreach (KeyValuePair<string, string> kv in renames)
                json = json.Replace(kv.Key, kv.Value);

            //pool.json 落地本地卡池目录，文件名与包名一致（重启自动加载机制不变）
            string target = Path.Combine(CardPoolIO.SaveFolder, Path.GetFileNameWithoutExtension(path) + ".json");
            Directory.CreateDirectory(CardPoolIO.SaveFolder);
            File.WriteAllText(target, json);

            int before = CardData.GetAll().Count;
            CardPoolIO.ImportFromFile(target, true);   //授予拥有数量，构筑界面立即可用
            int added = CardData.GetAll().Count - before;
            Debug.Log("已导入卡池包: " + path + "，新增 " + added + " 张卡" +
                (renames.Count > 0 ? "（" + renames.Count + " 个资源重名已自动改名）" : ""));
        }

        /// <summary>把一个 zip 条目落地到目标目录（冲突改名，记录旧名→新名映射）</summary>
        private static void PlaceAsset(ZipArchiveEntry entry, string destDir, Dictionary<string, string> renames)
        {
            string name = Path.GetFileName(entry.FullName);
            Directory.CreateDirectory(destDir);
            string destName = GetUniqueFileName(destDir, name);
            using (Stream src = entry.Open())
            using (FileStream dst = new FileStream(Path.Combine(destDir, destName), FileMode.Create))
                src.CopyTo(dst);
            if (destName != name)
                renames[name] = destName;
        }

        /// <summary>目标目录已存在同名文件时生成 "_1/_2..." 后缀，避免覆盖用户已有资源</summary>
        private static string GetUniqueFileName(string dir, string name)
        {
            if (!File.Exists(Path.Combine(dir, name)))
                return name;
            string ext = Path.GetExtension(name);
            string baseName = Path.GetFileNameWithoutExtension(name);
            for (int i = 1; i < 10000; i++)
            {
                string candidate = baseName + "_" + i + ext;
                if (!File.Exists(Path.Combine(dir, candidate)))
                    return candidate;
            }
            return Guid.NewGuid().ToString("N") + ext;
        }

        // ---------------- 工具 ----------------

        private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using (Stream s = entry.Open())
                s.Write(bytes, 0, bytes.Length);
        }

        /// <summary>按扩展名判断是否为音频（用于 zip 内 art/audio 目录分组）</summary>
        private static bool IsAudioFile(string fname)
        {
            string ext = Path.GetExtension(fname ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".mp3":
                case ".wav":
                case ".ogg":
                case ".aif":
                case ".aiff":
                    return true;
            }
            return false;
        }

        /// <summary>清洗为合法文件名（去掉非法字符）</summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "card";
            char[] invalids = Path.GetInvalidFileNameChars();
            foreach (char c in invalids)
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>AudioClip → 16-bit PCM WAV 字节（运行时无法还原原始 mp3/ogg 压缩字节，统一重编码为 wav）</summary>
        private static byte[] AudioClipToWav(AudioClip clip)
        {
            try
            {
                int sampleCount = clip.samples * clip.channels;
                if (sampleCount <= 0)
                    return null;
                float[] samples = new float[sampleCount];
                if (!clip.GetData(samples, 0))
                    return null;

                short[] intData = new short[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    float f = samples[i];
                    if (f > 1f) f = 1f;
                    else if (f < -1f) f = -1f;
                    intData[i] = (short)(f * short.MaxValue);
                }
                byte[] pcm = new byte[intData.Length * 2];
                Buffer.BlockCopy(intData, 0, pcm, 0, pcm.Length);

                int dataSize = pcm.Length;
                int byteRate = clip.frequency * clip.channels * 2;
                byte[] header = new byte[44];
                Array.Copy(Encoding.ASCII.GetBytes("RIFF"), 0, header, 0, 4);
                Array.Copy(BitConverter.GetBytes(36 + dataSize), 0, header, 4, 4);
                Array.Copy(Encoding.ASCII.GetBytes("WAVE"), 0, header, 8, 4);
                Array.Copy(Encoding.ASCII.GetBytes("fmt "), 0, header, 12, 4);
                Array.Copy(BitConverter.GetBytes(16), 0, header, 16, 4);
                Array.Copy(BitConverter.GetBytes((short)1), 0, header, 20, 2);            //PCM
                Array.Copy(BitConverter.GetBytes((short)clip.channels), 0, header, 22, 2);
                Array.Copy(BitConverter.GetBytes(clip.frequency), 0, header, 24, 4);
                Array.Copy(BitConverter.GetBytes(byteRate), 0, header, 28, 4);
                Array.Copy(BitConverter.GetBytes((short)(clip.channels * 2)), 0, header, 32, 2);
                Array.Copy(BitConverter.GetBytes((short)16), 0, header, 34, 2);           //16 bit
                Array.Copy(Encoding.ASCII.GetBytes("data"), 0, header, 36, 4);
                Array.Copy(BitConverter.GetBytes(dataSize), 0, header, 40, 4);

                byte[] wav = new byte[44 + dataSize];
                Buffer.BlockCopy(header, 0, wav, 0, 44);
                Buffer.BlockCopy(pcm, 0, wav, 44, dataSize);
                return wav;
            }
            catch (Exception e)
            {
                Debug.LogWarning("导出音频编码失败: " + e.Message);
                return null;
            }
        }
    }
}
