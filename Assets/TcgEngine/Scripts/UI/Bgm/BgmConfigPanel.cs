using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Audio;

namespace TcgEngine.UI
{
    /// <summary>
    /// 界面 BGM 配置面板（游戏内）：逐界面设置「默认兜底 / 指定曲目 / 不播 BGM」+ 曲目 + 音量，并可设置全局默认兜底曲。
    ///
    /// 改动即时写入 `Workshop/scene_bgm.json`（运行时覆盖），立即生效；「恢复出厂配置」删掉覆盖回到资产配置。
    /// 未设置时：默认兜底 → 全局默认曲 → 音乐库第一首（保证一导入素材就有声音）。
    /// </summary>
    public class BgmConfigPanel : UIPanel
    {
        private const string PREVIEW_CHANNEL = "bgm_preview";
        private const float PANEL_W = 900f;
        private const float PANEL_H = 640f;

        private RectTransform panel_rect;
        private RectTransform list_content;
        private TMP_Text txt_status, txt_default;
        private bool built;

        private static BgmConfigPanel instance;

        public static BgmConfigPanel Get()
        {
            if (instance == null)
                instance = FindObjectOfType<BgmConfigPanel>(true);
            return instance;
        }

        public static BgmConfigPanel Create(Transform context)
        {
            if (instance != null)
                return instance;
            GameObject go = new GameObject("BgmConfigPanel", typeof(RectTransform), typeof(CanvasGroup));
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            go.transform.SetParent(canvas != null ? canvas.transform : context, false);
            UIFactory.SetStretch(go.GetComponent<RectTransform>());
            go.transform.SetAsLastSibling();
            instance = go.AddComponent<BgmConfigPanel>();
            instance.EnsureBuilt();
            instance.Hide(true);
            return instance;
        }

        public static void Open(Transform host)
        {
            BgmConfigPanel panel = Get();
            if (panel == null)
                panel = Create(host);
            panel.Show();
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
            BgmLibrary.Ensure();
            SceneBgmConfig.Get().EnsureDefaultEntries();
            RebuildList();
            RefreshDefaultText();
            SetStatus(SceneBgmConfig.HasOverride
                ? "当前使用运行时配置（Workshop/scene_bgm.json），改动即时保存并生效"
                : "当前使用出厂配置；任何改动会写入运行时配置并立即生效");
        }

        public override void Hide(bool instant = false)
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }
            base.Hide(instant);
        }

        private void OnDestroy()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
        }

        // ---------------- 列表 ----------------

        private void RebuildList()
        {
            if (list_content == null)
                return;
            for (int i = list_content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(list_content.GetChild(i).gameObject);

            for (int i = 0; i < BgmKeys.All.Length; i++)
                MakeRow(BgmKeys.All[i]);
        }

        private void MakeRow(string key)
        {
            SceneBgmConfig cfg = SceneBgmConfig.Get();
            SceneBgmConfig.Entry e = cfg.GetOrAdd(key);

            GameObject row = new GameObject("Row_" + key, typeof(RectTransform));
            row.transform.SetParent(list_content, false);
            row.AddComponent<LayoutElement>().minHeight = 44f;
            Image bg = row.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.06f);

            //界面名
            TMP_Text name = MakeText("Name", row.transform, BgmKeys.DisplayName(key), 17, UITheme.TextBody, TextAlignmentOptions.Left);
            Anchor(name.rectTransform, 0f, 0f, 0f, 1f, 0f, 0.5f, 120f, 0f, 14f, 0f);

            //策略（点击循环切换：默认兜底 → 指定曲目 → 不播 BGM）
            string mode_name = BgmModes.DisplayName(e.mode);
            Color mode_color = e.IsMute ? new Color(1f, 0.6f, 0.6f, 0.3f)
                : (e.IsPick ? new Color(0.5f, 0.78f, 1f, 0.34f) : new Color(1f, 1f, 1f, 0.16f));
            MakeRowButton(row.transform, "Mode", mode_name, 140f, 130f, mode_color, () =>
            {
                //循环切换策略
                string next = e.UseDefault ? BgmModes.Pick : (e.IsPick ? BgmModes.Mute : BgmModes.Default);
                e.mode = next;
                SaveAndApply();
                RebuildList();
            });

            //曲目名
            string bgm_title;
            if (e.IsMute)
                bgm_title = "（不播放）";
            else if (e.IsPick)
            {
                BgmEntry pe = BgmLibrary.Find(e.bgm_id);
                bgm_title = pe != null ? pe.title : ("缺失: " + (string.IsNullOrEmpty(e.bgm_id) ? "未选择" : e.bgm_id));
            }
            else
            {
                BgmEntry de = ResolveDefaultEntry();
                bgm_title = "默认兜底 → " + (de != null ? de.title : "（音乐库为空）");
            }
            TMP_Text clip_txt = MakeText("Clip", row.transform, bgm_title, 16, UITheme.TextDim, TextAlignmentOptions.Left);
            Anchor(clip_txt.rectTransform, 0f, 0f, 0f, 1f, 0f, 0.5f, 250f, 0f, 282f, 0f);
            clip_txt.enableWordWrapping = false;
            clip_txt.overflowMode = TextOverflowModes.Ellipsis;

            //选择曲目（仅"指定曲目"策略可用）
            MakeRowButton(row.transform, "Pick", "选择曲目", 540f, 96f,
                e.IsPick ? new Color(0.6f, 0.72f, 1f, 0.32f) : new Color(1f, 1f, 1f, 0.10f), () =>
            {
                if (!e.IsPick)
                {
                    e.mode = BgmModes.Pick;
                    SaveAndApply();
                }
                OpenClipPicker(e);
            });

            //试听
            MakeRowButton(row.transform, "Preview", "试听", 642f, 64f, new Color(0.4f, 0.85f, 0.6f, 0.32f), () =>
            {
                BgmEntry pe = e.IsPick ? BgmLibrary.Find(e.bgm_id) : ResolveDefaultEntry();
                if (pe == null)
                {
                    SetStatus("没有可试听的曲目（先到「音乐库」导入，或把音频放进 Resources/BGM/）");
                    return;
                }
                PreviewEntry(pe, e.volume);
            });

            //音量 -
            MakeRowButton(row.transform, "VolDown", "-", 712f, 34f, new Color(1f, 1f, 1f, 0.14f), () =>
            {
                e.volume = Mathf.Clamp01(e.volume - 0.1f);
                SaveAndApply();
                RebuildList();
            });
            TMP_Text vol = MakeText("Vol", row.transform, Mathf.RoundToInt(e.volume * 100f) + "%", 15, UITheme.TextBody, TextAlignmentOptions.Center);
            Anchor(vol.rectTransform, 0f, 0f, 0f, 1f, 0f, 0.5f, 48f, 0f, 750f, 0f);
            MakeRowButton(row.transform, "VolUp", "+", 802f, 34f, new Color(1f, 1f, 1f, 0.14f), () =>
            {
                e.volume = Mathf.Clamp01(e.volume + 0.1f);
                SaveAndApply();
                RebuildList();
            });
        }

        private BgmEntry ResolveDefaultEntry()
        {
            SceneBgmConfig cfg = SceneBgmConfig.Get();
            BgmEntry e = BgmLibrary.Find(cfg.default_bgm_id);
            return e != null ? e : BgmLibrary.GetDefault();   //库里第一首
        }

        private void OpenClipPicker(SceneBgmConfig.Entry e)
        {
            List<BgmEntry> all = BgmLibrary.GetAll();
            if (all.Count == 0)
            {
                SetStatus("音乐库为空：请先在「音乐库」导入本地音频（或把音频放进 Resources/BGM/）");
                return;
            }
            List<string> options = new List<string>();
            List<string> values = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                options.Add(all[i].title + "（" + BgmLibrary.SourceLabel(all[i]) + "）");
                values.Add(all[i].id);
            }
            string current = e.bgm_id;
            UISelectPopup.OpenSingle(transform, "为「" + BgmKeys.DisplayName(e.scene_key) + "」选择 BGM",
                options, values, current, picked =>
                {
                    e.mode = BgmModes.Pick;
                    e.bgm_id = picked;
                    SaveAndApply();
                    RebuildList();
                    SetStatus("已设置 " + BgmKeys.DisplayName(e.scene_key) + " → " + picked);
                });
        }

        private void PreviewEntry(BgmEntry e, float volume)
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            BgmLibrary.LoadClip(e, (clip, err) =>
            {
                if (clip == null)
                {
                    SetStatus("试听失败：" + err);
                    return;
                }
                AudioTool.Get().PlaySFX(PREVIEW_CHANNEL, clip, Mathf.Max(volume, 0.05f), true);
                SetStatus("试听中：" + e.title + "（" + BgmLibrary.SourceLabel(e) + "）");
            });
        }

        private void SaveAndApply()
        {
            string err;
            SceneBgmConfig cfg = SceneBgmConfig.Get();
            if (!cfg.SaveOverride(out err))
            {
                SetStatus("保存失败：" + err);
                return;
            }
            //立即生效：对当前所在界面强制重放一次（只在已进入过界面时才有意义）
            if (!string.IsNullOrEmpty(BgmManager.CurrentKey))
                BgmManager.PlayFor(BgmManager.CurrentKey, true);
        }

        private void RefreshDefaultText()
        {
            if (txt_default == null)
                return;
            BgmEntry d = ResolveDefaultEntry();
            txt_default.text = "全局默认兜底：" + (d != null ? d.title + "（" + BgmLibrary.SourceLabel(d) + "）" : "（音乐库为空）");
        }

        private void SetStatus(string msg)
        {
            if (txt_status != null)
                txt_status.text = msg;
        }

        // ---------------- 顶部操作 ----------------

        private void OnClickPickDefault()
        {
            List<BgmEntry> all = BgmLibrary.GetAll();
            if (all.Count == 0)
            {
                SetStatus("音乐库为空：请先在「音乐库」导入本地音频");
                return;
            }
            List<string> options = new List<string> { "（自动：音乐库第一首）" };
            List<string> values = new List<string> { "" };
            for (int i = 0; i < all.Count; i++)
            {
                options.Add(all[i].title + "（" + BgmLibrary.SourceLabel(all[i]) + "）");
                values.Add(all[i].id);
            }
            SceneBgmConfig cfg = SceneBgmConfig.Get();
            UISelectPopup.OpenSingle(transform, "选择全局默认兜底 BGM", options, values, cfg.default_bgm_id, picked =>
            {
                cfg.default_bgm_id = picked;
                SaveAndApply();
                RefreshDefaultText();
                RebuildList();
                SetStatus(string.IsNullOrEmpty(picked) ? "默认兜底已设为：自动（音乐库第一首）" : "默认兜底已设为：" + picked);
            });
        }

        private void OnClickPreviewDefault()
        {
            BgmEntry d = ResolveDefaultEntry();
            if (d == null)
            {
                SetStatus("音乐库为空，无法试听");
                return;
            }
            PreviewEntry(d, 0.6f);
        }

        private void OnClickStopPreview()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
            SetStatus("已停止试听");
        }

        private void OnClickRestoreFactory()
        {
            string err;
            SceneBgmConfig.ClearOverride(out err);
            if (!string.IsNullOrEmpty(err))
            {
                SetStatus("恢复出厂失败：" + err);
                return;
            }
            SceneBgmConfig cfg = SceneBgmConfig.Get();
            cfg.EnsureDefaultEntries();
            RebuildList();
            RefreshDefaultText();
            SetStatus("已恢复出厂配置（运行时配置已删除）");
        }

        private void OnClickOpenLibrary()
        {
            MusicLibraryPanel.Open(transform);
        }

        // ---------------- UI 构建 ----------------

        private void EnsureBuilt()
        {
            if (built)
                return;
            built = true;

            Image mask = UIFactory.CreateImage("Mask", transform, UITheme.MaskPage);
            UIFactory.SetStretch(mask.rectTransform);
            mask.raycastTarget = true;
            Button mask_btn = mask.gameObject.AddComponent<Button>();
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(() => Hide());

            GameObject panel_go = new GameObject("Panel", typeof(RectTransform));
            panel_go.transform.SetParent(transform, false);
            panel_rect = panel_go.GetComponent<RectTransform>();
            panel_rect.anchorMin = new Vector2(0.5f, 0.5f);
            panel_rect.anchorMax = new Vector2(0.5f, 0.5f);
            panel_rect.pivot = new Vector2(0.5f, 0.5f);
            panel_rect.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            Image panel_bg = panel_go.AddComponent<Image>();
            panel_bg.color = UITheme.BgPopup;

            TMP_Text title = MakeText("Title", panel_rect, "界面 BGM 配置", 26, UITheme.TextTitle, TextAlignmentOptions.Left);
            Anchor(title.rectTransform, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 36f, 22f, -14f);
            Button close = MakeButton("Close", panel_rect, "×", new Color(1f, 1f, 1f, 0.14f), 22);
            Anchor(close.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 1f, 1f, 38f, 34f, -16f, -12f);
            close.onClick.AddListener(() => Hide());

            //第二行：默认兜底 + 操作
            MakeTopButton("PickDefault", "设置默认兜底", 22f, 150f, new Color(1f, 0.85f, 0.45f, 0.32f), OnClickPickDefault);
            MakeTopButton("PreviewDefault", "试听默认", 180f, 110f, new Color(0.4f, 0.85f, 0.6f, 0.3f), OnClickPreviewDefault);
            MakeTopButton("StopPreview", "停止", 298f, 76f, new Color(1f, 0.6f, 0.6f, 0.26f), OnClickStopPreview);
            MakeTopButton("Library", "音乐库", 382f, 96f, new Color(0.6f, 0.72f, 1f, 0.3f), OnClickOpenLibrary);
            MakeTopButton("RestoreFactory", "恢复出厂配置", 486f, 150f, new Color(1f, 1f, 1f, 0.14f), OnClickRestoreFactory);

            txt_default = MakeText("DefaultInfo", panel_rect, "", 15, UITheme.TextDim, TextAlignmentOptions.Right);
            Anchor(txt_default.rectTransform, 0f, 1f, 1f, 1f, 1f, 1f, -300f, 26f, -22f, -62f);

            //列表
            GameObject scroll_go = new GameObject("Scroll", typeof(RectTransform));
            scroll_go.transform.SetParent(panel_rect, false);
            RectTransform scroll_rt = scroll_go.GetComponent<RectTransform>();
            Stretch(scroll_rt, 18f, 18f, 108f, 56f);
            Image sgbg = scroll_go.AddComponent<Image>();
            sgbg.color = new Color(1f, 1f, 1f, 0.05f);
            scroll_go.AddComponent<RectMask2D>();
            ScrollRect sr = scroll_go.AddComponent<ScrollRect>();

            GameObject content_go = new GameObject("Content", typeof(RectTransform));
            content_go.transform.SetParent(scroll_go.transform, false);
            list_content = content_go.GetComponent<RectTransform>();
            list_content.anchorMin = new Vector2(0f, 1f);
            list_content.anchorMax = new Vector2(1f, 1f);
            list_content.pivot = new Vector2(0.5f, 1f);
            list_content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup vlg = content_go.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            ContentSizeFitter fitter = content_go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = scroll_rt;
            sr.content = list_content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            txt_status = MakeText("Status", panel_rect, "", 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Anchor(txt_status.rectTransform, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 34f, 22f, 14f);

            UIFonts.ApplyResolved(gameObject);
        }

        private void MakeTopButton(string name, string label, float x, float w, Color color, UnityEngine.Events.UnityAction action)
        {
            Button b = MakeButton(name, panel_rect, label, color, 16);
            Anchor(b.GetComponent<RectTransform>(), 0f, 1f, 0f, 1f, 0f, 1f, w, 34f, x, -58f);
            if (action != null)
                b.onClick.AddListener(action);
        }

        private void MakeRowButton(Transform parent, string name, string label, float x, float w, Color color, UnityEngine.Events.UnityAction action)
        {
            Button b = MakeButton(name, parent, label, color, 15);
            Anchor(b.GetComponent<RectTransform>(), 0f, 0.5f, 0f, 0.5f, 0f, 0.5f, w, 30f, x, 0f);
            if (action != null)
                b.onClick.AddListener(action);
        }

        // ---------------- 控件工具 ----------------

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
    }
}
