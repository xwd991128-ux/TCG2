using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.VFX
{
    /// <summary>
    /// 序列帧播放器（编辑器预演 / 战斗内共用）：
    /// 按 frameRate 切帧，支持 播放/停止/循环/来回播/从第 N 帧/时间缩放；
    /// 每帧按 缩放曲线、透明曲线、颜色渐变 驱动 localScale 与 color（tint 叠加）；
    /// 混合模式通过材质切换实现（加法 / 普通透明），SpriteRenderer（战斗）与 Image（预览）都支持。
    /// </summary>
    public class SpriteAnimationPlayer : MonoBehaviour
    {
        public SpriteRenderer sprite_renderer;
        public Image ui_image;

        public List<Sprite> frames = new List<Sprite>();
        public float frame_rate = 12f;
        public bool loop;
        public bool pingpong;                 //来回播（预览用；循环时生效）

        public Color tint = Color.white;
        public CurveData scale_curve;
        public CurveData alpha_curve;
        public GradientData color_over_time;
        public VFXBlend blend = VFXBlend.Additive;

        /// <summary>父级缩放补偿（由 VFXRuntime 按父级 lossyScale 反向设置）：
        /// 最终 localScale = base_scale × 缩放曲线值 —— 让特效的**世界尺寸只由素材决定**，
        /// 不因挂在"卡"还是"玩家地块"（后者可能带缩放）而变大变小。</summary>
        public Vector2 base_scale = Vector2.one;

        public float time_scale = 1f;

        /// <summary>单次播放结束（loop=false 时触发一次）</summary>
        public event Action onFinished;

        private bool playing;
        private float elapsed;
        private int current_frame = -1;
        private bool finished_raised;

        //材质缓存（全局共享，避免每次实例化都创建材质）
        private static Material mat_additive_battle;
        private static Material mat_alpha_battle;
        private static Material mat_additive_ui;
        private static bool blend_warned;

        public bool IsPlaying { get { return playing; } }
        public float Elapsed { get { return elapsed; } }
        public float Duration
        {
            get
            {
                float rate = frame_rate > 0.01f ? frame_rate : 12f;
                return Mathf.Max(frames.Count, 1) / rate;
            }
        }

        public float Progress01
        {
            get
            {
                float d = Duration;
                return d > 0.001f ? Mathf.Clamp01(elapsed / d) : 1f;
            }
        }

        /// <summary>应用配置（不解算帧序列，帧由调用方解析好传入）</summary>
        public void Setup(VFXConfig cfg, List<Sprite> resolved_frames, SpriteRenderer sr = null, Image img = null)
        {
            sprite_renderer = sr != null ? sr : sprite_renderer;
            ui_image = img != null ? img : ui_image;
            frames = resolved_frames ?? new List<Sprite>();

            if (cfg != null)
            {
                frame_rate = cfg.frame_rate > 0.01f ? cfg.frame_rate : 12f;
                loop = cfg.loop;
                tint = cfg.tint;
                scale_curve = cfg.scale_curve;
                alpha_curve = cfg.alpha_curve;
                color_over_time = cfg.color_over_time;
                blend = cfg.blend;
            }

            ApplyBlend();
            ApplyFrame(0, true);
        }

        public void Play(int from_frame = 0)
        {
            if (frames == null || frames.Count == 0)
                return;
            playing = true;
            finished_raised = false;
            elapsed = 0f;
            current_frame = -1;
            ApplyFrame(Mathf.Clamp(from_frame, 0, frames.Count - 1), true);
        }

        public void Stop()
        {
            playing = false;
        }

        public void SetTimeScale(float scale)
        {
            time_scale = Mathf.Max(scale, 0f);
        }

        private void Update()
        {
            if (!playing || frames.Count == 0)
                return;

            elapsed += Time.unscaledDeltaTime * Mathf.Max(time_scale, 0f);
            float rate = frame_rate > 0.01f ? frame_rate : 12f;

            int total = frames.Count;
            int idx;
            float cycle = total / rate;                       //一个来回/一轮的时长
            if (cycle <= 0.0001f)
                cycle = 0.0001f;

            if (loop)
            {
                float t = Mathf.Repeat(elapsed, cycle);
                float p = t / cycle;
                if (pingpong && total > 1)
                    p = p < 0.5f ? p * 2f : (1f - p) * 2f;   //0→1→0
                idx = Mathf.Clamp(Mathf.FloorToInt(p * total), 0, total - 1);
            }
            else
            {
                idx = Mathf.FloorToInt(elapsed * rate);
                if (idx >= total)
                {
                    idx = total - 1;
                    ApplyFrame(idx, true);
                    playing = false;
                    if (!finished_raised)
                    {
                        finished_raised = true;
                        if (onFinished != null)
                            onFinished.Invoke();
                    }
                    return;
                }
            }

            ApplyFrame(idx, false);
        }

        private void ApplyFrame(int idx, bool force)
        {
            if (!force && idx == current_frame)
                return;
            current_frame = idx;

            Sprite sprite = (frames.Count > 0 && idx >= 0 && idx < frames.Count) ? frames[idx] : null;
            if (sprite_renderer != null)
                sprite_renderer.sprite = sprite;
            if (ui_image != null)
                ui_image.sprite = sprite;

            ApplyAnim();
        }

        /// <summary>按进度驱动缩放/透明/颜色（每帧调用）</summary>
        private void ApplyAnim()
        {
            float p = Progress01;

            float scale = scale_curve != null ? scale_curve.Evaluate(p) : 1f;
            transform.localScale = new Vector3(base_scale.x * scale, base_scale.y * scale, 1f);

            float alpha = alpha_curve != null ? alpha_curve.Evaluate(p) : 1f;
            Color grad = color_over_time != null ? color_over_time.Evaluate(p) : Color.white;
            Color c = new Color(tint.r * grad.r, tint.g * grad.g, tint.b * grad.b,
                tint.a * grad.a * Mathf.Clamp01(alpha));

            if (sprite_renderer != null)
                sprite_renderer.color = c;
            if (ui_image != null)
                ui_image.color = c;
        }

        // ---------------- 混合模式材质 ----------------

        private void ApplyBlend()
        {
            if (sprite_renderer != null)
            {
                Material m = GetBattleMaterial(blend);
                if (m != null)
                    sprite_renderer.sharedMaterial = m;
            }
            if (ui_image != null)
                ui_image.material = GetUIMaterial(blend);
        }

        private static Material GetBattleMaterial(VFXBlend blend)
        {
            if (blend == VFXBlend.Additive)
            {
                if (mat_additive_battle == null)
                {
                    //与项目 FXGlowAdd.mat 同一套参数：URP Particles/Unlit + 预乘加法（_Blend=2, SrcAlpha/One, ZWrite 0）
                    Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if (sh == null)
                        sh = Shader.Find("Sprites/Default");
                    if (sh != null)
                    {
                        Material m = new Material(sh);
                        if (sh.name.Contains("Particles"))
                        {
                            m.SetFloat("_Surface", 1f);
                            m.SetFloat("_Blend", 2f);
                            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                            m.SetFloat("_ZWrite", 0f);
                            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                            m.EnableKeyword("_COLORADDSUBDIFF_ON");
                            m.DisableKeyword("_ALPHATEST_ON");
                            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        }
                        mat_additive_battle = m;
                    }
                }
                if (mat_additive_battle == null && !blend_warned)
                {
                    blend_warned = true;
                    Debug.LogWarning("[VFX] 未找到加法混合着色器（URP Particles/Unlit），已退化为普通透明。");
                }
                return mat_additive_battle;
            }

            if (mat_alpha_battle == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh == null)
                    sh = Shader.Find("Sprites/Default");
                if (sh == null)
                    sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null)
                    mat_alpha_battle = new Material(sh);
            }
            return mat_alpha_battle;
        }

        private static Material GetUIMaterial(VFXBlend blend)
        {
            if (blend != VFXBlend.Additive)
                return null;      //透明：UI 默认材质（null=Image 自带）

            if (mat_additive_ui == null)
            {
                //UI 上的加法：优先用粒子加法（顶点色×贴图，叠加），退化为 UI 默认
                Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
                if (sh == null)
                    sh = Shader.Find("Mobile/Particles/Additive");
                if (sh == null)
                    sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh != null)
                    mat_additive_ui = new Material(sh);
            }
            return mat_additive_ui;   //可能为 null（退化透明，界面仍然可预览动画）
        }
    }
}
