using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡牌构筑界面筛选重构工具（编辑器菜单，非运行时）。
    /// 一键重构 Menu.unity 中卡牌构筑界面（CollectionPanel）的筛选：
    ///   1. 删除旧的左侧筛选区（阵营/类型/稀有度/金卡/排序/搜索/卡池下拉）
    ///   2. 卡牌网格扩满
    ///   3. 左上角加「筛选」按钮
    ///   4. 生成右侧筛选弹层（卡池/种类/颜色/费用/搜索/金卡/稀有度/排序 + 全部清除/应用）
    /// 生成后保存在场景中，可在 Inspector 中自由调整。
    /// </summary>
    public static class CardFilterBuilder
    {
        private const string MENU = "TcgEngine/卡池管理/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";
        private const string EXIT_ICON_PATH = "Assets/TcgEngine/Sprites/UI/exit.png";

        private static Font _font;

        [MenuItem(MENU + "重构卡牌构筑筛选界面")]
        public static void RebuildCollectionFilter()
        {
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            CollectionPanel panel = Object.FindObjectOfType<CollectionPanel>();
            if (panel == null)
            {
                EditorUtility.DisplayDialog("卡池管理", "未找到卡牌构筑面板（CollectionPanel）。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            //0. 幂等：先删除旧筛选按钮和弹层，避免重复生成
            DestroyIfExists("FilterButton");
            DestroyIfExists("FilterPanel");

            //1. 删除旧左侧筛选区（含旧卡池下拉）
            RemoveOldFilterArea(panel);
            DestroyIfExists("PoolDropdown");
            DestroyIfExists("PoolLabel");

            //2. 卡牌网格扩满
            ExpandCardGrid(panel);

            //3. 生成右侧筛选弹层
            UIPanel filter_panel = BuildFilterPanel(panel.transform);

            //4. 左上角「筛选」按钮
            Button filter_btn = BuildFilterButton(panel.transform);

            //5. 绑定
            panel.filter_button = filter_btn;
            panel.filter_panel = filter_panel;

            EditorSceneManager.SaveScene(scene);
            Debug.Log("卡牌构筑筛选界面已重构并保存");
            EditorUtility.DisplayDialog("卡池管理", "已重构卡牌构筑筛选界面：\n- 删除旧左侧筛选区，卡牌网格扩满\n- 左上角「筛选」按钮\n- 右侧筛选弹层（卡池/种类/颜色/费用/搜索/金卡/稀有度/排序 + 全部清除/应用）\n已自动保存。", "确定");
        }

        // ---------------- 1. 删除旧左侧筛选区 ----------------

        private static void RemoveOldFilterArea(CollectionPanel panel)
        {
            //先收集匹配对象，避免遍历中销毁导致迭代器失效
            List<Transform> to_remove = new List<Transform>();
            foreach (Transform child in panel.transform)
            {
                int toggle_count = child.GetComponentsInChildren<Toggle>(true).Length;
                InputField input = child.GetComponentInChildren<InputField>(true);
                //旧筛选区特征：含搜索框 + 至少 5 个 Toggle
                if (toggle_count >= 5 && input != null)
                    to_remove.Add(child);
            }

            foreach (Transform child in to_remove)
            {
                Debug.Log("已删除旧左侧筛选区：" + child.name);
                Object.DestroyImmediate(child.gameObject);
            }

            if (to_remove.Count == 0)
                Debug.LogWarning("未找到旧左侧筛选区（可能已删除或结构变化）");
        }

        // ---------------- 2. 卡牌网格扩满 ----------------

        private static void ExpandCardGrid(CollectionPanel panel)
        {
            if (panel.scroll_rect == null)
                return;
            RectTransform grid = panel.scroll_rect.GetComponent<RectTransform>();

            //右侧牌组区宽度
            float deck_width = 0f;
            Transform deck_container = panel.deck_list_panel != null ? panel.deck_list_panel.transform.parent : null;
            if (deck_container != null && deck_container is RectTransform)
                deck_width = ((RectTransform)deck_container).rect.width;

            //顶部留出约 6% 高度（TopBar 下方）给筛选按钮，网格从 4% 到 94% 占满屏幕
            grid.anchorMin = new Vector2(0f, 0.04f);
            grid.anchorMax = new Vector2(1f, 0.94f);
            grid.offsetMin = new Vector2(20f, 0f);
            grid.offsetMax = new Vector2(-(deck_width + 30f), 0f);
        }

        // ---------------- 3. 筛选按钮 ----------------

        private static Button BuildFilterButton(Transform parent)
        {
            Button btn = CreateButton("FilterButton", parent, "筛选", _font, 26, new Color(0.5f, 0.78f, 1f, 0.35f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            //锚定全屏左上角；TopBar 覆盖屏幕顶部约 145px，按钮放到其下方留白区（网格顶部 20% 处）
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(110f, -180f);
            rt.sizeDelta = new Vector2(150f, 52f);
            return btn;
        }

        // ---------------- 4. 右侧筛选弹层 ----------------

        private static UIPanel BuildFilterPanel(Transform parent)
        {
            //根：全屏，CanvasGroup + UIPanel（默认隐藏）
            RectTransform root = CreateRect("FilterPanel", parent);
            SetStretch(root);
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f; //编辑器内可见方便调整；运行时 UIPanel 会隐藏
            group.interactable = true;
            group.blocksRaycasts = true;
            UIPanel panel = root.gameObject.AddComponent<UIPanel>();
            panel.display_speed = 8f;

            //遮罩：全屏半透明黑，点击关闭
            RectTransform mask = CreateRect("FilterMask", root);
            SetStretch(mask);
            Image mask_img = mask.gameObject.AddComponent<Image>();
            mask_img.color = new Color(0, 0, 0, 0.6f);
            Button mask_btn = mask.gameObject.AddComponent<Button>();
            mask_btn.targetGraphic = mask_img;
            mask_btn.onClick.AddListener(() => panel.Hide());

            //右侧面板
            RectTransform dock = CreateRect("FilterDock", root);
            dock.anchorMin = new Vector2(1, 0);
            dock.anchorMax = new Vector2(1, 1);
            dock.pivot = new Vector2(1, 0.5f);
            dock.anchoredPosition = Vector2.zero;
            dock.sizeDelta = new Vector2(440, 0);
            Image dock_img = dock.gameObject.AddComponent<Image>();
            dock_img.color = new Color(0.06f, 0.07f, 0.09f, 0.98f);

            //标题
            Text title = CreateText("FilterTitle", dock, "筛选", _font, 34, new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            RectTransform title_rt = title.rectTransform;
            title_rt.anchorMin = new Vector2(0, 1);
            title_rt.anchorMax = new Vector2(1, 1);
            title_rt.pivot = new Vector2(0.5f, 1);
            title_rt.anchoredPosition = new Vector2(0, -4);
            title_rt.sizeDelta = new Vector2(0, 72);
            title_rt.offsetMin = new Vector2(24, title_rt.offsetMin.y);

            //关闭按钮
            Button close = CreateButton("FilterCloseBtn", dock, "X", _font, 28, new Color(1, 1, 1, 0.2f));
            RectTransform close_rt = close.GetComponent<RectTransform>();
            close_rt.anchorMin = new Vector2(1, 1);
            close_rt.anchorMax = new Vector2(1, 1);
            close_rt.pivot = new Vector2(0.5f, 0.5f);
            close_rt.anchoredPosition = new Vector2(-42, -36);
            close_rt.sizeDelta = new Vector2(50, 50);
            close.onClick.AddListener(() => panel.Hide());

            //滚动内容区
            RectTransform scroll_rt = CreateRect("FilterScroll", dock);
            scroll_rt.anchorMin = new Vector2(0, 0.14f);
            scroll_rt.anchorMax = new Vector2(1, 0.86f);
            scroll_rt.offsetMin = Vector2.zero;
            scroll_rt.offsetMax = Vector2.zero;
            scroll_rt.sizeDelta = Vector2.zero;

            ScrollRect scroll = scroll_rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewport = CreateRect("Viewport", scroll_rt);
            SetStretch(viewport);
            Image vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1, 1, 1, 0.02f);
            Mask vmask = viewport.gameObject.AddComponent<Mask>();
            vmask.showMaskGraphic = false;

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 400);

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.padding = new RectOffset(20, 20, 12, 12);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            //各筛选节
            BuildSectionPool(content);
            BuildSectionType(content);
            BuildSectionTeam(content);
            BuildSectionCost(content);
            BuildSectionSearch(content);
            BuildSectionFoil(content);
            BuildSectionRarity(content);
            BuildSectionSort(content);

            //底部按钮栏
            RectTransform bottom = CreateRect("FilterBottomBar", dock);
            bottom.anchorMin = new Vector2(0, 0);
            bottom.anchorMax = new Vector2(1, 0);
            bottom.pivot = new Vector2(0.5f, 0);
            bottom.anchoredPosition = new Vector2(0, 16);
            bottom.sizeDelta = new Vector2(0, 72);

            Button clear = CreateButton("FilterClearBtn", bottom, "全部清除", _font, 22, new Color(1f, 0.65f, 0.6f, 0.35f));
            RectTransform clear_rt = clear.GetComponent<RectTransform>();
            clear_rt.anchorMin = new Vector2(0, 0.5f);
            clear_rt.anchorMax = new Vector2(0, 0.5f);
            clear_rt.pivot = new Vector2(0.5f, 0.5f);
            clear_rt.anchoredPosition = new Vector2(120, 0);
            clear_rt.sizeDelta = new Vector2(160, 52);

            Button apply = CreateButton("FilterApplyBtn", bottom, "应用", _font, 22, new Color(0.5f, 0.9f, 0.6f, 0.4f));
            RectTransform apply_rt = apply.GetComponent<RectTransform>();
            apply_rt.anchorMin = new Vector2(1, 0.5f);
            apply_rt.anchorMax = new Vector2(1, 0.5f);
            apply_rt.pivot = new Vector2(0.5f, 0.5f);
            apply_rt.anchoredPosition = new Vector2(-120, 0);
            apply_rt.sizeDelta = new Vector2(160, 52);

            return panel;
        }

        private static void BuildSectionPool(Transform content)
        {
            CreateLabel(content, "卡池");
            Dropdown dd = CreateDropdown("FilterPoolDd", content, new string[] { "全部卡池" });
        }

        private static void BuildSectionType(Transform content)
        {
            CreateLabel(content, "种类");
            AddToggleRow(content, "FilterTypeToggle_character", "随从");
            AddToggleRow(content, "FilterTypeToggle_spell", "法术");
            AddToggleRow(content, "FilterTypeToggle_artifact", "神器");
            AddToggleRow(content, "FilterTypeToggle_equipment", "装备");
            AddToggleRow(content, "FilterTypeToggle_secret", "奥秘");
        }

        private static void BuildSectionTeam(Transform content)
        {
            CreateLabel(content, "颜色");
            TeamData.Load();
            foreach (TeamData team in TeamData.GetAll())
                AddToggleRow(content, "FilterTeamToggle_" + team.id, string.IsNullOrEmpty(team.title) ? team.id : team.title);
        }

        private static void BuildSectionCost(Transform content)
        {
            CreateLabel(content, "费用");
            //0~6 直接显示数字；7 显示「7+」表示 7 费及以上；每行 2 个共 4 行
            string[] labels = { "0", "1", "2", "3", "4", "5", "6", "7+" };
            for (int i = 0; i < labels.Length; i += 2)
            {
                RectTransform row = CreateRect("CostRow", content);
                row.sizeDelta = new Vector2(0, 40);
                HorizontalLayoutGroup hlayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlayout.spacing = 12f;
                hlayout.childControlWidth = true;
                hlayout.childControlHeight = false;
                hlayout.childForceExpandWidth = true;
                hlayout.childForceExpandHeight = false;

                AddToggleRow(row, "FilterCostToggle_" + i, labels[i]);
                if (i + 1 < labels.Length)
                    AddToggleRow(row, "FilterCostToggle_" + (i + 1), labels[i + 1]);
            }
        }

        private static void BuildSectionSearch(Transform content)
        {
            CreateLabel(content, "牌名搜索");
            RectTransform input_rt = CreateRect("FilterSearchInput", content);
            input_rt.sizeDelta = new Vector2(0, 44);
            Image input_bg = input_rt.gameObject.AddComponent<Image>();
            input_bg.color = new Color(1, 1, 1, 0.25f);
            InputField input = input_rt.gameObject.AddComponent<InputField>();

            Text placeholder = CreateText("Placeholder", input_rt, "输入卡牌名称（模糊匹配）", _font, 20, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            RectTransform ph_rt = placeholder.rectTransform;
            ph_rt.anchorMin = Vector2.zero;
            ph_rt.anchorMax = Vector2.one;
            ph_rt.offsetMin = new Vector2(12, 0);
            ph_rt.offsetMax = Vector2.zero;

            Text display = CreateText("Text", input_rt, "", _font, 20, Color.white, TextAnchor.MiddleLeft);
            RectTransform d_rt = display.rectTransform;
            d_rt.anchorMin = Vector2.zero;
            d_rt.anchorMax = Vector2.one;
            d_rt.offsetMin = new Vector2(12, 0);
            d_rt.offsetMax = Vector2.zero;

            input.targetGraphic = input_bg;
            input.textComponent = display;
            input.placeholder = placeholder;
        }

        private static void BuildSectionFoil(Transform content)
        {
            AddToggleRow(content, "FilterFoilToggle", "仅金卡");
        }

        private static void BuildSectionRarity(Transform content)
        {
            CreateLabel(content, "稀有度");
            RarityData.Load();
            foreach (RarityData rarity in RarityData.GetAll())
                AddToggleRow(content, "FilterRarityToggle_" + rarity.id, string.IsNullOrEmpty(rarity.title) ? rarity.id : rarity.title);
        }

        private static void BuildSectionSort(Transform content)
        {
            CreateLabel(content, "排序");
            Dropdown sort_by = CreateDropdown("FilterSortByDd", content, new string[] { "名称", "法力值", "颜色", "稀有度" });
            Dropdown sort_dir = CreateDropdown("FilterSortDirDd", content, new string[] { "升序", "降序" });
        }

        // ---------------- UI 辅助 ----------------

        private static Text CreateLabel(Transform parent, string text)
        {
            Text t = CreateText("SectionLabel", parent, text, _font, 24, new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            t.rectTransform.sizeDelta = new Vector2(0, 40);
            return t;
        }

        private static Toggle AddToggleRow(Transform parent, string name, string label)
        {
            RectTransform rt = CreateRect(name, parent); //行对象名 = FilterXxx_yyy，Toggle 挂在其上供运行时按名绑定
            rt.sizeDelta = new Vector2(0, 40);

            //行对象背景：作为 Toggle 的点击区（Toggle 必须挂载在有 Graphic 的对象上才能接收点击）
            Image row_img = rt.gameObject.AddComponent<Image>();
            row_img.color = new Color(1, 1, 1, 0.02f);

            //勾选框（子对象，仅视觉展示，不拦截射线，保证点击命中行对象上的 Toggle）
            RectTransform box = CreateRect("Checkbox", rt);
            box.anchorMin = new Vector2(0, 0.5f);
            box.anchorMax = new Vector2(0, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = new Vector2(32, 0);
            box.sizeDelta = new Vector2(28, 28);
            Image box_img = box.gameObject.AddComponent<Image>();
            box_img.color = new Color(1, 1, 1, 0.3f);
            box_img.raycastTarget = false;

            //Toggle 挂在行对象上（名字=name），否则运行时按前缀/名字找不到
            Toggle toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = row_img;
            toggle.isOn = false;

            Text check = CreateText("Checkmark", box, "\u2714", _font, 20, Color.white, TextAnchor.MiddleCenter);
            SetStretch(check.rectTransform);
            toggle.graphic = check;

            //文字
            Text txt = CreateText("Label", rt, label, _font, 22, new Color(1, 1, 1, 0.9f), TextAnchor.MiddleLeft);
            RectTransform txt_rt = txt.rectTransform;
            txt_rt.anchorMin = new Vector2(0, 0);
            txt_rt.anchorMax = new Vector2(1, 1);
            txt_rt.offsetMin = new Vector2(52, 0);
            txt_rt.offsetMax = Vector2.zero;

            return toggle;
        }

        private static Dropdown CreateDropdown(string name, Transform parent, string[] options)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 44);

            Image img = go.GetComponent<Image>();
            img.color = new Color(1, 1, 1, 0.25f);

            Dropdown dd = go.GetComponent<Dropdown>();
            dd.targetGraphic = img;

            Text caption = CreateText("Label", go.transform, options.Length > 0 ? options[0] : "", _font, 22, Color.white, TextAnchor.MiddleLeft);
            RectTransform caption_rt = caption.rectTransform;
            caption_rt.anchorMin = Vector2.zero;
            caption_rt.anchorMax = Vector2.one;
            caption_rt.offsetMin = new Vector2(14, 0);
            caption_rt.offsetMax = new Vector2(-14, 0);
            dd.captionText = caption;

            dd.ClearOptions();
            dd.AddOptions(new List<string>(options));

            SetupDropdownTemplate(dd, rt);
            return dd;
        }

        /// <summary>为下拉框构造可用的 Item 模板（Dropdown 必须要有 template 才能弹出选项）</summary>
        private static void SetupDropdownTemplate(Dropdown dd, RectTransform dd_rt)
        {
            //模板根（运行时由 Dropdown 实例化，必须处于隐藏状态）
            RectTransform template = CreateRect("Template", dd_rt);
            template.anchorMin = new Vector2(0, 0);
            template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(0.5f, 1);
            template.anchoredPosition = new Vector2(0, 0);
            template.sizeDelta = new Vector2(0, 150);
            template.gameObject.SetActive(false);

            Image template_img = template.gameObject.AddComponent<Image>();
            template_img.color = new Color(0.1f, 0.1f, 0.12f, 1f);

            ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 1f;

            RectTransform viewport = CreateRect("Viewport", template);
            SetStretch(viewport);
            viewport.offsetMin = new Vector2(2, 2);
            viewport.offsetMax = new Vector2(-2, -2);
            Image vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1, 1, 1, 0.05f);
            Mask vmask = viewport.gameObject.AddComponent<Mask>();
            vmask.showMaskGraphic = false;

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 28);

            //Item 模板
            RectTransform item = CreateRect("Item", content);
            item.anchorMin = new Vector2(0, 0.5f);
            item.anchorMax = new Vector2(1, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.anchoredPosition = new Vector2(0, -14);
            item.sizeDelta = new Vector2(0, 28);

            //Item 根加 Image 作为点击区（Toggle 必须挂载在有 Graphic 的对象上才能接收点击）
            Image item_img = item.gameObject.AddComponent<Image>();
            item_img.color = new Color(1, 1, 1, 0f);

            RectTransform item_bg = CreateRect("Item Background", item);
            SetStretch(item_bg);
            Image item_bg_img = item_bg.gameObject.AddComponent<Image>();
            item_bg_img.color = new Color(1, 1, 1, 0.1f);
            item_bg_img.raycastTarget = false; //视觉层，不拦截射线

            Toggle item_toggle = item.gameObject.AddComponent<Toggle>();
            item_toggle.targetGraphic = item_bg_img;

            RectTransform item_check = CreateRect("Item Checkmark", item);
            item_check.anchorMin = new Vector2(0, 0.5f);
            item_check.anchorMax = new Vector2(0, 0.5f);
            item_check.pivot = new Vector2(0.5f, 0.5f);
            item_check.anchoredPosition = new Vector2(14, 0);
            item_check.sizeDelta = new Vector2(20, 20);
            Image item_check_img = item_check.gameObject.AddComponent<Image>();
            item_check_img.color = new Color(1, 1, 1, 0.4f);
            item_toggle.graphic = item_check_img;

            RectTransform item_label = CreateRect("Item Label", item);
            item_label.anchorMin = Vector2.zero;
            item_label.anchorMax = Vector2.one;
            item_label.offsetMin = new Vector2(40, 0);
            item_label.offsetMax = new Vector2(-10, 0);
            Text item_text = item_label.gameObject.AddComponent<Text>();
            item_text.font = _font;
            item_text.fontSize = 20;
            item_text.color = Color.white;
            item_text.alignment = TextAnchor.MiddleLeft;

            scroll.viewport = viewport;
            scroll.content = content;

            dd.template = template;
            dd.itemText = item_text;
            dd.itemImage = item_bg_img;
        }

        private static void DestroyIfExists(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
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
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = bg_color;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.fadeDuration = 0.1f;
            btn.colors = colors;

            // 取消/关闭/返回按钮：用 exit.png 图标替换文字
            if (name.Contains("CloseBtn") || name.Contains("ReturnBtn") || name.Contains("ExitBtn"))
            {
                Sprite exit_sprite = AssetDatabase.LoadAssetAtPath<Sprite>(EXIT_ICON_PATH);
                if (exit_sprite != null)
                {
                    img.sprite = exit_sprite;
                    img.type = Image.Type.Simple;
                    img.color = Color.white;
                    img.raycastTarget = true;
                }
            }
            else
            {
                Text txt = CreateText("Text", img.transform, label, font, size, Color.white, TextAnchor.MiddleCenter);
                SetStretch(txt.rectTransform);
            }
            return btn;
        }
    }
}
