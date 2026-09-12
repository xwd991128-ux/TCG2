using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡池管理页面搭建工具（编辑器菜单，非运行时）。
    /// 一键在 Menu.unity 场景中生成：
    ///   1. CardPoolPanel 全屏页面（与卡牌管理 CollectionPanel 同级，透明背景，无黑色遮罩）
    ///   2. 顶部导航栏「卡池」标签（复制「卡牌」标签样式，group=menu，点击切换到卡池页面）
    /// 生成后保存在场景中，可在 Inspector 中自由调整。
    /// </summary>
    public static class CardPoolPanelBuilder
    {
        private const string MENU = "TcgEngine/卡池管理/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        //优先用黑体 SimHei（标准 TTF 中文字体，Unity 可直接导入，中文清晰不糊）
        //资源路径统一登记在 UITheme（全项目只定义一次）
        private const string FONT_PATH = UITheme.FontPath;
        private const string FONT_FALLBACK_PATH = UITheme.FontFallbackPath;

        private static Font _font;

        // ---------------- 菜单入口 ----------------

        [MenuItem(MENU + "生成卡池管理页面到主菜单场景")]
        public static void BuildCardPoolPanel()
        {
            //强制切换到主菜单场景并在其中生成 + 自动保存，避免选错场景或忘记保存
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            //幂等清理：只删历史遗留的旧入口按钮 CardPoolBtn。
            //注意：绝不能再删除 CardPoolPanel / TabCardPool 本身——首页模块按钮 ModCardPool 与导航标签
            //TabCardPool 都直接引用面板组件；删除重建会让这些引用变成空引用（serialize 成 fileID:0），
            //点击「卡池」时会「隐藏所有页面却不显示任何页面」，表现为整屏黑色无内容。
            //现改为：复用面板根 + 重建内容 + 重新绑定引用。
            DestroyIfExists("CardPoolBtn");

            Canvas canvas = GetMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("卡池管理", "未找到 Canvas，请确认主菜单场景加载成功。", "确定");
                return;
            }

            ResolveFont();

            CardPoolPanel panel = BuildPanel(canvas.transform);
            int fixed_refs = RebindLostReferences(panel);
            BuildMenuTab(canvas.transform, panel);

            EditorSceneManager.SaveScene(scene);
            Debug.Log("卡池管理页面已生成到 Menu.unity 并保存" + (fixed_refs > 0 ? "（并修复失效引用 " + fixed_refs + " 处）" : ""));
            EditorUtility.DisplayDialog("卡池管理",
                "已在主菜单生成「卡池」导航标签和卡池管理页面，并已自动保存。\n" +
                (fixed_refs > 0 ? "已修复失效页面引用 " + fixed_refs + " 处（此前会导致点「卡池」黑屏）。\n" : "") +
                "可直接点 Play 测试。", "确定");
        }

        /// <summary>按名查找场景对象（含未激活对象、以及处于未激活父级下的对象）</summary>
        private static GameObject FindIncludingInactive(string name)
        {
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go != null && go.name == name)
                    return go;
            }
            return null;
        }

        /// <summary>
        /// 删除同名对象（含未激活）。原实现用 GameObject.Find 只能找到**激活**对象：
        /// 主菜单 TopBar 被「主菜单重构」隐藏后，其下所有导航标签都处于未激活层级，
        /// 于是旧 TabCardPool 永远删不掉、也不会被重建，引用就此悬空。
        /// </summary>
        private static void DestroyIfExists(string name)
        {
            //先收集再销毁：销毁过程中数组内其它元素可能失效
            List<GameObject> targets = new List<GameObject>();
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go != null && go.name == name)
                    targets.Add(go);
            }
            foreach (GameObject go in targets)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 修复历史遗留的失效引用：首页「卡池」模块按钮（ModCardPool）与导航标签（TabCardPool）
        /// 的 ui_panel 曾指向被删除的旧面板，序列化后变成空引用（fileID:0）。
        /// 空引用会让点击时「隐藏所有页面却不显示任何页面」→ 黑屏无内容。这里按名字重新绑定到当前面板。
        /// </summary>
        private static int RebindLostReferences(CardPoolPanel panel)
        {
            int count = 0;
            foreach (TabButton tb in Object.FindObjectsOfType<TabButton>(true))
            {
                if (tb == null || tb.ui_panel != null)
                    continue;
                string n = tb.gameObject.name;
                if (n == "ModCardPool" || n == "TabCardPool")
                {
                    tb.ui_panel = panel;
                    EditorUtility.SetDirty(tb);
                    count++;
                }
            }
            return count;
        }

        // ---------------- 页面（透明全屏，与 CollectionPanel 同级） ----------------

        private static CardPoolPanel BuildPanel(Transform canvas)
        {
            //根页面：全屏透明（无背景遮罩），CanvasGroup + CardPoolPanel 组件。
            //复用已存在的页面根（含未激活）：保留 GameObject 与 CardPoolPanel 组件实例，
            //使外部引用（ModCardPool / TabCardPool 的 ui_panel）始终有效——这是「点卡池黑屏」的根治点。
            GameObject existing = FindIncludingInactive("CardPoolPanel");
            RectTransform root = existing != null ? existing.transform as RectTransform : null;
            if (root == null)
            {
                root = CreateRect("CardPoolPanel", canvas);
            }
            else
            {
                //清空旧内容（标题/列表/模板/工具栏），随后按默认布局重建
                for (int i = root.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.GetChild(i).gameObject);
                root.SetParent(canvas, false);
            }
            SetStretch(root);
            root.SetAsLastSibling();   //与「新建后追加到末尾」的层级一致，确保盖在其它页面之上
            root.gameObject.SetActive(true);

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null) group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f; //编辑器内可见方便调整；运行时 UIPanel.Awake 会隐藏
            group.interactable = true;
            group.blocksRaycasts = true;

            CardPoolPanel panel = root.GetComponent<CardPoolPanel>();
            if (panel == null) panel = root.gameObject.AddComponent<CardPoolPanel>();

            //清掉可能指向已删除子物体的旧引用，稍后由各 Build 方法重新赋值
            panel.scroll_rect = null;
            panel.scroll_content = null;
            panel.line_template = null;
            panel.title_text = null;
            panel.close_btn = null;
            panel.select_all_btn = null;
            panel.select_none_btn = null;
            panel.import_btn = null;
            panel.export_btn = null;
            panel.status_text = null;

            //标题（放在顶部导航栏下方）：字号/颜色统一走 UITheme 令牌（P3）
            Text title = CreateText("TitleText", root, "卡池管理", _font, UITheme.FontPageTitle,
                UITheme.TextTitle, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.anchoredPosition = new Vector2(0, -200);
            title.rectTransform.sizeDelta = new Vector2(500, 60);

            //列表区域（标题下方、工具栏上方），浅色半透明面板
            ScrollRect scroll = CreateScrollRect(root, panel);

            //底部工具栏
            BuildToolbar(root, panel);

            panel.title_text = title;
            panel.close_btn = null; //由导航栏标签切换，不需要关闭按钮

            EditorUtility.SetDirty(panel);
            return panel;
        }

        private static ScrollRect CreateScrollRect(Transform parent, CardPoolPanel panel)
        {
            RectTransform scroll_rt = CreateRect("ListScroll", parent);
            scroll_rt.anchorMin = new Vector2(0.12f, 0.14f);
            scroll_rt.anchorMax = new Vector2(0.88f, 0.76f);
            scroll_rt.offsetMin = Vector2.zero;
            scroll_rt.offsetMax = Vector2.zero;
            scroll_rt.pivot = new Vector2(0.5f, 0.5f);
            scroll_rt.sizeDelta = Vector2.zero;

            //列表底面板（浅色，区别于黑色遮罩）
            Image bg = scroll_rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            ScrollRect scroll = scroll_rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            //视口
            RectTransform viewport = CreateRect("Viewport", scroll_rt);
            SetStretch(viewport);
            Image view_image = viewport.gameObject.AddComponent<Image>();
            view_image.color = new Color(1, 1, 1, 0.05f);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            //内容（自动布局）
            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 400);

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.GapSm;
            layout.padding = UITheme.PadList();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            //行模板（隐藏）
            GameObject line = CreatePoolLineTemplate(content);
            line.SetActive(false);

            panel.scroll_rect = scroll;
            panel.scroll_content = content;
            panel.line_template = line;

            return scroll;
        }

        private static GameObject CreatePoolLineTemplate(Transform content)
        {
            RectTransform line = CreateRect("LineTemplate", content);
            line.anchorMin = new Vector2(0, 1);
            line.anchorMax = new Vector2(1, 1);
            line.pivot = new Vector2(0.5f, 1);
            line.anchoredPosition = Vector2.zero;
            line.sizeDelta = new Vector2(0, 50);

            Image bg = CreateImage("LineBG", line, new Color(1, 1, 1, 0.16f));
            SetStretch(bg.rectTransform);

            //勾选框
            RectTransform toggle_rt = CreateRect("Toggle", line);
            toggle_rt.anchorMin = new Vector2(0, 0.5f);
            toggle_rt.anchorMax = new Vector2(0, 0.5f);
            toggle_rt.pivot = new Vector2(0.5f, 0.5f);
            toggle_rt.anchoredPosition = new Vector2(34, 0);
            toggle_rt.sizeDelta = new Vector2(30, 30);

            Image check_bg = toggle_rt.gameObject.AddComponent<Image>();
            check_bg.color = new Color(1, 1, 1, 0.35f);
            Toggle toggle = toggle_rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = check_bg;

            Text check_mark = CreateText("Checkmark", toggle_rt, "\u2714", _font, 22, Color.white, TextAnchor.MiddleCenter);
            SetStretch(check_mark.rectTransform);
            toggle.graphic = check_mark;
            toggle.isOn = false;

            //名称（右侧留出数量+按钮区域，避免文字压在按钮上）
            Text name = CreateText("NameText", line, "卡池名称", _font, 24, Color.white, TextAnchor.MiddleLeft);
            name.rectTransform.anchorMin = new Vector2(0, 0);
            name.rectTransform.anchorMax = new Vector2(1, 1);
            name.rectTransform.offsetMin = new Vector2(64, 0);
            name.rectTransform.offsetMax = new Vector2(-530, 0);

            //数量（右缘 -340 ~ -230，宽 110）
            Text count = CreateText("CountText", line, "0 张", _font, 22, new Color(1, 1, 1, 0.75f), TextAnchor.MiddleRight);
            count.rectTransform.anchorMin = new Vector2(0, 0);
            count.rectTransform.anchorMax = new Vector2(1, 1);
            count.rectTransform.offsetMin = new Vector2(-340, 0);
            count.rectTransform.offsetMax = new Vector2(-230, 0);

            //导出按钮（右缘 -221 ~ -149）
            Button export = CreateButton("ExportBtn", line, "导出", _font, UITheme.FontButton, new Color(0.5f, 0.78f, 1f, 0.3f));
            RectTransform ex_rt = export.GetComponent<RectTransform>();
            ex_rt.anchorMin = new Vector2(1, 0.5f);
            ex_rt.anchorMax = new Vector2(1, 0.5f);
            ex_rt.pivot = new Vector2(0.5f, 0.5f);
            ex_rt.anchoredPosition = new Vector2(-185, 0);
            ex_rt.sizeDelta = new Vector2(72, 34);

            //删除按钮（右缘 -131 ~ -59，仅本地卡池显示）
            Button del = CreateButton("DeleteBtn", line, "删除", _font, UITheme.FontButton, new Color(1f, 0.6f, 0.6f, 0.3f));
            RectTransform del_rt = del.GetComponent<RectTransform>();
            del_rt.anchorMin = new Vector2(1, 0.5f);
            del_rt.anchorMax = new Vector2(1, 0.5f);
            del_rt.pivot = new Vector2(0.5f, 0.5f);
            del_rt.anchoredPosition = new Vector2(-95, 0);
            del_rt.sizeDelta = new Vector2(72, 34);

            //编辑按钮（进入卡牌编辑器修改该卡池；内置卡池由运行时自动隐藏）
            EnsureLineEditButton(line.gameObject);

            return line.gameObject;
        }

        private static void BuildToolbar(Transform parent, CardPoolPanel panel)
        {
            RectTransform bar = CreateRect("BottomBar", parent);
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.anchoredPosition = new Vector2(0, 22);
            bar.sizeDelta = new Vector2(0, 66);

            panel.select_all_btn = CreateToolbarButton(bar, "SelectAllBtn", "全选", -300);
            panel.select_none_btn = CreateToolbarButton(bar, "SelectNoneBtn", "全不选", -180);
            panel.import_btn = CreateToolbarButton(bar, "ImportBtn", "导入", 180, new Color(0.5f, 0.78f, 1f, 0.4f));
            panel.export_btn = CreateToolbarButton(bar, "ExportAllBtn", "导出选中", 300, new Color(0.6f, 0.9f, 0.6f, 0.4f));

            //状态提示
            Text status = CreateText("StatusText", parent, "", _font, UITheme.FontStatus, UITheme.TextDim, TextAnchor.MiddleCenter);
            status.rectTransform.anchorMin = new Vector2(0, 0);
            status.rectTransform.anchorMax = new Vector2(1, 0);
            status.rectTransform.pivot = new Vector2(0.5f, 0);
            status.rectTransform.anchoredPosition = new Vector2(0, 90);
            status.rectTransform.sizeDelta = new Vector2(0, 40);
            panel.status_text = status;
        }

        private static Button CreateToolbarButton(RectTransform bar, string name, string label, float x, Color? color = null)
        {
            Button btn = CreateButton(name, bar, label, _font, 22, color ?? new Color(1, 1, 1, 0.25f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(96, 50);
            return btn;
        }

        /// <summary>
        /// 确保卡池行上有「编辑」按钮（点击进入卡牌编辑器修改该卡池）。
        /// 行模板由本工具生成，必须自带编辑按钮：否则单独重跑「生成卡池管理页面」后按钮会丢失
        /// （此前依赖「生成卡牌编辑器页面」额外补加），用户无法进入编辑功能。
        /// 「生成卡牌编辑器页面」也复用本方法，按钮定义只保留这一处。
        /// </summary>
        public static Button EnsureLineEditButton(GameObject line)
        {
            if (line == null)
                return null;

            Transform exist = FindChild(line.transform, "EditBtn");
            if (exist != null)
                return exist.GetComponent<Button>();

            Button btn = CreateButton("EditBtn", line.transform, "编辑", ResolveFont(), UITheme.FontButton, new Color(1f, 0.85f, 0.6f, 0.3f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            //与数量文本(-340~-230)、导出(-221~-149)、删除(-131~-59)错开：放在 -440~-350，中心 -395
            rt.anchoredPosition = new Vector2(-395, 0);
            rt.sizeDelta = new Vector2(86, 34);
            return btn;
        }

        /// <summary>解析页面字体（本工具入口之外被调用时，_font 可能尚未初始化）</summary>
        private static Font ResolveFont()
        {
            if (_font != null)
                return _font;
            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                Transform deep = FindChild(child, name);
                if (deep != null)
                    return deep;
            }
            return null;
        }

        // ---------------- 顶部导航栏「卡池」标签 ----------------

        private static void BuildMenuTab(Transform canvas, CardPoolPanel panel)
        {
            //优先复用已存在的「卡池」标签（它可能处于未激活的 TopBar 下，必须用 includeInactive 查找），
            //不存在才从「卡牌」标签复制一份。这样重建页面时不会产生重复标签，也能修复历史空引用。
            GameObject tab = FindIncludingInactive("TabCardPool");
            GameObject tab_collection = FindIncludingInactive("TabCollection");

            if (tab == null)
            {
                if (tab_collection == null)
                {
                    Debug.LogWarning("未找到 TabCollection，跳过导航标签（可手动创建）");
                    return;
                }

                //复制「卡牌」标签的完整样式，保证与导航栏其它标签完全一致
                tab = Object.Instantiate(tab_collection, tab_collection.transform.parent, false);
                tab.name = "TabCardPool";

                //放在「卡牌」标签右侧（用户可在 Inspector 中拖动调整）
                RectTransform rt = tab.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(1711, rt.anchoredPosition.y);

                //标签文字「卡牌」→「卡池」
                Text[] texts = tab.GetComponentsInChildren<Text>(true);
                foreach (Text t in texts)
                {
                    if (!string.IsNullOrEmpty(t.text))
                    {
                        t.text = "卡池";
                        break;
                    }
                }
            }

            tab.SetActive(true);

            //切换目标页面 → 卡池管理页面（始终重绑，修复历史空引用）
            TabButton tab_btn = tab.GetComponent<TabButton>();
            if (tab_btn != null)
            {
                tab_btn.group = "menu";
                tab_btn.active = false;
                tab_btn.ui_panel = panel;
                EditorUtility.SetDirty(tab_btn);
            }

            //调整标签在导航栏中的顺序（紧跟「卡牌」）
            if (tab_collection != null)
                tab.transform.SetSiblingIndex(tab_collection.transform.GetSiblingIndex() + 1);
        }

        // ---------------- UI 辅助 ----------------

        private static Canvas GetMainCanvas()
        {
            Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
            foreach (Canvas c in canvases)
            {
                if (c.transform.parent == null || c.GetComponentInParent<Canvas>() == null)
                    return c;
            }
            return canvases.Length > 0 ? canvases[0] : null;
        }

        // ---------------- 通用控件工厂：统一转发到 UIFactory（等价搬迁，调用点零改动） ----------------

        private static RectTransform CreateRect(string name, Transform parent)
            => UIFactory.CreateRect(name, parent);

        private static void SetStretch(RectTransform rt)
            => UIFactory.SetStretch(rt);

        private static Image CreateImage(string name, Transform parent, Color color)
            => UIFactory.CreateImage(name, parent, color);

        private static Text CreateText(string name, Transform parent, string text, Font font, int size, Color color, TextAnchor align)
            => UIFactory.CreateText(name, parent, text, font, size, color, align);

        private static Button CreateButton(string name, Transform parent, string label, Font font, int size, Color bg_color)
            => UIFactory.CreateButton(name, parent, label, font, size, bg_color);
    }
}
