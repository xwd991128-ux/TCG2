using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TcgEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// Base class for UI panels that can be hidden or shown, with a fade-in fade-out effect
    /// </summary>

    [RequireComponent(typeof(CanvasGroup))]
    public class UIPanel : MonoBehaviour
    {
        public float display_speed = 4f;

        public UnityAction onShow;
        public UnityAction onHide;

        protected CanvasGroup canvas_group;
        protected bool visible;

        protected virtual void Awake()
        {
            canvas_group = GetComponent<CanvasGroup>();
            canvas_group.alpha = 0f;
            visible = false;
        }

        protected virtual void Start()
        {

        }

        protected virtual void Update()
        {

            float add = visible ? display_speed : -display_speed;
            float alpha = Mathf.Clamp01(canvas_group.alpha + add * Time.deltaTime);
            canvas_group.alpha = alpha;

            if (!visible && alpha < 0.01f)
                AfterHide();
        }

        public virtual void Toggle(bool instant = false)
        {
            if (IsVisible())
                Hide(instant);
            else
                Show(instant);
        }

        public virtual void Show(bool instant = false)
        {
            visible = true;
            gameObject.SetActive(true);

            //显示页恢复输入（与 Hide 对称）
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = true;
                canvas_group.interactable = true;
            }

            if (instant || display_speed < 0.01f)
                canvas_group.alpha = 1f;

            //场景 BGM：界面自动切歌（只有 BgmManager 映射表里的界面会触发；弹窗/选择器自动忽略）
            TcgEngine.Audio.BgmManager.NotifyPanelShown(GetType().Name);

            if (onShow != null)
                onShow.Invoke();
        }

        public virtual void Hide(bool instant = false)
        {
            visible = false;
            if (instant || display_speed < 0.01f)
                canvas_group.alpha = 0f;

            //★ 关键：隐藏的页面必须同时停止接收射线。
            //只把 alpha 归零时，全屏页仍会以 blocksRaycasts=true 吃掉下面页面（规则编辑器等）的所有点击 ——
            //表现就是"界面看得见、所有按钮和输入框都点不动"（HomePanel 的注释也记过同类现象）。
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }

            if (onHide != null)
                onHide.Invoke();
        }

        public void SetVisible(bool visi)
        {
            if (!visible && visi)
                Show();
            else if (visible && !visi)
                Hide();
        }

        public virtual void AfterHide()
        {
            gameObject.SetActive(false);
        }

        public bool IsVisible()
        {
            return visible;
        }

        public bool IsFullyVisible()
        {
            return visible && canvas_group.alpha > 0.99f;
        }

        public float GetAlpha()
        {
            return canvas_group.alpha;
        }
    }
}