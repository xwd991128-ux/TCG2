using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Audio;

namespace TcgEngine.UI
{
    /// <summary>
    /// 音乐库面板（主菜单入口）：查看已导入 BGM、导入本地音频、试听、重命名、删除、设为默认兜底曲。
    ///
    /// 说明：
    /// ① 继承 UIPanel，复用项目弹层范式（运行时自建 UI + 遮罩，参照 ImageClipPopupUI / 音效DIY）；
    /// ② 素材来源分两类：官方（Resources/BGM，只读，标注「官方」）与导入（Workshop/Audio + bgm_library.json，标注「导入」）；
    /// ③ 导入复用「音效编辑器」那套本地文件能力（AudioPicker → FileBrowserBridge），不重复造轮子；
    /// ④ 试听走独立通道 "bgm_preview"，不打断正在播放的 BGM（BgmManager 的双通道）。
    /// </summary>
    public class MusicLibraryPanel : UIPanel
    {
        private const string PREVIEW_CHANNEL = "bgm_preview";
        private const float PANEL_W = 860f;
        private const float PANEL_H = 620f;

        private RectTransform panel_rect;
        private RectTransform list_content;
        private TMP_Text txt_status, txt_default;
        private bool built;

        private RectTransform rename_popup;
        private TMP_InputField rename_input;
        private BgmEntry rename_target;

        private static MusicLibraryPanel instance;

        public static MusicLibraryPanel Get()
        {
            if (instance == null)
                instance = FindObjectOfType<MusicLibraryPanel>(true);
            return instance;
        }

        /// <summary>创建到指定 Canvas 下（已存在则直接返回）</summary>
        public static MusicLibraryPanel Create(Transform context)
        {
            if (instance != null)
                return instance;
            GameObject go = new GameObject("MusicLibraryPanel", typeof(RectTransform), typeof(CanvasGroup));
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            go.transform.SetParent(canvas != null ? canvas.transform : context, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            UIFactory.SetStretch(rt);
            go.transform.SetAsLastSibling();
            instance = go.AddComponent<MusicLibraryPanel>();
            instance.EnsureBuilt();
            instance.Hide(true);
            return instance;
        }

        /// <summary>打开面板（不存在则创建；host 为挂载点，通常是主菜单的某个面板）</summary>
        public static void Open(Transform host)
        {
            MusicLibraryPanel panel = Get();
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
            BgmLibrary.Reload();     //每次打开重扫官方 BGM + 读导入清单
            RebuildList();
            RefreshDefaultText();
        }

        public override void Hide(bool instant = false)
        {
            StopPreview();
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }
            base.Hide(instant);
        }

        private void OnDestroy()
        {
            StopPreview();
        }

        // ---------------- 列表 ----------------

        private void RebuildList()
        {
            if (list_content == null)
                return;
            for (int i = list_content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(list_content.GetChild(i).gameObject);

            List<BgmEntry> list = BgmLibrary.GetAll();
            if (list.Count == 0)
            {
                MakeRowLabel("（音乐库为空：点左上「导入本地音频」导入 mp3/ogg/wav，" +
                    "或把音频放进任意 Resources/BGM/ 目录作为内置 BGM）");
                return;
            }
            for (int i = 0; i < list.Count; i++)
                MakeRow(list[i]);
        }

        private void MakeRow(BgmEntry e)
        {
            GameObject row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(list_content, false);
            row.AddComponent<LayoutElement>().minHeight = 42f;
            Image bg = row.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.06f);

            TMP_Text title = MakeText("Title", row.transform, e.title, 17, UITheme.TextBody, TextAlignmentOptions.Left);
            Anchor(title.rectTransform, 0f, 0f, 0f, 1f, 0f, 0.5f, 380f, 0f, 14f, 0f);
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;

            Color tag_color = e.official ? new Color(0.5f, 0.78f, 1f, 1f) : new Color(0.55f, 0.9f, 0.6f, 1f);
            TMP_Text tag = MakeText("Source", row.transform, "[" + BgmLibrary.SourceLabel(e) + "]", 15, tag_color, TextAlignmentOptions.Left);
            Anchor(tag.rectTransform, 0f, 0f, 0f, 1f, 0f, 0.5f, 76f, 0f, 400f, 0f);

            MakeRowButton(row.transform, "试听", 496f, 62f, new Color(0.4f, 0.85f, 0.6f, 0.34f), () => Preview(e));
            MakeRowButton(row.transform, "设默认", 564f, 76f, new Color(1f, 0.85f, 0.45f, 0.3f), () =>
            {
                BgmLibrary.SetDefault(e);
                RefreshDefaultText();
                SetStatus("默认兜底 BGM 已设为：" + e.title);
                RebuildList();
            });
            if (!e.official)
            {
                MakeRowButton(row.transform, "重命名", 646f, 84f, new Color(1f, 1f, 1f, 0.16f), () => OpenRename(e));
                MakeRowButton(row.transform, "删除", 736f, 62f, new Color(1f, 0.6f, 0.6f, 0.28f), () => DeleteEntry(e));
            }
            else
            {
                TMP_Text hint = MakeText("Hint", row.transform, "内置（只读）", 14, UITheme.TextDim, TextAlignmentOptions.Right);
                Anchor(hint.rectTransform, 0f, 0f, 1f, 1f, 1f, 0.5f, 160f, 0f, -16f, 0f);
            }
        }

        private void MakeRowLabel(string text)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(list_content, false);
            go.AddComponent<LayoutElement>().minHeight = 34f;
            TMP_Text t = MakeText("Text", go.transform, text, 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Stretch(t.rectTransform, 12f, 12f, 0f, 0f);
        }

        private void MakeRowButton(Transform parent, string label, float x, float w, Color color, UnityEngine.Events.UnityAction action)
        {
            GameObject go = new GameObject("Btn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w, 30f);
            rt.anchoredPosition = new Vector2(x, 0f);
            Image img = go.AddComponent<Image>();
            img.color = color;
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            UITheme.ApplyButtonColors(b);
            if (action != null)
                b.onClick.AddListener(action);
            TMP_Text t = MakeText("Text", go.transform, label, 15, Color.white, TextAlignmentOptions.Center);
            UIFactory.SetStretch(t.rectTransform);
        }

        // ---------------- 功能 ----------------

        private void OnClickImport()
        {
            string path = AudioPicker.OpenLocalFile();
            if (string.IsNullOrEmpty(path))
            {
                SetStatus(FileBrowserBridge.IsSupported ? "未选择文件" : "当前平台不支持本地文件对话框");
                return;
            }
            string title = System.IO.Path.GetFileNameWithoutExtension(path);
            string err;
            BgmEntry e = BgmLibrary.Import(path, title, out err);
            if (e == null)
            {
                SetStatus("导入失败：" + err);
                return;
            }
            SetStatus("已导入：" + e.title + "（点「试听」确认，或「重命名」改显示名）");
            RebuildList();
        }

        private void Preview(BgmEntry e)
        {
            StopPreview();
            BgmLibrary.LoadClip(e, (clip, err) =>
            {
                if (clip == null)
                {
                    SetStatus("试听失败：" + err);
                    return;
                }
                AudioTool.Get().PlaySFX(PREVIEW_CHANNEL, clip, BgmLibrary.VolumeOf(e, 0.6f), true);
                SetStatus("试听中：" + e.title + "（" + BgmLibrary.SourceLabel(e) + "）");
            });
        }

        private void StopPreview()
        {
            AudioTool.Get().StopSFX(PREVIEW_CHANNEL);
        }

        private void DeleteEntry(BgmEntry e)
        {
            if (e == null)
                return;
            string err;
            if (!BgmLibrary.Delete(e, out err))
            {
                SetStatus("删除失败：" + err);
                return;
            }
            SetStatus("已删除：" + e.title);
            RebuildList();
            RefreshDefaultText();
        }

        private void RefreshDefaultText()
        {
            if (txt_default == null)
                return;
            BgmEntry d = BgmLibrary.Find(BgmLibrary.Data.default_bgm);
            txt_default.text = "默认兜底：" + (d != null ? d.title : "（未设置，未配置的界面将保持当前音乐）");
        }

        private void SetStatus(string msg)
        {
            if (txt_status != null)
                txt_status.text = msg;
        }

        // ---------------- 重命名弹层 ----------------

        private void OpenRename(BgmEntry e)
        {
            rename_target = e;
            if (rename_popup == null)
                return;
            rename_popup.gameObject.SetActive(true);
            rename_popup.SetAsLastSibling();
            if (rename_input != null)
            {
                rename_input.SetTextWithoutNotify(e != null ? e.title : "");
                rename_input.ActivateInputField();
            }
        }

        private void CloseRename()
        {
            rename_target = null;
            if (rename_popup != null)
                rename_popup.gameObject.SetActive(false);
        }

        private void ConfirmRename()
        {
            if (rename_target == null || rename_input == null)
            {
                CloseRename();
                return;
            }
            string name = rename_input.text;
            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
            {
                SetStatus("名称不能为空");
                return;
            }
            BgmLibrary.Rename(rename_target, name);
            SetStatus("已重命名为：" + rename_target.title);
            CloseRename();
            RebuildList();
            RefreshDefaultText();
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

            //标题
            TMP_Text title = MakeText("Title", panel_rect, "音乐库（BGM 素材）", 26, UITheme.TextTitle, TextAlignmentOptions.Left);
            Anchor(title.rectTransform, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 36f, 22f, -14f);
            Button close = MakeButton("Close", panel_rect, "×", new Color(1f, 1f, 1f, 0.14f), 22);
            Anchor(close.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 1f, 1f, 38f, 34f, -16f, -12f);
            close.onClick.AddListener(() => Hide());

            //工具条
            Button import = MakeButton("Import", panel_rect, "导入本地音频", new Color(0.5f, 0.78f, 1f, 0.36f), 17);
            Anchor(import.GetComponent<RectTransform>(), 0f, 1f, 0f, 1f, 0f, 1f, 168f, 34f, 22f, -58f);
            import.onClick.AddListener(OnClickImport);

            Button stop = MakeButton("StopPreview", panel_rect, "停止试听", new Color(1f, 0.6f, 0.6f, 0.26f), 17);
            Anchor(stop.GetComponent<RectTransform>(), 0f, 1f, 0f, 1f, 0f, 1f, 118f, 34f, 200f, -58f);
            stop.onClick.AddListener(() => { StopPreview(); SetStatus("已停止试听"); });

            //快捷入口：逐界面配置 BGM（策略/曲目/音量）
            Button config = MakeButton("OpenConfig", panel_rect, "界面BGM配置", new Color(1f, 0.85f, 0.45f, 0.32f), 17);
            Anchor(config.GetComponent<RectTransform>(), 0f, 1f, 0f, 1f, 0f, 1f, 148f, 34f, 326f, -58f);
            config.onClick.AddListener(() => BgmConfigPanel.Open(transform));

            txt_default = MakeText("DefaultInfo", panel_rect, "", 15, UITheme.TextDim, TextAlignmentOptions.Right);
            Anchor(txt_default.rectTransform, 0f, 1f, 1f, 1f, 1f, 1f, -180f, 26f, -22f, -62f);

            //列表（ScrollRect + VerticalLayoutGroup + ContentSizeFitter）
            GameObject scroll_go = new GameObject("Scroll", typeof(RectTransform));
            scroll_go.transform.SetParent(panel_rect, false);
            RectTransform scroll_rt = scroll_go.GetComponent<RectTransform>();
            Stretch(scroll_rt, 18f, 18f, 104f, 52f);
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

            //状态栏
            txt_status = MakeText("Status", panel_rect, "", 15, UITheme.TextDim, TextAlignmentOptions.Left);
            Anchor(txt_status.rectTransform, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 30f, 22f, 12f);

            EnsureRenamePopup();

            UIFonts.ApplyResolved(gameObject);
        }

        private void EnsureRenamePopup()
        {
            GameObject go = new GameObject("RenamePopup", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            rename_popup = go.GetComponent<RectTransform>();
            UIFactory.SetStretch(rename_popup);

            Image mask = UIFactory.CreateImage("Mask", rename_popup, UITheme.MaskPopup);
            UIFactory.SetStretch(mask.rectTransform);
            mask.raycastTarget = true;
            Button mask_btn = mask.gameObject.AddComponent<Button>();
            mask_btn.transition = Selectable.Transition.None;
            //★ 改名弹框：点空白处**不关闭**（正在输入的曲名不应被丢弃），只认「确定 / 取消 / ×」
            mask_btn.onClick.AddListener(() => { });

            GameObject panel_go = new GameObject("Panel", typeof(RectTransform));
            panel_go.transform.SetParent(rename_popup, false);
            RectTransform prt = panel_go.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(420f, 190f);
            Image pbg = panel_go.AddComponent<Image>();
            pbg.color = UITheme.BgPopup;

            TMP_Text t = MakeText("Title", prt, "重命名 BGM", 22, UITheme.TextTitle, TextAlignmentOptions.Left);
            Anchor(t.rectTransform, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 30f, 20f, -14f);

            GameObject input_go = new GameObject("Input", typeof(RectTransform));
            input_go.transform.SetParent(prt, false);
            RectTransform irt = input_go.GetComponent<RectTransform>();
            Anchor(irt, 0f, 0.5f, 1f, 0.5f, 0.5f, 0.5f, -40f, 40f, 0f, 6f);
            input_go.AddComponent<RectMask2D>();
            Image ibg = input_go.AddComponent<Image>();
            ibg.color = new Color(1f, 1f, 1f, 0.18f);

            TMP_Text ph = MakeText("Placeholder", input_go.transform, "新名称", 16, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.Left);
            Stretch(ph.rectTransform, 10f, 10f, 6f, 6f);
            TMP_Text tx = MakeText("Text", input_go.transform, "", 16, Color.white, TextAlignmentOptions.Left);
            Stretch(tx.rectTransform, 10f, 10f, 6f, 6f);

            rename_input = input_go.AddComponent<TMP_InputField>();
            rename_input.targetGraphic = ibg;
            rename_input.textComponent = tx;
            rename_input.placeholder = ph;
            rename_input.textViewport = irt;
            rename_input.onSubmit.AddListener(_ => ConfirmRename());

            Button ok = MakeButton("OK", prt, "确定", new Color(0.45f, 0.85f, 0.5f, 0.45f), 17);
            Anchor(ok.GetComponent<RectTransform>(), 0.5f, 0f, 0.5f, 0f, 0.5f, 0f, 120f, 36f, -66f, 18f);
            ok.onClick.AddListener(ConfirmRename);
            Button cancel = MakeButton("Cancel", prt, "取消", new Color(1f, 1f, 1f, 0.16f), 17);
            Anchor(cancel.GetComponent<RectTransform>(), 0.5f, 0f, 0.5f, 0f, 0.5f, 0f, 120f, 36f, 66f, 18f);
            cancel.onClick.AddListener(CloseRename);

            rename_popup.gameObject.SetActive(false);
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
