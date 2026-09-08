using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace TcgEngine.UI
{
    /// <summary>
    /// 战斗界面自定义按钮搭建工具（编辑器菜单，非运行时）。
    /// 按钮是全局资源（Workshop/buttons.json，与 buffs.json 同目录），由卡牌编辑器（CardEditorPanel）内嵌编辑，
    /// 触发动作用全局按钮图（一图多按钮）配置。
    /// 本工具提供：
    ///   1. 在卡牌编辑器页面添加按钮编辑器（内嵌在 CardEditorPanel 中：点 TopBar「按钮」按钮切换编辑模式，
    ///      复用同一编辑器界面，隐藏卡牌编辑区、显示按钮编辑区——按钮列表 + 属性编辑 + 操作栏）
    ///   2. 生成战斗按钮容器到战斗场景（GameUI.prefab：屏幕右侧竖排容器 + 隐藏模板，绑定 GameUI 字段）
    /// 生成后自动保存，可在 Inspector 中自由调整。
    /// </summary>
    public static class BattleButtonBuilder
    {
        private const string MENU = "TcgEngine/战斗按钮/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        private const string GAMEUI_PREFAB = "Assets/TcgEngine/Prefabs/GameUI.prefab";
        //优先用黑体 SimHei（标准 TTF 中文字体，Unity 可直接导入，中文清晰不糊）
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei.ttf";
        private const string FONT_FALLBACK_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";

        private static Font _font;

        // ---------------- 菜单入口 ----------------

        [MenuItem(MENU + "在卡牌编辑器页面添加按钮编辑器")]
        public static void BuildButtonEditorIntoCardEditor()
        {
            //强制切换到主菜单场景并在其中生成 + 自动保存
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            //清理旧的独立按钮编辑器页面（早期版本生成的 ButtonPanel 全屏页），避免残留
            DestroyIfExists("ButtonPanel");

            CardEditorPanel panel = Object.FindObjectOfType<CardEditorPanel>();
            if (panel == null)
            {
                EditorUtility.DisplayDialog("战斗按钮", "未找到 CardEditorPanel，请先运行「生成卡牌编辑器页面到主菜单场景」工具。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            AddButtonEditorRoot(panel);
            AddTopBarEntry(panel);

            EditorSceneManager.SaveScene(scene);
            Debug.Log("已在内嵌卡牌编辑器中添加按钮编辑器并绑定");
            EditorUtility.DisplayDialog("战斗按钮",
                "已在卡牌编辑器（CardEditorPanel）内嵌按钮编辑器并绑定，已自动保存。\n" +
                "使用：卡牌编辑器 TopBar 点「按钮」→ 切换进入按钮编辑（新增/复制/删除/保存/编辑规则图/返回）。\n" +
                "「编辑规则图」在规则编辑器内用「点击按钮时」等节点配置触发逻辑（一图多按钮）。", "确定");
        }

        [MenuItem(MENU + "生成战斗按钮容器到战斗场景")]
        public static void BuildBattleButtonContainer()
        {
            //GameUI.prefab 是游戏场景使用的预制体，通过 PrefabUtility 在 prefab 内容中创建容器并绑定字段
            GameObject root = PrefabUtility.LoadPrefabContents(GAMEUI_PREFAB);
            if (root == null)
            {
                EditorUtility.DisplayDialog("战斗按钮", "未找到 GameUI.prefab，请确认路径存在：" + GAMEUI_PREFAB, "确定");
                return;
            }

            try
            {
                GameUI ui = root.GetComponent<GameUI>();
                if (ui == null)
                {
                    EditorUtility.DisplayDialog("战斗按钮", "未找到 GameUI 组件，请确认 GameUI.prefab 已生成。", "确定");
                    return;
                }

                _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
                if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                RectTransform game_canvas = root.transform.Find("GameCanvas") as RectTransform;
                if (game_canvas == null)
                {
                    EditorUtility.DisplayDialog("战斗按钮", "未找到 GameCanvas，请确认 GameUI.prefab 结构正常。", "确定");
                    return;
                }

                //幂等：已存在则先删除旧容器
                Transform old = game_canvas.Find("BattleButtonContainer");
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);

                //容器：屏幕右侧中部，右对齐，从上往下竖排（VerticalLayoutGroup 自动排版）
                RectTransform container = CreateRect("BattleButtonContainer", game_canvas);
                container.anchorMin = new Vector2(1f, 0.5f);
                container.anchorMax = new Vector2(1f, 0.5f);
                container.pivot = new Vector2(1f, 1f);
                container.anchoredPosition = new Vector2(-40, 0);
                container.sizeDelta = new Vector2(170, 0);

                VerticalLayoutGroup vlg = container.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 12;
                vlg.padding = new RectOffset(0, 0, 0, 0);
                vlg.childAlignment = TextAnchor.UpperRight;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                ContentSizeFitter fitter = container.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                //按钮模板（隐藏，运行时 Instantiate）：背景 + 文字
                RectTransform tpl = CreateRect("BattleButtonTemplate", container);
                tpl.sizeDelta = new Vector2(170, 56);
                AddLayoutElement(tpl, 56);

                Image tpl_bg = tpl.gameObject.AddComponent<Image>();
                tpl_bg.color = new Color(0.25f, 0.35f, 0.65f, 0.7f);
                Button tpl_btn = tpl.gameObject.AddComponent<Button>();
                tpl_btn.targetGraphic = tpl_bg;

                Text tpl_txt = CreateText("Text", tpl, "按钮", _font, 24, Color.white, TextAnchor.MiddleCenter);
                SetStretch(tpl_txt.rectTransform);

                tpl.gameObject.SetActive(false);

                //绑定 GameUI 字段（容器 + 模板）
                SerializedObject so = new SerializedObject(ui);
                so.FindProperty("battle_btn_container").objectReferenceValue = container;
                so.FindProperty("battle_btn_template").objectReferenceValue = tpl.gameObject;
                so.ApplyModifiedProperties();

                PrefabUtility.SaveAsPrefabAsset(root, GAMEUI_PREFAB);
                Debug.Log("已生成战斗按钮容器到 GameUI.prefab 并保存");
                EditorUtility.DisplayDialog("战斗按钮", "已在 GameUI.prefab 的 GameCanvas 下生成战斗按钮容器（右侧竖排）和隐藏模板，并绑定到 GameUI 组件，已自动保存。\n可打开战斗场景测试。", "确定");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------- 内嵌按钮编辑器 ----------------

        /// <summary>在 CardEditorPanel 下生成按钮编辑区并绑定所有按钮编辑字段（幂等：已存在则先删除再重建）</summary>
        private static void AddButtonEditorRoot(CardEditorPanel panel)
        {
            //幂等：已存在则先删除
            Transform old = panel.transform.Find("ButtonEditRoot");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            //按钮编辑区根：覆盖卡牌编辑区位置（隐藏，点「按钮」切换显示）。
            //顶部止于 0.78：TopBar（下缘≈0.79）保持可见不被遮挡
            RectTransform root = CreateRect("ButtonEditRoot", panel.transform);
            root.anchorMin = new Vector2(0.03f, 0.08f);
            root.anchorMax = new Vector2(0.99f, 0.78f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;

            Image bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = false;

            Text title = CreateText("TitleText", root, "按钮编辑器", _font, 30,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0, 1);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.anchoredPosition = new Vector2(0, -8);
            title.rectTransform.offsetMin = new Vector2(20, title.rectTransform.offsetMin.y);
            title.rectTransform.sizeDelta = new Vector2(0, 44);

            //左侧：按钮列表
            BuildButtonListArea(root, panel);

            //右侧：按钮属性
            BuildButtonEditArea(root, panel);

            //底部操作栏
            BuildButtonOpsBar(root, panel);

            //绑定卡牌编辑区根（按钮模式下隐藏）
            panel.card_list_root = panel.transform.Find("CardListArea")?.gameObject;
            panel.editor_area_root = panel.transform.Find("EditorArea")?.gameObject;

            //绑定按钮编辑区根（运行时点「按钮」切换显示）
            panel.button_editor_root = root.gameObject;

            //确保 TopBar 置顶（按钮编辑区不遮挡工具栏按钮）
            Transform topbar = panel.transform.Find("TopBar");
            if (topbar != null)
                topbar.SetAsLastSibling();

            //默认隐藏：仅通过点 TopBar「按钮」按钮进入
            root.gameObject.SetActive(false);
        }

        private static void BuildButtonListArea(Transform parent, CardEditorPanel panel)
        {
            RectTransform area = CreateRect("ButtonListArea", parent);
            area.anchorMin = new Vector2(0, 0.1f);
            area.anchorMax = new Vector2(0.38f, 1);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.05f);
            bg.raycastTarget = false;

            Text ltitle = CreateText("ListTitle", area, "按钮列表", _font, 24,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            ltitle.rectTransform.anchorMin = new Vector2(0, 1);
            ltitle.rectTransform.anchorMax = new Vector2(1, 1);
            ltitle.rectTransform.pivot = new Vector2(0.5f, 1);
            ltitle.rectTransform.anchoredPosition = new Vector2(0, -6);
            ltitle.rectTransform.offsetMin = new Vector2(14, ltitle.rectTransform.offsetMin.y);
            ltitle.rectTransform.sizeDelta = new Vector2(0, 40);

            //列表滚动区
            ScrollRect scroll = CreateRect("ButtonListScroll", area).gameObject.AddComponent<ScrollRect>();
            RectTransform scroll_rt = scroll.GetComponent<RectTransform>();
            scroll_rt.anchorMin = new Vector2(0.02f, 0.05f);
            scroll_rt.anchorMax = new Vector2(0.98f, 0.88f);
            scroll_rt.offsetMin = Vector2.zero;
            scroll_rt.offsetMax = Vector2.zero;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewport = CreateRect("Viewport", scroll_rt);
            SetStretch(viewport);
            Image vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1, 1, 1, 0.03f);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 4, 4);
            vlg.spacing = 6;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            panel.button_list_scroll = scroll;
            panel.button_list_content = content;

            //列表项模板（隐藏）：背景 + 标题文字 + 点击选中
            RectTransform item = CreateRect("ButtonListTemplate", content);
            item.sizeDelta = new Vector2(0, 52);
            AddLayoutElement(item, 52);
            Image item_bg = item.gameObject.AddComponent<Image>();
            item_bg.color = new Color(1, 1, 1, 0.08f);
            Text item_txt = CreateText("Text", item, "按钮", _font, 22, Color.white, TextAnchor.MiddleLeft);
            SetStretch(item_txt.rectTransform);
            item_txt.rectTransform.offsetMin = new Vector2(12, 0);
            item_txt.rectTransform.offsetMax = new Vector2(-12, 0);
            Button item_btn = item.gameObject.AddComponent<Button>();
            item_btn.targetGraphic = item_bg;
            item.gameObject.SetActive(false);
            panel.button_list_template = item.gameObject;
        }

        private static void BuildButtonEditArea(Transform parent, CardEditorPanel panel)
        {
            RectTransform area = CreateRect("ButtonEditArea", parent);
            area.anchorMin = new Vector2(0.42f, 0.1f);
            area.anchorMax = new Vector2(1, 1);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.05f);
            bg.raycastTarget = false;

            Text etitle = CreateText("EditTitle", area, "按钮属性", _font, 24,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            etitle.rectTransform.anchorMin = new Vector2(0, 1);
            etitle.rectTransform.anchorMax = new Vector2(1, 1);
            etitle.rectTransform.pivot = new Vector2(0.5f, 1);
            etitle.rectTransform.anchoredPosition = new Vector2(0, -6);
            etitle.rectTransform.offsetMin = new Vector2(14, etitle.rectTransform.offsetMin.y);
            etitle.rectTransform.sizeDelta = new Vector2(0, 40);

            //属性滚动区（VerticalLayoutGroup：ID / 显示文本 / 描述）
            ScrollRect escroll = CreateRect("ButtonEditScroll", area).gameObject.AddComponent<ScrollRect>();
            RectTransform escroll_rt = escroll.GetComponent<RectTransform>();
            escroll_rt.anchorMin = new Vector2(0.02f, 0.05f);
            escroll_rt.anchorMax = new Vector2(0.98f, 0.98f);
            escroll_rt.offsetMin = Vector2.zero;
            escroll_rt.offsetMax = Vector2.zero;
            escroll.horizontal = false;
            escroll.vertical = true;
            escroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform eview = CreateRect("Viewport", escroll_rt);
            SetStretch(eview);
            Image evimg = eview.gameObject.AddComponent<Image>();
            evimg.color = new Color(1, 1, 1, 0.03f);
            Mask emask = eview.gameObject.AddComponent<Mask>();
            emask.showMaskGraphic = false;

            RectTransform econtent = CreateRect("Content", eview);
            econtent.anchorMin = new Vector2(0, 1);
            econtent.anchorMax = new Vector2(1, 1);
            econtent.pivot = new Vector2(0.5f, 1);
            econtent.anchoredPosition = Vector2.zero;
            econtent.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup evlg = econtent.gameObject.AddComponent<VerticalLayoutGroup>();
            evlg.padding = new RectOffset(8, 8, 6, 6);
            evlg.spacing = 8;
            evlg.childAlignment = TextAnchor.UpperCenter;
            evlg.childControlWidth = true;
            evlg.childControlHeight = true;
            evlg.childForceExpandWidth = true;
            evlg.childForceExpandHeight = false;

            ContentSizeFitter efitter = econtent.gameObject.AddComponent<ContentSizeFitter>();
            efitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            escroll.viewport = eview;
            escroll.content = econtent;

            //按钮 ID（规则图 button_id 字段匹配用）/ 显示文本 / 描述
            panel.btn_id_input = CreateInputIn(CreateFieldRow(econtent, "按钮 ID", 50), "唯一标识（如 skill1）");
            panel.btn_title_input = CreateInputIn(CreateFieldRow(econtent, "显示文本", 50), "按钮上显示的文字");
            panel.btn_desc_input = CreateInputIn(CreateFieldRow(econtent, "描述", 96), "说明（可选）", true);

            //提示（一图多按钮）
            Text hint = CreateText("HintLabel", econtent, "触发动作：点底部「编辑规则图」配置\n图内用「点击按钮时」节点选择本按钮 ID",
                _font, 20, new Color(1, 0.85f, 0.6f, 0.85f), TextAnchor.MiddleLeft);
            hint.rectTransform.anchorMin = new Vector2(0, 0);
            hint.rectTransform.anchorMax = new Vector2(1, 0);
            hint.rectTransform.pivot = new Vector2(0.5f, 0);
            hint.rectTransform.anchoredPosition = new Vector2(0, 0);
            hint.rectTransform.offsetMin = new Vector2(10, 0);
            hint.rectTransform.offsetMax = new Vector2(-10, 60);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static void BuildButtonOpsBar(Transform parent, CardEditorPanel panel)
        {
            RectTransform bar = CreateRect("ButtonOpsBar", parent);
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.anchoredPosition = new Vector2(0, 6);
            bar.sizeDelta = new Vector2(0, 58);

            panel.btn_button_back = CreateToolbarButton(bar, "ButtonBackBtn", "返回", -450, new Color(0.9f, 0.9f, 0.9f, 0.3f));
            panel.btn_button_new = CreateToolbarButton(bar, "ButtonNewBtn", "新增", -340, new Color(0.5f, 0.9f, 0.6f, 0.4f));
            panel.btn_button_copy = CreateToolbarButton(bar, "ButtonCopyBtn", "复制", -230, new Color(0.5f, 0.78f, 1f, 0.4f));
            panel.btn_button_del = CreateToolbarButton(bar, "ButtonDelBtn", "删除", -120, new Color(1f, 0.6f, 0.6f, 0.4f));
            panel.btn_button_edit_graph = CreateToolbarButton(bar, "ButtonEditGraphBtn", "编辑规则图", 210, new Color(0.75f, 0.6f, 1f, 0.4f));
            panel.btn_button_save = CreateToolbarButton(bar, "ButtonSaveBtn", "保存", 340, new Color(0.6f, 0.9f, 0.6f, 0.4f));
        }

        /// <summary>在 CardEditorPanel TopBar 添加「按钮」入口按钮（幂等：已存在则先删除再重建）</summary>
        private static void AddTopBarEntry(CardEditorPanel panel)
        {
            //幂等：已存在则先删除
            Transform old = panel.transform.Find("TopBar/ButtonsBtn");
            if (old == null) old = panel.transform.Find("ButtonsBtn");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            Transform bar = panel.transform.Find("TopBar");
            Transform parent = bar != null ? bar : panel.transform;

            Button btn = CreateButton("ButtonsBtn", parent, "按钮", _font, 24,
                new Color(0.75f, 0.6f, 1f, 0.4f));   //紫色，与增益按钮区分
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(760, 0);   //在「增益」按钮（x=640）右侧
            rt.sizeDelta = new Vector2(100, 46);

            panel.btn_buttons = btn;
        }

        private static void DestroyIfExists(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // ---------------- UI 辅助 ----------------

        private static Button CreateToolbarButton(RectTransform bar, string name, string label, float x, Color? color = null)
        {
            Button btn = CreateButton(name, bar, label, _font, 24, color ?? new Color(1, 1, 1, 0.25f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(110, 52);
            return btn;
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

        /// <summary>字段行：左侧标签 + 右侧控件区（加入 VerticalLayoutGroup 自动排版）</summary>
        private static RectTransform CreateFieldRow(Transform parent, string label, float height)
        {
            RectTransform row = CreateRect(label + "Row", parent);
            row.sizeDelta = new Vector2(0, height);
            AddLayoutElement(row, height);

            Text lbl = CreateText("PropLabel", row, label, _font, 24, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleLeft);
            lbl.rectTransform.anchorMin = new Vector2(0, 0.5f);
            lbl.rectTransform.anchorMax = new Vector2(0.3f, 0.5f);
            lbl.rectTransform.pivot = new Vector2(0, 0.5f);
            lbl.rectTransform.anchoredPosition = new Vector2(6, 0);
            lbl.rectTransform.sizeDelta = new Vector2(0, 40);

            RectTransform field = CreateRect("Field", row);
            field.anchorMin = new Vector2(0.32f, 0.5f);
            field.anchorMax = new Vector2(1, 0.5f);
            field.pivot = new Vector2(0.5f, 0.5f);
            field.anchoredPosition = Vector2.zero;
            field.sizeDelta = new Vector2(-10, height - 8);
            return field;
        }

        private static InputField CreateInputIn(RectTransform field, string placeholder, bool multiline = false)
        {
            Image bg = CreateImage("InputBG", field, new Color(1, 1, 1, 0.18f));
            SetStretch(bg.rectTransform);
            if (!multiline)
            {
                RectTransform border = CreateRect("Border", field);
                SetStretch(border);
                Image border_img = border.gameObject.AddComponent<Image>();
                border_img.color = new Color(1, 1, 1, 0.35f);
            }

            InputField input = field.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = CreateText("Text", field, "", _font, 22, Color.white, TextAnchor.MiddleLeft);
            input.textComponent.rectTransform.anchorMin = new Vector2(0, 0);
            input.textComponent.rectTransform.anchorMax = new Vector2(1, 1);
            input.textComponent.rectTransform.offsetMin = new Vector2(8, 0);
            input.textComponent.rectTransform.offsetMax = new Vector2(-8, 0);
            if (multiline)
            {
                input.textComponent.alignment = TextAnchor.UpperLeft;
                input.textComponent.verticalOverflow = VerticalWrapMode.Overflow;
                input.lineType = InputField.LineType.MultiLineNewline;
            }
            else
            {
                input.lineType = InputField.LineType.SingleLine;
            }

            Text ph = CreateText("Placeholder", field, placeholder, _font, 20,
                new Color(1, 1, 1, 0.35f), multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            ph.rectTransform.anchorMin = new Vector2(0, 0);
            ph.rectTransform.anchorMax = new Vector2(1, 1);
            ph.rectTransform.offsetMin = new Vector2(8, 0);
            ph.rectTransform.offsetMax = new Vector2(-8, 0);
            input.placeholder = ph;
            return input;
        }

        private static void AddLayoutElement(RectTransform rt, float height)
        {
            LayoutElement le = rt.gameObject.GetComponent<LayoutElement>();
            if (le == null)
                le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }
    }
}
