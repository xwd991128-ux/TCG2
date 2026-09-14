using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.VFX;

namespace TcgEngine.UI
{
    /// <summary>
    /// 节点特效编辑器弹框（序列帧）：素材选择 / 播放生命周期 / 绑定与偏移 / 颜色 / 曲线动画，
    /// 并带"一键试播"实时预演（预览区就是一个 SpriteAnimationPlayer，配置改动即时反映）。
    ///
    /// 复用 UIPanel 弹层范式；确定后把 VFXConfig 交回宿主（GraphEditorPanel 写入 node.vfx，随图 JSON 落盘）。
    /// </summary>
    public class VFXEditorPopup : UIPanel
    {
        private const float PANEL_W = 760f;
        private const float PANEL_H = 820f;
        private const int THUMB_W = 150;
        private const int THUMB_H = 40;

        private static readonly string[] SCALE_PRESETS = { "linear", "pop", "pulse", "grow", "shrink" };
        private static readonly string[] ALPHA_PRESETS = { "linear", "fade_out", "fade_in", "fade_in_out", "flash" };
        private static readonly string[] COLOR_PRESETS = { "white", "fade_out", "warm", "cool", "poison" };
        private static readonly string[] SCALE_LABELS = { "不缩放", "弹出", "脉冲", "渐大", "渐小" };
        private static readonly string[] ALPHA_LABELS = { "不透明变化", "淡出", "淡入", "淡入淡出", "闪烁" };
        private static readonly string[] COLOR_LABELS = { "不着色", "白色→透明", "白→橙红(暖)", "青→蓝(冷)", "黄绿→紫(毒)" };
        //飞行缓动显示名：与 VFXConfig.TRAVEL_PRESETS 一一对应
        private static readonly string[] TRAVEL_LABELS = { "线性", "缓出", "缓入缓出" };

        private RectTransform panel_rect;
        private RectTransform gallery_panel;
        private RectTransform gallery_list;
        private Image preview_image;
        private SpriteAnimationPlayer preview_player;
        private RawImage scale_thumb, alpha_thumb;
        private Texture2D scale_tex, alpha_tex;
        private TMP_Text txt_title, txt_frames, txt_rate, txt_loop, txt_blend, txt_trigger, txt_delay,
            txt_bind, txt_offx, txt_offy, txt_pivot, txt_tint, txt_color, txt_status;
        private TMP_Text txt_scale, txt_alpha;
        private TMP_Text txt_travel, txt_travel_to, txt_travel_dur, txt_travel_curve, txt_audio, txt_volume;
        private GameObject travel_extra_go;          //弹道行右侧三项（终点/飞行/缓动）：未开弹道时整体隐藏
        private Button btn_pingpong;

        private VFXConfig cfg;                     //正在编辑的副本
        private Action<VFXConfig> on_confirm;
        private bool built;
        private bool pingpong;

        private static VFXEditorPopup instance;

        public static VFXEditorPopup Create(Transform context)
        {
            if (instance != null)
                return instance;
            GameObject go = new GameObject("VFXEditorPopup", typeof(RectTransform), typeof(CanvasGroup));
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            go.transform.SetParent(canvas != null ? canvas.transform : context, false);
            UIFactory.SetStretch(go.GetComponent<RectTransform>());
            go.transform.SetAsLastSibling();
            instance = go.AddComponent<VFXEditorPopup>();
            instance.EnsureBuilt();
            instance.Hide(true);
            return instance;
        }

        /// <summary>打开编辑（cfg 为 null 时用默认配置）</summary>
        public void Open(VFXConfig config, string node_title, Action<VFXConfig> confirm)
        {
            EnsureBuilt();
            cfg = config != null ? config.Clone() : VFXConfig.CreateDefault();
            on_confirm = confirm;
            pingpong = false;
            if (txt_title != null)
                txt_title.text = "编辑特效 · " + (string.IsNullOrEmpty(node_title) ? "节点" : node_title);
            RefreshAll();
            PlayPreview();
            Show();
            UIFonts.ApplyResolved(gameObject);
        }

        public override void Hide(bool instant = false)
        {
            if (preview_player != null)
                preview_player.Stop();
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }
            base.Hide(instant);
        }

        public override void Show(bool instant = false)
        {
            EnsureBuilt();
            base.Show(instant);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = true;
                canvas_group.interactable = true;
            }
        }

        private void OnDestroy()
        {
            if (scale_tex != null) Destroy(scale_tex);
            if (alpha_tex != null) Destroy(alpha_tex);
        }

        // ---------------- 刷新 ----------------

        private void RefreshAll()
        {
            if (cfg == null)
                return;
            if (txt_frames != null)
                txt_frames.text = FramesSummary();
            if (txt_rate != null)
                txt_rate.text = cfg.frame_rate.ToString("0.#") + " fps";
            if (txt_loop != null)
                txt_loop.text = cfg.loop ? "循环：开" : "循环：关";
            if (txt_blend != null)
                txt_blend.text = cfg.blend == VFXBlend.Additive ? "混合：加法" : "混合：透明";
            if (txt_trigger != null)
                txt_trigger.text = TriggerLabel(cfg.trigger);
            if (txt_delay != null)
                txt_delay.text = cfg.delay.ToString("0.0") + "s";
            if (txt_bind != null)
                txt_bind.text = BindLabel(cfg.bind);
            if (txt_offx != null)
                txt_offx.text = cfg.offset.x.ToString("0.0");
            if (txt_offy != null)
                txt_offy.text = cfg.offset.y.ToString("0.0");
            if (txt_pivot != null)
                txt_pivot.text = PivotLabel(cfg.pivot);
            if (txt_tint != null)
                txt_tint.text = "着色 " + ColorName(cfg.tint);
            if (txt_color != null)
                txt_color.text = "颜色随时间：" + ColorPresetLabel(cfg.color_over_time);
            if (txt_scale != null)
                txt_scale.text = "缩放：" + PresetLabel(SCALE_PRESETS, SCALE_LABELS, cfg.scale_curve != null ? cfg.scale_curve.preset : "linear");
            if (txt_alpha != null)
                txt_alpha.text = "透明：" + PresetLabel(ALPHA_PRESETS, ALPHA_LABELS, cfg.alpha_curve != null ? cfg.alpha_curve.preset : "linear");
            if (btn_pingpong != null && btn_pingpong.GetComponentInChildren<TMP_Text>(true) != null)
                btn_pingpong.GetComponentInChildren<TMP_Text>(true).text = pingpong ? "来回播：开" : "来回播";

            //弹道（起点=「绑定」，终点=bind_to；未开弹道时右侧三项隐藏）
            if (txt_travel != null)
                txt_travel.text = cfg.travel ? "弹道：开" : "弹道：关";
            if (txt_travel_to != null)
                txt_travel_to.text = "终点：" + BindShortLabel(cfg.bind_to);
            if (txt_travel_dur != null)
                txt_travel_dur.text = cfg.travel_duration > 0.01f ? (cfg.travel_duration.ToString("0.0") + "s") : "等长";
            if (txt_travel_curve != null)
                txt_travel_curve.text = "缓动：" + TravelLabel(cfg.travel_curve != null ? cfg.travel_curve.preset : "linear");
            if (travel_extra_go != null && travel_extra_go.activeSelf != cfg.travel)
                travel_extra_go.SetActive(cfg.travel);

            //音效
            if (txt_volume != null)
                txt_volume.text = cfg.audio_volume.ToString("0.0");
            if (txt_audio != null)
                txt_audio.text = string.IsNullOrEmpty(cfg.audio_path)
                    ? "未选择音效"
                    : System.IO.Path.GetFileName(cfg.audio_path);

            RebuildThumb(scale_thumb, ref scale_tex, cfg.scale_curve);
            RebuildThumb(alpha_thumb, ref alpha_tex, cfg.alpha_curve);
        }

        /// <summary>顶部帧数摘要（本地序列图优先显示：文件名 + 实际帧数）</summary>
        private string FramesSummary()
        {
            if (cfg == null)
                return "";
            if (cfg.HasSheet)
            {
                VFXFrameLibrary.SheetInfo si = VFXFrameLibrary.ProbeSheet(cfg);
                string name = SheetFileName();
                if (!si.ok)
                    return "序列图：" + (string.IsNullOrEmpty(si.message) ? "不可用" : si.message);
                return "序列图：" + name + " · " + si.used_count + " 帧";
            }
            return "序列帧：" + (cfg.HasFrames ? cfg.frame_names.Count + " 帧" : "未选择（点「选择序列帧」或「序列图…」）");
        }

        private string SheetFileName()
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.sheet_path))
                return "";
            string p = cfg.sheet_path.Replace("\\", "/");
            int i = p.LastIndexOf('/');
            return i >= 0 ? p.Substring(i + 1) : p;
        }

        /// <summary>未设置序列图时返回 null，便于预览/刷新分支判断</summary>
        private bool HasSheet()
        {
            return cfg != null && cfg.HasSheet;
        }

        private void ApplyToPreview()
        {
            if (preview_player == null || cfg == null)
                return;
            List<Sprite> frames = VFXFrameLibrary.ResolveFrames(cfg);
            preview_player.pingpong = pingpong;
            preview_player.Setup(cfg, frames, null, preview_image);
            preview_player.loop = cfg.loop || pingpong;
        }

        private void PlayPreview()
        {
            ApplyToPreview();
            if (preview_player == null)
                return;
            if (preview_player.frames == null || preview_player.frames.Count == 0)
            {
                SetStatus("请先选择素材：本地序列图（「序列图…」）或序列帧（「选择序列帧」）");
                return;
            }
            preview_player.Play();
            SetStatus("预演中：" + preview_player.frames.Count + " 帧 / " + cfg.frame_rate + " fps"
                + (preview_player.loop ? " / 循环" : ""));
        }

        private void SetStatus(string msg)
        {
            if (txt_status != null)
                txt_status.text = msg;
        }

        // ---------------- 操作 ----------------

        private void OnClickStop()
        {
            if (preview_player != null)
                preview_player.Stop();
            SetStatus("已停止预演");
        }

        private void OnClickPingpong()
        {
            pingpong = !pingpong;
            RefreshAll();
            PlayPreview();
        }

        private void OnClickConfirm()
        {
            if (cfg == null)
            {
                Hide();
                return;
            }
            if (!cfg.HasFrames)
                SetStatus("提示：未选择任何序列帧，保存后该节点不会出特效");

            VFXConfig result = cfg.Clone();
            Action<VFXConfig> cb = on_confirm;
            on_confirm = null;
            Hide();
            if (cb != null)
                cb(result);
        }

        private void OnClickCancel()
        {
            if (preview_player != null)
                preview_player.Stop();
            on_confirm = null;
            SetGalleryVisible(false);
            Hide();
        }

        // ---------------- 属性循环切换 ----------------

        private void CycleTrigger()
        {
            cfg.trigger = (VFXTrigger)(((int)cfg.trigger + 1) % 3);
            RefreshAll();
            ApplyToPreview();
        }

        private void CycleBind()
        {
            cfg.bind = (VFXBind)(((int)cfg.bind + 1) % 3);
            RefreshAll();
            ApplyToPreview();
        }

        private void CyclePivot()
        {
            cfg.pivot = (VFXPivot)(((int)cfg.pivot + 1) % 5);
            RefreshAll();
        }

        private void CycleBlend()
        {
            cfg.blend = cfg.blend == VFXBlend.Additive ? VFXBlend.Alpha : VFXBlend.Additive;
            RefreshAll();
            ApplyToPreview();
            SetStatus(cfg.blend == VFXBlend.Additive
                ? "加法混合：叠加发光（适合火焰/闪电/光效）"
                : "透明混合：常规覆盖（适合烟雾/碎片）");
        }

        private void CycleScale()
        {
            int i = IndexOf(SCALE_PRESETS, cfg.scale_curve != null ? cfg.scale_curve.preset : "linear");
            i = (i + 1) % SCALE_PRESETS.Length;
            cfg.scale_curve = CurveData.Preset(SCALE_PRESETS[i], true);
            RefreshAll();
            ApplyToPreview();
        }

        private void CycleAlpha()
        {
            int i = IndexOf(ALPHA_PRESETS, cfg.alpha_curve != null ? cfg.alpha_curve.preset : "linear");
            i = (i + 1) % ALPHA_PRESETS.Length;
            cfg.alpha_curve = CurveData.Preset(ALPHA_PRESETS[i], false);
            RefreshAll();
            ApplyToPreview();
        }

        private void CycleColorOverTime()
        {
            int i = IndexOf(COLOR_PRESETS, cfg.color_over_time != null ? cfg.color_over_time.preset : "white");
            i = (i + 1) % COLOR_PRESETS.Length;
            cfg.color_over_time = GradientData.Preset(COLOR_PRESETS[i]);
            RefreshAll();
            ApplyToPreview();
        }

        // ---------------- 弹道 / 音效 ----------------

        private void CycleTravel()
        {
            cfg.travel = !cfg.travel;
            RefreshAll();
            ApplyToPreview();
            SetStatus(cfg.travel
                ? ("弹道已开启：从「" + BindShortLabel(cfg.bind) + "」飞向「" + BindShortLabel(cfg.bind_to) + "」（起点=上面的「绑定」）" + TravelSameHint())
                : "弹道已关闭：特效原地播放");
        }

        private void CycleTravelTo()
        {
            cfg.bind_to = (VFXBind)(((int)cfg.bind_to + 1) % 3);
            RefreshAll();
            SetStatus("终点已改为「" + BindShortLabel(cfg.bind_to) + "」" + TravelSameHint());
        }

        /// <summary>起终点相同时不会产生位移，给出可操作提示（弹道是"从绑定飞向终点"）</summary>
        private string TravelSameHint()
        {
            return cfg.travel && cfg.bind == cfg.bind_to
                ? "（起终点相同 → 不会位移：请把上面的「绑定」改成另一个对象）"
                : "";
        }

        private void CycleTravelCurve()
        {
            int i = IndexOf(VFXConfig.TRAVEL_PRESETS, cfg.travel_curve != null ? cfg.travel_curve.preset : "linear");
            i = (i + 1) % VFXConfig.TRAVEL_PRESETS.Length;
            cfg.travel_curve = VFXConfig.TravelPreset(VFXConfig.TRAVEL_PRESETS[i]);
            RefreshAll();
        }

        private void OnClickPickAudio()
        {
            string file = AudioPicker.OpenLocalFile();
            if (string.IsNullOrEmpty(file))
            {
                SetStatus("未选择音效（本地文件导入仅 Windows 编辑器/独立版可用）");
                return;
            }
            cfg.audio_path = file.Replace("\\", "/");
            RefreshAll();
            SetStatus("已设置音效：" + System.IO.Path.GetFileName(cfg.audio_path)
                + "（运行时首次播放需异步解码，可能略滞后属正常）");
        }

        private void OnClickClearAudio()
        {
            cfg.audio_path = "";
            cfg.audio_volume = 1f;
            cfg.audio_loop = false;
            RefreshAll();
            SetStatus("已清除音效");
        }

        private static readonly Color[] TINT_PALETTE =
        {
            Color.white, new Color(1f, 0.5f, 0.4f), new Color(0.6f, 0.8f, 1f),
            new Color(0.6f, 1f, 0.6f), new Color(0.85f, 0.6f, 1f), new Color(1f, 0.9f, 0.5f)
        };
        private static readonly string[] TINT_NAMES = { "白", "红", "蓝", "绿", "紫", "金" };

        private void CycleTint()
        {
            int i = 0;
            for (int k = 0; k < TINT_PALETTE.Length; k++)
            {
                if (Approx(TINT_PALETTE[k], cfg.tint))
                {
                    i = k;
                    break;
                }
            }
            i = (i + 1) % TINT_PALETTE.Length;
            cfg.tint = TINT_PALETTE[i];
            RefreshAll();
            ApplyToPreview();
        }

        private static bool Approx(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.05f && Mathf.Abs(a.g - b.g) < 0.05f && Mathf.Abs(a.b - b.b) < 0.05f;
        }

        private static int IndexOf(string[] arr, string v)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == v)
                    return i;
            return 0;
        }

        // ---------------- 图库（序列帧选择） ----------------

        private void SetGalleryVisible(bool visible)
        {
            if (gallery_panel == null)
                return;
            gallery_panel.gameObject.SetActive(visible);
            if (visible)
            {
                gallery_panel.SetAsLastSibling();
                RebuildGallery();
            }
        }

        private void RebuildGallery()
        {
            if (gallery_list == null)
                return;
            for (int i = gallery_list.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(gallery_list.GetChild(i).gameObject);

            //素材只有一个来源：本地序列图（整张 PNG 切片）——
            //原「选择内置图片/序列帧列表」入口与列表已按要求移除；旧配置里已存的逐张帧列表仍可播放（此处仅提供清空）
            MakeSheetSection();
        }

        // ---------------- 本地序列图（整张 spritesheet 切片） ----------------

        private void MakeSheetSection()
        {
            MakeGalleryLabel("―― 本地序列图：一张 PNG 内含多帧，自动逐帧播放 ――");

            RectTransform row1 = MakeGalleryRowRect(38f);
            MakeBar(row1, "选择本地序列图…", 6f, 168f, new Color(0.5f, 0.78f, 1f, 0.34f), OnClickPickSheet);
            MakeBar(row1, "清除", 180f, 62f, new Color(1f, 0.6f, 0.6f, 0.28f), OnClickClearSheet);
            MakeBar(row1, "自动识别", 248f, 88f, new Color(0.85f, 0.7f, 1f, 0.34f), OnClickSheetAutoGrid);
            TMP_Text path = MakeText("SheetPath", row1, SheetPathLabel(), 14, UITheme.TextDim, TextAlignmentOptions.Right);
            Anchor(path.rectTransform, 0f, 0f, 1f, 1f, 1f, 0.5f, -360f, 0f, -10f, 0f);

            if (!HasSheet())
            {
                MakeGalleryLabel("（尚未选择本地序列图：点上方「选择序列图…」）");
                MakeLegacyFramesNotice();
                return;
            }

            VFXFrameLibrary.SheetInfo info = VFXFrameLibrary.ProbeSheet(cfg);
            MakeGalleryLabel(info.ok
                ? ("图片 " + info.width + "×" + info.height + " · 单帧 " + info.frame_w + "×" + info.frame_h
                    + " · " + info.total_cells + " 格（" + Mathf.Max(1, cfg.sheet_cols) + "列×" + Mathf.Max(1, cfg.sheet_rows) + "行）"
                    + " · 播放 " + info.used_count + " 帧"
                    + (string.IsNullOrEmpty(info.message) ? "" : "（" + info.message + "）"))
                : ("不可用：" + info.message));

            //切片：单帧尺寸（在"能整除整图"的候选值间切换；自动识别=按图片尺寸取最接近 192 的公因数）
            RectTransform row2 = MakeGalleryRowRect(36f);
            MakeBar(row2, "单帧 " + SheetCellLabel() + " -", 6f, 118f, BtnDim(), () => AdjustSheetCell(-1));
            MakeBar(row2, "+", 128f, 38f, BtnDim(), () => AdjustSheetCell(1));
            MakeBar(row2, SheetRowLabel(), 180f, 176f, new Color(1f, 0.85f, 0.45f, 0.3f), OnClickSheetCycleRow);
            MakeBar(row2, "范围复位", 366f, 92f, BtnDim(), OnClickSheetResetRange);

            RectTransform row3 = MakeGalleryRowRect(36f);
            MakeBar(row3, "起始 " + Mathf.Max(0, cfg.sheet_start) + " -", 6f, 100f, BtnDim(), () => AdjustSheet(2, -1));
            MakeBar(row3, "+", 110f, 38f, BtnDim(), () => AdjustSheet(2, 1));
            MakeBar(row3, "帧数 " + (cfg.sheet_count > 0 ? cfg.sheet_count.ToString() : "全部") + " -", 158f, 118f, BtnDim(), () => AdjustSheet(3, -1));
            MakeBar(row3, "+", 280f, 38f, BtnDim(), () => AdjustSheet(3, 1));
            MakeBar(row3, "缩放 " + cfg.sheet_scale.ToString("0.0") + " -", 328f, 112f, BtnDim(), () => AdjustSheet(4, -1));
            MakeBar(row3, "+", 444f, 38f, BtnDim(), () => AdjustSheet(4, 1));
            MakeBar(row3, "×2", 490f, 62f, new Color(0.85f, 0.9f, 1f, 0.28f), () => ScaleSheet(2f));
            MakeBar(row3, "÷2", 558f, 62f, new Color(0.85f, 0.9f, 1f, 0.28f), () => ScaleSheet(0.5f));

            MakeSheetPreview();
        }

        /// <summary>切片帧预览（最多 25 格，验证"第几帧是哪一格"是否正确）</summary>
        private void MakeSheetPreview()
        {
            List<Sprite> frames = VFXFrameLibrary.ResolveFrames(cfg);
            if (frames.Count == 0)
                return;
            const int PER_ROW = 8;
            int max = Mathf.Min(frames.Count, 25);
            RectTransform row = MakeGalleryRowRect(40f * Mathf.CeilToInt((float)max / PER_ROW) + 8f);
            GridLayoutGroup grid = row.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(40f, 40f);
            grid.spacing = new Vector2(4f, 4f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PER_ROW;
            grid.childAlignment = TextAnchor.UpperLeft;
            for (int i = 0; i < max; i++)
            {
                GameObject go = new GameObject("F" + i, typeof(RectTransform));
                go.transform.SetParent(row, false);
                Image img = go.AddComponent<Image>();
                img.sprite = frames[i];
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            if (frames.Count > max)
                MakeGalleryLabel("（预览前 " + max + " 帧；实际播放 " + frames.Count + " 帧）");
        }

        private RectTransform MakeGalleryRowRect(float height)
        {
            RectTransform rt = UIFactory.CreateRect("Row", gallery_list);
            rt.gameObject.AddComponent<LayoutElement>().minHeight = height;
            return rt;
        }

        private static Color BtnDim()
        {
            return new Color(1f, 1f, 1f, 0.14f);
        }

        private string SheetPathLabel()
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.sheet_path))
                return "未选择本地序列图";
            string p = cfg.sheet_path.Replace("\\", "/");
            int i = p.LastIndexOf('/');
            string file = i >= 0 ? p.Substring(i + 1) : p;
            int j = i > 0 ? p.LastIndexOf('/', i - 1) : -1;
            string dir = j >= 0 ? p.Substring(j + 1, i - j - 1) : "";
            return string.IsNullOrEmpty(dir) ? file : (dir + "/" + file);
        }

        private string SheetCellLabel()
        {
            if (cfg == null)
                return "";
            if (cfg.sheet_cell > 0)
                return cfg.sheet_cell + "×" + cfg.sheet_cell;
            VFXFrameLibrary.SheetInfo info = VFXFrameLibrary.ProbeSheet(cfg);
            return (info.frame_w > 0 && info.frame_h > 0)
                ? (info.frame_w + "×" + info.frame_h + "（自动）")
                : "自动";
        }

        private string SheetRowLabel()
        {
            if (cfg == null)
                return "播放范围";
            return cfg.sheet_row < 0 ? "播放范围：全部行" : ("播放范围：第 " + (cfg.sheet_row + 1) + " 行");
        }

        private void OnClickPickSheet()
        {
            string file = FileBrowserBridge.OpenImageFile("选择本地序列图（整张 PNG）");
            if (string.IsNullOrEmpty(file))
            {
                SetStatus("未选择序列图（本地文件导入仅 Windows 编辑器/独立版可用）");
                return;
            }
            cfg.sheet_path = file.Replace("\\", "/");
            AutoDetectSheetGrid(cfg, true);
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
            PlayPreview();
            VFXFrameLibrary.SheetInfo si = VFXFrameLibrary.ProbeSheet(cfg);
            SetStatus(si.ok
                ? ("已载入序列图：" + SheetFileName() + " · 单帧 " + si.frame_w + "×" + si.frame_h
                    + " · " + si.used_count + " 帧 · 缩放 " + cfg.sheet_scale.ToString("0.0") + "（可调）")
                : ("序列图不可用：" + si.message));
        }

        private void OnClickClearSheet()
        {
            cfg.sheet_path = "";
            cfg.sheet_row = -1;
            cfg.sheet_start = 0;
            cfg.sheet_count = 0;
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
            PlayPreview();
            SetStatus("已清除本地序列图（改回按序列帧列表播放）");
        }

        /// <summary>打开切片设置面板（素材/参数/帧预览都在该面板内）</summary>
        private void OnClickOpenSheetPanel()
        {
            SetGalleryVisible(true);
        }

        /// <summary>按图片尺寸重新自动识别切片网格（RPG Maker 动画单帧恒为 192×192）</summary>
        private void OnClickSheetAutoGrid()
        {
            cfg.sheet_cell = 0;
            AutoDetectSheetGrid(cfg, false);
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
            SetStatus("已自动识别切片：" + Mathf.Max(1, cfg.sheet_cols) + " 列 × " + Mathf.Max(1, cfg.sheet_rows) + " 行"
                + "（单帧 " + SheetCellLabel() + "）");
        }

        /// <summary>旧配置（逐张序列帧列表）遗留提示：只提供清空，播放逻辑仍保留以兼容旧图</summary>
        private void MakeLegacyFramesNotice()
        {
            if (cfg == null || cfg.frame_names == null || cfg.frame_names.Count == 0)
                return;
            RectTransform row = MakeGalleryRowRect(34f);
            MakeBar(row, "清除旧序列帧列表（" + cfg.frame_names.Count + " 张）", 6f, 260f,
                new Color(1f, 0.6f, 0.6f, 0.24f), OnClickClearLegacyFrames);
        }

        private void OnClickClearLegacyFrames()
        {
            if (cfg != null && cfg.frame_names != null)
                cfg.frame_names.Clear();
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
        }

        private void OnClickSheetCycleRow()
        {
            int rows = Mathf.Max(1, cfg.sheet_rows);
            cfg.sheet_row = cfg.sheet_row >= rows - 1 ? -1 : cfg.sheet_row + 1;
            cfg.sheet_start = 0;
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
        }

        private void OnClickSheetResetRange()
        {
            cfg.sheet_row = -1;
            cfg.sheet_start = 0;
            cfg.sheet_count = 0;
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
        }

        /// <summary>单帧尺寸 ±：只在"能整除整图"的候选值之间切换（候选=w、h 的公因数，降序）</summary>
        private void AdjustSheetCell(int delta)
        {
            if (cfg == null)
                return;
            VFXFrameLibrary.SheetInfo probe = VFXFrameLibrary.ProbeSheet(cfg);
            int w = probe.width;
            int h = probe.height;
            if (w <= 0 || h <= 0)
                return;
            List<int> cands = VFXFrameLibrary.SheetCellCandidates(w, h);
            if (cands.Count == 0)
                return;
            int cur = cfg.sheet_cell > 0 ? cfg.sheet_cell : VFXFrameLibrary.AutoSheetCell(w, h);
            int idx = cands.IndexOf(cur);
            if (idx < 0)
            {
                idx = 0;
                for (int i = 0; i < cands.Count; i++)
                {
                    if (Mathf.Abs(cands[i] - cur) < Mathf.Abs(cands[idx] - cur))
                        idx = i;
                }
            }
            //候选按降序：delta>0（想要更大的单帧）→ 索引减一
            idx = Mathf.Clamp(idx + (delta > 0 ? -1 : 1), 0, cands.Count - 1);
            cfg.sheet_cell = cands[idx];
            cfg.sheet_cols = Mathf.Max(1, w / cands[idx]);
            cfg.sheet_rows = Mathf.Max(1, h / cands[idx]);
            cfg.sheet_start = 0;
            if (cfg.sheet_row >= cfg.sheet_rows)
                cfg.sheet_row = -1;
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
            SetStatus("单帧 " + cfg.sheet_cell + "×" + cfg.sheet_cell + " → " + cfg.sheet_cols + " 列 × " + cfg.sheet_rows + " 行");
        }

        /// <summary>切片参数调整：2=起始帧 3=帧数（0=全部） 4=缩放（步长 0.1）</summary>
        private void AdjustSheet(int what, int delta)
        {
            if (cfg == null)
                return;
            switch (what)
            {
                case 2:
                    cfg.sheet_start = Mathf.Max(0, cfg.sheet_start + delta);
                    break;
                case 3:
                    cfg.sheet_count = Mathf.Max(0, cfg.sheet_count + delta);
                    break;
                case 4:
                    cfg.sheet_scale = Mathf.Clamp(cfg.sheet_scale + delta * 0.1f, 0.1f, 8f);
                    break;
            }
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
        }

        /// <summary>整图缩放倍率快捷调整（×2 / ÷2）：最终显示尺寸 = 单帧像素 / (100 / sheet_scale) × 缩放曲线</summary>
        private void ScaleSheet(float factor)
        {
            if (cfg == null)
                return;
            cfg.sheet_scale = Mathf.Clamp(cfg.sheet_scale * factor, 0.1f, 8f);
            RefreshAll();
            ApplyToPreview();
            RebuildGallery();
            SetStatus("特效缩放 = " + cfg.sheet_scale.ToString("0.00")
                + "（单帧 " + SheetCellLabel() + " → 约 " + (SheetContentUnits() * cfg.sheet_scale).ToString("0.0") + " 个棋盘单位）");
        }

        /// <summary>单帧在 100 PPU 下的基准世界尺寸（未乘缩放时）</summary>
        private float SheetContentUnits()
        {
            VFXFrameLibrary.SheetInfo info = VFXFrameLibrary.ProbeSheet(cfg);
            int px = info.frame_w > 0 ? info.frame_w : 0;
            return px / 100f;
        }

        /// <summary>按图片尺寸自动识别切片网格（统一分割逻辑：单帧取"能整除整图且最接近 192"的公因数）</summary>
        private void AutoDetectSheetGrid(VFXConfig c, bool reset_scale)
        {
            VFXFrameLibrary.SheetInfo probe = VFXFrameLibrary.ProbeSheet(c);
            c.sheet_row = -1;
            c.sheet_start = 0;
            c.sheet_count = 0;
            int w = probe.width;
            int h = probe.height;
            if (w <= 0 || h <= 0)
                return;
            int cols, rows, cell;
            VFXFrameLibrary.AutoGrid(w, h, c.sheet_cell, out cols, out rows, out cell);
            if (cols <= 0 || rows <= 0)
                return;
            c.sheet_cols = cols;
            c.sheet_rows = rows;
            c.sheet_cell = cell;

            //单帧偏大时给一个"可直接用"的默认缩放：RPG Maker 单帧 192px 在 100 PPU 下是 1.92 个棋盘单位，
            //明显大于一张卡；这里按"单帧约 0.9 个棋盘单位"折算（192px → 0.5），之后可用「缩放 -/+」微调。
            if (reset_scale)
            {
                int fw = Mathf.Max(1, w / Mathf.Max(1, cols));
                if (fw >= 64)
                    c.sheet_scale = Mathf.Clamp(Mathf.Round(150f / fw * 10f) / 10f, 0.1f, 8f);   //单帧约 1.5 个棋盘单位
            }
        }

        private void MakeGalleryLabel(string text)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(gallery_list, false);
            go.AddComponent<LayoutElement>().minHeight = 30f;
            TMP_Text t = MakeText("Text", go.transform, text, 14, UITheme.TextDim, TextAlignmentOptions.Left);
            Stretch(t.rectTransform, 8f, 8f, 0f, 0f);
        }

        // ---------------- 曲线缩略图 ----------------

        private void RebuildThumb(RawImage img, ref Texture2D tex, CurveData curve)
        {
            if (img == null)
                return;
            if (tex != null)
                Destroy(tex);
            tex = new Texture2D(THUMB_W, THUMB_H, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[THUMB_W * THUMB_H];
            Color32 bgc = new Color32(24, 26, 31, 255);
            for (int i = 0; i < px.Length; i++)
                px[i] = bgc;
            if (curve != null)
            {
                for (int x = 0; x < THUMB_W; x++)
                {
                    float t01 = (float)x / (THUMB_W - 1);
                    float v = Mathf.Clamp01(curve.Evaluate(t01));
                    int y = Mathf.RoundToInt(v * (THUMB_H - 3)) + 1;
                    px[x + y * THUMB_W] = new Color32(122, 214, 255, 255);
                    if (y + 1 < THUMB_H)
                        px[x + (y + 1) * THUMB_W] = new Color32(70, 130, 170, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            img.texture = tex;
        }

        // ---------------- 文本工具 ----------------

        private static string TriggerLabel(VFXTrigger t)
        {
            switch (t)
            {
                case VFXTrigger.AfterActionDelay: return "播放：动作完成后延迟";
                case VFXTrigger.OnEvent: return "播放：事件触发时";
                default: return "播放：动作时";
            }
        }

        private static string BindLabel(VFXBind b)
        {
            switch (b)
            {
                case VFXBind.Caster: return "绑定：施法者";
                case VFXBind.WorldFixed: return "绑定：场景固定坐标";
                default: return "绑定：目标卡";
            }
        }

        //锚点文案：把"特效中心对齐到绑定对象的哪条边"讲清楚（符号已修正，文案同步）
        private static string PivotLabel(VFXPivot p)
        {
            switch (p)
            {
                case VFXPivot.Bottom: return "锚点：绑定对象下缘";
                case VFXPivot.Top: return "锚点：绑定对象上缘";
                case VFXPivot.Left: return "锚点：绑定对象左缘";
                case VFXPivot.Right: return "锚点：绑定对象右缘";
                default: return "锚点：绑定对象中心";
            }
        }

        /// <summary>绑定对象的短名（弹道起/终点用，不含"绑定："前缀）</summary>
        private static string BindShortLabel(VFXBind b)
        {
            switch (b)
            {
                case VFXBind.Caster: return "施法者";
                case VFXBind.WorldFixed: return "场景固定";
                default: return "目标卡";
            }
        }

        private static string TravelLabel(string preset)
        {
            return TRAVEL_LABELS[IndexOf(VFXConfig.TRAVEL_PRESETS, preset)];
        }

        private static string ColorName(Color c)
        {
            for (int i = 0; i < TINT_PALETTE.Length; i++)
                if (Approx(TINT_PALETTE[i], c))
                    return TINT_NAMES[i];
            return "自定义";
        }

        private static string ColorPresetLabel(GradientData g)
        {
            return PresetLabel(COLOR_PRESETS, COLOR_LABELS, g != null ? g.preset : "white");
        }

        private static string PresetLabel(string[] keys, string[] labels, string preset)
        {
            int i = IndexOf(keys, preset);
            return labels[i];
        }

        // ---------------- UI 构建 ----------------

        private void EnsureBuilt()
        {
            if (built)
                return;
            built = true;

            Image mask = UIFactory.CreateImage("Mask", transform, UITheme.MaskPopup);
            UIFactory.SetStretch(mask.rectTransform);
            mask.raycastTarget = true;
            Button mask_btn = mask.gameObject.AddComponent<Button>();
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(OnClickCancel);

            GameObject panel_go = new GameObject("Panel", typeof(RectTransform));
            panel_go.transform.SetParent(transform, false);
            panel_rect = panel_go.GetComponent<RectTransform>();
            panel_rect.anchorMin = new Vector2(0.5f, 0.5f);
            panel_rect.anchorMax = new Vector2(0.5f, 0.5f);
            panel_rect.pivot = new Vector2(0.5f, 0.5f);
            float h = Mathf.Min(PANEL_H, Mathf.Max(600f, Screen.height - 60f));
            panel_rect.sizeDelta = new Vector2(PANEL_W, h);
            panel_rect.anchoredPosition = Vector2.zero;
            Image pbg = panel_go.AddComponent<Image>();
            pbg.color = UITheme.BgPopup;

            float y = 12f;

            //标题
            RectTransform title_row = MakeStack("TitleRow", 34f, ref y);
            txt_title = MakeText("Title", title_row, "编辑特效", 24, UITheme.TextTitle, TextAlignmentOptions.Left);
            Stretch(txt_title.rectTransform, 18f, 56f, 0f, 0f);
            Button close = MakeButton("Close", title_row, "×", new Color(1f, 1f, 1f, 0.14f), 20);
            Anchor(close.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 1f, 1f, 34f, 30f, -12f, -2f);
            close.onClick.AddListener(OnClickCancel);

            //工具条
            RectTransform bar = MakeStack("Bar", 40f, ref y);
            MakeBar(bar, "选择序列图…", 18f, 124f, new Color(0.85f, 0.7f, 1f, 0.38f), OnClickPickSheet);
            MakeBar(bar, "试播 ▶", 150f, 86f, new Color(0.4f, 0.85f, 0.6f, 0.34f), PlayPreview);
            MakeBar(bar, "停止 ■", 244f, 78f, new Color(1f, 0.6f, 0.6f, 0.28f), OnClickStop);
            btn_pingpong = MakeBar(bar, "来回播", 330f, 88f, new Color(1f, 0.85f, 0.45f, 0.3f), OnClickPingpong);
            MakeBar(bar, "切片设置…", 426f, 112f, new Color(0.5f, 0.78f, 1f, 0.34f), OnClickOpenSheetPanel);
            txt_frames = MakeText("Frames", bar, "", 15, UITheme.TextDim, TextAlignmentOptions.Right);
            Anchor(txt_frames.rectTransform, 1f, 0.5f, 1f, 0.5f, 1f, 0.5f, 156f, 26f, -14f, 0f);

            //预览区
            RectTransform preview_row = MakeStack("Preview", 240f, ref y);
            RectTransform area = UIFactory.CreateRect("Area", preview_row);
            Stretch(area, 200f, 200f, 6f, 6f);
            Image area_bg = area.gameObject.AddComponent<Image>();
            area_bg.color = new Color(0.06f, 0.07f, 0.09f, 1f);
            GameObject fx_go = new GameObject("FX", typeof(RectTransform));
            fx_go.transform.SetParent(area, false);
            RectTransform fx_rt = fx_go.GetComponent<RectTransform>();
            Stretch(fx_rt, 0f, 0f, 0f, 0f);
            preview_image = fx_go.AddComponent<Image>();
            preview_image.raycastTarget = false;
            preview_image.preserveAspect = true;
            preview_player = fx_go.AddComponent<SpriteAnimationPlayer>();

            //素材行
            RectTransform r1 = MakeStack("RowAsset1", 36f, ref y, 4f);
            MakeBar(r1, "帧率 -", 18f, 76f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.frame_rate = Mathf.Max(1f, cfg.frame_rate - 2f); RefreshAll(); ApplyToPreview(); });
            txt_rate = MakeText("Rate", r1, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Anchor(txt_rate.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 90f, 26f, 100f, 0f);
            MakeBar(r1, "帧率 +", 196f, 76f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.frame_rate = Mathf.Min(60f, cfg.frame_rate + 2f); RefreshAll(); ApplyToPreview(); });
            txt_loop = MakeText("Loop", r1, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button loop_btn = MakeBar(r1, "", 282f, 110f, new Color(1f, 1f, 1f, 0.16f), () => { cfg.loop = !cfg.loop; RefreshAll(); ApplyToPreview(); });
            txt_loop.rectTransform.SetParent(loop_btn.transform, false);
            UIFactory.SetStretch(txt_loop.rectTransform);
            txt_blend = MakeText("Blend", r1, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button blend_btn = MakeBar(r1, "", 400f, 130f, new Color(0.6f, 0.72f, 1f, 0.3f), CycleBlend);
            txt_blend.rectTransform.SetParent(blend_btn.transform, false);
            UIFactory.SetStretch(txt_blend.rectTransform);

            //触发行
            RectTransform r2 = MakeStack("RowTrigger", 36f, ref y, 4f);
            txt_trigger = MakeText("Trigger", r2, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button trig_btn = MakeBar(r2, "", 18f, 220f, new Color(1f, 1f, 1f, 0.16f), CycleTrigger);
            txt_trigger.rectTransform.SetParent(trig_btn.transform, false);
            UIFactory.SetStretch(txt_trigger.rectTransform);
            MakeBar(r2, "延迟 -", 246f, 76f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.delay = Mathf.Max(0f, cfg.delay - 0.1f); RefreshAll(); });
            txt_delay = MakeText("Delay", r2, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Anchor(txt_delay.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 84f, 26f, 328f, 0f);
            MakeBar(r2, "延迟 +", 418f, 76f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.delay = Mathf.Min(5f, cfg.delay + 0.1f); RefreshAll(); });

            //位置行
            RectTransform r3 = MakeStack("RowBind", 36f, ref y, 4f);
            txt_bind = MakeText("Bind", r3, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button bind_btn = MakeBar(r3, "", 18f, 150f, new Color(1f, 1f, 1f, 0.16f), CycleBind);
            txt_bind.rectTransform.SetParent(bind_btn.transform, false);
            UIFactory.SetStretch(txt_bind.rectTransform);
            txt_pivot = MakeText("Pivot", r3, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button pivot_btn = MakeBar(r3, "", 176f, 150f, new Color(1f, 1f, 1f, 0.16f), CyclePivot);
            txt_pivot.rectTransform.SetParent(pivot_btn.transform, false);
            UIFactory.SetStretch(txt_pivot.rectTransform);
            MakeBar(r3, "X -", 334f, 46f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.offset.x -= 0.1f; RefreshAll(); });
            txt_offx = MakeText("OffX", r3, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Anchor(txt_offx.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 56f, 26f, 384f, 0f);
            MakeBar(r3, "X +", 444f, 46f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.offset.x += 0.1f; RefreshAll(); });
            MakeBar(r3, "Y -", 496f, 46f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.offset.y -= 0.1f; RefreshAll(); });
            txt_offy = MakeText("OffY", r3, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Anchor(txt_offy.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 56f, 26f, 546f, 0f);
            MakeBar(r3, "Y +", 608f, 46f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.offset.y += 0.1f; RefreshAll(); });

            //弹道行（起点=上面的「绑定」，终点=bind_to；未开弹道时右侧三项整体隐藏）
            RectTransform r3b = MakeStack("RowTravel", 36f, ref y, 4f);
            txt_travel = MakeText("Travel", r3b, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button travel_btn = MakeBar(r3b, "", 18f, 144f, new Color(1f, 0.75f, 0.4f, 0.3f), CycleTravel);
            txt_travel.rectTransform.SetParent(travel_btn.transform, false);
            UIFactory.SetStretch(txt_travel.rectTransform);

            RectTransform travel_extra = UIFactory.CreateRect("TravelExtra", r3b);
            UIFactory.SetStretch(travel_extra);
            travel_extra_go = travel_extra.gameObject;

            txt_travel_to = MakeText("TravelTo", travel_extra, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button to_btn = MakeBar(travel_extra, "", 170f, 146f, new Color(1f, 1f, 1f, 0.16f), CycleTravelTo);
            txt_travel_to.rectTransform.SetParent(to_btn.transform, false);
            UIFactory.SetStretch(txt_travel_to.rectTransform);

            MakeBar(travel_extra, "飞行 -", 324f, 70f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.travel_duration = Mathf.Max(0f, cfg.travel_duration - 0.1f); RefreshAll(); });
            txt_travel_dur = MakeText("FlyDur", travel_extra, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Anchor(txt_travel_dur.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 80f, 26f, 400f, 0f);
            MakeBar(travel_extra, "飞行 +", 486f, 70f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.travel_duration = Mathf.Min(10f, cfg.travel_duration + 0.1f); RefreshAll(); });

            txt_travel_curve = MakeText("TravelCurve", travel_extra, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button travel_curve_btn = MakeBar(travel_extra, "", 564f, 160f, new Color(0.6f, 0.9f, 1f, 0.28f), CycleTravelCurve);
            txt_travel_curve.rectTransform.SetParent(travel_curve_btn.transform, false);
            UIFactory.SetStretch(txt_travel_curve.rectTransform);
            travel_extra_go.SetActive(false);

            //音效行（素材/音量；播放由 VFXRuntime + VFXAudio 负责）
            RectTransform r3c = MakeStack("RowAudio", 36f, ref y, 4f);
            MakeBar(r3c, "选择音效…", 18f, 140f, new Color(0.85f, 0.7f, 1f, 0.34f), OnClickPickAudio);
            MakeBar(r3c, "清除音效", 166f, 100f, new Color(1f, 0.6f, 0.6f, 0.28f), OnClickClearAudio);
            MakeBar(r3c, "音量 -", 274f, 70f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.audio_volume = Mathf.Clamp01(cfg.audio_volume - 0.1f); RefreshAll(); });
            txt_volume = MakeText("Volume", r3c, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Anchor(txt_volume.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 80f, 26f, 352f, 0f);
            MakeBar(r3c, "音量 +", 440f, 70f, new Color(1f, 1f, 1f, 0.14f), () => { cfg.audio_volume = Mathf.Clamp01(cfg.audio_volume + 0.1f); RefreshAll(); });
            txt_audio = MakeText("AudioName", r3c, "", 14, UITheme.TextDim, TextAlignmentOptions.Right);
            Anchor(txt_audio.rectTransform, 1f, 0.5f, 1f, 0.5f, 1f, 0.5f, 190f, 26f, -18f, 0f);

            //颜色行
            RectTransform r4 = MakeStack("RowColor", 36f, ref y, 4f);
            txt_tint = MakeText("Tint", r4, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button tint_btn = MakeBar(r4, "", 18f, 130f, new Color(1f, 1f, 1f, 0.16f), CycleTint);
            txt_tint.rectTransform.SetParent(tint_btn.transform, false);
            UIFactory.SetStretch(txt_tint.rectTransform);
            txt_color = MakeText("ColorOverTime", r4, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button color_btn = MakeBar(r4, "", 156f, 250f, new Color(0.6f, 0.72f, 1f, 0.28f), CycleColorOverTime);
            txt_color.rectTransform.SetParent(color_btn.transform, false);
            UIFactory.SetStretch(txt_color.rectTransform);

            //曲线行（缩放 / 透明 + 缩略图）
            RectTransform r5 = MakeStack("RowCurve1", 30f, ref y, 2f);
            txt_scale = MakeText("Scale", r5, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button scale_btn = MakeBar(r5, "", 18f, 150f, new Color(1f, 0.85f, 0.45f, 0.3f), CycleScale);
            txt_scale.rectTransform.SetParent(scale_btn.transform, false);
            UIFactory.SetStretch(txt_scale.rectTransform);
            scale_thumb = MakeThumb(r5, 182f);

            RectTransform r6 = MakeStack("RowCurve2", 30f, ref y, 2f);
            txt_alpha = MakeText("Alpha", r6, "", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Button alpha_btn = MakeBar(r6, "", 18f, 150f, new Color(0.6f, 0.9f, 1f, 0.3f), CycleAlpha);
            txt_alpha.rectTransform.SetParent(alpha_btn.transform, false);
            UIFactory.SetStretch(txt_alpha.rectTransform);
            alpha_thumb = MakeThumb(r6, 182f);

            //底部
            RectTransform bottom = MakeStack("Bottom", 44f, ref y, 0f);
            Button ok = MakeBar(bottom, "确定", 18f, 130f, new Color(0.45f, 0.85f, 0.5f, 0.45f), OnClickConfirm);
            MakeBar(bottom, "取消", 158f, 110f, new Color(1f, 1f, 1f, 0.16f), OnClickCancel);
            txt_status = MakeText("Status", bottom, "", 15, UITheme.TextDim, TextAlignmentOptions.Right);
            Anchor(txt_status.rectTransform, 0f, 0f, 1f, 1f, 1f, 0.5f, -300f, 0f, -18f, 0f);

            EnsureGallery();
            UIFonts.ApplyResolved(gameObject);
        }

        private RawImage MakeThumb(Transform parent, float x)
        {
            GameObject go = new GameObject("Thumb", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            Anchor(rt, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, THUMB_W, THUMB_H, x, 0f);
            RawImage img = go.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        private void EnsureGallery()
        {
            if (gallery_panel != null)
                return;
            gallery_panel = UIFactory.CreateRect("Gallery", panel_rect);
            UIFactory.SetStretch(gallery_panel);
            Image gbg = gallery_panel.gameObject.AddComponent<Image>();
            gbg.color = new Color(UITheme.BgPopup.r, UITheme.BgPopup.g, UITheme.BgPopup.b, 0.98f);

            TMP_Text gt = MakeText("Title", gallery_panel, "素材：本地序列图（整图切片）/ 序列帧列表", 20, UITheme.TextTitle, TextAlignmentOptions.Left);
            Anchor(gt.rectTransform, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 30f, 18f, -12f);
            Button gc = MakeButton("Close", gallery_panel, "×", new Color(1f, 1f, 1f, 0.14f), 20);
            Anchor(gc.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 1f, 1f, 34f, 30f, -12f, -10f);
            gc.onClick.AddListener(() => SetGalleryVisible(false));

            GameObject scroll_go = new GameObject("Scroll", typeof(RectTransform));
            scroll_go.transform.SetParent(gallery_panel, false);
            RectTransform scroll_rt = scroll_go.GetComponent<RectTransform>();
            Stretch(scroll_rt, 16f, 16f, 52f, 16f);
            Image sgbg = scroll_go.AddComponent<Image>();
            sgbg.color = new Color(1f, 1f, 1f, 0.05f);
            scroll_go.AddComponent<RectMask2D>();
            ScrollRect sr = scroll_go.AddComponent<ScrollRect>();

            GameObject list_go = new GameObject("Content", typeof(RectTransform));
            list_go.transform.SetParent(scroll_go.transform, false);
            gallery_list = list_go.GetComponent<RectTransform>();
            gallery_list.anchorMin = new Vector2(0f, 1f);
            gallery_list.anchorMax = new Vector2(1f, 1f);
            gallery_list.pivot = new Vector2(0.5f, 1f);
            gallery_list.sizeDelta = Vector2.zero;
            VerticalLayoutGroup vlg = list_go.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            ContentSizeFitter fitter = list_go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = scroll_rt;
            sr.content = gallery_list;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            gallery_panel.gameObject.SetActive(false);
        }

        // ---------------- 控件工具 ----------------

        private RectTransform MakeStack(string name, float height, ref float y, float gap = 10f)
        {
            RectTransform rt = UIFactory.CreateRect(name, panel_rect);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-36f, height);
            rt.anchoredPosition = new Vector2(0f, -y);
            y += height + gap;
            return rt;
        }

        private Button MakeBar(Transform parent, string label, float x, float w, Color color, UnityEngine.Events.UnityAction action)
        {
            Button b = MakeButton(string.IsNullOrEmpty(label) ? "Bar" : ("Bar_" + label), parent, label, color, 15);
            Anchor(b.GetComponent<RectTransform>(), 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, w, 28f, x, 0f);
            if (action != null)
                b.onClick.AddListener(action);
            return b;
        }

        private static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void Anchor(RectTransform rt, float ax_min, float ay_min, float ax_max, float ay_max,
            float px, float py, float w, float h, float x, float y)
        {
            rt.anchorMin = new Vector2(ax_min, ay_min);
            rt.anchorMax = new Vector2(ax_max, ay_max);
            rt.pivot = new Vector2(px, py);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private TMP_Text MakeText(string name, Transform parent, string txt, int size, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyFont(t);
            t.text = txt;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private Button MakeButton(string name, Transform parent, string label, Color color, int size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            UITheme.ApplyButtonColors(b);
            if (!string.IsNullOrEmpty(label))
            {
                TMP_Text t = MakeText("Text", go.transform, label, size, Color.white, TextAlignmentOptions.Center);
                UIFactory.SetStretch(t.rectTransform);
            }
            return b;
        }
    }
}
