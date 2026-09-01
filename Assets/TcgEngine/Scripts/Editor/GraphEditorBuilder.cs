using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡牌规则编辑器页面搭建工具（编辑器菜单，非运行时）。
    /// 一键在 Menu.unity 场景中生成 GraphEditorPanel 全屏页面：
    ///   左侧 连线画布（平移/缩放 + 节点 + 引脚 + 连线模板）
    ///   右侧上 卡牌属性配置（名称/类型/数值/图片/音效）
    ///   右侧下 节点库（筛选器 + 节点列表）
    /// 生成后保存在场景中，可在 Inspector 中自由调整。
    /// </summary>
    public static class GraphEditorBuilder
    {
        private const string MENU = "TcgEngine/卡牌编辑器/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";
        private const string EXIT_ICON_PATH = "Assets/TcgEngine/Sprites/UI/exit.png";

        private static Font _font;

        [MenuItem(MENU + "生成规则编辑器页面到主菜单场景")]
        public static void BuildGraphEditor()
        {
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            //布局保护：若已存在旧页面，先询问是否保留，避免覆盖用户在场景中手动调整的布局
            Transform existing = FindTransform("GraphEditorPanel");
            if (existing != null)
            {
                bool rebuild = !EditorUtility.DisplayDialog("规则编辑器",
                    "场景中已存在规则编辑器页面（含你手动调整的布局）。\n\n" +
                    "「保留现有布局」：跳过重建，布局不变；\n" +
                    "「重建（默认布局）」：删除旧页面并按代码默认布局重新生成，手动调整会丢失。",
                    "保留现有布局", "重建（默认布局）");
                if (!rebuild)
                {
                    Debug.Log("规则编辑器：已存在页面，保留现有布局，跳过重建。");
                    return;
                }
                Object.DestroyImmediate(existing.gameObject);
                EditorSceneManager.SaveScene(scene);
            }

            Canvas canvas = GetMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("规则编辑器", "未找到 Canvas，请确认主菜单场景加载成功。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildPanel(canvas.transform);

            EditorSceneManager.SaveScene(scene);
            Debug.Log("规则编辑器页面已生成到 Menu.unity 并保存");
            EditorUtility.DisplayDialog("规则编辑器",
                "已生成卡牌规则编辑器页面。\n从卡牌编辑器「进行」按钮进入，编辑单张卡自己的规则图。\n已自动保存，可直接点 Play 测试。", "确定");
        }

        // ---------------- 页面 ----------------

        private static void BuildPanel(Transform canvas)
        {
            RectTransform root = CreateRect("GraphEditorPanel", canvas);
            SetStretch(root);

            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            GraphEditorPanel panel = root.gameObject.AddComponent<GraphEditorPanel>();

            //全屏背景遮罩：盖住整个游戏画面
            Image bg = CreateImage("Background", root, new Color(0f, 0f, 0f, 0.9f));
            bg.raycastTarget = false;
            SetStretch(bg.rectTransform);

            //顶部工具栏
            BuildTopBar(root, panel);

            //左侧画布
            BuildCanvasArea(root, panel);

            //右侧上：属性配置
            BuildPropArea(root, panel);

            //右侧下：节点库（先建，默认显示；参数区覆盖其上，运行时互斥切换）
            BuildLibArea(root, panel);

            //右侧下：节点参数编辑区（后建，位于节点库上层，选中节点时显示）
            BuildFieldArea(root, panel);

            //工具栏置顶：确保返回/保存等按钮不被各内容区域覆盖
            Transform topbar = root.Find("TopBar");
            if (topbar != null)
                topbar.SetAsLastSibling();

            //底部状态
            Text status = CreateText("StatusText", root, "", _font, 20, new Color(1, 0.85f, 0.6f, 1f), TextAnchor.MiddleRight);
            status.rectTransform.anchorMin = new Vector2(1, 0);
            status.rectTransform.anchorMax = new Vector2(1, 0);
            status.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            status.rectTransform.anchoredPosition = new Vector2(-220, 24);
            status.rectTransform.sizeDelta = new Vector2(520, 36);
            panel.status_text = status;

            //与项目其他面板一致：SetActive(true) 存储，运行时由 UIPanel 生命周期隐藏
            root.gameObject.SetActive(true);
        }

        private static void BuildTopBar(Transform parent, GraphEditorPanel panel)
        {
            RectTransform bar = CreateRect("TopBar", parent);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -60);
            bar.sizeDelta = new Vector2(0, 60);

            Text title = CreateText("TitleText", bar, "规则编辑器", _font, 34,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0, 0.5f);
            title.rectTransform.anchorMax = new Vector2(0, 0.5f);
            title.rectTransform.pivot = new Vector2(0, 0.5f);
            title.rectTransform.anchoredPosition = new Vector2(30, 0);
            title.rectTransform.sizeDelta = new Vector2(640, 50);
            panel.title_text = title;

            panel.btn_delete_node = CreateButton("DeleteNodeBtn", bar, "删除节点", _font, 22,
                new Color(1f, 0.6f, 0.6f, 0.4f));
            RectTransform dnt = panel.btn_delete_node.GetComponent<RectTransform>();
            dnt.anchorMin = new Vector2(1, 0.5f);
            dnt.anchorMax = new Vector2(1, 0.5f);
            dnt.pivot = new Vector2(0.5f, 0.5f);
            dnt.anchoredPosition = new Vector2(-620, 0);
            dnt.sizeDelta = new Vector2(130, 46);

            panel.btn_test = CreateButton("TestBtn", bar, "模拟测试", _font, 22,
                new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform tst = panel.btn_test.GetComponent<RectTransform>();
            tst.anchorMin = new Vector2(1, 0.5f);
            tst.anchorMax = new Vector2(1, 0.5f);
            tst.pivot = new Vector2(0.5f, 0.5f);
            tst.anchoredPosition = new Vector2(-470, 0);
            tst.sizeDelta = new Vector2(130, 46);

            panel.btn_save = CreateButton("SaveBtn", bar, "保存", _font, 22,
                new Color(0.5f, 0.9f, 0.6f, 0.4f));
            RectTransform srt = panel.btn_save.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1, 0.5f);
            srt.anchorMax = new Vector2(1, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(-320, 0);
            srt.sizeDelta = new Vector2(130, 46);

            panel.btn_close = CreateButton("CloseBtn", bar, "返回", _font, 24, new Color(1, 1, 1, 0.25f));
            RectTransform crt = panel.btn_close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1, 0.5f);
            crt.anchorMax = new Vector2(1, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(-60, 0);
            crt.sizeDelta = new Vector2(56, 56);
        }

        // ---------------- 左侧画布 ----------------

        private static void BuildCanvasArea(Transform parent, GraphEditorPanel panel)
        {
            RectTransform area = CreateRect("CanvasArea", parent);
            area.anchorMin = new Vector2(0.02f, 0.05f);
            area.anchorMax = new Vector2(0.75f, 0.9f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            //缩放按钮（左上角，不显示提示文字，只留按钮）
            panel.btn_zoom_in = CreateButton("ZoomInBtn", area, "放大", _font, 20,
                new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform zi = panel.btn_zoom_in.GetComponent<RectTransform>();
            zi.anchorMin = new Vector2(0, 1);
            zi.anchorMax = new Vector2(0, 1);
            zi.pivot = new Vector2(0.5f, 1);
            zi.anchoredPosition = new Vector2(60, -12);
            zi.sizeDelta = new Vector2(80, 40);

            panel.btn_zoom_out = CreateButton("ZoomOutBtn", area, "缩小", _font, 20,
                new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform zo = panel.btn_zoom_out.GetComponent<RectTransform>();
            zo.anchorMin = new Vector2(0, 1);
            zo.anchorMax = new Vector2(0, 1);
            zo.pivot = new Vector2(0.5f, 1);
            zo.anchoredPosition = new Vector2(150, -12);
            zo.sizeDelta = new Vector2(80, 40);

            panel.btn_reset = CreateButton("ResetBtn", area, "复位", _font, 20,
                new Color(1f, 1f, 1f, 0.25f));
            RectTransform rs = panel.btn_reset.GetComponent<RectTransform>();
            rs.anchorMin = new Vector2(0, 1);
            rs.anchorMax = new Vector2(0, 1);
            rs.pivot = new Vector2(0.5f, 1);
            rs.anchoredPosition = new Vector2(240, -12);
            rs.sizeDelta = new Vector2(80, 40);

            //画布滚动区
            ScrollRect scroll = CreateCanvasScroll(area);
            panel.canvas_scroll = scroll;
            panel.canvas_content = scroll.content;
            panel.graph_canvas = scroll.viewport.GetComponent<GraphCanvas>();
            Transform nt = FindChild(scroll.content, "NodeTemplate");
            panel.node_template = nt != null ? nt.gameObject : null;
            Transform lt = FindChild(scroll.content, "LinkTemplate");
            panel.link_template = lt != null ? lt.gameObject : null;
            Transform pt = FindChild(scroll.content, "PinTemplate");
            panel.pin_template = pt != null ? pt.gameObject : null;
        }

        private static ScrollRect CreateCanvasScroll(Transform parent)
        {
            RectTransform scroll_rt = CreateRect("CanvasScroll", parent);
            scroll_rt.anchorMin = new Vector2(0.01f, 0.04f);
            scroll_rt.anchorMax = new Vector2(0.99f, 0.94f);
            scroll_rt.offsetMin = Vector2.zero;
            scroll_rt.offsetMax = Vector2.zero;
            scroll_rt.pivot = new Vector2(0.5f, 0.5f);
            scroll_rt.sizeDelta = Vector2.zero;

            //画布是自由 2D 空间：禁用滚动，由 GraphCanvas 接管平移/缩放
            ScrollRect scroll = scroll_rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.enabled = false;

            RectTransform viewport = CreateRect("Viewport", scroll_rt);
            SetStretch(viewport);
            Image vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(0.05f, 0.06f, 0.09f, 0.6f);
            vimg.raycastTarget = true;   //接收空白处点击/滚轮（平移/缩放）
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            //画布容器：大尺寸、无自动布局，节点自由定位（锚左下角）
            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.zero;
            content.pivot = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(4000, 2600);

            scroll.viewport = viewport;
            scroll.content = content;

            //平移/缩放控制器（挂在 viewport 上）
            GraphCanvas canvas = viewport.gameObject.AddComponent<GraphCanvas>();
            canvas.content = content;

            //模板
            CreateNodeTemplate(content);
            CreateLinkTemplate(content);
            CreatePinTemplate(content);

            return scroll;
        }

        private static void CreateNodeTemplate(Transform content)
        {
            RectTransform node = CreateRect("NodeTemplate", content);
            node.anchorMin = Vector2.zero;
            node.anchorMax = Vector2.zero;
            node.pivot = Vector2.zero;
            node.anchoredPosition = Vector2.zero;
            node.sizeDelta = new Vector2(190, 110);

            Image bg = CreateImage("LineBG", node, new Color(1f, 1f, 1f, 0.12f));
            SetStretch(bg.rectTransform);
            bg.raycastTarget = true;   //接收拖拽/点击

            Text type = CreateText("TypeText", node, "动作", _font, 18, new Color(0.6f, 0.9f, 1f, 1f), TextAnchor.MiddleLeft);
            type.rectTransform.anchorMin = new Vector2(0, 1);
            type.rectTransform.anchorMax = new Vector2(1, 1);
            type.rectTransform.pivot = new Vector2(0.5f, 1);
            type.rectTransform.anchoredPosition = new Vector2(0, -4);
            type.rectTransform.offsetMin = new Vector2(12, type.rectTransform.offsetMin.y);
            type.rectTransform.sizeDelta = new Vector2(0, 26);

            Text title = CreateText("TitleText", node, "标题", _font, 22, Color.white, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(12, 32);
            title.rectTransform.offsetMax = new Vector2(-12, -30);

            Text desc = CreateText("DescText", node, "描述", _font, 16, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft);
            desc.rectTransform.anchorMin = new Vector2(0, 0);
            desc.rectTransform.anchorMax = new Vector2(1, 0);
            desc.rectTransform.pivot = new Vector2(0.5f, 0);
            desc.rectTransform.anchoredPosition = new Vector2(0, 6);
            desc.rectTransform.offsetMin = new Vector2(12, desc.rectTransform.offsetMin.y);
            desc.rectTransform.sizeDelta = new Vector2(0, 24);

            RectTransform pins = CreateRect("Pins", node);
            SetStretch(pins);

            node.gameObject.SetActive(false);
        }

        private static void CreateLinkTemplate(Transform content)
        {
            GameObject go = new GameObject("LinkTemplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(content, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(10, 2.5f);

            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 0.85f, 0.5f, 0.9f);
            img.raycastTarget = false;

            go.AddComponent<NodeLink>();
            go.SetActive(false);
        }

        private static void CreatePinTemplate(Transform content)
        {
            //外层：大命中区（44x44 透明），NodePin 挂在命中区上。
            //UGUI 拖拽启动要求"按下对象 == 移动后悬停对象"，引脚命中区够大，
            //按住后小幅移动仍落在其上，拖拽才能启动（否则连线拉不出来）。
            GameObject go = new GameObject("PinTemplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(content, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(44, 44);

            Image hit = go.GetComponent<Image>();
            hit.color = new Color(1, 1, 1, 0f);
            hit.raycastTarget = true;

            //内层：视觉圆点（18x18 居中，不接收射线）
            GameObject dot_go = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dot_go.transform.SetParent(go.transform, false);
            RectTransform dot_rt = dot_go.GetComponent<RectTransform>();
            dot_rt.anchorMin = new Vector2(0.5f, 0.5f);
            dot_rt.anchorMax = new Vector2(0.5f, 0.5f);
            dot_rt.pivot = new Vector2(0.5f, 0.5f);
            dot_rt.anchoredPosition = Vector2.zero;
            dot_rt.sizeDelta = new Vector2(18, 18);
            Image dot = dot_go.GetComponent<Image>();
            dot.color = Color.white;
            dot.raycastTarget = false;

            go.AddComponent<NodePin>();
            go.SetActive(false);
        }

        // ---------------- 右侧上：属性配置 ----------------

        private static void BuildPropArea(Transform parent, GraphEditorPanel panel)
        {
            RectTransform area = CreateRect("PropArea", parent);
            area.anchorMin = new Vector2(0.77f, 0.5f);
            area.anchorMax = new Vector2(0.99f, 0.9f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            Text label = CreateText("AreaTitle", area, "卡牌属性配置", _font, 24,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 1);
            label.rectTransform.anchorMax = new Vector2(1, 1);
            label.rectTransform.pivot = new Vector2(0.5f, 1);
            label.rectTransform.anchoredPosition = new Vector2(0, -6);
            label.rectTransform.offsetMin = new Vector2(14, label.rectTransform.offsetMin.y);
            label.rectTransform.sizeDelta = new Vector2(0, 40);

            //属性区滚动
            ScrollRect scroll = CreateRect("PropScroll", area).gameObject.AddComponent<ScrollRect>();
            RectTransform scroll_rt = scroll.GetComponent<RectTransform>();
            scroll_rt.anchorMin = new Vector2(0.02f, 0.02f);
            scroll_rt.anchorMax = new Vector2(0.98f, 0.92f);
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

            //纵向排布
            VerticalLayoutGroup vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 6, 6);
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

            //字段：属性名与输入内容同一行（左侧标签 + 右侧控件），用 LayoutElement 固定行高防塌陷
            panel.input_name = CreateInputIn(CreateFieldRow(content, "名称", 40), "请输入卡牌名称");
            panel.dropdown_type = CreateDropdownIn(CreateFieldRow(content, "类型", 40), new List<string>(TYPE_NAMES));
            panel.dropdown_team = CreateDropdownIn(CreateFieldRow(content, "阵营", 40), new List<string>());
            panel.dropdown_rarity = CreateDropdownIn(CreateFieldRow(content, "稀有度", 40), new List<string>());
            panel.input_mana = CreateInputIn(CreateFieldRow(content, "费用", 40), "法力费用");
            panel.input_attack = CreateInputIn(CreateFieldRow(content, "攻击", 40), "攻击力");
            panel.input_hp = CreateInputIn(CreateFieldRow(content, "生命", 40), "生命值");
            panel.dropdown_trait = CreateDropdownIn(CreateFieldRow(content, "种族", 40), new List<string>());
            panel.input_text = CreateInputIn(CreateFieldRow(content, "卡牌文本", 84), "卡牌效果描述", true);
            panel.input_desc = CreateInputIn(CreateFieldRow(content, "描述", 84), "背景故事/风味文本", true);
            panel.toggle_deckbuilding = CreateToggleIn(CreateFieldRow(content, "可组卡", 40), "可在卡组中使用");
            panel.input_cost = CreateInputIn(CreateFieldRow(content, "购买价", 40), "购买价格");

            //卡牌图片
            CreatePropLabel(content, "卡牌图片");
            RectTransform art_row = CreateRect("ArtRow", content);
            art_row.sizeDelta = new Vector2(0, 172);
            AddLayoutElement(art_row, 172);
            VerticalLayoutGroup art_vlg = art_row.gameObject.AddComponent<VerticalLayoutGroup>();
            art_vlg.spacing = 6;
            art_vlg.childAlignment = TextAnchor.UpperCenter;
            art_vlg.childControlHeight = false;
            art_vlg.childControlWidth = false;
            art_vlg.childForceExpandHeight = false;
            art_vlg.childForceExpandWidth = false;

            Image preview = CreateImage("ArtPreview", art_row, new Color(1, 1, 1, 0.1f));
            RectTransform prt = preview.rectTransform;
            prt.sizeDelta = new Vector2(90, 118);
            preview.raycastTarget = false;
            preview.preserveAspect = true;
            panel.art_preview = preview;

            panel.btn_pick_art = CreateButton("PickArtBtn", art_row, "选择图片", _font, 18,
                new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform brt = panel.btn_pick_art.GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(140, 36);

            //面板（全图）图片：法术/奥秘运行时隐藏
            panel.art_full_row = CreateRect("ArtFullRow", content);
            RectTransform afr = panel.art_full_row;
            afr.sizeDelta = new Vector2(0, 172);
            AddLayoutElement(afr, 172);
            VerticalLayoutGroup afr_vlg = afr.gameObject.AddComponent<VerticalLayoutGroup>();
            afr_vlg.spacing = 6;
            afr_vlg.childAlignment = TextAnchor.UpperCenter;
            afr_vlg.childControlHeight = false;
            afr_vlg.childControlWidth = false;
            afr_vlg.childForceExpandHeight = false;
            afr_vlg.childForceExpandWidth = false;

            Image full_preview = CreateImage("ArtFullPreview", afr, new Color(1, 1, 1, 0.1f));
            RectTransform fprt = full_preview.rectTransform;
            fprt.sizeDelta = new Vector2(90, 118);
            full_preview.raycastTarget = false;
            full_preview.preserveAspect = true;
            panel.art_full_preview = full_preview;

            panel.btn_pick_full_art = CreateButton("PickFullArtBtn", afr, "选择面板图片", _font, 18,
                new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform fbrt = panel.btn_pick_full_art.GetComponent<RectTransform>();
            fbrt.sizeDelta = new Vector2(140, 36);

            CreatePropLabel(content, "音乐配置（存于 Workshop/Audio）");
            CreateAudioRow(content, panel, "打出音效", ref panel.input_audio_spawn, ref panel.btn_audio_spawn);
            CreateAudioRow(content, panel, "攻击音效", ref panel.input_audio_attack, ref panel.btn_audio_attack);
            CreateAudioRow(content, panel, "死亡音效", ref panel.input_audio_death, ref panel.btn_audio_death);
            CreateAudioRow(content, panel, "受伤音效", ref panel.input_audio_damage, ref panel.btn_audio_damage);
        }

        /// <summary>属性行：左侧属性名 + 右侧控件区（同一行，返回右侧控件区 RectTransform）</summary>
        private static RectTransform CreateFieldRow(Transform parent, string label, float height)
        {
            RectTransform row = CreateRect("PropRow", parent);
            row.sizeDelta = new Vector2(0, height);
            AddLayoutElement(row, height);

            Text lb = CreateText("PropLabel", row, label, _font, 16,
                new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft);
            RectTransform lrt = lb.rectTransform;
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(88, 0);

            RectTransform field = CreateRect("Field", row);
            field.anchorMin = new Vector2(0, 0);
            field.anchorMax = new Vector2(1, 1);
            field.offsetMin = new Vector2(92, 0);
            field.offsetMax = Vector2.zero;
            return field;
        }

        /// <summary>在指定控件区创建输入框（填充该区域）</summary>
        private static InputField CreateInputIn(RectTransform field, string placeholder, bool multiline = false)
        {
            RectTransform rt = CreateRect("PropInput", field);
            SetStretch(rt);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.25f);

            Text placeholder_txt = CreateText("Placeholder", rt, placeholder, _font, 16,
                new Color(1, 1, 1, 0.5f), TextAnchor.UpperLeft);
            RectTransform ph_rt = placeholder_txt.rectTransform;
            ph_rt.anchorMin = Vector2.zero;
            ph_rt.anchorMax = Vector2.one;
            ph_rt.offsetMin = new Vector2(10, 4);
            ph_rt.offsetMax = Vector2.zero;

            Text display = CreateText("Text", rt, "", _font, 16, Color.white, TextAnchor.UpperLeft);
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
                display.alignment = TextAnchor.UpperLeft;
                display.horizontalOverflow = HorizontalWrapMode.Wrap;
                display.verticalOverflow = VerticalWrapMode.Truncate;
            }
            return input;
        }

        /// <summary>在指定控件区创建下拉框（填充该区域）</summary>
        private static Dropdown CreateDropdownIn(RectTransform field, List<string> options)
        {
            Dropdown dd = CreateDropdown("PropDropdown", field, options);
            SetStretch(dd.GetComponent<RectTransform>());
            return dd;
        }

        /// <summary>在指定控件区创建开关（勾选框 + 文本）</summary>
        private static Toggle CreateToggleIn(RectTransform field, string text)
        {
            RectTransform rt = CreateRect("PropToggle", field);
            SetStretch(rt);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.03f);
            bg.raycastTarget = true;

            RectTransform check = CreateRect("Checkmark", rt);
            check.anchorMin = new Vector2(0, 0.5f);
            check.anchorMax = new Vector2(0, 0.5f);
            check.pivot = new Vector2(0.5f, 0.5f);
            check.anchoredPosition = new Vector2(20, 0);
            check.sizeDelta = new Vector2(26, 26);
            Image check_img = check.gameObject.AddComponent<Image>();
            check_img.color = new Color(1, 1, 1, 0.2f);

            Text check_text = CreateText("CheckText", check, "✔", _font, 18, Color.white, TextAnchor.MiddleCenter);
            SetStretch(check_text.rectTransform);

            Toggle toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = check_img;
            toggle.graphic = check_text;
            toggle.isOn = true;

            Text lbl = CreateText("ToggleLabel", rt, text, _font, 16, Color.white, TextAnchor.MiddleLeft);
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(1, 1);
            lrt.offsetMin = new Vector2(44, 0);
            lrt.offsetMax = Vector2.zero;
            return toggle;
        }

        /// <summary>音效行：标签 + 文件名输入框 + 「选择音频」按钮（弹出系统文件对话框）</summary>
        private static void CreateAudioRow(Transform parent, GraphEditorPanel panel, string label,
            ref InputField input_ref, ref Button btn_ref)
        {
            RectTransform row = CreateFieldRow(parent, label, 40);
            row.name = "AudioRow";

            RectTransform input_rt = CreateRect("AudioInput", row);
            input_rt.anchorMin = new Vector2(0, 0);
            input_rt.anchorMax = new Vector2(0.68f, 1);
            input_rt.offsetMin = Vector2.zero;
            input_rt.offsetMax = Vector2.zero;

            Image bg = input_rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.25f);

            Text placeholder_txt = CreateText("Placeholder", input_rt, "音频文件名", _font, 16,
                new Color(1, 1, 1, 0.5f), TextAnchor.UpperLeft);
            RectTransform ph_rt = placeholder_txt.rectTransform;
            ph_rt.anchorMin = Vector2.zero;
            ph_rt.anchorMax = Vector2.one;
            ph_rt.offsetMin = new Vector2(10, 4);
            ph_rt.offsetMax = Vector2.zero;

            Text display = CreateText("Text", input_rt, "", _font, 16, Color.white, TextAnchor.UpperLeft);
            RectTransform d_rt = display.rectTransform;
            d_rt.anchorMin = Vector2.zero;
            d_rt.anchorMax = Vector2.one;
            d_rt.offsetMin = new Vector2(10, 4);
            d_rt.offsetMax = Vector2.zero;

            InputField input = input_rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = display;
            input.placeholder = placeholder_txt;
            input_ref = input;

            btn_ref = CreateButton("PickAudioBtn", row, "选择音频", _font, 15,
                new Color(0.5f, 0.78f, 1f, 0.4f));
            RectTransform brt = btn_ref.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.72f, 0);
            brt.anchorMax = new Vector2(1, 1);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
        }

        private static Text CreatePropLabel(Transform parent, string text)
        {
            Text t = CreateText("PropLabel", parent, text, _font, 18,
                new Color(1, 1, 1, 0.65f), TextAnchor.MiddleLeft);
            t.rectTransform.sizeDelta = new Vector2(0, 30);
            AddLayoutElement(t.rectTransform, 30);
            return t;
        }

        private static InputField CreatePropInput(Transform parent, string placeholder, bool multiline = false)
        {
            RectTransform rt = CreateRect("PropInput", parent);
            rt.sizeDelta = new Vector2(0, multiline ? 90 : 46);
            AddLayoutElement(rt, multiline ? 90 : 46);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.25f);

            Text placeholder_txt = CreateText("Placeholder", rt, placeholder, _font, 18,
                new Color(1, 1, 1, 0.5f), TextAnchor.UpperLeft);
            RectTransform ph_rt = placeholder_txt.rectTransform;
            ph_rt.anchorMin = Vector2.zero;
            ph_rt.anchorMax = Vector2.one;
            ph_rt.offsetMin = new Vector2(12, 4);
            ph_rt.offsetMax = Vector2.zero;

            Text display = CreateText("Text", rt, "", _font, 18, Color.white, TextAnchor.UpperLeft);
            RectTransform d_rt = display.rectTransform;
            d_rt.anchorMin = Vector2.zero;
            d_rt.anchorMax = Vector2.one;
            d_rt.offsetMin = new Vector2(12, 4);
            d_rt.offsetMax = Vector2.zero;

            InputField input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = display;
            input.placeholder = placeholder_txt;
            if (multiline)
            {
                input.lineType = InputField.LineType.MultiLineNewline;
                display.alignment = TextAnchor.UpperLeft;
                display.horizontalOverflow = HorizontalWrapMode.Wrap;
                display.verticalOverflow = VerticalWrapMode.Truncate;
            }
            return input;
        }

        private static Dropdown CreatePropDropdown(Transform parent, string placeholder, string[] options)
        {
            Dropdown dd = CreateDropdown("PropDropdown", parent, new List<string>(options));
            RectTransform rt = dd.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 46);
            AddLayoutElement(rt, 46);
            return dd;
        }

        // ---------------- 右侧中：节点参数编辑区 ----------------

        private static void BuildFieldArea(Transform parent, GraphEditorPanel panel)
        {
            RectTransform area = CreateRect("FieldArea", parent);
            area.anchorMin = new Vector2(0.77f, 0.04f);
            area.anchorMax = new Vector2(0.99f, 0.48f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;
            panel.node_field_root = area.gameObject;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            Text label = CreateText("AreaTitle", area, "节点参数", _font, 24,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 1);
            label.rectTransform.anchorMax = new Vector2(1, 1);
            label.rectTransform.pivot = new Vector2(0.5f, 1);
            label.rectTransform.anchoredPosition = new Vector2(0, -6);
            label.rectTransform.offsetMin = new Vector2(14, label.rectTransform.offsetMin.y);
            label.rectTransform.sizeDelta = new Vector2(0, 40);

            //占位提示：未选中节点时覆盖整区居中显示
            Text hint = CreateText("Hint", area, "点击画布中的节点，可在此编辑其参数（数值 / 符号 / 开关）", _font, 16,
                new Color(1, 1, 1, 0.55f), TextAnchor.MiddleCenter);
            SetStretch(hint.rectTransform);
            hint.rectTransform.offsetMin = new Vector2(16, 40);
            hint.rectTransform.offsetMax = new Vector2(-16, -40);
            panel.node_field_hint = hint;

            //参数滚动区
            ScrollRect scroll = CreateRect("FieldScroll", area).gameObject.AddComponent<ScrollRect>();
            RectTransform scroll_rt = scroll.GetComponent<RectTransform>();
            scroll_rt.anchorMin = new Vector2(0.02f, 0.04f);
            scroll_rt.anchorMax = new Vector2(0.98f, 0.94f);
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

            panel.node_field_area = content;
            panel.node_field_input_template = CreateFieldInputTemplate(content);
            panel.node_field_dropdown_template = CreateFieldDropdownTemplate(content);
            panel.node_field_toggle_template = CreateFieldToggleTemplate(content);

            //默认隐藏参数面板：与节点库同位置，运行时由 ShowFieldPanel 互斥切换
            area.gameObject.SetActive(false);
        }

        /// <summary>参数行模板公共部分：左侧显示名 + 右侧控件区（返回整行，控件区子对象名为 Field）</summary>
        private static RectTransform CreateFieldRowBase(Transform parent, string name)
        {
            RectTransform row = CreateRect(name, parent);
            row.sizeDelta = new Vector2(0, 40);
            AddLayoutElement(row, 40);

            Text lb = CreateText("Label", row, "参数", _font, 16,
                new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft);
            RectTransform lrt = lb.rectTransform;
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(84, 0);

            RectTransform field = CreateRect("Field", row);
            field.anchorMin = new Vector2(0, 0);
            field.anchorMax = new Vector2(1, 1);
            field.offsetMin = new Vector2(90, 0);
            field.offsetMax = Vector2.zero;
            return row;
        }

        private static GameObject CreateFieldInputTemplate(Transform parent)
        {
            RectTransform row = CreateFieldRowBase(parent, "FieldInputTemplate");
            RectTransform field = row.Find("Field") as RectTransform;

            RectTransform rt = CreateRect("PropInput", field);
            SetStretch(rt);
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.25f);

            Text placeholder_txt = CreateText("Placeholder", rt, "请输入", _font, 15,
                new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            SetStretch(placeholder_txt.rectTransform);
            placeholder_txt.rectTransform.offsetMin = new Vector2(8, 0);

            Text display = CreateText("Text", rt, "", _font, 16, Color.white, TextAnchor.MiddleLeft);
            SetStretch(display.rectTransform);
            display.rectTransform.offsetMin = new Vector2(8, 0);

            InputField input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = display;
            input.placeholder = placeholder_txt;

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        private static GameObject CreateFieldDropdownTemplate(Transform parent)
        {
            RectTransform row = CreateFieldRowBase(parent, "FieldDropdownTemplate");
            RectTransform field = row.Find("Field") as RectTransform;
            Dropdown dd = CreateDropdown("PropDropdown", field, new List<string> { "选项" });
            SetStretch(dd.GetComponent<RectTransform>());
            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        private static GameObject CreateFieldToggleTemplate(Transform parent)
        {
            RectTransform row = CreateFieldRowBase(parent, "FieldToggleTemplate");
            RectTransform field = row.Find("Field") as RectTransform;

            RectTransform rt = CreateRect("PropToggle", field);
            SetStretch(rt);
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.03f);
            bg.raycastTarget = true;

            RectTransform check = CreateRect("Checkmark", rt);
            check.anchorMin = new Vector2(0, 0.5f);
            check.anchorMax = new Vector2(0, 0.5f);
            check.pivot = new Vector2(0.5f, 0.5f);
            check.anchoredPosition = new Vector2(20, 0);
            check.sizeDelta = new Vector2(26, 26);
            Image check_img = check.gameObject.AddComponent<Image>();
            check_img.color = new Color(1, 1, 1, 0.2f);

            Text check_text = CreateText("CheckText", check, "✔", _font, 18, Color.white, TextAnchor.MiddleCenter);
            SetStretch(check_text.rectTransform);

            Toggle toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = check_img;
            toggle.graphic = check_text;
            toggle.isOn = true;

            Text lbl = CreateText("ToggleLabel", rt, "开启", _font, 16, Color.white, TextAnchor.MiddleLeft);
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(1, 1);
            lrt.offsetMin = new Vector2(44, 0);
            lrt.offsetMax = Vector2.zero;

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        // ---------------- 右侧下：节点库 ----------------

        private static void BuildLibArea(Transform parent, GraphEditorPanel panel)
        {
            RectTransform area = CreateRect("LibArea", parent);
            area.anchorMin = new Vector2(0.77f, 0.04f);
            area.anchorMax = new Vector2(0.99f, 0.48f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = Vector2.zero;
            panel.node_lib_root = area.gameObject;

            Image bg = area.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            //标题 + 数量
            Text label = CreateText("AreaTitle", area, "节点库", _font, 24,
                new Color(0.76f, 1f, 0.99f, 1f), TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 1);
            label.rectTransform.anchorMax = new Vector2(1, 1);
            label.rectTransform.pivot = new Vector2(0.5f, 1);
            label.rectTransform.anchoredPosition = new Vector2(0, -6);
            label.rectTransform.offsetMin = new Vector2(14, label.rectTransform.offsetMin.y);
            label.rectTransform.sizeDelta = new Vector2(0, 40);
            panel.node_lib_count = label;

            //筛选按钮行（全部/触发/条件/动作/数值）
            RectTransform filter_bar = CreateRect("FilterBar", area);
            filter_bar.anchorMin = new Vector2(0.02f, 1);
            filter_bar.anchorMax = new Vector2(0.98f, 1);
            filter_bar.pivot = new Vector2(0.5f, 1);
            filter_bar.anchoredPosition = new Vector2(0, -46);
            filter_bar.sizeDelta = new Vector2(0, 40);

            string[] filters = { "全部", "触发", "条件", "动作", "数值" };
            panel.filter_buttons = new Button[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                Button btn = CreateButton("FilterBtn" + i, filter_bar, filters[i], _font, 18,
                    new Color(0.3f, 0.45f, 0.6f, 0.5f));
                RectTransform brt = btn.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0, 0.5f);
                brt.anchorMax = new Vector2(0, 0.5f);
                brt.pivot = new Vector2(0, 0.5f);
                brt.anchoredPosition = new Vector2(10 + i * 96, 0);
                brt.sizeDelta = new Vector2(88, 36);
                panel.filter_buttons[i] = btn;
            }

            //节点列表滚动区
            ScrollRect scroll = CreateRect("LibScroll", area).gameObject.AddComponent<ScrollRect>();
            RectTransform scroll_rt = scroll.GetComponent<RectTransform>();
            scroll_rt.anchorMin = new Vector2(0.02f, 0.02f);
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

            panel.node_lib_scroll = scroll;
            panel.node_lib_content = content;
            panel.node_lib_template = CreateNodeLibTemplate(content);
        }

        private static GameObject CreateNodeLibTemplate(Transform content)
        {
            RectTransform item = CreateRect("NodeLibTemplate", content);
            item.sizeDelta = new Vector2(0, 40);
            AddLayoutElement(item, 40);   //固定项高（单行，压缩以多显示几行）

            Image bg = item.gameObject.AddComponent<Image>();
            bg.color = new Color(0.3f, 0.45f, 0.6f, 0.3f);
            bg.raycastTarget = true;
            item.gameObject.AddComponent<Button>();

            //只显示一个节点名（去掉类型标签与描述，行内上下居中，压缩成单行）
            Text title = CreateText("TitleText", item, "标题", _font, 16, Color.white, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(12, 0);
            title.rectTransform.offsetMax = new Vector2(-12, 0);

            item.gameObject.SetActive(false);
            return item.gameObject;
        }

        // ---------------- UI 辅助 ----------------

        /// <summary>给对象添加 LayoutElement 固定首选高度（配合 VerticalLayoutGroup/ContentSizeFitter 防塌陷）</summary>
        private static void AddLayoutElement(RectTransform rt, float height)
        {
            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }

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

        private static Transform FindTransform(string name)
        {
            Transform[] all = Object.FindObjectsOfType<Transform>(true);
            foreach (Transform t in all)
            {
                if (t != null && t.name == name)
                    return t;
            }
            return null;
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

        private static Dropdown CreateDropdown(string name, Transform parent, List<string> options)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();

            Image img = go.GetComponent<Image>();
            img.color = new Color(1, 1, 1, 0.25f);

            Dropdown dd = go.GetComponent<Dropdown>();
            dd.targetGraphic = img;

            Text caption = CreateText("Label", go.transform, options.Count > 0 ? options[0] : "", _font, 18, Color.white, TextAnchor.MiddleLeft);
            RectTransform caption_rt = caption.rectTransform;
            caption_rt.anchorMin = Vector2.zero;
            caption_rt.anchorMax = Vector2.one;
            caption_rt.offsetMin = new Vector2(14, 0);
            caption_rt.offsetMax = new Vector2(-14, 0);
            dd.captionText = caption;

            dd.ClearOptions();
            dd.AddOptions(options);

            SetupDropdownTemplate(dd, rt);
            return dd;
        }

        private static void SetupDropdownTemplate(Dropdown dd, RectTransform dd_rt)
        {
            RectTransform template = CreateRect("Template", dd_rt);
            template.anchorMin = new Vector2(0, 0);
            template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(0.5f, 1);
            template.anchoredPosition = new Vector2(0, 0);
            template.sizeDelta = new Vector2(0, 120);
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

            RectTransform item = CreateRect("Item", content);
            item.anchorMin = new Vector2(0, 0.5f);
            item.anchorMax = new Vector2(1, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.anchoredPosition = new Vector2(0, -14);
            item.sizeDelta = new Vector2(0, 28);

            Image item_img = item.gameObject.AddComponent<Image>();
            item_img.color = new Color(1, 1, 1, 0f);

            RectTransform item_bg = CreateRect("Item Background", item);
            SetStretch(item_bg);
            Image item_bg_img = item_bg.gameObject.AddComponent<Image>();
            item_bg_img.color = new Color(1, 1, 1, 0.1f);
            item_bg_img.raycastTarget = false;

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
            item_text.fontSize = 18;
            item_text.color = Color.white;
            item_text.alignment = TextAnchor.MiddleLeft;

            scroll.viewport = viewport;
            scroll.content = content;

            dd.template = template;
            dd.itemText = item_text;
            dd.itemImage = item_bg_img;
        }

        private static readonly string[] TYPE_NAMES = { "随从", "法术", "英雄", "神器", "奥秘", "装备" };
    }
}
