using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 可复用波形交互组件：显示波形 + 两个可拖动的选区手柄（左=保留起点，右=保留终点）。
    /// 只负责"选区"，不负责截取/播放；选区变化通过 onSelectionChanged(归一化起, 归一化止) 抛出。
    ///
    /// 用法：
    ///   RectTransform rt = UIFactory.CreateRect("Wave", parent);
    ///   AudioWaveformView view = rt.gameObject.AddComponent&lt;AudioWaveformView&gt;();
    ///   view.SetClip(clip);
    ///   view.onSelectionChanged += (s, e) =&gt; {...};
    /// </summary>
    public class AudioWaveformView : MonoBehaviour, IPointerDownHandler
    {
        /// <summary>选区变化（归一化 0..1，起 &lt;= 止）</summary>
        public event Action<float, float> onSelectionChanged;

        /// <summary>手柄拖动结束（用于"拖动时省电、松手后重算预览"的场景）</summary>
        public event Action onSelectionCommitted;

        private const float HANDLE_WIDTH = 10f;
        private const float MIN_GAP = 0.002f;       //两手柄最小间隔（归一化），防止零长度选区

        private RawImage wave_image;
        private RectTransform wave_rect;
        private Image handle_left;
        private Image handle_right;

        private Texture2D wave_tex;
        private float[] samples;                    //交错采样（只在换素材时取一次）
        private int sample_channels;
        private int sample_frames;

        private float start01 = 0f;
        private float end01 = 1f;
        private bool built;

        public AudioClip Clip { get; private set; }
        public float Start01 { get { return start01; } }
        public float End01 { get { return end01; } }
        public float StartSeconds { get { return Clip != null ? start01 * Clip.length : 0f; } }
        public float EndSeconds { get { return Clip != null ? end01 * Clip.length : 0f; } }
        public float LengthSeconds { get { return Clip != null ? Clip.length : 0f; } }
        public bool HasClip { get { return Clip != null; } }

        private void EnsureBuilt()
        {
            if (built)
                return;
            built = true;

            EnsureRect();

            GameObject wave_go = new GameObject("Wave", typeof(RectTransform));
            wave_go.transform.SetParent(transform, false);
            wave_rect = wave_go.GetComponent<RectTransform>();
            UIFactory.SetStretch(wave_rect);
            wave_image = wave_go.AddComponent<RawImage>();
            wave_image.color = Color.white;
            wave_image.raycastTarget = true;        //点空白处把最近的手柄挪过来

            handle_left = MakeHandle("HandleL", new Color(1f, 0.84f, 0.4f, 0.95f), true);
            handle_right = MakeHandle("HandleR", new Color(1f, 0.84f, 0.4f, 0.95f), false);
        }

        private void EnsureRect()
        {
            if (GetComponent<RectTransform>() == null)
                gameObject.AddComponent<RectTransform>();
        }

        private Image MakeHandle(string name, Color color, bool left)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(HANDLE_WIDTH, 0f);
            rt.anchoredPosition = new Vector2(0f, 0f);

            AudioWaveformHandleDrag drag = go.AddComponent<AudioWaveformHandleDrag>();
            drag.Bind(this, left);
            return img;
        }

        /// <summary>载入素材：默认全选。reset_selection=false 时保留当前选区的归一化比例（换素材后仍可沿用）</summary>
        public void SetClip(AudioClip clip, bool reset_selection = true)
        {
            EnsureBuilt();
            Clip = clip;
            samples = AudioClipDSP.GetInterleaved(clip);
            sample_channels = clip != null ? clip.channels : 0;
            sample_frames = clip != null ? clip.samples : 0;

            if (reset_selection)
            {
                start01 = 0f;
                end01 = 1f;
            }
            RefreshView();
            Notify(false);
        }

        /// <summary>设置选区（归一化，自动夹紧到 [0,1] 且保证 start &lt;= end）</summary>
        public void SetSelection(float s, float e, bool notify = true)
        {
            EnsureBuilt();
            s = Mathf.Clamp01(s);
            e = Mathf.Clamp01(e);
            if (e < s)
            {
                float tmp = s;
                s = e;
                e = tmp;
            }
            if (e - s < MIN_GAP)
                e = Mathf.Min(1f, s + MIN_GAP);
            start01 = s;
            end01 = e;
            RefreshView();
            Notify(notify);
        }

        /// <summary>重绘波形与手柄位置（不触发事件）</summary>
        public void RefreshView()
        {
            EnsureBuilt();
            wave_tex = AudioWaveformDrawer.Build(samples, sample_channels, sample_frames,
                Mathf.RoundToInt(Mathf.Max(wave_rect.rect.width, 8f)),
                Mathf.RoundToInt(Mathf.Max(wave_rect.rect.height, 8f)),
                start01, end01, wave_tex);
            wave_image.texture = wave_tex;
            PositionHandles();
        }

        private void PositionHandles()
        {
            float w = wave_rect.rect.width;
            if (handle_left != null)
                handle_left.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(0f, w, start01), 0f);
            if (handle_right != null)
                handle_right.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(0f, w, end01), 0f);
        }

        // ---------------- 交互 ----------------

        /// <summary>点击波形空白：把最近的手柄移过去（拖动条太小的替代操作）</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            float t = ScreenTo01(eventData);
            if (t < 0f)
                return;
            if (Mathf.Abs(t - start01) <= Mathf.Abs(t - end01))
                SetSelection(t, end01);
            else
                SetSelection(start01, t);
            onSelectionCommitted?.Invoke();
        }

        internal void DragHandle(bool left, PointerEventData eventData)
        {
            float t = ScreenTo01(eventData);
            if (t < 0f)
                return;
            if (left)
                SetSelection(Mathf.Min(t, end01 - MIN_GAP), end01, false);
            else
                SetSelection(start01, Mathf.Max(t, start01 + MIN_GAP), false);
            Notify(false);
        }

        internal void CommitHandle()
        {
            onSelectionCommitted?.Invoke();
        }

        private float ScreenTo01(PointerEventData eventData)
        {
            EnsureBuilt();
            RectTransform rt = GetComponent<RectTransform>();
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position,
                    eventData.pressEventCamera, out local))
                return -1f;
            Rect r = rt.rect;
            if (r.width <= 0.001f)
                return -1f;
            return Mathf.Clamp01((local.x - r.xMin) / r.width);
        }

        private void Notify(bool committed)
        {
            onSelectionChanged?.Invoke(start01, end01);
            if (committed)
                onSelectionCommitted?.Invoke();
        }

        /// <summary>选区像素宽度（供手柄命中区/调试）</summary>
        public void OnRectTransformDimensionsChange()
        {
            if (built && wave_rect != null && wave_rect.rect.width > 1f)
                RefreshView();      //尺寸变化（面板缩放）后重画，避免拉伸模糊
        }

        private void OnDestroy()
        {
            if (wave_tex != null)
            {
                UnityEngine.Object.Destroy(wave_tex);   //显式限定：文件顶部有 using System，Object 会歧义
                wave_tex = null;
            }
        }
    }

    /// <summary>波形手柄拖动（挂在两个手柄上，转发给 AudioWaveformView）</summary>
    public class AudioWaveformHandleDrag : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        private AudioWaveformView view;
        private bool left;

        public void Bind(AudioWaveformView v, bool is_left)
        {
            view = v;
            left = is_left;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            //按下即开始拖动（无需先选中）
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (view != null)
                view.DragHandle(left, eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (view != null)
                view.CommitHandle();
        }
    }
}
