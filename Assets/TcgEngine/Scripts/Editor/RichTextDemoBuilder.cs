using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace TcgEngine.UI
{
    /// <summary>
    /// 富文本编辑器演示场景生成工具：新建独立场景 RichTextDemo（不触碰 Menu.unity）。
    /// 场景里只有「展示框 + 空弹框挂点」：弹框内部控件由 RichTextPopupUI 运行时自建，
    /// 风格与应用内「多选框」弹层一致，无需手动拖拽绑定任何控件引用。
    /// 菜单：TcgEngine → 富文本编辑器 → 生成演示场景 RichTextDemo
    /// </summary>
    public static class RichTextDemoBuilder
    {
        private const string SCENE_DIR = "Assets/TcgEngine/Scenes/Demo";
        private const string SCENE_PATH = SCENE_DIR + "/RichTextDemo.unity";
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei_TMP.asset";

        [MenuItem("TcgEngine/富文本编辑器/生成演示场景 RichTextDemo")]
        public static void BuildDemoScene()
        {
            // 字体：优先项目 SimHei_TMP；缺失时退回 TMP 默认（中文可能空但功能可测）
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            if (font == null)
                font = TMP_Settings.defaultFontAsset;

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cam_go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            Camera cam = cam_go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            cam.tag = "MainCamera";

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            GameObject canvas_go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvas_go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas_go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform canvas_rt = canvas_go.GetComponent<RectTransform>();

            // 顶部提示
            TextMeshProUGUI hint = CreateText(canvas_rt, "Hint", font, MakeRect(canvas_rt, "Hint", new Vector2(0, 330), new Vector2(1100, 60)),
                "点击下方文本框 → 弹出富文本编辑弹框（编辑 / 预览 / 确定回写）", 26,
                new Color(0.75f, 0.82f, 0.9f, 1f), TextAlignmentOptions.Center);
            hint.richText = false;

            // 展示框（点击弹出编辑）
            RectTransform box = MakeRect(canvas_rt, "RichTextBox", Vector2.zero, new Vector2(640, 200));
            Image box_img = box.gameObject.AddComponent<Image>();
            box_img.color = new Color(0.13f, 0.15f, 0.20f, 0.95f);
            box_img.raycastTarget = true;
            box.gameObject.AddComponent<Outline>().effectColor = new Color(0.35f, 0.55f, 0.65f, 0.8f);

            RichTextEditorUI editor_ui = box.gameObject.AddComponent<RichTextEditorUI>();
            editor_ui.text = "造成 <b>2</b> 点伤害，<i>并抽一张牌</i>";

            RectTransform display_rt = MakeRect(box, "Display", Vector2.zero, new Vector2(600, 160));
            TextMeshProUGUI display = display_rt.gameObject.AddComponent<TextMeshProUGUI>();
            UIFonts.SafeSetFont(display, font);
            display.text = editor_ui.text;
            display.fontSize = 28;
            display.color = Color.white;
            display.alignment = TextAlignmentOptions.Center;
            display.enableWordWrapping = true;
            display.richText = true;
            display.raycastTarget = false;
            editor_ui.display = display;

            // 回写回调：演示（把最新源码打到 Console）
            editor_ui.onTextChanged += (s) => Debug.Log("富文本已更新：" + s);

            // 空弹框挂点：内部控件运行时自建（与「多选框」弹层同风格），此处只需占位并绑定引用
            GameObject popup_go = new GameObject("RichTextPopup", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform popup_rt = popup_go.GetComponent<RectTransform>();
            popup_rt.SetParent(canvas_go.transform, false);
            popup_rt.anchorMin = Vector2.zero;
            popup_rt.anchorMax = Vector2.one;
            popup_rt.pivot = new Vector2(0.5f, 0.5f);
            popup_rt.offsetMin = Vector2.zero;
            popup_rt.offsetMax = Vector2.zero;
            popup_go.GetComponent<CanvasGroup>().alpha = 0f;   // 运行时 UIPanel.Awake 会重置并淡入
            editor_ui.popup = popup_go.AddComponent<RichTextPopupUI>();

            if (!Directory.Exists(SCENE_DIR))
                Directory.CreateDirectory(SCENE_DIR);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.Refresh();

            Debug.Log("富文本编辑器：演示场景已生成并保存到 " + SCENE_PATH + "（进 Play 后点击展示框开始编辑）");
        }

        // ---------- UI 构建辅助 ----------

        private static RectTransform MakeRect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, TMP_FontAsset font, RectTransform rt, string content,
            int size, Color color, TextAlignmentOptions align)
        {
            TextMeshProUGUI tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            rt.name = name;
            UIFonts.SafeSetFont(tmp, font);
            tmp.text = content;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = true;
            return tmp;
        }
    }
}
