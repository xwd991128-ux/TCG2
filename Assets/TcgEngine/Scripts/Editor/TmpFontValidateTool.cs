using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 最小验证工具：用项目里 SimHei 动态生成一张 TMP 字体资产，
    /// 并在当前打开场景的主 Canvas 上叠加一行 TMP 中文，与老式 Text 并排对比清晰度。
    /// 仅用于验证 TMP 是否是字体模糊的解药，不改动任何现有 UI 结构。
    /// 菜单：TcgEngine → 工具 → TMP字体验证（叠加对比文字）
    /// 结果对象名 "TMP_Validate_Overlay"，Delete 即可移除。
    /// </summary>
    public static class TmpFontValidateTool
    {
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei.ttf";
        private const string FONT_FALLBACK_PATH = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";
        private const string MENU = "TcgEngine/工具/";

        [MenuItem(MENU + "TMP字体验证（叠加对比文字）")]
        public static void Validate()
        {
            Font src = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (src == null) src = AssetDatabase.LoadAssetAtPath<Font>(FONT_FALLBACK_PATH);
            if (src == null)
            {
                Debug.LogWarning("[TMPValidate] 未找到 SimHei.ttf / OpenSans-Bold.ttf，无法生成 TMP 字体。");
                return;
            }

            // 动态 TMP 字体：运行时按需光栅中文，避免全量烘焙慢
            TMP_FontAsset tfa = TMP_FontAsset.CreateFontAsset(src);
            if (tfa == null)
            {
                Debug.LogWarning("[TMPValidate] TMP_FontAsset.CreateFontAsset 失败。");
                return;
            }
            tfa.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[TMPValidate] 当前场景没有 Canvas，请打开主菜单/节点编辑器场景再跑。");
                return;
            }

            // 叠加一行 TMP 文字，放在老式标题（预计左上角）下方，便于直接对比
            GameObject go = new GameObject("TMP_Validate_Overlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvas.transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(30, -150);
            rt.sizeDelta = new Vector2(1000, 80);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = tfa;
            tmp.fontSize = 34;
            tmp.color = Color.white;
            tmp.text = "TMP 验证行：规则编辑 撤销 重做 保存 名称 类型 法术 费用";
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.raycastTarget = false;

            Debug.Log("[TMPValidate] 已叠加 TMP 中文验证行（对象 TMP_Validate_Overlay）。" +
                      "请与旁边老式 Text 对比清晰度；确认后可 Delete 该对象。");
            EditorGUIUtility.PingObject(go);
        }
    }
}
