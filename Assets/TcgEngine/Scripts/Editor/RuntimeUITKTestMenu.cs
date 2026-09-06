using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using TcgEngine.Workshop;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 运行时 UIToolkit 节点画布验证入口：
    /// 先在编辑模式点"生成 UITK 运行时面板资源"（生成带主题的 PanelSettings）；
    /// 再进入 Play 点"Runtime UITK 节点测试(需Play)"。
    /// </summary>
    public static class RuntimeUITKTestMenu
    {
        private const string Folder = "Assets/TcgEngine/Resources/UITK";
        private const string PanelPath = Folder + "/PrototypePanel.asset";
        private const string ThemePath = Folder + "/CustomRuntimeTheme.tss";
        private const string PanelLoadPath = "UITK/PrototypePanel";

        [MenuItem("TcgEngine/工具/生成 UITK 运行时面板资源")]
        public static void EnsureRuntimeAssets()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/TcgEngine/Resources", "UITK");

            //自造一份最小 ThemeStyleSheet（.tss 由 Unity 默认导入为 ThemeStyleSheet）
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "* { color: white; }\n");
                AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceUpdate);
            }
            ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

            PanelSettings ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelPath);
            }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match = 0.5f;
            ps.sortingOrder = 1000;   //盖在 uGUI Canvas(默认0) 之上
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            Debug.Log("[UITK] 已生成 " + PanelPath + "（含主题）");
        }

        [MenuItem("TcgEngine/工具/Runtime UITK 节点测试(需Play)")]
        public static void Spawn()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("请先进入 Play 模式，再点击本菜单生成运行时 UIToolkit 测试面板。");
                return;
            }
            if (Resources.Load<PanelSettings>(PanelLoadPath) == null)
            {
                Debug.LogWarning("缺少 Resources/UITK/PrototypePanel.asset，请先在编辑模式执行 TcgEngine→工具→生成 UITK 运行时面板资源");
                return;
            }
            //清理旧的，避免叠屏
            foreach (RuntimeUITKNodeTest old in Object.FindObjectsOfType<RuntimeUITKNodeTest>())
                Object.Destroy(old.gameObject);

            GameObject go = new GameObject("RuntimeUITKNodeTest");
            go.AddComponent<RuntimeUITKNodeTest>();
            Debug.Log("已生成运行时 UIToolkit 节点画布（Game 视图应全屏覆盖）。Close 按钮删除。");
        }
    }
}
