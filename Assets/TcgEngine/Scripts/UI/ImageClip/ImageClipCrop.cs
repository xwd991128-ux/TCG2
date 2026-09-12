using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡图裁切的纯计算 + 纹理工具（无 UI 依赖，可直接单测）。
    ///
    /// 坐标约定：
    /// - 遮罩视口（viewport）以自身中心为原点，尺寸 viewportSize；
    /// - 图片（RawImage）是视口的子物体，anchor/pivot 均为中心，尺寸 = 原图像素 × scale，
    ///   anchoredPosition = 图片中心相对视口中心的偏移（即 scale=1 时「1 像素 = 1 UI 单位」，换算无误差）；
    /// - Texture2D 像素坐标原点在左下角，与 UI 的 y 轴向上一致，故 v 直接可用。
    ///
    /// 裁切只取原图素、不做缩放置采样，保证成品与源图同清晰度。
    /// </summary>
    public static class ImageClipCrop
    {
        /// <summary>成品 Sprite 的每单位像素（PPU）</summary>
        public const float SpritePPU = 100f;

        /// <summary>缩放上限（相对「铺满遮罩」的比例）</summary>
        public const float MaxZoom = 8f;

        /// <summary>
        /// 遮罩内的图片矩形 → 原图像素矩形。
        /// imageSize/imagePos 为 RawImage 的 sizeDelta / anchoredPosition，viewportSize 为遮罩尺寸。
        /// </summary>
        public static Rect ViewportToPixelRect(Vector2 imageSize, Vector2 imagePos, Vector2 viewportSize, int texW, int texH)
        {
            if (imageSize.x <= 0f || imageSize.y <= 0f || texW <= 0 || texH <= 0)
                return new Rect(0f, 0f, Mathf.Max(1, texW), Mathf.Max(1, texH));

            // 视口左下角 / 右上角在图片本地坐标中的归一化位置
            float u0 = (-viewportSize.x * 0.5f - imagePos.x + imageSize.x * 0.5f) / imageSize.x;
            float v0 = (-viewportSize.y * 0.5f - imagePos.y + imageSize.y * 0.5f) / imageSize.y;
            float uw = viewportSize.x / imageSize.x;
            float vh = viewportSize.y / imageSize.y;

            return new Rect(u0 * texW, v0 * texH, uw * texW, vh * texH);
        }

        /// <summary>把像素矩形裁剪到原图范围内（保证宽高至少 1 像素）</summary>
        public static Rect ClampPixelRect(Rect r, int texW, int texH)
        {
            float x = Mathf.Clamp(r.x, 0f, Mathf.Max(0f, texW - 1f));
            float y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, texH - 1f));
            float w = Mathf.Clamp(r.width, 1f, Mathf.Max(1f, texW - x));
            float h = Mathf.Clamp(r.height, 1f, Mathf.Max(1f, texH - y));
            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// 位置休眠 clamp：图片任意一边都不得进入视口内，保证遮罩始终被图片覆盖、不露白。
        /// 图片小于视口的轴上直接居中（调用方应保证 scale ≥ 铺满比例）。
        /// </summary>
        public static Vector2 ClampImagePosition(Vector2 imageSize, Vector2 pos, Vector2 viewportSize)
        {
            float maxX = Mathf.Max(0f, (imageSize.x - viewportSize.x) * 0.5f);
            float maxY = Mathf.Max(0f, (imageSize.y - viewportSize.y) * 0.5f);
            return new Vector2(Mathf.Clamp(pos.x, -maxX, maxX), Mathf.Clamp(pos.y, -maxY, maxY));
        }

        /// <summary>
        /// 「铺满遮罩」的缩放：取两轴中较大的比例（cover），保证最短边也铺满、四角不露白。
        /// 这也是缩放下限——若允许再缩小就会露白边。
        /// </summary>
        public static float FitScale(Vector2 imageSize, Vector2 viewportSize)
        {
            if (imageSize.x <= 0f || imageSize.y <= 0f)
                return 1f;
            return Mathf.Max(viewportSize.x / imageSize.x, viewportSize.y / imageSize.y);
        }

        /// <summary>缩放范围 clamp：下限 = 铺满比例，上限 = 8× 铺满比例</summary>
        public static float ClampScale(float scale, float fitScale)
        {
            return Mathf.Clamp(scale, fitScale, fitScale * MaxZoom);
        }

        /// <summary>
        /// 读取纹理像素。纹理不可读（导入资源常见：关闭了 Read/Write 或被压缩）时，
        /// 借 RenderTexture 回读，避免 GetPixels 抛异常。
        /// </summary>
        public static Color[] ReadPixels(Texture2D tex, out int w, out int h)
        {
            w = 0;
            h = 0;
            if (tex == null)
                return null;

            w = tex.width;
            h = tex.height;
            if (tex.isReadable)
                return tex.GetPixels();

            RenderTexture rt = null;
            RenderTexture prev = RenderTexture.active;
            Texture2D tmp = null;
            try
            {
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;

                tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tmp.Apply();
                return tmp.GetPixels();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("卡图裁切：像素回读失败（" + (tex != null ? tex.name : "null") + "）：" + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
                if (tmp != null)
                    Object.Destroy(tmp);
            }
        }

        /// <summary>按像素矩形裁切出一张新的可读 RGBA32 纹理（只裁不缩放）</summary>
        public static Texture2D Crop(Texture2D src, Rect pixelRect)
        {
            if (src == null)
                return null;

            int sw, sh;
            Color[] all = ReadPixels(src, out sw, out sh);
            if (all == null || all.Length < sw * sh)
                return null;

            Rect r = ClampPixelRect(pixelRect, sw, sh);
            int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, Mathf.Max(0, sw - 1));
            int y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, Mathf.Max(0, sh - 1));
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, sw - x);
            int h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, sh - y);

            Color[] out_px = new Color[w * h];
            for (int row = 0; row < h; row++)
                System.Array.Copy(all, (y + row) * sw + x, out_px, row * w, w);

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;      // 边缘 clamp：避免裁切/缩放时采样到对侧像素
            tex.filterMode = FilterMode.Bilinear;      // 双线性：卡图多为照片/插画，点采样在大幅缩小后会严重锯齿
            tex.SetPixels(out_px);
            tex.Apply();
            return tex;
        }

        /// <summary>纹理 → 居中 pivot、PPU=100 的 Sprite</summary>
        public static Sprite CreateSprite(Texture2D tex)
        {
            if (tex == null)
                return null;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                SpritePPU, 0, SpriteMeshType.FullRect);
        }

        /// <summary>裁切并直接产出 Sprite（一步到位）</summary>
        public static Sprite CropToSprite(Texture2D src, Rect pixelRect)
        {
            return CreateSprite(Crop(src, pixelRect));
        }
    }
}
