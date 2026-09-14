using System;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Workshop;   // Vector2Data（项目统一的 JSON 友好二维坐标）

namespace TcgEngine.VFX
{
    /// <summary>混合模式：加法（发光）/ 普通透明</summary>
    public enum VFXBlend { Additive = 0, Alpha = 1 }

    /// <summary>何时播放：动作时 / 动作完成后延迟 / 事件触发时</summary>
    public enum VFXTrigger { OnAction = 0, AfterActionDelay = 1, OnEvent = 2 }

    /// <summary>绑定对象：目标卡 / 施法者（本卡）/ 场景固定坐标</summary>
    public enum VFXBind { TargetCard = 0, Caster = 1, WorldFixed = 2 }

    /// <summary>对齐锚点</summary>
    public enum VFXPivot { Center = 0, Bottom = 1, Top = 2, Left = 3, Right = 4 }

    /// <summary>曲线上的一个关键点（t/v 归一化 0..1）</summary>
    [Serializable]
    public class CurveKeyData
    {
        public float t;
        public float v;

        public CurveKeyData() { }
        public CurveKeyData(float t, float v) { this.t = t; this.v = v; }
    }

    /// <summary>
    /// 曲线的 JSON 友好替身：UnityEngine.AnimationCurve 无法被 JsonUtility 序列化（导入导出会丢），
    /// 因此配置里存"关键点数组 + 是否平滑"，运行时用 Evaluate 求值（存档/读档、导入导出都不丢）。
    /// </summary>
    [Serializable]
    public class CurveData
    {
        public string preset = "linear";
        public bool smooth;
        public List<CurveKeyData> keys = new List<CurveKeyData>();

        public CurveData() { }

        public float Evaluate(float t01)
        {
            if (keys == null || keys.Count == 0)
                return 1f;
            if (keys.Count == 1)
                return keys[0].v;

            t01 = Mathf.Clamp01(t01);
            if (t01 <= keys[0].t)
                return keys[0].v;
            if (t01 >= keys[keys.Count - 1].t)
                return keys[keys.Count - 1].v;

            for (int i = 0; i < keys.Count - 1; i++)
            {
                CurveKeyData a = keys[i];
                CurveKeyData b = keys[i + 1];
                if (t01 >= a.t && t01 <= b.t)
                {
                    float span = Mathf.Max(b.t - a.t, 0.0001f);
                    float u = Mathf.Clamp01((t01 - a.t) / span);
                    if (smooth)
                        u = u * u * (3f - 2f * u);   //平滑插值（等价于缓入缓出）
                    return Mathf.Lerp(a.v, b.v, u);
                }
            }
            return keys[keys.Count - 1].v;
        }

        /// <summary>转成 AnimationCurve（供需要曲线对象的场景/预览使用）</summary>
        public AnimationCurve ToCurve()
        {
            if (keys == null || keys.Count == 0)
                return AnimationCurve.Linear(0f, 1f, 1f, 1f);
            Keyframe[] ks = new Keyframe[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                float slope = 0f;
                if (!smooth && keys.Count > 1)
                {
                    int prev = Mathf.Max(i - 1, 0);
                    int next = Mathf.Min(i + 1, keys.Count - 1);
                    float dt = Mathf.Max(keys[next].t - keys[prev].t, 0.0001f);
                    slope = (keys[next].v - keys[prev].v) / dt;
                }
                ks[i] = new Keyframe(keys[i].t, keys[i].v, slope, slope);
            }
            return new AnimationCurve(ks);
        }

        public static CurveData FromCurve(AnimationCurve curve)
        {
            CurveData d = new CurveData { preset = "custom", smooth = true };
            if (curve != null)
            {
                for (int i = 0; i < curve.length; i++)
                {
                    Keyframe k = curve[i];
                    d.keys.Add(new CurveKeyData(k.time, k.value));
                }
            }
            if (d.keys.Count == 0)
                d.keys.Add(new CurveKeyData(0f, 1f));
            return d;
        }

        public static CurveData Preset(string preset, bool scale_curve)
        {
            CurveData d = new CurveData { preset = preset };
            switch (preset)
            {
                case "grow":
                    d.keys.Add(new CurveKeyData(0f, 0.5f));
                    d.keys.Add(new CurveKeyData(1f, 1.4f));
                    break;
                case "shrink":
                    d.keys.Add(new CurveKeyData(0f, 1.4f));
                    d.keys.Add(new CurveKeyData(1f, 0.4f));
                    break;
                case "pulse":   //小 → 大 → 小
                    d.smooth = true;
                    d.keys.Add(new CurveKeyData(0f, 0.7f));
                    d.keys.Add(new CurveKeyData(0.5f, 1.3f));
                    d.keys.Add(new CurveKeyData(1f, 0.7f));
                    break;
                case "pop":     //弹出：0 → 1.15 → 1
                    d.keys.Add(new CurveKeyData(0f, 0.05f));
                    d.keys.Add(new CurveKeyData(0.45f, 1.15f));
                    d.keys.Add(new CurveKeyData(0.7f, 1f));
                    d.keys.Add(new CurveKeyData(1f, 1f));
                    break;
                case "fade_out":
                    d.keys.Add(new CurveKeyData(0f, 1f));
                    d.keys.Add(new CurveKeyData(1f, 0f));
                    break;
                case "fade_in":
                    d.keys.Add(new CurveKeyData(0f, 0f));
                    d.keys.Add(new CurveKeyData(1f, 1f));
                    break;
                case "fade_in_out":
                    d.smooth = true;
                    d.keys.Add(new CurveKeyData(0f, 0f));
                    d.keys.Add(new CurveKeyData(0.25f, 1f));
                    d.keys.Add(new CurveKeyData(0.75f, 1f));
                    d.keys.Add(new CurveKeyData(1f, 0f));
                    break;
                case "flash":
                    d.keys.Add(new CurveKeyData(0f, 1f));
                    d.keys.Add(new CurveKeyData(0.3f, 0.25f));
                    d.keys.Add(new CurveKeyData(1f, 1f));
                    break;
                default:        //linear：恒定（不改变）
                    d.keys.Add(new CurveKeyData(0f, 1f));
                    d.keys.Add(new CurveKeyData(1f, 1f));
                    break;
            }
            if (scale_curve && d.preset == "linear")
                d.smooth = false;
            return d;
        }

        public CurveData Clone()
        {
            CurveData d = new CurveData { preset = preset, smooth = smooth };
            if (keys != null)
                for (int i = 0; i < keys.Count; i++)
                    d.keys.Add(new CurveKeyData(keys[i].t, keys[i].v));
            return d;
        }
    }

    [Serializable]
    public class GradientColorKeyData
    {
        public float t;
        public Color color = Color.white;
        public GradientColorKeyData() { }
        public GradientColorKeyData(float t, Color c) { this.t = t; color = c; }
    }

    [Serializable]
    public class GradientAlphaKeyData
    {
        public float t;
        public float a = 1f;
        public GradientAlphaKeyData() { }
        public GradientAlphaKeyData(float t, float a) { this.t = t; this.a = a; }
    }

    /// <summary>
    /// 颜色渐变的 JSON 友好替身（UnityEngine.Gradient 同样不能被 JsonUtility 序列化）。
    /// 颜色标 + 透明度标分开存，运行时用 Evaluate 求值。
    /// </summary>
    [Serializable]
    public class GradientData
    {
        public string preset = "white";
        public List<GradientColorKeyData> color_keys = new List<GradientColorKeyData>();
        public List<GradientAlphaKeyData> alpha_keys = new List<GradientAlphaKeyData>();

        public Color Evaluate(float t01)
        {
            t01 = Mathf.Clamp01(t01);
            Color c = EvalColor(t01);
            c.a = Mathf.Clamp01(c.a * EvalAlpha(t01));
            return c;
        }

        private Color EvalColor(float t)
        {
            if (color_keys == null || color_keys.Count == 0)
                return Color.white;
            if (color_keys.Count == 1)
                return color_keys[0].color;
            if (t <= color_keys[0].t)
                return color_keys[0].color;
            if (t >= color_keys[color_keys.Count - 1].t)
                return color_keys[color_keys.Count - 1].color;
            for (int i = 0; i < color_keys.Count - 1; i++)
            {
                GradientColorKeyData a = color_keys[i];
                GradientColorKeyData b = color_keys[i + 1];
                if (t >= a.t && t <= b.t)
                {
                    float span = Mathf.Max(b.t - a.t, 0.0001f);
                    return Color.Lerp(a.color, b.color, (t - a.t) / span);
                }
            }
            return color_keys[color_keys.Count - 1].color;
        }

        private float EvalAlpha(float t)
        {
            if (alpha_keys == null || alpha_keys.Count == 0)
                return 1f;
            if (alpha_keys.Count == 1)
                return alpha_keys[0].a;
            if (t <= alpha_keys[0].t)
                return alpha_keys[0].a;
            if (t >= alpha_keys[alpha_keys.Count - 1].t)
                return alpha_keys[alpha_keys.Count - 1].a;
            for (int i = 0; i < alpha_keys.Count - 1; i++)
            {
                GradientAlphaKeyData a = alpha_keys[i];
                GradientAlphaKeyData b = alpha_keys[i + 1];
                if (t >= a.t && t <= b.t)
                {
                    float span = Mathf.Max(b.t - a.t, 0.0001f);
                    return Mathf.Lerp(a.a, b.a, (t - a.t) / span);
                }
            }
            return alpha_keys[alpha_keys.Count - 1].a;
        }

        public Gradient ToGradient()
        {
            Gradient g = new Gradient();
            List<GradientColorKey> ck = new List<GradientColorKey>();
            if (color_keys != null)
                for (int i = 0; i < color_keys.Count; i++)
                    ck.Add(new GradientColorKey(color_keys[i].color, color_keys[i].t));
            List<GradientAlphaKey> ak = new List<GradientAlphaKey>();
            if (alpha_keys != null)
                for (int i = 0; i < alpha_keys.Count; i++)
                    ak.Add(new GradientAlphaKey(alpha_keys[i].a, alpha_keys[i].t));
            if (ck.Count == 0) ck.Add(new GradientColorKey(Color.white, 0f));
            if (ak.Count == 0) ak.Add(new GradientAlphaKey(1f, 0f));
            g.SetKeys(ck.ToArray(), ak.ToArray());
            return g;
        }

        public static GradientData Preset(string preset)
        {
            GradientData d = new GradientData { preset = preset };
            switch (preset)
            {
                case "fade_out":
                    d.color_keys.Add(new GradientColorKeyData(0f, Color.white));
                    d.color_keys.Add(new GradientColorKeyData(1f, Color.white));
                    d.alpha_keys.Add(new GradientAlphaKeyData(0f, 1f));
                    d.alpha_keys.Add(new GradientAlphaKeyData(1f, 0f));
                    break;
                case "warm":    //白 → 橙 → 红
                    d.color_keys.Add(new GradientColorKeyData(0f, Color.white));
                    d.color_keys.Add(new GradientColorKeyData(0.5f, new Color(1f, 0.72f, 0.3f)));
                    d.color_keys.Add(new GradientColorKeyData(1f, new Color(1f, 0.35f, 0.2f)));
                    d.alpha_keys.Add(new GradientAlphaKeyData(0f, 1f));
                    break;
                case "cool":    //青 → 蓝
                    d.color_keys.Add(new GradientColorKeyData(0f, new Color(0.7f, 0.95f, 1f)));
                    d.color_keys.Add(new GradientColorKeyData(1f, new Color(0.3f, 0.55f, 1f)));
                    d.alpha_keys.Add(new GradientAlphaKeyData(0f, 1f));
                    break;
                case "poison":  //黄绿 → 紫
                    d.color_keys.Add(new GradientColorKeyData(0f, new Color(0.75f, 1f, 0.4f)));
                    d.color_keys.Add(new GradientColorKeyData(1f, new Color(0.7f, 0.4f, 1f)));
                    d.alpha_keys.Add(new GradientAlphaKeyData(0f, 1f));
                    break;
                default:        //white：不着色
                    d.color_keys.Add(new GradientColorKeyData(0f, Color.white));
                    d.alpha_keys.Add(new GradientAlphaKeyData(0f, 1f));
                    break;
            }
            return d;
        }

        public GradientData Clone()
        {
            GradientData d = new GradientData { preset = preset };
            if (color_keys != null)
                for (int i = 0; i < color_keys.Count; i++)
                    d.color_keys.Add(new GradientColorKeyData(color_keys[i].t, color_keys[i].color));
            if (alpha_keys != null)
                for (int i = 0; i < alpha_keys.Count; i++)
                    d.alpha_keys.Add(new GradientAlphaKeyData(alpha_keys[i].t, alpha_keys[i].a));
            return d;
        }
    }

    /// <summary>
    /// 节点特效配置（挂在 GraphNode.vfx 上，随图 JSON 序列化）。
    ///
    /// 序列化说明（交付要求）：
    /// ① 序列帧不存 Sprite 引用（JsonUtility 无法持久化资源引用，导出/导入会丢），改存**帧名列表 + 素材来源目录**，
    ///    运行时用 VFXFrameLibrary 解析；② AnimationCurve / Gradient 用 CurveData / GradientData 替身（关键点/色标数组）；
    /// ③ Color 与 Vector2Data 都是 JsonUtility 原生支持的。
    /// </summary>
    [Serializable]
    public class VFXConfig
    {
        public bool enabled = true;

        // ---------------- 素材 ----------------
        public string frame_source = "FX";                  //素材来源目录标记（见 VFXFrameLibrary）
        public List<string> frame_names = new List<string>();
        public float frame_rate = 12f;                      //每秒帧数
        public VFXBlend blend = VFXBlend.Additive;

        // ---------------- 本地序列图（整张 spritesheet 切片；设置后优先于上面的帧名列表） ----------------
        // 适用场景：RPG Maker 风格的"一张 PNG 里含多帧"序列图（标准动画图 = 5 列 × 5 行 = 25 帧，
        // 播放顺序为从左到右、从上到下）。只存**路径与切片参数**（JsonUtility 友好，导出/导入不丢），
        // 运行时由 VFXFrameLibrary.ResolveSheet 读盘并切片成 Sprite 序列。
        public string sheet_path = "";                      //图片路径：绝对路径，或 "Assets/..." 相对路径，或工程 Sprites 目录内的文件名
        public int sheet_cell = 0;                          //单帧边长（像素）：0=自动识别（按图片尺寸取公因数，RPG Maker 动画恒为 192）；>0=手动指定
        public int sheet_cols = 5;                          //横向帧数（列）：由单帧尺寸/图片尺寸推算，编辑器选图时自动写入
        public int sheet_rows = 5;                          //纵向帧数（行）：同上（960×768 这类图是 5×4，不能一律按 5×5）
        public int sheet_row = -1;                          //只播第 N 行（0 基、自上而下；-1 = 全部行依次播）
        public int sheet_start = 0;                         //起始帧（在选定范围内的偏移，0 基）
        public int sheet_count = 0;                         //使用帧数（0 = 从起始帧到末尾全部）
        public float sheet_scale = 1f;                      //整图缩放补偿（单帧像素偏大时调小，如 0.5 = 缩到一半）

        // ---------------- 生命周期 ----------------
        public VFXTrigger trigger = VFXTrigger.OnAction;
        public float delay = 0f;                            //延迟秒数
        public bool loop = false;
        public float loop_duration = 1f;                    //循环持续时长（循环时到点销毁；<=0 用 10s 兜底）

        // ---------------- 绑定与位置 ----------------
        public VFXBind bind = VFXBind.TargetCard;
        public Vector2Data offset = new Vector2Data(0f, 0f);
        public VFXPivot pivot = VFXPivot.Center;            //特效相对绑定对象的对齐位置（中心/下缘/上缘/左缘/右缘）

        // ---------------- 飞行弹道（起点→终点；开弹道时 bind=起点） ----------------
        public bool travel = false;                         //true：特效从 bind（起点）飞向 bind_to（终点）
        public VFXBind bind_to = VFXBind.TargetCard;        //终点锚点
        public float travel_duration = 0f;                  //飞行时长（秒）：0=与动画时长一致
        public CurveData travel_curve = TravelPreset("linear");   //飞行缓动（输出 0→1 的位移比例）

        // ---------------- 音效 ----------------
        public string audio_path = "";                      //音效：本地绝对路径 / Workshop 音频文件名 / Resources 名
        public float audio_volume = 1f;                     //音量 0~1
        public bool audio_loop = false;                     //是否循环（弹道/命中类一般为 false）

        /// <summary>飞行缓动预设名 → 曲线（linear=匀速 / ease_out=先快后慢 / ease_in_out=两头慢）</summary>
        public static readonly string[] TRAVEL_PRESETS = { "linear", "ease_out", "ease_in_out" };

        public static CurveData TravelPreset(string preset)
        {
            CurveData d = new CurveData { preset = preset };
            switch (preset)
            {
                case "ease_out":
                    d.smooth = true;
                    d.keys.Add(new CurveKeyData(0f, 0f));
                    d.keys.Add(new CurveKeyData(0.6f, 0.85f));
                    d.keys.Add(new CurveKeyData(1f, 1f));
                    break;
                case "ease_in_out":
                    d.smooth = true;
                    d.keys.Add(new CurveKeyData(0f, 0f));
                    d.keys.Add(new CurveKeyData(0.5f, 0.5f));
                    d.keys.Add(new CurveKeyData(1f, 1f));
                    break;
                default:      //linear
                    d.keys.Add(new CurveKeyData(0f, 0f));
                    d.keys.Add(new CurveKeyData(1f, 1f));
                    break;
            }
            return d;
        }

        // ---------------- 颜色 ----------------
        public Color tint = Color.white;
        public GradientData color_over_time = GradientData.Preset("white");

        // ---------------- 曲线动画 ----------------
        public CurveData scale_curve = CurveData.Preset("linear", true);
        public CurveData alpha_curve = CurveData.Preset("linear", false);

        /// <summary>是否配置了音效</summary>
        public bool HasAudio
        {
            get { return !string.IsNullOrEmpty(audio_path); }
        }

        /// <summary>是否使用本地序列图（整张 PNG 切片）</summary>
        public bool HasSheet
        {
            get { return !string.IsNullOrEmpty(sheet_path); }
        }

        /// <summary>序列图的有效切片帧数（与 VFXFrameLibrary 的切片顺序保持一致：
        /// 先按"播放行"筛选，再套用起始帧与帧数；纯计算、不读盘，可在 AI 线程安全调用）</summary>
        public int EffectiveSheetCount
        {
            get
            {
                int cols = Mathf.Max(1, sheet_cols);
                int rows = Mathf.Max(1, sheet_rows);
                int total = sheet_row >= 0 ? cols : cols * rows;
                int start = Mathf.Clamp(sheet_start, 0, Mathf.Max(0, total - 1));
                int count = sheet_count > 0 ? sheet_count : (total - start);
                return Mathf.Max(0, Mathf.Min(count, total - start));
            }
        }

        public bool HasFrames
        {
            get { return HasSheet || (frame_names != null && frame_names.Count > 0); }
        }

        /// <summary>单次播放时长（秒）</summary>
        public float Duration
        {
            get
            {
                float rate = frame_rate > 0.01f ? frame_rate : 12f;
                int count = HasSheet ? EffectiveSheetCount : (HasFrames ? frame_names.Count : 1);
                return Mathf.Max(count, 1) / rate;
            }
        }

        public bool IsConfigured
        {
            get { return enabled && HasFrames; }
        }

        public string Summary()
        {
            if (!HasFrames)
                return "特效：未配置";
            string src = HasSheet
                ? ("本地序列图 " + Mathf.Max(1, sheet_cols) + "×" + Mathf.Max(1, sheet_rows)
                    + (sheet_row >= 0 ? (" 第" + (sheet_row + 1) + "行") : "")
                    + " / " + EffectiveSheetCount + "帧")
                : (frame_names.Count + " 帧");
            return "特效：" + src + " / " + frame_rate + "fps"
                + (blend == VFXBlend.Additive ? " / 加法" : " / 透明")
                + (loop ? " / 循环" : "")
                + (travel ? " / 弹道" : "")
                + (HasAudio ? " / 音效" : "");
        }

        public VFXConfig Clone()
        {
            return JsonUtility.FromJson<VFXConfig>(JsonUtility.ToJson(this));
        }

        public static VFXConfig CreateDefault()
        {
            VFXConfig c = new VFXConfig();
            c.scale_curve = CurveData.Preset("pop", true);      //默认"弹出"手感
            c.alpha_curve = CurveData.Preset("fade_out", false); //默认淡出
            c.color_over_time = GradientData.Preset("white");
            return c;
        }
    }
}
