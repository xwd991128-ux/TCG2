using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 「未保存改动」控件监听：给一棵子树里的所有输入类控件（TMP 输入框 / 下拉 / 勾选）挂监听 → 置脏标记。
    /// 各编辑面板的**右侧参数表单是"保存时才读"**的（不在图 JSON / DTO 里），所以必须单独监听控件，
    /// 否则"只改了卡名/费用就退出"不会提示。
    /// 去重：同一控件只挂一次（按实例 id）；控件重建后 id 变化，下一次扫描自然重挂。
    /// </summary>
    public static class UnsavedWatch
    {
        private static readonly HashSet<int> hooked = new HashSet<int>();

        public static void HookAll(Transform root, Action on_dirty)
        {
            if (root == null || on_dirty == null)
                return;

            foreach (TMP_InputField inp in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (inp != null && hooked.Add(inp.GetInstanceID()))
                    inp.onValueChanged.AddListener(v => on_dirty());
            }
            foreach (Dropdown dd in root.GetComponentsInChildren<Dropdown>(true))
            {
                if (dd != null && hooked.Add(dd.GetInstanceID()))
                    dd.onValueChanged.AddListener(v => on_dirty());
            }
            foreach (TMP_Dropdown tdd in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                if (tdd != null && hooked.Add(tdd.GetInstanceID()))
                    tdd.onValueChanged.AddListener(v => on_dirty());
            }
            foreach (Toggle tg in root.GetComponentsInChildren<Toggle>(true))
            {
                if (tg != null && hooked.Add(tg.GetInstanceID()))
                    tg.onValueChanged.AddListener(v => on_dirty());
            }
        }
    }
}

namespace TcgEngine.UI
{
    /// <summary>
    /// 通用「未保存改动」确认弹层：**保存并返回 / 放弃改动 / 取消**。
    ///
    /// 用户要求（2026-10-01）：凡涉及保存的编辑界面（卡牌 / 关键词 / 增益 / 按钮 / 自定义节点 / 卡池…），
    /// 关闭或返回上一页前若存在未保存改动，必须先弹这个框，避免静默丢编辑。
    ///
    /// 用法：
    ///   UnsavedChangesPopup.Show(parent, "卡牌有未保存的修改", "直接退出会丢掉这些修改。要保存吗？",
    ///       () => { OnSave(); 导航离开(); },   // 保存并返回
    ///       () => { 放弃改动(); 导航离开(); });  // 放弃改动
    /// 点遮罩**不关闭**（有未保存内容 → 必须显式选择）。惰性创建、可重复复用，不每帧做任何事。
    /// </summary>
    public static class UnsavedChangesPopup
    {
        private static GameObject root;
        private static TMP_Text title_text;
        private static TMP_Text body_text;
        private static Button btn_save;
        private static Button btn_discard;
        private static Button btn_cancel;
        private static Action act_save;
        private static Action act_discard;

        public static bool IsOpen { get { return root != null && root.activeSelf; } }

        /// <summary>弹出确认框；on_save=保存并返回，on_discard=放弃改动并返回（取消=留在当前页）</summary>
        public static void Show(Transform parent, string title, string message, Action on_save, Action on_discard)
        {
            if (parent == null)
                return;
            if (root == null)
                Build(parent);
            if (root == null)
                return;

            act_save = on_save;
            act_discard = on_discard;
            if (title_text != null)
                title_text.text = string.IsNullOrEmpty(title) ? "有未保存的修改" : title;
            if (body_text != null)
                body_text.text = message ?? "";

            root.transform.SetParent(parent, false);
            root.SetActive(true);
            root.transform.SetAsLastSibling();
        }

        public static void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }

        // ---------------- 构建（只建一次） ----------------

        private static void Build(Transform parent)
        {
            root = new GameObject("UnsavedChangesPopup", typeof(RectTransform));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            //遮罩：点它不关闭（有未保存内容 → 必须显式选择）
            GameObject mask_go = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform mask_rt = mask_go.GetComponent<RectTransform>();
            mask_rt.SetParent(rt, false);
            mask_rt.anchorMin = Vector2.zero;
            mask_rt.anchorMax = Vector2.one;
            mask_rt.offsetMin = Vector2.zero;
            mask_rt.offsetMax = Vector2.zero;
            Image mask = mask_go.GetComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.55f);
            Button mask_btn = mask_go.GetComponent<Button>();
            mask_btn.targetGraphic = mask;
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(() => { });

            //面板
            GameObject panel_go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            RectTransform prt = panel_go.GetComponent<RectTransform>();
            prt.SetParent(rt, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(480f, 200f);
            prt.anchoredPosition = Vector2.zero;
            Image pimg = panel_go.GetComponent<Image>();
            pimg.color = new Color(0.16f, 0.17f, 0.2f, 1f);

            title_text = MakeText(prt, "Title", "有未保存的修改", 18);
            title_text.rectTransform.anchorMin = new Vector2(0f, 1f);
            title_text.rectTransform.anchorMax = new Vector2(1f, 1f);
            title_text.rectTransform.pivot = new Vector2(0.5f, 1f);
            title_text.rectTransform.anchoredPosition = new Vector2(0f, -18f);
            title_text.rectTransform.sizeDelta = new Vector2(-24f, 30f);

            body_text = MakeText(prt, "Body", "", 14);
            body_text.rectTransform.anchorMin = new Vector2(0f, 1f);
            body_text.rectTransform.anchorMax = new Vector2(1f, 1f);
            body_text.rectTransform.pivot = new Vector2(0.5f, 1f);
            body_text.rectTransform.anchoredPosition = new Vector2(0f, -56f);
            body_text.rectTransform.sizeDelta = new Vector2(-24f, 30f);

            btn_save = MakeButton(prt, "保存并返回", new Vector2(-158f, -62f));
            btn_save.onClick.AddListener(() =>
            {
                Action a = act_save;
                Hide();
                if (a != null) a();
            });
            btn_discard = MakeButton(prt, "放弃改动", new Vector2(0f, -62f));
            btn_discard.onClick.AddListener(() =>
            {
                Action a = act_discard;
                Hide();
                if (a != null) a();
            });
            btn_cancel = MakeButton(prt, "取消", new Vector2(158f, -62f));
            btn_cancel.onClick.AddListener(Hide);

            root.SetActive(false);
        }

        /// <summary>运行时建 TMP 文本（必须走 UIFonts，否则字形/风格与全页不一致）</summary>
        private static TMP_Text MakeText(RectTransform parent, string name, string text, int size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            UIFonts.ApplyFont(t);
            return t;
        }

        private static Button MakeButton(RectTransform parent, string label, Vector2 pos)
        {
            GameObject go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(146f, 38f);
            rt.anchoredPosition = pos;
            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.22f);
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            TMP_Text t = MakeText(rt, "Label", label, 15);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = Vector2.zero;
            t.rectTransform.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
