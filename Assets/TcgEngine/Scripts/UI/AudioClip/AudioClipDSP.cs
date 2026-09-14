using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 音频采样处理（纯函数静态类，与 UI 完全解耦，可被声音编辑器以外的流程复用）。
    ///
    /// 【为什么必须自己算】Unity 原生不能修改 AudioClip 的波形，只能：
    ///   AudioClip.Create(lengthSamples, channels, frequency, stream:false) + GetData/SetData 采样数组拷贝，
    /// 因此本类所有加工都落到"真实的采样数组"上，产物是新的 AudioClip 实例（不是播放参数）。
    ///
    /// 【音高语义】本实现选择 **重采样（变速变调）**：
    ///   半音数 s → 重采样比 ratio = 2^(s/12)（见 SemitoneToRatio）。
    ///   ratio > 1（升调）→ 采样点前移、样本数变少 → 音高升高、时长缩短；
    ///   ratio < 1（降调）→ 音高降低、时长变长。
    /// 这样加工结果**自包含**：保存成文件后在真实对局里播放时，音高变化依然存在，
    /// 不依赖 AudioSource.pitch（那只是播放参数，会随播放器状态丢失，且只变调不变速）。
    ///
    /// 【处理顺序】截取 → 重采样（音高/时长）→ 音量缩放 → 头尾淡入淡出（毫秒，按输出采样率换算）。
    /// </summary>
    public static class AudioClipDSP
    {
        /// <summary>超过该时长的素材在导入时提示（内存与主线程处理成本的保护线）</summary>
        public const float MaxImportSeconds = 30f;

        /// <summary>半音数 → 重采样比（目标频 f 相对 f0 的半音数 = 12 * log2(f / f0)）</summary>
        public static float SemitoneToRatio(float semitones)
        {
            return Mathf.Pow(2f, semitones / 12f);
        }

        /// <summary>重采样比 → 半音数（反向换算，供界面显示实际音高变化）</summary>
        public static float RatioToSemitone(float ratio)
        {
            if (ratio <= 0f)
                return 0f;
            return 12f * Mathf.Log(ratio, 2f);
        }

        /// <summary>取出交错（interleaved）采样数组：长度 = samples * channels</summary>
        public static float[] GetInterleaved(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0 || clip.channels <= 0)
                return null;
            int count = clip.samples * clip.channels;
            float[] samples = new float[count];
            if (!clip.GetData(samples, 0))
                return null;
            return samples;
        }

        /// <summary>用交错采样数组创建内存 AudioClip（stream=false，可直接 GetData/SetData）</summary>
        public static AudioClip CreateClip(string name, float[] samples, int channels, int frequency)
        {
            if (samples == null || channels <= 0 || frequency <= 0)
                return null;
            int frames = samples.Length / channels;
            if (frames <= 0)
                return null;
            AudioClip clip = AudioClip.Create(name, frames, channels, frequency, false);
            if (clip == null)
                return null;
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>采样级截取：保留 [startSample, endSample)（帧索引，含头不含尾）</summary>
        public static AudioClip Slice(AudioClip src, int startSample, int endSample, string name = "slice")
        {
            float[] all = GetInterleaved(src);
            if (all == null)
                return null;
            int ch = src.channels;
            int total = src.samples;
            startSample = Mathf.Clamp(startSample, 0, total);
            endSample = Mathf.Clamp(endSample, startSample, total);
            int frames = endSample - startSample;
            if (frames <= 0)
                return null;

            float[] outSamples = new float[frames * ch];
            System.Array.Copy(all, startSample * ch, outSamples, 0, outSamples.Length);
            return CreateClip(name, outSamples, ch, src.frequency);
        }

        /// <summary>音量缩放（原地修改交错采样数组；gain=1 为不变，0 为静音）</summary>
        public static void Scale(float[] samples, float gain)
        {
            if (samples == null || Mathf.Approximately(gain, 1f))
                return;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
        }

        /// <summary>重采样（变速变调）：ratio = 2^(semitones/12)，输出帧数 = 原帧数 / ratio，线性插值</summary>
        public static AudioClip Resample(AudioClip src, float ratio, string name = "resample")
        {
            float[] inSamples = GetInterleaved(src);
            if (inSamples == null || ratio <= 0f)
                return null;
            if (Mathf.Approximately(ratio, 1f))
                return Slice(src, 0, src.samples, name);   //比值≈1：等价拷贝，避免插值误差

            int ch = src.channels;
            int inFrames = src.samples;
            int outFrames = Mathf.Max(1, Mathf.RoundToInt(inFrames / ratio));
            float[] outSamples = new float[outFrames * ch];

            for (int f = 0; f < outFrames; f++)
            {
                float srcPos = f * ratio;
                int i0 = Mathf.Clamp((int)srcPos, 0, inFrames - 1);
                int i1 = Mathf.Min(i0 + 1, inFrames - 1);
                float t = srcPos - i0;
                for (int c = 0; c < ch; c++)
                {
                    float a = inSamples[i0 * ch + c];
                    float b = inSamples[i1 * ch + c];
                    outSamples[f * ch + c] = a + (b - a) * t;
                }
            }
            return CreateClip(name, outSamples, ch, src.frequency);
        }

        /// <summary>头尾淡入淡出（毫秒）：防止截取点爆音。原地修改交错采样数组。</summary>
        public static void Fade(float[] samples, int channels, int frequency, int fadeInMs, int fadeOutMs)
        {
            if (samples == null || channels <= 0 || frequency <= 0)
                return;
            int frames = samples.Length / channels;
            if (frames <= 1)
                return;

            int inFrames = Mathf.Clamp(Mathf.RoundToInt(frequency * (fadeInMs / 1000f)), 0, frames / 2);
            int outFrames = Mathf.Clamp(Mathf.RoundToInt(frequency * (fadeOutMs / 1000f)), 0, frames / 2);

            for (int f = 0; f < inFrames; f++)
            {
                float g = (float)f / inFrames;
                for (int c = 0; c < channels; c++)
                    samples[f * channels + c] *= g;
            }
            for (int f = 0; f < outFrames; f++)
            {
                float g = (float)f / outFrames;
                int frameIndex = frames - 1 - f;
                for (int c = 0; c < channels; c++)
                    samples[frameIndex * channels + c] *= g;
            }
        }

        /// <summary>
        /// 完整加工：截取 + 重采样(音高) + 音量 + 头尾淡入淡出，返回新的内存 AudioClip。
        /// 失败时 error 给出可提示的中文原因，返回 null。
        /// </summary>
        public static AudioClip Process(AudioClip src, float start01, float end01,
            float semitones, float volume, int fadeMs, out string error, string name = "audio_edit")
        {
            error = null;
            if (src == null)
            {
                error = "没有可加工的音频素材";
                return null;
            }
            if (src.samples <= 0)
            {
                error = "音频素材为空（无采样数据）";
                return null;
            }

            start01 = Mathf.Clamp01(start01);
            end01 = Mathf.Clamp01(end01);
            if (end01 < start01)
            {
                float tmp = start01;
                start01 = end01;
                end01 = tmp;
            }

            int startSample = Mathf.Clamp(Mathf.FloorToInt(start01 * src.samples), 0, src.samples - 1);
            int endSample = Mathf.Clamp(Mathf.CeilToInt(end01 * src.samples), startSample + 1, src.samples);
            if (endSample - startSample < 1)
            {
                error = "选区过短（至少要保留 1 个采样点）";
                return null;
            }

            AudioClip sliced = Slice(src, startSample, endSample, name + "_slice");
            if (sliced == null)
            {
                error = "截取失败（采样数据不可读）";
                return null;
            }

            AudioClip pitched = sliced;
            if (Mathf.Abs(semitones) > 0.01f)
            {
                AudioClip resampled = Resample(sliced, SemitoneToRatio(semitones), name + "_pitch");
                if (resampled != null)
                {
                    Object.Destroy(sliced);
                    pitched = resampled;
                }
            }

            float[] samples = GetInterleaved(pitched);
            if (samples == null)
            {
                Object.Destroy(pitched);
                error = "音量处理失败（采样数据不可读）";
                return null;
            }

            Scale(samples, Mathf.Max(volume, 0f));
            if (fadeMs > 0)
                Fade(samples, pitched.channels, pitched.frequency, fadeMs, fadeMs);

            AudioClip result = CreateClip(name, samples, pitched.channels, pitched.frequency);
            Object.Destroy(pitched);
            if (result == null)
                error = "生成加工音频失败";
            return result;
        }

        /// <summary>安全销毁运行时音频（null 安全；Asset 资源不会被销毁）</summary>
        public static void DestroyClip(AudioClip clip)
        {
            if (clip != null)
                Object.Destroy(clip);
        }

        /// <summary>时长格式化（界面显示统一口径）</summary>
        public static string FormatSeconds(float seconds)
        {
            return string.Format("{0:0.00}s", Mathf.Max(seconds, 0f));
        }
    }
}
