using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace TcgEngine.UI
{
    /// <summary>
    /// 增益管理页面搭建工具（编辑器菜单，非运行时）。
    /// 一键在 Menu.unity 场景中生成：
    ///   1. BuffPanel 全屏页面（与卡牌管理 CollectionPanel / 卡池管理 CardPoolPanel 同级，
    ///      透明背景无黑色遮罩，左侧增益列表 + 右侧属性表编辑 + 底部操作栏）
    ///   2. 顶部导航栏「增益」标签（复制「卡牌」标签样式，group=menu，点击切换到增益页面）
    /// 增益是全局资源（Workshop/buffs.json，与卡池文件同目录），与卡池平级，不挂靠任何卡池。
    /// 生成后保存在场景中，可在 Inspector 中自由调整。
    /// </summary>
    public static class BuffPanelBuilder
    {
        private const string MENU = "TcgEngine/增益/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        //优先用黑体 SimHei（标准 TTF 中文字体，Unity 可直接导入，中文清晰不糊）
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei.ttf";
        private const string FONT_FALLBACK_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";

        private static Font _font;

        // ---------------- 菜单入口 ----------------

        [MenuItem(MENU + "生成增益管理页面到主菜单场景")]
        public static void BuildBuffPanel()
        {
            //强制切换到主菜单场景并在其中生成 + 自动保存，避免选错场景或忘记保存
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            //已存在则先删除，保证幂等可重建
            DestroyIfExists("BuffPanel");
            DestroyIfExists("TabBuff");

            Canvas canvas = GetMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("增益管理", "未找到 Canvas，请确认主菜单场景加载成功。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuffPanel panel = BuildPanel(canvas.transform);
            BuildMenuTab(canvas.transform, panel);

            EditorSceneManager.SaveScene(scene);
            Debug.Log("增益管理页面已生成到 Menu.unity 并保存");
            EditorUtility.DisplayDialog("增益管理", "已在主菜单生成「增益」导航标签和增益管理页面，并已自动保存。\n可直接点 Play 测试。", "确定");
        }

        [MenuItem(MENU + "在卡牌编辑器页面添加「增益」按钮")]
        public static void AddBuffEditorButton()
        {
            //只新增按钮并绑定，不动卡牌编辑器页面任何已有内容（用户手动调整过的布局保持原样）
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            CardEditorPanel panel = Object.FindObjectOfType<CardEditorPanel>();
            if (panel == null)
            {
                EditorUtility.DisplayDialog("增益管理", "未找到 CardEditorPanel，请确认主菜单场景加载成功（卡牌编辑器页面已生成）。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            //幂等：已存在则先删除
            Transform old = panel.transform.Find("TopBar/BuffBtn");
            if (old == null) old = panel.transform.Find("BuffBtn");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            //挂在 TopBar（找不到 TopBar 就挂面板根部，可手动拖）
            Transform bar = panel.transform.Find("TopBar");
            Transform parent = bar != null ? bar : panel.transform;

            Button btn = CreateButton("BuffBtn", parent, "增益", _font, 24,
                new Color(0.85f, 0.75f, 1f, 0.4f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(640, 0);
            rt.sizeDelta = new Vector2(100, 46);

            panel.btn_buff = btn;

            EditorSceneManager.SaveScene(scene);
            Debug.Log("已在卡牌编辑器页面 TopBar 添加「增益」按钮并绑定");
            EditorUtility.DisplayDialog("增益管理", "已在卡牌编辑器 TopBar 添加「增益」按钮并绑定到 CardEditorPanel，已自动保存。\n按钮位置可在 Inspector 中拖动调整。", "确定");
        }

        [MenuItem(MENU + "在增益页面添加「编辑效果」按钮")]
        public static void AddBuffEditGraphButton()
        {
            //只新增按钮并绑定，不动增益页面任何已有内容（用户手动调整过的布局保持原样）
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            BuffPanel panel = Object.FindObjectOfType<BuffPanel>();
            if (panel == null)
            {
                EditorUtility.DisplayDialog("增益管理", "未找到 BuffPanel，请先运行「生成增益管理页面到主菜单场景」。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            //幂等：已存在则先删除
            Transform old = panel.transform.Find("BottomBar/BuffEditGraphBtn");
            if (old == null) old = panel.transform.Find("BuffEditGraphBtn");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            //挂在底部工具栏（找不到 BottomBar 就挂面板根部，可手动拖）
            Transform bar = panel.transform.Find("BottomBar");
            Transform parent = bar != null ? bar : panel.transform;

            Button btn = CreateButton("BuffEditGraphBtn", parent, "编辑效果", _font, 22,
                new Color(0.85f, 0.75f, 1f, 0.4f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(210, 0);
            rt.sizeDelta = new Vector2(110, 50);

            panel.btn_buff_edit_graph = btn;

            EditorSceneManager.SaveScene(scene);
            Debug.Log("已在增益页面底部工具栏添加「编辑效果」按钮并绑定");
            EditorUtility.DisplayDialog("增益管理", "已在增益页面底部工具栏添加「编辑效果」按钮并绑定到 BuffPanel，已自动保存。\n按钮位置可在 Inspector 中拖动调整。", "确定");
        }

        private static void DestroyIfExists(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // ---------------- 页面（透明全屏，与 CollectionPanel/CardPoolPanel 同级） ----------------

        private static BuffPanel BuildPanel(Transform canvas)
        {
            //根页面：全屏透明（无背景遮罩），CanvasGroup + BuffPanel 组件
            RectTransform root = CreateRect("BuffPanel", canvas);
            SetStretch(root);

            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f; //编辑器内可见方便调整；运行时 UIPanel.Awake 会隐藏
            group.interactable = true;
            group.blocksRaycasts = true;

            BuffPanel panel = root.gameObject.AddComponent<BuffPanel>();

            //标题（放在顶部导航栏下方，与卡池管理一致）
            Text title = CreateText("TitleText", root, "增益管理", _font, 42,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.anchoredPosition = new Vector2(0, -200);
            title.rectTransform.sizeDelta = new Vector2(500, 60);

            //左侧：增益列表
            BuildListArea(root, panel);

            //右侧：属性表编辑
            BuildEditArea(root, panel);

            //底部操作栏
            BuildToolbar(root, panel);

            return panel;
        }

        // ---------------- 左侧：增益列表 ----------------

        private static void BuildListArea(Transform parent, BuffPanel panel)
        {
            RectTransform area = CreateRect("ListArea", parent);
            area.anchorMin = new Vector2(0.06f, 0.14f);
            area.anchorMax = new Vector2(0.34f, 0.84f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            Text ltitle = CreateText("ListTitle", area, "增益列表", _font, 26,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            ltitle.rectTransform.anchorMin = new Vector2(0, 1);
            ltitle.rectTransform.anchorMax = new Vector2(1, 1);
            ltitle.rectTransform.pivot = new Vector2(0.5f, 1);
            ltitle.rectTransform.anchoredPosition = new Vector2(0, -6);
            ltitle.rectTransform.offsetMin = new Vector2(14, ltitle.rectTransform.offsetMin.y);
            ltitle.rectTransform.sizeDelta = new Vector2(0, 40);

            //列表滚动区
            ScrollRect scroll = CreateRect("BuffListScroll", area).gameObject.AddComponent<ScrollRect>();
            RectTransform scroll_rt = scroll.GetComponent<RectTransform>();
            scroll_rt.anchorMin = new Vector2(0.02f, 0.04f);
            scroll_rt.anchorMax = new Vector2(0.98f, 0.86f);
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
            panel.buff_list_scroll = scroll;
            panel.buff_list_content = content;

            //列表项模板（隐藏）：背景 + 标题文字 + 点击选中
            RectTransform item = CreateRect("BuffListTemplate", content);
            item.sizeDelta = new Vector2(0, 56);
            AddLayoutElement(item, 56);
            Image item_bg = item.gameObject.AddComponent<Image>();
            item_bg.color = new Color(1, 1, 1, 0.08f);
            Text item_txt = CreateText("Text", item, "增益", _font, 22, Color.white, TextAnchor.MiddleLeft);
            SetStretch(item_txt.rectTransform);
            item_txt.rectTransform.offsetMin = new Vector2(12, 0);
            item_txt.rectTransform.offsetMax = new Vector2(-12, 0);
            Button item_btn = item.gameObject.AddComponent<Button>();
            item_btn.targetGraphic = item_bg;
            item.gameObject.SetActive(false);
            panel.buff_list_template = item.gameObject;
        }

        // ---------------- 右侧：属性表编辑 ----------------

        private static void BuildEditArea(Transform parent, BuffPanel panel)
        {
            RectTransform area = CreateRect("EditArea", parent);
            area.anchorMin = new Vector2(0.38f, 0.14f);
            area.anchorMax = new Vector2(0.94f, 0.84f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            Text etitle = CreateText("EditTitle", area, "增益属性", _font, 26,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            etitle.rectTransform.anchorMin = new Vector2(0, 1);
            etitle.rectTransform.anchorMax = new Vector2(1, 1);
            etitle.rectTransform.pivot = new Vector2(0.5f, 1);
            etitle.rectTransform.anchoredPosition = new Vector2(0, -6);
            etitle.rectTransform.offsetMin = new Vector2(14, etitle.rectTransform.offsetMin.y);
            etitle.rectTransform.sizeDelta = new Vector2(0, 40);

            //编辑滚动区（VerticalLayoutGroup：名称/分类/持续回合/描述/属性表）
            ScrollRect escroll = CreateRect("BuffEditScroll", area).gameObject.AddComponent<ScrollRect>();
            RectTransform escroll_rt = escroll.GetComponent<RectTransform>();
            escroll_rt.anchorMin = new Vector2(0.02f, 0.04f);
            escroll_rt.anchorMax = new Vector2(0.98f, 0.92f);
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

            //名称 / 分类 / 持续回合 / 描述
            panel.buff_name_input = CreateInputIn(CreateFieldRow(econtent, "名称", 50), "增益名称");
            panel.buff_category_dropdown = CreateDropdownIn(CreateFieldRow(econtent, "分类", 50),
                new List<string> { "增益", "减益", "光环", "印记" });
            panel.buff_duration_input = CreateInputIn(CreateFieldRow(econtent, "持续回合", 50), "0=永久");
            panel.buff_desc_input = CreateInputIn(CreateFieldRow(econtent, "描述", 96), "说明（支持 <属性名> 显示数值）", true);

            CreatePropLabel(econtent, "属性表（攻击/生命加成自动参与战斗）");

            //属性表容器（嵌套布局组：每行 key + value + 删除）
            RectTransform prop_content = CreateRect("BuffPropContent", econtent);
            prop_content.sizeDelta = new Vector2(0, 0);
            VerticalLayoutGroup pvlg = prop_content.gameObject.AddComponent<VerticalLayoutGroup>();
            pvlg.padding = new RectOffset(0, 0, 0, 0);
            pvlg.spacing = 6;
            pvlg.childAlignment = TextAnchor.UpperCenter;
            pvlg.childControlWidth = true;
            pvlg.childControlHeight = true;
            pvlg.childForceExpandWidth = true;
            pvlg.childForceExpandHeight = false;
            panel.buff_prop_content = prop_content;

            //属性行模板（隐藏）：KeyInput + ValueInput + DelBtn
            RectTransform prow = CreateRect("BuffPropTemplate", prop_content);
            prow.sizeDelta = new Vector2(0, 48);
            AddLayoutElement(prow, 48);

            RectTransform key_rt = CreateRect("KeyInput", prow);
            key_rt.anchorMin = new Vector2(0, 0);
            key_rt.anchorMax = new Vector2(0.44f, 1);
            key_rt.offsetMin = Vector2.zero;
            key_rt.offsetMax = Vector2.zero;
            CreateInputIn(key_rt, "属性名");

            RectTransform val_rt = CreateRect("ValueInput", prow);
            val_rt.anchorMin = new Vector2(0.48f, 0);
            val_rt.anchorMax = new Vector2(0.72f, 1);
            val_rt.offsetMin = Vector2.zero;
            val_rt.offsetMax = Vector2.zero;
            CreateInputIn(val_rt, "数值");

            Button del_btn = CreateButton("DelBtn", prow, "✕", _font, 18, new Color(0.8f, 0.2f, 0.2f, 0.5f));
            RectTransform drt = del_btn.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.78f, 0);
            drt.anchorMax = new Vector2(1, 1);
            drt.offsetMin = Vector2.zero;
            drt.offsetMax = Vector2.zero;

            prow.gameObject.SetActive(false);
            panel.buff_prop_template = prow.gameObject;

            //新增属性按钮
            panel.btn_buff_add_prop = CreateButton("AddPropBtn", econtent, "+ 新增属性", _font, 20,
                new Color(0.5f, 0.78f, 1f, 0.3f));
            RectTransform ap_rt = panel.btn_buff_add_prop.GetComponent<RectTransform>();
            ap_rt.sizeDelta = new Vector2(0, 48);
            AddLayoutElement(ap_rt, 48);
        }

        // ---------------- 底部操作栏 ----------------

        private static void BuildToolbar(Transform parent, BuffPanel panel)
        {
            RectTransform bar = CreateRect("BottomBar", parent);
            bar.anchorMin = new Vector2(0.06f, 0.04f);
            bar.anchorMax = new Vector2(0.94f, 0.1f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = Vector2.zero;

            panel.btn_buff_new = CreateToolbarButton(bar, "BuffNewBtn", "新增", -340, new Color(0.5f, 0.9f, 0.6f, 0.4f));
            panel.btn_buff_copy = CreateToolbarButton(bar, "BuffCopyBtn", "复制", -230, new Color(0.5f, 0.78f, 1f, 0.4f));
            panel.btn_buff_del = CreateToolbarButton(bar, "BuffDelBtn", "删除", -120, new Color(1f, 0.6f, 0.6f, 0.4f));
            panel.btn_buff_save = CreateToolbarButton(bar, "BuffSaveBtn", "保存", 320, new Color(0.6f, 0.9f, 0.6f, 0.4f));

            //状态提示
            Text status = CreateText("StatusText", parent, "", _font, 22, new Color(1, 0.85f, 0.6f, 1f), TextAnchor.MiddleCenter);
            status.rectTransform.anchorMin = new Vector2(0, 0);
            status.rectTransform.anchorMax = new Vector2(1, 0);
            status.rectTransform.pivot = new Vector2(0.5f, 0);
            status.rectTransform.anchoredPosition = new Vector2(0, 90);
            status.rectTransform.sizeDelta = new Vector2(0, 40);
            panel.status_text = status;
        }

        private static Button CreateToolbarButton(RectTransform bar, string name, string label, float x, Color? color = null)
        {
            Button btn = CreateButton(name, bar, label, _font, 24, color ?? new Color(1, 1, 1, 0.25f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(100, 52);
            return btn;
        }

        // ---------------- 顶部导航栏「增益」标签 ----------------

        private static void BuildMenuTab(Transform canvas, BuffPanel panel)
        {
            //复制「卡牌」标签的完整样式，保证与导航栏其它标签完全一致
            GameObject tab_collection = GameObject.Find("TabCollection");
            if (tab_collection == null)
            {
                Debug.LogWarning("未找到 TabCollection，跳过导航标签（可手动创建）");
                return;
            }

            GameObject tab = Object.Instantiate(tab_collection, tab_collection.transform.parent, false);
            tab.name = "TabBuff";
            tab.SetActive(true);

            //放在「卡池」标签右侧（动态取卡池标签位置+宽度，保证贴邻且不超出相机可视区域）
            RectTransform rt = tab.GetComponent<RectTransform>();
            GameObject tab_pool = GameObject.Find("TabCardPool");
            if (tab_pool != null)
            {
                RectTransform prt = tab_pool.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(prt.anchoredPosition.x + prt.sizeDelta.x, rt.anchoredPosition.y);
            }
            else
            {
                rt.anchoredPosition = new Vector2(1711 + 185, rt.anchoredPosition.y);
            }

            //切换目标页面 → 增益管理页面
            TabButton tab_btn = tab.GetComponent<TabButton>();
            if (tab_btn != null)
            {
                tab_btn.group = "menu";
                tab_btn.active = false;
                tab_btn.ui_panel = panel;
            }

            //标签文字「卡牌」→「增益」
            Text[] texts = tab.GetComponentsInChildren<Text>(true);
            foreach (Text t in texts)
            {
                if (!string.IsNullOrEmpty(t.text))
                {
                    t.text = "增益";
                    break;
                }
            }

            //调整标签在导航栏中的顺序（紧跟「卡池」）
            if (tab_pool != null)
                tab.transform.SetSiblingIndex(tab_pool.transform.GetSiblingIndex() + 1);
            else
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

        /// <summary>字段行：左侧标签 + 右侧控件区（加入 VerticalLayoutGroup 自动排版）</summary>
        private static RectTransform CreateFieldRow(Transform parent, string label, float height)
        {
            RectTransform row = CreateRect(label + "Row", parent);
            row.sizeDelta = new Vector2(0, height);
            AddLayoutElement(row, height);

            CreatePropLabel(row, label);

            RectTransform field = CreateRect("Field", row);
            field.anchorMin = new Vector2(0.32f, 0.5f);
            field.anchorMax = new Vector2(1, 0.5f);
            field.pivot = new Vector2(0.5f, 0.5f);
            field.anchoredPosition = Vector2.zero;
            field.sizeDelta = new Vector2(-10, height - 8);
            return field;
        }

        private static Text CreatePropLabel(Transform parent, string text)
        {
            Text label = CreateText("PropLabel", parent, text, _font, 24, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 0.5f);
            label.rectTransform.anchorMax = new Vector2(0.3f, 0.5f);
            label.rectTransform.pivot = new Vector2(0, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(6, 0);
            label.rectTransform.sizeDelta = new Vector2(0, 40);
            return label;
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

        private static Dropdown CreateDropdownIn(RectTransform field, List<string> options)
        {
            Image bg = CreateImage("DropdownBG", field, new Color(1, 1, 1, 0.18f));
            SetStretch(bg.rectTransform);

            Dropdown dd = field.gameObject.AddComponent<Dropdown>();
            dd.targetGraphic = bg;
            dd.captionText = CreateText("Label", field, "", _font, 22, Color.white, TextAnchor.MiddleLeft);
            dd.captionText.rectTransform.anchorMin = new Vector2(0, 0);
            dd.captionText.rectTransform.anchorMax = new Vector2(1, 1);
            dd.captionText.rectTransform.offsetMin = new Vector2(8, 0);
            dd.captionText.rectTransform.offsetMax = new Vector2(-8, 0);
            dd.itemText = CreateText("ItemText", field, "", _font, 22, Color.white, TextAnchor.MiddleLeft);

            //箭头（右侧）
            Text arrow = CreateText("Arrow", field, "▾", _font, 24, Color.white, TextAnchor.MiddleCenter);
            arrow.rectTransform.anchorMin = new Vector2(1, 0);
            arrow.rectTransform.anchorMax = new Vector2(1, 1);
            arrow.rectTransform.pivot = new Vector2(1, 0.5f);
            arrow.rectTransform.anchoredPosition = new Vector2(-6, 0);
            arrow.rectTransform.sizeDelta = new Vector2(24, 0);

            dd.AddOptions(options);
            dd.value = 0;
            return dd;
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
