using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 音效DIY编辑弹框：波形选区（双手柄）→ 音量/音高/淡入淡出参数 → 试听 → 确定回写。
    ///
    /// 设计要点：
    /// ① 继承 UIPanel，复用项目弹框的淡入淡出与"运行时自建 UI"范式（与 ImageClipPopupUI 同结构）；
    /// ② 波形/选区交给可复用的 AudioWaveformView；加工交给纯函数 AudioClipDSP；
    /// ③ 音高=重采样（变速变调，结果自包含）；音量=采样缩放；淡入淡出=毫秒级头尾包络；
    /// ④ 试听走 AudioTool 独立通道 "audio_editor_sfx"，不打断对局音频通道，参数改动后重新试听即可听到最新效果；
    /// ⑤ 重加工只在"选区提交 / 参数松手 / 载入素材 / 重置 / 试听 / 确定"时执行，拖动过程只重绘波形，避免卡顿。
    /// </summary>
    public class AudioClipEditorPopupUI : UIPanel
    {
        private const string PREVIEW_CHANNEL = "audio_editor_sfx";
        private const float PANEL_WIDTH = 580f;
        private const float DEFAULT_FADE_MS = 20f;
        private const int THUMB_W = 236;
        private const int THUMB_H = 84;

        // ---------------- 状态 ----------------
        private AudioClipEditorUI m_owner;
        private Action<AudioClip, AudioClip, string> m_callback;

        private AudioClip m_source;         //当前素材（正在编辑的音频）
        private string m_source_file;       //素材来源名（文件名/资源名）
        private bool m_source_owned;        //素材是否由本弹框创建（自建的要销毁，缓存/资源的不销毁）

        private AudioClip m_result;         //加工结果（缓存；dirty 时重建）
        private bool m_dirty = true;

        private float m_volume = 1f;        //0..2（100%）
        private float m_semitones = 0f;     //-12..12 半音（重采样：变速变调）
        private float m_fade_ms = DEFAULT_FADE_MS;

        private bool m_suppress;            //程序化改控件值时抑制回调
        private bool built;

        // ---------------- UI ----------------
        private RectTransform panel_rect;
        private TMP_Text txt_title, txt_info, txt_sel, txt_out, txt_status;
        private TMP_Text txt_volume, txt_pitch, txt_fade;
        private TMP_InputField in_start, in_end;
        private SliderDrag sl_volume, sl_pitch, sl_fade;
        private AudioWaveformView wave_view;
        private RawImage thumb_image;
        private Texture2D thumb_tex;
        private Button btn_confirm;
        private RectTransform gallery_panel;
        private RectTransform gallery_list;

        // ---------------- 创建 ----------------

        public static AudioClipEditorPopupUI Create(Transform context)
        {
            GameObject go = new GameObject("AudioClipEditorPopup", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(context, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            UIFactory.SetStretch(rt);
            Canvas parent_canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            if (parent_canvas != null)
                go.transform.SetParent(parent_canvas.transform, false);
            go.transform.SetAsLastSibling();

            AudioClipEditorPopupUI popup = go.AddComponent<AudioClipEditorPopupUI>();
            popup.EnsureBuilt();
            popup.Hide(true);
            return popup;
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

        public override void Hide(bool instant = false)
        {
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }
            base.Hide(instant);
        }

        // ---------------- 打开 ----------------

        /// <summary>由入口组件打开（结果通过 owner.ApplyResult 回写）</summary>
        public void Open(AudioClipEditorUI owner, AudioClip source, string src_file, string slot_title)
        {
            m_owner = owner;
            m_callback = null;
            OpenInternal(source, src_file, slot_title);
        }

        /// <summary>通用打开（结果通过回调返回）</summary>
        public void Open(AudioClip source, Action<AudioClip, AudioClip, string> on_confirm, string src_file, string slot_title)
        {
            m_owner = null;
            m_callback = on_confirm;
            OpenInternal(source, src_file, slot_title);
        }

        private void OpenInternal(AudioClip source, string src_file, string slot_title)
        {
            EnsureBuilt();
            SetGalleryVisible(false);
            Show();      //先激活：后续 wave 刷新用协程，未激活的 GameObject 不能 StartCoroutine

            if (txt_title != null)
                txt_title.text = "音效DIY · " + (string.IsNullOrEmpty(slot_title) ? "音效" : slot_title);

            //参数每次打开回到默认（与"重置回到导入后原始状态"一致）
            m_volume = 1f;
            m_semitones = 0f;
            m_fade_ms = DEFAULT_FADE_MS;
            SyncParamControls();

            m_source_owned = false;
            if (source != null)
            {
                m_source = source;
                m_source_file = src_file;
                WaveSetClip(source, true);
                m_dirty = true;
                RebuildResult();
                SetStatus("已载入当前音效" + (string.IsNullOrEmpty(src_file) ? "" : "：" + src_file));
            }
            else
            {
                m_source = null;
                m_source_file = null;
                WaveSetClip(null, true);
                m_dirty = true;
                RebuildResult();
                SetStatus("请先导入素材：本地文件 或 从图库选择");
            }

            if (btn_confirm != null)
                btn_confirm.interactable = m_source != null;

            UIFonts.ApplyResolved(gameObject);
        }

        // ---------------- 结果加工 ----------------

        private void RebuildResult()
        {
            AudioClipDSP.DestroyClip(m_result);
            m_result = null;
            m_dirty = true;

            if (m_source != null && wave_view != null)
            {
                string err;
                m_result = AudioClipDSP.Process(m_source, wave_view.Start01, wave_view.End01,
                    m_semitones, m_volume, Mathf.RoundToInt(m_fade_ms), out err,
                    "diy_" + (string.IsNullOrEmpty(m_source_file) ? "audio" : m_source_file));
                if (m_result == null)
                    SetStatus("加工失败：" + err);
                else
                    m_dirty = false;
            }

            RebuildThumbnail();
            UpdateSelText();
            UpdateOutputText();
        }

        private void RebuildThumbnail()
        {
            if (thumb_image == null)
                return;
            float[] samples = m_result != null ? AudioClipDSP.GetInterleaved(m_result) : null;
            int ch = m_result != null ? m_result.channels : 0;
            int frames = m_result != null ? m_result.samples : 0;
            thumb_tex = AudioWaveformDrawer.Build(samples, ch, frames, THUMB_W, THUMB_H, 0f, 1f, thumb_tex);
            thumb_image.texture = thumb_tex;
        }

        private void UpdateSelText()
        {
            if (txt_sel == null || wave_view == null)
                return;
            if (wave_view.Clip == null)
            {
                txt_sel.text = "未载入素材";
                return;
            }
            float len = wave_view.LengthSeconds;
            float s = wave_view.StartSeconds;
            float e = wave_view.EndSeconds;
            txt_sel.text = string.Format("保留 {0:0.00}s ~ {1:0.00}s / 共 {2:0.00}s（选中 {3:0}%）",
                s, e, len, Mathf.RoundToInt(Mathf.Clamp01(wave_view.End01 - wave_view.Start01) * 100f));
            if (in_start != null && !in_start.isFocused)
                SetInputText(in_start, s.ToString("0.00"));
            if (in_end != null && !in_end.isFocused)
                SetInputText(in_end, e.ToString("0.00"));
        }

        private void UpdateOutputText()
        {
            if (txt_out == null)
                return;
            if (m_result == null)
            {
                txt_out.text = "加工结果：（空）";
                return;
            }
            string pitch_desc = Mathf.Abs(m_semitones) < 0.05f
                ? "原音高"
                : string.Format("{0}{1:0.#} 半音", m_semitones > 0f ? "+" : "", m_semitones);
            txt_out.text = string.Format("加工结果：{0:0.00}s · {1} · 音量 {2:0}% · 淡入淡出 {3:0}ms（重采样=变速变调）",
                m_result.length, pitch_desc, m_volume * 100f, m_fade_ms);
        }

        private void SetStatus(string msg)
        {
            if (txt_status != null)
                txt_status.text = msg;
        }

        // ---------------- 按钮 ----------------

        private void OnClickImportFile()
        {
            string path = AudioPicker.OpenLocalFile();
            if (string.IsNullOrEmpty(path))
            {
                SetStatus(FileBrowserBridge.IsSupported
                    ? "未选择文件"
                    : "当前平台不支持本地文件对话框，请使用「图库」");
                return;
            }
            SetStatus("正在解码：" + System.IO.Path.GetFileName(path) + " …");
            AudioPicker.LoadPath(path, (clip, err) =>
            {
                if (clip == null)
                {
                    SetStatus("导入失败：" + (err ?? "未知错误"));
                    return;
                }
                if (clip.length > AudioClipDSP.MaxImportSeconds)
                {
                    SetStatus(string.Format("音频过长（{0:0.0} 秒 > {1:0} 秒），未载入；请先裁剪好素材再导入",
                        clip.length, AudioClipDSP.MaxImportSeconds));
                    return;
                }
                SetSource(clip, System.IO.Path.GetFileName(path), true);
            });
        }

        private void OnClickGallery()
        {
            SetGalleryVisible(gallery_panel == null || !gallery_panel.gameObject.activeSelf);
        }

        private void OnClickPreview()
        {
            if (m_source == null)
            {
                SetStatus("请先导入素材");
                return;
            }
            RebuildResult();
            if (m_result == null)
                return;
            AudioTool.Get().PlaySFX(PREVIEW_CHANNEL, m_result, 1f, true);
            SetStatus("试听中…（参数改动后请重新点「试听」以听到最新效果）");
        }

        private void OnClickStop()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            SetStatus("已停止试听");
        }

        private void OnClickClear()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            if (m_owner != null)
                m_owner.ClearClip();
            else if (m_callback != null)
                m_callback(null, m_source, m_source_file);
            SetStatus("已清空该音效位");
            m_owner = null;
            m_callback = null;
            Hide();
        }

        public void OnClickConfirm()
        {
            if (m_source == null)
            {
                SetStatus("请先导入音频素材（本地文件或图库）");
                return;
            }
            RebuildResult();
            if (m_result == null)
            {
                SetStatus("加工失败，无法保存");
                return;
            }

            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);

            AudioClip result = m_result;             //所有权移交给宿主（保存到文件/回写卡牌）
            AudioClip source = m_source;
            string file = m_source_file;
            m_result = null;
            m_dirty = true;

            AudioClipEditorUI owner = m_owner;
            Action<AudioClip, AudioClip, string> cb = m_callback;
            m_owner = null;
            m_callback = null;

            Hide();
            if (owner != null)
                owner.ApplyResult(result, source, file);
            else if (cb != null)
                cb(result, source, file);
        }

        public void OnClickCancel()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            AudioClipDSP.DestroyClip(m_result);      //本次加工作废（宿主手里的旧结果不受影响）
            m_result = null;
            m_dirty = true;
            m_owner = null;
            m_callback = null;
            SetGalleryVisible(false);
            Hide();
        }

        public void OnClickReset()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            if (m_source == null)
            {
                SetStatus("请先导入素材");
                return;
            }
            WaveSetClip(m_source, true);             //回到"导入后原始全选状态"
            m_volume = 1f;
            m_semitones = 0f;
            m_fade_ms = DEFAULT_FADE_MS;
            SyncParamControls();
            RebuildResult();
            SetStatus("已重置：选区全选 + 默认参数");
        }

        // ---------------- 素材载入 ----------------

        private void SetSource(AudioClip clip, string src_file, bool owned)
        {
            if (clip == null)
                return;
            if (m_source_owned)
                AudioClipDSP.DestroyClip(m_source);  //替换掉上一次由本弹框解码的临时素材

            m_source = clip;
            m_source_file = src_file;
            m_source_owned = owned;

            WaveSetClip(clip, true);
            m_dirty = true;
            RebuildResult();
            if (btn_confirm != null)
                btn_confirm.interactable = true;
            SetStatus(string.Format("已载入素材：{0}（{1:0.00}s）",
                string.IsNullOrEmpty(src_file) ? clip.name : src_file, clip.length));
        }

        private void WaveSetClip(AudioClip clip, bool reset_selection)
        {
            if (wave_view == null)
                return;
            wave_view.SetClip(clip, reset_selection);
            //尺寸可能当帧才确定，下一帧重绘一次（避免首帧纹理尺寸为 0 被夹到 8px）
            if (isActiveAndEnabled)
                StartCoroutine(RefreshWaveNextFrame());
            else
                wave_view.RefreshView();
        }

        private System.Collections.IEnumerator RefreshWaveNextFrame()
        {
            yield return null;
            if (wave_view != null)
                wave_view.RefreshView();
        }

        // ---------------- 参数控件同步 ----------------

        private void SyncParamControls()
        {
            m_suppress = true;
            if (sl_volume != null)
                sl_volume.value = Mathf.Clamp(m_volume * 100f, sl_volume.minValue, sl_volume.maxValue);
            if (sl_pitch != null)
                sl_pitch.value = Mathf.Clamp(m_semitones, sl_pitch.minValue, sl_pitch.maxValue);
            if (sl_fade != null)
                sl_fade.value = Mathf.Clamp(m_fade_ms, sl_fade.minValue, sl_fade.maxValue);
            if (txt_volume != null)
                txt_volume.text = string.Format("{0:0}%", m_volume * 100f);
            if (txt_pitch != null)
                txt_pitch.text = string.Format("{0}{1:0.#}", m_semitones > 0f ? "+" : "", m_semitones);
            if (txt_fade != null)
                txt_fade.text = string.Format("{0:0}ms", m_fade_ms);
            m_suppress = false;
        }

        private void OnVolumeChanged(float v)
        {
            if (m_suppress)
                return;
            m_volume = v / 100f;
            if (txt_volume != null)
                txt_volume.text = string.Format("{0:0}%", v);
        }

        private void OnPitchChanged(float v)
        {
            if (m_suppress)
                return;
            m_semitones = v;
            if (txt_pitch != null)
                txt_pitch.text = string.Format("{0}{1:0.#}", v > 0f ? "+" : "", v);
        }

        private void OnFadeChanged(float v)
        {
            if (m_suppress)
                return;
            m_fade_ms = v;
            if (txt_fade != null)
                txt_fade.text = string.Format("{0:0}ms", v);
        }

        private void OnParamCommit()
        {
            if (m_suppress)
                return;
            RebuildResult();      //松手后才重加工（拖动过程只改数值文本）
        }

        private void OnSelectionCommitted()
        {
            RebuildResult();      //选区确定后刷新缩略预览与输出信息
        }

        private void OnSelectionChanged(float s, float e)
        {
            UpdateSelText();
        }

        private void OnStartInputEnd(string text)
        {
            if (m_suppress || wave_view == null || wave_view.Clip == null)
                return;
            float sec;
            if (!float.TryParse(text, out sec))
            {
                UpdateSelText();
                return;
            }
            wave_view.SetSelection(sec / Mathf.Max(wave_view.LengthSeconds, 0.0001f), wave_view.End01);
            OnSelectionCommitted();
        }

        private void OnEndInputEnd(string text)
        {
            if (m_suppress || wave_view == null || wave_view.Clip == null)
                return;
            float sec;
            if (!float.TryParse(text, out sec))
            {
                UpdateSelText();
                return;
            }
            wave_view.SetSelection(wave_view.Start01, sec / Mathf.Max(wave_view.LengthSeconds, 0.0001f));
            OnSelectionCommitted();
        }

        // ---------------- UI 构建 ----------------

        public void EnsureBuilt()
        {
            if (built)
                return;
            built = true;

            //全屏遮罩（点遮罩=取消本次编辑，与 ImageClip 一致）
            Image mask = UIFactory.CreateImage("Mask", transform, UITheme.MaskPopup);
            UIFactory.SetStretch(mask.rectTransform);
            mask.raycastTarget = true;              //CreateImage 默认吃射线；显式声明，保证能接点击
            Button mask_btn = mask.gameObject.AddComponent<Button>();
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(OnClickCancel);

            //主面板
            float panel_h = Mathf.Min(900f, Mathf.Max(620f, Screen.height - 80f));
            GameObject panel_go = new GameObject("Panel", typeof(RectTransform));
            panel_go.transform.SetParent(transform, false);
            panel_rect = panel_go.GetComponent<RectTransform>();
            panel_rect.anchorMin = new Vector2(0.5f, 0.5f);
            panel_rect.anchorMax = new Vector2(0.5f, 0.5f);
            panel_rect.pivot = new Vector2(0.5f, 0.5f);
            panel_rect.sizeDelta = new Vector2(PANEL_WIDTH, panel_h);
            Image panel_bg = panel_go.AddComponent<Image>();
            panel_bg.color = UITheme.BgPopup;

            float y = 14f;

            //标题
            RectTransform title_row = MakeStack("TitleRow", panel_rect, 34f, ref y);
            txt_title = MakeText("Title", title_row, "音效DIY", 24, UITheme.TextTitle, TextAlignmentOptions.Left);
            Stretch(txt_title.rectTransform, 18f, 52f, 0f, 0f);
            Button close_btn = MakeButton("Close", title_row, "×", new Color(1f, 1f, 1f, 0.14f), 20);
            Anchor(close_btn.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 1f, 1f, 34f, 30f, -12f, -2f);

            //波形区
            RectTransform wave_row = MakeStack("WaveRow", panel_rect, 196f, ref y);
            RectTransform wave_rt = UIFactory.CreateRect("WaveArea", wave_row);
            Stretch(wave_rt, 18f, 18f, 10f, 10f);
            Image wave_bg = wave_rt.gameObject.AddComponent<Image>();
            wave_bg.color = new Color(0.06f, 0.07f, 0.09f, 1f);
            wave_view = wave_rt.gameObject.AddComponent<AudioWaveformView>();
            wave_view.onSelectionChanged += OnSelectionChanged;
            wave_view.onSelectionCommitted += OnSelectionCommitted;

            //选区信息
            RectTransform sel_row = MakeStack("SelRow", panel_rect, 26f, ref y);
            txt_sel = MakeText("Sel", sel_row, "未载入素材", 16, UITheme.TextBody, TextAlignmentOptions.Left);
            Stretch(txt_sel.rectTransform, 18f, 18f, 0f, 0f);

            //选区数值输入
            RectTransform input_row = MakeStack("InputRow", panel_rect, 40f, ref y);
            TMP_Text lbl_s = MakeText("StartLabel", input_row, "起始(秒)", 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Anchor(lbl_s.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 70f, 26f, 18f, 0f);
            in_start = MakeNumberInput("StartInput", input_row, "0.00", OnStartInputEnd);
            Anchor(in_start.GetComponent<RectTransform>(), 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 96f, 32f, 92f, 0f);
            TMP_Text lbl_e = MakeText("EndLabel", input_row, "结束(秒)", 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Anchor(lbl_e.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 70f, 26f, 208f, 0f);
            in_end = MakeNumberInput("EndInput", input_row, "1.00", OnEndInputEnd);
            Anchor(in_end.GetComponent<RectTransform>(), 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 96f, 32f, 282f, 0f);

            //功能按钮条
            RectTransform btn_row = MakeStack("ButtonRow", panel_rect, 42f, ref y);
            MakeBarButton(btn_row, "导入文件", 18f, 118f, new Color(0.5f, 0.78f, 1f, 0.34f), OnClickImportFile);
            MakeBarButton(btn_row, "图库", 144f, 88f, new Color(0.6f, 0.72f, 1f, 0.34f), OnClickGallery);
            MakeBarButton(btn_row, "▶ 试听", 240f, 92f, new Color(0.4f, 0.85f, 0.6f, 0.36f), OnClickPreview);
            MakeBarButton(btn_row, "■ 停止", 340f, 92f, new Color(1f, 0.6f, 0.6f, 0.3f), OnClickStop);
            MakeBarButton(btn_row, "清空", 440f, 88f, new Color(1f, 0.75f, 0.4f, 0.26f), OnClickClear);

            //参数条
            RectTransform p1 = MakeStack("ParamVolume", panel_rect, 36f, ref y, 4f);
            sl_volume = MakeParamRow(p1, "音量", 0f, 200f, 100f, OnVolumeChanged, OnParamCommit, out txt_volume);
            RectTransform p2 = MakeStack("ParamPitch", panel_rect, 36f, ref y, 4f);
            sl_pitch = MakeParamRow(p2, "音高", -12f, 12f, 0f, OnPitchChanged, OnParamCommit, out txt_pitch);
            RectTransform p3 = MakeStack("ParamFade", panel_rect, 36f, ref y, 4f);
            sl_fade = MakeParamRow(p3, "淡入淡出", 0f, 200f, DEFAULT_FADE_MS, OnFadeChanged, OnParamCommit, out txt_fade);

            //缩略预览
            RectTransform thumb_row = MakeStack("ThumbRow", panel_rect, THUMB_H + 26f, ref y);
            txt_out = MakeText("OutInfo", thumb_row, "加工结果：（空）", 16, UITheme.TextBody, TextAlignmentOptions.Left);
            Anchor(txt_out.rectTransform, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 24f, 18f, -4f);
            RectTransform thumb_rt = UIFactory.CreateRect("Thumb", thumb_row);
            Anchor(thumb_rt, 0f, 0f, 0f, 0f, 0f, 0f, THUMB_W, THUMB_H, 18f, 4f);
            Image thumb_bg = thumb_rt.gameObject.AddComponent<Image>();
            thumb_bg.color = new Color(0.06f, 0.07f, 0.09f, 1f);
            GameObject thumb_wave = new GameObject("Wave", typeof(RectTransform));
            thumb_wave.transform.SetParent(thumb_rt, false);
            RectTransform tw_rt = thumb_wave.GetComponent<RectTransform>();
            UIFactory.SetStretch(tw_rt);
            thumb_image = thumb_wave.AddComponent<RawImage>();

            //底部
            RectTransform bottom_row = MakeStack("BottomRow", panel_rect, 46f, ref y, 0f);
            btn_confirm = MakeBarButton(bottom_row, "确定", 250f, 120f, new Color(0.45f, 0.85f, 0.5f, 0.45f), OnClickConfirm);
            MakeBarButton(bottom_row, "取消", 378f, 90f, new Color(1f, 1f, 1f, 0.16f), OnClickCancel);
            MakeBarButton(bottom_row, "重置", 18f, 100f, new Color(1f, 1f, 1f, 0.16f), OnClickReset);

            //状态文本（贴面板底部）
            txt_status = MakeText("Status", panel_rect, "", 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Anchor(txt_status.rectTransform, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 26f, 18f, 8f);

            EnsureGallery();

            UIFonts.ApplyResolved(gameObject);
        }

        private void EnsureGallery()
        {
            if (gallery_panel != null)
                return;
            gallery_panel = UIFactory.CreateRect("Gallery", panel_rect);
            UIFactory.SetStretch(gallery_panel);
            Image gbg = gallery_panel.gameObject.AddComponent<Image>();
            gbg.color = new Color(UITheme.BgPopup.r, UITheme.BgPopup.g, UITheme.BgPopup.b, 0.98f);

            TMP_Text gtitle = MakeText("Title", gallery_panel, "选择素材（点击载入）", 20, UITheme.TextTitle, TextAlignmentOptions.Left);
            Anchor(gtitle.rectTransform, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 30f, 18f, -12f);
            Button gclose = MakeButton("Close", gallery_panel, "×", new Color(1f, 1f, 1f, 0.14f), 20);
            Anchor(gclose.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 1f, 1f, 34f, 30f, -12f, -10f);

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
            gallery_list.sizeDelta = new Vector2(0f, 0f);
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

            List<AudioPickItem> workshop = AudioPicker.GetWorkshopLibrary();
            AddGallerySection("Workshop 音频（已导入）", workshop);
            List<AudioPickItem> resources = AudioPicker.GetResourceLibrary();
            AddGallerySection("项目 Resources 音频", resources);

            if (workshop.Count == 0 && resources.Count == 0)
                AddGalleryLabel("（暂无素材：请用「导入文件」导入本地音频）");
        }

        private void AddGallerySection(string title, List<AudioPickItem> items)
        {
            AddGalleryLabel("— " + title + "（" + items.Count + "）—");
            for (int i = 0; i < items.Count; i++)
                AddGalleryRow(items[i]);
        }

        private void AddGalleryLabel(string text)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(gallery_list, false);
            go.AddComponent<LayoutElement>().minHeight = 26f;
            TMP_Text t = MakeText("Text", go.transform, text, 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Stretch(t.rectTransform, 8f, 8f, 0f, 0f);
        }

        private void AddGalleryRow(AudioPickItem item)
        {
            GameObject go = new GameObject("Item", typeof(RectTransform));
            go.transform.SetParent(gallery_list, false);
            go.AddComponent<LayoutElement>().minHeight = 34f;
            Image bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.07f);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = bg;
            UITheme.ApplyButtonColors(b);
            TMP_Text t = MakeText("Text", go.transform, item.display, 16, UITheme.TextBody, TextAlignmentOptions.Left);
            Stretch(t.rectTransform, 12f, 12f, 0f, 0f);
            t.raycastTarget = false;

            AudioPickItem captured = item;
            b.onClick.AddListener(() =>
            {
                SetStatus("正在载入：" + captured.display + " …");
                AudioPicker.Load(captured, (clip, err) =>
                {
                    if (clip == null)
                    {
                        SetStatus("载入失败：" + (err ?? "未知错误"));
                        return;
                    }
                    bool owned = !captured.is_resource && string.IsNullOrEmpty(captured.fname);
                    SetSource(clip, captured.display, owned);
                    SetGalleryVisible(false);
                });
            });
        }

        // ---------------- 控件构造工具 ----------------

        private static RectTransform MakeStack(string name, Transform parent, float height, ref float y, float gap = 10f)
        {
            RectTransform rt = UIFactory.CreateRect(name, parent);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-36f, height);
            rt.anchoredPosition = new Vector2(0f, -y);
            y += height + gap;
            return rt;
        }

        /// <summary>四边内缩铺满（left/right/top/bottom 均为内缩像素）</summary>
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
            TMP_Text t = MakeText("Text", go.transform, label, size, Color.white, TextAlignmentOptions.Center);
            UIFactory.SetStretch(t.rectTransform);
            return b;
        }

        private Button MakeBarButton(Transform parent, string label, float x, float w, Color color, UnityEngine.Events.UnityAction action)
        {
            Button b = MakeButton("Bar_" + label, parent, label, color, 17);
            RectTransform rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w, 34f);
            rt.anchoredPosition = new Vector2(x, 0f);
            if (action != null)
                b.onClick.AddListener(action);
            return b;
        }

        private SliderDrag MakeParamRow(Transform parent, string label, float min, float max, float value,
            Action<float> on_changed, Action on_end, out TMP_Text value_text)
        {
            TMP_Text lbl = MakeText("Label", parent, label, 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Anchor(lbl.rectTransform, 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, 84f, 26f, 18f, 0f);

            GameObject slider_go = new GameObject("Slider", typeof(RectTransform));
            slider_go.transform.SetParent(parent, false);
            RectTransform sr = slider_go.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0.5f);      //左起 106px，右侧留 92px 给数值文本
            sr.anchorMax = new Vector2(1f, 0.5f);
            sr.offsetMin = new Vector2(106f, -9f);
            sr.offsetMax = new Vector2(-92f, 9f);

            Image bg = UIFactory.CreateImage("Background", slider_go.transform, new Color(1f, 1f, 1f, 0.16f));
            UIFactory.SetStretch(bg.rectTransform);

            RectTransform fill_area = UIFactory.CreateRect("Fill Area", slider_go.transform);
            fill_area.anchorMin = new Vector2(0f, 0.5f);
            fill_area.anchorMax = new Vector2(1f, 0.5f);
            fill_area.offsetMin = new Vector2(7f, -4f);
            fill_area.offsetMax = new Vector2(-7f, 4f);
            Image fill = UIFactory.CreateImage("Fill", fill_area, new Color(0.45f, 0.8f, 1f, 0.9f));
            UIFactory.SetStretch(fill.rectTransform);

            RectTransform handle_area = UIFactory.CreateRect("Handle Slide Area", slider_go.transform);
            handle_area.anchorMin = new Vector2(0f, 0f);
            handle_area.anchorMax = new Vector2(1f, 1f);
            handle_area.offsetMin = new Vector2(8f, 0f);
            handle_area.offsetMax = new Vector2(-8f, 0f);
            Image handle = UIFactory.CreateImage("Handle", handle_area, Color.white);
            handle.rectTransform.sizeDelta = new Vector2(14f, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);

            Slider s = slider_go.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = false;
            s.value = value;

            SliderDrag drag = slider_go.AddComponent<SliderDrag>();
            if (on_changed != null)
                drag.onValueChanged = () => on_changed(s.value);   //UnityAction ← 无参 lambda（不能直接赋 System.Action）
            if (on_end != null)
                drag.onEndDrag = () => on_end();

            value_text = MakeText("Value", parent, "", 15, UITheme.TextBody, TextAlignmentOptions.Right);
            Anchor(value_text.rectTransform, 1f, 0.5f, 1f, 0.5f, 1f, 0.5f, 76f, 26f, -18f, 0f);
            return drag;
        }

        private TMP_InputField MakeNumberInput(string name, Transform parent, string placeholder_text, Action<string> on_end)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            go.AddComponent<RectMask2D>();
            Image bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.18f);

            TMP_Text placeholder = MakeText("Placeholder", go.transform, placeholder_text, 15,
                new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.Left);
            Stretch(placeholder.rectTransform, 8f, 8f, 5f, 5f);

            TMP_Text display = MakeText("Text", go.transform, "", 15, Color.white, TextAlignmentOptions.Left);
            Stretch(display.rectTransform, 8f, 8f, 5f, 5f);

            TMP_InputField input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.textComponent = display;
            input.placeholder = placeholder;
            input.textViewport = rt;              //运行时手建 TMP_InputField 必须指定 viewport
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.onEndEdit.AddListener(t => { if (on_end != null) on_end(t); });
            return input;
        }

        private void SetInputText(TMP_InputField input, string text)
        {
            if (input != null)
                input.SetTextWithoutNotify(text);
        }

        private void OnDestroy()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            AudioClipDSP.DestroyClip(m_result);
            if (m_source_owned)
                AudioClipDSP.DestroyClip(m_source);
            if (thumb_tex != null)
            {
                Destroy(thumb_tex);
                thumb_tex = null;
            }
        }
    }
}
