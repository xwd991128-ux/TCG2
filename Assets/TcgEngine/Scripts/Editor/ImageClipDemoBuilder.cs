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
    /// 卡图裁切演示场景生成工具：新建独立场景 ImageClipDemo（不触碰 Menu.unity）。
    /// 场景里只有「卡图展示框 + 空弹框挂点」：弹框内部控件由 ImageClipPopupUI 运行时自建，
    /// 按钮监听也全部在运行时挂，因此不受「edit 期 AddListener 不序列化」影响。
    /// 菜单：TcgEngine → 卡图裁切 → 生成演示场景 ImageClipDemo
    /// </summary>
    public static class ImageClipDemoBuilder
    {
        private const string SCENE_DIR = "Assets/TcgEngine/Scenes/Demo";
        private const string SCENE_PATH = SCENE_DIR + "/ImageClipDemo.unity";
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei_TMP.asset";

        [MenuItem("TcgEngine/卡图裁切/生成演示场景 ImageClipDemo")]
        public static void BuildDemoScene()
        {
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

            CreateText(canvas_rt, "Hint", font, MakeRect(canvas_rt, "Hint", new Vector2(0, 330), new Vector2(1200, 60)),
                "点击下方卡图 → 弹出裁切弹框（从文件导入 / 从图库选择 → 拖动缩放 → 确定回写）", 24,
                new Color(0.75f, 0.82f, 0.9f, 1f), TextAlignmentOptions.Center);

            // 卡图展示框（点击弹出裁切）：正方形，与战场卡牌底板比例一致
            RectTransform art = MakeRect(canvas_rt, "CardArt", new Vector2(0, 0), new Vector2(360, 360));
            Image art_img = art.gameObject.AddComponent<Image>();
            art_img.color = new Color(0.13f, 0.15f, 0.20f, 1f);   //占位底色（无图时可见、可点）
            art_img.raycastTarget = true;
            art.gameObject.AddComponent<Outline>().effectColor = new Color(0.35f, 0.55f, 0.65f, 0.8f);

            ImageClipEditorUI editor = art.gameObject.AddComponent<ImageClipEditorUI>();
            editor.targetSize = new Vector2(8.56f, 8.36f);   //与战场卡牌底板显示区一致

            // 空弹框挂点：内部控件运行时自建，这里只需占位并绑定引用
            GameObject popup_go = new GameObject("ImageClipPopup", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform popup_rt = popup_go.GetComponent<RectTransform>();
            popup_rt.SetParent(canvas_go.transform, false);
            popup_rt.anchorMin = Vector2.zero;
            popup_rt.anchorMax = Vector2.one;
            popup_rt.pivot = new Vector2(0.5f, 0.5f);
            popup_rt.offsetMin = Vector2.zero;
            popup_rt.offsetMax = Vector2.zero;
            popup_go.GetComponent<CanvasGroup>().alpha = 0f;   //运行时 UIPanel.Awake 会重置并淡入
            editor.popup = popup_go.AddComponent<ImageClipPopupUI>();

            if (!Directory.Exists(SCENE_DIR))
                Directory.CreateDirectory(SCENE_DIR);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.Refresh();

            Debug.Log("卡图裁切：演示场景已生成并保存到 " + SCENE_PATH + "（进 Play 后点击卡图开始裁切）");
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
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
