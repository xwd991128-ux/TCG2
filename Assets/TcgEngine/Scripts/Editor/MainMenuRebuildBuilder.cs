using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace TcgEngine.UI
{
    /// <summary>
    /// 主菜单界面重构工具（编辑器菜单，非运行时）。
    /// 一键在 Menu.unity 场景中：
    ///   1. 隐藏顶部导航栏 TopBar（并停用其旧导航标签，避免启动时误显示旧页面）
    ///   2. 生成首页 HomePanel：顶部玩家信息（头像/用户名/金币/Logo）+ 中央 2×3 六大模块入口
    ///      （对战/卡牌/卡包/排行榜/卡池/设置）+ 右下角退出登录/退出游戏按钮
    ///   3. 为六大全屏页面左上角添加「返回」按钮（HomeReturnButton）
    /// 生成后保存在场景中，可在 Inspector 中自由调整。
    /// </summary>
    public static class MainMenuRebuildBuilder
    {
        private const string MENU = "TcgEngine/主菜单/";
        private const string MENU_SCENE = "Assets/TcgEngine/Scenes/Menu/Menu.unity";
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";
        private const string EXIT_ICON_PATH = "Assets/TcgEngine/Sprites/UI/exit.png";

        private static Font _font;

        // ---------------- 菜单入口 ----------------

        [MenuItem(MENU + "重构主菜单界面")]
        public static void RebuildMainMenu()
        {
            // 强制切换到主菜单场景并在其中生成 + 自动保存
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);

            // 幂等：删除旧首页（保留各页面已有的手动调整）
            DestroyIfExists("HomePanel");

            Canvas canvas = GetMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("主菜单重构", "未找到 Canvas，请确认主菜单场景加载成功。", "确定");
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            HomePanel home = BuildHomePanel(canvas.transform);
            BindMainMenu(home);
            HideTopBar();
            AddReturnButtons();
            SetupCanvasScaler();

            EditorSceneManager.SaveScene(scene);
            Debug.Log("主菜单界面已重构并保存");
            EditorUtility.DisplayDialog("主菜单重构",
                "已完成：\n" +
                "1. 隐藏顶部导航栏 TopBar\n" +
                "2. 生成首页 HomePanel（顶部玩家信息 + 2×3 六大模块入口 + 退出按钮）\n" +
                "3. 为对战/卡牌/卡包/排行榜/卡池/设置 六个页面添加「返回」按钮\n" +
                "已自动保存场景，可直接点 Play 测试。", "确定");
        }

        [MenuItem(MENU + "取消按钮统一换成exit图标")]
        public static void ReplaceCancelButtonsWithExit()
        {
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);
            int count = 0;
            Transform[] all = Object.FindObjectsOfType<Transform>(true);
            foreach (Transform t in all)
            {
                string n = t.name;
                if (!(n.Contains("CloseBtn") || n.Contains("ReturnBtn") || n.Contains("ExitBtn")))
                    continue;
                if (t.GetComponent<Image>() == null)
                    continue;
                ApplyExitIcon(t.gameObject);
                RectTransform rt = t.GetComponent<RectTransform>();
                if (rt != null)
                    rt.sizeDelta = new Vector2(56, 56);
                count++;
            }
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("取消按钮替换", "已将 " + count + " 个取消/返回按钮换成 exit 图标并保存场景。", "确定");
        }

        [MenuItem(MENU + "移除设置界面的小X关闭按钮")]
        public static void RemoveSettingsClose()
        {
            Scene scene = EditorSceneManager.OpenScene(MENU_SCENE);
            int removed = 0;

            SettingsPanel panel = Object.FindObjectOfType<SettingsPanel>();
            if (panel != null)
            {
                Transform close = FindChild(panel.transform, "Close");
                if (close != null)
                {
                    Object.DestroyImmediate(close.gameObject);
                    removed++;
                }
            }

            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("设置界面", "已移除设置界面小X按钮 " + removed + " 个并保存场景。", "确定");
        }

        /// <summary>递归查找子对象</summary>
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

        // ---------------- 首页生成 ----------------

        private static HomePanel BuildHomePanel(Transform canvas)
        {
            // 根页面：全屏拉伸，置顶显示
            RectTransform root = CreateRect("HomePanel", canvas);
            SetStretch(root);
            root.SetAsLastSibling();

            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f; // 编辑器内可见方便调整；运行时 UIPanel 会淡入淡出
            group.interactable = true;
            group.blocksRaycasts = true;

            HomePanel home = root.gameObject.AddComponent<HomePanel>();

            TabButton home_tab = root.gameObject.AddComponent<TabButton>();
            home_tab.group = "home";
            home_tab.active = true;
            home_tab.highlight = null;
            home_tab.ui_panel = home;

            // 背景：复用主菜单背景图（与对战页一致），无则用深色底
            Image bg = CreateImage("Background", root, new Color(0.08f, 0.1f, 0.16f, 1f));
            SetStretch(bg.rectTransform);
            bg.raycastTarget = false;
            GameObject play_bg = GameObject.Find("PlayPanel/Background");
            if (play_bg != null)
            {
                Image src = play_bg.GetComponent<Image>();
                if (src != null && src.sprite != null)
                    bg.sprite = src.sprite;
            }

            // 顶部信息：复制 TopBar 的 Logo / 头像 / 用户名 / 金币（样式 100% 一致）
            // 注意：TopBar 已被上次运行隐藏（inactive），GameObject.Find 找不到，需用 includeInactive 查找
            Transform topbar = FindTopBar();
            if (topbar != null)
            {
                BuildTopInfo(topbar, root, home);
            }

            // 中央 2×3 六大模块按钮
            BuildModuleGrid(root);

            // 右下角退出按钮
            BuildExitButtons(root, home);

            return home;
        }

        private static void BuildTopInfo(Transform topbar, Transform parent, HomePanel home)
        {
            // Logo：右上角
            Transform logo_src = topbar.Find("Logo");
            if (logo_src != null)
            {
                GameObject logo = Object.Instantiate(logo_src.gameObject, parent, false);
                logo.name = "Logo";
                RectTransform lrt = logo.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(1, 1);
                lrt.anchorMax = new Vector2(1, 1);
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = new Vector2(-90, -64);
                lrt.sizeDelta = new Vector2(151.45f, 112.76f);
            }

            // 头像：左上角
            Transform avatar_src = topbar.Find("Avatar");
            if (avatar_src != null)
            {
                GameObject avatar_go = Object.Instantiate(avatar_src.gameObject, parent, false);
                avatar_go.name = "Avatar";
                RectTransform art = avatar_go.GetComponent<RectTransform>();
                art.anchorMin = new Vector2(0, 1);
                art.anchorMax = new Vector2(0, 1);
                art.pivot = new Vector2(0.5f, 0.5f);
                art.anchoredPosition = new Vector2(110, -70);
                art.sizeDelta = new Vector2(120, 120);
                home.avatar = avatar_go.GetComponent<AvatarUI>();
            }

            // 用户名：头像右侧，左边缘锚定（左对齐文字从头像右侧开始，避免覆盖头像）
            Transform name_src = topbar.Find("PlayerName");
            if (name_src != null)
            {
                GameObject ngo = Object.Instantiate(name_src.gameObject, parent, false);
                ngo.name = "PlayerName";
                RectTransform nrt = ngo.GetComponent<RectTransform>();
                nrt.anchorMin = new Vector2(0, 1);
                nrt.anchorMax = new Vector2(0, 1);
                nrt.pivot = new Vector2(0, 0.5f);   // 左边缘锚定
                nrt.anchoredPosition = new Vector2(190, -50); // 头像右边缘170，留20间距
                nrt.sizeDelta = new Vector2(560, 60);
                home.username_txt = ngo.GetComponent<Text>();
            }

            // 金币：头像右侧、用户名下方（同样左边缘锚定，不覆盖头像）
            Transform credits_src = topbar.Find("Credits");
            if (credits_src != null)
            {
                GameObject cgo = Object.Instantiate(credits_src.gameObject, parent, false);
                cgo.name = "Credits";
                RectTransform crt = cgo.GetComponent<RectTransform>();
                crt.anchorMin = new Vector2(0, 1);
                crt.anchorMax = new Vector2(0, 1);
                crt.pivot = new Vector2(0, 0.5f);   // 左边缘锚定
                crt.anchoredPosition = new Vector2(190, -110);
                crt.sizeDelta = new Vector2(560, 44);
                home.credits_txt = cgo.GetComponent<Text>();
            }
        }

        private static void BuildModuleGrid(Transform parent)
        {
            // 模板：直接复用对战页大按钮样式（PlaySolo），保持原外观
            Transform template = GameObject.Find("PlayPanel/PlaySolo")?.transform;

            string[] names = { "ModPlay", "ModCollection", "ModPacks", "ModLeaderboard", "ModCardPool", "ModSettings" };
            string[] labels = { "对战", "卡牌", "卡包", "排行榜", "卡池", "设置" };
            string[] panel_names = { "PlayPanel", "CollectionPanel", "PackPanel", "Loaderboard", "CardPoolPanel", "SettingsPanel" };
            Vector2[] poses = {
                new Vector2(-320, 190), new Vector2(0, 190), new Vector2(320, 190),
                new Vector2(-320, -190), new Vector2(0, -190), new Vector2(320, -190)
            };

            for (int i = 0; i < names.Length; i++)
            {
                GameObject target = GameObject.Find(panel_names[i]);
                if (target == null)
                {
                    Debug.LogWarning("主菜单重构：未找到页面 " + panel_names[i] + "，模块「" + labels[i] + "」跳过");
                    continue;
                }
                UIPanel panel = target.GetComponent<UIPanel>();
                if (panel == null)
                {
                    Debug.LogWarning("主菜单重构：页面 " + panel_names[i] + " 缺少 UIPanel 组件");
                    continue;
                }

                if (template == null)
                {
                    Debug.LogWarning("主菜单重构：未找到对战页按钮模板 PlaySolo，模块「" + labels[i] + "」使用简易按钮");
                    CreateModuleButtonFallback(names[i], parent, labels[i], panel, poses[i], _font);
                    continue;
                }

                // 复用对战按钮外观
                GameObject go = Object.Instantiate(template.gameObject, parent, false);
                go.name = names[i];

                // 清空旧的持久点击绑定（原 PlaySolo 点击进入单人对战）
                Button btn = go.GetComponent<Button>();
                if (btn != null)
                    btn.onClick = new Button.ButtonClickedEvent();

                // 修改文字为模块名
                Transform txt_t = go.transform.Find("Text");
                if (txt_t != null)
                {
                    Text txt = txt_t.GetComponent<Text>();
                    if (txt != null) txt.text = labels[i];
                }

                // 布局：保持原尺寸（269×326），2 行 3 列居中排布
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = poses[i];

                TabButton tab = go.AddComponent<TabButton>();
                tab.group = "home";
                tab.active = false;
                tab.highlight = null;
                tab.ui_panel = panel;
            }
        }

        /// <summary>模板缺失时的兜底：简易色块按钮</summary>
        private static void CreateModuleButtonFallback(string name, Transform parent, string label, UIPanel panel, Vector2 pos, Font font)
        {
            Button btn = CreateButton(name, parent, label, font, 34, new Color(0.15f, 0.25f, 0.45f, 0.75f));
            RectTransform brt = btn.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = pos;
            brt.sizeDelta = new Vector2(280, 170);

            TabButton tab = btn.gameObject.AddComponent<TabButton>();
            tab.group = "home";
            tab.active = false;
            tab.highlight = null;
            tab.ui_panel = panel;
        }

        private static void BuildExitButtons(Transform parent, HomePanel home)
        {
            Button logout = CreateButton("LogoutBtn", parent, "退出登录", _font, 22, new Color(1, 1, 1, 0.25f));
            RectTransform lrt = logout.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(1, 0);
            lrt.anchorMax = new Vector2(1, 0);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(-185, 70);
            lrt.sizeDelta = new Vector2(135, 50);
            home.logout_btn = logout;

            Button quit = CreateButton("QuitBtn", parent, "退出游戏", _font, 22, new Color(1, 1, 1, 0.25f));
            RectTransform qrt = quit.GetComponent<RectTransform>();
            qrt.anchorMin = new Vector2(1, 0);
            qrt.anchorMax = new Vector2(1, 0);
            qrt.pivot = new Vector2(0.5f, 0.5f);
            qrt.anchoredPosition = new Vector2(-55, 70);
            qrt.sizeDelta = new Vector2(135, 50);
            home.quit_btn = quit;
        }

        // ---------------- 隐藏顶部导航栏 ----------------

        /// <summary>
        /// 将 MainMenu 的顶部玩家信息引用改绑到首页（激活的）元素。
        /// 若不重绑，TopBar 被隐藏后其下 Avatar 的 Awake 永不执行，
        /// AvatarUI.avatar_img 为 null，登录回调调用 SetAvatar 会抛 NullReferenceException。
        /// </summary>
        private static void BindMainMenu(HomePanel home)
        {
            MainMenu main = Object.FindObjectOfType<MainMenu>(true);
            if (main == null)
            {
                Debug.LogWarning("主菜单重构：未找到 MainMenu 组件，跳过顶部信息重绑");
                return;
            }

            SerializedObject so = new SerializedObject(main);
            SerializedProperty p;

            p = so.FindProperty("username_txt");
            if (p != null && home.username_txt != null) p.objectReferenceValue = home.username_txt;
            p = so.FindProperty("credits_txt");
            if (p != null && home.credits_txt != null) p.objectReferenceValue = home.credits_txt;
            p = so.FindProperty("avatar");
            if (p != null && home.avatar != null) p.objectReferenceValue = home.avatar;

            so.ApplyModifiedProperties();
        }

        private static void HideTopBar()
        {
            Transform topbar_t = FindTopBar();
            if (topbar_t == null)
            {
                Debug.LogWarning("主菜单重构：未找到 TopBar");
                return;
            }

            // 停用旧导航标签，避免启动时误显示旧页面
            foreach (TabButton tb in topbar_t.GetComponentsInChildren<TabButton>(true))
                tb.active = false;

            topbar_t.gameObject.SetActive(false);
        }

        /// <summary>查找主菜单 TopBar（排除卡牌编辑器内部同名 TopBar；优先返回激活的，找不到激活的退回 inactive）</summary>
        private static Transform FindTopBar()
        {
            Transform fallback = null;
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name != "TopBar") continue;
                if (t.GetComponentInParent<CardEditorPanel>() != null) continue; // 排除卡牌编辑器内的 TopBar
                if (t.gameObject.activeInHierarchy) return t; // 主菜单 TopBar 激活时优先
                if (fallback == null) fallback = t;
            }
            return fallback;
        }

        /// <summary>调整画布缩放模式：高度匹配（参考分辨率 1920×1080，UI 垂直铺满整个屏幕）。
        /// 纯高度匹配保证任何宽高比下 UI 都占满屏幕高度；标准 16:9 窗口下水平也精确铺满。</summary>
        private static void SetupCanvasScaler()
        {
            foreach (CanvasScaler scaler in Object.FindObjectsOfType<CanvasScaler>(true))
            {
                if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    continue;
                scaler.matchWidthOrHeight = 1f;
                Debug.Log("主菜单重构：已调整 CanvasScaler(" + scaler.gameObject.name + ") matchWidthOrHeight=1，UI 占满整个屏幕");
            }
        }

        // ---------------- 各页面返回按钮 ----------------

        private static void AddReturnButtons()
        {
            string[] panel_names = { "PlayPanel", "CollectionPanel", "PackPanel", "Loaderboard", "CardPoolPanel", "SettingsPanel" };
            foreach (string name in panel_names)
            {
                GameObject target = GameObject.Find(name);
                if (target == null)
                {
                    Debug.LogWarning("主菜单重构：未找到页面 " + name + "，跳过返回按钮");
                    continue;
                }
                AddReturnButton(target.transform);
            }
        }

        private static void AddReturnButton(Transform panel_root)
        {
            // 已存在则只更新样式/位置（统一右上角），否则创建
            Transform exist = panel_root.Find("HomeReturnBtn");
            RectTransform rt;
            if (exist != null)
            {
                rt = exist.GetComponent<RectTransform>();
                ApplyExitIcon(exist.gameObject);
            }
            else
            {
                Button btn = CreateButton("HomeReturnBtn", panel_root, "返回", _font, 26, new Color(1, 1, 1, 0.25f));
                rt = btn.GetComponent<RectTransform>();
                btn.gameObject.AddComponent<HomeReturnButton>();
                btn.transform.SetAsLastSibling();
            }

            // 统一放在右上角（exit.png 图标按钮，方形）
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-80, -60);
            rt.sizeDelta = new Vector2(56, 56);
        }

        /// <summary>把已存在的取消/返回按钮替换为 exit.png 图标并去掉文字</summary>
        private static void ApplyExitIcon(GameObject go)
        {
            Image img = go.GetComponent<Image>();
            if (img == null)
                img = go.AddComponent<Image>();
            Sprite exit_sprite = AssetDatabase.LoadAssetAtPath<Sprite>(EXIT_ICON_PATH);
            if (exit_sprite != null)
            {
                img.sprite = exit_sprite;
                img.type = Image.Type.Simple;
                img.color = Color.white;
                img.raycastTarget = true;
            }
            // 去掉旧文字
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = go.transform.GetChild(i);
                if (child.GetComponent<Text>() != null)
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        // ---------------- UI 辅助 ----------------

        private static void DestroyIfExists(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
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
    }
}
