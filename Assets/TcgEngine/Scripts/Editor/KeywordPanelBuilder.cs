using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine;
using TcgEngine.UI;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 生成关键词管理页面到主菜单场景：全屏面板 = 顶栏(新建/保存/关闭) + 左侧关键词列表 + 右侧属性与规则图列表。
    /// 与「生成规则编辑器页面」同一模式：UI 构建进场景，运行时由 KeywordPanel 驱动。
    /// </summary>
    public static class KeywordPanelBuilder
    {
        private const string MENU = "TcgEngine/卡牌编辑器/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei.ttf";
        private const string FONT_FALLBACK_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";
        private const string EXIT_ICON_PATH = "Assets/TcgEngine/Sprites/UI/exit.png";

        private static Font _font;

        [MenuItem(MENU + "生成关键词管理页面到主菜单场景")]
        public static void BuildKeywordPanel()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            Transform existing = FindTransform("KeywordPanel");
            if (existing != null)
            {
                bool rebuild = !EditorUtility.DisplayDialog("关键词管理",
                    "场景中已存在关键词管理页面（含你手动调整的布局）。\n\n" +
                    "「保留现有布局」：跳过重建；\n" +
                    "「重建（默认布局）」：删除旧页面重新生成。",
                    "保留现有布局", "重建（默认布局）");
                if (!rebuild)
                    return;
                Object.DestroyImmediate(existing.gameObject);
            }

            Canvas canvas = GetMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("关键词管理", "未找到 Canvas，请确认主菜单场景加载成功。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildPanel(canvas.transform);

            EditorSceneManager.SaveScene(scene);
            Debug.Log("关键词管理页面已生成到 Menu.unity 并保存（运行时从任意入口 Show 即可打开）");
            EditorUtility.DisplayDialog("关键词管理",
                "已生成关键词管理页面。\n运行游戏后通过代码打开：FindObjectOfType<KeywordPanel>(true).Show();\n（可自行把入口按钮接到卡牌编辑器或主菜单）", "确定");
        }

        // ---------------- 页面构建 ----------------

        private static void BuildPanel(Transform canvas)
        {
            RectTransform root = CreateRect("KeywordPanel", canvas);
            SetStretch(root);

            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            KeywordPanel panel = root.gameObject.AddComponent<KeywordPanel>();

            Image bg = CreateImage("Background", root, new Color(0f, 0f, 0f, 0.92f));
            bg.raycastTarget = false;
            SetStretch(bg.rectTransform);

            BuildTopBar(root, panel);
            BuildListArea(root, panel);
            BuildFormArea(root, panel);

            Text status = CreateText("StatusText", root, "", _font, 20, new Color(1, 0.85f, 0.6f, 1f), TextAnchor.MiddleRight);
            status.rectTransform.anchorMin = new Vector2(1, 0);
            status.rectTransform.anchorMax = new Vector2(1, 0);
            status.rectTransform.pivot = new Vector2(1, 0.5f);
            status.rectTransform.anchoredPosition = new Vector2(-24, 24);
            status.rectTransform.sizeDelta = new Vector2(520, 36);
            panel.status_text = status;

            root.gameObject.SetActive(true);
        }

        private static void BuildTopBar(Transform parent, KeywordPanel panel)
        {
            RectTransform bar = CreateRect("TopBar", parent);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0, 70);

            Image bar_img = bar.gameObject.AddComponent<Image>();
            bar_img.color = new Color(0.08f, 0.08f, 0.1f, 1f);

            Text title = CreateText("Title", bar, "关键词管理", _font, 28, Color.white, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0, 0.5f);
            title.rectTransform.anchorMax = new Vector2(0, 0.5f);
            title.rectTransform.pivot = new Vector2(0, 0.5f);
            title.rectTransform.anchoredPosition = new Vector2(24, 0);
            title.rectTransform.sizeDelta = new Vector2(300, 40);

            Button new_btn = CreateButton("NewBtn", bar, "新建", _font, 20, new Color(0.25f, 0.45f, 0.7f, 1f));
            AnchorRect(new_btn.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(-220, 0), new Vector2(90, 44));
            panel.btn_new = new_btn;

            Button save_btn = CreateButton("SaveBtn", bar, "保存", _font, 20, new Color(0.2f, 0.55f, 0.3f, 1f));
            AnchorRect(save_btn.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(90, 44));
            panel.btn_save = save_btn;

            Button close_btn = CreateButton("CloseBtn", bar, "×", _font, 24, new Color(0.7f, 0.25f, 0.25f, 1f));
            AnchorRect(close_btn.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(70, 44));
            panel.btn_close = close_btn;
        }

        private static void BuildListArea(Transform parent, KeywordPanel panel)
        {
            RectTransform area = CreateRect("ListArea", parent);
            area.anchorMin = new Vector2(0, 0);
            area.anchorMax = new Vector2(0, 1);
            area.pivot = new Vector2(0, 0.5f);
            area.anchoredPosition = new Vector2(12, -35);
            area.sizeDelta = new Vector2(320, -105);

            Text header = CreateText("Header", area, "关键词列表", _font, 20, new Color(0.8f, 0.8f, 0.85f, 1f), TextAnchor.MiddleLeft);
            header.rectTransform.anchorMin = new Vector2(0, 1);
            header.rectTransform.anchorMax = new Vector2(1, 1);
            header.rectTransform.pivot = new Vector2(0, 1);
            header.rectTransform.anchoredPosition = Vector2.zero;
            header.rectTransform.sizeDelta = new Vector2(0, 30);

            //滚动列表
            RectTransform scroll = CreateRect("Scroll", area);
            scroll.anchorMin = new Vector2(0, 0);
            scroll.anchorMax = new Vector2(1, 1);
            scroll.offsetMin = Vector2.zero;
            scroll.offsetMax = new Vector2(0, -36);

            Image scroll_img = scroll.gameObject.AddComponent<Image>();
            scroll_img.color = new Color(1, 1, 1, 0.05f);
            ScrollRect scroll_rect = scroll.gameObject.AddComponent<ScrollRect>();
            scroll_rect.horizontal = false;
            scroll_rect.movementType = ScrollRect.MovementType.Clamped;
            scroll_rect.scrollSensitivity = 2f;

            RectTransform viewport = CreateRect("Viewport", scroll);
            SetStretch(viewport);
            Mask vmask = viewport.gameObject.AddComponent<Mask>();
            vmask.showMaskGraphic = false;
            Image vimg = viewport.gameObject.GetComponent<Image>();
            if (vimg == null) vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1, 1, 1, 0.02f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 4;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll_rect.viewport = viewport;
            scroll_rect.content = content;
            panel.list_content = content;

            //行模板：Button+Text
            GameObject template = CreateButton("RowTemplate", content, "", _font, 18, new Color(1, 1, 1, 0.12f)).gameObject;
            LayoutElement le = template.AddComponent<LayoutElement>();
            le.preferredHeight = 40;
            template.SetActive(false);
            panel.row_template = template;
        }

        private static void BuildFormArea(Transform parent, KeywordPanel panel)
        {
            RectTransform area = CreateRect("FormArea", parent);
            area.anchorMin = new Vector2(0, 0);
            area.anchorMax = new Vector2(1, 0);
            area.pivot = new Vector2(0, 0);
            area.anchoredPosition = new Vector2(348, 40);
            area.sizeDelta = new Vector2(-390, -130);

            //标题行
            Text title_label = CreateText("TitleLabel", area, "标题", _font, 18, new Color(0.8f, 0.8f, 0.85f, 1f), TextAnchor.MiddleLeft);
            title_label.rectTransform.anchorMin = new Vector2(0, 1);
            title_label.rectTransform.anchorMax = new Vector2(0, 1);
            title_label.rectTransform.pivot = new Vector2(0, 1);
            title_label.rectTransform.anchoredPosition = new Vector2(0, -8);
            title_label.rectTransform.sizeDelta = new Vector2(80, 26);

            RectTransform title_field = CreateRect("TitleField", area);
            title_field.anchorMin = new Vector2(0, 1);
            title_field.anchorMax = new Vector2(1, 1);
            title_field.pivot = new Vector2(0, 1);
            title_field.anchoredPosition = new Vector2(90, -4);
            title_field.sizeDelta = new Vector2(-90, 34);
            panel.input_title = CreateInputIn(title_field, "输入关键词标题");

            //说明行
            Text desc_label = CreateText("DescLabel", area, "说明", _font, 18, new Color(0.8f, 0.8f, 0.85f, 1f), TextAnchor.UpperLeft);
            desc_label.rectTransform.anchorMin = new Vector2(0, 1);
            desc_label.rectTransform.anchorMax = new Vector2(0, 1);
            desc_label.rectTransform.pivot = new Vector2(0, 1);
            desc_label.rectTransform.anchoredPosition = new Vector2(0, -56);
            desc_label.rectTransform.sizeDelta = new Vector2(80, 26);

            RectTransform desc_field = CreateRect("DescField", area);
            desc_field.anchorMin = new Vector2(0, 1);
            desc_field.anchorMax = new Vector2(1, 1);
            desc_field.pivot = new Vector2(0, 1);
            desc_field.anchoredPosition = new Vector2(90, -44);
            desc_field.sizeDelta = new Vector2(-90, 76);
            panel.input_desc = CreateInputIn(desc_field, "输入关键词说明（悬浮预览显示）", true);

            //原生状态行
            Text status_label = CreateText("StatusLabel", area, "原生机制", _font, 18, new Color(0.8f, 0.8f, 0.85f, 1f), TextAnchor.MiddleLeft);
            status_label.rectTransform.anchorMin = new Vector2(0, 1);
            status_label.rectTransform.anchorMax = new Vector2(0, 1);
            status_label.rectTransform.pivot = new Vector2(0, 1);
            status_label.rectTransform.anchoredPosition = new Vector2(0, -132);
            status_label.rectTransform.sizeDelta = new Vector2(80, 26);

            RectTransform status_field = CreateRect("StatusField", area);
            status_field.anchorMin = new Vector2(0, 1);
            status_field.anchorMax = new Vector2(0.55f, 1);
            status_field.pivot = new Vector2(0, 1);
            status_field.anchoredPosition = new Vector2(90, -128);
            status_field.sizeDelta = new Vector2(0, 34);
            panel.dropdown_status = CreateDropdownIn(status_field, KeywordPanel.GetStatusOptionNames());

            //规则图区
            Text rules_label = CreateText("RulesLabel", area, "自定义规则图（触发时机 → 图）", _font, 18, new Color(0.8f, 0.8f, 0.85f, 1f), TextAnchor.MiddleLeft);
            rules_label.rectTransform.anchorMin = new Vector2(0, 1);
            rules_label.rectTransform.anchorMax = new Vector2(1, 1);
            rules_label.rectTransform.pivot = new Vector2(0, 1);
            rules_label.rectTransform.anchoredPosition = new Vector2(0, -178);
            rules_label.rectTransform.sizeDelta = new Vector2(0, 26);

            RectTransform rules_scroll = CreateRect("RulesScroll", area);
            rules_scroll.anchorMin = new Vector2(0, 0);
            rules_scroll.anchorMax = new Vector2(1, 1);
            rules_scroll.offsetMin = new Vector2(0, 50);
            rules_scroll.offsetMax = new Vector2(0, -210);

            Image rs_img = rules_scroll.gameObject.AddComponent<Image>();
            rs_img.color = new Color(1, 1, 1, 0.04f);
            ScrollRect scroll_rect = rules_scroll.gameObject.AddComponent<ScrollRect>();
            scroll_rect.horizontal = false;
            scroll_rect.movementType = ScrollRect.MovementType.Clamped;
            scroll_rect.scrollSensitivity = 2f;

            RectTransform viewport = CreateRect("Viewport", rules_scroll);
            SetStretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 4;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll_rect.viewport = viewport;
            scroll_rect.content = content;
            panel.rules_content = content;

            //规则行模板：InputField(时机) + EditBtn(编辑图) + DelBtn(删除)
            GameObject row = CreateRect("RuleTemplate", content).gameObject;
            row.AddComponent<Image>().color = new Color(1, 1, 1, 0.08f);
            LayoutElement row_le = row.AddComponent<LayoutElement>();
            row_le.preferredHeight = 40;

            RectTransform trigger_field = CreateRect("TriggerField", row.transform);
            trigger_field.anchorMin = new Vector2(0, 0.5f);
            trigger_field.anchorMax = new Vector2(0.6f, 0.5f);
            trigger_field.pivot = new Vector2(0, 0.5f);
            trigger_field.anchoredPosition = new Vector2(8, 0);
            trigger_field.sizeDelta = new Vector2(0, 30);
            CreateInputIn(trigger_field, "触发时机（OnPlay/StartOfTurn/OnDeath…）");

            Button edit_btn = CreateButton("EditBtn", row.transform, "编辑图", _font, 16, new Color(0.25f, 0.45f, 0.7f, 1f));
            AnchorRect(edit_btn.GetComponent<RectTransform>(), new Vector2(0.62f, 0.5f), new Vector2(0, 0), new Vector2(90, 30));

            Button del_btn = CreateButton("DelBtn", row.transform, "删除", _font, 16, new Color(0.7f, 0.25f, 0.25f, 1f));
            AnchorRect(del_btn.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(60, 30));

            row.SetActive(false);
            panel.rule_template = row;

            //添加规则按钮
            Button add_btn = CreateButton("AddRuleBtn", area, "+ 添加规则", _font, 18, new Color(0.25f, 0.45f, 0.7f, 1f));
            AnchorRect(add_btn.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0, 0), new Vector2(160, 40));
            panel.btn_add_rule = add_btn;
        }

        // ---------------- 通用构建辅助（与 GraphEditorBuilder 同款） ----------------

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AnchorRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
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
            txt.alignment = align;
            txt.color = color;
            txt.raycastTarget = false;
            return txt;
        }

        private static Button CreateButton(string name, Transform parent, string label, Font font, int size, Color bg_color)
        {
            Image img = CreateImage(name, parent, bg_color);
            Button btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock colors = btn.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.fadeDuration = 0.1f;
            btn.colors = colors;

            if (name.Contains("CloseBtn"))
            {
                Sprite exit_sprite = AssetDatabase.LoadAssetAtPath<Sprite>(EXIT_ICON_PATH);
                if (exit_sprite != null)
                {
                    img.sprite = exit_sprite;
                    img.type = Image.Type.Simple;
                    img.color = Color.white;
                }
            }
            else
            {
                Text txt = CreateText("Text", img.transform, label, font, size, Color.white, TextAnchor.MiddleCenter);
                SetStretch(txt.rectTransform);
            }
            return btn;
        }

        private static InputField CreateInputIn(RectTransform field, string placeholder, bool multiline = false)
        {
            RectTransform rt = CreateRect("Input", field);
            SetStretch(rt);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.25f);

            Text placeholder_txt = CreateText("Placeholder", rt, placeholder, _font, 16, new Color(1, 1, 1, 0.5f), multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            RectTransform ph_rt = placeholder_txt.rectTransform;
            ph_rt.anchorMin = Vector2.zero;
            ph_rt.anchorMax = Vector2.one;
            ph_rt.offsetMin = new Vector2(10, 4);
            ph_rt.offsetMax = Vector2.zero;

            Text display = CreateText("Text", rt, "", _font, 16, Color.white, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            RectTransform d_rt = display.rectTransform;
            d_rt.anchorMin = Vector2.zero;
            d_rt.anchorMax = Vector2.one;
            d_rt.offsetMin = new Vector2(10, 4);
            d_rt.offsetMax = Vector2.zero;

            InputField input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = display;
            input.placeholder = placeholder_txt;
            if (multiline)
            {
                input.lineType = InputField.LineType.MultiLineNewline;
                display.horizontalOverflow = HorizontalWrapMode.Wrap;
                display.verticalOverflow = VerticalWrapMode.Truncate;
            }
            return input;
        }

        private static Dropdown CreateDropdownIn(RectTransform field, List<string> options)
        {
            GameObject go = new GameObject("StatusDropdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(field, false);
            SetStretch(go.GetComponent<RectTransform>());

            Image img = go.GetComponent<Image>();
            img.color = new Color(1, 1, 1, 0.25f);
            Dropdown dd = go.GetComponent<Dropdown>();
            dd.targetGraphic = img;

            Text caption = CreateText("Label", go.transform, options.Count > 0 ? options[0] : "", _font, 16, Color.white, TextAnchor.MiddleLeft);
            RectTransform caption_rt = caption.rectTransform;
            caption_rt.anchorMin = Vector2.zero;
            caption_rt.anchorMax = Vector2.one;
            caption_rt.offsetMin = new Vector2(12, 0);
            caption_rt.offsetMax = new Vector2(-12, 0);
            dd.captionText = caption;

            dd.ClearOptions();
            dd.AddOptions(options);
            SetupDropdownTemplate(dd);
            return dd;
        }

        private static void SetupDropdownTemplate(Dropdown dd)
        {
            RectTransform dd_rt = dd.GetComponent<RectTransform>();
            RectTransform template = CreateRect("Template", dd_rt);
            template.anchorMin = new Vector2(0, 0);
            template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(0.5f, 1);
            template.anchoredPosition = Vector2.zero;
            template.sizeDelta = new Vector2(0, 160);
            template.gameObject.SetActive(false);

            Image template_img = template.gameObject.AddComponent<Image>();
            template_img.color = new Color(0.1f, 0.1f, 0.12f, 1f);

            ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewport = CreateRect("Viewport", template);
            SetStretch(viewport);
            viewport.offsetMin = new Vector2(2, 2);
            viewport.offsetMax = new Vector2(-2, -2);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, 28);

            RectTransform item = CreateRect("Item", content);
            item.anchorMin = new Vector2(0, 0.5f);
            item.anchorMax = new Vector2(1, 0.5f);
            item.anchoredPosition = new Vector2(0, -14);
            item.sizeDelta = new Vector2(0, 28);

            RectTransform item_bg = CreateRect("Item Background", item);
            SetStretch(item_bg);
            Image item_bg_img = item_bg.gameObject.AddComponent<Image>();
            item_bg_img.color = new Color(1, 1, 1, 0.1f);

            Toggle item_toggle = item.gameObject.AddComponent<Toggle>();
            item_toggle.targetGraphic = item_bg_img;

            RectTransform item_label = CreateRect("Item Label", item);
            item_label.anchorMin = Vector2.zero;
            item_label.anchorMax = Vector2.one;
            item_label.offsetMin = new Vector2(16, 0);
            item_label.offsetMax = new Vector2(-10, 0);
            Text item_text = item_label.gameObject.AddComponent<Text>();
            item_text.font = _font;
            item_text.fontSize = 16;
            item_text.color = Color.white;
            item_text.alignment = TextAnchor.MiddleLeft;

            scroll.viewport = viewport;
            scroll.content = content;
            dd.template = template;
            dd.itemText = item_text;
        }

        private static Canvas GetMainCanvas()
        {
            Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
            foreach (Canvas c in canvases)
            {
                if (c.transform.parent == null)
                    return c;
            }
            return canvases.Length > 0 ? canvases[0] : null;
        }

        private static Transform FindTransform(string name)
        {
            GameObject[] all = Object.FindObjectsOfType<GameObject>(true);
            foreach (GameObject go in all)
            {
                if (go != null && go.name == name)
                    return go.transform;
            }
            return null;
        }
    }
}
