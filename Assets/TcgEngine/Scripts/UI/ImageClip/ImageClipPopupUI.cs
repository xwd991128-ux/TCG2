using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace TcgEngine.UI
{
    /// <summary>视口事件转发：拖动/滚轮交给弹框处理（视口自身只是 RectTransform + Image + Mask）</summary>
    public class ImageClipDragProxy : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public ImageClipPopupUI popup;

        public void OnDrag(PointerEventData eventData)
        {
            if (popup != null)
                popup.HandleDrag(eventData);
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (popup != null)
                popup.HandleScroll(eventData);
        }
    }

    /// <summary>
    /// 卡图裁切弹框（复用 UIPanel 的淡入淡出；风格与「多选框」「富文本编辑」弹层一致，控件运行时自建）。
    ///
    /// 选型：<b>Mask + RawImage</b>（规格里的方案一）。理由：源图是刚解码出来的运行时 Texture2D，
    /// RawImage 直接吃 Texture、零额外 Sprite 分配，拖动/缩放只改 rectTransform，一次到位、最省。
    /// 若改成 Mask + Image(Sprite) 则每次换图都要多建一个 Sprite，且 Sprite 只用于显示，纯属浪费。
    ///
    /// 关键约定：
    /// - 视图即为所见即所得：遮罩内看到的就是成品；预览区按节流刷新（约 8 次/秒），避免每帧对整图做像素回读；
    /// - 缩放以视口中心为锚点（位置同步等比缩放），下限 = 「铺满遮罩」比例 —— 再小就会露白边；
    /// - 位置休眠 clamp：图片任意一边都不允许进入视口内部；
    /// - 上次的裁切位置/缩放记在 ImageClipState 上，重新打开即回到上次结果（且始终从原图裁，不会二次裁切）。
    /// </summary>
    public class ImageClipPopupUI : UIPanel
    {
        [Header("可选：场景预绑定（留空则运行时自动创建）")]
        public ImageClipEditorUI owner;

        [Header("文案")]
        public string title = "编辑卡图";

        // ---- 与其它弹层统一的配色 ----
        private static readonly Color MaskColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color PanelColor = new Color(0.12f, 0.12f, 0.15f, 1f);
        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color ViewportColor = new Color(0.06f, 0.07f, 0.10f, 1f);
        private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color HintColor = new Color(0.80f, 0.85f, 0.90f, 1f);
        private static readonly Color ErrorColor = new Color(1f, 0.55f, 0.55f, 1f);

        private const float PanelWidth = 520f;
        private const float PanelHeight = 760f;
        private const float ViewportHeight = 360f;                  // 视口高度固定，宽度按目标比例算
        private const float ViewportMinWidth = 140f;
        private const float ViewportMaxWidth = PanelWidth - 80f;
        private const float PreviewBoxHeight = 120f;

        /// <summary>当前遮罩视口尺寸（宽随目标图比例变化）</summary>
        private Vector2 m_viewport_size = new Vector2(360f, 360f);
        private const float PreviewInterval = 0.12f;   // 预览节流间隔（秒）
        private const int PreviewBaseMax = 384;        // 预览用缩略底图的长边上限（降低每次裁切的像素量）

        // ---- 运行期引用 ----
        private bool built;
        private RectTransform root_rect;
        private RectTransform panel_rect;
        private RectTransform viewport_rect;
        private RectTransform preview_box_rect;
        private RawImage m_raw;
        private Image m_preview;
        private TextMeshProUGUI m_hint;
        private RectTransform gallery_panel;
        private RectTransform gallery_list;

        // ---- 会话 ----
        private ImageClipState m_state;
        private Action<Sprite> m_callback;
        private Texture2D m_source;        // 指向 m_state.source
        private Texture2D m_base;          // 预览用缩略底图（本弹框持有）
        private bool m_base_owned;
        private Texture2D m_preview_tex;   // 预览贴图（复用，避免每次刷新都新建）
        private Sprite m_preview_sprite;

        private Vector2 m_pos;             // 图片中心相对视口中心的偏移（UI 单位）
        private float m_scale = 1f;        // 图片缩放（1 = 1 像素 1 UI 单位）
        private float m_fit_scale = 1f;    // 「铺满遮罩」的比例，也是缩放下限

        private bool m_preview_dirty;
        private float m_preview_next_time;
        private int m_input_lock_frame = -1;   // 文件对话框往返期间锁输入

        // ================= 生命周期 =================

        protected override void Awake()
        {
            base.Awake();
            EnsureBuilt();
        }

        protected override void Update()
        {
            base.Update();
            if (m_preview_dirty && Time.unscaledTime >= m_preview_next_time)
            {
                m_preview_dirty = false;
                m_preview_next_time = Time.unscaledTime + PreviewInterval;
                RefreshPreview();
            }
        }

        private void OnDestroy()
        {
            if (m_base_owned && m_base != null)
                Destroy(m_base);
            if (m_preview_sprite != null)
                Destroy(m_preview_sprite);
            if (m_preview_tex != null)
                Destroy(m_preview_tex);
            m_base = null;
            m_preview_sprite = null;
            m_preview_tex = null;
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = true;
                canvas_group.interactable = true;
            }
        }

        public override void Hide(bool instant = false)
        {
            HideSubPanels();
            base.Hide(instant);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }
        }

        // ================= 对外 API =================

        /// <summary>运行时创建弹框：优先挂到 context 所在 Canvas 下（占满屏幕）</summary>
        public static ImageClipPopupUI Create(Transform context)
        {
            Transform parent = context;
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>(true) : null;
            if (canvas != null)
                parent = canvas.transform;
            if (parent == null)
                return null;

            GameObject go = new GameObject("ImageClipPopup", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            return go.AddComponent<ImageClipPopupUI>();
        }

        /// <summary>
        /// 打开：载入当前图（display 可空＝首次为空），state 保存跨次打开的原始图与裁切位置。
        /// target_size 为目标显示区域的宽高（例如卡面图 8.56×8.36、面板图 250×350），决定裁切比例。
        /// </summary>
        public void Open(ImageClipState state, Sprite display, ImageClipEditorUI editor, Vector2 target_size)
        {
            // 先走通用入口（会把 owner 清空），再记录回写目标，避免 ApplyResult 被调用两次
            Open(state, display, editor != null ? (Action<Sprite>)editor.ApplyResult : null, target_size);
            owner = editor;
        }

        /// <summary>正方形视口（默认）</summary>
        public void Open(ImageClipState state, Sprite display, ImageClipEditorUI editor)
        {
            Open(state, display, editor, new Vector2(1f, 1f));
        }

        /// <summary>通用打开入口：确认时把裁好的 Sprite 交给 on_confirm</summary>
        public void Open(ImageClipState state, Sprite display, Action<Sprite> on_confirm, Vector2 target_size)
        {
            EnsureBuilt();
            SetViewportAspect(target_size);
            if (state == null)
                state = new ImageClipState();
            m_state = state;
            m_callback = on_confirm;
            owner = null;   // 通用入口不写回展示框

            HideSubPanels();
            Show();
            UIFonts.ApplyResolved(gameObject);

            string hint = null;
            bool is_error = false;

            // 源图优先复用上次会话的原始图：这样"重新打开"是从原图恢复上次裁切，不会被二次裁切
            Texture2D src = m_state.source;
            if (src == null && display != null)
            {
                Texture2D t;
                string err;
                if (ImagePicker.TryLoadTextureFromSprite(display, out t, out err))
                {
                    m_state.source = t;
                    src = t;
                }
                else
                {
                    hint = err;
                    is_error = true;
                }
            }

            if (src != null)
            {
                ApplySource(src, m_state.has_state);
                if (hint == null)
                    hint = m_state.has_state ? "已回到上次的裁切位置，可继续调整" : "已载入图片：拖动移动、滚轮缩放";
            }
            else
            {
                ClearView();
                if (hint == null)
                    hint = "点击「从文件导入」或「从图库选择」载入图片";
            }

            SetHint(hint, is_error);
        }

        /// <summary>通用打开入口（正方形视口）</summary>
        public void Open(ImageClipState state, Sprite display, Action<Sprite> on_confirm)
        {
            Open(state, display, on_confirm, new Vector2(1f, 1f));
        }

        /// <summary>
        /// 按目标显示区域宽高设置视口比例（高度固定、宽度换算），并同步重排视口与预览框。
        /// 卡面图与面板图的显示区域比例不同，必须分开传，否则裁出来的比例和游戏里对不上。
        /// </summary>
        public void SetViewportAspect(Vector2 target_size)
        {
            float aw = target_size.x > 0f ? target_size.x : 1f;
            float ah = target_size.y > 0f ? target_size.y : 1f;
            float w = Mathf.Clamp(ViewportHeight * (aw / ah), ViewportMinWidth, ViewportMaxWidth);
            m_viewport_size = new Vector2(Mathf.Round(w), ViewportHeight);
            ApplyViewportLayout();
        }

        /// <summary>视口 / 预览框按当前比例重新定位（水平居中，垂直位置不变）</summary>
        private void ApplyViewportLayout()
        {
            float left = (PanelWidth - m_viewport_size.x) * 0.5f;
            if (viewport_rect != null)
                SetTopStretch(viewport_rect, left, 56f, left, m_viewport_size.y);

            float box_w = Mathf.Max(m_viewport_size.x, 240f);
            float box_left = (PanelWidth - box_w) * 0.5f;
            if (preview_box_rect != null)
                SetTopStretch(preview_box_rect, box_left, 56f + m_viewport_size.y + 112f, box_left, PreviewBoxHeight);
        }

        /// <summary>确定：按当前视口裁出成品并回调</summary>
        public void OnClickConfirm()
        {
            if (m_source == null)
            {
                SetHint("请先导入一张图片", true);
                return;
            }

            Sprite result = ImageClipCrop.CropToSprite(m_source, GetPixelRect());
            if (result == null)
            {
                SetHint("裁切失败（图片像素读取异常）", true);
                return;
            }

            // 记录裁切状态，供"重新打开回到上次结果"
            if (m_state != null)
            {
                m_state.pos = m_pos;
                m_state.scale = m_scale;
                m_state.has_state = true;
            }

            Action<Sprite> callback = m_callback;
            ImageClipEditorUI ow = owner;
            m_callback = null;
            owner = null;

            Hide();
            if (ow != null)
                ow.ApplyResult(result);
            if (callback != null)
                callback(result);
        }

        /// <summary>取消：丢弃本次拖动/缩放（原图与上次结果保持不变）</summary>
        public void OnClickCancel()
        {
            owner = null;
            m_callback = null;
            Hide();
        }

        // ================= 交互：拖动 / 缩放 =================

        /// <summary>视口内拖动：按屏幕位移移动图片，并做休眠 clamp（不露白）</summary>
        public void HandleDrag(PointerEventData eventData)
        {
            if (m_source == null)
                return;
            if (Time.frameCount <= m_input_lock_frame)
                return;   // 文件对话框往返的同一帧不响应拖拽，防止误操作

            float scale_factor = 1f;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.0001f)
                scale_factor = canvas.scaleFactor;

            m_pos += eventData.delta / scale_factor;
            ApplyTransform();
            m_preview_dirty = true;
        }

        /// <summary>滚轮缩放（以视口中心为锚点）</summary>
        public void HandleScroll(PointerEventData eventData)
        {
            if (m_source == null)
                return;
            Zoom(Mathf.Pow(1.1f, eventData.scrollDelta.y));
        }

        /// <summary>±按钮缩放</summary>
        public void OnClickZoomIn()
        {
            Zoom(1.25f);
        }

        public void OnClickZoomOut()
        {
            Zoom(0.8f);
        }

        /// <summary>重置：回到「铺满遮罩、居中」的初始状态</summary>
        public void OnClickReset()
        {
            if (m_source == null)
                return;
            m_scale = m_fit_scale;
            m_pos = Vector2.zero;
            ApplyTransform();
            m_preview_dirty = true;
            SetHint("已重置位置与缩放", false);
        }

        private void Zoom(float k)
        {
            if (m_source == null || Mathf.Approximately(k, 1f))
                return;

            float prev = m_scale;
            float next = ImageClipCrop.ClampScale(m_scale * k, m_fit_scale);
            if (Mathf.Approximately(prev, next))
                return;

            // 以视口中心为锚点：位置同步等比缩放，中心处对应的图像内容保持不变
            m_pos *= next / prev;
            m_scale = next;
            ApplyTransform();
            m_preview_dirty = true;
        }

        /// <summary>把当前 pos/scale 落到 RawImage 上（含 clamp）</summary>
        private void ApplyTransform()
        {
            if (m_source == null || m_raw == null)
                return;

            m_scale = ImageClipCrop.ClampScale(m_scale, m_fit_scale);
            Vector2 size = new Vector2(m_source.width, m_source.height) * m_scale;
            m_pos = ImageClipCrop.ClampImagePosition(size, m_pos, m_viewport_size);

            m_raw.texture = m_source;
            m_raw.rectTransform.sizeDelta = size;
            m_raw.rectTransform.anchoredPosition = m_pos;
        }

        // ================= 导入 =================

        /// <summary>《从文件导入》：PC 原生对话框 → 解码为可读纹理</summary>
        public void OnClickImportFile()
        {
            if (!FileBrowserBridge.IsSupported)
            {
                SetHint("当前平台不支持本地文件导入，请用「从图库选择」", true);
                return;
            }

            SetHint("正在打开文件对话框…", false);
            m_input_lock_frame = Time.frameCount + 1;

            string path = FileBrowserBridge.OpenImageFile("选择卡牌图片");
            if (string.IsNullOrEmpty(path))
            {
                SetHint("已取消选择文件", false);
                return;
            }

            Texture2D tex;
            string err;
            if (!ImagePicker.TryLoadTextureFromFile(path, out tex, out err))
            {
                SetHint(err, true);
                return;
            }

            ReplaceSource(tex);
            SetHint("已导入：" + Path.GetFileName(path), false);
        }

        /// <summary>《从图库选择》：列出工程现有卡图</summary>
        public void OnClickImportGallery()
        {
            bool open = gallery_panel != null && !gallery_panel.gameObject.activeSelf;
            SetSubPanelVisible(gallery_panel, open);
            if (open)
                RebuildGallery();
        }

        private void OnPickGallery(ImagePicker.GalleryItem item)
        {
            if (item == null)
                return;
            Texture2D tex;
            string err;
            if (!ImagePicker.TryLoadTextureFromSprite(item.sprite, out tex, out err))
            {
                SetHint(err, true);
                return;
            }
            HideSubPanels();
            ReplaceSource(tex);
            SetHint("已选择：" + item.name, false);
        }

        /// <summary>换源图：释放上一张（本弹框负责其生命周期），并重置裁切位置</summary>
        private void ReplaceSource(Texture2D tex)
        {
            if (m_state != null && m_state.source != null && m_state.source != tex)
                Destroy(m_state.source);
            if (m_state != null)
                m_state.source = tex;
            ApplySource(tex, false);
        }

        /// <summary>套用源图：重算铺满比例、按需恢复上次位置、刷新视口与预览</summary>
        private void ApplySource(Texture2D tex, bool keep_state)
        {
            m_source = tex;
            BuildPreviewBase();

            if (tex == null)
            {
                ClearView();
                return;
            }

            m_fit_scale = ImageClipCrop.FitScale(new Vector2(tex.width, tex.height), m_viewport_size);
            bool restore = keep_state && m_state != null && m_state.has_state;
            m_scale = restore ? m_state.scale : m_fit_scale;
            m_pos = restore ? m_state.pos : Vector2.zero;

            if (m_raw != null)
            {
                m_raw.uvRect = new Rect(0f, 0f, 1f, 1f);
                m_raw.enabled = true;
            }
            ApplyTransform();
            m_preview_dirty = true;
            m_preview_next_time = 0f;   // 立即刷新一次
        }

        private void ClearView()
        {
            m_source = null;
            m_scale = 1f;
            m_fit_scale = 1f;
            m_pos = Vector2.zero;
            m_preview_dirty = false;
            if (m_raw != null)
            {
                m_raw.texture = null;
                m_raw.enabled = false;
            }
            if (m_preview != null)
                m_preview.enabled = false;
        }

        // ================= 预览 =================

        /// <summary>视口矩形 → 原图像素矩形</summary>
        private Rect GetPixelRect()
        {
            if (m_source == null)
                return new Rect(0f, 0f, 1f, 1f);
            Vector2 img_size = new Vector2(m_source.width, m_source.height) * m_scale;
            return ImageClipCrop.ViewportToPixelRect(img_size, m_pos, m_viewport_size, m_source.width, m_source.height);
        }

        /// <summary>预览用缩略底图：长边限制到 PreviewBaseMax，降低每次裁切的像素量</summary>
        private void BuildPreviewBase()
        {
            if (m_base_owned && m_base != null)
                Destroy(m_base);
            m_base = null;
            m_base_owned = false;

            if (m_source == null)
                return;

            int sw = m_source.width;
            int sh = m_source.height;
            float k = Mathf.Min(1f, (float)PreviewBaseMax / Mathf.Max(sw, sh));
            int bw = Mathf.Max(1, Mathf.RoundToInt(sw * k));
            int bh = Mathf.Max(1, Mathf.RoundToInt(sh * k));

            if (bw >= sw && bh >= sh)
            {
                m_base = m_source;   // 原图本身不大，直接用
                return;
            }

            RenderTexture rt = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(bw, bh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(m_source, rt);
                RenderTexture.active = rt;

                Texture2D t = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                t.ReadPixels(new Rect(0f, 0f, bw, bh), 0, 0);
                t.Apply();
                m_base = t;
                m_base_owned = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("卡图裁切：预览底图生成失败，改用原图：" + e.Message);
                m_base = m_source;
                m_base_owned = false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
            }
        }

        private void RefreshPreview()
        {
            if (m_preview == null)
                return;
            if (m_source == null || m_base == null)
            {
                m_preview.enabled = false;
                return;
            }

            Rect pr = GetPixelRect();
            float sx = m_base.width / (float)m_source.width;
            float sy = m_base.height / (float)m_source.height;
            Rect br = ImageClipCrop.ClampPixelRect(new Rect(pr.x * sx, pr.y * sy, pr.width * sx, pr.height * sy),
                m_base.width, m_base.height);

            int w = Mathf.Clamp(Mathf.RoundToInt(br.width), 1, m_base.width);
            int h = Mathf.Clamp(Mathf.RoundToInt(br.height), 1, m_base.height);
            int x = Mathf.Clamp(Mathf.RoundToInt(br.x), 0, m_base.width - w);
            int y = Mathf.Clamp(Mathf.RoundToInt(br.y), 0, m_base.height - h);

            Color[] px = m_base.GetPixels(x, y, w, h);
            if (px == null || px.Length < w * h)
                return;

            if (m_preview_sprite == null || m_preview_tex == null
                || m_preview_tex.width != w || m_preview_tex.height != h)
            {
                if (m_preview_sprite != null)
                    Destroy(m_preview_sprite);
                if (m_preview_tex != null)
                    Destroy(m_preview_tex);
                m_preview_tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                m_preview_tex.wrapMode = TextureWrapMode.Clamp;
                m_preview_tex.filterMode = FilterMode.Bilinear;
                m_preview_sprite = Sprite.Create(m_preview_tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f),
                    ImageClipCrop.SpritePPU, 0, SpriteMeshType.FullRect);
            }

            m_preview_tex.SetPixels(px);
            m_preview_tex.Apply();
            m_preview.sprite = m_preview_sprite;
            m_preview.enabled = true;
        }

        // ================= 提示 =================

        private void SetHint(string msg, bool is_error)
        {
            if (m_hint == null)
                return;
            m_hint.text = msg ?? "";
            m_hint.color = is_error ? ErrorColor : HintColor;
        }

        // ================= 界面构建 =================

        private void EnsureBuilt()
        {
            if (built)
                return;
            built = true;

            if (root_rect == null)
                root_rect = GetComponent<RectTransform>();
            if (canvas_group == null)
                canvas_group = GetComponent<CanvasGroup>();
            if (root_rect != null)
                Stretch(root_rect, 0f);
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }

            Image mask = GetComponent<Image>();
            if (mask == null)
                mask = gameObject.AddComponent<Image>();
            mask.color = MaskColor;
            Button mask_btn = GetComponent<Button>();
            if (mask_btn == null)
                mask_btn = gameObject.AddComponent<Button>();
            mask_btn.targetGraphic = mask;
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(OnClickCancel);

            BuildPanel();
        }

        private void BuildPanel()
        {
            panel_rect = MakeRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            Image panel_img = AddImage(panel_rect, PanelColor);
            AddClickBlocker(panel_rect, panel_img);

            MakeTextTop("Title", panel_rect, title, 24, TextAlignmentOptions.MidlineLeft, Color.white, 24f, 10f, 74f, 40f);
            Button close = MakeButton("Close", panel_rect, "×", 24, OnClickCancel);
            SetTopRight(close.GetComponent<RectTransform>(), 12f, 10f, 40f, 40f);

            RectTransform line = MakeRect("Divider", panel_rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(PanelWidth - 48f, 2f));
            SetTopStretch(line, 24f, 54f, 24f, 2f);
            AddImage(line, DividerColor);

            BuildViewport();
            BuildToolbar();
            BuildPreview();
            BuildGalleryPanel();
            ApplyViewportLayout();   // 视口/预览框按当前比例对齐（后续 Open 时还会再按目标比例重排一次）

            // 确定 / 取消
            Button confirm = MakeButton("BtnConfirm", panel_rect, "确定", 20, OnClickConfirm);
            SetBottomRight(confirm.GetComponent<RectTransform>(), 24f, 16f, 130f, 46f);
            Button cancel = MakeButton("BtnCancel", panel_rect, "取消", 20, OnClickCancel);
            SetBottomRight(cancel.GetComponent<RectTransform>(), 170f, 16f, 130f, 46f);

            HideSubPanels();
        }

        /// <summary>遮罩视口：Image(背景) + Mask(裁剪) + 子 RawImage(可拖动缩放) + 事件转发</summary>
        private void BuildViewport()
        {
            RectTransform viewport = MakeRect("Viewport", panel_rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, m_viewport_size);
            viewport_rect = viewport;
            SetTopStretch(viewport, (PanelWidth - m_viewport_size.x) * 0.5f, 56f, (PanelWidth - m_viewport_size.x) * 0.5f, m_viewport_size.y);

            Image bg = AddImage(viewport, ViewportColor);
            bg.raycastTarget = true;
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;   // 只做裁剪，不显示遮罩图本身

            RectTransform clip = MakeRect("ClipImage", viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, m_viewport_size);
            m_raw = clip.gameObject.AddComponent<RawImage>();
            m_raw.color = Color.white;
            m_raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            m_raw.enabled = false;

            ImageClipDragProxy proxy = viewport.gameObject.AddComponent<ImageClipDragProxy>();
            proxy.popup = this;

            RectTransform frame = MakeRect("Frame", viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            Stretch(frame, 0f);
            Image frame_img = AddImage(frame, new Color(1f, 1f, 1f, 0.01f));
            frame_img.raycastTarget = false;
            Outline outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.55f, 0.65f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            // 提示行（载入状态 / 错误信息）
            m_hint = MakeTextTop("Hint", panel_rect, "", 18, TextAlignmentOptions.Center, HintColor, 16f, 420f, 16f, 24f);
        }

        /// <summary>工具栏：从文件导入 / 从图库选择 / + / - / 重置</summary>
        private void BuildToolbar()
        {
            RectTransform bar = MakeRect("Toolbar", panel_rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(PanelWidth - 48f, 42f));
            SetTopStretch(bar, 24f, 452f, 24f, 42f);

            float x = 0f;
            x = MakeToolButton(bar, "BtnImportFile", "从文件导入", 140f, x, OnClickImportFile);
            x = MakeToolButton(bar, "BtnImportGallery", "从图库选择", 140f, x, OnClickImportGallery);
            x = MakeToolButton(bar, "BtnZoomIn", "+", 44f, x, OnClickZoomIn);
            x = MakeToolButton(bar, "BtnZoomOut", "-", 44f, x, OnClickZoomOut);
            x = MakeToolButton(bar, "BtnReset", "重置", 72f, x, OnClickReset);
        }

        private float MakeToolButton(RectTransform bar, string name, string label, float width, float x, Action onClick)
        {
            Button btn = MakeButton(name, bar, label, 19, onClick);
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, 42f);
            return x + width + 8f;
        }

        /// <summary>预览区：遮罩内成品的实时缩略</summary>
        private void BuildPreview()
        {
            MakeTextTop("PreviewLabel", panel_rect, "实时预览（遮罩内的成品）", 18, TextAlignmentOptions.MidlineLeft,
                HintColor, 24f, 502f, 24f, 24f);

            RectTransform box = MakeRect("PreviewBox", panel_rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, Vector2.zero);
            preview_box_rect = box;
            SetTopStretch(box, (PanelWidth - m_viewport_size.x) * 0.5f, 528f, (PanelWidth - m_viewport_size.x) * 0.5f, PreviewBoxHeight);
            AddImage(box, new Color(0f, 0f, 0f, 0.25f));

            RectTransform inner = MakeRect("PreviewImage", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            Stretch(inner, 6f);
            m_preview = inner.gameObject.AddComponent<Image>();
            m_preview.preserveAspect = true;   // 预览按成品真实宽高比显示，不做拉伸
            m_preview.raycastTarget = false;
            m_preview.enabled = false;
        }

        /// <summary>图库选择面板（多选框风格的滚动列表）</summary>
        private void BuildGalleryPanel()
        {
            gallery_panel = MakeRect("GalleryPanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 30f), new Vector2(500f, 500f));
            Image bg = AddImage(gallery_panel, PanelColor);
            AddClickBlocker(gallery_panel, bg);

            MakeTextTop("Title", gallery_panel, "从图库选择（工程内卡图）", 22, TextAlignmentOptions.MidlineLeft,
                Color.white, 20f, 8f, 64f, 36f);
            Button close = MakeButton("Close", gallery_panel, "×", 22, HideSubPanels);
            SetTopRight(close.GetComponent<RectTransform>(), 10f, 8f, 36f, 36f);

            RectTransform scroll_rt = MakeRect("Scroll", gallery_panel, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(scroll_rt, 0f);
            scroll_rt.offsetMin = new Vector2(12f, 12f);
            scroll_rt.offsetMax = new Vector2(-12f, -50f);
            ScrollRect scroll = scroll_rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            RectTransform view = MakeRect("Viewport", scroll_rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Stretch(view, 0f);
            view.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = view;

            RectTransform content = MakeRect("Content", view, new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 0f));
            content.anchorMax = new Vector2(1f, 1f);
            VerticalLayoutGroup vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            ContentSizeFitter csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            gallery_list = content;

            gallery_panel.gameObject.SetActive(false);
        }

        private void RebuildGallery()
        {
            if (gallery_list == null)
                return;

            for (int i = gallery_list.childCount - 1; i >= 0; i--)
            {
                Transform c = gallery_list.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            List<ImagePicker.GalleryItem> items = ImagePicker.GetGallery();
            if (items.Count == 0)
            {
                MakeGalleryRowName("当前工程没有可用的卡图，请先用「从文件导入」");
                return;
            }

            for (int i = 0; i < items.Count; i++)
                MakeGalleryRow(items[i]);
        }

        private void MakeGalleryRow(ImagePicker.GalleryItem item)
        {
            GameObject go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(gallery_list, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 52f;
            le.minHeight = 52f;

            Image row_bg = go.GetComponent<Image>();
            row_bg.color = new Color(1f, 1f, 1f, 0.06f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = row_bg;
            btn.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.fadeDuration = 0.05f;
            btn.colors = cb;

            RectTransform thumb_rt = MakeRect("Thumb", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(40f, 40f));
            Image thumb = thumb_rt.gameObject.AddComponent<Image>();
            thumb.sprite = item.sprite;
            thumb.preserveAspect = true;
            thumb.raycastTarget = false;

            TextMeshProUGUI label = MakeText("Name", rt, item.name, 17, TextAlignmentOptions.MidlineLeft, Color.white);
            Stretch(label.rectTransform, 0f);
            label.rectTransform.offsetMin = new Vector2(56f, 0f);
            label.rectTransform.offsetMax = new Vector2(-8f, 0f);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            ImagePicker.GalleryItem captured = item;
            btn.onClick.AddListener(() => OnPickGallery(captured));
        }

        private void MakeGalleryRowName(string text)
        {
            GameObject go = new GameObject("Empty", typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(gallery_list, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.minHeight = 40f;
            TextMeshProUGUI t = MakeText("Text", rt, text, 17, TextAlignmentOptions.Center, HintColor);
            Stretch(t.rectTransform, 4f);
        }

        private void SetSubPanelVisible(RectTransform panel, bool visible)
        {
            if (gallery_panel != null)
                gallery_panel.gameObject.SetActive(visible && panel == gallery_panel);
            if (visible && panel != null)
                panel.SetAsLastSibling();
        }

        private void HideSubPanels()
        {
            if (gallery_panel != null)
                gallery_panel.gameObject.SetActive(false);
        }

        // ================= UI 构建辅助 =================

        private TextMeshProUGUI MakeText(string name, Transform parent, string content, int size, TextAlignmentOptions align, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt, 0f);

            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.richText = false;
            t.raycastTarget = false;
            return t;
        }

        private TextMeshProUGUI MakeTextTop(string name, RectTransform parent, string content, int size, TextAlignmentOptions align,
            Color color, float left, float top, float right, float height)
        {
            TextMeshProUGUI t = MakeText(name, parent, content, size, align, color);
            SetTopStretch(t.rectTransform, left, top, right, height);
            return t;
        }

        private Button MakeButton(string name, Transform parent, string label, int font_size, Action onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 40f);

            Image img = go.GetComponent<Image>();
            img.color = ButtonColor;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.05f;
            btn.colors = cb;

            TextMeshProUGUI t = MakeText("Text", rt, label, font_size, TextAlignmentOptions.Center, Color.white);
            Stretch(t.rectTransform, 4f);
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static void AddClickBlocker(RectTransform rt, Image img)
        {
            Button btn = rt.gameObject.GetComponent<Button>();
            if (btn == null)
                btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
        }

        private static Image AddImage(RectTransform rt, Color color)
        {
            Image img = rt.gameObject.GetComponent<Image>();
            if (img == null)
                img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static RectTransform MakeRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static void Stretch(RectTransform rt, float margin)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }

        private static void SetTopStretch(RectTransform rt, float left, float top, float right, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-(left + right), height);
            rt.anchoredPosition = new Vector2((left - right) * 0.5f, -top);
        }

        private static void SetTopRight(RectTransform rt, float right, float top, float width, float height)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-right, -top);
        }

        private static void SetBottomRight(RectTransform rt, float right, float bottom, float width, float height)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-right, bottom);
        }
    }
}
