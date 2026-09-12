using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 图片来源封装：① 本地文件（PC，经 FileBrowserBridge）② 项目 Resources 图库（CardData 的卡面/面板图）。
    /// 统一出口是「一张运行时生成、可读、RGBA32 的 Texture2D」——
    /// 后续拖动/缩放/裁切/编码 PNG 都只需要这一种数据形态，不必区分来源，
    /// 也顺带解决了项目 Sprite 因关闭 Read/Write 而无法 GetPixels 的问题。
    /// </summary>
    public static class ImagePicker
    {
        /// <summary>允许的最大边长；超过直接拒绝，避免显存/内存爆掉</summary>
        public const int MAX_SIDE = 4096;

        /// <summary>本地文件大小上限（字节）：先按体积挡掉明显过大的文件，再解码</summary>
        public const long MAX_FILE_BYTES = 32L * 1024 * 1024;

        /// <summary>图库条目：显示名 + Sprite</summary>
        public class GalleryItem
        {
            public string name;
            public Sprite sprite;
        }

        // ================= 图库（Resources / CardData） =================

        /// <summary>
        /// 图库列表：当前工程所有卡的卡面图(art_board) 与 面板图(art_full)，
        /// 同类 Sprite 去重、按名字排序，供弹框「从图库选择」使用。
        /// </summary>
        public static List<GalleryItem> GetGallery()
        {
            List<GalleryItem> list = new List<GalleryItem>();
            HashSet<Sprite> seen = new HashSet<Sprite>();

            List<CardData> cards = CardData.GetAll();
            if (cards == null || cards.Count == 0)
            {
                CardData.Load();            //独立演示场景没有 DataLoader，这里兜底加载一次 Resources 卡牌
                cards = CardData.GetAll();
            }
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    CardData c = cards[i];
                    if (c == null)
                        continue;
                    string id = string.IsNullOrEmpty(c.id) ? "?" : c.id;
                    AddGallery(list, seen, c.art_board, id + " · 卡面");
                    AddGallery(list, seen, c.art_full, id + " · 面板");
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        private static void AddGallery(List<GalleryItem> list, HashSet<Sprite> seen, Sprite sp, string name)
        {
            if (sp == null || seen.Contains(sp))
                return;
            seen.Add(sp);
            list.Add(new GalleryItem { name = name, sprite = sp });
        }

        // ================= 本地文件 =================

        /// <summary>
        /// 读取本地图片文件为可读 Texture2D。
        /// 失败时 error 为可直接展示给玩家的中文原因；不抛异常。
        /// </summary>
        public static bool TryLoadTextureFromFile(string path, out Texture2D tex, out string error)
        {
            tex = null;
            error = null;

            if (string.IsNullOrEmpty(path))
            {
                error = "未选择文件";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "文件不存在：" + path;
                return false;
            }
            if (!FileBrowserBridge.IsSupportedExtension(path))
            {
                error = "不支持的图片格式（仅 png / jpg / jpeg / bmp）";
                return false;
            }

            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Length > MAX_FILE_BYTES)
                {
                    error = "图片文件过大（" + (fi.Length / 1024 / 1024) + "MB，上限 " + (MAX_FILE_BYTES / 1024 / 1024) + "MB）";
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                // 先用占位尺寸构造 RGBA32 且不开 mipmap，接着 LoadImage 会按真实尺寸替换内容
                // （LoadImage 之前拿不到宽高，故尺寸校验只能放到解码之后）
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(bytes))
                {
                    Object.Destroy(t);
                    error = "不是有效的图片文件（解码失败）";
                    return false;
                }

                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;

                if (!ValidateSize(t.width, t.height, out error))
                {
                    Object.Destroy(t);
                    return false;
                }

                tex = t;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("读取本地图片失败：" + path + " —— " + e.Message);
                error = "读取图片失败：" + e.Message;
                return false;
            }
        }

        /// <summary>尺寸校验：超过 MAX_SIDE 直接拒绝</summary>
        public static bool ValidateSize(int w, int h, out string error)
        {
            error = null;
            if (w <= 0 || h <= 0)
            {
                error = "图片尺寸无效";
                return false;
            }
            if (w > MAX_SIDE || h > MAX_SIDE)
            {
                error = "图片过大（" + w + "×" + h + "，单边上限 " + MAX_SIDE + "），请先压缩后再导入";
                return false;
            }
            return true;
        }

        // ================= Sprite（图库）→ 可读 Texture2D =================

        /// <summary>
        /// 把项目里的 Sprite 转成可读 Texture2D（只取该 Sprite 在图集中的实际区域）。
        /// 图集打包 / 资源关闭 Read/Write 时走 RenderTexture 回读，避免 GetPixels 抛异常。
        /// </summary>
        public static bool TryLoadTextureFromSprite(Sprite sp, out Texture2D tex, out string error)
        {
            tex = null;
            error = null;

            if (sp == null)
            {
                error = "图片为空";
                return false;
            }
            if (sp.texture == null)
            {
                error = "该图片没有绑定纹理";
                return false;
            }

            int tw, th;
            Color[] all = ImageClipCrop.ReadPixels(sp.texture, out tw, out th);
            if (all == null || all.Length < tw * th)
            {
                error = "无法读取图片像素（纹理不可读且回读失败）";
                return false;
            }

            // textureRect 是 Sprite 在图集纹理中的实际区域；未打包时等于 sp.rect
            Rect r = sp.textureRect;
            int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, Mathf.Max(0, tw - 1));
            int y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, Mathf.Max(0, th - 1));
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, tw - x);
            int h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, th - y);

            if (!ValidateSize(w, h, out error))
                return false;

            Color[] px = new Color[w * h];
            for (int row = 0; row < h; row++)
                System.Array.Copy(all, (y + row) * tw + x, px, row * w, w);

            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixels(px);
            t.Apply();
            tex = t;
            return true;
        }
    }
}
