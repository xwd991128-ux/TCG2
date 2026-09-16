using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 一次裁切会话的状态：**原始图**（未裁切的 Texture2D）+ 视口变换，跨多次打开弹框保留，
    /// 用来实现「重新打开回到上次结果」。
    ///
    /// 关键设计：这里存的是原始图而不是裁好的成品——否则再次打开就是在成品上二次裁切，
    /// 越裁越小、越裁越糊。成品只通过 onImageChanged 往外交付。
    /// </summary>
    public class ImageClipState
    {
        public Texture2D source;      // 原始图（本类负责释放）
        public Vector2 pos;           // 图片中心相对视口中心的偏移（UI 单位）
        public float scale = 1f;      // 图片缩放（1 = 1 像素 1 UI 单位）
        public bool has_state;        // 是否已有可恢复的裁切位置

        public bool HasImage { get { return source != null; } }

        /// <summary>释放原始图并清空全部状态</summary>
        public void Dispose()
        {
            if (source != null)
            {
                UnityEngine.Object.Destroy(source);
                source = null;
            }
            ResetCrop();
        }

        /// <summary>只重置裁切位置，保留原始图</summary>
        public void ResetCrop()
        {
            pos = Vector2.zero;
            scale = 1f;
            has_state = false;
        }
    }

    /// <summary>
    /// 卡图裁切入口：挂在要编辑的图片（UnityEngine.UI.Image）上，**点击即弹出裁切弹框**。
    /// 与 RichTextEditorUI 同一套用法：popup 留空时运行时自动创建，不需要手动拖拽任何引用。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ImageClipEditorUI : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>是否允许在弹框里换图。面板图片（战场）置 false：只允许调整大小与重置（图片由卡牌图片自动生成）。</summary>
        public bool allow_replace = true;

        [Header("引用（可留空，运行时自动补齐）")]
        public ImageClipPopupUI popup;     // 裁切弹框
        public Image display;              // 显示 / 回写的图片；留空取自身 Image
        public Sprite currentSprite;       // 当前图（也是裁切结果）
        public bool interactable = true;   // 关掉后只显示、不响应点击

        [Header("裁切比例")]
        /// <summary>目标显示区域的宽×高，决定裁切比例（卡面图 8.56×8.36；面板图 250×350）</summary>
        public Vector2 targetSize = new Vector2(1f, 1f);

        /// <summary>裁切确认后回调（携带裁好的 Sprite；ClearImage 时回调 null）</summary>
        public event Action<Sprite> onImageChanged;

        private readonly ImageClipState state = new ImageClipState();

        /// <summary>取显示图：显式绑定优先，否则退化为自身的 Image</summary>
        private Image DisplayImage()
        {
            if (display != null)
                return display;
            display = GetComponent<Image>();
            return display;
        }

        /// <summary>点击图片 / 手动调用：打开裁切弹框</summary>
        public void Open()
        {
            if (!interactable)
                return;
            EnsurePopup();
            if (popup == null)
                return;

            // 外部换过图（例如「选择卡面图片」按钮）→ 丢弃旧裁切状态，从新图重新开始
            Image img = DisplayImage();
            Sprite shown = img != null ? img.sprite : null;
            if (shown != currentSprite)
            {
                currentSprite = shown;
                state.Dispose();
            }

            popup.Open(state, currentSprite, this, targetSize);
        }

        /// <summary>关闭并丢弃本次改动</summary>
        public void Close()
        {
            if (popup != null && popup.IsVisible())
                popup.OnClickCancel();
        }

        /// <summary>确定（等价于点弹框里的确定）</summary>
        public void Confirm()
        {
            if (popup != null && popup.IsVisible())
                popup.OnClickConfirm();
        }

        /// <summary>清空图片与裁切状态</summary>
        public void ClearImage()
        {
            state.Dispose();
            currentSprite = null;
            Image img = DisplayImage();
            if (img != null)
                img.sprite = null;
            if (onImageChanged != null)
                onImageChanged.Invoke(null);
        }

        /// <summary>外部设置图片（会重置裁切状态）</summary>
        public void SetImage(Sprite sp)
        {
            state.Dispose();
            currentSprite = sp;
            ApplyToImage(sp);
        }

        /// <summary>弹框确认后由 popup 调用：回写图片并触发回调</summary>
        public void ApplyResult(Sprite sp)
        {
            currentSprite = sp;
            ApplyToImage(sp);
            if (onImageChanged != null)
                onImageChanged.Invoke(sp);
        }

        /// <summary>写到显示图上；有图时把 color 拉回白色，避免占位色把卡图压暗</summary>
        private void ApplyToImage(Sprite sp)
        {
            Image img = DisplayImage();
            if (img == null)
                return;
            img.sprite = sp;
            img.enabled = sp != null;
            if (sp != null)
                img.color = Color.white;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Open();
        }

        /// <summary>没有引用弹框时，自动在同 Canvas 下创建一个</summary>
        public void EnsurePopup()
        {
            if (popup != null)
                return;
            popup = ImageClipPopupUI.Create(transform);
        }

        private void OnDestroy()
        {
            state.Dispose();   // 原始图是本组件创建的，随组件一起释放
        }
    }
}
