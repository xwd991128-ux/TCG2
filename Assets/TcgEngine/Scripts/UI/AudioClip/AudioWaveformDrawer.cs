using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 波形绘制模块（纯静态，独立于任何弹框/交互，可复用）：
    /// 把"交错采样数组"按列聚合成 min/max 包络并画进 Texture2D，同时高亮当前选区。
    ///
    /// 为什么传采样数组而不是 AudioClip：GetData 在大素材上是一次 MB 级拷贝，
    /// 拖动选区时每帧重绘会非常卡；因此由调用方（AudioWaveformView）**只取一次**采样，
    /// 后续重绘只做列聚合与像素写入。
    /// </summary>
    public static class AudioWaveformDrawer
    {
        // 与项目弹框配色一致（深色底 + 亮色波形）
        private static readonly Color32 BgColor = new Color32(26, 28, 33, 255);
        private static readonly Color32 SelBgColor = new Color32(38, 62, 78, 255);
        private static readonly Color32 WaveDimColor = new Color32(120, 146, 168, 255);
        private static readonly Color32 WaveSelColor = new Color32(122, 214, 255, 255);
        private static readonly Color32 EdgeColor = new Color32(255, 214, 102, 255);
        private static readonly Color32 CenterColor = new Color32(58, 64, 74, 255);

        /// <summary>
        /// 生成（或复用）波形纹理。start01/end01 为 0..1 的归一化选区，用于高亮。
        /// samples 为交错采样（长度 = frames * channels），可为 null（只画底）。
        /// </summary>
        public static Texture2D Build(float[] samples, int channels, int frames,
            int width, int height, float start01, float end01, Texture2D reuse = null)
        {
            width = Mathf.Max(width, 8);
            height = Mathf.Max(height, 8);

            Texture2D tex = reuse;
            if (tex == null || tex.width != width || tex.height != height)
            {
                if (tex != null)
                    Object.Destroy(tex);
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
            }

            Color32[] px = new Color32[width * height];
            for (int i = 0; i < px.Length; i++)
                px[i] = BgColor;

            int yCenter = height / 2;
            for (int x = 0; x < width; x++)
                px[x + yCenter * width] = CenterColor;   //零轴参考线

            if (samples != null && channels > 0 && frames > 0)
            {
                start01 = Mathf.Clamp01(Mathf.Min(start01, end01));
                end01 = Mathf.Clamp01(Mathf.Max(start01, end01));
                int selStartCol = Mathf.RoundToInt(start01 * width);
                int selEndCol = Mathf.RoundToInt(end01 * width);

                //1) 选区底色
                for (int x = selStartCol; x < selEndCol; x++)
                {
                    if (x < 0 || x >= width)
                        continue;
                    for (int y = 0; y < height; y++)
                        px[x + y * width] = SelBgColor;
                }

                //2) 逐列 min/max 包络
                for (int x = 0; x < width; x++)
                {
                    int f0 = (int)((long)x * frames / width);
                    int f1 = (int)((long)(x + 1) * frames / width);
                    if (f1 <= f0)
                        f1 = f0 + 1;
                    f0 = Mathf.Clamp(f0, 0, frames - 1);
                    f1 = Mathf.Clamp(f1, f0 + 1, frames);

                    float min = 1f;
                    float max = -1f;
                    for (int f = f0; f < f1; f++)
                    {
                        for (int c = 0; c < channels; c++)
                        {
                            int idx = f * channels + c;
                            if (idx < 0 || idx >= samples.Length)
                                continue;
                            float v = samples[idx];
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                    }
                    if (max < min)
                    {
                        min = 0f;
                        max = 0f;
                    }

                    int y0 = Mathf.Clamp(Mathf.RoundToInt((min + 1f) * 0.5f * (height - 1)), 0, height - 1);
                    int y1 = Mathf.Clamp(Mathf.RoundToInt((max + 1f) * 0.5f * (height - 1)), 0, height - 1);
                    if (y1 < y0)
                    {
                        int tmp = y0;
                        y0 = y1;
                        y1 = tmp;
                    }

                    bool in_sel = x >= selStartCol && x < selEndCol;
                    Color32 wc = in_sel ? WaveSelColor : WaveDimColor;
                    for (int y = y0; y <= y1; y++)
                        px[x + y * width] = wc;
                }

                //3) 选区两条边界线
                DrawColumn(px, width, height, selStartCol, EdgeColor);
                DrawColumn(px, width, height, selEndCol - 1, EdgeColor);
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        private static void DrawColumn(Color32[] px, int width, int height, int x, Color32 color)
        {
            if (x < 0 || x >= width)
                return;
            for (int y = 0; y < height; y++)
                px[x + y * width] = color;
        }
    }
}
