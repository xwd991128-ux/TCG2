using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 自绘「弹出选择」层（单选 / 多选）：全项目统一的选项选择交互。
    ///
    /// 背景：UGUI Dropdown 的模板会被父级裁切、且不支持多选；规则编辑器已自绘一套
    /// （类型 / 阵营 / 稀有度 / 种族 / 关键词）。这里把它抽成公共组件，供关键词管理、
    /// 筛选弹层等复用，保证「点一下弹出列表 → 点选项即选中」的交互与样式处处一致。
    ///
    /// 特点：
    ///   · 运行时自建，不需要在场景里预先摆控件；
    ///   · 单例复用，重复打开只重建选项列表；
    ///   · 单选=点选项即回调并关闭；多选=每次勾选都回调，弹层保持打开；
    ///   · 点击遮罩或右上角 × 关闭。
    ///
    /// 用法：
    ///   UISelectPopup.OpenSingle(transform, "原生机制", names, values, current, v => {...});
    ///   UISelectPopup.OpenMulti (transform, "关键词",  names, values, selected, list => {...});
    ///
    /// 与旧控件对接：<see cref="AttachToDropdown"/> 可把场景里已有的 UGUI Dropdown
    /// 就地换成同尺寸的选择按钮（停用 Dropdown 组件、保留它的底色与布局），无需重跑生成工具。
    /// </summary>
    public class UISelectPopup : MonoBehaviour
    {
        private static UISelectPopup m_instance;

        private GameObject m_root;
        private RectTransform m_list;
        private TMP_Text m_title;

        private string[] m_options;          // 选项显示名
        private string[] m_values;           // 与显示名一一对应的实际值（null 时显示名即实际值）
        private bool m_multi;
        private readonly List<string> m_selected = new List<string>();   // 已选的实际值
        private Action<List<string>> m_commit;

        /// <summary>弹层是否正在显示</summary>
        public static bool IsOpen => m_instance != null && m_instance.m_root != null && m_instance.m_root.activeSelf;

        // ==================== 对外 API ====================

        /// <summary>弹出单选：点选项即回调并关闭。values 为 null 时显示名即实际值。</summary>
        public static void OpenSingle(Transform context, string title, IList<string> options, IList<string> values,
            string current, Action<string> on_pick)
        {
            UISelectPopup p = Ensure(context);
            p.m_multi = false;
            p.m_commit = list => { if (on_pick != null) on_pick(list != null && list.Count > 0 ? list[0] : ""); };
            p.m_selected.Clear();
            if (!string.IsNullOrEmpty(current))
                p.m_selected.Add(current);
            p.Show(title, options, values);
        }

        /// <summary>弹出多选：每次勾选/取消都回调（弹层保持打开）。</summary>
        public static void OpenMulti(Transform context, string title, IList<string> options, IList<string> values,
            IList<string> selected, Action<List<string>> on_commit)
        {
            UISelectPopup p = Ensure(context);
            p.m_multi = true;
            p.m_commit = on_commit;
            p.m_selected.Clear();
            if (selected != null)
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    if (!string.IsNullOrEmpty(selected[i]) && !p.m_selected.Contains(selected[i]))
                        p.m_selected.Add(selected[i]);
                }
            }
            p.Show(title, options, values);
        }

        /// <summary>关闭弹层（会清掉回调，避免关闭后仍被触发）</summary>
        public void Close()
        {
            m_commit = null;
            if (m_root != null)
                m_root.SetActive(false);
        }

        /// <summary>
        /// 把场景里已有的 UGUI Dropdown 就地换成「弹出单选」按钮：
        /// 停用 Dropdown 组件（保留它的底色 Image 与全部布局），隐藏它自带的 Label/Template，
        /// 再在原对象上补挂 Button + TMP 文本。尺寸/位置完全不变，也不需要重跑生成工具。
        /// 返回文本组件，供调用方刷新当前值显示。
        /// </summary>
        public static TMP_Text AttachToDropdown(Dropdown dd, Action onClick)
        {
            if (dd == null)
                return null;

            //幂等：已经挂过交互层就直接复用，避免重复挂载出现两层按钮
            Transform existing_hit = dd.transform.Find("SelectHit");
            if (existing_hit != null)
            {
                Transform existing_label = existing_hit.Find("SelectLabel");
                return existing_label != null ? existing_label.GetComponent<TMP_Text>() : null;
            }

            dd.enabled = false;   // 停用旧下拉逻辑；GameObject 保持激活，布局不受影响

            Transform legacy_label = dd.transform.Find("Label");
            if (legacy_label != null)
                legacy_label.gameObject.SetActive(false);
            Transform template = dd.transform.Find("Template");
            if (template != null)
                template.gameObject.SetActive(false);

            //交互层：新建一个铺满的透明子对象承载 Button + 文本，叠在原下拉之上。
            //不能直接在 Dropdown 所在对象上 AddComponent<Button>()——同一对象已有 Selectable(Dropdown)，
            //AddComponent 会返回 null（规则编辑器里是先 Destroy 掉旧 Dropdown 才敢挂 Button）。
            //用叠加层既不破坏原对象、也不需要重跑生成工具，尺寸随父级拉伸、位置完全不变。
            GameObject hit = new GameObject("SelectHit", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform hrt = hit.GetComponent<RectTransform>();
            hrt.SetParent(dd.transform, false);
            UIFactory.SetStretch(hrt);
            hrt.SetAsLastSibling();

            Image hit_img = hit.GetComponent<Image>();
            hit_img.color = new Color(1f, 1f, 1f, 0f);   //透明但接收射线
            hit_img.raycastTarget = true;

            Button btn = hit.GetComponent<Button>();
            if (btn == null)
            {
                Debug.LogWarning("UISelectPopup：无法为「" + dd.name + "」创建选择按钮（Button 组件创建失败）");
                return null;
            }
            //悬停/按下反馈仍然作用在原输入框底色上，观感与原来一致
            Image field = dd.GetComponent<Image>();
            btn.targetGraphic = field != null ? (Graphic)field : hit_img;
            UITheme.ApplyButtonColors(btn);
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());

            TMP_Text t = UIFactory.CreateTmpText("SelectLabel", hit.transform, "", UITheme.FontBody, UITheme.TextBody,
                TextAlignmentOptions.Left, UIFonts.ResolveFont());
            t.overflowMode = TextOverflowModes.Ellipsis;   //长卡池名等超出时省略，不撑破输入框
            RectTransform rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12, 0);
            rt.offsetMax = new Vector2(-12, 0);
            t.raycastTarget = false;
            return t;
        }

        // ==================== 内部：显示与重建 ====================

        private void Show(string title, IList<string> options, IList<string> values)
        {
            m_options = ToArray(options);
            m_values = ToArray(values);
            if (m_title != null)
                m_title.text = (m_multi ? "多选：" : "选择：") + title;

            m_root.SetActive(true);
            m_root.transform.SetAsLastSibling();
            RebuildList();
        }

        private void RebuildList()
        {
            if (m_list == null)
                return;

            //清空旧行（先脱离父级再销毁，避免布局组在销毁过程中反复重排）
            for (int i = m_list.childCount - 1; i >= 0; i--)
            {
                Transform c = m_list.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            if (m_options != null)
            {
                for (int i = 0; i < m_options.Length; i++)
                {
                    string label = m_options[i];
                    if (string.IsNullOrEmpty(label))
                        continue;
                    string val = ValueAt(i);
                    CreateRow(label, val, m_selected.Contains(val));
                }
            }

            //不在候选里的已选值也列出来（便于回看 / 取消）
            for (int i = 0; i < m_selected.Count; i++)
            {
                string v = m_selected[i];
                if (!string.IsNullOrEmpty(v) && !HasOptionValue(v))
                    CreateRow(v, v, true);
            }
        }

        private void CreateRow(string label, string value, bool on)
        {
            GameObject row = new GameObject("Opt", typeof(RectTransform));
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.SetParent(m_list, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 30;
            le.minHeight = 30;

            Image bg = row.AddComponent<Image>();
            bg.color = on ? new Color(0.2f, 0.55f, 0.85f, 0.95f) : UITheme.CtrlWeak;

            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            UITheme.ApplyButtonColors(btn);

            TMP_Text t = NewText("Label", rt, (m_multi ? (on ? "√ " : "□ ") : "") + label,
                UITheme.FontBody, TextAlignmentOptions.Left);   //√/□ 在 GB2312 内：中文字体必有字形（☑/☐ 常缺 → 方块）
            t.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform trt = t.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12, 0);
            trt.offsetMax = new Vector2(-12, 0);
            t.raycastTarget = false;

            string captured = value;   //写回实际值（如关键词 id），显示仍用 label
            btn.onClick.AddListener(() => OnPick(captured));
        }

        private void OnPick(string value)
        {
            if (m_multi)
            {
                if (m_selected.Contains(value))
                    m_selected.Remove(value);
                else
                    m_selected.Add(value);

                if (m_commit != null)
                    m_commit(new List<string>(m_selected));
                RebuildList();
            }
            else
            {
                if (m_commit != null)
                    m_commit(new List<string> { value });
                Close();
            }
        }

        private string ValueAt(int index)
        {
            if (m_values != null && index < m_values.Length)
                return m_values[index];
            return m_options[index];
        }

        private bool HasOptionValue(string value)
        {
            if (m_options == null)
                return false;
            for (int i = 0; i < m_options.Length; i++)
            {
                if (ValueAt(i) == value)
                    return true;
            }
            return false;
        }

        private static string[] ToArray(IList<string> list)
        {
            if (list == null)
                return null;
            string[] arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
                arr[i] = list[i];
            return arr;
        }

        // ==================== 内部：构建 ====================

        private static UISelectPopup Ensure(Transform context)
        {
            if (m_instance != null)
                return m_instance;

            //挂到调用方所在的 Canvas 上（覆盖整屏），找不到再退回场景根 Canvas / 调用方自身
            Transform parent = null;
            Canvas own = context != null ? context.GetComponentInParent<Canvas>() : null;
            if (own != null)
                parent = own.transform;
            if (parent == null)
            {
                Canvas top = FindTopCanvas();
                parent = top != null ? top.transform : context;
            }

            GameObject go = new GameObject("UISelectPopup", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UIFactory.SetStretch(go.GetComponent<RectTransform>());

            m_instance = go.AddComponent<UISelectPopup>();
            m_instance.Build(go);
            return m_instance;
        }

        private static Canvas FindTopCanvas()
        {
            Canvas best = null;
            foreach (Canvas c in FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isRootCanvas)
                    continue;
                if (best == null || c.sortingOrder >= best.sortingOrder)
                    best = c;
            }
            return best;
        }

        private void Build(GameObject go)
        {
            m_root = go;
            RectTransform root = go.GetComponent<RectTransform>();

            //遮罩：点击关闭
            Image mask = go.AddComponent<Image>();
            mask.color = UITheme.MaskPopup;
            Button mask_btn = go.AddComponent<Button>();
            mask_btn.targetGraphic = mask;
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(Close);

            //面板
            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            RectTransform prt = panel.GetComponent<RectTransform>();
            prt.SetParent(root, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(440, 520);

            Image pimg = panel.AddComponent<Image>();
            pimg.color = UITheme.BgPopup;
            Button pbtn = panel.AddComponent<Button>();   //吞掉点击，避免穿透到遮罩
            pbtn.targetGraphic = pimg;
            pbtn.transition = Selectable.Transition.None;

            m_title = NewText("Title", prt, "选择", UITheme.FontButton, TextAlignmentOptions.Left);
            m_title.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform trt = m_title.rectTransform;
            trt.anchorMin = new Vector2(0, 1);
            trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0.5f, 1);
            trt.anchoredPosition = new Vector2(0, -6);
            trt.sizeDelta = new Vector2(-60, 34);

            RectTransform close = NewButton("Close", prt, "×", Close);
            close.anchorMin = new Vector2(1, 1);
            close.anchorMax = new Vector2(1, 1);
            close.pivot = new Vector2(1, 1);
            close.anchoredPosition = new Vector2(-6, -6);
            close.sizeDelta = new Vector2(34, 34);

            //滚动列表
            GameObject scroll_go = new GameObject("Scroll", typeof(RectTransform));
            RectTransform srt = scroll_go.GetComponent<RectTransform>();
            srt.SetParent(prt, false);
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(12, 16);
            srt.offsetMax = new Vector2(-12, -46);

            ScrollRect scroll = scroll_go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            GameObject view_go = new GameObject("Viewport", typeof(RectTransform));
            RectTransform vrt = view_go.GetComponent<RectTransform>();
            vrt.SetParent(srt, false);
            UIFactory.SetStretch(vrt);
            view_go.AddComponent<RectMask2D>();
            scroll.viewport = vrt;

            GameObject content_go = new GameObject("Content", typeof(RectTransform));
            RectTransform crt = content_go.GetComponent<RectTransform>();
            crt.SetParent(vrt, false);
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = content_go.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 3;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            ContentSizeFitter csf = content_go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            m_list = crt;

            UIFonts.ApplyResolved(go);
            go.SetActive(false);
        }

        private static TMP_Text NewText(string name, Transform parent, string txt, int size, TextAlignmentOptions align)
        {
            return UIFactory.CreateTmpText(name, parent, txt, size, UITheme.TextBody, align, UIFonts.ResolveFont());
        }

        /// <summary>面板内按钮（自绘 TMP 版本，避免依赖无中文字形的内置 Font）</summary>
        private static RectTransform NewButton(string name, Transform parent, string label, Action onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = UITheme.Ctrl;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);

            TMP_Text t = NewText("Text", rt, label, UITheme.FontButton, TextAlignmentOptions.Center);
            UIFactory.SetStretch(t.rectTransform);
            t.raycastTarget = false;

            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            return rt;
        }
    }
}
