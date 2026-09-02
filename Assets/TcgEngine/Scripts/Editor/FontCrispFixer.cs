using UnityEditor;
using UnityEngine;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 字体清晰化工具：把项目中的中文字体（SimHei 黑体）导入的渲染模式改为 HintedSmooth（抗锯齿 + 像素对齐提示），
    /// 解决 uGUI 中文小字号文字发糊问题（默认 Smooth 模式在 Windows 下偏软）。
    /// 用法：菜单 TcgEngine → 工具 → 字体清晰化（SimHei 渲染模式）
    /// 运行一次后 Unity 会重新导入字体；字体 GUID 不变，场景无需重建即可生效。
    /// </summary>
    public static class FontCrispFixer
    {
        private const string FONT_PATH = "Assets/TcgEngine/Fonts/SimHei.ttf";
        private const string MENU = "TcgEngine/工具/";

        [MenuItem(MENU + "字体清晰化（SimHei 渲染模式）")]
        public static void ApplyCrispFont()
        {
            TrueTypeFontImporter importer = AssetImporter.GetAtPath(FONT_PATH) as TrueTypeFontImporter;
            if (importer == null)
            {
                Debug.LogWarning("[FontCrispFixer] 未找到字体: " + FONT_PATH);
                return;
            }

            importer.fontRenderingMode = FontRenderingMode.HintedSmooth;   //抗锯齿 + 像素对齐提示，小字号最清晰
            importer.SaveAndReimport();

            Debug.Log("[FontCrispFixer] SimHei 渲染模式已设为 HintedSmooth（抗锯齿+像素对齐），字体已重新导入。");
        }
    }
}
