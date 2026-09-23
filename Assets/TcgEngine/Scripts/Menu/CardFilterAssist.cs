using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 构筑「筛选助手」——把高级筛选语法与筛选预设做成**看得懂、点得到**的一层：
    ///
    ///   · 语法提示：一行示例 + 可滚动的「字段速查」（点一下就把示例追加进搜索框）
    ///   · 已生效条件：把搜索框里的语法解析成小胶囊（点 × 即从搜索框移除该条件）
    ///   · 预设：保存当前 / 应用 / 删除 / 导出 / 导入（落盘 Workshop/filter_presets.json，
    ///           导出到 Workshop/presets/&lt;名&gt;.json 可分享）
    ///
    /// 设计取舍（重要）：
    ///   1) **不新建输入框**——用户就在原来的搜索框里打字，助手只负责"提示 + 追加 + 移除 + 预设"，
    ///      这样不会与场景里已生成的筛选弹层抢布局，也不需要重跑生成工具；
    ///   2) 运行时自建 UI 全部用 TMP + UITheme/UIFonts 规范（旧版 uGUI Text 会字体发糊/缺中文字形）；
    ///   3) 预设名直接取语法原文（天然唯一、免打字），列表里显示的就是"这条预设筛什么"。
    /// </summary>
    public class CardFilterAssist : MonoBehaviour
    {
        private static CardFilterAssist m_instance;

        private GameObject m_root;
        private RectTransform m_cond_row;      //已生效条件
        private RectTransform m_help_list;     //字段速查
        private TMP_Text m_status;
        private Func<string> m_get_query;
        private Action<string> m_set_query;
        private Func<CardFilterPreset> m_capture;      //打包当前整套筛选状态（保存预设用）
        private Action<CardFilterPreset> m_apply;      //应用整套筛选状态
        private string m_current_name = "";           //最近选中/保存的预设名（删除/导出用）
        private string m_last_query;
        private int m_help_count;              //已生成的速查行数（用于"只建一次"）

        private const float PANEL_W = 560f;
        private const float PANEL_H = 600f;

        /// <summary>
        /// 打开筛选助手。
        /// capture/apply：打包/应用**整套筛选方案**（语法 + 勾选 + 卡池 + 金卡 + 排序）——预设用；
        /// get_query/set_query：只读写高级筛选语法（条件胶囊与字段速查用，改完即时生效）。
        /// </summary>
        public static void Open(Transform context,
            Func<CardFilterPreset> capture, Action<CardFilterPreset> apply,
            Func<string> get_query, Action<string> set_query)
        {
            CardFilterAssist p = Ensure(context);
            p.m_capture = capture;
            p.m_apply = apply;
            p.m_get_query = get_query;
            p.m_set_query = set_query;
            p.m_root.SetActive(true);
            p.m_root.transform.SetAsLastSibling();
            p.RefreshConditions();
            p.SetStatus("");
        }

        public static bool IsOpen
        {
            get { return m_instance != null && m_instance.m_root != null && m_instance.m_root.activeSelf; }
        }

        // ==================== 构建 ====================

        private static CardFilterAssist Ensure(Transform context)
        {
            if (m_instance != null)
                return m_instance;

            Transform parent = null;
            Canvas own = context != null ? context.GetComponentInParent<Canvas>() : null;
            if (own != null)
                parent = own.transform;
            if (parent == null)
            {
                Canvas top = null;
                foreach (Canvas c in FindObjectsOfType<Canvas>())
                {
                    if (c == null || !c.isRootCanvas)
                        continue;
                    if (top == null || c.sortingOrder >= top.sortingOrder)
                        top = c;
                }
                parent = top != null ? top.transform : context;
            }

            GameObject go = new GameObject("CardFilterAssist", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UIFactory.SetStretch(go.GetComponent<RectTransform>());

            m_instance = go.AddComponent<CardFilterAssist>();
            m_instance.Build(go);
            return m_instance;
        }

        private void Update()
        {
            if (m_root == null || !m_root.activeSelf)
                return;
            //搜索框可能被用户继续编辑 → 条件胶囊跟着刷新（便宜：只比字符串）
            string now = m_get_query != null ? (m_get_query() ?? "") : "";
            if (now != m_last_query)
                RefreshConditions();
        }

        private void Close()
        {
            if (m_root != null)
                m_root.SetActive(false);
        }

        private void Build(GameObject go)
        {
            m_root = go;

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
            prt.SetParent(go.transform, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            Image pimg = panel.AddComponent<Image>();
            pimg.color = UITheme.BgPopup;
            Button pbtn = panel.AddComponent<Button>();     //吞掉点击，避免穿透到遮罩
            pbtn.targetGraphic = pimg;
            pbtn.transition = Selectable.Transition.None;

            //标题
            TMP_Text title = NewText("Title", prt, "筛选助手（高级语法 + 预设）", UITheme.FontButton, TextAlignmentOptions.Left);
            SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(-96, 34));

            RectTransform close = NewButton("Close", prt, "×", Close);
            SetRect(close, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(34, 34));

            //提示行
            TMP_Text hint = NewText("Hint", prt, "语法示例：" + CardQuery.SyntaxHint + "　　多条件空格分隔（与），前面加减号表示排除",
                UITheme.FontBody - 2, TextAlignmentOptions.Left);
            hint.color = UITheme.TextDim;
            SetRect(hint.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -46), new Vector2(-28, 44));

            //已生效条件标题
            TMP_Text cond_title = NewText("CondTitle", prt, "已生效条件（点 × 移除）：", UITheme.FontBody, TextAlignmentOptions.Left);
            cond_title.color = UITheme.TextDim;
            SetRect(cond_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -94), new Vector2(-28, 24));

            //已生效条件行
            GameObject cond_go = new GameObject("Conds", typeof(RectTransform));
            m_cond_row = cond_go.GetComponent<RectTransform>();
            m_cond_row.SetParent(prt, false);
            SetRect(m_cond_row, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -122), new Vector2(-28, 30));
            HorizontalLayoutGroup hlg = cond_go.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.spacing = 6;
            hlg.padding = new RectOffset(0, 0, 2, 2);

            //速查标题
            TMP_Text help_title = NewText("HelpTitle", prt, "字段速查（点一下插进搜索框）：", UITheme.FontBody, TextAlignmentOptions.Left);
            help_title.color = UITheme.TextDim;
            SetRect(help_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -166), new Vector2(-28, 24));

            //速查滚动列表（底部锚定，与下面的预设行/按钮分层，避免重叠）
            GameObject scroll_go = new GameObject("Scroll", typeof(RectTransform));
            RectTransform srt = scroll_go.GetComponent<RectTransform>();
            srt.SetParent(prt, false);
            SetRect(srt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 132), new Vector2(-28, 268));
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
            m_help_list = content_go.GetComponent<RectTransform>();
            m_help_list.SetParent(vrt, false);
            m_help_list.anchorMin = new Vector2(0, 1);
            m_help_list.anchorMax = new Vector2(1, 1);
            m_help_list.pivot = new Vector2(0.5f, 1);
            m_help_list.anchoredPosition = Vector2.zero;
            m_help_list.sizeDelta = new Vector2(0, 0);
            VerticalLayoutGroup vlg = content_go.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 3;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            ContentSizeFitter csf = content_go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = m_help_list;

            //预设行
            RectTransform preset_row = UIFactory.CreateRect("Presets", prt);
            SetRect(preset_row, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(-28, 34));
            HorizontalLayoutGroup prlg = preset_row.gameObject.AddComponent<HorizontalLayoutGroup>();
            prlg.childForceExpandWidth = false;
            prlg.childControlWidth = true;
            prlg.childControlHeight = true;
            prlg.spacing = 6;
            AddRowButton(preset_row, "预设 ▾", 110f, OnClickPresets);
            AddRowButton(preset_row, "保存当前", 100f, OnClickSavePreset);
            AddRowButton(preset_row, "删除选中", 100f, OnClickDeletePreset);
            AddRowButton(preset_row, "导出", 70f, OnClickExportPreset);
            AddRowButton(preset_row, "导入", 70f, OnClickImportPreset);

            //底部：状态 + 清空 + 应用（自下而上分层：状态 12 / 按钮 44 / 预设行 90 / 速查列表 132）
            m_status = NewText("Status", prt, "", UITheme.FontBody - 2, TextAlignmentOptions.Left);
            m_status.color = UITheme.TextDim;
            SetRect(m_status.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(-28, 24));

            RectTransform clear = NewButton("ClearAll", prt, "清空条件", OnClickClear);
            SetRect(clear, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(-12, 44), new Vector2(150, 38));

            RectTransform apply = NewButton("Apply", prt, "应用并关闭", OnClickApply);
            SetRect(apply, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(12, 44), new Vector2(170, 38));
            apply.GetComponent<Image>().color = UITheme.CtrlStrong;

            BuildHelpList();

            UIFonts.ApplyResolved(go);
            go.SetActive(false);
        }

        /// <summary>字段速查列表（建一次即可；行多但都是按钮，复用同一个滚动区）</summary>
        private void BuildHelpList()
        {
            if (m_help_list == null || m_help_count > 0)
                return;

            List<CardQueryFieldHelp> all = new List<CardQueryFieldHelp>();
            all.AddRange(CardQuery.FieldHelp());
            all.AddRange(CardQuery.ValueHelp());

            string last_group = null;
            for (int i = 0; i < all.Count; i++)
            {
                CardQueryFieldHelp h = all[i];
                if (h == null)
                    continue;
                if (h.group != last_group)
                {
                    last_group = h.group;
                    TMP_Text gt = NewText("Group", m_help_list, "— " + last_group + " —", UITheme.FontBody - 2, TextAlignmentOptions.Left);
                    gt.color = UITheme.TextDim;
                    LayoutElement gle = gt.gameObject.AddComponent<LayoutElement>();
                    gle.preferredHeight = 24;
                    gle.minHeight = 24;
                }

                string example = h.example;
                RectTransform row = NewButton("Help_" + i, m_help_list, h.title + "　" + example, () => OnClickHelp(example));
                LayoutElement le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 30;
                le.minHeight = 30;
                if (!string.IsNullOrEmpty(h.desc) && row.childCount > 0)
                {
                    TMP_Text rt = row.GetChild(0).GetComponent<TMP_Text>();
                    if (rt != null)
                        rt.color = UITheme.TextBody;
                }
                m_help_count++;
            }
        }

        // ==================== 交互 ====================

        /// <summary>点速查项：把示例追加进搜索框（已存在则不动），即时生效</summary>
        private void OnClickHelp(string example)
        {
            string cur = m_get_query != null ? (m_get_query() ?? "") : "";
            if (cur.Contains(example))
            {
                SetStatus("条件已存在：" + example);
                return;
            }
            string next = string.IsNullOrEmpty(cur.Trim()) ? example : (cur.Trim() + " " + example);
            ApplyQuery(next);
            SetStatus("已添加条件：" + example);
        }

        /// <summary>点条件胶囊的 × ：只移除这一条（其余条件原样保留）</summary>
        private void OnRemoveTerm(CardQueryTerm term)
        {
            string cur = m_get_query != null ? (m_get_query() ?? "") : "";
            CardQuery q = CardQuery.Parse(cur);
            List<CardQueryTerm> keep = new List<CardQueryTerm>();
            bool removed = false;
            for (int i = 0; i < q.terms.Count; i++)
            {
                if (!removed && q.terms[i].raw == term.raw)
                {
                    removed = true;
                    continue;
                }
                keep.Add(q.terms[i]);
            }
            ApplyQuery(CardQuery.ToSyntax(keep));
        }

        private void ApplyQuery(string text)
        {
            if (m_set_query != null)
                m_set_query(text ?? "");
            RefreshConditions();
        }

        private void OnClickClear()
        {
            ApplyQuery("");
            SetStatus("已清空筛选条件");
        }

        private void OnClickApply()
        {
            ApplyQuery(m_get_query != null ? m_get_query() : "");
            Close();
        }

        private void OnClickPresets()
        {
            List<string> labels = new List<string>();
            List<string> values = new List<string>();

            List<string> recent = CardFilterPresetIO.GetRecent();
            for (int i = 0; i < recent.Count; i++)
            {
                CardFilterPreset rp = CardFilterPresetIO.Get(recent[i]);
                if (rp == null)
                    continue;
                labels.Add("最近：" + rp.name);
                values.Add(rp.name);
            }

            List<CardFilterPreset> all = CardFilterPresetIO.GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || string.IsNullOrEmpty(all[i].name))
                    continue;
                //列表里显示"这条预设筛什么"（摘要含语法/勾选/卡池/排序）
                string sum = all[i].Summary();
                labels.Add(sum == all[i].name ? all[i].name : (all[i].name + "　（" + sum + "）"));
                values.Add(all[i].name);
            }

            if (values.Count == 0)
            {
                SetStatus("还没有预设：先输入条件，再点「保存当前」");
                return;
            }

            string current = GetCurrentPresetName();
            UISelectPopup.OpenSingle(transform, "筛选预设", labels, values, current, OnPresetPicked);
        }

        private void OnPresetPicked(string name)
        {
            CardFilterPreset p = CardFilterPresetIO.Get(name);
            if (p == null)
                return;
            CardFilterPresetIO.MarkUsed(name);
            m_current_name = name;

            if (m_apply != null)
                m_apply(p);              //整套写回：勾选 / 卡池 / 金卡 / 排序 / 语法
            else
                ApplyQuery(p.query);     //没有打包回调时退回"只应用语法"

            RefreshConditions();         //语法胶囊跟着刷新
            SetStatus("已应用预设：" + name + "　（" + p.Summary() + "）");
        }

        private void OnClickSavePreset()
        {
            CardFilterPreset p = m_capture != null ? m_capture() : null;
            if (p == null)
            {
                SetStatus("拿不到当前筛选状态，保存失败");
                return;
            }
            if (!p.HasListState() && string.IsNullOrEmpty(p.query))
            {
                SetStatus("当前没有任何筛选条件，没什么可保存");
                return;
            }
            p.name = CardFilterPreset.AutoName(p);     //自动命名：有语法用语法，否则用勾选摘要
            CardFilterPresetIO.Save(p);
            m_current_name = p.name;
            SetStatus("已保存预设：" + p.name + "　（" + p.Summary() + "）");
        }

        private void OnClickDeletePreset()
        {
            string name = GetCurrentPresetName();
            if (CardFilterPresetIO.Remove(name))
                SetStatus("已删除预设：" + name);
            else
                SetStatus("当前条件没有对应的预设");
        }

        private void OnClickExportPreset()
        {
            string name = GetCurrentPresetName();
            string path = CardFilterPresetIO.ExportPreset(name);
            SetStatus(path != null ? ("已导出 → " + path) : "导出失败：先保存成预设再导出");
        }

        private void OnClickImportPreset()
        {
            int n = CardFilterPresetIO.ImportShared();
            SetStatus(n > 0 ? ("已导入 " + n + " 条预设（点「预设 ▾」查看）")
                            : ("没有可导入的预设文件（放到 " + CardFilterPresetIO.ShareFolder + "）"));
        }

        /// <summary>删除/导出用的预设名：优先"最近选中/保存过的"，否则按当前状态算一个（与保存时同规则）</summary>
        private string GetCurrentPresetName()
        {
            if (!string.IsNullOrEmpty(m_current_name) && CardFilterPresetIO.Get(m_current_name) != null)
                return m_current_name;
            CardFilterPreset p = m_capture != null ? m_capture() : null;
            return p != null ? CardFilterPreset.AutoName(p) : "";
        }

        private void SetStatus(string s)
        {
            if (m_status != null)
                m_status.text = s ?? "";
        }

        // ==================== 条件胶囊 ====================

        private void RefreshConditions()
        {
            m_last_query = m_get_query != null ? (m_get_query() ?? "") : "";
            if (m_cond_row == null)
                return;

            for (int i = m_cond_row.childCount - 1; i >= 0; i--)
            {
                Transform c = m_cond_row.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            CardQuery q = CardQuery.Parse(m_last_query);
            if (q.terms.Count == 0)
            {
                TMP_Text none = NewText("None", m_cond_row, "（无：当前按全部卡牌显示）", UITheme.FontBody - 2, TextAlignmentOptions.Left);
                none.color = UITheme.TextDim;
                LayoutElement nle = none.gameObject.AddComponent<LayoutElement>();
                nle.preferredHeight = 26;
                nle.minHeight = 26;
                return;
            }

            for (int i = 0; i < q.terms.Count; i++)
            {
                CardQueryTerm term = q.terms[i];
                RectTransform chip = NewButton("Chip_" + i, m_cond_row, term.raw + "  ×", () => OnRemoveTerm(term));
                LayoutElement le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 26;
                le.minHeight = 26;
                le.preferredWidth = Mathf.Max(64f, term.raw.Length * 15f + 34f);
                chip.GetComponent<Image>().color = UITheme.Ctrl;
            }
        }

        // ==================== 小工具（与 UISelectPopup 同规范） ====================

        private static void SetRect(RectTransform rt, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = amin;
            rt.anchorMax = amax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static TMP_Text NewText(string name, Transform parent, string txt, int size, TextAlignmentOptions align)
        {
            return UIFactory.CreateTmpText(name, parent, txt, size, UITheme.TextBody, align, UIFonts.ResolveFont());
        }

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
            t.overflowMode = TextOverflowModes.Ellipsis;
            UIFactory.SetStretch(t.rectTransform);
            t.raycastTarget = false;

            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            return rt;
        }

        private static void AddRowButton(Transform parent, string label, float width, Action onClick)
        {
            RectTransform rt = NewButton("Btn_" + label, parent, label, onClick);
            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 34;
            le.minHeight = 34;
        }
    }
}
