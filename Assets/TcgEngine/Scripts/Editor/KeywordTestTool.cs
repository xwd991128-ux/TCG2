using UnityEditor;
using UnityEngine;
using TcgEngine;
using TcgEngine.Workshop;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 自定义关键词系统验证工具：
    /// 生成一个测试关键词（打出时对目标造成 1 点伤害），挂到任意卡的 keywords 上即可对局验证。
    /// </summary>
    public static class KeywordTestTool
    {
        [MenuItem("TcgEngine/关键词验证/生成测试关键词（打出时对目标造成1点伤害）")]
        public static void CreateTestKeyword()
        {
            const string folder = "Assets/TcgEngine/Resources/Keywords";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/TcgEngine/Resources", "Keywords");

            //最简图：OnPlay 事件 → 202001 造成伤害(1)，目标口不连线=沿用打出时选中的目标
            GraphData graph = GraphBuilder.BuildSimpleGraph("OnPlay", "202001", 1);

            KeywordData keyword = AssetDatabase.LoadAssetAtPath<KeywordData>(folder + "/keyword_test.asset");
            if (keyword == null)
            {
                keyword = ScriptableObject.CreateInstance<KeywordData>();
                AssetDatabase.CreateAsset(keyword, folder + "/keyword_test.asset");
            }
            keyword.id = "keyword_test";
            keyword.title = "测试关键词";
            keyword.desc = "验证用：打出时对目标造成 1 点伤害";
            keyword.status_type = StatusType.None;
            keyword.rules.Clear();
            if (graph != null)
                keyword.rules.Add(new KeywordRule { trigger_action = "OnPlay", graph = graph });

            EditorUtility.SetDirty(keyword);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = keyword;
            Debug.Log("[关键词验证] 已生成 Assets/TcgEngine/Resources/Keywords/keyword_test.asset，把它加到某张卡的 Keywords 列表后进对局：打出该卡→目标掉1点血=关键词链路生效", keyword);
        }
    }
}
