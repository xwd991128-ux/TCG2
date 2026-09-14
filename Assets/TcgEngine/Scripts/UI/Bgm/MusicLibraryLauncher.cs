using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 主菜单 BGM 入口：右上角注入「音乐库」（导入/试听/重命名/删除）与「界面BGM配置」（逐界面策略/曲目/音量）。
    /// 运行时注入，不依赖重跑场景生成工具；也可在场景里手工放按钮绑 <see cref="MainMenu.OnClickMusicLibrary"/>（等价）。
    /// </summary>
    public static class MusicLibraryLauncher
    {
        public const string BUTTON_NAME = "MusicLibraryBtn";
        public const string CONFIG_BUTTON_NAME = "BgmConfigBtn";

        /// <summary>确保主菜单存在两个入口按钮（幂等）</summary>
        public static void EnsureEntry()
        {
            bool has_lib = GameObject.Find(BUTTON_NAME) != null;
            bool has_cfg = GameObject.Find(CONFIG_BUTTON_NAME) != null;
            if (has_lib && has_cfg)
                return;

            Transform host = FindHost();
            if (host == null)
            {
                Debug.LogWarning("[BGM] 未找到主菜单首页（HomePanel），跳过 BGM 入口创建；" +
                    "可在场景里手工加按钮并绑定 MainMenu.OnClickMusicLibrary()");
                return;
            }

            if (!has_lib)
                MakeButton(host, BUTTON_NAME, "音乐库", -24f, 132f, UITheme.CtrlStrong, () => Open(host));
            if (!has_cfg)
                MakeButton(host, CONFIG_BUTTON_NAME, "界面BGM配置", -166f, 142f,
                    new Color(1f, 0.85f, 0.45f, 0.34f), () => BgmConfigPanel.Open(host));
        }

        /// <summary>打开音乐库面板</summary>
        public static void Open(Transform host)
        {
            MusicLibraryPanel.Open(host != null ? host : FindHost());
        }

        /// <summary>打开界面 BGM 配置面板</summary>
        public static void OpenConfig(Transform host)
        {
            BgmConfigPanel.Open(host != null ? host : FindHost());
        }

        private static void MakeButton(Transform host, string name, string label, float right_offset,
            float width, Color color, UnityEngine.Events.UnityAction action)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(host, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(width, 38f);
            rt.anchoredPosition = new Vector2(right_offset, -18f);

            Image img = go.AddComponent<Image>();
            img.color = color;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);
            if (action != null)
                btn.onClick.AddListener(action);

            GameObject txt_go = new GameObject("Text", typeof(RectTransform));
            txt_go.transform.SetParent(go.transform, false);
            RectTransform trt = txt_go.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4f, 2f);
            trt.offsetMax = new Vector2(-4f, -2f);
            TextMeshProUGUI txt = txt_go.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyFont(txt);
            txt.text = label;
            txt.fontSize = 16;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
            txt.enableWordWrapping = false;
            txt.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static Transform FindHost()
        {
            HomePanel home = Object.FindObjectOfType<HomePanel>(true);
            if (home != null)
                return home.transform;
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            return canvas != null ? canvas.transform : null;
        }
    }
}
