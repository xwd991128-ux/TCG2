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
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei.ttf";
        private const string FONT_FALLBACK_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";

        private static Font _font;

        // ---------------- 菜单入口 ----------------

        [MenuItem(MENU + "生成卡池管理页面到主菜单场景")]
        public static void BuildCardPoolPanel()
        {
            //强制切换到主菜单场景并在其中生成 + 自动保存，避免选错场景或忘记保存
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            //已存在则先删除，保证幂等可重建
            DestroyIfExists("CardPoolPanel");
            DestroyIfExists("CardPoolBtn");
            DestroyIfExists("TabCardPool");

            Canvas canvas = GetMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("卡池管理", "未找到 Canvas，请确认主菜单场景加载成功。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            CardPoolPanel panel = BuildPanel(canvas.transform);
            BuildMenuTab(canvas.transform, panel);

            EditorSceneManager.SaveScene(scene);
            Debug.Log("卡池管理页面已生成到 Menu.unity 并保存");
            EditorUtility.DisplayDialog("卡池管理", "已在主菜单生成「卡池」导航标签和卡池管理页面，并已自动保存。\n可直接点 Play 测试。", "确定");
        }

        private static void DestroyIfExists(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // ---------------- 页面（透明全屏，与 CollectionPanel 同级） ----------------

        private static CardPoolPanel BuildPanel(Transform canvas)
        {
            //根页面：全屏透明（无背景遮罩），CanvasGroup + CardPoolPanel 组件
            RectTransform root = CreateRect("CardPoolPanel", canvas);
            SetStretch(root);

            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f; //编辑器内可见方便调整；运行时 UIPanel.Awake 会隐藏
            group.interactable = true;
            group.blocksRaycasts = true;

            CardPoolPanel panel = root.gameObject.AddComponent<CardPoolPanel>();

            //标题（放在顶部导航栏下方）
            Text title = CreateText("TitleText", root, "卡池管理", _font, 42,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleCenter);
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
            layout.spacing = 6f;
            layout.padding = new RectOffset(12, 12, 8, 8);
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
            Button export = CreateButton("ExportBtn", line, "导出", _font, 20, new Color(0.5f, 0.78f, 1f, 0.3f));
            RectTransform ex_rt = export.GetComponent<RectTransform>();
            ex_rt.anchorMin = new Vector2(1, 0.5f);
            ex_rt.anchorMax = new Vector2(1, 0.5f);
            ex_rt.pivot = new Vector2(0.5f, 0.5f);
            ex_rt.anchoredPosition = new Vector2(-185, 0);
            ex_rt.sizeDelta = new Vector2(72, 34);

            //删除按钮（右缘 -131 ~ -59，仅本地卡池显示）
            Button del = CreateButton("DeleteBtn", line, "删除", _font, 20, new Color(1f, 0.6f, 0.6f, 0.3f));
            RectTransform del_rt = del.GetComponent<RectTransform>();
            del_rt.anchorMin = new Vector2(1, 0.5f);
            del_rt.anchorMax = new Vector2(1, 0.5f);
            del_rt.pivot = new Vector2(0.5f, 0.5f);
            del_rt.anchoredPosition = new Vector2(-95, 0);
            del_rt.sizeDelta = new Vector2(72, 34);

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
            Text status = CreateText("StatusText", parent, "", _font, 22, new Color(1, 1, 1, 0.75f), TextAnchor.MiddleCenter);
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

        // ---------------- 顶部导航栏「卡池」标签 ----------------

        private static void BuildMenuTab(Transform canvas, CardPoolPanel panel)
        {
            //复制「卡牌」标签的完整样式，保证与导航栏其它标签完全一致
            GameObject tab_collection = GameObject.Find("TabCollection");
            if (tab_collection == null)
            {
                Debug.LogWarning("未找到 TabCollection，跳过导航标签（可手动创建）");
                return;
            }

            GameObject tab = Object.Instantiate(tab_collection, tab_collection.transform.parent, false);
            tab.name = "TabCardPool";
            tab.SetActive(true);

            //放在「卡牌」标签右侧（用户可在 Inspector 中拖动调整）
            RectTransform rt = tab.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(1711, rt.anchoredPosition.y);

            //切换目标页面 → 卡池管理页面
            TabButton tab_btn = tab.GetComponent<TabButton>();
            if (tab_btn != null)
            {
                tab_btn.group = "menu";
                tab_btn.active = false;
                tab_btn.ui_panel = panel;
            }

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

            //调整标签在导航栏中的顺序（紧跟「卡牌」）
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

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static Text CreateText(string name, Transform parent, string text, Font font, int size, Color color, TextAnchor align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            Text txt = go.GetComponent<Text>();
            txt.text = text;
            txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = FontStyle.Normal;
            txt.alignment = align;
            txt.color = color;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        private static Button CreateButton(string name, Transform parent, string label, Font font, int size, Color bg_color)
        {
            Image img = CreateImage(name, parent, bg_color);
            Button btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.fadeDuration = 0.1f;
            btn.colors = colors;

            Text txt = CreateText("Text", img.transform, label, font, size, Color.white, TextAnchor.MiddleCenter);
            SetStretch(txt.rectTransform);
            return btn;
        }
    }
}
