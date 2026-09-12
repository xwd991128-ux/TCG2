using System;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

namespace TcgEngine.UI
{
    /// <summary>
    /// 富文本编辑入口：挂在「长文本展示框」上，点击即弹出 RichTextPopupUI 编辑。
    /// text 保存带标签的富文本源码（如「造成 &lt;b&gt;2&lt;/b&gt; 点伤害」），可直接存 JSON / 卡牌数据。
    ///
    /// 用法：
    /// 1) 展示框上挂本组件 + 一个 Image（作为点击射线目标）；
    /// 2) display 留空会自动取自身或子物体上的 TMP_Text（richText 会被置为 true）；
    /// 3) popup 留空会在首次点击时自动创建一个富文本弹框到同 Canvas 下，无需手动拖拽引用。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RichTextEditorUI : MonoBehaviour, IPointerClickHandler
    {
        [Header("引用（可留空，运行时自动补齐）")]
        public RichTextPopupUI popup;      // 富文本编辑弹框
        public TMP_Text display;           // 展示用 TMP 文本（richText=true）

        [TextArea(3, 8)]
        public string text = "造成 <b>2</b> 点伤害";   // 当前富文本（带标签原文）

        /// <summary>编辑确认后回调（携带新富文本字符串）</summary>
        public event Action<string> onTextChanged;

        protected virtual void Awake()
        {
            if (display == null)
                display = GetComponent<TMP_Text>();
            if (display == null)
                display = GetComponentInChildren<TMP_Text>(true);
            // 全局字体未设置时，现做一份可渲染中文的动态字体（否则会退回无中文字形的 TMP 默认字体、显示成方块）
            if (UIFonts.font_asset == null)
                UIFonts.GetChineseFont();
            RefreshDisplay();
            UIFonts.Apply(gameObject);   // 展示框同样服从全局字体入口
        }

        /// <summary>点击展示框时调用：弹框载入 text</summary>
        public void Open()
        {
            EnsurePopup();
            if (popup == null)
                return;
            if (popup.font == null)
                popup.font = UIFonts.font_asset != null ? UIFonts.font_asset : (display != null ? display.font : null);   // 复用已渲染成功的字体
            popup.Open(text, this);   // Open 内部会统一 ApplyFonts，兼容 Awake 先后顺序
        }

        /// <summary>关闭并丢弃修改</summary>
        public void Close()
        {
            if (popup != null && popup.IsVisible())
                popup.OnClickCancel();
        }

        /// <summary>确定：回写 text 并触发 onTextChanged</summary>
        public void Confirm()
        {
            if (popup != null && popup.IsVisible())
                popup.OnClickConfirm();
        }

        /// <summary>设置富文本并刷新展示（不触发 onTextChanged，供外部初始化用）</summary>
        public void SetText(string new_text)
        {
            text = new_text ?? "";
            RefreshDisplay();
        }

        /// <summary>弹框确认后由 popup 调用：更新文本、刷新展示并触发回调</summary>
        public void ApplyResult(string new_text)
        {
            text = new_text ?? "";
            RefreshDisplay();
            if (onTextChanged != null)
                onTextChanged.Invoke(text);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Open();
        }

        /// <summary>没有引用弹框时，自动在同 Canvas 下创建一个（风格与「多选框」弹层一致）</summary>
        public void EnsurePopup()
        {
            if (popup != null)
                return;
            popup = RichTextPopupUI.Create(transform);   // Awake 内自建全部控件
        }

        private void RefreshDisplay()
        {
            if (display == null)
                return;
            display.richText = true;
            display.text = text;
        }
    }
}
