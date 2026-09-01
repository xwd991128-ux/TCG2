using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡牌编辑器页面搭建工具（编辑器菜单，非运行时）。
    /// 一键在 Menu.unity 场景中生成：
    ///   1. CardEditorPanel 全屏页面（与 CardPoolPanel/CollectionPanel 同级，透明背景）
    ///   2. 卡池管理页行模板加「编辑」按钮（接入打开卡牌编辑器）
    /// 生成后保存在场景中，可在 Inspector 中自由调整。
    /// </summary>
    public static class CardEditorBuilder
    {
        private const string MENU = "TcgEngine/卡牌编辑器/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";
        private const string EXIT_ICON_PATH = "Assets/TcgEngine/Sprites/UI/exit.png";

        private static Font _font;

        [MenuItem(MENU + "生成卡牌编辑器页面到主菜单场景")]
        public static void BuildCardEditor()
        {
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            //幂等：先删除旧页面与旧编辑按钮
            DestroyIfExists("CardEditorPanel");
            RemoveEditButtons();

            Canvas canvas = GetMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("卡牌编辑器", "未找到 Canvas，请确认主菜单场景加载成功。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            CardEditorPanel panel = BuildPanel(canvas.transform);
            AddEditButtonToPoolLines();

            EditorSceneManager.SaveScene(scene);

            //同时生成卡牌规则编辑器页面（供「进行」按钮进入）
            GraphEditorBuilder.BuildGraphEditor();

            Debug.Log("卡牌编辑器页面已生成到 Menu.unity 并保存");
            EditorUtility.DisplayDialog("卡牌编辑器",
                "已生成卡牌编辑器页面 + 卡池管理页「编辑」按钮 + 卡牌规则编辑器页面。\nP2 支持：选中卡牌 → 「进行」进入规则编辑器（属性/图片/音效 + 节点连线）。\n已自动保存，可直接点 Play 测试。", "确定");
        }

        // ---------------- 页面 ----------------

        private static CardEditorPanel BuildPanel(Transform canvas)
        {
            RectTransform root = CreateRect("CardEditorPanel", canvas);
            SetStretch(root);

            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            CardEditorPanel panel = root.gameObject.AddComponent<CardEditorPanel>();

            //全屏背景遮罩：盖住整个游戏画面（含主菜单栏），本面板内容在其上
            Image bg = CreateImage("Background", root, new Color(0f, 0f, 0f, 0.9f));
            bg.raycastTarget = false;
            SetStretch(bg.rectTransform);

            //标题
            Text title = CreateText("TitleText", root, "卡牌编辑器", _font, 40,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.anchoredPosition = new Vector2(0, -70);
            title.rectTransform.sizeDelta = new Vector2(600, 60);
            panel.title_text = title;

            //顶部工具栏：保存 / 测试 / 关闭
            BuildTopBar(root, panel);

            //左侧：卡牌列表（仿卡牌构筑，展示当前卡池的所有卡）
            BuildCardListArea(root, panel);

            //右侧：编辑区（暂空，后续放卡池/卡牌参数编辑）
            BuildEditorArea(root, panel);

            //工具栏置顶：确保保存/测试/返回按钮不被各内容区域覆盖
            Transform topbar = root.Find("TopBar");
            if (topbar != null)
                topbar.SetAsLastSibling();

            //底部文件路径 + 状态
            Text file = CreateText("FileText", root, "", _font, 20, new Color(1, 1, 1, 0.55f), TextAnchor.MiddleLeft);
            file.rectTransform.anchorMin = new Vector2(0, 0);
            file.rectTransform.anchorMax = new Vector2(0, 0);
            file.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            file.rectTransform.anchoredPosition = new Vector2(280, 24);
            file.rectTransform.sizeDelta = new Vector2(520, 36);
            panel.file_text = file;

            Text status = CreateText("StatusText", root, "", _font, 20, new Color(1, 0.85f, 0.6f, 1f), TextAnchor.MiddleRight);
            status.rectTransform.anchorMin = new Vector2(1, 0);
            status.rectTransform.anchorMax = new Vector2(1, 0);
            status.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            status.rectTransform.anchoredPosition = new Vector2(-280, 24);
            status.rectTransform.sizeDelta = new Vector2(520, 36);
            panel.status_text = status;

            //与项目其他面板（CardPoolPanel/FilterPanel）一致：SetActive(true) 存储，
            //运行时由 UIPanel.Awake 将 alpha 归零、首帧 AfterHide 自动失活隐藏；
            //不要用 SetActive(false)，否则 Awake 不执行、Get() 恒为 null，
            //且 GameObject.Find 找不到失活对象导致重跑工具的旧面板无法清理。
            root.gameObject.SetActive(true);

            return panel;
        }

        private static void BuildTopBar(Transform parent, CardEditorPanel panel)
        {
            RectTransform bar = CreateRect("TopBar", parent);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -165);
            bar.sizeDelta = new Vector2(0, 60);

            panel.btn_save = CreateButton("SaveBtn", bar, "保存", _font, 24, new Color(0.5f, 0.9f, 0.6f, 0.4f));
            RectTransform srt = panel.btn_save.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(-300, 0);
            srt.sizeDelta = new Vector2(120, 46);

            panel.btn_test = CreateButton("TestBtn", bar, "模拟测试", _font, 24, new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform trt = panel.btn_test.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f);
            trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(300, 0);
            trt.sizeDelta = new Vector2(160, 46);

            panel.btn_go = CreateButton("GoBtn", bar, "进行", _font, 24, new Color(1f, 0.85f, 0.6f, 0.4f));
            RectTransform grt = panel.btn_go.GetComponent<RectTransform>();
            grt.anchorMin = new Vector2(0.5f, 0.5f);
            grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(480, 0);
            grt.sizeDelta = new Vector2(120, 46);

            panel.btn_close = CreateButton("CloseBtn", bar, "返回", _font, 24, new Color(1, 1, 1, 0.25f));
            RectTransform crt = panel.btn_close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1, 0.5f);
            crt.anchorMax = new Vector2(1, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(-60, 0);
            crt.sizeDelta = new Vector2(56, 56);
        }

        private static void BuildCardListArea(Transform parent, CardEditorPanel panel)
        {
            RectTransform area = CreateRect("CardListArea", parent);
            area.anchorMin = new Vector2(0.03f, 0.08f);
            area.anchorMax = new Vector2(0.82f, 0.9f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = false; //仅作底色，不拦截射线，避免挡住上方工具栏按钮
            Text label = CreateText("AreaTitle", area, "卡牌列表", _font, 28,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 1);
            label.rectTransform.anchorMax = new Vector2(1, 1);
            label.rectTransform.pivot = new Vector2(0.5f, 1);
            label.rectTransform.anchoredPosition = new Vector2(0, -6);
            label.rectTransform.offsetMin = new Vector2(20, label.rectTransform.offsetMin.y);
            label.rectTransform.sizeDelta = new Vector2(0, 50);

            //新增卡按钮（右上角）
            panel.btn_add_card = CreateButton("AddCardBtn", area, "新增卡", _font, 22,
                new Color(0.5f, 0.9f, 0.6f, 0.4f));
            RectTransform art = panel.btn_add_card.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(1, 1);
            art.anchorMax = new Vector2(1, 1);
            art.pivot = new Vector2(1, 1);
            art.anchoredPosition = new Vector2(-16, -10);
            art.sizeDelta = new Vector2(110, 44);

            //卡牌列表（CardGrid，运行时复用 card_prefab 卡面）
            ScrollRect scroll = CreateCardListScroll(area);
            panel.card_scroll = scroll;
            panel.card_content = scroll.content;
            panel.card_grid = scroll.content.GetComponent<CardGrid>();

            //底部操作栏：复制 / 删除 / 保存（针对当前选中卡）
            BuildCardListOps(area, panel);
        }

        /// <summary>卡牌列表底部操作栏（复制/删除/保存）</summary>
        private static void BuildCardListOps(Transform parent, CardEditorPanel panel)
        {
            RectTransform bar = CreateRect("CardListOps", parent);
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.anchoredPosition = new Vector2(0, 8);
            bar.sizeDelta = new Vector2(0, 58);

            panel.btn_copy = CreateButton("CopyCardBtn", bar, "复制", _font, 22,
                new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform crt = panel.btn_copy.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(-170, 0);
            crt.sizeDelta = new Vector2(130, 44);

            panel.btn_delete = CreateButton("DeleteCardBtn", bar, "删除", _font, 22,
                new Color(1f, 0.6f, 0.6f, 0.4f));
            RectTransform drt = panel.btn_delete.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.5f, 0.5f);
            drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(0, 0);
            drt.sizeDelta = new Vector2(130, 44);

            panel.btn_save2 = CreateButton("SaveCardBtn", bar, "保存", _font, 22,
                new Color(0.5f, 0.9f, 0.6f, 0.4f));
            RectTransform srt = panel.btn_save2.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(170, 0);
            srt.sizeDelta = new Vector2(130, 44);
        }

        /// <summary>右侧编辑区（暂空，后续放卡池/卡牌参数编辑）</summary>
        private static void BuildEditorArea(Transform parent, CardEditorPanel panel)
        {
            RectTransform area = CreateRect("EditorArea", parent);
            area.anchorMin = new Vector2(0.84f, 0.08f);
            area.anchorMax = new Vector2(0.99f, 0.9f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            bg.raycastTarget = false; //仅作底色，不拦截射线，避免挡住上方工具栏按钮

            Text hint = CreateText("AreaTitle", area, "属性编辑区\n（后续开发）", _font, 26,
                new Color(1, 1, 1, 0.4f), TextAnchor.MiddleCenter);
            hint.rectTransform.anchorMin = Vector2.zero;
            hint.rectTransform.anchorMax = Vector2.one;
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            panel.editor_hint = hint;
        }

        private static ScrollRect CreateCardListScroll(Transform parent)
        {
            RectTransform scroll_rt = CreateRect("CardListScroll", parent);
            scroll_rt.anchorMin = new Vector2(0.03f, 0.16f);
            scroll_rt.anchorMax = new Vector2(0.97f, 0.9f);
            scroll_rt.offsetMin = Vector2.zero;
            scroll_rt.offsetMax = Vector2.zero;
            scroll_rt.pivot = new Vector2(0.5f, 0.5f);
            scroll_rt.sizeDelta = Vector2.zero;

            ScrollRect scroll = scroll_rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewport = CreateRect("Viewport", scroll_rt);
            SetStretch(viewport);
            Image vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1, 1, 1, 0.04f);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            //列表容器：CardGrid（排布完全复用卡组构筑器配置，由 CardEditorPanel 运行时复制）
            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);

            //GridLayoutGroup 具体参数（cellSize/列/间距）由 CardEditorPanel 运行时从卡组构筑器复制
            GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
            CardGrid card_grid = content.gameObject.AddComponent<CardGrid>();

            scroll.viewport = viewport;
            scroll.content = content;

            return scroll;
        }

        private static void BuildNodeArea(Transform parent, CardEditorPanel panel)
        {
            RectTransform area = CreateRect("NodeArea", parent);
            area.anchorMin = new Vector2(0.27f, 0.08f);
            area.anchorMax = new Vector2(0.99f, 0.9f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            Text label = CreateText("AreaTitle", area, "规则节点（P1 平铺展示）", _font, 28,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 1);
            label.rectTransform.anchorMax = new Vector2(1, 1);
            label.rectTransform.pivot = new Vector2(0.5f, 1);
            label.rectTransform.anchoredPosition = new Vector2(0, -6);
            label.rectTransform.offsetMin = new Vector2(20, label.rectTransform.offsetMin.y);
            label.rectTransform.sizeDelta = new Vector2(0, 50);

            ScrollRect scroll = CreateNodeScroll(area);
            panel.node_scroll = scroll;
            panel.node_content = scroll.content;
            panel.node_template = FindNodeTemplate(scroll.content);
        }

        private static ScrollRect CreateNodeScroll(Transform parent)
        {
            RectTransform scroll_rt = CreateRect("NodeScroll", parent);
            scroll_rt.anchorMin = new Vector2(0.02f, 0.04f);
            scroll_rt.anchorMax = new Vector2(0.98f, 0.9f);
            scroll_rt.offsetMin = Vector2.zero;
            scroll_rt.offsetMax = Vector2.zero;
            scroll_rt.pivot = new Vector2(0.5f, 0.5f);
            scroll_rt.sizeDelta = Vector2.zero;

            //画布是自由 2D 空间，非列表滚动：必须禁用 ScrollRect 滚动，
            //否则拖拽节点时 ScrollRect 会接管滚动、结束时 content 回弹导致节点弹回原位。
            ScrollRect scroll = scroll_rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.enabled = false;

            RectTransform viewport = CreateRect("Viewport", scroll_rt);
            SetStretch(viewport);
            Image vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1, 1, 1, 0.05f);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            //画布容器：大尺寸、无自动布局，节点在其中自由定位（P2 拖拽）
            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.zero;   //固定左下角，节点用绝对坐标定位
            content.pivot = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(3000, 2200);

            //注意：画布不添加 VerticalLayoutGroup/ContentSizeFitter，
            //节点用绝对坐标自由定位（自动布局会强制打乱节点位置）。

            scroll.viewport = viewport;
            scroll.content = content;

            //节点行模板（隐藏）
            GameObject line = CreateNodeLineTemplate(content);
            line.SetActive(false);

            return scroll;
        }

        private static GameObject CreateNodeLineTemplate(Transform content)
        {
            RectTransform line = CreateRect("NodeTemplate", content);
            line.anchorMin = Vector2.zero;      //固定左下角，运行时用绝对坐标定位
            line.anchorMax = new Vector2(0, 0) + Vector2.zero;
            line.pivot = Vector2.zero;
            line.anchoredPosition = Vector2.zero;
            line.sizeDelta = new Vector2(420, 70);   //固定尺寸，可拖拽的节点行

            Image bg = CreateImage("LineBG", line, new Color(1, 1, 1, 0.12f));
            SetStretch(bg.rectTransform);

            Text type = CreateText("TypeText", line, "动作", _font, 20, new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            type.rectTransform.anchorMin = new Vector2(0, 0);
            type.rectTransform.anchorMax = new Vector2(0.16f, 1);
            type.rectTransform.offsetMin = Vector2.zero;
            type.rectTransform.offsetMax = Vector2.zero;

            Text title = CreateText("TitleText", line, "标题", _font, 20, Color.white, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0.16f, 0);
            title.rectTransform.anchorMax = new Vector2(0.42f, 1);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;

            Text desc = CreateText("DescText", line, "描述", _font, 19, new Color(1, 1, 1, 0.75f), TextAnchor.MiddleLeft);
            desc.rectTransform.anchorMin = new Vector2(0.42f, 0);
            desc.rectTransform.anchorMax = new Vector2(1, 1);
            desc.rectTransform.offsetMin = Vector2.zero;
            desc.rectTransform.offsetMax = Vector2.zero;

            return line.gameObject;
        }

        private static GameObject FindNodeTemplate(RectTransform content)
        {
            foreach (Transform child in content)
            {
                if (child.name == "NodeTemplate")
                    return child.gameObject;
            }
            return null;
        }

        // ---------------- 卡池管理页「编辑」按钮 ----------------

        private static void AddEditButtonToPoolLines()
        {
            GameObject line = FindPoolLine();
            if (line == null)
            {
                Debug.LogWarning("未找到卡池管理页行模板，跳过编辑按钮");
                return;
            }

            //幂等：已存在编辑按钮则跳过
            Transform exist = FindChild(line.transform, "EditBtn");
            if (exist != null)
                return;

            Button btn = CreateButton("EditBtn", line.transform, "编辑", _font, 20, new Color(1f, 0.85f, 0.6f, 0.3f));
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            //与数量文本(-340~-230)、导出(-221~-149)、删除(-131~-59)错开：
            //放在 -440~-350 之间，中心 -395
            rt.anchoredPosition = new Vector2(-395, 0);
            rt.sizeDelta = new Vector2(86, 34);
        }

        /// <summary>
        /// 取卡池管理页行模板。直接取 CardPoolPanel.line_template 引用，
        /// 因为模板是隐藏的（SetActive(false)），而 GameObject.Find 找不到未激活对象。
        /// </summary>
        private static GameObject FindPoolLine()
        {
            CardPoolPanel panel = Object.FindObjectOfType<CardPoolPanel>();
            if (panel != null && panel.line_template != null)
                return panel.line_template;
            return null;
        }

        /// <summary>移除行模板上的旧编辑按钮（幂等）</summary>
        private static void RemoveEditButtons()
        {
            GameObject line = FindPoolLine();
            if (line == null)
                return;
            Transform exist = FindChild(line.transform, "EditBtn");
            if (exist != null)
                Object.DestroyImmediate(exist.gameObject);
        }

        // ---------------- UI 辅助（复用 CardFilterBuilder 的写法） ----------------

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

        private static void DestroyIfExists(string name)
        {
            //先收集名字匹配的对象，再统一销毁，
            //避免遍历中 DestroyImmediate 使数组内其它对象失效导致 MissingReferenceException。
            List<GameObject> targets = new List<GameObject>();
            GameObject[] all = Object.FindObjectsOfType<GameObject>(true);
            foreach (GameObject go in all)
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

        private static InputField CreateInput(Transform parent, string name, string placeholder, int order)
        {
            RectTransform rt = CreateRect(name, parent);
            float y = -62f - (order - 1) * 58f;
            rt.anchorMin = new Vector2(0.5f, 1);
            rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(260, 48);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.25f);
            InputField input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;

            Text placeholder_txt = CreateText("Placeholder", rt, placeholder, _font, 20, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            RectTransform ph_rt = placeholder_txt.rectTransform;
            ph_rt.anchorMin = Vector2.zero;
            ph_rt.anchorMax = Vector2.one;
            ph_rt.offsetMin = new Vector2(14, 0);
            ph_rt.offsetMax = Vector2.zero;

            Text display = CreateText("Text", rt, "", _font, 20, Color.white, TextAnchor.MiddleLeft);
            RectTransform d_rt = display.rectTransform;
            d_rt.anchorMin = Vector2.zero;
            d_rt.anchorMax = Vector2.one;
            d_rt.offsetMin = new Vector2(14, 0);
            d_rt.offsetMax = Vector2.zero;

            input.targetGraphic = bg;
            input.textComponent = display;
            input.placeholder = placeholder_txt;
            return input;
        }
    }
}